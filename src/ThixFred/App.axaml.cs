using System.Net.NetworkInformation;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MatDesktop;
using ThixFred.Core.Business;
using ThixFred.Core.Infrastructure;
using ThixFred.Core.Models;
using ThixFred.Launcher;
using TrayIcon = ThixFred.Tray.TrayIcon;

namespace ThixFred;

/// <summary>
/// Start-up and end of ThixFred.
///  - Windows: the published ThixFred.exe is also its setup (RunSetupIfRequested).
///  - The look is MatDesktop's theme with ThixFred's accent (DESK-02).
///  - One instance per user and data folder (MatSingleInstance, DESK-05): a second start opens the window of the first one.
///  - The launcher and the settings window are built once at start and only shown and hidden afterwards: the shortcut opens
///    the launcher at once; the program index comes from its cache and is scanned again in the background.
///  - "--autostart" (sign-in) and "--tray" (after an update) start hidden in the tray / menu bar.
/// </summary>
public partial class App : Application
{
    public const string TrayArgument = "--tray";

    /// <summary>ThixFred's accent (IDENT-01): MatDesktop derives every accent token of light and dark from it.</summary>
    public const string Accent = "#5B33F0";

    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MatSingleInstance? _instance;
    private TrayIcon? _trayIcon;
    private MainWindow? _mainWindow;
    private LauncherWindow? _launcher;
    private bool _isExiting;

    /// <summary>The program file; the autostart entry points to it.</summary>
    public static string ExecutablePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ThixFred");

    public static App Instance => (App)Current!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

        // ThixFred speaks German, the texts of MatDesktop (title bar, menu, tray, setup) too.
        MatText.Language = "de";
        // the icon of every window (MatWindow.DefaultIcon): the main window, the dialogs, the setup
        MatTheme.Install(this, new MatThemeOptions(Accent) { Icon = Ui.AppIcon });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        _desktop = desktop;
        var args = desktop.Args ?? [];

        var selfTest = Array.IndexOf(args, SelfTest.Argument);
        if (selfTest >= 0)
        {
            var report = selfTest + 1 < args.Length ? args[selfTest + 1] : Path.Combine(AppPaths.DataDirectory, "self-test.txt");
            Dispatcher.UIThread.Post(async () => EndProcess(await SelfTest.RunAsync(report)));
            return;
        }

        if (RunSetupIfRequested(args))
        {
            return;
        }

        if (!TakeSingleInstance())
        {
            EndProcess(0);
            return;
        }

        Start(args);
    }

    /// <summary>The settings window (null during setup).</summary>
    public MainWindow? MainWindow => _mainWindow;

    /// <summary>The launcher (null during setup).</summary>
    public LauncherWindow? Launcher => _launcher;

    /// <summary>
    /// Ends the process through the main loop. Shutting down straight from OnFrameworkInitializationCompleted (update,
    /// quiet uninstall, second instance) throws "Dispatcher shut down" because the loop has not started yet.
    /// </summary>
    private void EndProcess(int exitCode) => Dispatcher.UIThread.Post(() => _desktop?.Shutdown(exitCode));

    private void Start(string[] args)
    {
        Dispatcher.UIThread.UnhandledException += OnUnhandledException;
        UiWatchdog.Start();
        Log.Info($"ThixFred {AppInfo.Version} starting on {UpdateService.PlatformKey} ({string.Join(' ', args)})");

        LaunchService.Clipboard = CopyToClipboardAsync;
        AppIndex.Start();
        ApplyTheme(SettingsService.Get().Theme);
        SettingsService.Changed += (_, settings) => Ui.Post(() => ApplyTheme(settings.Theme));

        _launcher = CreateLauncher();
        _mainWindow = new MainWindow();
        _trayIcon = new TrayIcon(this);
        var hotkeyRegistered = RegisterHotkey();
        SyncService.Start();
        _ = Task.Run(StoreService.WarmUpAsync); // the search then knows the keywords of the store ("ip" offers to install it)

        // Build the launcher's visuals once, off screen, so the first shortcut opens it as fast as every later one.
        _launcher.Prewarm();
        var startHidden = args.Contains(TrayArgument) || args.Contains(AutostartService.StartupArgument);
        if (!startHidden)
        {
            Dispatcher.UIThread.Post(() => ShowLauncher(), DispatcherPriority.Background);
        }

        if (!SettingsService.Get().WelcomeShown)
        {
            SettingsService.Remember(settings => settings with { WelcomeShown = true });
            _trayIcon.ShowNotification("ThixFred ist bereit", $"Öffne den Launcher jederzeit mit {HotkeyService.Display(SettingsService.Get().Hotkey)}.");
        }

        if (!hotkeyRegistered)
        {
            _trayIcon.ShowNotification("Tastenkürzel nicht verfügbar", "Das Tastenkürzel ließ sich nicht anmelden – bitte unter Einstellungen ein anderes wählen.", isWarning: true);
        }

        // Crashed or hung last time: say so, with the report.
        Dispatcher.UIThread.Post(() => Dialogs.ProblemReportDialog.ShowUnseen(), DispatcherPriority.Background);

        // macOS: opening the app again (Finder, Spotlight) shows the window.
        if (TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime activatable)
        {
            activatable.Activated += (_, activation) =>
            {
                if (activation.Kind == ActivationKind.Reopen)
                {
                    ShowSettings();
                }
            };
        }

        UpdateCoordinator.Checked += (_, result) =>
        {
            _trayIcon?.SetUpdateAvailable(result.IsUpdateAvailable);
            if (result is { IsUpdateAvailable: true, Manifest: { } manifest } && !SettingsService.Get().InstallUpdatesAutomatically)
            {
                _trayIcon?.ShowNotification("Update verfügbar", $"ThixFred {manifest.Version} kann installiert werden (Einstellungen › Updates).");
            }
        };
        UpdateCoordinator.Start();

        // After sleep, a network change or a new Wi-Fi the cloud is asked at once.
        NetworkChange.NetworkAvailabilityChanged += (_, availability) =>
        {
            if (availability.IsAvailable)
            {
                SyncService.Kick();
            }
        };
        NetworkChange.NetworkAddressChanged += (_, _) => SyncService.Kick();

        SyncService.StatusChanged += (_, status) =>
        {
            if (status.State == CloudState.SignedOut)
            {
                Ui.Post(() => _trayIcon?.ShowNotification("ThixCloud", status.Message, isWarning: true));
            }
        };
    }

    /// <summary>Registers the global shortcut of the settings (again after a change).</summary>
    public bool RegisterHotkey() => HotkeyService.Apply(SettingsService.Get().Hotkey, OnHotkeyPressed);

    /// <summary>The shortcut (Windows: a thread-pool thread, macOS: the main thread): show or hide the launcher.</summary>
    private void OnHotkeyPressed()
    {
        if (_launcher is null || _isExiting)
        {
            return;
        }

        Ui.Post(() => _launcher.Toggle());
    }

    private LauncherWindow CreateLauncher()
    {
        var launcher = new LauncherWindow();
        launcher.RebuildRequested += (_, _) => RebuildLauncher(launcher);
        return launcher;
    }

    /// <summary>A theme of another kind of window (glass, shadow, flat): a new launcher replaces the old one, showing what it showed.</summary>
    private void RebuildLauncher(LauncherWindow old)
    {
        if (_isExiting || _launcher != old)
        {
            return;
        }

        var wasVisible = old.IsVisible;
        var query = old.IsInTerminal ? null : old.View.SearchBox.Text;
        _launcher = CreateLauncher();
        _launcher.Prewarm();
        old.IsExiting = true;
        old.Close();
        if (wasVisible)
        {
            Dispatcher.UIThread.Post(() => _launcher.ShowLauncher(query), DispatcherPriority.Background);
        }
    }

    /// <summary>Shows the launcher (tray, the settings window, the shortcut), optionally with a search.</summary>
    public void ShowLauncher(string? query = null)
    {
        if (_launcher is not null && !_isExiting)
        {
            _launcher.ShowLauncher(query);
        }
    }

    /// <summary>A command of ThixFred: from the launcher ("ThixFred: Store"), the tray or a second start.</summary>
    public void RunCommand(AppCommand command)
    {
        if (_mainWindow is null || _isExiting)
        {
            return;
        }

        switch (command)
        {
            case AppCommand.Quit:
                ExitApplication();
                return;
            case AppCommand.Reindex:
                _ = AppIndex.Rescan();
                return;
            case AppCommand.CheckForUpdates:
                _mainWindow.ShowSettings(Pages.SettingsPage.UpdatesTab);
                _mainWindow.ShowOn(null);
                _ = _mainWindow.SettingsPage.CheckForUpdatesAsync();
                return;
            default:
                _mainWindow.ShowOn(command);
                return;
        }
    }

    /// <summary>The settings window on a tab (tray, notifications).</summary>
    public void ShowSettings(string? tab = null)
    {
        if (_mainWindow is null || _isExiting)
        {
            return;
        }

        _mainWindow.ShowSettings(tab);
        _mainWindow.ShowOn(null);
    }

    /// <summary>A notification from anywhere in the app (store, plugins).</summary>
    public void Notify(string title, string text, bool isWarning = false) => _trayIcon?.ShowNotification(title, text, isWarning);

    private async Task CopyToClipboardAsync(string text)
    {
        var clipboard = (_launcher ?? (TopLevel?)_mainWindow)?.Clipboard;
        if (clipboard is null)
        {
            throw new BusinessException("Die Zwischenablage ist nicht verfügbar.");
        }

        await clipboard.SetTextAsync(text);
    }

    /// <summary>The setting "Erscheinungsbild": Wie das System, Hell, Dunkel (DESK-02).</summary>
    public static void ApplyTheme(ThemeChoice theme) => MatTheme.SetAppearance(theme switch
    {
        ThemeChoice.Light => MatAppearance.Light,
        ThemeChoice.Dark => MatAppearance.Dark,
        _ => MatAppearance.System,
    });

    /// <summary>"Beenden": ends the application.</summary>
    public void ExitApplication()
    {
        if (_isExiting)
        {
            return;
        }

        _isExiting = true;
        UiWatchdog.Stop();
        Log.Info("ThixFred exiting");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        void Step(string name, Action action)
        {
            var started = watch.ElapsedMilliseconds;
            action();
            if (watch.ElapsedMilliseconds - started > 300)
            {
                Log.Warning($"Exit step {name} took {watch.ElapsedMilliseconds - started} ms");
            }
        }

        Step("sync", SyncService.Stop);
        Step("shortcut", HotkeyService.Unregister);
        Step("program index", AppIndex.Stop);
        Step("usage", UsageService.Flush);
        Step("tray icon", () => _trayIcon?.Dispose());
        if (_launcher is not null)
        {
            _launcher.IsExiting = true;
            Step("launcher", _launcher.Close);
        }

        if (_mainWindow is not null)
        {
            _mainWindow.IsExiting = true;
            Step("settings window", _mainWindow.Close);
        }
        _instance?.Dispose();
        Log.Info($"ThixFred ended after {watch.ElapsedMilliseconds} ms");
        _desktop?.Shutdown(0);
    }

    /// <summary>
    /// The command line of SETUP-05 with the install options of SETUP-08 (InstallerOptions; a wrong value: exit code 2).
    /// Windows: the published ThixFred.exe is its own setup (InstallService):
    ///  - "--uninstall" (from "Installierte Apps"): the uninstall assistant, or silently with "--quiet".
    ///  - "--quiet [options]" (ThixLauncher, ThixInstall, an administrator): installs without a window; "--start" starts it in the tray.
    ///  - "--update [options]" (downloaded by the update check): installs itself silently and starts the installed ThixFred in the tray.
    ///  - started anywhere else than the program folder (e.g. from Downloads): the setup assistant.
    /// Both systems: "--configure [options]" applies the options to the installed ThixFred and ends (macOS: ThixLauncher calls
    /// it after it copied the bundle). A development build (not single-file) runs as the application otherwise.
    /// </summary>
    private bool RunSetupIfRequested(string[] args)
    {
        SetupRequest request;
        try
        {
            request = InstallerOptions.Parse(args);
        }
        catch (BusinessException exception)
        {
            Log.Warning($"Setup arguments refused: {exception.Message}");
            Console.Error.WriteLine(exception.Message);
            EndProcess(2);
            return true;
        }

        if (request.Action == SetupAction.Configure)
        {
            InstallerOptions.Apply(request.Choices, OperatingSystem.IsWindows() && File.Exists(InstallService.InstalledExecutable) ? InstallService.InstalledExecutable : ExecutablePath, Log.Info);
            EndProcess(0);
            return true;
        }

        return OperatingSystem.IsWindows() && RunWindowsSetup(request);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private bool RunWindowsSetup(SetupRequest request)
    {
        if (request.Action == SetupAction.Uninstall)
        {
            if (request.Quiet)
            {
                if (InstallService.Uninstall(deleteUserData: false, Log.Info))
                {
                    InstallService.ScheduleProgramFolderRemoval();
                }

                EndProcess(0);
                return true;
            }

            Setup.ThixFredSetup.Run(isUninstall: true, removeProgramFolder =>
            {
                if (removeProgramFolder)
                {
                    InstallService.ScheduleProgramFolderRemoval();
                }

                EndProcess(0);
            });
            return true;
        }

        if (request.Action is SetupAction.Install or SetupAction.Update)
        {
            var exitCode = 0;
            try
            {
                InstallService.Install(ExecutablePath, request.Choices, Log.Info);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException
                                                  or System.Runtime.InteropServices.COMException or System.Security.SecurityException)
            {
                Log.Error(request.Action == SetupAction.Update ? "Update failed" : "Quiet install failed", exception);
                Console.Error.WriteLine(exception.Message);
                exitCode = 1;
            }

            // an update starts ThixFred again; a quiet install only with "--start"
            if ((request.Action == SetupAction.Update || request.Start) && File.Exists(InstallService.InstalledExecutable))
            {
                InstallService.StartInstalled(TrayArgument);
            }

            EndProcess(exitCode);
            return true;
        }

#if SINGLE_FILE
        const bool isPublishedBuild = true;
#else
        const bool isPublishedBuild = false;
#endif
        if (isPublishedBuild && !InstallService.IsInstalledExecutable(ExecutablePath))
        {
            Setup.ThixFredSetup.Run(isUninstall: false, _ => EndProcess(0));
            return true;
        }

        return false;
    }

    /// <summary>
    /// One instance per user and data folder (MatSingleInstance, DESK-05: a named mutex and a pipe, on macOS a lock file
    /// and a socket): a second start hands its arguments to the first one, which shows its window, and ends. A development
    /// run with its own THIXFRED_DATA_DIR does not meet the installed one. On Windows the setup may also ask the running
    /// instance to end (ListenForExitSignal).
    /// </summary>
    private bool TakeSingleInstance()
    {
        _instance = new MatSingleInstance(AppInfo.ProductName, AppPaths.HasOverride ? AppPaths.DataDirectory : null);
        _instance.SecondStart += (_, _) => Ui.Post(() => ShowLauncher());
        if (!_instance.TryClaim(_desktop?.Args ?? []))
        {
            Log.Info("ThixFred runs already: its window was asked to show");
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            ListenForExitSignal();
        }

        return true;
    }

    /// <summary>The setup asks a running ThixFred to end (update, uninstall).</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private void ListenForExitSignal()
    {
        if (AppPaths.HasOverride)
        {
            return; // a development run with its own data folder is none of the setup's business
        }

        var exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstallService.ExitSignalName);
        var listener = new Thread(() =>
        {
            exitSignal.WaitOne();
            Ui.Post(() =>
            {
                Log.Info("Setup asked ThixFred to end");
                ExitApplication();
            });
        })
        {
            IsBackground = true,
            Name = "ThixFred exit signal listener",
        };
        listener.Start();
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled error", e.Exception);
        e.Handled = true;
        if (e.Exception is BusinessException business)
        {
            _trayIcon?.ShowNotification("ThixFred", business.Message, isWarning: true);
            return;
        }

        _trayIcon?.ShowNotification("Unerwarteter Fehler", $"{e.Exception.Message} – Details im Protokoll ({AppPaths.LogDirectory}).", isWarning: true);
    }
}
