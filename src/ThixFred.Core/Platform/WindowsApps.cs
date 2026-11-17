using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using ThixFred.Core.Business;
using ThixFred.Core.Infrastructure;
using ThixFred.Core.Platform.Windows;

namespace ThixFred.Core.Platform;

/// <summary>
/// The programs of Windows: the shortcuts of the Start menus and desktops (their targets are read, uninstallers and help
/// files left out), .exe files of extra folders, the apps of the Microsoft Store (Get-StartApps, in the background) and the
/// most used pages of the settings and tools of the system.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsApps
{
    private static readonly string[] SkippedWords = ["uninstall", "deinstall", "entfernen", "readme", "liesmich", "help", "hilfe", "documentation", "dokumentation", "license", "lizenz", "release notes"];

    private static readonly string[] SkippedTargetExtensions = [".txt", ".chm", ".pdf", ".html", ".htm", ".rtf", ".hlp", ".ini", ".log", ".md"];

    /// <summary>Pages of the settings and tools of Windows: German name, what opens, English words.</summary>
    private static readonly (string Name, string Path, string Keywords)[] SystemEntries =
    [
        ("Einstellungen", "ms-settings:", "settings"),
        ("Bluetooth- und andere Geräte", "ms-settings:bluetooth", "bluetooth,devices"),
        ("WLAN", "ms-settings:network-wifi", "wifi,wlan,network"),
        ("Netzwerk und Internet", "ms-settings:network", "network,ethernet,netzwerk"),
        ("Anzeige", "ms-settings:display", "display,monitor,bildschirm"),
        ("Sound", "ms-settings:sound", "sound,audio,ton"),
        ("Benachrichtigungen", "ms-settings:notifications", "notifications"),
        ("Energie und Akku", "ms-settings:powersleep", "power,battery,energie"),
        ("Installierte Apps", "ms-settings:appsfeatures", "apps,programs,uninstall,deinstallieren"),
        ("Standard-Apps", "ms-settings:defaultapps", "default apps"),
        ("Windows Update", "ms-settings:windowsupdate", "update"),
        ("Datum und Uhrzeit", "ms-settings:dateandtime", "date,time,zeit"),
        ("Personalisierung", "ms-settings:personalization", "personalization,hintergrund,wallpaper"),
        ("Drucker und Scanner", "ms-settings:printers", "printer,drucker,scanner"),
        ("Maus", "ms-settings:mousetouchpad", "mouse,touchpad"),
        ("Tastatur", "ms-settings:keyboard", "keyboard"),
        ("Datenschutz und Sicherheit", "ms-settings:privacy", "privacy"),
        ("Windows-Sicherheit", "windowsdefender:", "security,defender,antivirus"),
        ("Systemsteuerung", "control.exe", "control panel"),
        ("Geräte-Manager", "devmgmt.msc", "device manager"),
        ("Datenträgerverwaltung", "diskmgmt.msc", "disk management"),
        ("Dienste", "services.msc", "services"),
        ("Ereignisanzeige", "eventvwr.msc", "event viewer"),
        ("Task-Manager", "taskmgr.exe", "task manager"),
        ("Registrierungs-Editor", "regedit.exe", "regedit,registry"),
        ("Datei-Explorer", "explorer.exe", "explorer,files,dateien"),
        ("Eingabeaufforderung", "cmd.exe", "cmd,command prompt"),
        ("Netzwerkverbindungen", "ncpa.cpl", "network connections,adapter"),
        ("Systeminformationen", "msinfo32.exe", "system information"),
    ];

    public static IEnumerable<AppEntry> Scan(IReadOnlyList<string> extraFolders, bool includeStoreApps)
    {
        var entries = new List<AppEntry>();
        foreach (var folder in ShortcutFolders())
        {
            entries.AddRange(ScanShortcuts(folder, recursive: !IsDesktop(folder)));
        }

        foreach (var folder in extraFolders.Where(Directory.Exists))
        {
            entries.AddRange(ScanShortcuts(folder, recursive: true));
            entries.AddRange(ScanPrograms(folder));
        }

        if (includeStoreApps)
        {
            entries.AddRange(StoreApps(entries));
        }

        entries.AddRange(SystemEntries.Select(entry => new AppEntry
        {
            Name = entry.Name,
            Path = entry.Path,
            Kind = AppEntryKind.SystemSetting,
            Keywords = entry.Keywords,
            Target = entry.Path.StartsWith("ms-settings:", StringComparison.Ordinal) ? "Windows-Einstellungen" : "Windows",
        }));
        return entries;
    }

    public static IEnumerable<string> WatchedFolders() => ShortcutFolders();

    private static IEnumerable<string> ShortcutFolders() =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
    ];

    private static bool IsDesktop(string folder) =>
        folder == Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) ||
        folder == Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);

    private static IEnumerable<AppEntry> ScanShortcuts(string folder, bool recursive)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            yield break;
        }

        var options = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true, MaxRecursionDepth = 6 };
        foreach (var file in Directory.EnumerateFiles(folder, "*", options))
        {
            var extension = Path.GetExtension(file).ToLowerInvariant();
            if (extension is not (".lnk" or ".url" or ".appref-ms"))
            {
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(file);
            if (IsSkippedName(name))
            {
                continue;
            }

            var target = extension == ".lnk" ? ShellLink.ReadTarget(file) ?? "" : extension == ".url" ? ReadUrl(file) : "";
            if (target.Length > 0 && SkippedTargetExtensions.Contains(Path.GetExtension(target).ToLowerInvariant()))
            {
                continue;
            }

            yield return new AppEntry
            {
                Name = name,
                Path = file,
                Kind = AppEntryKind.Shortcut,
                Target = target,
                Keywords = target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(target) : "",
            };
        }
    }

    private static IEnumerable<AppEntry> ScanPrograms(string folder)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 3 };
        foreach (var file in Directory.EnumerateFiles(folder, "*.exe", options))
        {
            var name = ProgramName(file);
            if (!IsSkippedName(name) && !IsSkippedName(Path.GetFileName(file)))
            {
                yield return new AppEntry { Name = name, Path = file, Kind = AppEntryKind.Program, Target = file, Keywords = Path.GetFileNameWithoutExtension(file) };
            }
        }
    }

    /// <summary>The product name of a program file ("Visual Studio Code" for Code.exe), else its file name.</summary>
    private static string ProgramName(string file)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(file);
            var name = string.IsNullOrWhiteSpace(info.FileDescription) ? info.ProductName : info.FileDescription;
            return string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(file) : name.Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Path.GetFileNameWithoutExtension(file);
        }
    }

    /// <summary>The address of an internet shortcut (.url: an INI file).</summary>
    private static string ReadUrl(string file)
    {
        try
        {
            return File.ReadLines(file).FirstOrDefault(line => line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))?[4..].Trim() ?? "";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private static bool IsSkippedName(string name)
    {
        var lower = name.ToLowerInvariant();
        return SkippedWords.Any(word => lower.Contains(word, StringComparison.Ordinal));
    }

    /// <summary>
    /// The apps of the Microsoft Store (Rechner, Fotos, Einstellungen …) as Get-StartApps lists them; only packaged apps (their
    /// AppID has a '!'), the others came with their shortcuts already. Opened through shell:AppsFolder\&lt;AppID&gt;.
    /// </summary>
    private static IEnumerable<AppEntry> StoreApps(List<AppEntry> known)
    {
        var json = RunPowerShell("Get-StartApps | Select-Object Name, AppID | ConvertTo-Json -Compress");
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        var names = known.Select(entry => entry.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<AppEntry>();
        try
        {
            using var document = JsonDocument.Parse(json);
            var items = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement.EnumerateArray().ToList() : [document.RootElement];
            foreach (var item in items)
            {
                var name = item.TryGetProperty("Name", out var nameValue) ? nameValue.GetString() ?? "" : "";
                var appId = item.TryGetProperty("AppID", out var idValue) ? idValue.GetString() ?? "" : "";
                if (name.Length == 0 || !appId.Contains('!') || names.Contains(name) || IsSkippedName(name))
                {
                    continue;
                }

                result.Add(new AppEntry { Name = name, Path = $@"shell:AppsFolder\{appId}", Kind = AppEntryKind.StoreApp, Target = "Microsoft Store-App" });
            }
        }
        catch (JsonException exception)
        {
            Log.Warning($"Program index: the Store apps could not be read ({exception.Message})");
        }

        return result;
    }

    private static string RunPowerShell(string command)
    {
        try
        {
            var startInfo = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "[Console]::OutputEncoding = [Text.Encoding]::UTF8; " + command })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(20_000))
            {
                process.Kill();
                return "";
            }

            return output.Result;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warning($"Program index: PowerShell is not available ({exception.Message})");
            return "";
        }
    }
}
