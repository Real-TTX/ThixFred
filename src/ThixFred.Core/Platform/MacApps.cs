using ThixFred.Core.Business;

namespace ThixFred.Core.Platform;

/// <summary>
/// The programs of macOS: the .app bundles of the Applications folders (two levels deep: "Microsoft Office/…" and
/// "Utilities/…") and of extra folders. The bundle's folder name is its name; the system apps also have their German names
/// as keywords ("Rechner" finds Calculator.app).
/// </summary>
internal static class MacApps
{
    private static readonly Dictionary<string, string> GermanNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["System Settings"] = "Systemeinstellungen",
        ["System Preferences"] = "Systemeinstellungen",
        ["Calculator"] = "Rechner",
        ["Calendar"] = "Kalender",
        ["Notes"] = "Notizen",
        ["Reminders"] = "Erinnerungen",
        ["Contacts"] = "Kontakte",
        ["Photos"] = "Fotos",
        ["Preview"] = "Vorschau",
        ["Activity Monitor"] = "Aktivitätsanzeige",
        ["Disk Utility"] = "Festplattendienstprogramm",
        ["Music"] = "Musik",
        ["Maps"] = "Karten",
        ["Messages"] = "Nachrichten",
        ["Weather"] = "Wetter",
        ["Clock"] = "Uhr",
        ["Books"] = "Bücher",
        ["Shortcuts"] = "Kurzbefehle",
        ["Stocks"] = "Aktien",
        ["Screenshot"] = "Bildschirmfoto",
        ["Font Book"] = "Schriftsammlung",
        ["Keychain Access"] = "Schlüsselbundverwaltung",
        ["Console"] = "Konsole",
        ["Image Capture"] = "Digitale Bilder",
        ["Voice Memos"] = "Sprachmemos",
        ["Photo Booth"] = "Photo Booth",
        ["Migration Assistant"] = "Migrationsassistent",
        ["System Information"] = "Systeminformationen",
        ["Find My"] = "Wo ist?",
        ["Home"] = "Home",
        ["Passwords"] = "Passwörter",
        ["Phone"] = "Telefon",
        ["Games"] = "Spiele",
        ["Journal"] = "Journal",
    };

    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static IEnumerable<AppEntry> Scan(IReadOnlyList<string> extraFolders)
    {
        var entries = new List<AppEntry>();
        foreach (var folder in WatchedFolders().Concat(extraFolders).Where(Directory.Exists).Distinct())
        {
            entries.AddRange(ScanFolder(folder, depth: 2));
        }

        foreach (var app in new[] { "/System/Library/CoreServices/Finder.app" }.Where(Directory.Exists))
        {
            entries.Add(Entry(app));
        }

        return entries;
    }

    public static IEnumerable<string> WatchedFolders() =>
    [
        "/Applications",
        "/Applications/Utilities",
        "/System/Applications",
        "/System/Applications/Utilities",
        Path.Combine(Home, "Applications"),
    ];

    private static IEnumerable<AppEntry> ScanFolder(string folder, int depth)
    {
        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(folder).ToList();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var child in children)
        {
            if (child.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                yield return Entry(child);
            }
            else if (depth > 1 && !Path.GetFileName(child).StartsWith('.'))
            {
                foreach (var nested in ScanFolder(child, depth - 1))
                {
                    yield return nested;
                }
            }
        }
    }

    private static AppEntry Entry(string bundle)
    {
        var name = Path.GetFileNameWithoutExtension(bundle);
        var german = GermanNames.GetValueOrDefault(name);
        return new AppEntry
        {
            Name = german ?? name,
            Path = bundle,
            Kind = AppEntryKind.MacApp,
            Target = bundle,
            Keywords = german is null ? "" : name,
        };
    }
}
