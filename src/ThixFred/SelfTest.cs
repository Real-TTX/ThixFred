using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using MatCloud.Contract;
using ThixFred.Core.Business;
using ThixFred.Core.Infrastructure;
using ThixFred.Core.Models;
using ThixFred.Core.Platform;

namespace ThixFred;

/// <summary>
/// "ThixFred.exe --self-test &lt;report file&gt;": the published program checks itself – everything trimming or single-file
/// packaging could break silently: the JSON of settings, themes, sync and update manifest, the protected token store, Windows
/// shortcuts and icons (COM), the PNG writer, the calculator, a script run, the program index and the windows with their
/// bindings. It runs in a fresh temporary data folder (Program sets it), never touches the user's data, writes a report and
/// ends with exit code 0 (all passed) or 1. CI runs it on every Windows build.
/// </summary>
internal static class SelfTest
{
    public const string Argument = "--self-test";

    public static async Task<int> RunAsync(string reportPath)
    {
        var report = new StringBuilder($"ThixFred {AppInfo.Version} self test, data folder {AppPaths.DataDirectory}{Environment.NewLine}");
        var failures = 0;

        async Task Check(string name, Func<Task<string>> check)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                var note = await check();
                report.AppendLine($"ok    {name} ({watch.ElapsedMilliseconds} ms){(note.Length > 0 ? " – " + note : "")}");
            }
            catch (Exception exception)
            {
                failures++;
                report.AppendLine($"FAIL  {name}: {exception}");
            }
        }

        await Check("settings (JSON)", () =>
        {
            SettingsService.Update(settings => settings with { MaxResults = 7, Theme = ThemeChoice.Dark, Terminal = TerminalChoice.CommandPrompt });
            SettingsService.RememberLook("plugins", ListLook.List);
            SettingsStore.Reset();
            var read = SettingsService.Get();
            Ensure(read is { MaxResults: 7, Theme: ThemeChoice.Dark, Terminal: TerminalChoice.CommandPrompt } && SettingsService.LookOf("plugins") == ListLook.List, "settings did not survive a save");
            return Task.FromResult("");
        });

        await Check("themes, web searches, scripts (JSON, files)", () =>
        {
            var theme = ThemeService.Duplicate("ozean");
            ThemeService.ResetForTests();
            Ensure(ThemeService.Find(theme.Id) is { Accent: "#FF38BDF8" }, "theme not read back");
            WebSearchService.Save(new WebSearch { Id = "", Name = "Selbsttest", Keyword = "st", Url = "https://example.com/?q={query}" });
            WebSearchService.ResetForTests();
            Ensure(WebSearchService.FindByKeyword("st") is not null, "web search not read back");
            var script = ScriptService.Save(ScriptService.Template(ScriptService.Languages[0]) with { Name = "Selbsttest", Keyword = "selbsttest" });
            Ensure(ScriptService.Find(script.Id) is { Keyword: "selbsttest" }, "script not read back");
            Ensure(PluginService.MatchKeyword("selbsttest x") is { Argument: "x" }, "script not found by its keyword");
            return Task.FromResult("");
        });

        await Check("update manifest (JSON)", () =>
        {
            const string json = """{"version":"0.1.9-20261115","channel":"release","buildNumber":9,"setup":"ThixFredSetup.exe","sha256":"AB","packages":{"win-x64":{"file":"ThixFredSetup.exe","sha256":"AB"}}}""";
            var manifest = new HttpResponseMessage { Content = new StringContent(json, Encoding.UTF8, "application/json") }.Content
                .ReadFromJsonAsync<UpdateManifest>().GetAwaiter().GetResult();
            Ensure(manifest is { BuildNumber: 9 } && UpdateService.PackageFor(manifest, "win-x64")?.File == "ThixFredSetup.exe", "manifest not read");
            return Task.FromResult("");
        });

        await Check("MatCloud contract (JSON)", () =>
        {
            var item = new SyncItemDto { Collection = "themes", Key = "ozean", Data = "{}", UpdatedAt = DateTimeOffset.UtcNow, Revision = 3 };
            var json = JsonSerializer.Serialize(new SyncChangeSet([item], 3, false), CloudContract.Json);
            var back = JsonSerializer.Deserialize<SyncChangeSet>(json, CloudContract.Json);
            Ensure(back is { Latest: 3 } && back.Items[0].Key == "ozean", "change set not read back");
            return Task.FromResult("");
        });

        await Check("install options (installer.json, arguments)", () =>
        {
            Ensure(InstallerOptions.Config is { Autostart: not null, DesktopShortcut: not null }, "installer.json not read");
            var request = InstallerOptions.Parse([InstallerOptions.QuietArgument, "--autostart=no", "--desktop-shortcut=yes"]);
            Ensure(request is { Action: SetupAction.Install, Choices: { Autostart: false, DesktopShortcut: true } }, "arguments not read");
            return Task.FromResult("");
        });

        await Check("token store", () =>
        {
            var stored = TokenStore.Save("self-test", "geheim");
            Ensure(TokenStore.Read("self-test", stored) == "geheim", "token not read back");
            TokenStore.Delete("self-test");
            return Task.FromResult("");
        });

        await Check("calculator and search", () =>
        {
            Ensure(Calculator.TryEvaluate("3,5 * (2 + 1)^2") == 31.5, "calculation wrong");
            Ensure(SearchService.Search(">ping heise.de", 8) is [{ Kind: ResultKind.Terminal }], "terminal result missing");
            Ensure(SearchService.Search("2+2", 8).First().Title.EndsWith('4'), "calculation result missing");
            return Task.FromResult("");
        });

        await Check("program index", async () =>
        {
            await AppIndex.Rescan();
            return $"{AppIndex.Entries.Count} entries";
        });

        if (OperatingSystem.IsWindows())
        {
            await Check("shortcut and icon (COM, PNG)", async () =>
            {
                var link = Path.Combine(AppPaths.DataDirectory, "ThixFred.lnk");
                Core.Platform.Windows.ShellLink.Write(link, App.ExecutablePath, AppPaths.DataDirectory, "Selbsttest", App.ExecutablePath);
                Ensure(File.Exists(link) && new FileInfo(link).Length > 0, "no .lnk written");
                Ensure(string.Equals(Core.Platform.Windows.ShellLink.ReadTarget(link), App.ExecutablePath, StringComparison.OrdinalIgnoreCase), "target of the .lnk not read");
                var icon = await IconService.LoadAsync(App.ExecutablePath);
                Ensure(icon is not null && File.ReadAllBytes(icon).AsSpan(1, 3).SequenceEqual("PNG"u8), "no icon extracted");
                return "";
            });

            await Check("script (cmd)", async () =>
            {
                var script = Path.Combine(AppPaths.DataDirectory, "selbsttest.cmd");
                await File.WriteAllTextAsync(script, "@echo off\r\necho Hallo %~1\r\n");
                var result = await ScriptRunner.RunAsync(script, "Welt", TimeSpan.FromSeconds(20), isAction: true, CancellationToken.None);
                Ensure(result.Succeeded && result.Output.Trim() == "Hallo Welt", $"output \"{result.Output.Trim()}\" (exit {result.ExitCode})");
                return "";
            });
        }

        await Check("integrated terminal (shell)", async () =>
        {
            IntegratedTerminal.UseWorkingDirectoryForTests(AppPaths.DataDirectory);
            await IntegratedTerminal.RunAsync("echo Grüße");
            Ensure(IntegratedTerminal.LastExitCode == 0 && IntegratedTerminal.Output.Contains("\nGrüße", StringComparison.Ordinal),
                $"output \"{IntegratedTerminal.Output.Trim()}\" (exit {IntegratedTerminal.LastExitCode})");
            IntegratedTerminal.Clear();
            return "";
        });

        await Check("windows and bindings", async () =>
        {
            var launcher = new Launcher.LauncherWindow();
            launcher.ShowLauncher("2+2");
            await Settle();
            Ensure(launcher.View.Results.Count > 0 && launcher.View.Results[0].Kind == ResultKind.Calculation, "launcher shows no calculation");
            launcher.IsExiting = true;
            launcher.Close();

            var window = new MainWindow();
            window.ShowOn(AppCommand.Plugins);
            await Settle();
            Ensure(window.PluginsPage.List.Rows.Count > 0, "no plugin listed");
            window.ShowOn(AppCommand.Themes);
            await Settle();
            window.ShowOn(AppCommand.WebSearches);
            await Settle();
            window.ShowSettings();
            await Settle();
            window.ShowAbout();
            await Settle();
            if (OperatingSystem.IsWindows())
            {
                var setup = Setup.ThixFredSetup.Create(isUninstall: false, InstallService.IsInstalled, InstallService.InstalledVersion, InstallService.ProgramFolder, _ => Task.CompletedTask);
                setup.Show();
                await Settle();
                setup.Close();
            }

            ProblemReports.WriteHang(TimeSpan.FromSeconds(9), withDump: false);
            Dialogs.ProblemReportDialog.ShowUnseen()?.Close();
            window.IsExiting = true;
            window.Close();
            return "";
        });

        report.AppendLine(failures == 0 ? "PASSED" : $"FAILED ({failures})");
        await File.WriteAllTextAsync(reportPath, report.ToString(), Encoding.UTF8);

        // The temporary data folder goes again (unless the report was written into it).
        if (!Path.GetFullPath(reportPath).StartsWith(Path.GetFullPath(AppPaths.DataDirectory), StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Directory.Delete(AppPaths.DataDirectory, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return failures == 0 ? 0 : 1;
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static async Task Settle()
    {
        for (var round = 0; round < 10; round++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(30);
        }
    }
}
