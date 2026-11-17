using System.Text.RegularExpressions;
using ThixFred.Core.Models;

namespace ThixFred.Core.Business;

/// <summary>
/// The search of the launcher. It runs on every key, synchronously and in memory (the program index, the commands, the
/// calculator): nothing waits for a disk or a network. Only plugins of mode filter run a script; the launcher asks them
/// afterwards (<see cref="FilterPlugin"/>, <see cref="PluginService.FilterAsync"/>).
///
/// Order: a keyword (">", "kill", "g", a plugin's) decides first; then calculation, programs, commands, system and web by
/// score (the match, raised by usage: <see cref="UsageService"/>); "Im Web suchen" is the fallback at the end.
/// </summary>
public static partial class SearchService
{
    /// <summary>The words of the system commands (German and English).</summary>
    private static readonly (SystemCommand Command, string Name, string Keywords, string Icon, string? Confirmation)[] SystemCommands =
    [
        (SystemCommand.Lock, "Bildschirm sperren", "sperren,lock", "lock", null),
        (SystemCommand.Sleep, "Energie sparen", "ruhezustand,standby,sleep", "moon", null),
        (SystemCommand.SignOut, "Abmelden", "abmelden,log out,logout,sign out", "logout", "Wirklich abmelden? Nicht gespeicherte Arbeit geht verloren."),
        (SystemCommand.Restart, "Neu starten", "neustart,restart,reboot", "refresh", "Wirklich neu starten? Nicht gespeicherte Arbeit geht verloren."),
        (SystemCommand.ShutDown, "Herunterfahren", "ausschalten,shutdown,shut down,power off", "power", "Wirklich herunterfahren? Nicht gespeicherte Arbeit geht verloren."),
        (SystemCommand.EmptyTrash, OperatingSystem.IsMacOS() ? "Papierkorb entleeren" : "Papierkorb leeren", "papierkorb,trash,recycle bin", "trash", "Den Papierkorb endgültig leeren?"),
    ];

    /// <summary>The commands of ThixFred (all start with "ThixFred" too).</summary>
    private static readonly (AppCommand Command, string Name, string Keywords, string Icon)[] AppCommands =
    [
        (AppCommand.Settings, "ThixFred: Einstellungen", "einstellungen,settings,preferences,optionen", "settings"),
        (AppCommand.Plugins, "ThixFred: Plugins und Skripte", "plugins,skripte,scripts,erweiterungen", "plug"),
        (AppCommand.Store, "ThixFred: Store", "store,plugin store,themes laden", "store"),
        (AppCommand.Themes, "ThixFred: Themes", "themes,aussehen,design,farben", "palette"),
        (AppCommand.WebSearches, "ThixFred: Websuchen", "websuchen,web searches,suchmaschinen", "globe"),
        (AppCommand.About, "Über ThixFred", "über,about,version", "info"),
        (AppCommand.Reindex, "ThixFred: Programme neu einlesen", "neu einlesen,reindex,index,rescan", "refresh"),
        (AppCommand.CheckForUpdates, "ThixFred: Nach Updates suchen", "updates,aktualisieren", "download"),
        (AppCommand.Quit, "ThixFred beenden", "beenden,quit,exit", "x-circle"),
    ];

    private static readonly HashSet<string> FileExtensions =
        ["exe", "msc", "cpl", "lnk", "dll", "txt", "md", "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "json", "xml", "js", "ts", "cs", "py", "sh", "ps1", "bat", "cmd", "zip", "png", "jpg", "gif", "app", "log", "ini", "csv"];

    private static readonly string[] RunningAliases = ["kill", "quit", "beenden", "schließen", "close"];

    /// <summary>The results for a query, best first, at most <paramref name="max"/>.</summary>
    public static IReadOnlyList<SearchResult> Search(string query, int max)
    {
        var text = query.Trim();
        if (text.Length == 0)
        {
            return Recent(max);
        }

        var prepared = FuzzyMatcher.Prepare(text);
        var results = new List<SearchResult>();

        // 1. a keyword decides
        if (KeywordResults(query, text) is { } keyworded)
        {
            return Rank(keyworded, prepared, max);
        }

        // 2. everything that matches
        if (PluginService.IsEnabled(PluginService.CalculatorId) && Calculator.TryEvaluate(text) is { } value)
        {
            results.Add(CalculationResult(text, value));
        }

        if (PluginService.IsEnabled(PluginService.AppsId))
        {
            foreach (var (entry, score) in AppIndex.Find(prepared))
            {
                results.Add(AppResult(entry, score * 8));
            }
        }

        if (PluginService.IsEnabled(PluginService.SystemId))
        {
            foreach (var command in SystemCommands)
            {
                if (MatchScore(prepared, command.Name, command.Keywords) is > 0 and var score)
                {
                    results.Add(SystemResult(command, score * 7.5));
                }
            }
        }

        if (PluginService.IsEnabled(PluginService.ThixFredId))
        {
            foreach (var command in AppCommands)
            {
                var score = Math.Max(MatchScore(prepared, command.Name, command.Keywords), MatchScore(prepared, command.Name.Replace("ThixFred: ", ""), ""));
                if (score > 0)
                {
                    results.Add(AppCommandResult(command, score * 7));
                }
            }
        }

        if (PluginService.IsEnabled(PluginService.WebId))
        {
            if (AddressOf(text) is { } address)
            {
                results.Add(new SearchResult
                {
                    Id = $"url:{address}",
                    Title = $"{text} öffnen",
                    Subtitle = address,
                    Kind = ResultKind.Url,
                    Icon = ResultIcon.Of(address.StartsWith("http", StringComparison.Ordinal) ? "link" : "folder-open"),
                    Action = new LaunchAction("Öffnen", LaunchActionKind.Open, address),
                    AlternateAction = address.StartsWith("http", StringComparison.Ordinal) ? new LaunchAction("Adresse kopieren", LaunchActionKind.Copy, address) : new LaunchAction("Im Ordner zeigen", LaunchActionKind.Reveal, address),
                    Score = 950,
                });
            }

            foreach (var search in WebSearchService.List().Where(search => search.IsEnabled))
            {
                var score = FuzzyMatcher.Score(prepared, FuzzyMatcher.Prepare(search.Keyword)) is 100 ? 60 : 0;
                if (score > 0)
                {
                    results.Add(WebSearchHint(search, score * 6));
                }
            }
        }

        // plugins and scripts by their name: Enter fills in their keyword
        foreach (var plugin in PluginService.List().Where(plugin => plugin.IsEnabled && plugin.Source != PluginSource.BuiltIn && plugin.Keyword.Length > 0))
        {
            var score = MatchScore(prepared, plugin.Name, plugin.Keyword);
            if (score > 0)
            {
                results.Add(new SearchResult
                {
                    Id = $"plugin:{plugin.Id}",
                    Title = plugin.Name,
                    Subtitle = plugin.Description.Length > 0 ? $"{plugin.Keyword} · {plugin.Description}" : $"Schlüsselwort: {plugin.Keyword}",
                    Kind = ResultKind.Plugin,
                    Icon = ResultIcon.Of(plugin.Icon),
                    Action = plugin.Mode == PluginMode.Run
                        ? new LaunchAction("Ausführen", LaunchActionKind.Script, plugin.Id, "")
                        : new LaunchAction("Weiter", LaunchActionKind.SetQuery, plugin.Keyword + " "),
                    Autocomplete = plugin.Keyword + " ",
                    Score = score * 6.5,
                });
            }
        }

        // 3. the fallback
        var settings = SettingsService.Get();
        if (settings.WebSearchFallback && PluginService.IsEnabled(PluginService.WebId) &&
            WebSearchService.Find(settings.DefaultWebSearchId) is { IsEnabled: true } fallback)
        {
            results.Add(WebSearchResult(fallback, text, 0) with { IsFallback = true });
        }

        return Rank(results, prepared, max);
    }

    /// <summary>A filter plugin that answers this query (its keyword starts it), and the text after the keyword.</summary>
    public static (PluginInfo Plugin, string Argument)? FilterPlugin(string query) =>
        PluginService.MatchKeyword(query.TrimStart()) is { Plugin.Mode: PluginMode.Filter } match ? match : null;

    /// <summary>Usage counts on top of the provider's score; fallbacks stay last.</summary>
    internal static IReadOnlyList<SearchResult> Rank(IEnumerable<SearchResult> results, string prepared, int max) =>
        results
            .Select(result => result.IsFallback ? result : result with { Score = result.Score + 3 * UsageService.Boost(result.Id, prepared) })
            .OrderBy(result => result.IsFallback)
            .ThenByDescending(result => result.Score)
            .ThenBy(result => result.Kind)
            .ThenBy(result => result.Title, StringComparer.CurrentCultureIgnoreCase)
            .DistinctBy(result => result.Id)
            .Take(Math.Max(1, max))
            .ToList();

    /// <summary>"heise.de", "https://…", "localhost:8080", a path that exists ("C:\Users", "~/Downloads"): what Open gets; else null.</summary>
    internal static string? AddressOf(string text)
    {
        if (text.Contains(' '))
        {
            return null;
        }

        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return Uri.TryCreate(text, UriKind.Absolute, out _) ? text : null;
        }

        if (text.StartsWith('~') || Path.IsPathFullyQualified(text))
        {
            var path = text.StartsWith('~') ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), text[1..].TrimStart('/', '\\')) : text;
            return File.Exists(path) || Directory.Exists(path) ? path : null;
        }

        if (!HostRegex().IsMatch(text))
        {
            return null;
        }

        // "notepad.exe", "readme.md", "node.js" are names of files and programs, no addresses
        var host = text.Split('/', ':')[0];
        return FileExtensions.Contains(host[(host.LastIndexOf('.') + 1)..].ToLowerInvariant()) ? null : "https://" + text;
    }

    private static List<SearchResult>? KeywordResults(string query, string text)
    {
        var lower = text.ToLowerInvariant();

        // ">ping heise.de" (the keyword of the terminal, ">" by default, may be glued to the command)
        var terminal = PluginService.Find(PluginService.TerminalId);
        if (terminal is { IsEnabled: true } && terminal.Keyword.Length > 0 && lower.StartsWith(terminal.Keyword, StringComparison.Ordinal) &&
            (IsSymbol(terminal.Keyword) || text.Length == terminal.Keyword.Length || text[terminal.Keyword.Length] == ' '))
        {
            return TerminalResults(text[terminal.Keyword.Length..].Trim());
        }

        // "kill word"
        var running = PluginService.Find(PluginService.RunningId);
        if (running is { IsEnabled: true })
        {
            foreach (var alias in RunningAliases.Prepend(running.Keyword).Where(alias => alias.Length > 0).Distinct())
            {
                if (lower == alias || lower.StartsWith(alias + " ", StringComparison.Ordinal))
                {
                    return RunningResults(text[alias.Length..].Trim());
                }
            }
        }

        // a plugin or script with its keyword
        if (PluginService.MatchKeyword(text) is { } match)
        {
            if (match.Plugin.Mode == PluginMode.Filter)
            {
                return []; // the launcher asks the script (FilterPlugin)
            }

            return
            [
                new SearchResult
                {
                    Id = $"plugin:{match.Plugin.Id}",
                    Title = match.Argument.Length > 0 ? $"{match.Plugin.Name}: {match.Argument}" : match.Plugin.Name,
                    Subtitle = match.Plugin.Description.Length > 0 ? match.Plugin.Description : "Enter führt das Skript aus",
                    Kind = ResultKind.Plugin,
                    Icon = ResultIcon.Of(match.Plugin.Icon),
                    Action = new LaunchAction("Ausführen", LaunchActionKind.Script, match.Plugin.Id, match.Argument),
                    Score = 1000,
                },
            ];
        }

        // "g heise" – a web search with its keyword and terms
        if (PluginService.IsEnabled(PluginService.WebId))
        {
            var space = text.IndexOf(' ');
            if (space > 0 && WebSearchService.FindByKeyword(text[..space]) is { } search && text[(space + 1)..].Trim().Length > 0)
            {
                return [WebSearchResult(search, text[(space + 1)..], 1000)];
            }
        }

        return null;
    }

    /// <summary>A keyword of one sign (">", "$") may be glued to what follows: ">ping".</summary>
    private static bool IsSymbol(string keyword) => keyword.Length == 1 && !char.IsLetterOrDigit(keyword[0]);

    private static List<SearchResult> TerminalResults(string command)
    {
        if (command.Length == 0)
        {
            var recent = UsageService.RecentCommands().Take(8).Select((previous, index) => new SearchResult
            {
                Id = $"terminal:{previous}",
                Title = previous,
                Subtitle = "Zuletzt ausgeführt · Enter führt erneut aus · Tab übernimmt",
                Kind = ResultKind.Terminal,
                Icon = ResultIcon.Of("terminal"),
                Action = new LaunchAction("Im Terminal ausführen", LaunchActionKind.Terminal, previous),
                AlternateAction = OperatingSystem.IsWindows() ? new LaunchAction("Als Administrator ausführen", LaunchActionKind.Terminal, previous, "admin") : null,
                Autocomplete = ">" + previous,
                Score = 900 - index,
            }).ToList();
            if (recent.Count > 0)
            {
                return recent;
            }

            return
            [
                new SearchResult
                {
                    Id = "terminal:",
                    Title = "Befehl im Terminal ausführen",
                    Subtitle = "Tippe den Befehl nach „>“, z. B. >ping heise.de",
                    Kind = ResultKind.Terminal,
                    Icon = ResultIcon.Of("terminal"),
                    Action = new LaunchAction("Weiter", LaunchActionKind.SetQuery, ">"),
                    Score = 900,
                },
            ];
        }

        var terminal = TerminalService.DisplayName(SettingsService.Get().Terminal);
        return
        [
            new SearchResult
            {
                Id = $"terminal:{command}",
                Title = command,
                Subtitle = $"Im Terminal ausführen ({terminal})" + (OperatingSystem.IsWindows() ? " · Umschalt+Enter als Administrator" : ""),
                Kind = ResultKind.Terminal,
                Icon = ResultIcon.Of("terminal"),
                Action = new LaunchAction("Im Terminal ausführen", LaunchActionKind.Terminal, command),
                AlternateAction = OperatingSystem.IsWindows() ? new LaunchAction("Als Administrator ausführen", LaunchActionKind.Terminal, command, "admin") : null,
                Score = 1000,
            },
        ];
    }

    private static List<SearchResult> RunningResults(string filter)
    {
        var prepared = FuzzyMatcher.Prepare(filter);
        var results = new List<SearchResult>();
        foreach (var app in ProcessService.List())
        {
            var score = prepared.Length == 0 ? 50 : Math.Max(FuzzyMatcher.Score(prepared, FuzzyMatcher.Prepare(app.Name), FuzzyMatcher.Initials(app.Name)),
                FuzzyMatcher.Score(prepared, FuzzyMatcher.Prepare(app.WindowTitle)) - 10);
            if (score <= 0)
            {
                continue;
            }

            results.Add(new SearchResult
            {
                Id = $"running:{app.ProcessId}",
                Title = $"{app.Name} schließen",
                Subtitle = app.WindowTitle != app.Name ? $"{app.WindowTitle} · Umschalt+Enter beendet sofort" : "Umschalt+Enter beendet sofort",
                Kind = ResultKind.RunningApplication,
                Icon = new ResultIcon("x-circle", app.ExecutablePath),
                Action = new LaunchAction("Schließen", LaunchActionKind.CloseProcess, app.ProcessId.ToString()),
                AlternateAction = new LaunchAction("Sofort beenden", LaunchActionKind.KillProcess, app.ProcessId.ToString())
                {
                    Confirmation = $"{app.Name} sofort beenden? Nicht gespeicherte Arbeit geht verloren.",
                },
                Score = score * 10,
            });
        }

        if (results.Count == 0)
        {
            results.Add(new SearchResult
            {
                Id = "running:none",
                Title = filter.Length == 0 ? "Kein Programm mit Fenster läuft" : $"Kein laufendes Programm passt zu „{filter}“",
                Subtitle = "Programme beenden",
                Kind = ResultKind.RunningApplication,
                Icon = ResultIcon.Of("x-circle"),
                Action = new LaunchAction("Weiter", LaunchActionKind.SetQuery, PluginService.KeywordOf(PluginService.RunningId) + " "),
            });
        }

        return results;
    }

    private static IReadOnlyList<SearchResult> Recent(int max)
    {
        var results = new List<SearchResult>();
        foreach (var id in UsageService.Recent(max * 2))
        {
            if (id.StartsWith("app:", StringComparison.Ordinal) && AppIndex.FindById(id) is { } entry && PluginService.IsEnabled(PluginService.AppsId))
            {
                results.Add(AppResult(entry, 100 - results.Count) with { Subtitle = "Zuletzt verwendet · " + Location(entry) });
            }
            else if (id.StartsWith("command:", StringComparison.Ordinal) && Enum.TryParse<AppCommand>(id["command:".Length..], out var command) &&
                     AppCommands.FirstOrDefault(entry => entry.Command == command) is { Name.Length: > 0 } appCommand)
            {
                results.Add(AppCommandResult(appCommand, 100 - results.Count) with { Subtitle = "Zuletzt verwendet" });
            }

            if (results.Count >= max)
            {
                break;
            }
        }

        return results;
    }

    private static int MatchScore(string prepared, string name, string keywords)
    {
        var score = FuzzyMatcher.Score(prepared, FuzzyMatcher.Prepare(name), FuzzyMatcher.Initials(name));
        foreach (var keyword in keywords.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            score = Math.Max(score, FuzzyMatcher.Score(prepared, FuzzyMatcher.Prepare(keyword)) - 5);
        }

        // short queries only find commands by the start of a word: "ne" must not offer "Neu starten" before "Netflix"
        return prepared.Length < 3 && score < 70 ? 0 : score;
    }

    private static SearchResult CalculationResult(string text, double value)
    {
        var formatted = Calculator.Format(value);
        var bases = Calculator.OtherBases(value);
        return new SearchResult
        {
            Id = "calc",
            Title = $"= {formatted}",
            Subtitle = (bases is null ? "" : bases + " · ") + "Enter kopiert das Ergebnis",
            Kind = ResultKind.Calculation,
            Icon = ResultIcon.Of("grid"),
            Action = new LaunchAction("Ergebnis kopieren", LaunchActionKind.Copy, formatted),
            AlternateAction = new LaunchAction("Rechnung kopieren", LaunchActionKind.Copy, $"{text.TrimStart('=').Trim()} = {formatted}"),
            Autocomplete = "=" + formatted,
            Score = 2000,
        };
    }

    private static SearchResult AppResult(AppEntry entry, double score)
    {
        var revealTarget = entry.Kind switch
        {
            AppEntryKind.Shortcut when File.Exists(entry.Target) => entry.Target,
            AppEntryKind.StoreApp or AppEntryKind.SystemSetting => null,
            _ => entry.Path,
        };
        var alternate = OperatingSystem.IsWindows() && entry.Kind is AppEntryKind.Shortcut or AppEntryKind.Program
            ? new LaunchAction("Als Administrator öffnen", LaunchActionKind.OpenAsAdministrator, entry.Path)
            : revealTarget is not null ? new LaunchAction(OperatingSystem.IsMacOS() ? "Im Finder zeigen" : "Im Ordner zeigen", LaunchActionKind.Reveal, revealTarget) : null;
        return new SearchResult
        {
            Id = entry.ResultId,
            Title = entry.Name,
            Subtitle = Location(entry),
            Kind = entry.Kind == AppEntryKind.SystemSetting ? ResultKind.System : ResultKind.Application,
            Icon = new ResultIcon(entry.Kind == AppEntryKind.SystemSetting ? "settings" : "apps", entry.Kind == AppEntryKind.SystemSetting && entry.Path.Contains(':') ? null : entry.Path),
            Action = new LaunchAction("Öffnen", LaunchActionKind.Open, entry.Path),
            AlternateAction = alternate,
            Score = score,
        };
    }

    private static string Location(AppEntry entry) => entry.Kind switch
    {
        AppEntryKind.StoreApp => "Microsoft Store-App",
        AppEntryKind.SystemSetting => entry.Target,
        AppEntryKind.MacApp => Path.GetDirectoryName(entry.Path) ?? "",
        _ => entry.Target.Length > 0 ? entry.Target : entry.Path,
    };

    private static SearchResult SystemResult((SystemCommand Command, string Name, string Keywords, string Icon, string? Confirmation) command, double score) => new()
    {
        Id = $"system:{command.Command}",
        Title = command.Name,
        Subtitle = "System",
        Kind = ResultKind.System,
        Icon = ResultIcon.Of(command.Icon),
        Action = new LaunchAction(command.Name, LaunchActionKind.System, command.Command.ToString()) { Confirmation = command.Confirmation },
        Score = score,
    };

    private static SearchResult AppCommandResult((AppCommand Command, string Name, string Keywords, string Icon) command, double score) => new()
    {
        Id = $"command:{command.Command}",
        Title = command.Name,
        Subtitle = "ThixFred",
        Kind = ResultKind.Command,
        Icon = ResultIcon.Of(command.Icon),
        Action = new LaunchAction(command.Name, LaunchActionKind.App, command.Command.ToString()),
        Score = score,
    };

    private static SearchResult WebSearchResult(WebSearch search, string terms, double score)
    {
        var url = WebSearchService.BuildUrl(search, terms);
        return new SearchResult
        {
            Id = $"web:{search.Id}",
            Title = $"„{terms.Trim()}“ mit {search.Name} suchen",
            Subtitle = url,
            Kind = ResultKind.WebSearch,
            Icon = ResultIcon.Of("search"),
            Action = new LaunchAction("Suchen", LaunchActionKind.Open, url),
            AlternateAction = new LaunchAction("Adresse kopieren", LaunchActionKind.Copy, url),
            Score = score,
        };
    }

    private static SearchResult WebSearchHint(WebSearch search, double score) => new()
    {
        Id = $"websearch:{search.Id}",
        Title = $"Mit {search.Name} suchen",
        Subtitle = $"{search.Keyword} <Suchbegriff>",
        Kind = ResultKind.WebSearch,
        Icon = ResultIcon.Of("search"),
        Action = new LaunchAction("Weiter", LaunchActionKind.SetQuery, search.Keyword + " "),
        Autocomplete = search.Keyword + " ",
        Score = score,
    };

    [GeneratedRegex(@"^(localhost|([a-z0-9-]+\.)+[a-z]{2,})(:\d{1,5})?(/\S*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex HostRegex();
}
