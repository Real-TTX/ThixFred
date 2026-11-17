namespace ThixFred.Core.Models;

/// <summary>How a plugin takes part in the search.</summary>
public enum PluginMode
{
    /// <summary>One result ("UUID erzeugen"); Enter runs the script with what was typed after the keyword.</summary>
    Run = 0,

    /// <summary>The script runs while typing and returns the results itself (JSON or one result per line).</summary>
    Filter = 1,
}

/// <summary>Where the output of a script of <see cref="PluginMode.Run"/> goes.</summary>
public enum ScriptOutput
{
    /// <summary>Shown in the launcher (it stays open).</summary>
    Launcher = 0,

    /// <summary>Into the clipboard (the launcher closes).</summary>
    Clipboard = 1,

    /// <summary>The script runs in a terminal window that stays open.</summary>
    Terminal = 2,

    /// <summary>Nothing (the launcher closes).</summary>
    None = 3,
}

/// <summary>Where a plugin comes from.</summary>
public enum PluginSource
{
    /// <summary>Part of ThixFred (apps, calculator, terminal …): can be switched off and get another keyword, nothing else.</summary>
    BuiltIn = 0,

    /// <summary>Installed from the store (updates come from there).</summary>
    Store = 1,

    /// <summary>A folder with a plugin.json the user put into the plugins folder.</summary>
    Folder = 2,

    /// <summary>A single script of the user (scripts folder, editable in ThixFred, synced with the cloud).</summary>
    Script = 3,
}

/// <summary>The script language of a <see cref="UserScript"/>: decides the file extension, the comment and the program that runs it.</summary>
public enum ScriptLanguage
{
    PowerShell = 0,
    Batch = 1,
    Bash = 2,
    Zsh = 3,
    Python = 4,
    JavaScript = 5,
}

/// <summary>
/// plugin.json of a plugin folder (store or the user's own). <see cref="Script"/> runs everywhere; <see cref="ScriptWindows"/> and
/// <see cref="ScriptMacOS"/> replace it on their system.
/// </summary>
public sealed record PluginManifest
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    public string Version { get; init; } = "1.0.0";

    public string Author { get; init; } = "";

    public string Description { get; init; } = "";

    /// <summary>Typed first: "uuid", "ip". Empty: the plugin answers every search (only <see cref="PluginMode.Filter"/>, use with care).</summary>
    public string Keyword { get; init; } = "";

    /// <summary>A symbol of MatDesign's sprite ("key", "globe" …).</summary>
    public string Icon { get; init; } = "plug";

    public PluginMode Mode { get; init; }

    public ScriptOutput Output { get; init; }

    public string? Script { get; init; }

    public string? ScriptWindows { get; init; }

    public string? ScriptMacOS { get; init; }

    /// <summary>A script that takes longer is stopped.</summary>
    public int TimeoutSeconds { get; init; } = 10;

    public string Homepage { get; init; } = "";
}

/// <summary>A plugin as the search and the settings see it (built-in, store, folder or script).</summary>
public sealed record PluginInfo
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public string Version { get; init; } = "";

    public string Author { get; init; } = "";

    /// <summary>The keyword in use (the user's or the plugin's own).</summary>
    public string Keyword { get; init; } = "";

    /// <summary>The keyword the plugin brings.</summary>
    public string DefaultKeyword { get; init; } = "";

    public string Icon { get; init; } = "plug";

    public PluginMode Mode { get; init; }

    public ScriptOutput Output { get; init; }

    public PluginSource Source { get; init; }

    /// <summary>The folder of a store or folder plugin; the scripts folder for a script.</summary>
    public string Folder { get; init; } = "";

    /// <summary>The script that runs on this system (null: built in, or none for this system).</summary>
    public string? ScriptPath { get; init; }

    public int TimeoutSeconds { get; init; } = 10;

    public bool IsEnabled { get; init; } = true;

    /// <summary>False when the plugin has no script for this system (e.g. a PowerShell plugin on a Mac without pwsh).</summary>
    public bool IsSupported { get; init; } = true;

    public string Homepage { get; init; } = "";
}

/// <summary>
/// A script of the user: one file in the scripts folder whose first comment lines describe it (@name, @keyword, @mode,
/// @output, @description). ThixFred writes and reads exactly that, so a script dropped into the folder by hand works too.
/// </summary>
public sealed record UserScript
{
    /// <summary>The file name without extension (small letters, digits, '-').</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Keyword { get; init; } = "";

    public string Description { get; init; } = "";

    public ScriptLanguage Language { get; init; }

    public PluginMode Mode { get; init; }

    public ScriptOutput Output { get; init; }

    /// <summary>The script without its header.</summary>
    public string Body { get; init; } = "";

    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Plugins and themes of the store.</summary>
public enum StoreItemKind
{
    Plugin = 0,
    Theme = 1,
}

/// <summary>One entry of the store's catalog (store.json).</summary>
public sealed record StoreItem
{
    public string Id { get; init; } = "";

    public StoreItemKind Kind { get; init; }

    public string Name { get; init; } = "";

    public string Version { get; init; } = "1.0.0";

    public string Author { get; init; } = "";

    public string Description { get; init; } = "";

    /// <summary>A plugin's keyword (shown before installing).</summary>
    public string Keyword { get; init; } = "";

    public string Icon { get; init; } = "plug";

    /// <summary>"windows", "macos": where it runs (empty: everywhere).</summary>
    public IReadOnlyList<string> Platforms { get; init; } = [];

    /// <summary>The package: a zip (plugin) or a JSON file (theme), relative to the catalog or an absolute https address.</summary>
    public string Download { get; init; } = "";

    /// <summary>SHA-256 of the package (hex): checked before anything is unpacked.</summary>
    public string Sha256 { get; init; } = "";

    public string Homepage { get; init; } = "";

    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>store.json: what the store offers.</summary>
public sealed record StoreCatalog
{
    public int Format { get; init; } = 1;

    public DateTimeOffset GeneratedAt { get; init; }

    public IReadOnlyList<StoreItem> Items { get; init; } = [];
}

/// <summary>A store entry together with what is installed here.</summary>
public sealed record StoreListing(StoreItem Item, string? InstalledVersion, bool IsSupported)
{
    public bool IsInstalled => InstalledVersion is not null;

    public bool HasUpdate => InstalledVersion is not null && Business.StoreService.CompareVersions(Item.Version, InstalledVersion) > 0;
}
