using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using MatCloud.Contract;
using MatDesktop;
using ThixFred.Core.Business;
using ThixFred.Core.Infrastructure;
using ThixFred.Core.Models;

namespace ThixFred;

/// <summary>
/// "ThixFred --snapshot &lt;folder&gt;": draws the windows of ThixFred into PNG files without a screen (Avalonia headless
/// with Skia, the real windows, themes and fonts of the machine), in light and dark, at 1x and their natural size – the
/// pictures of docs/images and the README. It works in a temporary data folder (Program sets it) filled with the sample
/// computer (SampleData: programs, scripts, a store plugin, a theme), with a pinned clock, version 1.0.0 on the release channel
/// and a fixed computer name: no real program, icon, shortcut, tray icon, registry value, network or update check is touched.
/// </summary>
internal static class Snapshot
{
    public const string Argument = "--snapshot";

    /// <summary>What every picture shows as version, channel and computer.</summary>
    public const string Version = "1.0.0";

    public const string Channel = "release";

    public const string MachineName = "ANNA-PC";

    /// <summary>"Now" in every picture (local time): Thursday, 12 November 2026, 14:32.</summary>
    public static readonly DateTimeOffset Moment = new(new DateTime(2026, 11, 12, 14, 32, 0, DateTimeKind.Local));

    private const string ProgramFolder = @"C:\Users\Anna\AppData\Local\Programs\ThixFred";
    private const string DataFolder = @"C:\Users\Anna\AppData\Local\ThixFred";
    private const string CloudAddress = "https://cloud.example.com";

    private static readonly string[] Themes = ["light", "dark"];

    private static readonly string[] ThemedPictures =
    [
        "setup-welcome", "setup-options", "setup-progress", "setup-finish", "setup-update", "setup-uninstall",
        "launcher-search", "launcher-calc", "launcher-terminal", "launcher-console", "launcher-kill", "launcher-recent",
        "main-plugins", "main-script", "main-store", "main-themes", "main-theme-edit", "main-websearches", "main-settings", "main-search",
        "main-cloud", "main-cloud-code", "main-about", "toast",
    ];

    /// <summary>The other built-in themes, drawn once each.</summary>
    private static readonly string[] ThemeNames = ["graphit", "ozean", "mitternacht", "sand", "kompakt", "kante", "kante-hell"];

    /// <summary>Every file a snapshot writes (the tray icons are the same in both themes).</summary>
    public static IReadOnlyList<string> FileNames { get; } =
    [
        .. ThemedPictures.SelectMany(name => Themes.Select(theme => $"{name}-{theme}.png")),
        .. ThemeNames.Select(name => $"launcher-theme-{name}.png"),
        "tray-icon-windows.png", "tray-icon-macos.png",
    ];

    /// <summary>The command: sets Avalonia up headless, draws every picture into the folder, ends with 0 (all written) or 1.</summary>
    public static int Run(string[] args)
    {
        var index = Array.IndexOf(args, Argument);
        if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"Usage: ThixFred {Argument} <folder>");
            return 2;
        }

        var folder = Path.GetFullPath(args[index + 1]);
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
        AvaloniaSynchronizationContext.InstallIfNeeded();

        int exitCode;
        try
        {
            var report = Render(folder);
            foreach (var file in report.Written)
            {
                Console.WriteLine($"written  {Path.Combine(folder, file)}");
            }

            foreach (var (file, reason) in report.Skipped)
            {
                Console.WriteLine($"skipped  {file}: {reason}");
            }

            exitCode = report.Written.Count + report.Skipped.Count == FileNames.Count ? 0 : 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Snapshot failed: {exception}");
            exitCode = 1;
        }

        // The temporary data folder goes again.
        try
        {
            Directory.Delete(AppPaths.DataDirectory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return exitCode;
    }

    /// <summary>
    /// Draws every picture into <paramref name="folder"/> (Avalonia must run headless, the data folder must be a fresh
    /// one: the command and the UI tests take care of both). Existing files of the same names are replaced.
    /// </summary>
    public static SnapshotReport Render(string folder)
    {
        Directory.CreateDirectory(folder);
        var report = new SnapshotReport();
        var previousTheme = Application.Current!.RequestedThemeVariant;
        using (Isolate())
        {
            SampleData.Create();
            foreach (var theme in Themes)
            {
                Application.Current.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
                RenderSetup(folder, theme, report);
                RenderLauncher(folder, theme, report);
                RenderMainWindow(folder, theme, report);
                RenderToast(folder, theme, report);
            }

            RenderLauncherThemes(folder, report);
            RenderTrayIcons(folder, report);
        }

        Application.Current.RequestedThemeVariant = previousTheme;
        return report;
    }

    private static void RenderSetup(string folder, string theme, SnapshotReport report)
    {
        foreach (var page in Enum.GetValues<SetupSnapshotPage>())
        {
            var name = $"setup-{page.ToString().ToLowerInvariant()}";
            if (!OperatingSystem.IsWindows())
            {
                report.Skipped.Add(($"{name}-{theme}.png", "the setup assistant exists on Windows only (macOS: the app bundle and the dmg)"));
                continue;
            }

            RenderSetupPage(folder, $"{name}-{theme}.png", page, report);
        }
    }

    /// <summary>
    /// One page of the setup with fixed values (MatSetupWindow, ThixFredSetup.Create): nothing is installed, removed or asked
    /// from the system. Progress shows the steps of an installation two thirds along (the running bar would show wherever its
    /// animation happens to be), Finish the end of one that worked.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void RenderSetupPage(string folder, string file, SetupSnapshotPage page, SnapshotReport report)
    {
        // An update brings 1.0.0 over 0.9.0; the uninstall removes the installed 1.0.0.
        var installed = page == SetupSnapshotPage.Update ? "0.9.0" : Version;
        var hold = new TaskCompletionSource();
        var window = Setup.ThixFredSetup.Create(page == SetupSnapshotPage.Uninstall, page == SetupSnapshotPage.Update, installed, ProgramFolder, async run =>
        {
            if (page != SetupSnapshotPage.Progress)
            {
                return;
            }

            run.Progress.Report(new MatSetupReport($"Installiere ThixFred nach {ProgramFolder}", MatSetupStepState.Done));
            run.Progress.Report(new MatSetupReport("Programmdatei kopiert", MatSetupStepState.Done));
            run.Progress.Report(new MatSetupReport("Startmenü-Eintrag angelegt", MatSetupStepState.Done));
            run.Progress.Report(new MatSetupReport("Autostart bei der Windows-Anmeldung eingerichtet", MatSetupStepState.Running, 65));
            await hold.Task;
        });
        window.Show();
        Settle(window);
        if (page == SetupSnapshotPage.Options)
        {
            window.GoTo(MatSetupStepKind.Options);
        }
        else if (page is SetupSnapshotPage.Progress or SetupSnapshotPage.Finish)
        {
            _ = window.RunAsync();
        }

        Settle(window);
        Save(window, folder, file, report);
        hold.TrySetResult();
        Settle(window);
        window.Close();
    }

    /// <summary>The launcher with the sample index: a search, a calculation, a terminal command, closing a program, the empty search.</summary>
    private static void RenderLauncher(string folder, string theme, SnapshotReport report)
    {
        SettingsService.Update(settings => settings with { LauncherThemeId = theme == "dark" ? ThemeService.DarkId : ThemeService.LightId });
        var launcher = new Launcher.LauncherWindow();
        foreach (var (name, query) in new[] { ("search", "vis"), ("calc", "3,5 * (2 + 1)^2"), ("terminal", ">ping heise.de"), ("kill", "kill"), ("recent", "") })
        {
            launcher.ShowLauncher(query);
            Settle(launcher, waitLonger: true);
            Save(launcher, folder, $"launcher-{name}-{theme}.png", report);
        }

        // the integrated terminal after a command: the output is made up, no process runs
        IntegratedTerminal.ResetForTests();
        IntegratedTerminal.UseWorkingDirectoryForTests(OperatingSystem.IsWindows() ? @"C:\Users\Anna" : "/Users/anna/Projekte");
        IntegratedTerminal.RunForTests = (_, _, write, _) =>
        {
            write(PingOutput);
            return Task.FromResult(0);
        };
        launcher.ShowLauncher(">");
        _ = launcher.RunInTerminalAsync("ping heise.de");
        Settle(launcher, waitLonger: true);
        Save(launcher, folder, $"launcher-console-{theme}.png", report);
        IntegratedTerminal.ResetForTests();

        launcher.IsExiting = true;
        launcher.Close();
        SettingsService.Update(settings => settings with { LauncherThemeId = LauncherTheme.SystemId });
    }

    private const string PingOutput = """

        Ping wird ausgeführt für heise.de [193.99.144.80] mit 32 Bytes Daten:
        Antwort von 193.99.144.80: Bytes=32 Zeit=12ms TTL=246
        Antwort von 193.99.144.80: Bytes=32 Zeit=11ms TTL=246
        Antwort von 193.99.144.80: Bytes=32 Zeit=12ms TTL=246
        Antwort von 193.99.144.80: Bytes=32 Zeit=13ms TTL=246

        Ping-Statistik für 193.99.144.80:
            Pakete: Gesendet = 4, Empfangen = 4, Verloren = 0
            (0% Verlust),
        Ca. Zeitangaben in Millisek.:
            Minimum = 11ms, Maximum = 13ms, Mittelwert = 12ms

        """;

    /// <summary>The other themes of the launcher, each with the search for "vis" (light pictures only: a theme has its own colours).</summary>
    private static void RenderLauncherThemes(string folder, SnapshotReport report)
    {
        foreach (var id in ThemeNames)
        {
            SettingsService.Update(settings => settings with { LauncherThemeId = id });
            var launcher = new Launcher.LauncherWindow();
            launcher.ShowLauncher("vis");
            Settle(launcher, waitLonger: true);
            Save(launcher, folder, $"launcher-theme-{id}.png", report);
            launcher.IsExiting = true;
            launcher.Close();
        }

        SettingsService.Update(settings => settings with { LauncherThemeId = LauncherTheme.SystemId });
    }

    private static void RenderMainWindow(string folder, string theme, SnapshotReport report)
    {
        var settings = SettingsService.Get();
        var window = new MainWindow();
        window.ShowOn(AppCommand.Plugins);
        window.Width = settings.WindowWidth;
        window.Height = settings.WindowHeight;
        Settle(window, waitLonger: true);
        Save(window, folder, $"main-plugins-{theme}.png", report);

        window.OpenPage(new Pages.ScriptEditPage(window, ScriptService.Find("meine-ip")!));
        Settle(window);
        Save(window, folder, $"main-script-{theme}.png", report);
        window.GoBack();

        window.ShowOn(AppCommand.Store);
        Settle(window, waitLonger: true);
        Save(window, folder, $"main-store-{theme}.png", report);

        window.ShowOn(AppCommand.Themes);
        Settle(window, waitLonger: true);
        Save(window, folder, $"main-themes-{theme}.png", report);
        window.OpenPage(new Pages.ThemeEditPage(window, ThemeService.Find("abendrot")!));
        Settle(window, waitLonger: true);
        Save(window, folder, $"main-theme-edit-{theme}.png", report);
        window.GoBack();

        window.ShowOn(AppCommand.WebSearches);
        Settle(window);
        Save(window, folder, $"main-websearches-{theme}.png", report);

        window.ShowSettings();
        Settle(window);
        Save(window, folder, $"main-settings-{theme}.png", report);
        window.ShowSettings(Pages.SettingsPage.SearchTab);
        Settle(window);
        Save(window, folder, $"main-search-{theme}.png", report);

        // The cloud tab: the sign-in in the browser first, then the code it shows while the person confirms it there (no network: a fixed code).
        window.ShowSettings(Pages.SettingsPage.CloudTab);
        var settingsPage = window.SettingsPage;
        settingsPage.ServerUrlBox.Text = CloudAddress;
        Settle(window);
        Save(window, folder, $"main-cloud-{theme}.png", report);
        settingsPage.ShowBrowserCode(new DeviceLinkStart("KXQ4-7MPA", $"{CloudAddress}/Connect?code=KXQ4-7MPA", "", 600, 2));
        Settle(window);
        Save(window, folder, $"main-cloud-code-{theme}.png", report);
        settingsPage.ShowSignInWay(password: false);
        Settle(window);

        // The about page tells this machine's system and folder: the pictures show a typical installation instead.
        window.ShowAbout();
        var about = (Pages.AboutPage)window.CurrentPage!;
        about.SystemText.Text = OperatingSystem.IsMacOS()
            ? $"macOS 15.5 ({UpdateService.PlatformKey}), .NET 10.0.0"
            : $"Microsoft Windows 10.0.26100 ({UpdateService.PlatformKey}), .NET 10.0.0";
        about.DataText.Text = OperatingSystem.IsMacOS() ? "/Users/anna/Library/Application Support/ThixFred" : DataFolder;
        Settle(window);
        Save(window, folder, $"main-about-{theme}.png", report);

        window.IsExiting = true;
        window.Close();
    }

    private static void RenderToast(string folder, string theme, SnapshotReport report)
    {
        var toast = MatNotification.Show("ThixFred ist bereit", "Öffne den Launcher jederzeit mit Alt + Leertaste.");
        Settle(toast);
        Save(toast, folder, $"toast-{theme}.png", report);
        toast.Close();
        Settle(toast);
    }

    /// <summary>
    /// The tray / menu bar icons at 64 px, as MatDesktop draws them (TRAY-02): the mark with the green dot of a running app,
    /// on macOS the template image.
    /// </summary>
    private static void RenderTrayIcons(string folder, SnapshotReport report)
    {
        foreach (var (template, platform) in new[] { (false, "windows"), (true, "macos") })
        {
            var name = $"tray-icon-{platform}.png";
            using var mark = Tray.TrayIcon.Mark(template);
            File.WriteAllBytes(Path.Combine(folder, name), MatTrayIcons.Png(mark, MatState.Running, template));
            report.Written.Add(name);
        }
    }

    /// <summary>Lets the dispatcher, the layout and the render timer run; longer for the background image decoding and animations.</summary>
    private static void Settle(TopLevel topLevel, bool waitLonger = false)
    {
        for (var round = 0; round < (waitLonger ? 40 : 8); round++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(waitLonger ? 40 : 15);
        }

        Dispatcher.UIThread.RunJobs();
        topLevel.UpdateLayout();
    }

    private static void Save(TopLevel topLevel, string folder, string name, SnapshotReport report)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = topLevel.CaptureRenderedFrame() ?? throw new InvalidOperationException($"{name}: no frame was rendered");
        frame.Save(Path.Combine(folder, name));
        report.Written.Add(name);
    }

    /// <summary>Pins clock, version and computer name and keeps tokens, policies, icons and autostart in memory.</summary>
    private static IDisposable Isolate()
    {
        AppClock.Pin(Moment);
        AppInfo.Pin(Version, Channel, buildNumber: 0, MachineName);
        AutostartService.UseInMemoryEntry(true);
        TokenStore.UseMemoryForTests(true);
        AccessPolicy.UsePolicyReaderForTests(_ => false);
        IconService.IsDisabledForTests = true;
        AppIndex.ScannerForTests = () => SampleData.Apps;
        StoreService.ListingsForTests = () => SampleData.StoreListings;
        ProcessService.ListForTests = () => [new RunningApp(4711, "Microsoft Word", "Angebot 2026.docx – Word", null), new RunningApp(4712, "Spotify", "Spotify Premium", null)];
        return new Restore(() =>
        {
            ProcessService.ListForTests = null;
            StoreService.ListingsForTests = null;
            AppIndex.ScannerForTests = null;
            IconService.IsDisabledForTests = false;
            AccessPolicy.UsePolicyReaderForTests(null);
            TokenStore.UseMemoryForTests(false);
            AutostartService.UseInMemoryEntry(null);
            AppInfo.Pin(null);
            AppClock.Pin(null);
        });
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

}

/// <summary>What a snapshot wrote (file names) and what it left out, with the reason.</summary>
internal sealed class SnapshotReport
{
    public List<string> Written { get; } = [];

    public List<(string File, string Reason)> Skipped { get; } = [];
}

/// <summary>The pages of the setup that "ThixFred --snapshot" draws (setup-welcome ... setup-uninstall).</summary>
internal enum SetupSnapshotPage
{
    Welcome,
    Options,
    Progress,
    Finish,
    Update,
    Uninstall,
}
