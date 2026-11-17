using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace ThixFred.Core.Platform;

/// <summary>What a script printed and how it ended.</summary>
public sealed record ScriptResult(int ExitCode, string Output, string Error, bool TimedOut)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

/// <summary>
/// Runs the script of a plugin with the program its extension asks for (.ps1 PowerShell, .cmd/.bat cmd, .sh bash, .zsh zsh,
/// .py Python, .js Node, .exe or an executable file directly). The script gets what was typed as its first argument and in
/// THIXFRED_QUERY, its folder in THIXFRED_PLUGIN_DIR and, for the second step of a filter result, THIXFRED_ACTION=run. Output
/// is read as UTF-8 (cmd: the OEM code page); a script that takes longer than its time is ended with everything it started.
/// </summary>
internal static class ScriptRunner
{
    /// <summary>Tests replace the process with an answer.</summary>
    internal static Func<string, string, ScriptResult>? RunForTests { get; set; }

    static ScriptRunner() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>The program that runs a script of this extension on this system; null when it is not installed.</summary>
    public static string? InterpreterFor(string scriptPath)
    {
        var extension = Path.GetExtension(scriptPath).ToLowerInvariant();
        return extension switch
        {
            ".ps1" => Shell.FindProgram(OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh") ?? (OperatingSystem.IsWindows() ? "powershell.exe" : null),
            ".cmd" or ".bat" => OperatingSystem.IsWindows() ? "cmd.exe" : null,
            ".sh" => OperatingSystem.IsWindows() ? Shell.FindProgram("bash.exe") : "/bin/bash",
            ".zsh" => OperatingSystem.IsWindows() ? null : "/bin/zsh",
            ".py" => OperatingSystem.IsWindows() ? Shell.FindProgram("python.exe") ?? Shell.FindProgram("py.exe") : Shell.FindProgram("python3"),
            ".js" => Shell.FindProgram(OperatingSystem.IsWindows() ? "node.exe" : "node"),
            ".exe" => OperatingSystem.IsWindows() ? scriptPath : null,
            "" => OperatingSystem.IsWindows() ? null : scriptPath,
            _ => null,
        };
    }

    public static async Task<ScriptResult> RunAsync(string scriptPath, string argument, TimeSpan timeout, bool isAction, CancellationToken cancellationToken)
    {
        if (RunForTests is { } fake)
        {
            return fake(scriptPath, argument);
        }

        var interpreter = InterpreterFor(scriptPath) ?? throw new InvalidOperationException($"Für {Path.GetExtension(scriptPath)} ist auf diesem Computer kein Programm installiert.");
        var extension = Path.GetExtension(scriptPath).ToLowerInvariant();
        var isBatch = extension is ".cmd" or ".bat";
        var startInfo = new ProcessStartInfo(interpreter)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(scriptPath)!,
            StandardOutputEncoding = isBatch ? Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage) : Encoding.UTF8,
            StandardErrorEncoding = isBatch ? Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage) : Encoding.UTF8,
        };
        startInfo.Environment["THIXFRED_QUERY"] = argument;
        startInfo.Environment["THIXFRED_SCRIPT"] = scriptPath;
        startInfo.Environment["THIXFRED_PLUGIN_DIR"] = Path.GetDirectoryName(scriptPath)!;
        startInfo.Environment["THIXFRED_ACTION"] = isAction ? "run" : "filter";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";

        switch (extension)
        {
            case ".ps1":
                // the path and the text through the environment: no quoting can break them; UTF-8 for umlauts in the output
                foreach (var part in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command",
                             "[Console]::OutputEncoding = [Text.Encoding]::UTF8; & $env:THIXFRED_SCRIPT $env:THIXFRED_QUERY; exit $LASTEXITCODE" })
                {
                    startInfo.ArgumentList.Add(part);
                }

                break;
            case ".cmd" or ".bat":
                startInfo.ArgumentList.Add("/d");
                startInfo.ArgumentList.Add("/c");
                startInfo.ArgumentList.Add(scriptPath);
                startInfo.ArgumentList.Add(argument);
                break;
            case ".exe" or "":
                startInfo.ArgumentList.Add(argument);
                break;
            default:
                startInfo.ArgumentList.Add(scriptPath);
                startInfo.ArgumentList.Add(argument);
                break;
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Das Skript ließ sich nicht starten.");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new ScriptResult(-1, Limit(await output), Limit(await error), TimedOut: true);
        }

        return new ScriptResult(process.ExitCode, Limit(await output), Limit(await error), TimedOut: false);
    }

    /// <summary>At most 1 MB of output: what a launcher shows.</summary>
    private static string Limit(string text) => text.Length <= 1_000_000 ? text : text[..1_000_000];
}
