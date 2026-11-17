using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MatDesktop;
using ThixFred.Core.Business;
using ThixFred.Core.Models;
using ThixFred.Pages;

namespace ThixFred;

/// <summary>
/// The settings window (the launcher is <see cref="Launcher.LauncherWindow"/>). Built once; the tray, the launcher
/// ("ThixFred: Plugins" …) and a second start show it on a page. The sections are list pages (Plugins, Store, Themes,
/// Websuchen) whose entries open their own edit page; Esc goes back one page, then hides the window.
///   Strg/⌘+1 … 4 the sections · Strg/⌘+, settings · Strg/⌘+B fold the menu · Strg/⌘+F the search of the list.
/// </summary>
public partial class MainWindow : MatWindow
{
    private readonly DispatcherTimer _sizeTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly MatNavItem _navPlugins;
    private readonly MatNavItem _navStore;
    private readonly MatNavItem _navThemes;
    private readonly MatNavItem _navWebSearches;
    private readonly MatNavItem _navSettings;
    private readonly Lazy<PluginsPage> _plugins;
    private readonly Lazy<StorePage> _store;
    private readonly Lazy<ThemesPage> _themes;
    private readonly Lazy<WebSearchesPage> _webSearches;
    private readonly Lazy<SettingsPage> _settings = new(() => new SettingsPage());
    private readonly Lazy<AboutPage> _about = new(() => new AboutPage());
    private readonly Stack<Control> _back = new();

    public MainWindow()
    {
        InitializeComponent();
        UseOwnTitleBar();
        _plugins = new(() => new PluginsPage(this));
        _store = new(() => new StorePage(this));
        _themes = new(() => new ThemesPage(this));
        _webSearches = new(() => new WebSearchesPage(this));
        var settings = SettingsService.Get();
        Width = settings.WindowWidth;
        Height = settings.WindowHeight;

        // The X (and Esc, Alt+F4) only hides: ThixFred keeps running in the tray / menu bar (DESK-06).
        TitleBar.Logo = Ui.Logo;
        TitleBar.CloseRequested += (_, e) =>
        {
            e.Handled = true;
            Hide();
        };

        var command = Ui.CommandKey.Name;
        _navPlugins = new MatNavItem("plug", "Plugins", $"{command}+1") { IsCurrent = true };
        _navStore = new MatNavItem("store", "Store", $"{command}+2");
        _navThemes = new MatNavItem("palette", "Themes", $"{command}+3");
        _navWebSearches = new MatNavItem("globe", "Websuchen", $"{command}+4");
        _navSettings = new MatNavItem("settings", "Einstellungen", $"{command}+,");
        Sidebar.Items.AddRange([_navPlugins, _navStore, _navThemes, _navWebSearches]);
        Sidebar.Settings = _navSettings;
        Sidebar.Version = Core.Infrastructure.AppInfo.Version;
        Sidebar.Channel = Core.Infrastructure.AppInfo.Channel;
        Sidebar.TitleBar = TitleBar;
        Sidebar.IsFolded = settings.IsSidebarCollapsed;
        Sidebar.FoldShortcut = $"{command}+B";
        Sidebar.ItemInvoked += (_, item) => Navigate(item);
        Sidebar.AboutRequested += (_, _) => ShowAbout();
        Sidebar.UpdateRequested += (_, _) => ShowAbout();
        Sidebar.FoldChanged += (_, folded) =>
        {
            SettingsService.Remember(current => current with { IsSidebarCollapsed = folded });
            RefreshCloudStatus();
        };
        AddToMenuFoot();

        _sizeTimer.Tick += (_, _) =>
        {
            _sizeTimer.Stop();
            if (WindowState == WindowState.Normal)
            {
                SettingsService.Remember(current => current with { WindowWidth = Width, WindowHeight = Height });
            }
        };
        Resized += (_, _) =>
        {
            _sizeTimer.Stop();
            _sizeTimer.Start();
        };

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        UpdateCoordinator.Checked += (_, _) => Ui.Post(RefreshUpdate);
        SettingsService.Changed += (_, current) => Ui.Post(() => HotkeyCaps.Keys = current.Hotkey);
        SyncService.StatusChanged += (_, _) => Ui.Post(RefreshCloudStatus);
        HotkeyCaps.Keys = settings.Hotkey;
        ShowSection(_navPlugins);
        RefreshUpdate();
        RefreshCloudStatus();
    }

    /// <summary>Set by App when the application ends: then the window really closes.</summary>
    public bool IsExiting { get; set; }

    /// <summary>The version at the foot of the menu (<c>Version 1.0.0 · release</c>, SETUP-07): it opens About.</summary>
    public Button VersionButton => Sidebar.VersionButton;

    /// <summary>The page shown now (tests).</summary>
    public Control? CurrentPage => PageHost.Content as Control;

    public PluginsPage PluginsPage => _plugins.Value;

    public StorePage StorePage => _store.Value;

    public ThemesPage ThemesPage => _themes.Value;

    public WebSearchesPage WebSearchesPage => _webSearches.Value;

    public SettingsPage SettingsPage => _settings.Value;

    public TextBlock CloudText { get; private set; } = null!;

    private Button CloudStatus { get; set; } = null!;

    private Ellipse CloudDot { get; set; } = null!;

    /// <summary>Shows the window (tray, launcher, second start) on a page.</summary>
    public void ShowOn(AppCommand? page)
    {
        switch (page)
        {
            case AppCommand.Store:
                ShowSection(_navStore);
                break;
            case AppCommand.Themes:
                ShowSection(_navThemes);
                break;
            case AppCommand.WebSearches:
                ShowSection(_navWebSearches);
                break;
            case AppCommand.Settings:
                ShowSettings();
                break;
            case AppCommand.About:
                ShowAbout();
                break;
            case AppCommand.Plugins:
                ShowSection(_navPlugins);
                break;
        }

        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Core.Platform.WindowActivation.BringToFront(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
    }

    public void ShowSettings(string? tab = null)
    {
        _back.Clear();
        Sidebar.Select(_navSettings);
        PageHost.Content = _settings.Value;
        if (tab is not null)
        {
            _settings.Value.SelectTab(tab);
        }
    }

    public void ShowAbout()
    {
        _back.Clear();
        Sidebar.Select(null);
        PageHost.Content = _about.Value;
    }

    /// <summary>Opens a page of an entry (edit page); Zurück or Esc returns to the page before.</summary>
    public void OpenPage(Control page)
    {
        if (PageHost.Content is Control current)
        {
            _back.Push(current);
        }

        PageHost.Content = page;
    }

    /// <summary>Back to the page before (an edit page closes); the list it came from draws again.</summary>
    public void GoBack()
    {
        if (_back.Count == 0)
        {
            return;
        }

        var previous = _back.Pop();
        PageHost.Content = previous;
        (previous as IRefreshable)?.Refresh();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!IsExiting)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private void ShowSection(MatNavItem item)
    {
        _back.Clear();
        Sidebar.Select(item);
        Control page = item == _navStore ? _store.Value : item == _navThemes ? _themes.Value : item == _navWebSearches ? _webSearches.Value : _plugins.Value;
        PageHost.Content = page;
        (page as IRefreshable)?.Refresh();
    }

    private void Navigate(MatNavItem item)
    {
        if (item == _navSettings)
        {
            ShowSettings();
        }
        else
        {
            ShowSection(item);
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var withCommand = e.KeyModifiers.HasFlag(Ui.CommandKey.Modifier);
        switch (e.Key)
        {
            case Key.Escape when OwnedWindows.Count == 0 && FocusManager?.GetFocusedElement() is not ComboBox:
                e.Handled = true;
                if (_back.Count > 0)
                {
                    GoBack();
                }
                else
                {
                    Hide();
                }

                return;
            case Key.D1 when withCommand:
                e.Handled = true;
                ShowSection(_navPlugins);
                return;
            case Key.D2 when withCommand:
                e.Handled = true;
                ShowSection(_navStore);
                return;
            case Key.D3 when withCommand:
                e.Handled = true;
                ShowSection(_navThemes);
                return;
            case Key.D4 when withCommand:
                e.Handled = true;
                ShowSection(_navWebSearches);
                return;
            case Key.OemComma when withCommand:
                e.Handled = true;
                ShowSettings();
                return;
            case Key.B when withCommand:
                e.Handled = true;
                Sidebar.IsFolded = !Sidebar.IsFolded;
                return;
            case Key.F when withCommand && PageHost.Content is ISearchable searchable:
                e.Handled = true;
                searchable.FocusSearch();
                return;
        }
    }

    /// <summary>The state of the cloud at the foot of the menu (a click leads to Einstellungen › Cloud).</summary>
    private void AddToMenuFoot()
    {
        CloudDot = new Ellipse { Width = 8, Height = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -2, -1) };
        CloudText = new TextBlock { FontSize = MatTokens.Type.FsSm, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 170 };
        CloudStatus = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(8, 0),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 9,
                Children = { new Panel { Children = { new MatIcon { Icon = "cloud", Size = 18 }, CloudDot } }, CloudText },
            },
        }.Themed("MatButton.Ghost");
        CloudStatus.Click += (_, _) => ShowSettings(SettingsPage.CloudTab);
        Sidebar.Foot = new StackPanel { Spacing = 10, Margin = new Thickness(0, 12, 0, 10), Children = { CloudStatus } };
    }

    private void RefreshUpdate() =>
        Sidebar.UpdateVersion = UpdateService.LastResult is { IsUpdateAvailable: true, Manifest: { } manifest } ? manifest.Version : null;

    private void RefreshCloudStatus()
    {
        var cloud = SettingsService.Get().Cloud;
        var status = SyncService.Status;
        (CloudText.Text, var dot) = status.State switch
        {
            CloudState.Online => ($"Synchronisiert · {cloud.UserName}", "Mat.Success"),
            CloudState.Syncing => ("Synchronisiere …", "Mat.Accent"),
            CloudState.Connecting => ("Verbinde …", "Mat.Accent"),
            CloudState.Offline => ("Cloud nicht erreichbar", "Mat.Warning"),
            CloudState.SignedOut => ("Abgemeldet – neu anmelden", "Mat.Danger"),
            _ => cloud.IsConnected ? ("Cloud verbunden", "Mat.Success") : ("Mit ThixCloud verbinden", (string?)null),
        };
        CloudDot.IsVisible = dot is not null;
        if (dot is not null)
        {
            CloudDot.Use(Shape.FillProperty, dot);
        }

        CloudText.IsVisible = !Sidebar.IsFolded;
        CloudStatus.HorizontalContentAlignment = Sidebar.IsFolded ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        var tip = status.LastSync is { } last ? $"{status.Message} (zuletzt {last:HH:mm:ss})" : status.Message;
        ToolTip.SetTip(CloudStatus, Sidebar.IsFolded ? $"{CloudText.Text}\n{tip}" : tip);
        AutomationProperties.SetName(CloudStatus, CloudText.Text);
    }

    private void OnOpenLauncherClick(object? sender, RoutedEventArgs e) => App.Instance.ShowLauncher();

    private void OnHotkeyBadgeClick(object? sender, RoutedEventArgs e) => ShowSettings(SettingsPage.GeneralTab);
}

/// <summary>A page that reads its data again when it is shown (after an edit page, after a change of the cloud).</summary>
public interface IRefreshable
{
    void Refresh();
}

/// <summary>A list page with a search (Strg/⌘+F).</summary>
public interface ISearchable
{
    void FocusSearch();
}
