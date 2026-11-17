using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixFred.Core.Business;

namespace ThixFred.Controls;

/// <summary>
/// Records a new global shortcut: click (or Enter/Space) starts recording, the next key with modifiers is the new
/// shortcut, Esc cancels. While it records, the current global shortcut is paused so it does not open ThixFred.
/// </summary>
public sealed class HotkeyRecorder : Button
{
    public static readonly StyledProperty<string> HotkeyProperty = AvaloniaProperty.Register<HotkeyRecorder, string>(nameof(Hotkey), "");

    private readonly KeyCaps _caps = new();
    private readonly TextBlock _prompt = new() { Text = "Tastenkombination drücken … (Esc: abbrechen)", VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
    private bool _isRecording;

    static HotkeyRecorder()
    {
        HotkeyProperty.Changed.AddClassHandler<HotkeyRecorder>((recorder, _) => recorder.Refresh());
    }

    public HotkeyRecorder()
    {
        Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _caps, _prompt } };
        MinWidth = 180;
        AutomationProperties.SetName(this, "Tastenkürzel aufnehmen");
        ToolTip.SetTip(this, "Klicken und die neue Tastenkombination drücken");
        Refresh();
    }

    /// <summary>Raised with the new shortcut in the stored form ("Ctrl+Shift+V").</summary>
    public event EventHandler<string>? Recorded;

    public string Hotkey
    {
        get => GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(Button);

    protected override void OnClick()
    {
        base.OnClick();
        if (!_isRecording)
        {
            StartRecording();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!_isRecording)
        {
            base.OnKeyDown(e);
            return;
        }

        e.Handled = true;
        if (e.Key == Key.Escape)
        {
            StopRecording();
            return;
        }

        var key = KeyName(e.Key);
        if (key is null)
        {
            return; // a modifier alone: wait for the key
        }

        var parts = new List<string>();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            parts.Add(OperatingSystem.IsMacOS() ? "Cmd" : "Win");
        }

        parts.Add(key);
        StopRecording();
        Recorded?.Invoke(this, string.Join('+', parts));
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        if (_isRecording)
        {
            StopRecording();
        }
    }

    private void StartRecording()
    {
        _isRecording = true;
        HotkeyService.Suspend(true);
        _caps.IsVisible = false;
        _prompt.IsVisible = true;
        this.Use(BorderBrushProperty, "Mat.Accent");
        Focus();
    }

    private void StopRecording()
    {
        _isRecording = false;
        HotkeyService.Suspend(false);
        _caps.IsVisible = true;
        _prompt.IsVisible = false;
        ClearValue(BorderBrushProperty);
    }

    private void Refresh() => _caps.Keys = Hotkey;

    /// <summary>The supported keys by name (letters, digits, F1-F12, Space, Insert); null for anything else.</summary>
    internal static string? KeyName(Key key) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => ((int)(key - Key.NumPad0)).ToString(),
        >= Key.F1 and <= Key.F12 => key.ToString(),
        Key.Space => "Space",
        Key.Insert => "Insert",
        _ => null,
    };
}
