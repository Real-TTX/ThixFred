using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MatDesktop;
using ThixFred.Core.Business;
using ThixFred.Core.Infrastructure;
using ThixFred.Core.Models;

namespace ThixFred.Tray;

/// <summary>
/// The icon in the notification area (Windows) or the menu bar (macOS), with MatDesktop's tray (TRAY-01 … TRAY-04): the menu in
/// the order of every Mat tray – "ThixFred 1.0.0", the state, "ThixFred öffnen" (the launcher, with its shortcut), ThixFred's
/// own items (Plugins, Store, Themes; Programme neu einlesen, Jetzt synchronisieren), "Einstellungen …", "Log-Ordner öffnen",
/// "Update installieren …" while one waits, "Beenden". Windows: a left click opens the launcher, the right button the menu;
/// macOS: every click opens the menu.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly App _app;
    private readonly MatTrayMenu _menu;
    private readonly MatTray _tray;
    private bool _updateAvailable;
    private bool _refreshPosted;

    public TrayIcon(App app)
    {
        _app = app;
        _menu = new MatTrayMenu(AppInfo.ProductName, AppInfo.Version, () => _app.ExitApplication())
        {
            Open = () => _app.ShowLauncher(),
            Settings = () => _app.RunCommand(AppCommand.Settings),
            LogFolder = () => Ui.OpenExternal(AppPaths.LogDirectory),
        };
        Update();
        _tray = new MatTray(app, _menu, Mark());
        _tray.MenuOpening += (_, _) => Update();
        SettingsService.Changed += OnChanged;
        SyncService.StatusChanged += OnChanged;
    }

    public MatTrayMenu Menu => _menu;

    /// <summary>The mark the tray icons are drawn from: the logo on Windows, the template image (black on transparent) on macOS.</summary>
    public static Bitmap Mark(bool? template = null) =>
        new(AssetLoader.Open(new Uri((template ?? OperatingSystem.IsMacOS()) ? "avares://ThixFred/Assets/tray-macos.png" : "avares://ThixFred/Assets/logo-64.png")));

    public void SetUpdateAvailable(bool available)
    {
        _updateAvailable = available;
        Refresh();
    }

    /// <summary>A notification of ThixFred's own (TRAY-04: only for what the user did not cause); a click opens the settings.</summary>
    public void ShowNotification(string title, string text, bool isWarning = false) =>
        MatNotification.Show(title, text, isWarning, () => _app.RunCommand(AppCommand.Settings));

    public void Dispose()
    {
        SettingsService.Changed -= OnChanged;
        SyncService.StatusChanged -= OnChanged;
        _tray.Dispose();
    }

    private void OnChanged(object? sender, object? e)
    {
        if (_refreshPosted)
        {
            return;
        }

        _refreshPosted = true;
        Ui.Post(() =>
        {
            _refreshPosted = false;
            Refresh();
        });
    }

    private void Refresh()
    {
        Update();
        _tray.Refresh();
    }

    private void Update()
    {
        var settings = SettingsService.Get();
        _menu.State = MatState.Running;
        _menu.OpenShortcut = settings.Hotkey; // at the right of "ThixFred öffnen" (DESK-07)
        _menu.TooltipText = $"Öffnen mit {HotkeyService.Display(settings.Hotkey)}";
        _menu.InstallUpdate = _updateAvailable ? () => _app.ShowSettings(Pages.SettingsPage.UpdatesTab) : null;
        _menu.ClearOwn();
        _menu.AddOwn("Plugins und Skripte …", () => _app.RunCommand(AppCommand.Plugins));
        _menu.AddOwn("Store …", () => _app.RunCommand(AppCommand.Store));
        _menu.AddOwn("Themes …", () => _app.RunCommand(AppCommand.Themes));
        _menu.AddOwnSeparator();
        _menu.AddOwn("Programme neu einlesen", () => _ = AppIndex.Rescan());
        if (settings.Cloud.IsConnected)
        {
            _menu.AddOwn("Jetzt synchronisieren", SyncService.Kick);
        }
    }
}
