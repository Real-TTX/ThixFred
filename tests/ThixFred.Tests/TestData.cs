using ThixFred.Core.Business;
using ThixFred.Core.Infrastructure;
using ThixFred.Core.Platform;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ThixFred.Tests;

/// <summary>
/// Every test gets its own data folder, a token store in memory, no machine policies, no icons of the computer, a fixed program
/// index (no scan of the real Start menu) and no running program of its own. Nothing a test does touches the real settings,
/// scripts, Keychain, registry or programs; no address, folder, program or terminal opens (Shell and Ui record instead).
/// </summary>
internal sealed class TestData : IDisposable
{
    /// <param name="folder">A data folder of the test's own that stays after the test (another computer that comes back); null: a new one.</param>
    public TestData(string? folder = null)
    {
        _keepFolder = folder is not null;
        Folder = folder ?? Path.Combine(Path.GetTempPath(), "thixfred-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        AppPaths.UseDirectoryForTests(Folder);
        SettingsStore.Reset();
        TokenStore.UseMemoryForTests(true);
        AccessPolicy.UsePolicyReaderForTests(name => Policies.Contains(name));
        IconService.IsDisabledForTests = true;
        AppIndex.ScannerForTests = () => SampleData.Apps;
        AppIndex.SetEntriesForTests(SampleData.Apps);
        ProcessService.ListForTests = () => Running;
        ProcessService.EndForTests = (id, force) => Ended.Add((id, force));
        Shell.StartForTests = (program, arguments) => Started.Add((program, arguments));
        ScriptRunner.RunForTests = null;
        StoreService.HandlerForTests = null;
        StoreService.ListingsForTests = () => [];
        StoreService.ResetForTests();
        SyncService.HandlerForTests = null;
        SyncService.ResetForTests();
        UsageService.ResetForTests();
        WebSearchService.ResetForTests();
        ThemeService.ResetForTests();
        IntegratedTerminal.ResetForTests();
        IntegratedTerminal.RunForTests = (command, _, write, _) =>
        {
            TerminalCommands.Add(command);
            write($"(Test) {command}\n");
            return Task.FromResult(0);
        };
        StoreService.ResetForTests();
        PluginService.Invalidate();
        LaunchService.Clipboard = text =>
        {
            Clipboard = text;
            return Task.CompletedTask;
        };
        Ui.OpenExternalForTests = _ => { }; // never a real browser or folder
    }

    private readonly bool _keepFolder;

    public string Folder { get; }

    /// <summary>The machine policies switched on for this test (LockConfiguration, DisableScripts …).</summary>
    public HashSet<string> Policies { get; } = [];

    /// <summary>The programs "running" now.</summary>
    public List<RunningApp> Running { get; } = [new RunningApp(4711, "Microsoft Word", "Angebot.docx – Word", null), new RunningApp(4712, "Spotify", "Spotify Premium", null)];

    /// <summary>The processes the test closed (id, at once).</summary>
    public List<(int Id, bool Force)> Ended { get; } = [];

    /// <summary>What would have been started (program or address, arguments).</summary>
    public List<(string Program, IReadOnlyList<string> Arguments)> Started { get; } = [];

    /// <summary>What Shell.Open opened last: the target itself on Windows, the argument of /usr/bin/open on macOS.</summary>
    public string LastOpened => Started.Last() is var (program, arguments) && program == "/usr/bin/open" ? arguments[0] : Started.Last().Program;

    /// <summary>What went into the clipboard last.</summary>
    public string? Clipboard { get; private set; }

    /// <summary>The commands the integrated terminal ran (no shell runs in a test unless it asks for one).</summary>
    public List<string> TerminalCommands { get; } = [];

    public void Dispose()
    {
        SyncService.Stop();
        SyncService.HandlerForTests = null;
        SyncService.LoopDisabledForTests = false;
        StoreService.HandlerForTests = null;
        StoreService.ListingsForTests = null;
        ScriptRunner.RunForTests = null;
        Shell.StartForTests = null;
        IntegratedTerminal.ResetForTests();
        ProcessService.ListForTests = null;
        ProcessService.EndForTests = null;
        AppIndex.ScannerForTests = null;
        AppIndex.Stop();
        IconService.IsDisabledForTests = false;
        LaunchService.Clipboard = null;
        Ui.OpenExternalForTests = null;
        AccessPolicy.UsePolicyReaderForTests(null);
        TokenStore.UseMemoryForTests(false);
        UsageService.ResetForTests();
        WebSearchService.ResetForTests();
        ThemeService.ResetForTests();
        SettingsStore.Reset();
        AppPaths.UseDirectoryForTests(null);
        PluginService.Invalidate();
        if (_keepFolder)
        {
            return;
        }

        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
