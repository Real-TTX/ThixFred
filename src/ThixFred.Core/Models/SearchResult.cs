namespace ThixFred.Core.Models;

/// <summary>What kind of thing a result is (the launcher shows it in the subtitle and sorts ties by it).</summary>
public enum ResultKind
{
    Application = 0,
    RunningApplication = 1,
    Calculation = 2,
    Terminal = 3,
    System = 4,
    Command = 5,
    Url = 6,
    WebSearch = 7,
    Plugin = 8,
}

/// <summary>What happens when a result is chosen.</summary>
public enum LaunchActionKind
{
    /// <summary>Opens <see cref="LaunchAction.Target"/> with the system: a program, a shortcut, a document, a folder, an address, a Store app.</summary>
    Open,

    /// <summary>Shows <see cref="LaunchAction.Target"/> in Explorer / Finder.</summary>
    Reveal,

    /// <summary>Starts <see cref="LaunchAction.Target"/> with administrator rights (Windows).</summary>
    OpenAsAdministrator,

    /// <summary>Puts <see cref="LaunchAction.Target"/> into the clipboard.</summary>
    Copy,

    /// <summary>Runs <see cref="LaunchAction.Target"/> in a terminal window that stays open.</summary>
    Terminal,

    /// <summary>Runs <see cref="LaunchAction.Target"/> in the launcher's own terminal (<see cref="Business.IntegratedTerminal"/>).</summary>
    IntegratedTerminal,

    /// <summary>Installs the plugin <see cref="LaunchAction.Target"/> from the store; then the search is <see cref="LaunchAction.Argument"/>.</summary>
    StoreInstall,

    /// <summary>Asks the program with the process id <see cref="LaunchAction.Target"/> to close (it may ask to save).</summary>
    CloseProcess,

    /// <summary>Ends the process <see cref="LaunchAction.Target"/> at once.</summary>
    KillProcess,

    /// <summary>A command of the system (<see cref="SystemCommand"/> in <see cref="LaunchAction.Target"/>).</summary>
    System,

    /// <summary>Runs the script of the plugin <see cref="LaunchAction.Target"/> with <see cref="LaunchAction.Argument"/>.</summary>
    Script,

    /// <summary>A command of ThixFred itself (<see cref="AppCommand"/> in <see cref="LaunchAction.Target"/>).</summary>
    App,

    /// <summary>Replaces the search with <see cref="LaunchAction.Target"/> (the launcher stays open).</summary>
    SetQuery,
}

/// <summary>A command of the operating system.</summary>
public enum SystemCommand
{
    Lock,
    Sleep,
    SignOut,
    Restart,
    ShutDown,
    EmptyTrash,
}

/// <summary>A command of ThixFred that the UI carries out (the business layer does not open windows).</summary>
public enum AppCommand
{
    Settings,
    Plugins,
    Store,
    Themes,
    WebSearches,
    About,
    Reindex,
    CheckForUpdates,
    Quit,
}

/// <summary>One thing a result can do: "Öffnen", "Im Ordner zeigen", "Beenden" …</summary>
public sealed record LaunchAction(string Label, LaunchActionKind Kind, string Target, string? Argument = null)
{
    /// <summary>Asked before it runs ("Wirklich neu starten?"); null = runs at once.</summary>
    public string? Confirmation { get; init; }
}

/// <summary>
/// The picture of a result: a symbol of MatDesign's sprite (<see cref="Glyph"/>) or the icon of a file, program or Store app
/// (<see cref="IconSource"/>: a path or a shell name, the UI loads and caches it); the glyph stands in until the icon is there.
/// </summary>
public sealed record ResultIcon(string Glyph, string? IconSource = null)
{
    public static ResultIcon Of(string glyph) => new(glyph);
}

/// <summary>One row of the launcher.</summary>
public sealed record SearchResult
{
    /// <summary>Stable over searches and starts ("app:C:\…\Code.lnk", "calc", "web:google"): usage is learned by it.</summary>
    public required string Id { get; init; }

    public required string Title { get; init; }

    public string Subtitle { get; init; } = "";

    public required ResultKind Kind { get; init; }

    public ResultIcon Icon { get; init; } = ResultIcon.Of("search");

    /// <summary>Enter.</summary>
    public required LaunchAction Action { get; init; }

    /// <summary>Shift+Enter (and the second button of the row): "Im Ordner zeigen", "Als Administrator", "Sofort beenden" …</summary>
    public LaunchAction? AlternateAction { get; init; }

    /// <summary>Tab completes the search to this text.</summary>
    public string? Autocomplete { get; init; }

    /// <summary>Higher comes first (0 … about 1000). Set by the provider, raised by usage.</summary>
    public double Score { get; init; }

    /// <summary>True for results that are only a fallback ("Im Web suchen"): they never rise above a real match.</summary>
    public bool IsFallback { get; init; }
}

/// <summary>What the launcher does after an action ran.</summary>
public enum LaunchOutcomeKind
{
    /// <summary>Done: the launcher hides.</summary>
    Hide,

    /// <summary>The launcher stays open with a new search (<see cref="LaunchOutcome.Query"/>).</summary>
    SetQuery,

    /// <summary>The UI carries out <see cref="LaunchOutcome.Command"/>.</summary>
    AppCommand,

    /// <summary>Shows <see cref="LaunchOutcome.Message"/> in the launcher (an error, the output of a script); it stays open.</summary>
    Message,

    /// <summary>The launcher runs <see cref="LaunchOutcome.Query"/> in its terminal and shows the output.</summary>
    Terminal,
}

/// <summary>The answer of <see cref="Business.LaunchService.Execute"/>.</summary>
public sealed record LaunchOutcome(LaunchOutcomeKind Kind, string? Query = null, AppCommand? Command = null, string? Message = null, bool IsError = false)
{
    public static readonly LaunchOutcome Done = new(LaunchOutcomeKind.Hide);

    public static LaunchOutcome Show(string message, bool isError = false) => new(LaunchOutcomeKind.Message, Message: message, IsError: isError);
}
