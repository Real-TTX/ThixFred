using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using MatDesktop;
using ThixFred.Core.Business;
using ThixFred.Core.Infrastructure;
using ThixFred.Core.Models;
using ThixFred.Launcher;
using ThixFred.Pages;

[assembly: AvaloniaTestApplication(typeof(ThixFred.Tests.UiTestApp))]

namespace ThixFred.Tests;

/// <summary>The real app (App.axaml, themes, pages) rendered headless with Skia: real pixels, no screen needed.</summary>
public static class UiTestApp
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// The launcher and the settings window driven by the keyboard like a user would, and screenshots of every page
/// (set THIXFRED_SCREENSHOTS to a folder to keep them; docs/images has the ones of the README, made by the snapshot).
/// </summary>
public sealed class UiTests
{
    private static readonly string? ShotFolder = Environment.GetEnvironmentVariable("THIXFRED_SCREENSHOTS");

    [AvaloniaFact]
    public void Typing_searches_arrows_choose_and_enter_runs()
    {
        using var data = new TestData();
        var launcher = OpenLauncher();
        Assert.True(launcher.View.SearchBox.IsFocused, "the search has the focus when the launcher opens");

        launcher.KeyTextInput("vis");
        Settle(launcher);
        Assert.Equal("Visio", launcher.View.Selected!.Title);
        Save(launcher, "launcher-search");

        // the arrows walk down to Visual Studio Code
        var steps = launcher.View.Results.ToList().FindIndex(result => result.Title == "Visual Studio Code");
        Assert.True(steps > 0);
        for (var step = 0; step < steps; step++)
        {
            launcher.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        }

        Settle(launcher);
        Assert.Equal("Visual Studio Code", launcher.View.Selected!.Title);
        launcher.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Settle(launcher);
        Assert.False(launcher.IsVisible, "the launcher hides after starting a program");
        Assert.Equal(SampleData.Apps[0].Path, data.LastOpened);

        // the next time the same letters choose what was chosen
        launcher.ShowLauncher();
        Settle(launcher);
        Assert.Equal("", launcher.View.SearchBox.Text);
        launcher.KeyTextInput("vis");
        Settle(launcher);
        Assert.Equal("Visual Studio Code", launcher.View.Selected!.Title);
        Close(launcher);
    }

    [AvaloniaFact]
    public void Escape_clears_then_hides_and_ctrl_digit_runs_the_nth_result()
    {
        using var data = new TestData();
        var launcher = OpenLauncher();
        launcher.KeyTextInput("micro");
        Settle(launcher);
        Assert.True(launcher.View.Results.Count >= 2);
        var second = launcher.View.Results[1];
        launcher.KeyPressQwerty(PhysicalKey.Digit2, CommandModifier);
        Settle(launcher);
        Assert.Equal(second.Action.Target, data.LastOpened);

        launcher.ShowLauncher("abc");
        Settle(launcher);
        launcher.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Settle(launcher);
        Assert.Equal("", launcher.View.SearchBox.Text);
        Assert.True(launcher.IsVisible);
        launcher.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Settle(launcher);
        Assert.False(launcher.IsVisible);
        Close(launcher);
    }

    [AvaloniaFact]
    public void A_calculation_is_copied_and_a_command_runs_in_the_terminal()
    {
        using var data = new TestData();
        var launcher = OpenLauncher();
        launcher.KeyTextInput("3,5*(2+1)^2");
        Settle(launcher);
        Assert.Equal(ResultKind.Calculation, launcher.View.Selected!.Kind);
        Save(launcher, "launcher-calc");
        launcher.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Settle(launcher);
        Assert.Equal(Calculator.Format(31.5), data.Clipboard);

        launcher.ShowLauncher(">ping heise.de");
        Settle(launcher);
        Save(launcher, "launcher-terminal");
        launcher.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Shift);
        Settle(launcher);
        Assert.Contains("ping heise.de", data.Started.Last().Arguments.Last());
        Close(launcher);
    }

    [AvaloniaFact]
    public void The_integrated_terminal_shows_the_output_and_takes_the_next_command()
    {
        using var data = new TestData();
        var launcher = OpenLauncher();
        launcher.KeyTextInput(">ping heise.de");
        Settle(launcher);
        launcher.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Settle(launcher);
        Assert.True(launcher.IsVisible);
        Assert.True(launcher.IsInTerminal);
        Assert.Equal(["ping heise.de"], data.TerminalCommands);
        Assert.Contains("(Test) ping heise.de", launcher.View.TerminalText);
        Assert.Equal(">", launcher.View.SearchBox.Text);
        Save(launcher, "launcher-console");

        // the next command in the same place; ↑ brings the last one back
        launcher.KeyTextInput("echo zwei");
        launcher.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Settle(launcher);
        Assert.Equal(["ping heise.de", "echo zwei"], data.TerminalCommands);
        Assert.Contains("(Test) echo zwei", launcher.View.TerminalText);
        launcher.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        Settle(launcher);
        Assert.Equal(">echo zwei", launcher.View.SearchBox.Text);

        // typing something else is a search again; Esc from the terminal too
        launcher.View.SearchBox.Text = "vis";
        Settle(launcher);
        Assert.False(launcher.IsInTerminal);
        Assert.Contains(launcher.View.Results, result => result.Title == "Visual Studio Code");
        launcher.View.SearchBox.Text = ">";
        Settle(launcher);
        launcher.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        launcher.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Settle(launcher);
        Assert.True(launcher.IsInTerminal);
        launcher.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Settle(launcher);
        Assert.False(launcher.IsInTerminal);
        Assert.Equal("", launcher.View.SearchBox.Text);
        Close(launcher);
    }

    [AvaloniaFact]
    public void A_running_command_gets_the_typed_input_and_ctrl_c_ends_it()
    {
        using var data = new TestData();
        IntegratedTerminal.RunForTests = async (_, _, write, cancel) =>
        {
            write("Name? ");
            await Task.Delay(Timeout.Infinite, cancel);
            return 0;
        };
        var launcher = OpenLauncher();
        _ = launcher.RunInTerminalAsync("frag");
        Settle(launcher);
        Assert.True(IntegratedTerminal.IsRunning);
        Assert.Equal("", launcher.View.SearchBox.Text);

        launcher.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
        WaitUntil(launcher, () => !IntegratedTerminal.IsRunning);
        Settle(launcher);
        Assert.EndsWith("^C", launcher.View.TerminalText);
        Assert.Equal(">", launcher.View.SearchBox.Text);
        Close(launcher);
    }

    [AvaloniaFact]
    public void A_theme_of_another_kind_of_window_asks_for_a_new_launcher()
    {
        using var data = new TestData();
        var launcher = OpenLauncher();
        var asked = 0;
        launcher.RebuildRequested += (_, _) => asked++;
        ThemeService.Use(ThemeService.KanteId);
        Settle(launcher);
        Assert.Equal(1, asked);
        Close(launcher);

        // a launcher built for the flat theme is content with it
        var flat = OpenLauncher();
        flat.RebuildRequested += (_, _) => asked++;
        flat.KeyTextInput("vis");
        Settle(flat);
        Save(flat, "launcher-theme-kante");
        ThemeService.Use(ThemeService.KanteLightId);
        Settle(flat);
        Assert.Equal(1, asked);
        Close(flat);
        ThemeService.Use(LauncherTheme.SystemId);
    }

    [AvaloniaFact]
    public void Tab_completes_and_a_question_comes_before_shutting_down()
    {
        using var data = new TestData();
        var launcher = OpenLauncher();
        launcher.KeyTextInput("wiki");
        Settle(launcher);
        Assert.Equal("Mit Wikipedia suchen", launcher.View.Selected!.Title);
        launcher.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
        Settle(launcher);
        Assert.Equal("wiki ", launcher.View.SearchBox.Text);

        launcher.ShowLauncher("herunterfahren");
        Settle(launcher);
        launcher.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Settle(launcher);
        Assert.True(launcher.View.IsShowingMessage);
        Assert.StartsWith("Wirklich herunterfahren?", launcher.View.MessageText);
        Assert.Empty(data.Started);
        launcher.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Settle(launcher);
        Assert.False(launcher.View.IsShowingMessage);
        Assert.Empty(data.Started);

        launcher.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Settle(launcher);
        launcher.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Settle(launcher);
        var started = Assert.Single(data.Started);
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal("system:ShutDown", started.Program);
        }
        Close(launcher);
    }

    [AvaloniaFact]
    public void The_output_of_a_script_shows_in_the_launcher()
    {
        using var data = new TestData();
        ScriptService.Save(ScriptService.Template(ScriptLanguage.PowerShell) with { Name = "Hallo", Keyword = "hallo" });
        Core.Platform.ScriptRunner.RunForTests = (_, argument) => new Core.Platform.ScriptResult(0, $"Hallo {argument}!", "", false);
        var launcher = OpenLauncher();
        launcher.KeyTextInput("hallo Welt");
        Settle(launcher);
        launcher.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        WaitUntil(launcher, () => launcher.View.IsShowingMessage);
        Assert.Equal("Hallo Welt!", launcher.View.MessageText);
        Save(launcher, "launcher-message");
        Close(launcher);
    }

    [AvaloniaFact]
    public void Every_theme_draws_the_launcher()
    {
        using var data = new TestData();
        foreach (var theme in ThemeService.List())
        {
            ThemeService.Use(theme.Id);
            var launcher = OpenLauncher();
            launcher.ShowLauncher("vis");
            Settle(launcher);
            Assert.Equal(theme.Width, launcher.View.Bounds.Width, 0);
            Save(launcher, $"launcher-theme-{theme.Id}");
            Close(launcher);
        }
    }

    [AvaloniaFact]
    public void A_new_script_is_written_saved_and_deleted_on_its_pages()
    {
        using var data = new TestData();
        var window = OpenWindow();
        Save(window, "main-plugins");
        var plugins = window.PluginsPage;
        Assert.Equal(PluginService.BuiltIn.Count, plugins.List.List.ItemCount);

        // Neues Skript: the name is missing – the error stands at the field
        window.OpenPage(new ScriptEditPage(window, ScriptService.Template(ScriptLanguage.PowerShell)));
        Settle(window);
        var page = (ScriptEditPage)window.CurrentPage!;
        page.KeywordBox.Text = "hallo";
        Assert.False(page.Save());
        Assert.Equal("Bitte einen Namen mit höchstens 60 Zeichen angeben.", FieldError(page, nameof(UserScript.Name)));
        Save(window, "main-script-error");

        page.NameBox.Text = "Hallo";
        Assert.True(page.Save());
        Settle(window);
        Assert.IsType<PluginsPage>(window.CurrentPage);
        Assert.Equal(PluginService.BuiltIn.Count + 1, plugins.List.List.ItemCount);

        // the toolbar filters the own scripts
        plugins.Toolbar.FilterIndex = 3;
        Settle(window);
        Assert.Equal(1, plugins.List.List.ItemCount);
        ScriptService.Delete("hallo");
        Settle(window);
        Assert.Equal(0, plugins.List.List.ItemCount);
        Close(window);
    }

    [AvaloniaFact]
    public void Themes_and_web_searches_have_their_list_and_edit_pages()
    {
        using var data = new TestData();
        SampleData.Create();
        var window = OpenWindow();
        window.ShowOn(AppCommand.Themes);
        Settle(window);
        Assert.Equal(ThemeService.List().Count, window.ThemesPage.List.List.ItemCount);
        Save(window, "main-themes");
        window.OpenPage(new ThemeEditPage(window, ThemeService.Find("abendrot")!));
        Settle(window);
        Save(window, "main-theme-edit");
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Settle(window);
        Assert.IsType<ThemesPage>(window.CurrentPage);

        window.ShowOn(AppCommand.WebSearches);
        Settle(window);
        Assert.Equal(WebSearchService.List().Count, window.WebSearchesPage.List.List.ItemCount);
        Save(window, "main-websearches");

        window.ShowOn(AppCommand.Store);
        window.StorePage.ShowListings(SampleData.StoreListings);
        Settle(window);
        Assert.Equal(SampleData.StoreListings.Count, window.StorePage.List.List.ItemCount);
        Save(window, "main-store");
        Close(window);
    }

    [AvaloniaFact]
    public void The_settings_change_the_launcher()
    {
        using var data = new TestData();
        var window = OpenWindow();
        window.ShowSettings(SettingsPage.SearchTab);
        Settle(window);
        Save(window, "main-search");
        window.SettingsPage.MaxResultsBox.Value = 4;
        Settle(window);
        Assert.Equal(4, SettingsService.Get().MaxResults);
        Assert.True(SearchService.Search("o", SettingsService.Get().MaxResults).Count <= 4);

        window.ShowSettings();
        Settle(window);
        Save(window, "main-settings");
        window.ShowSettings(SettingsPage.CloudTab);
        Settle(window);
        Save(window, "main-cloud");
        window.ShowAbout();
        Settle(window);
        Save(window, "main-about");
        Close(window);
    }

    [AvaloniaFact]
    public void The_version_bottom_left_opens_about()
    {
        using var data = new TestData();
        var window = OpenWindow();
        window.VersionButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Settle(window);
        Assert.IsType<AboutPage>(window.CurrentPage);
        Close(window);
    }

    [AvaloniaFact]
    public void The_snapshot_draws_every_window_in_light_and_dark()
    {
        using var data = new TestData();
        var folder = Path.Combine(data.Folder, "snapshot");
        var report = Snapshot.Render(folder);
        Assert.Equal(Snapshot.FileNames.Order(), report.Written.Concat(report.Skipped.Select(skipped => skipped.File)).Order());
        Assert.All(report.Skipped, skipped => Assert.StartsWith("setup-", skipped.File));
        foreach (var name in report.Written)
        {
            Assert.True(new FileInfo(Path.Combine(folder, name)).Length > 0, name);
        }

        Assert.Null(AppClock.Pinned);
        Assert.NotEqual(Snapshot.Version, AppInfo.Version);
    }

    [Fact]
    public void No_window_writes_a_colour_of_its_own()
    {
        // DESK-02: every colour of the windows comes from MatDesktop's theme; the one value ThixFred gives is its accent (App.Accent).
        // The launcher (Launcher/) is the exception by design: its colours are the user's theme (data, LauncherTheme).
        var source = Path.Combine(Repository.Root, "src", "ThixFred");
        var files = Directory.EnumerateFiles(source, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".axaml", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}MatDesktop{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !file.Contains($"{Path.DirectorySeparatorChar}Launcher{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && Path.GetFileName(file) != "SelfTest.cs"
                           && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        var literal = new System.Text.RegularExpressions.Regex(@"#[0-9A-Fa-f]{6}\b|#[0-9A-Fa-f]{8}\b|Color\.(Parse|FromRgb|FromArgb)|Colors\.\w+|Brushes\.\w+");
        var findings = files
            .SelectMany(file => File.ReadAllLines(file).Select((line, index) => (file, line, index)))
            .Where(item => literal.IsMatch(item.line) && !item.line.Contains("public const string Accent", StringComparison.Ordinal))
            .Select(item => $"{Path.GetRelativePath(source, item.file)}:{item.index + 1}: {item.line.Trim()}")
            .ToList();
        Assert.Empty(findings);
    }

    [Fact]
    public void The_copy_of_MatDesktop_is_unchanged()
    {
        // MatDesktop is copied from MatDesign (node tools/matdesktop-copy.js src/ThixFred there) and never edited here.
        var folder = Path.Combine(Repository.Root, "src", "ThixFred", "MatDesktop");
        var lines = File.ReadAllLines(Path.Combine(folder, "matdesktop.version"));
        Assert.StartsWith("MatDesktop ", lines[0]);
        foreach (var line in lines.Skip(1).Where(line => line.Length > 0))
        {
            var (hash, file) = (line[..64], line[66..]);
            var text = File.ReadAllText(Path.Combine(folder, file)).Replace("\r\n", "\n", StringComparison.Ordinal);
            var actual = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
            Assert.True(hash == actual, $"MatDesktop/{file} was changed: copy it again from MatDesign instead of editing it");
        }
    }

    [Fact]
    public void The_copy_of_the_MatCloud_contract_is_unchanged()
    {
        // The contract of MatCloud is copied from there (node ../MatCloud/tools/contract-copy.js src/ThixFred.Core) and never edited here.
        var folder = Path.Combine(Repository.Root, "src", "ThixFred.Core", "MatCloud");
        var lines = File.ReadAllLines(Path.Combine(folder, "matcloud.version"));
        Assert.StartsWith("MatCloud.Contract ", lines[0]);
        var listed = new List<string>();
        foreach (var line in lines.Skip(1).Where(line => line.Length > 0))
        {
            var (hash, file) = (line[..64], line[66..]);
            listed.Add(file);
            var text = File.ReadAllText(Path.Combine(folder, file)).Replace("\r\n", "\n", StringComparison.Ordinal);
            Assert.StartsWith("// MatCloud.Contract ", text);
            var actual = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
            Assert.True(hash == actual, $"MatCloud/{file} was changed: copy the contract again from MatCloud instead of editing it");
        }

        var present = Directory.GetFiles(folder, "*.cs", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(folder, file).Replace('\\', '/'));
        Assert.Equal(listed.Order(), present.Order());
    }

    private static RawInputModifiers CommandModifier => OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static LauncherWindow OpenLauncher()
    {
        var launcher = new LauncherWindow();
        launcher.ShowLauncher();
        Settle(launcher);
        return launcher;
    }

    private static MainWindow OpenWindow()
    {
        var window = new MainWindow { Width = 1200, Height = 800 };
        window.ShowOn(AppCommand.Plugins);
        Settle(window);
        return window;
    }

    private static void Close(Window window)
    {
        if (window is LauncherWindow launcher)
        {
            launcher.IsExiting = true;
        }
        else if (window is MainWindow main)
        {
            main.IsExiting = true;
        }

        window.Close();
    }

    private static string? FieldError(Control root, string field) =>
        Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(root).OfType<MatFormField>().Distinct()
            .Single(formField => ThixFred.Controls.FormErrors.GetField(formField) == field).Error;

    /// <summary>Runs the dispatcher until the condition holds (background work), at most two seconds.</summary>
    private static void WaitUntil(Window window, Func<bool> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(2))
        {
            Settle(window);
        }
    }

    private static void Settle(Window window)
    {
        for (var round = 0; round < 8; round++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(15);
        }

        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Save(Window window, string name)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (ShotFolder is null)
        {
            return;
        }

        Directory.CreateDirectory(ShotFolder);
        frame.Save(Path.Combine(ShotFolder, $"{name}.png"));
    }
}
