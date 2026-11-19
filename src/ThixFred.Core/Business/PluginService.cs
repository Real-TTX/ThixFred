using System.Text.Json;
using ThixFred.Core.Infrastructure;
using ThixFred.Core.Models;
using ThixFred.Core.Platform;

namespace ThixFred.Core.Business;

/// <summary>
/// Every plugin of the launcher in one list: the built-in providers (programs, calculator, terminal …), the plugin folders
/// (plugins/&lt;id&gt;/plugin.json: from the store or put there by the user) and the user's scripts. Switching on and off, the
/// keywords and running a plugin's script go through here; the policy DisableScripts stops every script (built-in providers
/// keep working).
///
/// A script plugin answers in one of two ways:
///   run:    Enter runs it with what was typed after the keyword; its output goes to the launcher, the clipboard, a terminal
///           or nowhere (plugin.json "output").
///   filter: it runs while typing; it prints JSON <c>{"items":[{"title","subtitle","arg","action","icon","autocomplete"}]}</c>
///           (action: copy | open | terminal | run | query) or one result per line (Enter copies it).
/// </summary>
public static class PluginService
{
    public const string AppsId = "apps";
    public const string CalculatorId = "calculator";
    public const string TerminalId = "terminal";
    public const string RunningId = "running";
    public const string SystemId = "system";
    public const string WebId = "web";
    public const string ThixFredId = "thixfred";

    private const string ManifestFile = "plugin.json";
    private const string StoreMarkerFile = ".store.json";

    private static readonly object Gate = new();
    private static IReadOnlyList<PluginInfo>? _plugins;

    /// <summary>Raised when the list changed (a plugin added, removed, switched, a keyword changed).</summary>
    public static event EventHandler? Changed;

    /// <summary>What ThixFred brings itself (id, name, description, keyword, icon).</summary>
    public static IReadOnlyList<PluginInfo> BuiltIn { get; } =
    [
        Builtin(AppsId, "Programme", "Programme starten: Startmenü, Desktop und Store-Apps (Windows), Programme-Ordner (macOS).", "", "apps"),
        Builtin(CalculatorId, "Rechner", "Rechnet, was du tippst: 3,5 * (2 + 1)^2, sqrt(2), 200 + 15 %, 0xff. Enter kopiert das Ergebnis.", "", "grid"),
        Builtin(TerminalId, "Terminal", "Führt einen Befehl aus: >ping heise.de läuft im Launcher, Umschalt+Enter im Terminal-Fenster.", ">", "terminal"),
        Builtin(RunningId, "Programme beenden", "Laufende Programme schließen: kill word. Umschalt+Enter beendet sofort.", "kill", "x-circle"),
        Builtin(SystemId, "System", "Sperren, Energie sparen, Abmelden, Neu starten, Herunterfahren, Papierkorb leeren.", "", "power"),
        Builtin(WebId, "Web", "Adressen öffnen (heise.de) und Websuchen mit Schlüsselwort (g, wiki, yt …).", "", "globe"),
        Builtin(ThixFredId, "ThixFred", "Befehle von ThixFred: Einstellungen, Plugins, Store, Themes, Programme neu einlesen, Beenden.", "", "rocket"),
    ];

    /// <summary>Built-ins, then plugin folders and scripts by name.</summary>
    public static IReadOnlyList<PluginInfo> List()
    {
        lock (Gate)
        {
            return _plugins ??= Load();
        }
    }

    public static PluginInfo? Find(string id) => List().FirstOrDefault(plugin => plugin.Id == id);

    public static bool IsEnabled(string id) => Find(id)?.IsEnabled ?? false;

    /// <summary>The keyword of a plugin in use (the user's own or its own).</summary>
    public static string KeywordOf(string id) => Find(id)?.Keyword ?? "";

    /// <summary>The plugin whose keyword starts the query ("uuid 5" → the uuid plugin with "5"); null when none does.</summary>
    public static (PluginInfo Plugin, string Argument)? MatchKeyword(string query)
    {
        foreach (var plugin in List())
        {
            if (!plugin.IsEnabled || plugin.Source == PluginSource.BuiltIn || plugin.Keyword.Length == 0)
            {
                continue;
            }

            if (query.Equals(plugin.Keyword, StringComparison.OrdinalIgnoreCase))
            {
                return (plugin, "");
            }

            if (query.Length > plugin.Keyword.Length && query.StartsWith(plugin.Keyword, StringComparison.OrdinalIgnoreCase) && query[plugin.Keyword.Length] == ' ')
            {
                return (plugin, query[(plugin.Keyword.Length + 1)..].TrimStart());
            }
        }

        return null;
    }

    public static void SetEnabled(string id, bool enabled) => ChangePreference(id, preference => preference with { IsEnabled = enabled });

    /// <summary>Another keyword ("" = the plugin's own). Keywords are one word and are not shared by two enabled plugins.</summary>
    public static void SetKeyword(string id, string keyword)
    {
        var value = keyword.Trim().ToLowerInvariant();
        var plugin = Find(id) ?? throw new BusinessException("Dieses Plugin gibt es nicht (mehr).");
        if (value.Any(char.IsWhiteSpace) || value.Length > 20)
        {
            throw new BusinessException("Das Schlüsselwort hat höchstens 20 Zeichen und kein Leerzeichen.", nameof(PluginPreference.Keyword));
        }

        var effective = value.Length > 0 ? value : plugin.DefaultKeyword;
        if (effective.Length == 0 && plugin.Id is TerminalId or RunningId)
        {
            throw new BusinessException("Dieses Plugin braucht ein Schlüsselwort.", nameof(PluginPreference.Keyword));
        }

        var other = List().FirstOrDefault(candidate => candidate.Id != id && candidate.IsEnabled && effective.Length > 0 && candidate.Keyword == effective);
        if (other is not null)
        {
            throw new BusinessException($"„{effective}“ ist schon das Schlüsselwort von „{other.Name}“.", nameof(PluginPreference.Keyword));
        }

        ChangePreference(id, preference => preference with { Keyword = value == plugin.DefaultKeyword ? "" : value });
    }

    /// <summary>Removes a plugin folder (store plugins through <see cref="StoreService.Uninstall"/>) or a script.</summary>
    public static void Remove(string id)
    {
        AccessPolicy.EnsureConfigurationEditable();
        var plugin = Find(id) ?? throw new BusinessException("Dieses Plugin gibt es nicht (mehr).");
        switch (plugin.Source)
        {
            case PluginSource.BuiltIn:
                throw new BusinessException("Eingebaute Plugins lassen sich nur ausschalten.");
            case PluginSource.Script:
                ScriptService.Delete(id["script:".Length..]);
                return;
            case PluginSource.Store:
                StoreService.Uninstall(id);
                return;
            default:
                Directory.Delete(plugin.Folder, recursive: true);
                Invalidate();
                return;
        }
    }

    /// <summary>Enter on a plugin of mode run (or the second step of a filter result): runs the script with the argument.</summary>
    public static async Task<LaunchOutcome> RunAsync(string id, string argument, bool isAction)
    {
        var plugin = Runnable(id);
        if (plugin.Output == ScriptOutput.Terminal && plugin.ScriptPath is { } script)
        {
            var command = OperatingSystem.IsWindows() ? $"& '{script.Replace("'", "''")}' '{argument.Replace("'", "''")}'" : $"'{script.Replace("'", "'\\''")}' '{argument.Replace("'", "'\\''")}'";
            TerminalService.Run(command);
            return LaunchOutcome.Done;
        }

        var result = await RunScriptAsync(plugin, argument, isAction, CancellationToken.None);
        var output = result.Output.Trim();
        if (!result.Succeeded)
        {
            var error = result.TimedOut ? $"{plugin.Name} hat länger als {plugin.TimeoutSeconds} s gebraucht und wurde beendet." :
                result.Error.Trim() is { Length: > 0 } text ? text : $"{plugin.Name} ist mit Code {result.ExitCode} beendet.";
            return LaunchOutcome.Show(error.Length > 600 ? error[..600] + " …" : error, isError: true);
        }

        switch (plugin.Output)
        {
            case ScriptOutput.Clipboard:
                await LaunchService.CopyAsync(output);
                return LaunchOutcome.Done;
            case ScriptOutput.None:
                return LaunchOutcome.Done;
            default:
                return LaunchOutcome.Show(output.Length == 0 ? $"{plugin.Name}: fertig." : output);
        }
    }

    /// <summary>A filter plugin while typing: its results (empty when it fails; the reason is logged).</summary>
    public static async Task<IReadOnlyList<SearchResult>> FilterAsync(string id, string argument, CancellationToken cancellationToken)
    {
        var plugin = Runnable(id);
        var result = await RunScriptAsync(plugin, argument, isAction: false, cancellationToken);
        if (!result.Succeeded)
        {
            Log.Warning($"Plugin {plugin.Id}: {(result.TimedOut ? "timed out" : $"exit code {result.ExitCode}")}");
            return [ErrorResult(plugin, result.TimedOut ? $"Keine Antwort nach {plugin.TimeoutSeconds} s" : FirstLine(result.Error) ?? $"Fehler (Code {result.ExitCode})")];
        }

        return ParseResults(plugin, result.Output);
    }

    /// <summary>The results a filter script printed: JSON items or one result per line.</summary>
    internal static IReadOnlyList<SearchResult> ParseResults(PluginInfo plugin, string output)
    {
        var text = output.Trim();
        var results = new List<SearchResult>();
        if (text.StartsWith('{') || text.StartsWith('['))
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                var items = document.RootElement.ValueKind == JsonValueKind.Array ? document.RootElement :
                    document.RootElement.TryGetProperty("items", out var list) ? list : default;
                if (items.ValueKind == JsonValueKind.Array)
                {
                    var position = 0;
                    foreach (var item in items.EnumerateArray().Take(50))
                    {
                        if (ResultOf(plugin, item, position++) is { } result)
                        {
                            results.Add(result);
                        }
                    }

                    return results;
                }
            }
            catch (JsonException)
            {
                // not JSON after all: lines
            }
        }

        var index = 0;
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(50))
        {
            results.Add(new SearchResult
            {
                Id = $"plugin:{plugin.Id}:{index}",
                Title = line,
                Subtitle = $"{plugin.Name} · Enter kopiert",
                Kind = ResultKind.Plugin,
                Icon = ResultIcon.Of(plugin.Icon),
                Action = new LaunchAction("Kopieren", LaunchActionKind.Copy, line),
                Score = 500 - index++,
            });
        }

        return results;
    }

    /// <summary>The list is read again at the next use (a folder changed, a script was saved, the store installed something).</summary>
    public static void Invalidate()
    {
        lock (Gate)
        {
            _plugins = null;
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>The store: which store plugin is installed in which version (folder marker .store.json).</summary>
    internal static string? InstalledStoreVersion(string id)
    {
        var marker = Path.Combine(AppPaths.PluginsDirectory, id, StoreMarkerFile);
        return JsonFile.Read<StoreMarker>(marker)?.Version;
    }

    internal static void WriteStoreMarker(string folder, string id, string version) =>
        JsonFile.Write(Path.Combine(folder, StoreMarkerFile), new StoreMarker(id, version));

    /// <summary>The manifest of a plugin folder (validated: id, name, a script inside the folder).</summary>
    internal static PluginManifest ReadManifest(string folder)
    {
        var manifest = JsonFile.Read<PluginManifest>(Path.Combine(folder, ManifestFile))
                       ?? throw new BusinessException($"Im Plugin fehlt {ManifestFile} (oder sie ist beschädigt).");
        if (!Slug.IsValid(manifest.Id) || manifest.Name.Trim().Length == 0)
        {
            throw new BusinessException("Das Plugin braucht eine Kennung (kleine Buchstaben, Ziffern, „-“) und einen Namen.");
        }

        foreach (var script in new[] { manifest.Script, manifest.ScriptWindows, manifest.ScriptMacOS }.Where(script => script is not null))
        {
            var full = Path.GetFullPath(Path.Combine(folder, script!));
            if (!full.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new BusinessException("Das Skript eines Plugins muss in seinem Ordner liegen.");
            }
        }

        return manifest;
    }

    private static PluginInfo Runnable(string id)
    {
        AccessPolicy.EnsureScriptsAllowed();
        var plugin = Find(id) ?? throw new BusinessException("Dieses Plugin gibt es nicht (mehr).");
        if (!plugin.IsEnabled)
        {
            throw new BusinessException($"{plugin.Name} ist ausgeschaltet (Plugins).");
        }

        if (!plugin.IsSupported || plugin.ScriptPath is null)
        {
            throw new BusinessException($"{plugin.Name} läuft auf diesem System nicht (es fehlt das Programm für sein Skript).");
        }

        return plugin;
    }

    private static async Task<ScriptResult> RunScriptAsync(PluginInfo plugin, string argument, bool isAction, CancellationToken cancellationToken)
    {
        try
        {
            return await ScriptRunner.RunAsync(plugin.ScriptPath!, argument, TimeSpan.FromSeconds(plugin.TimeoutSeconds), isAction, cancellationToken);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            throw new BusinessException($"{plugin.Name} ließ sich nicht starten: {exception.Message}");
        }
    }

    private static SearchResult? ResultOf(PluginInfo plugin, JsonElement item, int position)
    {
        string? Text(string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        var title = Text("title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var argument = Text("arg") ?? title;
        var action = (Text("action") ?? "copy").ToLowerInvariant() switch
        {
            "open" => new LaunchAction("Öffnen", LaunchActionKind.Open, argument),
            "terminal" => new LaunchAction("Im Terminal ausführen", LaunchActionKind.Terminal, argument),
            "run" => new LaunchAction("Ausführen", LaunchActionKind.Script, plugin.Id, argument),
            "query" => new LaunchAction("Weiter", LaunchActionKind.SetQuery, argument),
            _ => new LaunchAction("Kopieren", LaunchActionKind.Copy, argument),
        };
        return new SearchResult
        {
            Id = $"plugin:{plugin.Id}:{Text("uid") ?? argument}",
            Title = title,
            Subtitle = Text("subtitle") ?? plugin.Name,
            Kind = ResultKind.Plugin,
            Icon = ResultIcon.Of(Text("icon") ?? plugin.Icon),
            Action = action,
            Autocomplete = Text("autocomplete"),
            Score = 500 - position,
        };
    }

    private static SearchResult ErrorResult(PluginInfo plugin, string message) => new()
    {
        Id = $"plugin:{plugin.Id}:error",
        Title = $"{plugin.Name}: {message}",
        Subtitle = "Das Plugin hat keine Ergebnisse geliefert",
        Kind = ResultKind.Plugin,
        Icon = ResultIcon.Of("alert"),
        Action = new LaunchAction("Plugins öffnen", LaunchActionKind.App, nameof(AppCommand.Plugins)),
        Score = 400,
    };

    private static string? FirstLine(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

    private static void ChangePreference(string id, Func<PluginPreference, PluginPreference> change)
    {
        SettingsService.Update(settings =>
        {
            var preferences = settings.Plugins.ToDictionary(pair => pair.Key, pair => pair.Value);
            var next = change(preferences.GetValueOrDefault(id) ?? new PluginPreference());
            if (next.IsEnabled && next.Keyword.Length == 0)
            {
                preferences.Remove(id);
            }
            else
            {
                preferences[id] = next;
            }

            return settings with { Plugins = preferences };
        });
        Invalidate();
    }

    private static IReadOnlyList<PluginInfo> Load()
    {
        var preferences = SettingsService.Get().Plugins;
        var scriptsAllowed = !AccessPolicy.AreScriptsDisabled;
        PluginInfo Apply(PluginInfo plugin)
        {
            var preference = preferences.GetValueOrDefault(plugin.Id);
            var allowed = plugin.Source == PluginSource.BuiltIn || scriptsAllowed;
            return plugin with
            {
                IsEnabled = (preference?.IsEnabled ?? true) && allowed && plugin.IsSupported,
                Keyword = preference is { Keyword.Length: > 0 } ? preference.Keyword : plugin.DefaultKeyword,
            };
        }

        var plugins = BuiltIn.Select(Apply).ToList();
        var others = new List<PluginInfo>();
        if (Directory.Exists(AppPaths.PluginsDirectory))
        {
            foreach (var folder in Directory.EnumerateDirectories(AppPaths.PluginsDirectory))
            {
                try
                {
                    others.Add(Apply(FromFolder(folder)));
                }
                catch (BusinessException exception)
                {
                    Log.Warning($"Plugin folder {Path.GetFileName(folder)}: {exception.Message}");
                }
            }
        }

        foreach (var script in ScriptService.List())
        {
            var path = ScriptService.FileOf(script.Id);
            others.Add(Apply(new PluginInfo
            {
                Id = $"script:{script.Id}",
                Name = script.Name,
                Description = script.Description,
                DefaultKeyword = script.Keyword,
                Icon = script.Mode == PluginMode.Filter ? "list" : "code",
                Mode = script.Mode,
                Output = script.Output,
                Source = PluginSource.Script,
                Folder = AppPaths.ScriptsDirectory,
                ScriptPath = path,
                IsSupported = path is not null && ScriptRunner.InterpreterFor(path) is not null,
                Author = "Eigenes Skript",
            }));
        }

        plugins.AddRange(others.OrderBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase));
        return plugins;
    }

    private static PluginInfo FromFolder(string folder)
    {
        var manifest = ReadManifest(folder);
        var script = OperatingSystem.IsWindows() ? manifest.ScriptWindows ?? manifest.Script : OperatingSystem.IsMacOS() ? manifest.ScriptMacOS ?? manifest.Script : manifest.Script;
        var path = script is null ? null : Path.GetFullPath(Path.Combine(folder, script));
        var fromStore = File.Exists(Path.Combine(folder, StoreMarkerFile));
        return new PluginInfo
        {
            Id = manifest.Id,
            Name = manifest.Name.Trim(),
            Description = manifest.Description,
            Version = manifest.Version,
            Author = manifest.Author,
            DefaultKeyword = manifest.Keyword.Trim().ToLowerInvariant(),
            Icon = manifest.Icon,
            Mode = manifest.Mode,
            Output = manifest.Output,
            Source = fromStore ? PluginSource.Store : PluginSource.Folder,
            Folder = folder,
            ScriptPath = path,
            TimeoutSeconds = Math.Clamp(manifest.TimeoutSeconds, 1, 120),
            IsSupported = path is not null && File.Exists(path) && ScriptRunner.InterpreterFor(path) is not null,
            Homepage = manifest.Homepage,
        };
    }

    private static PluginInfo Builtin(string id, string name, string description, string keyword, string icon) => new()
    {
        Id = id,
        Name = name,
        Description = description,
        DefaultKeyword = keyword,
        Keyword = keyword,
        Icon = icon,
        Source = PluginSource.BuiltIn,
        Author = "ThixFred",
    };

    private sealed record StoreMarker(string Id, string Version);
}
