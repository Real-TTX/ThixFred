using ThixFred.Core.Infrastructure;
using ThixFred.Core.Models;
using ThixFred.Core.Platform.Mac;
using ThixFred.Core.Platform.Windows;

namespace ThixFred.Core.Business;

/// <summary>
/// The global shortcut that opens the launcher: parsing, display, validation and the registration with the system.
/// Default: Alt+Space on Windows, Option+Space (stored "Alt+Space") on macOS - the shortcut of most launchers. ThixFred's shortcut takes precedence:
/// on Windows a keyboard hook swallows it before anyone else sees it, on macOS the Carbon hot key belongs to ThixFred.
/// </summary>
public static class HotkeyService
{
    public const string DefaultHotkey = "Alt+Space";

    private static readonly string[] FunctionKeys = Enumerable.Range(1, 12).Select(number => $"F{number}").ToArray();

    /// <summary>The shortcut that is registered now (null = none).</summary>
    public static Hotkey? Active { get; private set; }

    /// <summary>Parses "Ctrl+Win+V" (also "Cmd", "Option", "Strg", "Control"); null when it is not a valid shortcut.</summary>
    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        bool control = false, alt = false, shift = false, meta = false;
        string? key = null;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL" or "STRG" or "CONTROL" or "⌃":
                    control = true;
                    break;
                case "ALT" or "OPTION" or "OPT" or "⌥":
                    alt = true;
                    break;
                case "SHIFT" or "UMSCHALT" or "⇧":
                    shift = true;
                    break;
                case "WIN" or "CMD" or "COMMAND" or "META" or "SUPER" or "⌘":
                    meta = true;
                    break;
                default:
                    if (key is not null)
                    {
                        return null;
                    }

                    key = NormalizeKey(part);
                    if (key is null)
                    {
                        return null;
                    }

                    break;
            }
        }

        return key is null ? null : new Hotkey(control, alt, shift, meta, key);
    }

    /// <summary>The stored form: "Ctrl+Alt+Shift+Win+V" (Win is "Cmd" on macOS).</summary>
    public static string Format(Hotkey hotkey)
    {
        var parts = new List<string>();
        if (hotkey.Control)
        {
            parts.Add("Ctrl");
        }

        if (hotkey.Alt)
        {
            parts.Add("Alt");
        }

        if (hotkey.Shift)
        {
            parts.Add("Shift");
        }

        if (hotkey.Meta)
        {
            parts.Add(OperatingSystem.IsMacOS() ? "Cmd" : "Win");
        }

        parts.Add(hotkey.Key);
        return string.Join('+', parts);
    }

    /// <summary>For the user: "Strg + Umschalt + V" on Windows, "⌃⌥V" on macOS.</summary>
    public static string Display(Hotkey hotkey)
    {
        if (OperatingSystem.IsMacOS())
        {
            return (hotkey.Control ? "⌃" : "") + (hotkey.Alt ? "⌥" : "") + (hotkey.Shift ? "⇧" : "") + (hotkey.Meta ? "⌘" : "") +
                   (hotkey.Key == "Space" ? "Leertaste" : hotkey.Key);
        }

        var parts = new List<string>();
        if (hotkey.Control)
        {
            parts.Add("Strg");
        }

        if (hotkey.Alt)
        {
            parts.Add("Alt");
        }

        if (hotkey.Shift)
        {
            parts.Add("Umschalt");
        }

        if (hotkey.Meta)
        {
            parts.Add("Win");
        }

        parts.Add(hotkey.Key == "Space" ? "Leertaste" : hotkey.Key == "Insert" ? "Einfg" : hotkey.Key);
        return string.Join(" + ", parts);
    }

    public static string Display(string text) => Parse(text) is { } hotkey ? Display(hotkey) : text;

    /// <summary>
    /// A global shortcut must not take what every program needs: F-keys may stand alone, one modifier with the space bar
    /// (Alt+Space, Ctrl+Space, ⌘Space: the shortcuts of launchers) and Win + key on Windows are allowed, everything else needs
    /// two modifiers (Ctrl+V, Cmd+C, Alt+F ... belong to the apps).
    /// </summary>
    public static void Validate(Hotkey hotkey)
    {
        var isFunctionKey = FunctionKeys.Contains(hotkey.Key);
        var modifiers = (hotkey.Control ? 1 : 0) + (hotkey.Alt ? 1 : 0) + (hotkey.Shift ? 1 : 0) + (hotkey.Meta ? 1 : 0);
        var winAlone = OperatingSystem.IsWindows() && hotkey.Meta && modifiers == 1;
        var withSpace = hotkey.Key == "Space" && modifiers >= 1 && !(hotkey.Shift && modifiers == 1);
        if (!isFunctionKey && !winAlone && !withSpace && (modifiers < 2 || (!hotkey.Control && !hotkey.Alt && !hotkey.Meta)))
        {
            throw new BusinessException(OperatingSystem.IsMacOS()
                ? "Das Tastenkürzel braucht zwei Sondertasten (z. B. ⌃⌥F) oder eine mit der Leertaste (⌥ Leertaste) – ⌘V und Co. gehören den Apps."
                : "Das Tastenkürzel braucht Win, zwei Sondertasten (z. B. Strg + Umschalt + F) oder eine mit der Leertaste (Alt + Leertaste) – Strg + V und Co. gehören den Programmen.",
                nameof(AppSettings.Hotkey));
        }

        if (OperatingSystem.IsWindows() && hotkey is { Control: true, Alt: true, Key: "Delete" })
        {
            throw new BusinessException("Strg+Alt+Entf gehört Windows.", nameof(AppSettings.Hotkey));
        }
    }

    /// <summary>Registers the shortcut of the settings (macOS: call it on the main thread).</summary>
    public static bool Apply(string text, Action pressed)
    {
        var hotkey = Parse(text) ?? Parse(DefaultHotkey)!;
        Unregister();
        var registered = Register(hotkey, pressed);
        Active = registered ? hotkey : null;
        Log.Info(registered ? $"Shortcut {Format(hotkey)} registered" : $"Shortcut {Format(hotkey)} could not be registered");
        return registered;
    }

    public static void Unregister()
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsKeyboardHook.Unregister();
        }
        else if (OperatingSystem.IsMacOS())
        {
            MacHotkey.Unregister();
        }

        Active = null;
    }

    /// <summary>While a new shortcut is recorded the current one must not fire.</summary>
    public static void Suspend(bool suspended)
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsKeyboardHook.Suspend(suspended);
        }
        else if (OperatingSystem.IsMacOS())
        {
            MacHotkey.Suspend(suspended);
        }
    }

    /// <summary>Windows virtual-key code of a key name.</summary>
    internal static int WindowsKeyCode(string key) => key switch
    {
        "Space" => 0x20,
        "Insert" => 0x2D,
        _ when key.Length == 1 && char.IsAsciiLetterUpper(key[0]) => key[0],
        _ when key.Length == 1 && char.IsAsciiDigit(key[0]) => key[0],
        _ when key.StartsWith('F') && int.TryParse(key[1..], out var number) && number is >= 1 and <= 12 => 0x70 + number - 1,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown key"),
    };

    /// <summary>macOS virtual key code (kVK_*) of a key name: the ANSI layout positions.</summary>
    internal static uint MacKeyCode(string key) => key switch
    {
        "A" => 0x00, "S" => 0x01, "D" => 0x02, "F" => 0x03, "H" => 0x04, "G" => 0x05, "Z" => 0x06, "X" => 0x07, "C" => 0x08,
        "V" => 0x09, "B" => 0x0B, "Q" => 0x0C, "W" => 0x0D, "E" => 0x0E, "R" => 0x0F, "Y" => 0x10, "T" => 0x11,
        "1" => 0x12, "2" => 0x13, "3" => 0x14, "4" => 0x15, "6" => 0x16, "5" => 0x17, "9" => 0x19, "7" => 0x1A, "8" => 0x1C,
        "0" => 0x1D, "O" => 0x1F, "U" => 0x20, "I" => 0x22, "P" => 0x23, "L" => 0x25, "J" => 0x26, "K" => 0x28, "N" => 0x2D,
        "M" => 0x2E, "Space" => 0x31,
        "F1" => 0x7A, "F2" => 0x78, "F3" => 0x63, "F4" => 0x76, "F5" => 0x60, "F6" => 0x61, "F7" => 0x62, "F8" => 0x64,
        "F9" => 0x65, "F10" => 0x6D, "F11" => 0x67, "F12" => 0x6F,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown key"),
    };

    private static bool Register(Hotkey hotkey, Action pressed)
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsKeyboardHook.Register(WindowsKeyCode(hotkey.Key), hotkey.Control, hotkey.Alt, hotkey.Shift, hotkey.Meta, pressed);
            return true;
        }

        if (OperatingSystem.IsMacOS())
        {
            var modifiers = (hotkey.Control ? MacHotkey.ControlModifier : 0) | (hotkey.Alt ? MacHotkey.OptionModifier : 0) |
                            (hotkey.Shift ? MacHotkey.ShiftModifier : 0) | (hotkey.Meta ? MacHotkey.CommandModifier : 0);
            return MacHotkey.Register(MacKeyCode(hotkey.Key), modifiers, pressed);
        }

        return false;
    }

    private static string? NormalizeKey(string part)
    {
        var upper = part.ToUpperInvariant();
        if (upper.Length == 1 && (char.IsAsciiLetterUpper(upper[0]) || char.IsAsciiDigit(upper[0])))
        {
            return upper;
        }

        if (FunctionKeys.Contains(upper))
        {
            return upper;
        }

        return upper switch
        {
            "SPACE" or "LEERTASTE" => "Space",
            "INSERT" or "EINFG" => OperatingSystem.IsMacOS() ? null : "Insert",
            _ => null,
        };
    }
}
