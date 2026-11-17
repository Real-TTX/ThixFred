using System.Text;
using ThixFred.Core.Infrastructure;
using ThixFred.Core.Models;

namespace ThixFred.Core.Business;

/// <summary>
/// The user's own scripts (scripts folder, synced with the cloud): a plugin can be a single script. Each script is one file;
/// its first comment lines describe it, so a file written by hand works as well as one written in ThixFred:
/// <code>
/// # @name Meine IP
/// # @keyword ip
/// # @mode run            (run | filter)
/// # @output launcher     (launcher | clipboard | terminal | none)
/// # @description Zeigt die öffentliche IP-Adresse
/// </code>
/// ("//" in JavaScript, "::" in batch files). Create, change, delete: checked here; needs the configuration to be editable and
/// scripts to be allowed (policy DisableScripts).
/// </summary>
public static class ScriptService
{
    private const string Marker = "@";
    private static readonly string[] Tags = ["name", "keyword", "mode", "output", "description"];

    /// <summary>Raised after a script was saved or deleted (here or from the cloud).</summary>
    public static event EventHandler? Changed;

    public static string Extension(ScriptLanguage language) => language switch
    {
        ScriptLanguage.PowerShell => ".ps1",
        ScriptLanguage.Batch => ".cmd",
        ScriptLanguage.Bash => ".sh",
        ScriptLanguage.Zsh => ".zsh",
        ScriptLanguage.Python => ".py",
        ScriptLanguage.JavaScript => ".js",
        _ => ".txt",
    };

    public static string DisplayName(ScriptLanguage language) => language switch
    {
        ScriptLanguage.PowerShell => "PowerShell",
        ScriptLanguage.Batch => "Batch (cmd)",
        ScriptLanguage.Bash => "Bash",
        ScriptLanguage.Zsh => "Zsh",
        ScriptLanguage.Python => "Python",
        ScriptLanguage.JavaScript => "JavaScript (Node)",
        _ => language.ToString(),
    };

    /// <summary>The languages of this system first (PowerShell on Windows, Zsh on macOS).</summary>
    public static IReadOnlyList<ScriptLanguage> Languages => OperatingSystem.IsMacOS()
        ? [ScriptLanguage.Zsh, ScriptLanguage.Bash, ScriptLanguage.Python, ScriptLanguage.JavaScript, ScriptLanguage.PowerShell]
        : [ScriptLanguage.PowerShell, ScriptLanguage.Batch, ScriptLanguage.Python, ScriptLanguage.JavaScript, ScriptLanguage.Bash];

    public static ScriptLanguage? LanguageOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".ps1" => ScriptLanguage.PowerShell,
        ".cmd" or ".bat" => ScriptLanguage.Batch,
        ".sh" => ScriptLanguage.Bash,
        ".zsh" => ScriptLanguage.Zsh,
        ".py" => ScriptLanguage.Python,
        ".js" => ScriptLanguage.JavaScript,
        _ => null,
    };

    /// <summary>A new script to start from: it shows what a script gets and gives.</summary>
    public static UserScript Template(ScriptLanguage language) => new()
    {
        Id = "",
        Name = "",
        Language = language,
        Body = language switch
        {
            ScriptLanguage.PowerShell => "param([string]$Query)\n\n# $Query ist, was nach dem Schlüsselwort getippt wurde.\n\"Hallo $Query\"\n",
            ScriptLanguage.Batch => "@echo off\nrem %1 ist, was nach dem Schlüsselwort getippt wurde.\necho Hallo %~1\n",
            ScriptLanguage.Python => "import sys\n\nquery = sys.argv[1] if len(sys.argv) > 1 else \"\"\nprint(f\"Hallo {query}\")\n",
            ScriptLanguage.JavaScript => "const query = process.argv[2] ?? \"\";\nconsole.log(`Hallo ${query}`);\n",
            _ => "query=\"$1\"\necho \"Hallo $query\"\n",
        },
    };

    public static IReadOnlyList<UserScript> List()
    {
        if (!Directory.Exists(AppPaths.ScriptsDirectory))
        {
            return [];
        }

        var scripts = new List<UserScript>();
        foreach (var file in Directory.EnumerateFiles(AppPaths.ScriptsDirectory))
        {
            if (LanguageOf(file) is not null && Slug.IsValid(Path.GetFileNameWithoutExtension(file)) && Read(file) is { } script)
            {
                scripts.Add(script);
            }
        }

        return scripts.OrderBy(script => script.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static UserScript? Find(string id) => List().FirstOrDefault(script => script.Id == id);

    /// <summary>The file of a script (whatever its language).</summary>
    public static string? FileOf(string id) =>
        !Directory.Exists(AppPaths.ScriptsDirectory) ? null :
        Directory.EnumerateFiles(AppPaths.ScriptsDirectory, id + ".*").FirstOrDefault(file => LanguageOf(file) is not null && Path.GetFileNameWithoutExtension(file) == id);

    /// <summary>Creates (empty id) or changes a script; a changed language renames its file.</summary>
    public static UserScript Save(UserScript script)
    {
        AccessPolicy.EnsureConfigurationEditable();
        AccessPolicy.EnsureScriptsAllowed();
        var name = script.Name.Trim();
        var keyword = script.Keyword.Trim().ToLowerInvariant();
        if (name.Length is 0 or > 60)
        {
            throw new BusinessException("Bitte einen Namen mit höchstens 60 Zeichen angeben.", nameof(UserScript.Name));
        }

        if (keyword.Length == 0 || keyword.Length > 20 || keyword.Any(char.IsWhiteSpace))
        {
            throw new BusinessException("Das Schlüsselwort braucht 1 bis 20 Zeichen ohne Leerzeichen.", nameof(UserScript.Keyword));
        }

        if (script.Description.Contains('\n'))
        {
            throw new BusinessException("Die Beschreibung hat nur eine Zeile.", nameof(UserScript.Description));
        }

        if (script.Body.Trim().Length == 0)
        {
            throw new BusinessException("Das Skript ist leer.", nameof(UserScript.Body));
        }

        var existingFile = script.Id.Length > 0 ? FileOf(script.Id) : null;
        var id = script.Id.Length > 0 ? script.Id : Slug.Make(name, "skript", candidate => FileOf(candidate) is not null);
        if (!Slug.IsValid(id))
        {
            throw new BusinessException("Die Kennung besteht aus kleinen Buchstaben, Ziffern und „-“.");
        }

        var conflicting = PluginService.List().FirstOrDefault(plugin => plugin.Id != $"script:{id}" && plugin.IsEnabled && plugin.Keyword == keyword);
        if (conflicting is not null)
        {
            throw new BusinessException($"Das Schlüsselwort „{keyword}“ hat schon „{conflicting.Name}“.", nameof(UserScript.Keyword));
        }

        var saved = script with { Id = id, Name = name, Keyword = keyword, Description = script.Description.Trim(), UpdatedAt = AppClock.UtcNow };
        var path = Path.Combine(AppPaths.ScriptsDirectory, id + Extension(saved.Language));
        Directory.CreateDirectory(AppPaths.ScriptsDirectory);
        WriteFile(path, Compose(saved));
        if (existingFile is not null && !string.Equals(existingFile, path, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(existingFile);
        }

        Raise();
        return saved;
    }

    public static void Delete(string id)
    {
        AccessPolicy.EnsureConfigurationEditable();
        if (FileOf(id) is { } file)
        {
            File.Delete(file);
            Raise();
        }
    }

    /// <summary>The whole file: shebang (where it helps), the header and the body. Same input, same text (the sync hashes it).</summary>
    public static string Compose(UserScript script)
    {
        var comment = CommentOf(script.Language);
        var builder = new StringBuilder();
        var shebang = script.Language switch
        {
            ScriptLanguage.Bash => "#!/bin/bash",
            ScriptLanguage.Zsh => "#!/bin/zsh",
            ScriptLanguage.Python => "#!/usr/bin/env python3",
            ScriptLanguage.JavaScript => "#!/usr/bin/env node",
            _ => null,
        };
        if (shebang is not null)
        {
            builder.Append(shebang).Append('\n');
        }

        if (script.Language == ScriptLanguage.Batch && !script.Body.TrimStart().StartsWith("@echo off", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append("@echo off\n");
        }

        void Tag(string tag, string value)
        {
            if (value.Length > 0)
            {
                builder.Append(comment).Append(' ').Append(Marker).Append(tag).Append(' ').Append(value).Append('\n');
            }
        }

        Tag("name", script.Name);
        Tag("keyword", script.Keyword);
        Tag("mode", script.Mode.ToString().ToLowerInvariant());
        Tag("output", script.Output.ToString().ToLowerInvariant());
        Tag("description", script.Description);
        builder.Append('\n').Append(script.Body.Replace("\r\n", "\n").TrimEnd('\n')).Append('\n');
        return builder.ToString();
    }

    /// <summary>Reads a script file: its header and its body; null when it cannot be read.</summary>
    public static UserScript? Read(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return Parse(Path.GetFileNameWithoutExtension(path), LanguageOf(path) ?? ScriptLanguage.Bash, text, File.GetLastWriteTimeUtc(path));
    }

    internal static UserScript Parse(string id, ScriptLanguage language, string text, DateTime written)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bodyStart = 0;
        for (var index = 0; index < Math.Min(lines.Count, 40); index++)
        {
            var line = lines[index].Trim();
            if (line.StartsWith("#!", StringComparison.Ordinal) || (language == ScriptLanguage.Batch && line.Equals("@echo off", StringComparison.OrdinalIgnoreCase) && values.Count == 0))
            {
                bodyStart = index + 1;
                continue;
            }

            var tag = HeaderTag(line, out var value);
            if (tag is null)
            {
                break;
            }

            values[tag] = value;
            bodyStart = index + 1;
        }

        if (bodyStart < lines.Count && lines[bodyStart].Trim().Length == 0)
        {
            bodyStart++;
        }

        var body = string.Join('\n', lines.Skip(bodyStart));
        return new UserScript
        {
            Id = id,
            Name = values.GetValueOrDefault("name") is { Length: > 0 } name ? name : id,
            Keyword = (values.GetValueOrDefault("keyword") ?? id).ToLowerInvariant(),
            Description = values.GetValueOrDefault("description") ?? "",
            Language = language,
            Mode = Enum.TryParse<PluginMode>(values.GetValueOrDefault("mode"), true, out var mode) ? mode : PluginMode.Run,
            Output = Enum.TryParse<ScriptOutput>(values.GetValueOrDefault("output"), true, out var output) ? output : ScriptOutput.Launcher,
            Body = body,
            UpdatedAt = new DateTimeOffset(DateTime.SpecifyKind(written, DateTimeKind.Utc)),
        };
    }

    /// <summary>The sync: id → (file name, the whole text).</summary>
    internal static IReadOnlyList<(string Id, string FileName, string Content)> Snapshot()
    {
        if (!Directory.Exists(AppPaths.ScriptsDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(AppPaths.ScriptsDirectory)
            .Where(file => LanguageOf(file) is not null && Slug.IsValid(Path.GetFileNameWithoutExtension(file)))
            .Select(file => (Path.GetFileNameWithoutExtension(file), Path.GetFileName(file), File.ReadAllText(file).Replace("\r\n", "\n")))
            .ToList();
    }

    /// <summary>The sync: a script of the cloud (null content = deleted there).</summary>
    internal static void ApplyFromCloud(string id, string? fileName, string? content)
    {
        if (!Slug.IsValid(id))
        {
            return;
        }

        foreach (var existing in Directory.Exists(AppPaths.ScriptsDirectory) ? Directory.EnumerateFiles(AppPaths.ScriptsDirectory, id + ".*").ToList() : [])
        {
            if (Path.GetFileNameWithoutExtension(existing) == id && (content is null || !string.Equals(Path.GetFileName(existing), fileName, StringComparison.OrdinalIgnoreCase)))
            {
                File.Delete(existing);
            }
        }

        if (content is not null && fileName is not null && Path.GetFileNameWithoutExtension(fileName) == id && LanguageOf(fileName) is not null)
        {
            Directory.CreateDirectory(AppPaths.ScriptsDirectory);
            WriteFile(Path.Combine(AppPaths.ScriptsDirectory, fileName), content);
        }

        Raise();
    }

    private static string? HeaderTag(string line, out string value)
    {
        value = "";
        foreach (var prefix in new[] { "#", "//", "::", "rem ", "REM " })
        {
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var rest = line[prefix.Length..].TrimStart();
            if (!rest.StartsWith(Marker, StringComparison.Ordinal))
            {
                return null;
            }

            var space = rest.IndexOfAny([' ', ':', '\t']);
            var tag = (space < 0 ? rest[1..] : rest[1..space]).ToLowerInvariant();
            if (!Tags.Contains(tag))
            {
                return null;
            }

            value = space < 0 ? "" : rest[(space + 1)..].TrimStart(':', ' ', '\t').Trim();
            return tag;
        }

        return null;
    }

    private static string CommentOf(ScriptLanguage language) => language switch
    {
        ScriptLanguage.Batch => "::",
        ScriptLanguage.JavaScript => "//",
        _ => "#",
    };

    /// <summary>Batch files in CRLF (cmd needs them), the others in LF; PowerShell with a BOM (Windows PowerShell 5.1 reads UTF-8 only so); executable on macOS.</summary>
    private static void WriteFile(string path, string content)
    {
        var isBatch = Path.GetExtension(path).ToLowerInvariant() is ".cmd" or ".bat";
        var text = isBatch ? content.Replace("\r\n", "\n").Replace("\n", "\r\n") : content.Replace("\r\n", "\n");
        var isPowerShell = Path.GetExtension(path).Equals(".ps1", StringComparison.OrdinalIgnoreCase);
        File.WriteAllText(path, text.TrimStart((char)0xFEFF), new UTF8Encoding(encoderShouldEmitUTF8Identifier: isPowerShell));
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute);
        }
    }

    private static void Raise()
    {
        PluginService.Invalidate();
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
