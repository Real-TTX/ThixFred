namespace ThixFred.Core.Infrastructure;

/// <summary>
/// Where ThixFred keeps its files. Program files and user data are separate: updates and uninstalling never touch the
/// user data unless the user decides so.
///   Windows: %LocalAppData%\ThixFred (settings, plugins, scripts, themes, caches, updates, logs)
///   macOS:   ~/Library/Application Support/ThixFred
/// </summary>
public static class AppPaths
{
    /// <summary>Developers can point ThixFred to a separate folder (sample data, screenshots) with this variable.</summary>
    public const string DataDirectoryVariable = "THIXFRED_DATA_DIR";

    private static string? _dataDirectoryOverride = NullIfEmpty(Environment.GetEnvironmentVariable(DataDirectoryVariable));

    public static string DataDirectory => _dataDirectoryOverride ?? Path.Combine(UserFolder(), AppInfo.ProductName);

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    /// <summary>Plugins in folders (store and the user's own): plugins/&lt;id&gt;/plugin.json.</summary>
    public static string PluginsDirectory => Path.Combine(DataDirectory, "plugins");

    /// <summary>Single scripts of the user (synced with the cloud).</summary>
    public static string ScriptsDirectory => Path.Combine(DataDirectory, "scripts");

    /// <summary>Themes of the user and of the store (one JSON each).</summary>
    public static string ThemesDirectory => Path.Combine(DataDirectory, "themes");

    /// <summary>The web searches with keywords.</summary>
    public static string WebSearchesFile => Path.Combine(DataDirectory, "websearches.json");

    /// <summary>The programs found at the last scan: the launcher answers from it at once after a start.</summary>
    public static string AppIndexFile => Path.Combine(CacheDirectory, "apps.json");

    /// <summary>What was chosen how often for which search (learning).</summary>
    public static string UsageFile => Path.Combine(DataDirectory, "usage.json");

    /// <summary>What the cloud and this computer agreed on last (per item).</summary>
    public static string SyncIndexFile => Path.Combine(DataDirectory, "sync.json");

    /// <summary>Program icons as PNG, the last catalog of the store.</summary>
    public static string CacheDirectory => Path.Combine(DataDirectory, "cache");

    public static string IconCacheDirectory => Path.Combine(CacheDirectory, "icons");

    public static string UpdateDirectory => Path.Combine(DataDirectory, "updates");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>True when a developer or a test pointed ThixFred to its own folder: it is then its own instance.</summary>
    public static bool HasOverride => _dataDirectoryOverride is not null;

    /// <summary>Tests use a temporary folder.</summary>
    internal static void UseDirectoryForTests(string? directory) => _dataDirectoryOverride = directory;

    private static string UserFolder() => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
        : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
