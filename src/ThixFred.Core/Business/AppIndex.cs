using System.Text.Json.Serialization;
using ThixFred.Core.Infrastructure;
using ThixFred.Core.Models;
using ThixFred.Core.Platform;

namespace ThixFred.Core.Business;

/// <summary>What an entry of the program index is.</summary>
public enum AppEntryKind
{
    /// <summary>A shortcut of the Start menu or the desktop (.lnk, .appref-ms, .url).</summary>
    Shortcut = 0,

    /// <summary>A program file (.exe) of an extra folder.</summary>
    Program = 1,

    /// <summary>An app of the Microsoft Store (opened through shell:AppsFolder).</summary>
    StoreApp = 2,

    /// <summary>A macOS app bundle (.app).</summary>
    MacApp = 3,

    /// <summary>A page of the system settings or a tool of the system ("Bluetooth", "Geräte-Manager").</summary>
    SystemSetting = 4,
}

/// <summary>A program the launcher can start.</summary>
public sealed record AppEntry
{
    public required string Name { get; init; }

    /// <summary>What is opened: the shortcut, the program, the bundle, "shell:AppsFolder\…" or "ms-settings:…".</summary>
    public required string Path { get; init; }

    public AppEntryKind Kind { get; init; }

    /// <summary>The program behind a shortcut (shown below the name, "Im Ordner zeigen" goes there).</summary>
    public string Target { get; init; } = "";

    /// <summary>Other words that find it: the English name of a system app, the file name.</summary>
    public string Keywords { get; init; } = "";

    [JsonIgnore]
    internal string PreparedName { get; private set; } = "";

    [JsonIgnore]
    internal string Initials { get; private set; } = "";

    [JsonIgnore]
    internal string[] PreparedKeywords { get; private set; } = [];

    internal AppEntry Prepared()
    {
        PreparedName = FuzzyMatcher.Prepare(Name);
        Initials = FuzzyMatcher.Initials(Name);
        PreparedKeywords = Keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(FuzzyMatcher.Prepare).ToArray();
        return this;
    }

    /// <summary>The id of its results (usage is learned by it).</summary>
    public string ResultId => $"app:{Path}";
}

/// <summary>
/// The programs of this computer, in memory: the launcher searches them on every key without touching the disk. At start the
/// last index comes from the cache (apps.json) at once; a scan in the background (at start, every 30 minutes, after
/// "Programme neu einlesen" and when a Start menu / Applications folder changes) replaces it.
///   Windows: the Start menu of the user and of all users, the desktops, extra folders, the apps of the Microsoft Store and a
///            list of system settings and tools.
///   macOS:   the .app bundles of /Applications, /System/Applications, ~/Applications (two levels deep), extra folders.
/// </summary>
public static class AppIndex
{
    private static readonly object Gate = new();
    private static readonly TimeSpan RescanInterval = TimeSpan.FromMinutes(30);
    private static IReadOnlyList<AppEntry> _entries = [];
    private static Task? _scan;
    private static bool _rescanRequested;
    private static Timer? _timer;
    private static List<FileSystemWatcher> _watchers = [];
    private static Timer? _watchDelay;

    /// <summary>Raised after a scan changed the index (any thread).</summary>
    public static event EventHandler? Changed;

    public static IReadOnlyList<AppEntry> Entries => _entries;

    /// <summary>When the last scan ended.</summary>
    public static DateTimeOffset? LastScan { get; private set; }

    /// <summary>Tests and snapshots give the index themselves (no scan, no watchers).</summary>
    internal static Func<IReadOnlyList<AppEntry>>? ScannerForTests { get; set; }

    /// <summary>Loads the cache and starts the background scan and the watchers.</summary>
    public static void Start()
    {
        var cached = JsonFile.Read<List<AppEntry>>(AppPaths.AppIndexFile);
        if (cached is { Count: > 0 })
        {
            SetEntries(cached);
        }

        Rescan();
        _timer ??= new Timer(_ => Rescan(), null, RescanInterval, RescanInterval);
        if (ScannerForTests is null)
        {
            Watch();
        }
    }

    public static void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _watchDelay?.Dispose();
        _watchDelay = null;
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers = [];
    }

    /// <summary>Scans again in the background (a scan already running is followed by one more).</summary>
    public static Task Rescan()
    {
        lock (Gate)
        {
            if (_scan is { IsCompleted: false })
            {
                _rescanRequested = true;
                return _scan;
            }

            _scan = Task.Run(ScanLoop);
            return _scan;
        }
    }

    /// <summary>The entries that match, with their score (unsorted).</summary>
    public static IEnumerable<(AppEntry Entry, int Score)> Find(string preparedQuery)
    {
        foreach (var entry in _entries)
        {
            var score = FuzzyMatcher.Score(preparedQuery, entry.PreparedName, entry.Initials);
            foreach (var keyword in entry.PreparedKeywords)
            {
                score = Math.Max(score, FuzzyMatcher.Score(preparedQuery, keyword) - 5);
            }

            if (score > 0)
            {
                yield return (entry, score);
            }
        }
    }

    /// <summary>The entry with this result id (recently used programs, the usage list).</summary>
    public static AppEntry? FindById(string resultId) => _entries.FirstOrDefault(entry => entry.ResultId == resultId);

    /// <summary>Tests: a fixed index.</summary>
    internal static void SetEntriesForTests(IEnumerable<AppEntry> entries)
    {
        SetEntries(entries.ToList());
        LastScan = AppClock.Now;
    }

    private static void ScanLoop()
    {
        while (true)
        {
            try
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var found = ScannerForTests?.Invoke() ?? Scan();
                SetEntries(found.ToList());
                LastScan = AppClock.Now;
                if (ScannerForTests is null)
                {
                    JsonFile.Write(AppPaths.AppIndexFile, _entries);
                }

                Log.Info($"Program index: {_entries.Count} entries in {watch.ElapsedMilliseconds} ms");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Log.Warning($"Program index: scan failed ({exception.GetType().Name}: {exception.Message})");
            }

            lock (Gate)
            {
                if (!_rescanRequested)
                {
                    return;
                }

                _rescanRequested = false;
            }
        }
    }

    private static IEnumerable<AppEntry> Scan()
    {
        var settings = SettingsService.Get();
        if (OperatingSystem.IsWindows())
        {
            return WindowsApps.Scan(settings.ExtraAppFolders, settings.IncludeStoreApps);
        }

        if (OperatingSystem.IsMacOS())
        {
            return MacApps.Scan(settings.ExtraAppFolders);
        }

        return [];
    }

    private static void SetEntries(List<AppEntry> entries)
    {
        // one entry per name and target: the same program in the Start menu of the user and of all users is one result
        var unique = entries
            .Where(entry => entry.Name.Length > 0)
            .GroupBy(entry => (entry.Name.ToLowerInvariant(), Target: (entry.Target.Length > 0 ? entry.Target : entry.Path).ToLowerInvariant()))
            .Select(group => group.First().Prepared())
            .OrderBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var changed = unique.Count != _entries.Count || !unique.Select(entry => entry.Path).SequenceEqual(_entries.Select(entry => entry.Path));
        _entries = unique;
        if (changed)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>A new or removed shortcut / app: scan again two seconds after the last change (installers write many files).</summary>
    private static void Watch()
    {
        var folders = OperatingSystem.IsWindows() ? WindowsApps.WatchedFolders() : OperatingSystem.IsMacOS() ? MacApps.WatchedFolders() : [];
        foreach (var folder in folders.Where(Directory.Exists))
        {
            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = OperatingSystem.IsWindows(),
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                };
                watcher.Created += OnFolderChanged;
                watcher.Deleted += OnFolderChanged;
                watcher.Renamed += OnFolderChanged;
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
            {
                Log.Warning($"Program index: cannot watch {folder} ({exception.Message})");
            }
        }
    }

    private static void OnFolderChanged(object sender, FileSystemEventArgs e)
    {
        _watchDelay ??= new Timer(_ => Rescan(), null, Timeout.Infinite, Timeout.Infinite);
        _watchDelay.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
    }
}
