using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using MatDesktop;
using ThixFred.Core.Business;

namespace ThixFred.Controls;

/// <summary>
/// A shortcut shown as one chip ("Strg + Win + V", "⌃⌥V"): header, hints, settings. <see cref="Keys"/> is either a stored
/// shortcut ("Ctrl+Win+V", shown the way the system names it) or a free text ("↵").
/// </summary>
public sealed class KeyCaps : Border
{
    public static readonly StyledProperty<string> KeysProperty = AvaloniaProperty.Register<KeyCaps, string>(nameof(Keys), "");

    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center };

    static KeyCaps()
    {
        KeysProperty.Changed.AddClassHandler<KeyCaps>((caps, _) => caps.Refresh());
    }

    public KeyCaps()
    {
        this.Themed("MatKeyCap");
        Child = _text;
    }

    public string Keys
    {
        get => GetValue(KeysProperty);
        set => SetValue(KeysProperty, value);
    }

    private void Refresh() => _text.Text = HotkeyService.Parse(Keys) is { } hotkey ? HotkeyService.Display(hotkey) : Keys;
}
