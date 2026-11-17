using ThixFred.Core.Infrastructure;
using ThixFred.Core.Models;

namespace ThixFred.Core.Business;

/// <summary>
/// The sample computer of the snapshots and UI tests: a program index (no real files), two scripts, a store plugin, an own theme,
/// a learned usage and the catalog of the store. Only ever written into a test or temporary data folder.
/// </summary>
internal static class SampleData
{
    public static IReadOnlyList<AppEntry> Apps { get; } =
    [
        App("Visual Studio Code", @"C:\Users\Anna\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Visual Studio Code\Visual Studio Code.lnk", @"C:\Users\Anna\AppData\Local\Programs\Microsoft VS Code\Code.exe", "code"),
        App("Visual Studio 2026", @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Visual Studio 2026.lnk", @"C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\devenv.exe", "devenv"),
        App("Visio", @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Visio.lnk", @"C:\Program Files\Microsoft Office\root\Office16\VISIO.EXE", "visio"),
        App("Google Chrome", @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Google Chrome.lnk", @"C:\Program Files\Google\Chrome\Application\chrome.exe", "chrome"),
        App("Firefox", @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Firefox.lnk", @"C:\Program Files\Mozilla Firefox\firefox.exe", "firefox"),
        App("Microsoft Word", @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Word.lnk", @"C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE", "winword"),
        App("Microsoft Excel", @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Excel.lnk", @"C:\Program Files\Microsoft Office\root\Office16\EXCEL.EXE", "excel"),
        App("Outlook", @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Outlook (classic).lnk", @"C:\Program Files\Microsoft Office\root\Office16\OUTLOOK.EXE", "outlook"),
        App("Spotify", @"C:\Users\Anna\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Spotify.lnk", @"C:\Users\Anna\AppData\Roaming\Spotify\Spotify.exe", "spotify"),
        App("Notepad++", @"C:\ProgramData\Microsoft\Windows\Start Menu\Programs\Notepad++.lnk", @"C:\Program Files\Notepad++\notepad++.exe", "notepad++"),
        App("Terminal", @"shell:AppsFolder\Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", "", "wt", AppEntryKind.StoreApp),
        App("Rechner", @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", "", "calc,calculator", AppEntryKind.StoreApp),
        App("Bluetooth- und andere Geräte", "ms-settings:bluetooth", "Windows-Einstellungen", "bluetooth", AppEntryKind.SystemSetting),
        App("Geräte-Manager", "devmgmt.msc", "Windows", "device manager", AppEntryKind.SystemSetting),
    ];

    /// <summary>What the store offers in the pictures.</summary>
    public static IReadOnlyList<StoreListing> StoreListings { get; } =
    [
        Listing("uuid", StoreItemKind.Plugin, "UUID", "Erzeugt neue UUIDs zum Kopieren.", "uuid", "key", "1.1.0", "1.0.0"),
        Listing("ip", StoreItemKind.Plugin, "Meine IP", "Zeigt die lokale und die öffentliche IP-Adresse.", "ip", "network", "1.0.0", "1.0.0"),
        Listing("passwort", StoreItemKind.Plugin, "Passwort-Generator", "Sichere Passwörter in der gewünschten Länge.", "pw", "lock", "1.2.0", null),
        Listing("zeit", StoreItemKind.Plugin, "Zeitstempel", "Unix-Zeit ⇄ Datum, ISO 8601, Kalenderwoche.", "ts", "clock", "1.0.0", null),
        Listing("base64", StoreItemKind.Plugin, "Base64", "Text kodieren und dekodieren.", "b64", "code", "1.0.0", null),
        Listing("nordlicht", StoreItemKind.Theme, "Nordlicht", "Dunkles Glas mit grünem Akzent.", "", "palette", "1.0.0", null),
        Listing("papier", StoreItemKind.Theme, "Papier", "Hell, ruhig, ohne Glas.", "", "palette", "1.0.0", null),
    ];

    /// <summary>Fills the (test) data folder: the index, scripts, a plugin folder of the store, a theme, usage.</summary>
    public static void Create()
    {
        AppIndex.SetEntriesForTests(Apps);
        ScriptService.Save(new UserScript
        {
            Id = "meine-ip",
            Name = "Meine IP",
            Keyword = "myip",
            Description = "Zeigt die öffentliche IP-Adresse dieses Computers",
            Language = ScriptLanguage.PowerShell,
            Mode = PluginMode.Run,
            Output = ScriptOutput.Launcher,
            Body = "param([string]$Query)\n\n(Invoke-RestMethod https://api.ipify.org?format=json).ip\n",
        });
        ScriptService.Save(new UserScript
        {
            Id = "projekte",
            Name = "Projekte",
            Keyword = "pr",
            Description = "Öffnet ein Projekt in Visual Studio Code",
            Language = ScriptLanguage.Python,
            Mode = PluginMode.Filter,
            Body = "import json, os, sys\n\nquery = sys.argv[1] if len(sys.argv) > 1 else \"\"\nroot = os.path.expanduser(\"~/Projekte\")\nitems = [{\"title\": name, \"subtitle\": os.path.join(root, name), \"arg\": os.path.join(root, name), \"action\": \"open\"}\n         for name in sorted(os.listdir(root)) if query.lower() in name.lower()]\nprint(json.dumps({\"items\": items}))\n",
        });

        var plugin = Path.Combine(AppPaths.PluginsDirectory, "uuid");
        Directory.CreateDirectory(plugin);
        JsonFile.Write(Path.Combine(plugin, "plugin.json"), new PluginManifest
        {
            Id = "uuid",
            Name = "UUID",
            Version = "1.0.0",
            Author = "ThixFred",
            Description = "Erzeugt neue UUIDs zum Kopieren.",
            Keyword = "uuid",
            Icon = "key",
            Mode = PluginMode.Filter,
            ScriptWindows = "uuid.ps1",
            ScriptMacOS = "uuid.sh",
        });
        File.WriteAllText(Path.Combine(plugin, "uuid.ps1"), "1..5 | ForEach-Object { [guid]::NewGuid().ToString() }\n");
        File.WriteAllText(Path.Combine(plugin, "uuid.sh"), "#!/bin/bash\nfor i in 1 2 3 4 5; do uuidgen | tr 'A-Z' 'a-z'; done\n");
        PluginService.WriteStoreMarker(plugin, "uuid", "1.0.0");
        PluginService.Invalidate();

        ThemeService.Save(new LauncherTheme
        {
            Id = "abendrot",
            Name = "Abendrot",
            Author = "Anna",
            IsDark = true,
            Background = "#FF2A1A24",
            Border = "#FF4A2C3A",
            Text = "#FFFFE8EE",
            MutedText = "#FFC79AAE",
            Selection = "#FF44283A",
            Accent = "#FFFF7A93",
            CornerRadius = 16,
        });

        foreach (var (id, times) in new[] { ("app:" + Apps[0].Path, 9), ("app:" + Apps[3].Path, 6), ("app:" + Apps[5].Path, 3), ("command:Settings", 1) })
        {
            for (var time = 0; time < times; time++)
            {
                UsageService.Record(id, "");
            }
        }

        UsageService.RememberCommand("ping heise.de");
        UsageService.RememberCommand("ipconfig /all");
    }

    private static AppEntry App(string name, string path, string target, string keywords, AppEntryKind kind = AppEntryKind.Shortcut) =>
        new() { Name = name, Path = path, Target = target, Keywords = keywords, Kind = kind };

    private static StoreListing Listing(string id, StoreItemKind kind, string name, string description, string keyword, string icon, string version, string? installed) =>
        new(new StoreItem
        {
            Id = id,
            Kind = kind,
            Name = name,
            Description = description,
            Keyword = keyword,
            Icon = icon,
            Version = version,
            Author = "ThixFred",
            Download = $"{id}.zip",
            Sha256 = new string('0', 64),
            UpdatedAt = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero),
        }, installed, IsSupported: true);
}
