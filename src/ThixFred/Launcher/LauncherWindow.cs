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
/// </summary>
public sealed class LauncherWindow : Window
{
    private const int ShadowMargin = 18;

    private readonly LauncherView _view = new();
    private readonly Border _frame;
    private CancellationTokenSource? _filter;
    private (SearchResult Result, LaunchAction Action)? _pendingConfirmation;
    private bool _isExecuting;
    private bool _systemIsDark;

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

        ThemeService.Changed += (_, _) => Ui.Post(ApplyTheme);
        SettingsService.Changed += (_, settings) => Ui.Post(() =>
        {
            _view.KeyHint = HotkeyService.Display(settings.Hotkey);
            ApplyTheme();
        });
        ActualThemeVariantChanged += (_, _) => ApplyTheme();
        _view.KeyHint = HotkeyService.Display(SettingsService.Get().Hotkey);
        ApplyTheme();
    }

    /// <summary>Set by App when the application ends: then the window really closes.</summary>
    public bool IsExiting { get; set; }

    /// <summary>The native window handle (Windows): the hotkey tells "ThixFred is in front" from another app by it.</summary>
    public IntPtr NativeHandle { get; private set; }

    /// <summary>True while the launcher is shown and in front (the shortcut then hides it).</summary>
    public bool IsActiveLauncher => IsVisible && IsActive;

    public LauncherView View => _view;

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
            var text = query ?? (settings.KeepLastQuery ? settings.LastQuery : "");
            if (_view.SearchBox.Text != text)
            {
                _view.SearchBox.Text = text;
            }
            else
            {
                Search();
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
        _view.SearchBox.SelectAll();
    }

    public void HideLauncher()
    {
        _filter?.Cancel();
        if (SettingsService.Get().KeepLastQuery)
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

    /// <summary>
    /// The theme: Solid draws its own rounded window with a shadow on a transparent window; Acrylic and Mica let the system
    /// draw the glass (and round the corners and cast the shadow) and tint it with the theme's background.
    /// </summary>
    private void ApplyTheme()
    {
        _systemIsDark = (Application.Current?.ActualThemeVariant ?? ThemeVariant.Light) == ThemeVariant.Dark;
        var theme = ThemeService.Active(_systemIsDark);
        RequestedThemeVariant = theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;
        _view.ApplyTheme(theme);
        if (theme.Backdrop == ThemeBackdrop.Solid)
        {
            SystemDecorations = SystemDecorations.None;
            ExtendClientAreaToDecorationsHint = false;
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
            _frame.Padding = new Thickness(ShadowMargin);
            _view.BoxShadow = BoxShadows.Parse("0 12 32 0 #40000000");
        }
        else
        {
            SystemDecorations = SystemDecorations.Full;
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
            ExtendClientAreaTitleBarHeightHint = 0;
            TransparencyLevelHint = theme.Backdrop == ThemeBackdrop.Mica
                ? [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None]
                : [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur, WindowTransparencyLevel.None];
            _frame.Padding = new Thickness(0);
            _view.BoxShadow = default;
            _view.CornerRadius = new CornerRadius(0);
            _view.BorderThickness = new Thickness(0);
        }

        // no glass where the system has none: the background without transparency
        if (theme.Backdrop != ThemeBackdrop.Solid && ActualTransparencyLevel == WindowTransparencyLevel.None && IsVisible)
        {
            _view.Background = LauncherView.Brush(Opaque(theme.Background));
        }
    }

    private static string Opaque(string color) => color.Length == 9 ? "#FF" + color[3..] : color;

    private void Search()
    {
        _pendingConfirmation = null;
        _filter?.Cancel();
        var query = _view.SearchBox.Text ?? "";
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
            if (!cancel.IsCancellationRequested && _view.SearchBox.Text == query)
            {
                _view.ShowResults(SearchService.Rank(results, FuzzyMatcher.Prepare(query), max));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (BusinessException exception)
        {
            if (_view.SearchBox.Text == query)
            {
                _view.ShowMessage(exception.Message, isError: true, "Esc zurück");
            }
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
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
                    _view.SearchBox.Text = outcome.Query ?? "";
                    _view.SearchBox.CaretIndex = _view.SearchBox.Text.Length;
                    break;
                case LaunchOutcomeKind.AppCommand:
                    HideLauncher();
                    _view.SearchBox.Text = "";
                    App.Instance.RunCommand(outcome.Command!.Value);
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
