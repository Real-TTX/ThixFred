using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MatCloud.Contract;
using MatDesktop;
using ThixFred.Controls;
using ThixFred.Core.Business;
using ThixFred.Core.Infrastructure;
using ThixFred.Core.Models;
using ThixFred.Dialogs;

namespace ThixFred.Pages;

/// <summary>
/// The settings. Every change goes through the business layer (SettingsService, ThemeService, AutostartService, SyncService,
/// UpdateService), which checks values and policies; an error is shown at its field or above the tabs.
/// </summary>
public partial class SettingsPage : UserControl
{
    public const string GeneralTab = "general";
    public const string CloudTab = "cloud";
    public const string UpdatesTab = "updates";
    public const string SearchTab = "search";

    private bool _isLoading;
    private List<string> _themeIds = [];
    private List<string> _searchIds = [];
    private UpdateManifest? _availableUpdate;
    private CancellationTokenSource? _browserSignIn;
    private string? _browserAddress;

    public SettingsPage()
    {
        InitializeComponent();
        TerminalChoiceBox.ItemsSource = TerminalService.Choices.Select(TerminalService.DisplayName).ToList();
        StoreAppsRow.IsVisible = OperatingSystem.IsWindows();

        HotkeyRecorder.Recorded += (_, text) => Apply(() =>
        {
            SettingsService.SetHotkey(text);
            if (!App.Instance.RegisterHotkey())
            {
                throw new BusinessException("Das Tastenkürzel ist schon von einer anderen App belegt.", nameof(AppSettings.Hotkey));
            }
        });
        AutostartSwitch.IsCheckedChanged += (_, _) => Apply(() =>
        {
            if (AutostartSwitch.IsChecked == true)
            {
                AutostartService.Enable(App.ExecutablePath);
            }
            else
            {
                AutostartService.Disable();
            }
        });
        HideSwitch.IsCheckedChanged += (_, _) => Apply(() => SettingsService.Update(settings => settings with { HideOnDeactivate = HideSwitch.IsChecked == true }));
        KeepQuerySwitch.IsCheckedChanged += (_, _) => Apply(() => SettingsService.Update(settings => settings with { KeepLastQuery = KeepQuerySwitch.IsChecked == true }));
        ThemeChoice.SelectionChanged += (_, _) => Apply(() => SettingsService.Update(settings => settings with { Theme = (ThemeChoice)Math.Max(0, ThemeChoice.SelectedIndex) }));
        LauncherThemeChoice.SelectionChanged += (_, _) => Apply(() =>
        {
            if (LauncherThemeChoice.SelectedIndex >= 0 && LauncherThemeChoice.SelectedIndex < _themeIds.Count && _themeIds[LauncherThemeChoice.SelectedIndex] != SettingsService.Get().LauncherThemeId)
            {
                ThemeService.Use(_themeIds[LauncherThemeChoice.SelectedIndex]);
            }
        });
        MaxResultsBox.ValueChanged += (_, _) => Apply(() => SettingsService.Update(settings => settings with { MaxResults = (int)(MaxResultsBox.Value ?? 8) }));
        LearnSwitch.IsCheckedChanged += (_, _) => Apply(() => SettingsService.Update(settings => settings with { LearnFromUsage = LearnSwitch.IsChecked == true }));
        WebFallbackSwitch.IsCheckedChanged += (_, _) => Apply(() => SettingsService.Update(settings => settings with { WebSearchFallback = WebFallbackSwitch.IsChecked == true }));
        DefaultSearchChoice.SelectionChanged += (_, _) => Apply(() =>
        {
            if (DefaultSearchChoice.SelectedIndex >= 0 && DefaultSearchChoice.SelectedIndex < _searchIds.Count)
            {
                SettingsService.Update(settings => settings with { DefaultWebSearchId = _searchIds[DefaultSearchChoice.SelectedIndex] });
            }
        });
        TerminalChoiceBox.SelectionChanged += (_, _) => Apply(() => SettingsService.Update(settings => settings with { Terminal = TerminalService.Choices[Math.Max(0, TerminalChoiceBox.SelectedIndex)] }));
        StoreAppsSwitch.IsCheckedChanged += (_, _) => Apply(() =>
        {
            SettingsService.Update(settings => settings with { IncludeStoreApps = StoreAppsSwitch.IsChecked == true });
            _ = AppIndex.Rescan();
        });
        CheckUpdatesSwitch.IsCheckedChanged += (_, _) => Apply(() => SettingsService.Update(settings => settings with { CheckForUpdates = CheckUpdatesSwitch.IsChecked == true }));
        AutoInstallSwitch.IsCheckedChanged += (_, _) => Apply(() => SettingsService.Update(settings => settings with { InstallUpdatesAutomatically = AutoInstallSwitch.IsChecked == true }));

        SettingsService.Changed += (_, _) => Ui.Post(Load);
        ThemeService.Changed += (_, _) => Ui.Post(Load);
        WebSearchService.Changed += (_, _) => Ui.Post(Load);
        AppIndex.Changed += (_, _) => Ui.Post(LoadIndex);
        SyncService.StatusChanged += (_, _) => Ui.Post(LoadCloud);
        UpdateCoordinator.Checked += (_, result) => ShowUpdateResult(result);
        Load();
    }

    /// <summary>A sign-in in the browser is waiting for its confirmation (the main window stays open meanwhile).</summary>
    public bool IsSigningInInBrowser => _browserSignIn is not null;

    public void SelectTab(string tab) => Tabs.SelectedItem = tab switch
    {
        CloudTab => CloudTabItem,
        SearchTab => SearchTabItem,
        UpdatesTab => UpdatesTabItem,
        _ => GeneralTabItem,
    };

    /// <summary>Shows the stored settings (again after every change, also from the tray or the sidebar).</summary>
    private void Load()
    {
        _isLoading = true;
        try
        {
            var settings = SettingsService.Get();
            var locked = AccessPolicy.IsConfigurationLocked;
            PolicyNote.IsVisible = locked || AccessPolicy.IsCloudDisabled || AccessPolicy.AreUpdatesDisabled;

            HotkeyRecorder.Hotkey = settings.Hotkey;
            HotkeyRecorder.IsEnabled = HotkeyResetButton.IsEnabled = !locked;
            HotkeyResetButton.IsVisible = settings.Hotkey != HotkeyService.DefaultHotkey;
            AutostartSwitch.IsChecked = AutostartService.IsEnabled();
            AutostartSwitch.IsEnabled = AutostartService.IsSupported && !locked;
            HideSwitch.IsChecked = settings.HideOnDeactivate;
            KeepQuerySwitch.IsChecked = settings.KeepLastQuery;
            ThemeChoice.SelectedIndex = (int)settings.Theme;

            var themes = ThemeService.List();
            _themeIds = [LauncherTheme.SystemId, .. themes.Select(theme => theme.Id)];
            LauncherThemeChoice.ItemsSource = new[] { "Wie das System" }.Concat(themes.Select(theme => theme.Name)).ToList();
            LauncherThemeChoice.SelectedIndex = Math.Max(0, _themeIds.IndexOf(settings.LauncherThemeId));

            MaxResultsBox.Value = settings.MaxResults;
            LearnSwitch.IsChecked = settings.LearnFromUsage;
            WebFallbackSwitch.IsChecked = settings.WebSearchFallback;
            var searches = WebSearchService.List();
            _searchIds = searches.Select(search => search.Id).ToList();
            DefaultSearchChoice.ItemsSource = searches.Select(search => search.Name).ToList();
            DefaultSearchChoice.SelectedIndex = _searchIds.IndexOf(settings.DefaultWebSearchId);
            TerminalChoiceBox.SelectedIndex = Math.Max(0, TerminalService.Choices.ToList().IndexOf(settings.Terminal));
            StoreAppsSwitch.IsChecked = settings.IncludeStoreApps;
            LoadFolders(settings, locked);
            LoadIndex();
            foreach (var control in new Control[] { HideSwitch, KeepQuerySwitch, ThemeChoice, LauncherThemeChoice, MaxResultsBox, LearnSwitch, ForgetButton, WebFallbackSwitch, DefaultSearchChoice, TerminalChoiceBox, StoreAppsSwitch, CheckUpdatesSwitch, AutoInstallSwitch, StoreUrlBox, StoreUrlSaveButton })
            {
                control.IsEnabled = !locked;
            }

            if (string.IsNullOrEmpty(StoreUrlBox.Text))
            {
                StoreUrlBox.Text = settings.StoreUrl;
            }

            CheckUpdatesSwitch.IsChecked = settings.CheckForUpdates;
            AutoInstallSwitch.IsChecked = settings.InstallUpdatesAutomatically;
            InstalledVersionText.Text = MatVersion.Value(AppInfo.Version, AppInfo.Channel);
            ChannelText.Text = AppInfo.Channel switch
            {
                "release" => "Release (Branch main)",
                "nightly" => "Nightly (Branch dev)",
                _ => "Lokaler Build – keine Updates",
            };
            LastCheckText.Text = UpdateService.LastCheck is { } last ? Ui.FormatDateTime(last) : "–";
            CheckUpdateButton.IsEnabled = UpdateService.CanUpdate;
            CheckUpdatesSwitch.IsEnabled = AutoInstallSwitch.IsEnabled = UpdateService.CanUpdate && !locked;

            DataFolderRow.Description = AppPaths.DataDirectory;
            PluginFolderRow.Description = AppPaths.PluginsDirectory;
            ScriptFolderRow.Description = AppPaths.ScriptsDirectory;
            LogFolderRow.Description = AppPaths.LogDirectory;
            LoadCloud();
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void LoadCloud()
    {
        var cloud = SettingsService.Get().Cloud;
        var status = SyncService.Status;
        CloudLogin.IsVisible = !cloud.IsConnected;
        CloudConnected.IsVisible = cloud.IsConnected;
        CloudLogin.IsEnabled = !AccessPolicy.IsCloudDisabled;
        if (AccessPolicy.IsCloudDisabled)
        {
            CloudMessage.Show("Von deiner Organisation festgelegt: die Cloud-Synchronisierung ist ausgeschaltet (Richtlinie DisableCloud).", InfoSeverity.Warning);
        }
        else if (status.State == CloudState.SignedOut)
        {
            CloudMessage.Show(status.Message, InfoSeverity.Warning);
        }

        if (!cloud.IsConnected)
        {
            if (string.IsNullOrEmpty(ServerUrlBox.Text))
            {
                ServerUrlBox.Text = cloud.ServerUrl;
            }

            if (string.IsNullOrEmpty(UserNameBox.Text))
            {
                UserNameBox.Text = cloud.UserName;
            }

            if (string.IsNullOrEmpty(DeviceNameBox.Text))
            {
                DeviceNameBox.Text = cloud.DeviceName.Length > 0 ? cloud.DeviceName : AppInfo.MachineName;
            }

            return;
        }

        CloudServerText.Text = cloud.ServerUrl;
        CloudAccountText.Text = cloud.DisplayName.Length > 0 && cloud.DisplayName != cloud.UserName ? $"{cloud.DisplayName} ({cloud.UserName})" : cloud.UserName;
        CloudDeviceText.Text = cloud.DeviceName;
        CloudStateText.Text = status.Message;
        CloudLastSyncText.Text = status.LastSync is { } last ? Ui.FormatDateTime(last) : "–";
    }

    private void Apply(Action change)
    {
        if (_isLoading)
        {
            return;
        }

        try
        {
            FormErrors.ShowError(this, null, "");
            PageMessage.Hide();
            change();
        }
        catch (BusinessException exception)
        {
            ShowError(exception.Message, exception.Field);
            Load();
        }
    }

    private async Task ApplyAsync(Func<Task> change)
    {
        try
        {
            FormErrors.ShowError(this, null, "");
            PageMessage.Hide();
            await change();
        }
        catch (BusinessException exception)
        {
            ShowError(exception.Message, exception.Field);
            Load();
        }
    }

    private void ShowError(string message, string? field)
    {
        if (!FormErrors.ShowError(this, field, message))
        {
            PageMessage.Show(message, InfoSeverity.Error);
        }
    }

    private void OnHotkeyResetClick(object? sender, RoutedEventArgs e) => Apply(() =>
    {
        SettingsService.SetHotkey(HotkeyService.DefaultHotkey);
        App.Instance.RegisterHotkey();
    });

    /// <summary>The first way: MatCloud shows a code, the browser opens, the person confirms it there; ThixFred waits (Abbrechen ends it).</summary>
    private async void OnBrowserConnectClick(object? sender, RoutedEventArgs e)
    {
        if (_browserSignIn is not null)
        {
            return;
        }

        CloudMessage.Hide();
        FormErrors.ShowError(this, null, "");
        _browserSignIn = new CancellationTokenSource();
        ServerUrlBox.IsEnabled = DeviceNameBox.IsEnabled = BrowserConnectButton.IsEnabled = ShowPasswordSignInButton.IsEnabled = false;
        try
        {
            if (await SyncService.ConnectInBrowserAsync(ServerUrlBox.Text ?? "", DeviceNameBox.Text ?? "", start => Ui.Post(() => ShowBrowserCode(start)),
                    Ui.OpenExternal, _browserSignIn.Token))
            {
                CloudMessage.Show("Verbunden. Deine Einstellungen, Themes und Skripte werden jetzt mit der Cloud abgeglichen.", InfoSeverity.Success);
            }
        }
        catch (BusinessException exception)
        {
            if (!FormErrors.ShowError(this, exception.Field, exception.Message))
            {
                CloudMessage.Show(exception.Message, InfoSeverity.Error);
            }
        }
        finally
        {
            _browserSignIn.Dispose();
            _browserSignIn = null;
            ServerUrlBox.IsEnabled = DeviceNameBox.IsEnabled = BrowserConnectButton.IsEnabled = ShowPasswordSignInButton.IsEnabled = true;
            ShowSignInWay(password: false);
            LoadCloud();
        }
    }

    /// <summary>The code of a started sign-in in the browser, big, with the hint to confirm it there.</summary>
    internal void ShowBrowserCode(DeviceLinkStart start)
    {
        _browserAddress = start.VerificationUrl;
        BrowserCodeText.Text = start.Code;
        BrowserSignIn.IsVisible = PasswordSignIn.IsVisible = false;
        BrowserCode.IsVisible = true;
        FocusAfterLayout(CancelLinkButton);
    }

    /// <summary>The way to sign in: in the browser (first) or with user name and password (second).</summary>
    internal void ShowSignInWay(bool password)
    {
        BrowserCode.IsVisible = false;
        BrowserSignIn.IsVisible = !password;
        PasswordSignIn.IsVisible = password;
        BrowserConnectButton.IsDefault = !password;
        ConnectButton.IsDefault = password;
    }

    private void OnShowPasswordSignInClick(object? sender, RoutedEventArgs e)
    {
        ShowSignInWay(password: true);
        FocusAfterLayout(string.IsNullOrEmpty(UserNameBox.Text) ? UserNameBox : PasswordBox);
    }

    private void OnShowBrowserSignInClick(object? sender, RoutedEventArgs e)
    {
        ShowSignInWay(password: false);
        FocusAfterLayout(BrowserConnectButton);
    }

    /// <summary>A control that has just become visible takes the focus once it is laid out (before that it cannot).</summary>
    private static void FocusAfterLayout(Control control) =>
        Dispatcher.UIThread.Post(() => control.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);

    private void OnOpenLinkClick(object? sender, RoutedEventArgs e)
    {
        if (_browserAddress is not null)
        {
            Ui.OpenExternal(_browserAddress);
        }
    }

    private void OnCancelLinkClick(object? sender, RoutedEventArgs e) => _browserSignIn?.Cancel();

    /// <summary>The second way: user name and password of a local account of MatCloud.</summary>
    private async void OnConnectClick(object? sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = false;
        CloudMessage.Hide();
        try
        {
            FormErrors.ShowError(this, null, "");
            await SyncService.ConnectAsync(ServerUrlBox.Text ?? "", UserNameBox.Text ?? "", PasswordBox.Text ?? "", DeviceNameBox.Text ?? "");
            PasswordBox.Text = "";
            CloudMessage.Show("Verbunden. Deine Einstellungen, Themes und Skripte werden jetzt mit der Cloud abgeglichen.", InfoSeverity.Success);
        }
        catch (BusinessException exception)
        {
            if (!FormErrors.ShowError(this, exception.Field, exception.Message))
            {
                CloudMessage.Show(exception.Message, InfoSeverity.Error);
            }
        }
        finally
        {
            ConnectButton.IsEnabled = true;
            LoadCloud();
        }
    }

    private void OnSyncNowClick(object? sender, RoutedEventArgs e) => SyncService.Kick();

    private void OnOpenCloudClick(object? sender, RoutedEventArgs e) => Ui.OpenExternal(SettingsService.Get().Cloud.ServerUrl);

    private async void OnDisconnectClick(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDialog.AskAsync(Ui.WindowOf(this), "Von der Cloud abmelden?",
                "Dieser Computer wird aus deinem Cloud-Konto entfernt und gleicht nicht mehr ab. Was auf diesem Computer eingerichtet ist, bleibt.",
                "Abmelden", isDestructive: true))
        {
            return;
        }

        await SyncService.DisconnectAsync();
        CloudMessage.Show("Abgemeldet.", InfoSeverity.Info);
        LoadCloud();
    }

    private async void OnCheckUpdateClick(object? sender, RoutedEventArgs e) => await CheckForUpdatesAsync();

    /// <summary>"Nach Updates suchen" (also the command of the launcher).</summary>
    public async Task CheckForUpdatesAsync()
    {
        CheckUpdateButton.IsEnabled = false;
        try
        {
            ShowUpdateResult(await UpdateCoordinator.CheckAsync());
        }
        catch (BusinessException exception)
        {
            UpdateMessage.Show(exception.Message, InfoSeverity.Error);
        }
        finally
        {
            CheckUpdateButton.IsEnabled = UpdateService.CanUpdate;
            LastCheckText.Text = UpdateService.LastCheck is { } last ? Ui.FormatDateTime(last) : "–";
        }
    }

    private void ShowUpdateResult(UpdateCheckResult result)
    {
        _availableUpdate = result.Manifest;
        InstallUpdateButton.IsVisible = result.IsUpdateAvailable;
        UpdateMessage.Show(result.Message, result.IsUpdateAvailable ? InfoSeverity.Success : InfoSeverity.Info);
    }

    private async void OnInstallUpdateClick(object? sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null)
        {
            return;
        }

        InstallUpdateButton.IsEnabled = CheckUpdateButton.IsEnabled = false;
        UpdateProgress.IsVisible = true;
        try
        {
            await UpdateCoordinator.InstallAsync(_availableUpdate, new Progress<double>(value => UpdateProgress.Value = value));
        }
        catch (Exception exception) when (exception is BusinessException or HttpRequestException or IOException)
        {
            UpdateMessage.Show($"Das Update ist fehlgeschlagen: {exception.Message}", InfoSeverity.Error);
            InstallUpdateButton.IsEnabled = CheckUpdateButton.IsEnabled = true;
            UpdateProgress.IsVisible = false;
        }
    }

    private void OnReleasePageClick(object? sender, RoutedEventArgs e) => Ui.OpenExternal(UpdateService.ReleasePageUrl(AppInfo.IsLocalBuild ? "release" : AppInfo.Channel));

    /// <summary>The time and size of the program index.</summary>
    private void LoadIndex() => IndexRow.Description = AppIndex.LastScan is { } last
        ? $"{AppIndex.Entries.Count} Programme und Einstellungen gefunden, zuletzt eingelesen {Ui.FormatDateTime(last)}."
        : $"{AppIndex.Entries.Count} Programme – werden gerade eingelesen …";

    /// <summary>The extra folders, each with its button to remove it.</summary>
    private void LoadFolders(AppSettings settings, bool locked)
    {
        FolderList.Children.Clear();
        NoFoldersText.IsVisible = settings.ExtraAppFolders.Count == 0;
        foreach (var folder in settings.ExtraAppFolders)
        {
            var remove = Ui.Button("trash", "Entfernen", "MatButton.Ghost", () => Apply(() => SettingsService.RemoveAppFolder(folder)));
            remove.IsEnabled = !locked;
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            row.Children.Add(new MatIcon { Icon = "folder", Size = 16, Margin = new Avalonia.Thickness(0, 0, 10, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            var text = new SelectableTextBlock { Text = folder, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            Grid.SetColumn(text, 1);
            Grid.SetColumn(remove, 2);
            row.Children.Add(text);
            row.Children.Add(remove);
            FolderList.Children.Add(row);
        }
    }

    private async void OnAddFolderClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanPickFolder: true } storage)
        {
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions { Title = "Ordner mit Programmen", AllowMultiple = false });
        if (folders.FirstOrDefault() is { } picked && Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(picked) is { } path)
        {
            Apply(() => SettingsService.AddAppFolder(path));
        }
    }

    private void OnReindexClick(object? sender, RoutedEventArgs e)
    {
        _ = AppIndex.Rescan();
        IndexRow.Description = "Wird eingelesen …";
    }

    private async void OnForgetClick(object? sender, RoutedEventArgs e)
    {
        if (await ConfirmDialog.AskAsync(Ui.WindowOf(this), "Gelerntes vergessen?", "Die Reihenfolge der Ergebnisse beginnt wieder von vorn; die zuletzt ausgeführten Befehle werden vergessen.", "Vergessen", isDestructive: true))
        {
            UsageService.Clear();
            PageMessage.Show("Vergessen.", InfoSeverity.Success);
        }
    }

    private void OnThemesClick(object? sender, RoutedEventArgs e) => (Ui.WindowOf(this) as MainWindow)?.ShowOn(AppCommand.Themes);

    private void OnStoreUrlSaveClick(object? sender, RoutedEventArgs e) => Apply(() =>
    {
        SettingsService.Update(settings => settings with { StoreUrl = (StoreUrlBox.Text ?? "").Trim() });
        PageMessage.Show("Übernommen. Der Store lädt beim nächsten Öffnen von dort.", InfoSeverity.Success);
    });

    private void OnOpenPluginFolderClick(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.PluginsDirectory);
        Ui.OpenExternal(AppPaths.PluginsDirectory);
    }

    private void OnOpenScriptFolderClick(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.ScriptsDirectory);
        Ui.OpenExternal(AppPaths.ScriptsDirectory);
    }

    private void OnOpenDataFolderClick(object? sender, RoutedEventArgs e) => Ui.OpenExternal(AppPaths.DataDirectory);

    private void OnOpenLogFolderClick(object? sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppPaths.LogDirectory);
        Ui.OpenExternal(AppPaths.LogDirectory);
    }
}
