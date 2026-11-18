using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using ThixFred.Core.Infrastructure;
using ThixFred.Core.Models;
using ThixFred.Core.Platform;

namespace ThixFred.Core.Business;

/// <summary>
/// The terminal inside the launcher: "&gt;ping heise.de" runs without a window of its own, its output streams below the search.
/// One session for the whole app: the folder ("cd") and the output stay while the launcher is hidden (a long command goes on);
/// one command at a time; what is typed while it runs goes to its input; Strg+C ends it with everything it started.
/// cd / chdir, a drive ("D:"), cls / clear and pwd are done here (each command runs in a shell of its own, so the shell could
/// not remember them). The output keeps the last <see cref="MaxOutputLength"/> characters; colour codes are removed.
/// </summary>
public static partial class IntegratedTerminal
{
    public const int MaxOutputLength = 200_000;

    private static readonly object Gate = new();
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static Encoding? _legacyEncoding;
    private static readonly StringBuilder Buffer = new();
    private static CancellationTokenSource? _cancel;
    private static Process? _process;
    private static string? _workingDirectory;
    private static bool _carriageReturn;

    /// <summary>Raised on any thread after the output or the state changed.</summary>
    public static event EventHandler? Changed;

    /// <summary>The folder the next command runs in (the user's home at first).</summary>
    public static string WorkingDirectory
    {
        get
        {
            lock (Gate)
            {
                return _workingDirectory ??= Home;
            }
        }
    }

    public static bool IsRunning
    {
        get
        {
            lock (Gate)
            {
                return _cancel is not null;
            }
        }
    }

    /// <summary>The command that runs now (null: none).</summary>
    public static string? RunningCommand { get; private set; }

    /// <summary>The exit code of the last command (null: none yet, or it was cancelled).</summary>
    public static int? LastExitCode { get; private set; }

    /// <summary>Everything the session wrote, the prompts with the commands included.</summary>
    public static string Output
    {
        get
        {
            lock (Gate)
            {
                return Buffer.ToString();
            }
        }
    }

    /// <summary>The prompt the next command gets: "C:\Users\anna&gt;".</summary>
    public static string Prompt => CommandShell.Prompt(WorkingDirectory);

    /// <summary>Tests: runs the command instead of a shell (command, folder, write, cancel → exit code).</summary>
    internal static Func<string, string, Action<string>, CancellationToken, Task<int>>? RunForTests { get; set; }

    /// <summary>Runs <paramref name="command"/>; returns when it ended (or was cancelled).</summary>
    public static async Task RunAsync(string command)
    {
        var text = command.Trim();
        if (text.Length == 0)
        {
            throw new BusinessException("Bitte einen Befehl eingeben, z. B. ping heise.de");
        }

        CancellationTokenSource cancel;
        lock (Gate)
        {
            if (_cancel is not null)
            {
                throw new BusinessException("Es läuft noch ein Befehl – Strg+C bricht ihn ab.");
            }

            cancel = _cancel = new CancellationTokenSource();
            RunningCommand = text;
            LastExitCode = null;
        }

        UsageService.RememberCommand(text);
        Write($"{Prompt} {text}\n");
        try
        {
            LastExitCode = TryBuiltIn(text) ?? (RunForTests is { } fake
                ? await fake(text, WorkingDirectory, Write, cancel.Token)
                : await RunProcessAsync(text, cancel.Token));
        }
        catch (OperationCanceledException)
        {
            LastExitCode = null;
            Write("^C\n");
        }
        catch (BusinessException exception)
        {
            // the shell did not start: said where the output goes
            LastExitCode = null;
            Write(exception.Message + "\n");
        }
        finally
        {
            lock (Gate)
            {
                _cancel = null;
                _process = null;
                RunningCommand = null;
            }

            cancel.Dispose();
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>A line for the input of the running command (echoed into the output); false when nothing runs.</summary>
    public static bool SendInput(string line)
    {
        Process? process;
        lock (Gate)
        {
            process = _process;
        }

        if (process is null)
        {
            return false;
        }

        Write(line + "\n");
        try
        {
            process.StandardInput.WriteLine(line);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            Write("(Der Befehl nimmt keine Eingabe mehr an.)\n");
        }

        return true;
    }

    /// <summary>Strg+C: ends the running command and what it started.</summary>
    public static void Cancel()
    {
        lock (Gate)
        {
            _cancel?.Cancel();
        }
    }

    /// <summary>cls / clear / Strg+L: the output starts empty.</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Buffer.Clear();
            _carriageReturn = false;
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Tests and snapshots: a fixed folder (it need not exist).</summary>
    internal static void UseWorkingDirectoryForTests(string directory)
    {
        lock (Gate)
        {
            _workingDirectory = directory;
        }
    }

    /// <summary>Tests: a new session in the home folder.</summary>
    internal static void ResetForTests()
    {
        Cancel();
        lock (Gate)
        {
            Buffer.Clear();
            _workingDirectory = null;
            _carriageReturn = false;
        }

        LastExitCode = null;
        RunForTests = null;
    }

    /// <summary>Adds output: colour codes removed, "\r" without "\n" goes back to the start of the line (progress), at most <see cref="MaxOutputLength"/>.</summary>
    internal static void Write(string chunk)
    {
        var text = AnsiRegex().Replace(chunk, "").Replace("\r\n", "\n");
        lock (Gate)
        {
            foreach (var character in text)
            {
                if (character == '\r')
                {
                    _carriageReturn = true;
                    continue;
                }

                if (_carriageReturn && character != '\n')
                {
                    var lineStart = LastIndexOf(Buffer, '\n') + 1;
                    Buffer.Length = lineStart;
                }

                _carriageReturn = false;
                Buffer.Append(character);
            }

            if (Buffer.Length > MaxOutputLength)
            {
                var cut = Buffer.Length - MaxOutputLength;
                var nextLine = Buffer.ToString(cut, Math.Min(Buffer.Length - cut, 2000)).IndexOf('\n');
                Buffer.Remove(0, cut + Math.Max(0, nextLine + 1));
            }
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static Encoding LegacyEncoding => _legacyEncoding ??= CreateLegacyEncoding();

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>cd, a drive, cls, pwd: the exit code; null when the shell must run it.</summary>
    private static int? TryBuiltIn(string text)
    {
        // "cd x && dir", "cd x | …": the shell's business (the folder then stays as it is)
        if (text.IndexOfAny(['&', '|', ';', '>', '<']) >= 0)
        {
            return null;
        }

        var lower = text.ToLowerInvariant();
        if (lower is "cls" or "clear")
        {
            Clear();
            return 0;
        }

        if (lower == "pwd" || (OperatingSystem.IsWindows() && lower is "cd" or "chdir"))
        {
            Write(WorkingDirectory + "\n");
            return 0;
        }

        if (OperatingSystem.IsWindows() && DriveRegex().IsMatch(text))
        {
            return ChangeDirectory(text + "\\");
        }

        if (lower == "cd")
        {
            return ChangeDirectory("~");
        }

        var match = ChangeDirectoryRegex().Match(text);
        return match.Success ? ChangeDirectory(match.Groups["path"].Value) : null;
    }

    private static int ChangeDirectory(string target)
    {
        var path = target.Trim().Trim('"', '\'');
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            path = Path.Combine(Home, path.Length > 2 ? path[2..] : "");
        }

        string full;
        try
        {
            full = Path.GetFullPath(Path.Combine(WorkingDirectory, path));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            Write($"Ungültiger Pfad: {target}\n");
            return 1;
        }

        if (!Directory.Exists(full))
        {
            Write($"Ordner nicht gefunden: {full}\n");
            return 1;
        }

        lock (Gate)
        {
            _workingDirectory = full;
        }

        return 0;
    }

    private static async Task<int> RunProcessAsync(string command, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = CommandShell.StartInfo(command, WorkingDirectory) };
        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            Log.Warning($"Integrated terminal: the shell did not start ({exception.Message})");
            throw new BusinessException($"Die Shell ließ sich nicht starten: {exception.Message}");
        }

        lock (Gate)
        {
            _process = process;
        }

        process.StandardInput.AutoFlush = true;
        var pumps = Task.WhenAll(PumpAsync(process.StandardOutput), PumpAsync(process.StandardError));
        using (cancellationToken.Register(() => Kill(process)))
        {
            await process.WaitForExitAsync(CancellationToken.None);

            // a program the command started in the background may hold the output open: then it is not waited for
            await Task.WhenAny(pumps, Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return process.ExitCode;
    }

    /// <summary>
    /// Reads the bytes of an output and writes them as text: UTF-8 where they are (chcp 65001, zsh), else the console's old code
    /// page (cmd's own commands keep it when ThixFred has no console of its own: "Grüße" arrives as 850). A character cut by
    /// the end of a read waits for the rest.
    /// </summary>
    private static async Task PumpAsync(StreamReader reader)
    {
        var stream = reader.BaseStream;
        var buffer = new byte[4096];
        var carry = 0;
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(carry), CancellationToken.None)) > 0)
            {
                var length = carry + read;
                var complete = length - IncompleteUtf8Tail(buffer.AsSpan(0, length));
                Write(Decode(buffer.AsSpan(0, complete)));
                carry = length - complete;
                buffer.AsSpan(complete, carry).CopyTo(buffer);
            }

            if (carry > 0)
            {
                Write(Decode(buffer.AsSpan(0, carry)));
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
    }

    internal static string Decode(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return LegacyEncoding.GetString(bytes);
        }
    }

    /// <summary>How many bytes at the end begin a UTF-8 character whose rest has not come yet (0 … 3).</summary>
    internal static int IncompleteUtf8Tail(ReadOnlySpan<byte> bytes)
    {
        for (var back = 1; back <= Math.Min(3, bytes.Length); back++)
        {
            var value = bytes[^back];
            if ((value & 0xC0) == 0x80)
            {
                continue; // a continuation byte: look further back for its start
            }

            var needed = (value & 0xE0) == 0xC0 ? 2 : (value & 0xF0) == 0xE0 ? 3 : (value & 0xF8) == 0xF0 ? 4 : 1;
            return needed > back ? back : 0;
        }

        return 0;
    }

    private static Encoding CreateLegacyEncoding()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Encoding.Latin1;
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }
    }

    private static int LastIndexOf(StringBuilder builder, char character)
    {
        for (var index = builder.Length - 1; index >= 0; index--)
        {
            if (builder[index] == character)
            {
                return index;
            }
        }

        return -1;
    }

    [GeneratedRegex(@"\x1B(\[[0-9;?]*[ -/]*[@-~]|\][^\x07\x1B]*(\x07|\x1B\\)|[()][A-Z0-9])")]
    private static partial Regex AnsiRegex();

    [GeneratedRegex(@"^[A-Za-z]:$")]
    private static partial Regex DriveRegex();

    [GeneratedRegex(@"^(cd|chdir)(\s+/d)?\s+(?<path>.+)$|^(cd|chdir)(?<path>\.\.|\\|/)$", RegexOptions.IgnoreCase)]
    private static partial Regex ChangeDirectoryRegex();
}
