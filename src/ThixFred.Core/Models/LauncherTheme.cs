namespace ThixFred.Core.Models;

/// <summary>The backdrop of the launcher window: a plain colour, or the system's glass behind it (Windows: Acrylic / Mica, macOS: blur).</summary>
public enum ThemeBackdrop
{
    Solid = 0,
    Acrylic = 1,
    Mica = 2,
}

/// <summary>
/// The look of the launcher: colours, form and size. Built-in themes come with ThixFred (<see cref="IsBuiltIn"/>), the user's
/// own live as JSON in the data folder (and in the cloud) and come from the store too. Colours are "#RRGGBB" or "#AARRGGBB".
/// </summary>
public sealed record LauncherTheme
{
    /// <summary>The theme that follows light and dark of the system (ThixFred Hell / ThixFred Dunkel).</summary>
    public const string SystemId = "system";

    /// <summary>Small letters, digits and '-': "ozean", "mein-theme".</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Author { get; init; } = "";

    /// <summary>Dark themes ask for dark scroll bars and tool tips.</summary>
    public bool IsDark { get; init; }

    public ThemeBackdrop Backdrop { get; init; } = ThemeBackdrop.Solid;

    /// <summary>The window behind everything (with alpha, so the backdrop shines through).</summary>
    public string Background { get; init; } = "#F7FFFFFF";

    /// <summary>The line around the window.</summary>
    public string Border { get; init; } = "#1F000000";

    /// <summary>The search text.</summary>
    public string Text { get; init; } = "#FF1D1D1F";

    /// <summary>Subtitles, hints, the placeholder.</summary>
    public string MutedText { get; init; } = "#FF6E6E73";

    /// <summary>The chosen row (a tint).</summary>
    public string Selection { get; init; } = "#1F2F6FED";

    /// <summary>The title of the chosen row, the caret, the badges.</summary>
    public string Accent { get; init; } = "#FF2F6FED";

    /// <summary>The texts of the chosen row; empty = <see cref="Text"/> (a flat theme fills the row with its accent and writes white on it).</summary>
    public string SelectionText { get; init; } = "";

    /// <summary>The shadow around the window (deckend only: glass casts the system's shadow). Flat themes have none.</summary>
    public bool Shadow { get; init; } = true;

    /// <summary>Corners of the window (px).</summary>
    public double CornerRadius { get; init; } = 14;

    /// <summary>The size of the search text (px); results scale with it.</summary>
    public double FontSize { get; init; } = 24;

    /// <summary>The width of the window (px).</summary>
    public double Width { get; init; } = 720;

    /// <summary>Space between the edge of the window and its content (px).</summary>
    public double Padding { get; init; } = 10;

    public bool IsBuiltIn { get; init; }

    /// <summary>When it was changed last (the cloud keeps the newer one).</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>A web search with a keyword: "g heise" searches Google for heise. <see cref="Url"/> holds {query}.</summary>
public sealed record WebSearch
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Typed before the search terms: "g", "wiki", "yt".</summary>
    public required string Keyword { get; init; }

    /// <summary>The address with {query} where the search terms go: https://www.google.com/search?q={query}.</summary>
    public required string Url { get; init; }

    public bool IsEnabled { get; init; } = true;

    public bool IsBuiltIn { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }
}
