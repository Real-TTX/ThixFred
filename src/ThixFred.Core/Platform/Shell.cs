using System.Diagnostics;
using System.Runtime.InteropServices;
using ThixFred.Core.Models;

namespace ThixFred.Core.Platform;

/// <summary>
/// What the launcher asks of the system: open a program, a document, a folder or an address, show a file in Explorer /
/// Finder, start with administrator rights, a terminal command and the commands of the system (lock, sleep, restart …).
/// Errors are <see cref="System.ComponentModel.Win32Exception"/> or <see cref="InvalidOperationException"/>; the business
/// layer turns them into messages.
/// </summary>
internal static partial class Shell
{
    /// <summary>Tests and snapshots see what would be started instead of starting it.</summary>
    internal static Action<string, IReadOnlyList<string>>? StartForTests { get; set; }

    public static void Open(string target)
    {
        if (OperatingSystem.IsMacOS())
        {
            Start("/usr/bin/open", target);
            return;
        }

        if (StartForTests is { } start)
        {
            start(target, []);
            return;
        }

        using var process = Process.Start(new ProcessStartInfo(target)
        {
            UseShellExecute = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        });
    }

    public static void Reveal(string path)
    {
        if (OperatingSystem.IsMacOS())
        {
            Start("/usr/bin/open", "-R", path);
        }
        else if (OperatingSystem.IsWindows())
        {
            Start("explorer.exe", $"/select,{path}");
        }
    }

    public static void OpenAsAdministrator(string target, string arguments = "")
    {
        if (!OperatingSystem.IsWindows())
        {
            Open(target);
            return;
        }

        if (StartForTests is { } start)
        {
            start("runas:" + target, [arguments]);
            return;
        }

        using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, Verb = "runas", Arguments = arguments });
    }

    /// <summary>Starts a program with arguments (each one passed as it is, quoted where needed).</summary>
    public static void Start(string program, params string[] arguments)
    {
        if (StartForTests is { } start)
        {
            start(program, arguments);
            return;
        }

        var startInfo = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
    }

    /// <summary>A program on the PATH or in a well-known place; null when it is not installed.</summary>
    public static string? FindProgram(string name)
    {
        if (OperatingSystem.IsWindows() && name.Equals("wt.exe", StringComparison.OrdinalIgnoreCase))
        {
            var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
            if (File.Exists(alias))
            {
                return alias;
            }
        }

        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(folder.Trim('"'), name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        if (OperatingSystem.IsMacOS())
        {
            foreach (var folder in new[] { "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin" })
            {
                var candidate = Path.Combine(folder, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>Runs a command of the system (the launcher asked before where it ends the session).</summary>
    public static void Run(SystemCommand command)
    {
        if (OperatingSystem.IsWindows())
        {
            RunWindows(command);
        }
        else if (OperatingSystem.IsMacOS())
        {
            RunMac(command);
        }
    }

    private static void RunWindows(SystemCommand command)
    {
        if (StartForTests is { } start)
        {
            start("system:" + command, []);
            return;
        }

        switch (command)
        {
            case SystemCommand.Lock:
                LockWorkStation();
                break;
            case SystemCommand.Sleep:
                SetSuspendState(false, false, false);
                break;
            case SystemCommand.SignOut:
                Start("shutdown.exe", "/l");
                break;
            case SystemCommand.Restart:
                Start("shutdown.exe", "/r", "/t", "0");
                break;
            case SystemCommand.ShutDown:
                Start("shutdown.exe", "/s", "/t", "0");
                break;
            case SystemCommand.EmptyTrash:
                // no confirmation, no progress, no sound: the launcher asked already; 0x8000FFFF = the bin was empty
                var result = SHEmptyRecycleBinW(IntPtr.Zero, null, 0x1 | 0x2 | 0x4);
                if (result < 0 && result != unchecked((int)0x8000FFFF))
                {
                    throw new InvalidOperationException($"Der Papierkorb ließ sich nicht leeren (0x{result:X8}).");
                }

                break;
        }
    }

    private static void RunMac(SystemCommand command)
    {
        switch (command)
        {
            case SystemCommand.Lock:
                Start("/usr/bin/pmset", "displaysleepnow");
                break;
            case SystemCommand.Sleep:
                Start("/usr/bin/pmset", "sleepnow");
                break;
            case SystemCommand.SignOut:
                Start("/usr/bin/osascript", "-e", "tell application \"System Events\" to log out");
                break;
            case SystemCommand.Restart:
                Start("/usr/bin/osascript", "-e", "tell application \"System Events\" to restart");
                break;
            case SystemCommand.ShutDown:
                Start("/usr/bin/osascript", "-e", "tell application \"System Events\" to shut down");
                break;
            case SystemCommand.EmptyTrash:
                Start("/usr/bin/osascript", "-e", "tell application \"Finder\" to empty trash");
                break;
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LockWorkStation();

    [LibraryImport("powrprof.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetSuspendState([MarshalAs(UnmanagedType.Bool)] bool hibernate, [MarshalAs(UnmanagedType.Bool)] bool force,
        [MarshalAs(UnmanagedType.Bool)] bool disableWakeEvent);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHEmptyRecycleBinW(IntPtr window, string? rootPath, uint flags);
}
