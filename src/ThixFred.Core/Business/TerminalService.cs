using ThixFred.Core.Infrastructure;
using ThixFred.Core.Models;
using ThixFred.Core.Platform;

namespace ThixFred.Core.Business;

/// <summary>
/// "&gt;ping heise.de": the command runs in a terminal window that stays open (the output can be read, the shell goes on).
///   Windows: Windows Terminal with PowerShell when it is installed (Automatisch), else PowerShell / the command prompt in its
///            own window; Umschalt+Enter starts it with administrator rights.
///   macOS:   Terminal or iTerm (AppleScript "do script" / "write text").
/// The last commands are remembered (<see cref="UsageService"/>): "&gt;" alone lists them.
/// </summary>
public static class TerminalService
{
    /// <summary>The terminals this system offers in the settings.</summary>
    public static IReadOnlyList<TerminalChoice> Choices => OperatingSystem.IsMacOS()
        ? [TerminalChoice.Automatic, TerminalChoice.MacTerminal, TerminalChoice.ITerm]
        : [TerminalChoice.Automatic, TerminalChoice.WindowsTerminal, TerminalChoice.PowerShell, TerminalChoice.CommandPrompt];

    public static string DisplayName(TerminalChoice choice) => choice switch
    {
        TerminalChoice.WindowsTerminal => "Windows Terminal",
        TerminalChoice.PowerShell => "PowerShell",
        TerminalChoice.CommandPrompt => "Eingabeaufforderung (cmd)",
        TerminalChoice.MacTerminal => "Terminal",
        TerminalChoice.ITerm => "iTerm",
        _ => OperatingSystem.IsMacOS() ? "Automatisch (Terminal)" : "Automatisch (Windows Terminal, sonst PowerShell)",
    };

    /// <summary>Runs <paramref name="command"/> in the chosen terminal; <paramref name="asAdministrator"/> on Windows with elevation.</summary>
    public static void Run(string command, bool asAdministrator = false)
    {
        var text = command.Trim();
        if (text.Length == 0)
        {
            throw new BusinessException("Bitte einen Befehl nach „>“ eingeben, z. B. >ping heise.de");
        }

        UsageService.RememberCommand(text);
        var choice = SettingsService.Get().Terminal;
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                RunMac(text, choice);
            }
            else
            {
                RunWindows(text, choice, asAdministrator);
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warning($"Terminal could not start ({exception.Message})");
            throw new BusinessException($"Das Terminal ließ sich nicht starten: {exception.Message}");
        }
    }

    /// <summary>The program and arguments for Windows (public for the tests: what exactly is started).</summary>
    internal static (string Program, string[] Arguments) WindowsCommand(string command, TerminalChoice choice, bool hasWindowsTerminal, string powerShell)
    {
        var effective = choice == TerminalChoice.Automatic ? (hasWindowsTerminal ? TerminalChoice.WindowsTerminal : TerminalChoice.PowerShell) : choice;
        return effective switch
        {
            // Windows Terminal reads ';' as "new tab": escaped, the command arrives whole
            TerminalChoice.WindowsTerminal => ("wt.exe", ["new-tab", "--title", "ThixFred", powerShell, "-NoExit", "-Command", command.Replace(";", "\\;")]),
            TerminalChoice.CommandPrompt => ("cmd.exe", ["/k", command]),
            _ => (powerShell, ["-NoExit", "-Command", command]),
        };
    }

    /// <summary>The AppleScript of macOS (public for the tests).</summary>
    internal static string MacScript(string command, TerminalChoice choice)
    {
        var quoted = command.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return choice == TerminalChoice.ITerm
            ? $"tell application \"iTerm\"\nactivate\nset newWindow to (create window with default profile)\ntell current session of newWindow to write text \"{quoted}\"\nend tell"
            : $"tell application \"Terminal\"\nactivate\ndo script \"{quoted}\"\nend tell";
    }

    private static void RunWindows(string command, TerminalChoice choice, bool asAdministrator)
    {
        var powerShell = Shell.FindProgram("pwsh.exe") is not null ? "pwsh.exe" : "powershell.exe";
        var (program, arguments) = WindowsCommand(command, choice, Shell.FindProgram("wt.exe") is not null, powerShell);
        if (asAdministrator)
        {
            Shell.OpenAsAdministrator(program, string.Join(' ', arguments.Select(Quote)));
            return;
        }

        Shell.Start(program, arguments);
    }

    private static void RunMac(string command, TerminalChoice choice) => Shell.Start("/usr/bin/osascript", "-e", MacScript(command, choice));

    /// <summary>Windows command-line quoting for the one call that needs a single string (runas).</summary>
    private static string Quote(string argument) =>
        argument.Length > 0 && !argument.Any(character => char.IsWhiteSpace(character) || character == '"') ? argument : $"\"{argument.Replace("\"", "\\\"")}\"";
}
