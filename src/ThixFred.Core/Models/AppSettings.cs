namespace ThixFred.Core.Models;

/// <summary>The look of the settings window: like the system, light or dark (the launcher has its own <see cref="LauncherTheme"/>).</summary>
public enum ThemeChoice
{
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>Where "&gt;ping heise.de" runs.</summary>
public enum TerminalChoice
{
    /// <summary>Windows Terminal when installed, otherwise PowerShell (Windows); Terminal (macOS).</summary>
    Automatic = 0,
    WindowsTerminal = 1,
    PowerShell = 2,
    CommandPrompt = 3,
    MacTerminal = 4,
    ITerm = 5,
}

/// <summary>The settings of this computer (settings.json). Only <see cref="Business.SettingsService"/> writes them.</summary>
public sealed record AppSettings
{
    /// <summary>The global shortcut that opens the launcher: "Alt+Space" (Windows) or "Alt+Space" = ⌥ Space (macOS).</summary>
    public string Hotkey { get; init; } = Business.HotkeyService.DefaultHotkey;

    /// <summary>The look of the settings window.</summary>
    public ThemeChoice Theme { get; init; } = ThemeChoice.System;

    /// <summary>The theme of the launcher (<see cref="LauncherTheme.Id"/>); "system" follows light and dark of the system.</summary>
    public string LauncherThemeId { get; init; } = LauncherTheme.SystemId;

    /// <summary>How many results the launcher shows at most.</summary>
    public int MaxResults { get; init; } = 8;

    /// <summary>The launcher hides as soon as another app comes to the front.</summary>
    public bool HideOnDeactivate { get; init; } = true;

    /// <summary>The launcher opens with the last search instead of an empty field.</summary>
    public bool KeepLastQuery { get; init; }

    /// <summary>Results that were chosen often for what was typed come first (learned on this computer).</summary>
    public bool LearnFromUsage { get; init; } = true;

    /// <summary>Where "&gt;command" runs.</summary>
    public TerminalChoice Terminal { get; init; } = TerminalChoice.Automatic;

    /// <summary>"Im Web suchen" is offered below the results for every search.</summary>
    public bool WebSearchFallback { get; init; } = true;

    /// <summary>The web search of the fallback (<see cref="WebSearch.Id"/>).</summary>
    public string DefaultWebSearchId { get; init; } = "google";

    /// <summary>Windows: apps of the Microsoft Store (Rechner, Einstellungen, …) are found too.</summary>
    public bool IncludeStoreApps { get; init; } = true;

    /// <summary>Folders searched for programs in addition to the Start menu / Applications.</summary>
    public IReadOnlyList<string> ExtraAppFolders { get; init; } = [];

    /// <summary>Switched off built-in providers and plugins, by id; and keywords chosen by the user.</summary>
    public IReadOnlyDictionary<string, PluginPreference> Plugins { get; init; } = new Dictionary<string, PluginPreference>();

    /// <summary>The address of the plugin store (catalog JSON); empty = the store of ThixFred on GitHub.</summary>
    public string StoreUrl { get; init; } = "";

    public bool CheckForUpdates { get; init; } = true;

    /// <summary>Full auto update: install new versions without asking (on by default, the setup offers it).</summary>
    public bool InstallUpdatesAutomatically { get; init; } = true;

    /// <summary>Size of the settings window (device-independent pixels).</summary>
    public double WindowWidth { get; init; } = 1120;

    public double WindowHeight { get; init; } = 740;

    /// <summary>The menu at the left of the settings window is folded to its icons.</summary>
    public bool IsSidebarCollapsed { get; init; }

    /// <summary>The welcome hint (shortcut, tray) has been shown.</summary>
    public bool WelcomeShown { get; init; }

    /// <summary>The last search of the launcher (only kept with <see cref="KeepLastQuery"/>).</summary>
    public string LastQuery { get; init; } = "";

    public CloudSettings Cloud { get; init; } = new();
}

/// <summary>What the user chose for one provider or plugin.</summary>
public sealed record PluginPreference
{
    public bool IsEnabled { get; init; } = true;

    /// <summary>A keyword of the user's own instead of the plugin's ("" = the plugin's).</summary>
    public string Keyword { get; init; } = "";
}

/// <summary>The connection to MatCloud (ThixCloud). The device token is stored protected (DPAPI / Keychain), not in here.</summary>
public sealed record CloudSettings
{
    /// <summary>The address of the cloud, e.g. https://cloud.example.com. Empty = not connected.</summary>
    public string ServerUrl { get; init; } = "";

    public string UserName { get; init; } = "";

    public string DisplayName { get; init; } = "";

    public string DeviceName { get; init; } = "";

    public long DeviceId { get; init; }

    /// <summary>The device token, protected for this user (Windows DPAPI); on macOS it lives in the Keychain.</summary>
    public string ProtectedToken { get; init; } = "";

    public bool IsConnected => ServerUrl.Length > 0 && DeviceId > 0;
}
