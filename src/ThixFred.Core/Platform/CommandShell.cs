using System.Diagnostics;
using System.Text;

namespace ThixFred.Core.Platform;

/// <summary>
/// The shell behind the integrated terminal: one command line runs in a process of its own with its output redirected.
///   Windows: cmd.exe, switched to UTF-8 first (chcp 65001), so umlauts of ping, dir and ipconfig arrive as they are.
///   macOS:   zsh as login shell (-l): the PATH of the user's profile (Homebrew), not the bare one of an app.
/// No pseudo console: programs with a screen of their own (vim, htop, ssh asking a password) need the terminal window.
/// </summary>
public static class CommandShell
{
    /// <summary>What starts <paramref name="command"/> in <paramref name="workingDirectory"/> (public for the tests).</summary>
    public static ProcessStartInfo StartInfo(string command, string workingDirectory)
    {
        var startInfo = OperatingSystem.IsWindows()
            // "/s /c" takes everything between the first and the last quote as it is: no escaping of the user's quotes
            ? new ProcessStartInfo("cmd.exe") { Arguments = $"/d /s /c \"chcp 65001 >nul & {command}\"" }
            : new ProcessStartInfo("/bin/zsh") { ArgumentList = { "-l", "-c", command } };
        startInfo.WorkingDirectory = workingDirectory;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;
        startInfo.Environment["THIXFRED"] = "1";
        if (!OperatingSystem.IsWindows())
        {
            startInfo.Environment["TERM"] = "dumb";
        }

        return startInfo;
    }

    /// <summary>The prompt before a command: "C:\Users\anna&gt;" (Windows) or "~/Projekte %" (macOS).</summary>
    public static string Prompt(string workingDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            return workingDirectory + ">";
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var shown = workingDirectory.StartsWith(home, StringComparison.Ordinal) ? "~" + workingDirectory[home.Length..] : workingDirectory;
        return shown + " %";
    }
}
