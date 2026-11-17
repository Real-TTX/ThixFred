using System.Security.Cryptography;
using System.Text;
using MatCloud.Contract;
using ThixFred.Core.Infrastructure;
using ThixFred.Core.Models;

namespace ThixFred.Core.Business;

/// <summary>
/// What ThixFred keeps in the general sync of MatCloud, and how an item of the cloud and the state of this computer meet.
/// Collections (keys):
///   settings     (launcher)      the settings of the account: theme, results, web search, terminal, plugins switched on/off …
///   themes       (theme id)      the user's own themes (JSON)
///   websearches  (search id)     the web searches with keywords (JSON)
///   scripts      (script id)     the user's scripts: { fileName, content } – the whole file
///   store        (package id)    what is installed from the store: { kind, version } – other computers install it too
/// The hotkey, window sizes and the program index stay on each computer.
///
/// The index (sync.json) remembers per item the hash both sides agreed on last. A round: the cloud's changes come down and are
/// applied where this computer has no newer change of its own; then every local item whose hash differs from the index goes
/// up (an item missing here goes up as deleted). Last writer wins by time, as MatCloud decides.
/// </summary>
internal static class ConfigSync
{
    public const string Settings = "settings";
    public const string Themes = "themes";
    public const string WebSearches = "websearches";
    public const string Scripts = "scripts";
    public const string Store = "store";
    public const string SettingsKey = "launcher";

    private static readonly object Gate = new();
    private static SyncIndex? _index;

    public static long LastRevision(string account)
    {
        lock (Gate)
        {
            return Index(account).LastRevision;
        }
    }

    /// <summary>Applies the cloud's changes; returns true when something changed here.</summary>
    public static bool ApplyRemote(string account, IReadOnlyList<SyncItemDto> items, long latest)
    {
        var changed = false;
        var local = Snapshot().ToDictionary(item => (item.Collection, item.Key));
        lock (Gate)
        {
            var index = Index(account);
            foreach (var item in items)
            {
                var id = (item.Collection, item.Key);
                index.Items.TryGetValue(Key(id), out var known);
                local.TryGetValue(id, out var mine);
                var mineHash = mine is null ? null : Hash(mine.Data);
                var localChanged = mineHash != known?.Hash;
                var remoteHash = item.IsDeleted ? null : Hash(item.Data ?? "");
                if (localChanged && mine is not null && mine.UpdatedAt > item.UpdatedAt)
                {
                    continue; // this computer changed it later: it goes up and wins
                }

                if (remoteHash != mineHash)
                {
                    Apply(item);
                    changed = true;
                }

                index.Items[Key(id)] = new IndexEntry(remoteHash, item.UpdatedAt);
            }

            index.LastRevision = Math.Max(index.LastRevision, latest);
            Save(index);
        }

        return changed;
    }

    /// <summary>The local items whose state the cloud does not have yet (deleted ones as tombstones).</summary>
    public static List<SyncItemDto> PendingChanges(string account, int max)
    {
        var local = Snapshot();
        var now = AppClock.UtcNow;
        var pending = new List<SyncItemDto>();
        lock (Gate)
        {
            var index = Index(account);
            foreach (var item in local)
            {
                var hash = Hash(item.Data);
                if (index.Items.TryGetValue(Key((item.Collection, item.Key)), out var known) && known.Hash == hash)
                {
                    continue;
                }

                pending.Add(new SyncItemDto
                {
                    Collection = item.Collection,
                    Key = item.Key,
                    Data = item.Data,
                    ContentHash = hash,
                    UpdatedAt = item.UpdatedAt > (known?.UpdatedAt ?? DateTimeOffset.MinValue) ? item.UpdatedAt : now,
                });
            }

            var present = local.Select(item => Key((item.Collection, item.Key))).ToHashSet();
            foreach (var (key, entry) in index.Items)
            {
                if (entry.Hash is not null && !present.Contains(key))
                {
                    var (collection, itemKey) = Split(key);
                    pending.Add(new SyncItemDto { Collection = collection, Key = itemKey, IsDeleted = true, UpdatedAt = now });
                }
            }
        }

        return pending.Take(max).ToList();
    }

    /// <summary>What the cloud did with a push: applied items are agreed; a newer item of the cloud is taken here.</summary>
    public static bool ConfirmPushed(string account, IReadOnlyList<SyncItemDto> pushed, SyncPushResponse response)
    {
        var changed = false;
        lock (Gate)
        {
            var index = Index(account);
            foreach (var result in response.Results)
            {
                var item = pushed.FirstOrDefault(candidate => candidate.Collection == result.Collection && candidate.Key == result.Key);
                if (item is null)
                {
                    continue;
                }

                if (result.Applied)
                {
                    index.Items[Key((item.Collection, item.Key))] = new IndexEntry(item.IsDeleted ? null : Hash(item.Data ?? ""), item.UpdatedAt);
                }
                else if (result.Current is { } current)
                {
                    Apply(current);
                    index.Items[Key((current.Collection, current.Key))] = new IndexEntry(current.IsDeleted ? null : Hash(current.Data ?? ""), current.UpdatedAt);
                    changed = true;
                }
            }

            index.LastRevision = Math.Max(index.LastRevision, response.Latest);
            Save(index);
        }

        return changed;
    }

    /// <summary>A new sign-in starts from nothing: the cloud's state comes down, then this computer's goes up.</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _index = null;
            if (File.Exists(AppPaths.SyncIndexFile))
            {
                File.Delete(AppPaths.SyncIndexFile);
            }
        }
    }

    /// <summary>The settings that travel with the account.</summary>
    internal sealed record SharedSettings
    {
        public string LauncherThemeId { get; init; } = LauncherTheme.SystemId;

        public int MaxResults { get; init; } = 8;

        public bool HideOnDeactivate { get; init; } = true;

        public bool KeepLastQuery { get; init; }

        public bool LearnFromUsage { get; init; } = true;

        public bool WebSearchFallback { get; init; } = true;

        public string DefaultWebSearchId { get; init; } = "google";

        public string StoreUrl { get; init; } = "";

        public SortedDictionary<string, PluginPreference> Plugins { get; init; } = new(StringComparer.Ordinal);

        public static SharedSettings From(AppSettings settings) => new()
        {
            LauncherThemeId = settings.LauncherThemeId,
            MaxResults = settings.MaxResults,
            HideOnDeactivate = settings.HideOnDeactivate,
            KeepLastQuery = settings.KeepLastQuery,
            LearnFromUsage = settings.LearnFromUsage,
            WebSearchFallback = settings.WebSearchFallback,
            DefaultWebSearchId = settings.DefaultWebSearchId,
            StoreUrl = settings.StoreUrl,
            Plugins = new SortedDictionary<string, PluginPreference>(settings.Plugins.ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal),
        };

        public AppSettings ApplyTo(AppSettings settings) => settings with
        {
            LauncherThemeId = LauncherThemeId,
            MaxResults = MaxResults,
            HideOnDeactivate = HideOnDeactivate,
            KeepLastQuery = KeepLastQuery,
            LearnFromUsage = LearnFromUsage,
            WebSearchFallback = WebSearchFallback,
            DefaultWebSearchId = DefaultWebSearchId,
            StoreUrl = StoreUrl,
            Plugins = Plugins,
        };
    }

    private sealed record LocalItem(string Collection, string Key, string Data, DateTimeOffset UpdatedAt);

    private sealed record ScriptData(string FileName, string Content);

    private sealed record StoreData(StoreItemKind Kind, string Version);

    /// <summary>Everything this computer shares, serialized the same way every time (the hash compares it).</summary>
    private static List<LocalItem> Snapshot()
    {
        var items = new List<LocalItem>
        {
            new(Settings, SettingsKey, JsonFile.Serialize(SharedSettings.From(SettingsService.Get())), DateTimeOffset.MinValue),
        };
        items.AddRange(ThemeService.Snapshot().Select(theme => new LocalItem(Themes, theme.Id, JsonFile.Serialize(theme with { UpdatedAt = default }), theme.UpdatedAt)));
        items.AddRange(WebSearchService.Snapshot().Select(search => new LocalItem(WebSearches, search.Id, JsonFile.Serialize(search with { UpdatedAt = default }), search.UpdatedAt)));
        items.AddRange(ScriptService.Snapshot().Select(script => new LocalItem(Scripts, script.Id, JsonFile.Serialize(new ScriptData(script.FileName, script.Content)), DateTimeOffset.MinValue)));
        items.AddRange(StoreService.Snapshot().Select(package => new LocalItem(Store, package.Id, JsonFile.Serialize(new StoreData(package.Kind, package.Version)), DateTimeOffset.MinValue)));
        return items;
    }

    private static void Apply(SyncItemDto item)
    {
        var deleted = item.IsDeleted || item.Data is null;
        switch (item.Collection)
        {
            case Settings when item.Key == SettingsKey && !deleted:
                if (JsonFile.Deserialize<SharedSettings>(item.Data!) is { } shared)
                {
                    SettingsService.ApplyFromCloud(shared.ApplyTo);
                }

                break;
            case Themes:
                ThemeService.ApplyFromCloud(item.Key, deleted ? null : JsonFile.Deserialize<LauncherTheme>(item.Data!) is { } theme ? theme with { UpdatedAt = item.UpdatedAt } : null);
                break;
            case WebSearches:
                WebSearchService.ApplyFromCloud(item.Key, deleted ? null : JsonFile.Deserialize<WebSearch>(item.Data!) is { } search ? search with { UpdatedAt = item.UpdatedAt } : null);
                break;
            case Scripts:
                var script = deleted ? null : JsonFile.Deserialize<ScriptData>(item.Data!);
                ScriptService.ApplyFromCloud(item.Key, script?.FileName, script?.Content);
                break;
            case Store:
                _ = StoreService.ApplyFromCloudAsync(item.Key, installed: !deleted);
                break;
        }
    }

    private static string Hash(string data) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(data)));

    private static string Key((string Collection, string Key) id) => $"{id.Collection}/{id.Key}";

    private static (string Collection, string Key) Split(string key)
    {
        var slash = key.IndexOf('/');
        return (key[..slash], key[(slash + 1)..]);
    }

    private static SyncIndex Index(string account)
    {
        if (_index is { } index && index.Account == account)
        {
            return index;
        }

        var stored = JsonFile.Read<SyncIndex>(AppPaths.SyncIndexFile);
        _index = stored is not null && stored.Account == account ? stored : new SyncIndex { Account = account };
        return _index;
    }

    private static void Save(SyncIndex index) => JsonFile.Write(AppPaths.SyncIndexFile, index);

    private sealed class SyncIndex
    {
        public string Account { get; set; } = "";

        public long LastRevision { get; set; }

        public Dictionary<string, IndexEntry> Items { get; set; } = [];
    }

    private sealed record IndexEntry(string? Hash, DateTimeOffset UpdatedAt);
}
