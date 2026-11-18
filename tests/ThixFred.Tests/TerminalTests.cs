using ThixFred.Core.Business;
using ThixFred.Core.Models;
using ThixFred.Core.Platform;

namespace ThixFred.Tests;

/// <summary>The integrated terminal: the session (folder, output, history), its own commands, and a real shell.</summary>
public sealed class TerminalTests
{
    [Fact]
    public async Task A_command_writes_its_prompt_and_output_and_is_remembered()
    {
        using var data = new TestData();
        var folder = Directory.CreateDirectory(Path.Combine(data.Folder, "Projekte")).FullName;
        IntegratedTerminal.UseWorkingDirectoryForTests(folder);
        await IntegratedTerminal.RunAsync("  ping heise.de ");
        Assert.Equal(["ping heise.de"], data.TerminalCommands);
        Assert.Equal($"{CommandShell.Prompt(folder)} ping heise.de\n(Test) ping heise.de\n", IntegratedTerminal.Output);
        Assert.Equal(0, IntegratedTerminal.LastExitCode);
        Assert.False(IntegratedTerminal.IsRunning);
        Assert.Equal("ping heise.de", UsageService.RecentCommands()[0]);
        await Assert.ThrowsAsync<BusinessException>(() => IntegratedTerminal.RunAsync("   "));
    }

    [Fact]
    public async Task Cd_and_cls_are_done_by_the_session()
    {
        using var data = new TestData();
        var folder = Directory.CreateDirectory(Path.Combine(data.Folder, "eins", "zwei")).FullName;
        IntegratedTerminal.UseWorkingDirectoryForTests(data.Folder);

        await IntegratedTerminal.RunAsync("cd eins");
        Assert.Equal(Path.Combine(data.Folder, "eins"), IntegratedTerminal.WorkingDirectory);
        await IntegratedTerminal.RunAsync("cd \"zwei\"");
        Assert.Equal(folder, IntegratedTerminal.WorkingDirectory);
        await IntegratedTerminal.RunAsync("cd ..");
        await IntegratedTerminal.RunAsync("cd gibtsnicht");
        Assert.Equal(1, IntegratedTerminal.LastExitCode);
        Assert.Equal(Path.Combine(data.Folder, "eins"), IntegratedTerminal.WorkingDirectory);
        Assert.Contains("Ordner nicht gefunden", IntegratedTerminal.Output);

        // with "&&" the shell does it (the session's folder stays)
        await IntegratedTerminal.RunAsync("cd zwei && dir");
        Assert.Equal(["cd zwei && dir"], data.TerminalCommands);
        Assert.Equal(Path.Combine(data.Folder, "eins"), IntegratedTerminal.WorkingDirectory);

        await IntegratedTerminal.RunAsync("cls");
        Assert.Equal("", IntegratedTerminal.Output);
    }

    [Fact]
    public void The_output_loses_colour_codes_and_progress_overwrites_its_line()
    {
        using var data = new TestData();
        IntegratedTerminal.Write("\u001b[32mgrün\u001b[0m\r\n");
        IntegratedTerminal.Write("10 %\r20 %\r");
        IntegratedTerminal.Write("100 %\n");
        Assert.Equal("grün\n100 %\n", IntegratedTerminal.Output);

        IntegratedTerminal.Write(new string('x', IntegratedTerminal.MaxOutputLength) + "\nende\n");
        Assert.True(IntegratedTerminal.Output.Length <= IntegratedTerminal.MaxOutputLength);
        Assert.EndsWith("ende\n", IntegratedTerminal.Output);
    }

    [Fact]
    public void Output_is_utf8_where_it_is_and_the_old_code_page_where_not()
    {
        var utf8 = System.Text.Encoding.UTF8.GetBytes("Grüße");
        Assert.Equal("Grüße", IntegratedTerminal.Decode(utf8));

        // "ü" cut after its first byte waits for the second
        Assert.Equal(1, IntegratedTerminal.IncompleteUtf8Tail(utf8.AsSpan(0, 3)));
        Assert.Equal(0, IntegratedTerminal.IncompleteUtf8Tail(utf8));
        Assert.Equal(0, IntegratedTerminal.IncompleteUtf8Tail("abc"u8));

        if (OperatingSystem.IsWindows())
        {
            // cmd's echo without a console of ThixFred: code page 850 (German Windows) – not UTF-8, so the old code page
            byte[] oem = [0x47, 0x72, 0x81, 0xE1, 0x65];
            Assert.DoesNotContain('�', IntegratedTerminal.Decode(oem));
        }
    }

    [Fact]
    public async Task Cancel_ends_the_command_and_one_runs_at_a_time()
    {
        using var data = new TestData();
        IntegratedTerminal.RunForTests = async (_, _, _, cancel) =>
        {
            await Task.Delay(Timeout.Infinite, cancel);
            return 0;
        };
        var run = IntegratedTerminal.RunAsync("ping -t heise.de");
        Assert.True(IntegratedTerminal.IsRunning);
        Assert.Equal("ping -t heise.de", IntegratedTerminal.RunningCommand);
        await Assert.ThrowsAsync<BusinessException>(() => IntegratedTerminal.RunAsync("dir"));

        IntegratedTerminal.Cancel();
        await run;
        Assert.False(IntegratedTerminal.IsRunning);
        Assert.Null(IntegratedTerminal.LastExitCode);
        Assert.EndsWith("^C\n", IntegratedTerminal.Output);
        Assert.False(IntegratedTerminal.SendInput("y"));
    }

    [Fact]
    public async Task A_real_shell_runs_the_command_in_the_folder_with_umlauts_and_its_exit_code()
    {
        using var data = new TestData();
        IntegratedTerminal.RunForTests = null;
        var folder = Directory.CreateDirectory(Path.Combine(data.Folder, "Ordner mit Leerzeichen")).FullName;
        IntegratedTerminal.UseWorkingDirectoryForTests(folder);

        await IntegratedTerminal.RunAsync("echo Grüße aus \"ThixFred\"");
        Assert.Equal(0, IntegratedTerminal.LastExitCode);
        Assert.Contains(OperatingSystem.IsWindows() ? "Grüße aus \"ThixFred\"" : "Grüße aus ThixFred", IntegratedTerminal.Output);

        await IntegratedTerminal.RunAsync(OperatingSystem.IsWindows() ? "cd" : "pwd");
        Assert.Contains(folder, IntegratedTerminal.Output);

        await IntegratedTerminal.RunAsync("exit 3");
        Assert.Equal(3, IntegratedTerminal.LastExitCode);

        // what is typed while it runs is its input
        var run = IntegratedTerminal.RunAsync(OperatingSystem.IsWindows() ? "set /p NAME=Name? & call echo Hallo %NAME%" : "read NAME; echo Hallo $NAME");
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!IntegratedTerminal.IsRunning && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.True(IntegratedTerminal.SendInput("Anna"));
        await run.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Contains("Hallo Anna", IntegratedTerminal.Output);
    }

    [Fact]
    public void The_shell_gets_the_command_whole()
    {
        var startInfo = CommandShell.StartInfo("echo \"a & b\" | sort", "C:\\");
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("cmd.exe", startInfo.FileName);
            Assert.Equal("/d /s /c \"chcp 65001 >nul & echo \"a & b\" | sort\"", startInfo.Arguments);
        }
        else
        {
            Assert.Equal("/bin/zsh", startInfo.FileName);
            Assert.Equal(["-l", "-c", "echo \"a & b\" | sort"], startInfo.ArgumentList);
        }

        Assert.True(startInfo.RedirectStandardInput && startInfo.RedirectStandardOutput && startInfo.CreateNoWindow);
        Assert.Equal("C:\\", startInfo.WorkingDirectory);
    }
}
