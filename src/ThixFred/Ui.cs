using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using MatDesktop;
using ThixFred.Core.Infrastructure;

namespace ThixFred;

/// <summary>
/// Small helpers for code-built UI: the app icon and logo, the UI thread, opening addresses, texts of times and sizes.
/// Colours, sizes and named looks are MatDesktop's (<c>control.Use(property, "Mat.TextMuted")</c>,
/// <c>control.Themed("MatButton.Primary")</c>), icons are <c>MatIcon</c> with the name of the sprite.
/// </summary>
internal static class Ui
{
    private static WindowIcon? _appIcon;
    private static Bitmap? _logo;

    /// <summary>The icon of every window of ThixFred (the task bar, Alt+Tab): MatThemeOptions.Icon gives it to every MatWindow.</summary>
    public static WindowIcon AppIcon => _appIcon ??= new WindowIcon(AssetLoader.Open(new Uri("avares://ThixFred/Assets/thixfred.ico")));

    /// <summary>The logo (the mark of the app), for the title bar and the header of the setup.</summary>
    public static Bitmap Logo => _logo ??= new Bitmap(AssetLoader.Open(new Uri("avares://ThixFred/Assets/logo-256.png")));

    /// <summary>
    /// A named look of MatDesktop (a control theme such as "MatButton.Primary" or "MatChoiceRow") for
    /// an object initializer; anywhere else <c>control.Themed(key)</c>. A control theme is the same in light and dark.
    /// </summary>
    public static ControlTheme Theme(string key) =>
        Application.Current!.TryGetResource(key, null, out var value) && value is ControlTheme theme
            ? theme
            : throw new InvalidOperationException($"The control theme {key} is missing (MatDesktop/Themes/Controls.axaml).");

    /// <summary>Runs the action on the UI thread (events of the business layer come from any thread).</summary>
    public static void Post(Action action) => Dispatcher.UIThread.Post(action);

    /// <summary>The window that hosts a control (null while it is not shown).</summary>
    public static Window? WindowOf(Control control) => TopLevel.GetTopLevel(control) as Window;

    /// <summary>Ctrl on Windows, Cmd on macOS: the modifier of the app's own shortcuts.</summary>
    public static KeyModifiersName CommandKey => OperatingSystem.IsMacOS() ? new("⌘", Avalonia.Input.KeyModifiers.Meta) : new("Strg", Avalonia.Input.KeyModifiers.Control);


    /// <summary>Tests see what would be opened instead of starting the browser.</summary>
    internal static Action<string>? OpenExternalForTests { get; set; }

    /// <summary>Opens a web address or a folder with the program the system uses for it (browser, Explorer, Finder).</summary>
    public static void OpenExternal(string pathOrUrl)
    {
        if (OpenExternalForTests is { } open)
        {
            open(pathOrUrl);
            return;
        }

        try
        {
            var startInfo = OperatingSystem.IsMacOS()
                ? new System.Diagnostics.ProcessStartInfo("open") { ArgumentList = { pathOrUrl }, UseShellExecute = false }
                : OperatingSystem.IsLinux()
                    ? new System.Diagnostics.ProcessStartInfo("xdg-open") { ArgumentList = { pathOrUrl }, UseShellExecute = false }
                    : new System.Diagnostics.ProcessStartInfo(pathOrUrl) { UseShellExecute = true };
            System.Diagnostics.Process.Start(startInfo)?.Dispose();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warning($"Could not open {pathOrUrl}: {exception.Message}");
        }
    }

    /// <summary>"vor 3 Min." / "14:28" / "Gestern, 17:56" / "12.11., 09:14" / "12.11.2025".</summary>
    public static string FormatTime(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        var today = AppClock.Today;
        if (local.Date == today)
        {
            return local.ToString("HH:mm");
        }

        if (local.Date == today.AddDays(-1))
        {
            return $"Gestern, {local:HH:mm}";
        }

        return local.Year == today.Year ? local.ToString("dd.MM., HH:mm") : local.ToString("dd.MM.yyyy");
    }

    /// <summary>"Heute, 13:47" or "12.11.2026, 13:47".</summary>
    public static string FormatDateTime(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        if (local.Date == AppClock.Today)
        {
            return $"Heute, {local:HH:mm}";
        }

        return local.Date == AppClock.Today.AddDays(-1) ? $"Gestern, {local:HH:mm}" : local.ToString("dd.MM.yyyy, HH:mm");
    }

    public static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.0} MB",
    };

    // ---- building forms and pages in code (one look everywhere) ----

    /// <summary>A text in a named look of MatDesktop ("MatText.Muted", "MatText.PageTitle" …).</summary>
    public static TextBlock Text(string text, string? theme = null, bool wrap = true)
    {
        var block = new TextBlock { Text = text, TextWrapping = wrap ? Avalonia.Media.TextWrapping.Wrap : Avalonia.Media.TextWrapping.NoWrap };
        return theme is null ? block : block.Themed(theme);
    }

    /// <summary>A button with a symbol of the sprite and a text, optionally in a named look ("MatButton.Primary", ".Danger").</summary>
    public static Button Button(string icon, string text, string? theme = null, Action? click = null)
    {
        var button = new Button { Content = new Controls.IconLabel { Icon = icon, Text = text } };
        if (theme is not null)
        {
            button.Themed(theme);
        }

        if (click is not null)
        {
            button.Click += (_, _) => click();
        }

        return button;
    }

    /// <summary>A labelled field; <paramref name="field"/> names the <see cref="ThixFred.Core.Models.BusinessException.Field"/> shown at it.</summary>
    public static MatFormField Field(string label, Control input, string? field = null, string? hint = null)
    {
        var formField = new MatFormField { Label = label, Hint = hint, Content = input };
        if (field is not null)
        {
            Controls.FormErrors.SetField(formField, field);
        }

        return formField;
    }

    /// <summary>A setting row (title, description, the control at the right).</summary>
    public static MatSettingRow Row(string title, string? description, Control control, bool last = false)
    {
        var row = new MatSettingRow { Title = title, Description = description, Content = control };
        if (last)
        {
            row.BorderThickness = new Thickness(0);
        }

        return row;
    }

    /// <summary>A switch without the words of Fluent (the row says what it is).</summary>
    public static ToggleSwitch Switch(string name, bool isChecked, Action<bool>? changed = null)
    {
        var toggle = new ToggleSwitch { OnContent = "", OffContent = "", IsChecked = isChecked };
        Avalonia.Automation.AutomationProperties.SetName(toggle, name);
        if (changed is not null)
        {
            toggle.IsCheckedChanged += (_, _) => changed(toggle.IsChecked == true);
        }

        return toggle;
    }

    /// <summary>The page frame of the settings window: the surface, scrolling, the margins and the width of a page.</summary>
    public static Control Page(Control content, double maxWidth = 920) => new Border
    {
        Child = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new Border { Margin = new Thickness(28, 22, 28, 28), MaxWidth = maxWidth, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Child = content },
        },
    }.Use(Border.BackgroundProperty, "Mat.Surface");
}

/// <summary>The name and the modifier of the command key of the platform.</summary>
internal sealed record KeyModifiersName(string Name, Avalonia.Input.KeyModifiers Modifier);
