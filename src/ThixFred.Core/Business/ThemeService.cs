using System.Globalization;
using ThixFred.Core.Infrastructure;
using ThixFred.Core.Models;

namespace ThixFred.Core.Business;

/// <summary>
/// The themes of the launcher: the built-in ones and the user's own (themes/&lt;id&gt;.json, synced with the cloud, also from the
/// store). Create, change, duplicate, delete and choose; every change is checked here (id, name, colours, sizes) and needs
/// the configuration to be editable. "system" is no theme of its own: it is ThixFred Hell or ThixFred Dunkel, as the system is.
/// </summary>
public static class ThemeService
{
    public const string LightId = "thixfred-hell";
    public const string DarkId = "thixfred-dunkel";

    private static readonly object Gate = new();
    private static List<LauncherTheme>? _custom;

    /// <summary>Raised after every change of a theme or of the choice (here, from the store or the cloud).</summary>
    public static event EventHandler? Changed;

    public static IReadOnlyList<LauncherTheme> BuiltIn { get; } =
    [
        new()
        {
            Id = LightId, Name = "ThixFred Hell", Author = "ThixFred", IsBuiltIn = true, Backdrop = ThemeBackdrop.Acrylic,
            Background = "#E6F9F9FB", Border = "#24000000", Text = "#FF1D1D1F", MutedText = "#FF6E6E73", Selection = "#262F6FED", Accent = "#FF2F6FED",
        },
        new()
        {
            Id = DarkId, Name = "ThixFred Dunkel", Author = "ThixFred", IsBuiltIn = true, IsDark = true, Backdrop = ThemeBackdrop.Acrylic,
            Background = "#E01C1C1E", Border = "#33FFFFFF", Text = "#FFF5F5F7", MutedText = "#FF98989D", Selection = "#4D4C8DFF", Accent = "#FF7AA7FF",
        },
        new()
        {
            Id = "graphit", Name = "Graphit", Author = "ThixFred", IsBuiltIn = true, IsDark = true,
            Background = "#FF2B2D31", Border = "#FF3F4147", Text = "#FFE8E8EA", MutedText = "#FF9A9CA3", Selection = "#FF3D4048", Accent = "#FFF5A524",
            CornerRadius = 10,
        },
        new()
        {
            Id = "ozean", Name = "Ozean", Author = "ThixFred", IsBuiltIn = true, IsDark = true, Backdrop = ThemeBackdrop.Acrylic,
            Background = "#E60B2534", Border = "#4D38BDF8", Text = "#FFE0F2FE", MutedText = "#FF7DD3FC", Selection = "#4D0EA5E9", Accent = "#FF38BDF8",
            CornerRadius = 18,
        },
        new()
        {
            Id = "mitternacht", Name = "Mitternacht", Author = "ThixFred", IsBuiltIn = true, IsDark = true,
            Background = "#FF0B0B12", Border = "#FF262637", Text = "#FFEDEBFF", MutedText = "#FF8B88B0", Selection = "#FF22203A", Accent = "#FFA78BFA",
            CornerRadius = 16, FontSize = 26,
        },
        new()
        {
            Id = "sand", Name = "Sand", Author = "ThixFred", IsBuiltIn = true,
            Background = "#FFFBF6EE", Border = "#FFE6DCCB", Text = "#FF3B2F22", MutedText = "#FF8A7863", Selection = "#FFF1E4CF", Accent = "#FFC2410C",
            CornerRadius = 12,
        },
        new()
        {
            Id = "kompakt", Name = "Kompakt", Author = "ThixFred", IsBuiltIn = true, Backdrop = ThemeBackdrop.Solid,
            Background = "#FFFFFFFF", Border = "#FFD9D9DE", Text = "#FF1D1D1F", MutedText = "#FF6E6E73", Selection = "#FFE8EFFD", Accent = "#FF2F6FED",
            CornerRadius = 8, FontSize = 18, Width = 600, Padding = 6,
        },
    ];

    /// <summary>The built-in themes, then the user's own by name.</summary>
    public static IReadOnlyList<LauncherTheme> List()
    {
        lock (Gate)
        {
            return BuiltIn.Concat(Custom.OrderBy(theme => theme.Name, StringComparer.CurrentCultureIgnoreCase)).ToList();
        }
    }

    public static LauncherTheme? Find(string id) => List().FirstOrDefault(theme => theme.Id == id);

    /// <summary>The theme the launcher shows now: the chosen one, "system" resolved with the system's light or dark.</summary>
    public static LauncherTheme Active(bool systemIsDark)
    {
        var id = SettingsService.Get().LauncherThemeId;
        if (id == LauncherTheme.SystemId)
        {
            return Find(systemIsDark ? DarkId : LightId)!;
        }

        return Find(id) ?? Find(systemIsDark ? DarkId : LightId)!;
    }

    /// <summary>Chooses the theme of the launcher ("system" or an id).</summary>
    public static void Use(string id)
    {
        if (id != LauncherTheme.SystemId && Find(id) is null)
        {
            throw new BusinessException("Dieses Theme gibt es nicht (mehr).");
        }

        SettingsService.Update(settings => settings with { LauncherThemeId = id });
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Creates (empty id) or changes one of the user's themes; a built-in theme cannot be changed (duplicate it).</summary>
    public static LauncherTheme Save(LauncherTheme theme)
    {
        AccessPolicy.EnsureConfigurationEditable();
        if (theme.IsBuiltIn || BuiltIn.Any(builtIn => builtIn.Id == theme.Id))
        {
            throw new BusinessException("Eingebaute Themes lassen sich nicht ändern – „Duplizieren“ macht eine eigene Kopie.");
        }

        var name = theme.Name.Trim();
        if (name.Length is 0 or > 60)
        {
            throw new BusinessException("Bitte einen Namen mit höchstens 60 Zeichen angeben.", nameof(LauncherTheme.Name));
        }

        foreach (var (field, value) in Colors(theme))
        {
            if (!IsColor(value))
            {
                throw new BusinessException("Eine Farbe ist #RRGGBB oder #AARRGGBB (z. B. #2F6FED).", field);
            }
        }

        CheckRange(theme.CornerRadius, 0, 32, nameof(LauncherTheme.CornerRadius), "Die Ecken haben 0 bis 32 px.");
        CheckRange(theme.FontSize, 14, 40, nameof(LauncherTheme.FontSize), "Die Schrift hat 14 bis 40 px.");
        CheckRange(theme.Width, 480, 1200, nameof(LauncherTheme.Width), "Die Breite ist 480 bis 1200 px.");
        CheckRange(theme.Padding, 0, 32, nameof(LauncherTheme.Padding), "Der Innenabstand ist 0 bis 32 px.");

        lock (Gate)
        {
            var id = theme.Id.Length > 0 ? theme.Id : Slug.Make(name, "theme", candidate => List().Any(existing => existing.Id == candidate));
            if (!Slug.IsValid(id))
            {
                throw new BusinessException("Die Kennung besteht aus kleinen Buchstaben, Ziffern und „-“.", nameof(LauncherTheme.Id));
            }

            var saved = theme with { Id = id, Name = name, IsBuiltIn = false, UpdatedAt = AppClock.UtcNow };
            WriteTheme(saved);
            return saved;
        }
    }

    /// <summary>A copy of a theme (built-in or own) under a new name: "Ozean (Kopie)".</summary>
    public static LauncherTheme Duplicate(string id)
    {
        var source = Find(id) ?? throw new BusinessException("Dieses Theme gibt es nicht (mehr).");
        return Save(source with { Id = "", Name = $"{source.Name} (Kopie)", IsBuiltIn = false, Author = "" });
    }

    public static void Delete(string id)
    {
        AccessPolicy.EnsureConfigurationEditable();
        if (BuiltIn.Any(theme => theme.Id == id))
        {
            throw new BusinessException("Eingebaute Themes lassen sich nicht löschen.");
        }

        lock (Gate)
        {
            Custom.RemoveAll(theme => theme.Id == id);
            DeleteFile(id);
        }

        if (SettingsService.Get().LauncherThemeId == id)
        {
            SettingsService.Update(settings => settings with { LauncherThemeId = LauncherTheme.SystemId });
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>"#AARRGGBB" or "#RRGGBB".</summary>
    public static bool IsColor(string? value) =>
        value is { Length: 7 or 9 } && value[0] == '#' && uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);

    /// <summary>The store: a theme file (JSON) installed under its id.</summary>
    internal static LauncherTheme InstallFromStore(string json, string id)
    {
        // the file of the store need not name its id (the catalog does) nor every colour (the defaults fill in)
        var node = System.Text.Json.Nodes.JsonNode.Parse(json) as System.Text.Json.Nodes.JsonObject ?? throw new BusinessException("Das Theme aus dem Store ist beschädigt.");
        node["id"] = id;
        node["name"] ??= id;
        var theme = JsonFile.Deserialize<LauncherTheme>(node.ToJsonString()) ?? throw new BusinessException("Das Theme aus dem Store ist beschädigt.");
        if (BuiltIn.Any(builtIn => builtIn.Id == id))
        {
            throw new BusinessException("Ein Theme des Stores darf kein eingebautes ersetzen.");
        }

        if (Colors(theme).Any(color => !IsColor(color.Value)))
        {
            throw new BusinessException("Das Theme aus dem Store hat ungültige Farben.");
        }

        lock (Gate)
        {
            var saved = theme with { Id = id, IsBuiltIn = false, UpdatedAt = AppClock.UtcNow };
            WriteTheme(saved);
            return saved;
        }
    }

    /// <summary>The sync: the user's themes as they are now.</summary>
    internal static IReadOnlyList<LauncherTheme> Snapshot()
    {
        lock (Gate)
        {
            return Custom.ToList();
        }
    }

    /// <summary>The sync: a theme of the cloud (null = deleted there).</summary>
    internal static void ApplyFromCloud(string id, LauncherTheme? theme)
    {
        if (!Slug.IsValid(id) || BuiltIn.Any(builtIn => builtIn.Id == id))
        {
            return;
        }

        lock (Gate)
        {
            if (theme is null)
            {
                Custom.RemoveAll(existing => existing.Id == id);
                DeleteFile(id);
            }
            else
            {
                WriteTheme(theme with { Id = id, IsBuiltIn = false }, raise: false);
            }
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Tests: read the folder again.</summary>
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            _custom = null;
        }
    }

    private static List<LauncherTheme> Custom => _custom ??= Load();

    private static List<LauncherTheme> Load()
    {
        var themes = new List<LauncherTheme>();
        if (!Directory.Exists(AppPaths.ThemesDirectory))
        {
            return themes;
        }

        foreach (var file in Directory.EnumerateFiles(AppPaths.ThemesDirectory, "*.json"))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            if (Slug.IsValid(id) && JsonFile.Read<LauncherTheme>(file) is { } theme && BuiltIn.All(builtIn => builtIn.Id != id))
            {
                themes.Add(theme with { Id = id, IsBuiltIn = false });
            }
        }

        return themes;
    }

    private static void WriteTheme(LauncherTheme theme, bool raise = true)
    {
        JsonFile.Write(Path.Combine(AppPaths.ThemesDirectory, theme.Id + ".json"), theme);
        Custom.RemoveAll(existing => existing.Id == theme.Id);
        Custom.Add(theme);
        if (raise)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    private static void DeleteFile(string id)
    {
        var path = Path.Combine(AppPaths.ThemesDirectory, id + ".json");
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static IEnumerable<(string Field, string Value)> Colors(LauncherTheme theme) =>
    [
        (nameof(LauncherTheme.Background), theme.Background),
        (nameof(LauncherTheme.Border), theme.Border),
        (nameof(LauncherTheme.Text), theme.Text),
        (nameof(LauncherTheme.MutedText), theme.MutedText),
        (nameof(LauncherTheme.Selection), theme.Selection),
        (nameof(LauncherTheme.Accent), theme.Accent),
    ];

    private static void CheckRange(double value, double minimum, double maximum, string field, string message)
    {
        if (double.IsNaN(value) || value < minimum || value > maximum)
        {
            throw new BusinessException(message, field);
        }
    }
}
