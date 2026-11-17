using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using ThixFred.Core.Infrastructure;
using ThixFred.Core.Models;

namespace ThixFred.Core.Business;

/// <summary>
/// The store of plugins and themes. Its catalog (store.json) lists every package with its version and SHA-256; ThixFred's own
/// store is built by CI from the folder store/ of the repository and published in the release "store-channel" (another store
/// can be set in the settings). Installing downloads the package, checks its hash BEFORE it is unpacked, keeps every file
/// inside the plugin's folder and checks plugin.json; updating is installing the newer version; removing deletes the folder or
/// the theme. Installed store packages travel with the cloud: another computer of the account installs them too.
/// </summary>
public static class StoreService
{
    private const string ThemeMarkerFile = ".store.json";
    private static readonly TimeSpan CatalogLifetime = TimeSpan.FromHours(1);
    private static readonly SemaphoreSlim InstallGate = new(1, 1);
    private static (DateTimeOffset At, string Url, StoreCatalog Catalog)? _catalog;

    /// <summary>Raised after something was installed, updated or removed.</summary>
    public static event EventHandler? Changed;

    /// <summary>ThixFred's own store on GitHub.</summary>
    public static string DefaultCatalogUrl => $"https://github.com/{AppInfo.Repository}/releases/download/store-channel/store.json";

    public static string CatalogUrl => SettingsService.Get().StoreUrl is { Length: > 0 } url ? url : DefaultCatalogUrl;

    /// <summary>Tests talk to a store in memory.</summary>
    internal static Func<HttpMessageHandler>? HandlerForTests { get; set; }

    /// <summary>Snapshots show a fixed catalog (no network).</summary>
    internal static Func<IReadOnlyList<StoreListing>>? ListingsForTests { get; set; }

    /// <summary>The catalog with what is installed here; <paramref name="refresh"/> asks the store again (else at most once an hour).</summary>
    public static async Task<IReadOnlyList<StoreListing>> LoadAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        AccessPolicy.EnsureStoreAllowed();
        if (ListingsForTests is { } listings)
        {
            return listings();
        }

        var catalog = await CatalogAsync(refresh, cancellationToken);
        return catalog.Items
            .Where(item => Slug.IsValid(item.Id) && item.Name.Length > 0)
            .Select(item => new StoreListing(item, InstalledVersion(item), IsSupported(item)))
            .OrderBy(listing => listing.Item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Installs (or updates to) the version of the catalog.</summary>
    public static async Task InstallAsync(StoreItem item, CancellationToken cancellationToken = default)
    {
        AccessPolicy.EnsureConfigurationEditable();
        AccessPolicy.EnsureStoreAllowed();
        if (!Slug.IsValid(item.Id))
        {
            throw new BusinessException("Dieses Paket hat keine gültige Kennung.");
        }

        if (!IsSupported(item))
        {
            throw new BusinessException($"{item.Name} gibt es für dieses System nicht.");
        }

        if (item.Kind == StoreItemKind.Plugin && PluginService.BuiltIn.Any(plugin => plugin.Id == item.Id))
        {
            throw new BusinessException("Ein Plugin des Stores darf kein eingebautes ersetzen.");
        }

        await InstallGate.WaitAsync(cancellationToken);
        try
        {
            var bytes = await DownloadAsync(item, cancellationToken);
            if (item.Kind == StoreItemKind.Theme)
            {
                ThemeService.InstallFromStore(System.Text.Encoding.UTF8.GetString(bytes), item.Id);
                var markers = ThemeMarkers();
                markers[item.Id] = item.Version;
                JsonFile.Write(Path.Combine(AppPaths.ThemesDirectory, ThemeMarkerFile), markers);
            }
            else
            {
                InstallPlugin(item, bytes);
            }

            Log.Info($"Store: {item.Id} {item.Version} installed");
        }
        finally
        {
            InstallGate.Release();
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Removes a store plugin or theme.</summary>
    public static void Uninstall(string id)
    {
        AccessPolicy.EnsureConfigurationEditable();
        RemoveInstalled(id);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>The installed version of a store package (null: not installed).</summary>
    public static string? InstalledVersion(StoreItem item) =>
        item.Kind == StoreItemKind.Theme
            ? ThemeMarkers().GetValueOrDefault(item.Id) is { } version && ThemeService.Find(item.Id) is not null ? version : null
            : PluginService.InstalledStoreVersion(item.Id);

    /// <summary>"1.10.0" &gt; "1.9.2": numeric parts compared one by one (missing parts are 0, text after '-' is ignored).</summary>
    public static int CompareVersions(string left, string right)
    {
        static int[] Parts(string version) => version.Split('-')[0].Split('.')
            .Select(part => int.TryParse(part, out var number) ? number : 0).ToArray();

        var a = Parts(left);
        var b = Parts(right);
        for (var index = 0; index < Math.Max(a.Length, b.Length); index++)
        {
            var difference = (index < a.Length ? a[index] : 0).CompareTo(index < b.Length ? b[index] : 0);
            if (difference != 0)
            {
                return difference;
            }
        }

        return 0;
    }

    /// <summary>The sync: what is installed from the store (id → kind and version).</summary>
    internal static IReadOnlyList<(string Id, StoreItemKind Kind, string Version)> Snapshot()
    {
        var installed = new List<(string, StoreItemKind, string)>();
        foreach (var plugin in PluginService.List().Where(plugin => plugin.Source == PluginSource.Store))
        {
            installed.Add((plugin.Id, StoreItemKind.Plugin, PluginService.InstalledStoreVersion(plugin.Id) ?? plugin.Version));
        }

        foreach (var (id, version) in ThemeMarkers().Where(pair => ThemeService.Find(pair.Key) is not null))
        {
            installed.Add((id, StoreItemKind.Theme, version));
        }

        return installed.OrderBy(entry => entry.Item1, StringComparer.Ordinal).ToList();
    }

    /// <summary>The sync: another computer installed (or removed) a store package; installed here from the store in the background.</summary>
    internal static async Task ApplyFromCloudAsync(string id, bool installed)
    {
        if (!installed)
        {
            RemoveInstalled(id);
            Changed?.Invoke(null, EventArgs.Empty);
            return;
        }

        if (AccessPolicy.IsStoreDisabled)
        {
            return;
        }

        try
        {
            var listing = (await LoadAsync()).FirstOrDefault(entry => entry.Item.Id == id);
            if (listing is { IsSupported: true } && (!listing.IsInstalled || listing.HasUpdate))
            {
                await InstallAsync(listing.Item);
            }
        }
        catch (Exception exception) when (exception is BusinessException or HttpRequestException or IOException or TaskCanceledException)
        {
            Log.Warning($"Store: {id} from the cloud could not be installed ({exception.Message})");
        }
    }

    /// <summary>Tests: no catalog in memory.</summary>
    internal static void ResetForTests() => _catalog = null;

    private static bool IsSupported(StoreItem item)
    {
        if (item.Platforms.Count == 0)
        {
            return true;
        }

        var platform = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        return item.Platforms.Any(entry => string.Equals(entry, platform, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<StoreCatalog> CatalogAsync(bool refresh, CancellationToken cancellationToken)
    {
        var url = CatalogUrl;
        if (!refresh && _catalog is { } cached && cached.Url == url && AppClock.UtcNow - cached.At < CatalogLifetime)
        {
            return cached.Catalog;
        }

        var cacheFile = Path.Combine(AppPaths.CacheDirectory, "store.json");
        try
        {
            using var http = CreateClient();
            var catalog = await http.GetFromJsonAsync<StoreCatalog>(url, JsonFile.Options, cancellationToken) ?? new StoreCatalog();
            _catalog = (AppClock.UtcNow, url, catalog);
            JsonFile.Write(cacheFile, catalog);
            return catalog;
        }
        catch (Exception exception) when (exception is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException or NotSupportedException or InvalidOperationException)
        {
            Log.Warning($"Store: the catalog could not be loaded ({exception.Message})");
            if (!refresh && JsonFile.Read<StoreCatalog>(cacheFile) is { } offline)
            {
                return offline;
            }

            throw new BusinessException("Der Store ist gerade nicht erreichbar. Bitte später noch einmal versuchen.");
        }
    }

    private static async Task<byte[]> DownloadAsync(StoreItem item, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(new Uri(CatalogUrl), item.Download, out var address) || address.Scheme is not ("https" or "http"))
        {
            throw new BusinessException($"{item.Name} hat keine gültige Download-Adresse.");
        }

        byte[] bytes;
        try
        {
            using var http = CreateClient();
            bytes = await http.GetByteArrayAsync(address, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            throw new BusinessException($"{item.Name} ließ sich nicht laden ({exception.Message}).");
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (item.Sha256.Length == 0 || !string.Equals(hash, item.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning($"Store: {item.Id} has the wrong SHA-256 (expected {item.Sha256}, got {hash})");
            throw new BusinessException($"{item.Name} ist nicht das Paket, das der Store beschreibt (Prüfsumme falsch) – es wurde nicht installiert.");
        }

        return bytes;
    }

    /// <summary>Unpacks into a new folder next to the plugins, checks it, then replaces the old version in one move.</summary>
    private static void InstallPlugin(StoreItem item, byte[] bytes)
    {
        Directory.CreateDirectory(AppPaths.PluginsDirectory);
        var staging = Path.Combine(AppPaths.PluginsDirectory, $".install-{item.Id}-{Guid.NewGuid():N}");
        var target = Path.Combine(AppPaths.PluginsDirectory, item.Id);
        try
        {
            Directory.CreateDirectory(staging);
            var root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
            using (var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    var destination = Path.GetFullPath(Path.Combine(staging, entry.FullName));
                    if (!destination.StartsWith(root, StringComparison.Ordinal))
                    {
                        throw new BusinessException($"{item.Name} enthält Dateien außerhalb seines Ordners – es wurde nicht installiert.");
                    }

                    if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                    {
                        Directory.CreateDirectory(destination);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination, overwrite: true);
                    if (!OperatingSystem.IsWindows() && Path.GetExtension(destination) is ".sh" or ".zsh" or ".py" or "")
                    {
                        File.SetUnixFileMode(destination, File.GetUnixFileMode(destination) | UnixFileMode.UserExecute);
                    }
                }
            }

            var manifest = PluginService.ReadManifest(staging);
            if (manifest.Id != item.Id)
            {
                throw new BusinessException($"{item.Name}: die Kennung im Paket ({manifest.Id}) passt nicht zum Store ({item.Id}).");
            }

            PluginService.WriteStoreMarker(staging, item.Id, item.Version);
            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true);
            }

            Directory.Move(staging, target);
        }
        catch (InvalidDataException)
        {
            throw new BusinessException($"{item.Name}: das Paket ist kein gültiges ZIP-Archiv.");
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            PluginService.Invalidate();
        }
    }

    private static void RemoveInstalled(string id)
    {
        var markers = ThemeMarkers();
        if (markers.Remove(id))
        {
            JsonFile.Write(Path.Combine(AppPaths.ThemesDirectory, ThemeMarkerFile), markers);
            if (ThemeService.Find(id) is { IsBuiltIn: false })
            {
                ThemeService.Delete(id);
            }

            return;
        }

        var folder = Path.Combine(AppPaths.PluginsDirectory, id);
        if (Slug.IsValid(id) && Directory.Exists(folder) && PluginService.InstalledStoreVersion(id) is not null)
        {
            Directory.Delete(folder, recursive: true);
            PluginService.Invalidate();
        }
    }

    private static Dictionary<string, string> ThemeMarkers() =>
        JsonFile.Read<Dictionary<string, string>>(Path.Combine(AppPaths.ThemesDirectory, ThemeMarkerFile)) ?? [];

    private static HttpClient CreateClient()
    {
        var http = HandlerForTests is { } handler ? new HttpClient(handler()) : new HttpClient();
        http.Timeout = TimeSpan.FromSeconds(60);
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"ThixFred/{AppInfo.BuildNumber} ({AppInfo.PlatformName})");
        return http;
    }
}
