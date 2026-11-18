using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ThixFred.Core.Business;
using ThixFred.Core.Models;
using ThixFred.Core.Platform;

namespace ThixFred.Launcher;

/// <summary>
/// The launcher: a window without frame, above everything, centred in the upper third of the screen the user works on. It is
/// built once at start and only shown and hidden by the shortcut, so it is there at once; the search runs in memory on every
/// key (<see cref="SearchService"/>). Everything works with the keyboard:
///   ↑ ↓ Bild↑ Bild↓ choose · Enter run · Umschalt+Enter the second action (as administrator, in the folder, at once) ·
///   Strg/⌘+1 … 9 run the n-th · Tab complete · Esc back / clear / hide · Strg/⌘+, settings · Strg/⌘+C copy a message.
/// A filter plugin (its keyword typed) is asked in the background, 120 ms after the last key; its results replace the list.
///
/// The integrated terminal (<see cref="IntegratedTerminal"/>): "&gt;command" + Enter shows its output instead of the results;
/// the search line then takes the next command (↑ ↓ the last ones), while one runs it is the input of that command;
/// Strg+C ends it, Strg+L clears, Esc goes back to the search (a running command goes on, the launcher shows it again).
///
/// The kind of window (system glass, own shadow, flat) is fixed when it is built: Windows does not change a window from glass
/// to plain cleanly (the glass stays as a frame around a smaller window). A theme of another kind asks the app for a new
/// window (<see cref="RebuildRequested"/>).
/// </summary>
public sealed class LauncherWindow : Window
{
    private const int ShadowMargin = 18;

    private readonly LauncherView _view = new();
    private readonly Border _frame;
    private readonly WindowKind _kind;
    private readonly EventHandler _themeChanged;
    private readonly EventHandler<AppSettings> _settingsChanged;
    private readonly EventHandler _terminalChanged;
    private CancellationTokenSource? _filter;
    private (SearchResult Result, LaunchAction Action)? _pendingConfirmation;
    private bool _isExecuting;
    private bool _systemIsDark;
    private bool _terminalMode;
    private bool _terminalRefreshPending;
    private bool _rebuildRequested;
    private int _historyIndex = -1;

    public LauncherWindow()
    {
        Title = "ThixFred";
        ShowInTaskbar = false;
        Topmost = true;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Transparent;
        _frame = new Border { Child = _view };
        Content = _frame;

        _view.SearchBox.TextChanged += (_, _) => Search();
        _view.RowClicked += async (_, index) =>
        {
            _view.Select(index);
            await RunSelectedAsync(alternate: false);
        };
        _view.RowHovered += (_, index) => _view.Select(index);
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Deactivated += (_, _) => HideWhenAnotherAppComesFirst();

        _themeChanged = (_, _) => Ui.Post(ApplyTheme);
        _settingsChanged = (_, settings) => Ui.Post(() =>
        {
            _view.KeyHint = HotkeyService.Display(settings.Hotkey);
            ApplyTheme();
        });
        _terminalChanged = (_, _) => OnTerminalChanged();
        ThemeService.Changed += _themeChanged;
        SettingsService.Changed += _settingsChanged;
        IntegratedTerminal.Changed += _terminalChanged;
        ActualThemeVariantChanged += (_, _) => ApplyTheme();
        _view.KeyHint = HotkeyService.Display(SettingsService.Get().Hotkey);

        _systemIsDark = (Application.Current?.ActualThemeVariant ?? ThemeVariant.Light) == ThemeVariant.Dark;
        _kind = KindOf(ThemeService.Active(_systemIsDark));
        SetUpWindow();
        ApplyTheme();
    }

    /// <summary>
    /// The theme now needs another kind of window (glass ↔ plain, shadow ↔ flat): the app builds a new launcher and closes
    /// this one. Raised on the UI thread.
    /// </summary>
    public event EventHandler? RebuildRequested;

    /// <summary>What the native window is: the system's glass, a plain window with an own shadow, a plain flat one.</summary>
    private enum WindowKind
    {
        Acrylic,
        Mica,
        Shadowed,
        Flat,
    }

    /// <summary>Set by App when the application ends: then the window really closes.</summary>
    public bool IsExiting { get; set; }

    /// <summary>The native window handle (Windows): the hotkey tells "ThixFred is in front" from another app by it.</summary>
    public IntPtr NativeHandle { get; private set; }

    /// <summary>True while the launcher is shown and in front (the shortcut then hides it).</summary>
    public bool IsActiveLauncher => IsVisible && IsActive;

    public LauncherView View => _view;

    /// <summary>True while the output of the integrated terminal is shown (tests).</summary>
    public bool IsInTerminal => _terminalMode;

    /// <summary>Runs a command in the integrated terminal as Enter would (tests, snapshots).</summary>
    public Task RunInTerminalAsync(string command) => RunTerminalAsync(command);

    /// <summary>The shortcut was pressed (UI thread): show, or hide when it is in front already.</summary>
    public void Toggle()
    {
        if (IsActiveLauncher)
        {
            HideLauncher();
            return;
        }

        ShowLauncher();
    }

    /// <summary>Shows the launcher on the screen of the mouse, with the search selected (or empty).</summary>
    public void ShowLauncher(string? query = null)
    {
        if (!IsVisible)
        {
            var settings = SettingsService.Get();
            _pendingConfirmation = null;
            if (IntegratedTerminal.IsRunning && query is null)
            {
                // a command still runs: the launcher comes back with its output
                EnterTerminal("");
            }
            else
            {
                var text = query ?? (settings.KeepLastQuery ? settings.LastQuery : "");
                if (_view.SearchBox.Text != text)
                {
                    _view.SearchBox.Text = text;
                }
                else
                {
                    Search();
                }
            }

            Place();
        }
        else if (query is not null)
        {
            _view.SearchBox.Text = query;
        }

        Show();
        Activate();
        WindowActivation.BringToFront(NativeHandle);
        _view.SearchBox.Focus();
        if (_terminalMode)
        {
            _view.SearchBox.CaretIndex = _view.SearchBox.Text?.Length ?? 0;
        }
        else
        {
            _view.SearchBox.SelectAll();
        }
    }

    public void HideLauncher()
    {
        _filter?.Cancel();
        if (SettingsService.Get().KeepLastQuery && !_terminalMode)
        {
            SettingsService.Remember(settings => settings with { LastQuery = _view.SearchBox.Text ?? "" });
        }

        Hide();
    }

    /// <summary>Creates the native window once, invisibly, so the first shortcut is as fast as every later one.</summary>
    public void Prewarm()
    {
        Opacity = 0;
        Position = new PixelPoint(-32000, -32000);
        Show();
        NativeHandle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        Dispatcher.UIThread.Post(() =>
        {
            Hide();
            Opacity = 1;
        }, DispatcherPriority.Background);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        NativeHandle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!IsExiting)
        {
            e.Cancel = true;
            HideLauncher();
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        ThemeService.Changed -= _themeChanged;
        SettingsService.Changed -= _settingsChanged;
        IntegratedTerminal.Changed -= _terminalChanged;
        _filter?.Cancel();
        base.OnClosed(e);
    }

    private static WindowKind KindOf(LauncherTheme theme) => theme.Backdrop switch
    {
        ThemeBackdrop.Acrylic => WindowKind.Acrylic,
        ThemeBackdrop.Mica => WindowKind.Mica,
        _ => theme.Shadow ? WindowKind.Shadowed : WindowKind.Flat,
    };

    /// <summary>
    /// The native side, once: glass lets the system draw the glass (and round the corners and cast the shadow); Shadowed draws
    /// its own rounded window with a shadow on a transparent window; Flat is a plain window exactly as large as the launcher.
    /// </summary>
    private void SetUpWindow()
    {
        switch (_kind)
        {
            case WindowKind.Acrylic or WindowKind.Mica:
                SystemDecorations = SystemDecorations.Full;
                ExtendClientAreaToDecorationsHint = true;
                ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
                ExtendClientAreaTitleBarHeightHint = 0;
                TransparencyLevelHint = _kind == WindowKind.Mica
                    ? [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None]
                    : [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur, WindowTransparencyLevel.None];
                _frame.Padding = new Thickness(0);
                break;
            case WindowKind.Shadowed:
                SystemDecorations = SystemDecorations.None;
                TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
                _frame.Padding = new Thickness(ShadowMargin);
                break;
            default:
                SystemDecorations = SystemDecorations.None;
                TransparencyLevelHint = [WindowTransparencyLevel.None];
                _frame.Padding = new Thickness(0);
                break;
        }
    }

    /// <summary>The colours and sizes of the theme; a theme that needs another kind of window asks for a new one.</summary>
    private void ApplyTheme()
    {
        _systemIsDark = (Application.Current?.ActualThemeVariant ?? ThemeVariant.Light) == ThemeVariant.Dark;
        var theme = ThemeService.Active(_systemIsDark);
        RequestedThemeVariant = theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        _view.ApplyTheme(theme);
        switch (_kind)
        {
            case WindowKind.Acrylic or WindowKind.Mica:
                _view.BoxShadow = default;
                _view.CornerRadius = new CornerRadius(0);
                _view.BorderThickness = new Thickness(0);

                // no glass where the system has none: the background without transparency
                if (ActualTransparencyLevel == WindowTransparencyLevel.None && IsVisible)
                {
                    _view.Background = LauncherView.Brush(Opaque(theme.Background));
                }

                break;
            case WindowKind.Shadowed:
                _view.BoxShadow = BoxShadows.Parse("0 12 32 0 #40000000");
                break;
            default:
                _view.BoxShadow = default;
                _view.CornerRadius = new CornerRadius(0);
                _view.Background = LauncherView.Brush(Opaque(theme.Background));
                Background = _view.Background;
                break;
        }

        if (KindOf(theme) != _kind && !IsExiting && !_rebuildRequested)
        {
            _rebuildRequested = true;
            Dispatcher.UIThread.Post(() => RebuildRequested?.Invoke(this, EventArgs.Empty));
        }
    }

    private static string Opaque(string color) => color.Length == 9 ? "#FF" + color[3..] : color;

    private void Search()
    {
        _pendingConfirmation = null;
        _filter?.Cancel();
        var query = _view.SearchBox.Text ?? "";
        if (_terminalMode)
        {
            // the terminal stays while a command runs (the line is its input) or the line holds the next command
            if (IntegratedTerminal.IsRunning || SearchService.TerminalCommand(query) is not null)
            {
                ShowTerminal();
                return;
            }

            LeaveTerminal();
        }

        var max = SettingsService.Get().MaxResults;
        _view.ShowResults(SearchService.Search(query, max));
        if (SearchService.FilterPlugin(query) is { } filter)
        {
            _ = FilterAsync(filter.Plugin, filter.Argument, query, max);
        }
    }

    private async Task FilterAsync(PluginInfo plugin, string argument, string query, int max)
    {
        var cancel = new CancellationTokenSource();
        _filter = cancel;
        try
        {
            await Task.Delay(120, cancel.Token);
            var results = await PluginService.FilterAsync(plugin.Id, argument, cancel.Token);
            if (!cancel.IsCancellationRequested && _view.SearchBox.Text == query && !_terminalMode)
            {
                _view.ShowResults(SearchService.FilterResults(plugin, query, results, max));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (BusinessException exception)
        {
            if (_view.SearchBox.Text == query && !_terminalMode)
            {
                _view.ShowMessage(exception.Message, isError: true, "Esc zurück");
            }
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_terminalMode && OnTerminalKey(e))
        {
            e.Handled = true;
            return;
        }

        var command = Ui.CommandKey.Modifier;
        var withCommand = e.KeyModifiers.HasFlag(command);
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                if (_view.IsShowingMessage)
                {
                    _pendingConfirmation = null;
                    Search();
                }
                else if (!string.IsNullOrEmpty(_view.SearchBox.Text))
                {
                    _view.SearchBox.Text = "";
                }
                else
                {
                    HideLauncher();
                }

                return;

            case Key.Enter when _pendingConfirmation is { } confirmation:
                e.Handled = true;
                _pendingConfirmation = null;
                _ = ExecuteAsync(confirmation.Result, confirmation.Action);
                return;

            case Key.Enter:
                e.Handled = true;
                _ = RunSelectedAsync(alternate: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                return;

            case Key.Down:
                e.Handled = true;
                MoveSelection(1);
                return;

            case Key.Up:
                e.Handled = true;
                MoveSelection(-1);
                return;

            case Key.PageDown:
                e.Handled = true;
                _view.Select(_view.SelectedIndex + 5);
                return;

            case Key.PageUp:
                e.Handled = true;
                _view.Select(_view.SelectedIndex - 5);
                return;

            case Key.Tab when _view.Selected?.Autocomplete is { } completion:
                e.Handled = true;
                _view.SearchBox.Text = completion;
                _view.SearchBox.CaretIndex = completion.Length;
                return;

            case Key.Tab:
                e.Handled = true;
                return;

            case >= Key.D1 and <= Key.D9 when withCommand:
                e.Handled = true;
                var index = e.Key - Key.D1;
                if (index < _view.Results.Count)
                {
                    _view.Select(index);
                    _ = RunSelectedAsync(alternate: false);
                }

                return;

            case Key.OemComma when withCommand:
                e.Handled = true;
                HideLauncher();
                App.Instance.RunCommand(AppCommand.Settings);
                return;

            case Key.C when withCommand && _view.MessageText is { } message:
                e.Handled = true;
                _ = LaunchService.CopyAsync(message);
                return;
        }
    }

    /// <summary>The keys of the terminal; false = the normal handling (typing, Strg+, …).</summary>
    private bool OnTerminalKey(KeyEventArgs e)
    {
        var withCommand = e.KeyModifiers.HasFlag(Ui.CommandKey.Modifier);
        var withControl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var text = _view.SearchBox.Text ?? "";
        switch (e.Key)
        {
            case Key.Escape when IntegratedTerminal.IsRunning:
                HideLauncher(); // the command goes on; the shortcut shows it again
                return true;

            case Key.Escape:
                LeaveTerminal();
                _view.SearchBox.Text = "";
                return true;

            case Key.Enter when IntegratedTerminal.IsRunning:
                IntegratedTerminal.SendInput(text);
                _view.SearchBox.Text = "";
                return true;

            case Key.Enter:
                if (SearchService.TerminalCommand(text) is not { Length: > 0 } command)
                {
                    return true;
                }

                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                {
                    _ = RunInTerminalWindowAsync(command);
                    return true;
                }

                _ = RunTerminalAsync(command);
                return true;

            // Strg+C ends the command (also on the Mac, as in every terminal); without one it copies the output
            case Key.C when withControl && IntegratedTerminal.IsRunning:
                IntegratedTerminal.Cancel();
                return true;

            case Key.C when withCommand && _view.SearchBox.SelectionStart == _view.SearchBox.SelectionEnd:
                _ = LaunchService.CopyAsync(IntegratedTerminal.Output);
                return true;

            case Key.L when withControl:
                IntegratedTerminal.Clear();
                return true;

            case Key.Up when !IntegratedTerminal.IsRunning:
                ShowHistory(_historyIndex + 1);
                return true;

            case Key.Down when !IntegratedTerminal.IsRunning:
                ShowHistory(_historyIndex - 1);
                return true;

            case Key.Up or Key.Down:
                return true;

            case Key.PageUp:
                _view.ScrollTerminal(-1);
                return true;

            case Key.PageDown:
                _view.ScrollTerminal(1);
                return true;

            case Key.Tab:
                return true;

            case >= Key.D1 and <= Key.D9 when withCommand:
                return true;

            default:
                return false;
        }
    }

    /// <summary>The terminal takes the place of the results; <paramref name="text"/> is the line (the next command, an input).</summary>
    private void EnterTerminal(string text)
    {
        _terminalMode = true;
        _historyIndex = -1;
        _view.SearchGlyph = "terminal";
        if (_view.SearchBox.Text != text)
        {
            _view.SearchBox.Text = text;
        }
        else
        {
            ShowTerminal();
        }

        _view.SearchBox.CaretIndex = text.Length;
    }

    private void LeaveTerminal()
    {
        _terminalMode = false;
        _view.SearchGlyph = "search";
        _view.SearchBox.Watermark = LauncherView.DefaultWatermark;
        _view.FooterRight = "ThixFred";
    }

    private void ShowTerminal()
    {
        var running = IntegratedTerminal.RunningCommand;
        var copy = OperatingSystem.IsMacOS() ? "⌘C" : "Strg+C";
        var hint = running is not null
            ? "Läuft · Enter sendet Eingabe · Strg+C bricht ab · Esc blendet aus"
            : (IntegratedTerminal.LastExitCode switch
            {
                null => "Bereit",
                0 => "Fertig",
                var code => $"Exit-Code {code}",
            }) + $" · ↵ ausführen · ↑ letzte Befehle · {copy} kopiert · Strg+L leert · Esc zurück";
        _view.SearchBox.Watermark = running is not null ? $"Eingabe für „{running}“ …" : "Befehl …";
        _view.FooterRight = IntegratedTerminal.WorkingDirectory;
        _view.ShowTerminal(IntegratedTerminal.Output, hint);
    }

    /// <summary>The output grew (any thread): one refresh at a time on the UI thread.</summary>
    private void OnTerminalChanged()
    {
        if (_terminalRefreshPending)
        {
            return;
        }

        _terminalRefreshPending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _terminalRefreshPending = false;
            if (_terminalMode)
            {
                ShowTerminal();
            }
        }, DispatcherPriority.Background);
    }

    private async Task RunTerminalAsync(string command)
    {
        var run = IntegratedTerminal.RunAsync(command);

        // while it runs the line is its input; a command done at once (cd) leaves the line for the next one
        EnterTerminal(IntegratedTerminal.IsRunning ? "" : SearchService.TerminalPrefix());
        try
        {
            await run;
        }
        catch (BusinessException exception)
        {
            LeaveTerminal();
            _view.ShowMessage(exception.Message, isError: true, "Esc zurück");
            return;
        }

        if (_terminalMode && SearchService.TerminalCommand(_view.SearchBox.Text ?? "") is null)
        {
            EnterTerminal(SearchService.TerminalPrefix());
        }
    }

    /// <summary>Umschalt+Enter: the command in the terminal window instead (for programs with a screen of their own).</summary>
    private async Task RunInTerminalWindowAsync(string command)
    {
        var result = new SearchResult
        {
            Id = $"terminal:{command}",
            Title = command,
            Kind = ResultKind.Terminal,
            Action = new LaunchAction("Im Terminal öffnen", LaunchActionKind.Terminal, command),
        };
        HideLauncher();
        var outcome = await LaunchService.ExecuteAsync(result, result.Action, SearchService.TerminalPrefix() + command);
        if (outcome.Kind == LaunchOutcomeKind.Message)
        {
            ShowLauncher();
            LeaveTerminal();
            _view.ShowMessage(outcome.Message ?? "", outcome.IsError, "Esc zurück");
        }
    }

    /// <summary>↑ ↓: the last commands (0 = the newest; -1 = the empty line).</summary>
    private void ShowHistory(int index)
    {
        var commands = UsageService.RecentCommands();
        _historyIndex = Math.Clamp(index, -1, commands.Count - 1);
        var text = SearchService.TerminalPrefix() + (_historyIndex >= 0 ? commands[_historyIndex] : "");
        _view.SearchBox.Text = text;
        _view.SearchBox.CaretIndex = text.Length;
    }

    private void MoveSelection(int step)
    {
        if (_view.IsShowingMessage || _view.Results.Count == 0)
        {
            return;
        }

        var next = _view.SelectedIndex + step;
        _view.Select(next < 0 ? _view.Results.Count - 1 : next >= _view.Results.Count ? 0 : next);
    }

    private async Task RunSelectedAsync(bool alternate)
    {
        if (_view.IsShowingMessage || _view.Selected is not { } result)
        {
            return;
        }

        var action = alternate ? result.AlternateAction ?? result.Action : result.Action;
        if (action.Confirmation is { } question)
        {
            _pendingConfirmation = (result, action);
            _view.ShowMessage(question, isError: false, $"Enter: {action.Label} · Esc: abbrechen");
            return;
        }

        await ExecuteAsync(result, action);
    }

    private async Task ExecuteAsync(SearchResult result, LaunchAction action)
    {
        if (_isExecuting)
        {
            return;
        }

        _isExecuting = true;
        try
        {
            // starting a program: away at once, the program comes up meanwhile
            var hidesFirst = action.Kind is LaunchActionKind.Open or LaunchActionKind.Reveal or LaunchActionKind.OpenAsAdministrator or LaunchActionKind.Terminal or LaunchActionKind.System;
            var query = _view.SearchBox.Text ?? "";
            if (hidesFirst)
            {
                HideLauncher();
            }

            var outcome = await LaunchService.ExecuteAsync(result, action, query);
            switch (outcome.Kind)
            {
                case LaunchOutcomeKind.Hide:
                    if (IsVisible)
                    {
                        HideLauncher();
                    }

                    if (!SettingsService.Get().KeepLastQuery)
                    {
                        _view.SearchBox.Text = "";
                    }

                    break;
                case LaunchOutcomeKind.SetQuery:
                    var text = outcome.Query ?? "";
                    if (_view.SearchBox.Text == text)
                    {
                        Search(); // the same search again: after an install, "Erneut abfragen"
                    }
                    else
                    {
                        _view.SearchBox.Text = text;
                    }

                    _view.SearchBox.CaretIndex = text.Length;
                    break;
                case LaunchOutcomeKind.AppCommand:
                    HideLauncher();
                    _view.SearchBox.Text = "";
                    App.Instance.RunCommand(outcome.Command!.Value);
                    break;
                case LaunchOutcomeKind.Terminal:
                    _ = RunTerminalAsync(outcome.Query ?? "");
                    break;
                case LaunchOutcomeKind.Message:
                    if (!IsVisible)
                    {
                        ShowLauncher();
                    }

                    _view.ShowMessage(outcome.Message ?? "", outcome.IsError, "Esc zurück · " + (OperatingSystem.IsMacOS() ? "⌘C" : "Strg+C") + " kopiert");
                    break;
            }
        }
        finally
        {
            _isExecuting = false;
        }
    }

    private void HideWhenAnotherAppComesFirst()
    {
        if (!IsVisible || _isExecuting || !SettingsService.Get().HideOnDeactivate)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!IsActive && IsVisible)
            {
                HideLauncher();
            }
        }, DispatcherPriority.Background);
    }

    /// <summary>Centred on the screen with the mouse (Windows) or the current one, its top at a fifth of the height.</summary>
    private void Place()
    {
        var screen = WindowActivation.CursorPosition() is { } cursor ? Screens.ScreenFromPoint(new PixelPoint(cursor.X, cursor.Y)) : null;
        screen ??= Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var scaling = screen.Scaling;
        var width = (_view.LauncherTheme.Width + 2 * _frame.Padding.Left) * scaling;
        Position = new PixelPoint(area.X + (int)((area.Width - width) / 2), area.Y + (int)(area.Height * 0.2) - (int)(_frame.Padding.Top * scaling));
    }
}
