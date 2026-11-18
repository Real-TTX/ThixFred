using System.Globalization;
using ThixFred.Core.Business;
using ThixFred.Core.Models;

namespace ThixFred.Tests;

/// <summary>The search of the launcher: matching, the calculator, keywords, the terminal, closing programs, usage, launching.</summary>
public sealed class SearchTests
{
    [Theory]
    [InlineData("code", "code", 100)]
    [InlineData("vis", "visual studio code", 87)]
    [InlineData("studio", "visual studio code", 74)]
    [InlineData("vsc", "visual studio code", 72)]
    [InlineData("xyz", "visual studio code", 0)]
    public void Names_match_by_start_word_initials_and_letters(string query, string name, int expected) =>
        Assert.Equal(expected, FuzzyMatcher.Score(query, name, FuzzyMatcher.Initials(name)));

    [Fact]
    public void Matching_ignores_case_and_accents()
    {
        Assert.Equal("gerate-manager", FuzzyMatcher.Prepare("Geräte-Manager"));
        Assert.Equal("strasse", FuzzyMatcher.Prepare("Straße"));
        Assert.Equal("tf", FuzzyMatcher.Initials("ThixFred"));
        Assert.True(FuzzyMatcher.Score("chrm", "google chrome") > 0, "letters in order match");
        Assert.Equal(0, FuzzyMatcher.Score("note", "windows update")); // spread over the name: chance
        Assert.True(FuzzyMatcher.Score("note", "kurznotizen") > 0);
    }

    [Theory]
    [InlineData("2+2", 4)]
    [InlineData("3,5 * (2 + 1)^2", 31.5)]
    [InlineData("=sqrt(16) + 2^3^2", 516)]
    [InlineData("200 + 15%", 230)]
    [InlineData("200 * 15%", 30)]
    [InlineData("0xff + 1", 256)]
    [InlineData("0b101", 5)]
    [InlineData("5!", 120)]
    [InlineData("2*pi", 2 * Math.PI)]
    [InlineData("max(1, 7; 3)", 7)]
    [InlineData("(1+2", 3)]
    [InlineData("1e3 / 4", 250)]
    public void The_calculator_computes(string expression, double expected) =>
        Assert.Equal(expected, Calculator.TryEvaluate(expression)!.Value, 9);

    [Theory]
    [InlineData("42")]
    [InlineData("Word 2016")]
    [InlineData("notepad++")]
    [InlineData("1/0")]
    [InlineData("hallo + welt")]
    [InlineData("")]
    public void Text_that_is_no_calculation_is_not_computed(string text) => Assert.Null(Calculator.TryEvaluate(text));

    [Fact]
    public void Results_are_written_as_the_user_writes_numbers()
    {
        Assert.Equal("31,5", Calculator.Format(31.5, CultureInfo.GetCultureInfo("de-DE")));
        Assert.Equal("0,3333333333", Calculator.Format(1 / 3.0, CultureInfo.GetCultureInfo("de-DE")));
        Assert.Equal("0xFF · 0b11111111", Calculator.OtherBases(255));
        Assert.Null(Calculator.OtherBases(2.5));
    }

    [Fact]
    public async Task A_calculation_comes_first_and_enter_copies_its_result()
    {
        using var data = new TestData();
        var results = SearchService.Search("2+2*3", 8);
        Assert.Equal(ResultKind.Calculation, results[0].Kind);
        Assert.Equal(LaunchActionKind.Copy, results[0].Action.Kind);

        var outcome = (await LaunchService.ExecuteAsync(results[0], results[0].Action, "2+2*3"));
        Assert.Equal(LaunchOutcomeKind.Hide, outcome.Kind);
        Assert.Equal("8", data.Clipboard);
    }

    [Fact]
    public void Programs_are_found_by_name_initials_and_file_name()
    {
        using var data = new TestData();
        Assert.Equal("Visual Studio Code", SearchService.Search("vsc", 8)[0].Title);
        Assert.Equal("Microsoft Word", SearchService.Search("winword", 8)[0].Title);
        Assert.Equal("Rechner", SearchService.Search("calc", 8)[0].Title);
        Assert.Contains(SearchService.Search("vis", 8), result => result.Title == "Visio");
        Assert.True(SearchService.Search("vis", 8).Last().IsFallback, "the web search comes last");
    }

    [Fact]
    public async Task What_was_chosen_for_the_same_letters_comes_first_next_time()
    {
        using var data = new TestData();
        var first = SearchService.Search("vis", 8);
        Assert.Equal("Visio", first[0].Title); // the shortest name first
        var studio = first.Single(result => result.Title == "Visual Studio Code");
        await LaunchService.ExecuteAsync(studio, studio.Action, "vis");
        Assert.Equal("Visual Studio Code", SearchService.Search("vis", 8)[0].Title);

        // learning off: the plain order again
        SettingsService.Update(settings => settings with { LearnFromUsage = false });
        Assert.Equal("Visio", SearchService.Search("vis", 8)[0].Title);
    }

    [Fact]
    public async Task An_empty_search_shows_what_was_used_last()
    {
        using var data = new TestData();
        Assert.Empty(SearchService.Search("", 8));
        var chrome = SearchService.Search("chrome", 8)[0];
        await LaunchService.ExecuteAsync(chrome, chrome.Action, "chrome");
        var recent = SearchService.Search("  ", 8);
        Assert.Equal("Google Chrome", recent.Single().Title);
        Assert.StartsWith("Zuletzt verwendet", recent[0].Subtitle);
    }

    [Fact]
    public async Task Opening_a_program_starts_it_and_shift_enter_starts_it_as_administrator()
    {
        using var data = new TestData();
        var word = SearchService.Search("word", 8)[0];
        await LaunchService.ExecuteAsync(word, word.Action, "word");
        Assert.Equal(SampleData.Apps.Single(app => app.Name == "Microsoft Word").Path, data.LastOpened);
        if (OperatingSystem.IsWindows())
        {
            Assert.Equal(LaunchActionKind.OpenAsAdministrator, word.AlternateAction!.Kind);
            await LaunchService.ExecuteAsync(word, word.AlternateAction, "word");
            Assert.StartsWith("runas:", data.LastOpened);
        }
    }

    [Fact]
    public async Task A_greater_than_sign_runs_the_command_in_the_launcher_and_shift_enter_in_a_terminal()
    {
        using var data = new TestData();
        var terminal = Assert.Single(SearchService.Search(">ping heise.de", 8));
        Assert.Equal(LaunchActionKind.IntegratedTerminal, terminal.Action.Kind);
        var outcome = await LaunchService.ExecuteAsync(terminal, terminal.Action, ">ping heise.de");
        Assert.Equal((LaunchOutcomeKind.Terminal, "ping heise.de"), (outcome.Kind, outcome.Query));

        Assert.Equal(LaunchActionKind.Terminal, terminal.AlternateAction!.Kind);
        await LaunchService.ExecuteAsync(terminal, terminal.AlternateAction, ">ping heise.de");
        Assert.Contains("ping heise.de", data.Started.Last().Arguments.Last());

        Assert.Equal("ping heise.de", SearchService.TerminalCommand(">ping heise.de"));
        Assert.Equal("", SearchService.TerminalCommand(">"));
        Assert.Null(SearchService.TerminalCommand("ping"));
        Assert.Equal(">", SearchService.TerminalPrefix());
    }

    [Fact]
    public async Task Without_the_integrated_terminal_a_greater_than_sign_runs_the_command_in_a_terminal()
    {
        using var data = new TestData();
        SettingsService.Update(settings => settings with { IntegratedTerminal = false });
        var results = SearchService.Search(">ping heise.de", 8);
        var terminal = Assert.Single(results);
        Assert.Equal("ping heise.de", terminal.Title);
        Assert.Equal(LaunchActionKind.Terminal, terminal.Action.Kind);

        await LaunchService.ExecuteAsync(terminal, terminal.Action, ">ping heise.de");
        var started = data.Started.Last();
        Assert.Contains("ping heise.de", started.Arguments.Last());

        // ">" alone lists the last commands
        var recent = SearchService.Search(">", 8);
        Assert.Equal("ping heise.de", recent[0].Title);
        Assert.Equal(">ping heise.de", recent[0].Autocomplete);
    }

    [Fact]
    public void The_beginning_of_a_keyword_finds_its_web_search()
    {
        using var data = new TestData();

        // "w alfred": Wikipedia ("wiki") first, the fallback Google stays last
        var results = SearchService.Search("w alfred", 8);
        Assert.Equal("web:wikipedia", results[0].Id);
        Assert.Equal("„alfred“ mit Wikipedia suchen", results[0].Title);
        Assert.True(results[^1].IsFallback);

        // the whole keyword still decides alone
        Assert.Equal("web:wikipedia", Assert.Single(SearchService.Search("wiki alfred", 8)).Id);

        // "wi" offers the search, Enter fills in its keyword
        var hint = SearchService.Search("wi", 8).Single(result => result.Id == "websearch:wikipedia");
        Assert.Equal((LaunchActionKind.SetQuery, "wiki "), (hint.Action.Kind, hint.Action.Target));
    }

    [Fact]
    public async Task The_keyword_of_a_store_plugin_that_is_not_installed_offers_to_install_it()
    {
        using var data = new TestData();
        StoreService.SetKnownForTests(new StoreCatalog
        {
            Items = [new StoreItem { Id = "ip", Kind = StoreItemKind.Plugin, Name = "Meine IP", Keyword = "ip", Description = "Die IP-Adressen" }],
        });
        var install = SearchService.Search("ip", 8)[0];
        Assert.Equal("store:ip", install.Id);
        Assert.Equal("Meine IP aus dem Store installieren", install.Title);
        Assert.Equal((LaunchActionKind.StoreInstall, "ip", "ip"), (install.Action.Kind, install.Action.Target, install.Action.Argument));

        // without a store nothing is installed and the launcher says why
        data.Policies.Add("DisableStore");
        var outcome = await LaunchService.ExecuteAsync(install, install.Action, "ip");
        Assert.True(outcome.IsError);
    }

    [Fact]
    public void The_terminal_command_reaches_the_shell_whole()
    {
        var (program, arguments) = TerminalService.WindowsCommand("echo a; echo b", TerminalChoice.Automatic, hasWindowsTerminal: true, "pwsh.exe");
        Assert.Equal("wt.exe", program);
        Assert.Equal("echo a\\; echo b", arguments[^1]);
        var prompt = TerminalService.WindowsCommand("ping heise.de", TerminalChoice.CommandPrompt, true, "pwsh.exe");
        Assert.Equal("cmd.exe", prompt.Program);
        Assert.Equal(["/k", "ping heise.de"], prompt.Arguments);
        Assert.Equal("powershell.exe", TerminalService.WindowsCommand("x", TerminalChoice.Automatic, hasWindowsTerminal: false, "powershell.exe").Program);
        Assert.Contains("do script \"echo \\\"hi\\\"\"", TerminalService.MacScript("echo \"hi\"", TerminalChoice.MacTerminal));
        Assert.Contains("write text", TerminalService.MacScript("ls", TerminalChoice.ITerm));
    }

    [Fact]
    public async Task Kill_lists_running_programs_closes_and_shift_enter_ends_at_once_after_asking()
    {
        using var data = new TestData();
        var results = SearchService.Search("kill wo", 8);
        var word = Assert.Single(results);
        Assert.Equal("Microsoft Word schließen", word.Title);
        await LaunchService.ExecuteAsync(word, word.Action, "kill wo");
        Assert.Equal((4711, false), data.Ended.Last());

        Assert.NotNull(word.AlternateAction!.Confirmation);
        await LaunchService.ExecuteAsync(word, word.AlternateAction, "kill wo");
        Assert.Equal((4711, true), data.Ended.Last());

        Assert.Equal(2, SearchService.Search("beenden", 8).Count);
        Assert.Equal("Kein laufendes Programm passt zu „excel“", SearchService.Search("quit excel", 8).Single().Title);
    }

    [Fact]
    public void System_commands_that_end_the_session_ask_first()
    {
        using var data = new TestData();
        var shutdown = SearchService.Search("herunterfahren", 8)[0];
        Assert.Equal(LaunchActionKind.System, shutdown.Action.Kind);
        Assert.NotNull(shutdown.Action.Confirmation);
        Assert.Null(SearchService.Search("sperren", 8)[0].Action.Confirmation);
        Assert.Equal("Neu starten", SearchService.Search("reboot", 8)[0].Title);
    }

    [Fact]
    public void Addresses_open_and_file_names_do_not_look_like_addresses()
    {
        using var data = new TestData();
        Assert.Equal("https://heise.de", SearchService.AddressOf("heise.de"));
        Assert.Equal("https://localhost:8080/x", SearchService.AddressOf("localhost:8080/x"));
        Assert.Null(SearchService.AddressOf("notepad.exe"));
        Assert.Null(SearchService.AddressOf("readme.md"));
        Assert.Null(SearchService.AddressOf("hallo welt.de"));
        Assert.Equal(data.Folder, SearchService.AddressOf(data.Folder));
        Assert.Equal(ResultKind.Url, SearchService.Search("heise.de", 8)[0].Kind);
    }

    [Fact]
    public void Web_searches_answer_their_keyword()
    {
        using var data = new TestData();
        var google = Assert.Single(SearchService.Search("g thix fred", 8));
        Assert.Equal("https://www.google.com/search?q=thix%20fred", google.Action.Target);
        Assert.Equal("https://de.wikipedia.org/w/index.php?search=Alfred", SearchService.Search("wiki Alfred", 8).Single().Action.Target);

        // the fallback follows the chosen search and can be switched off
        SettingsService.Update(settings => settings with { DefaultWebSearchId = "duckduckgo" });
        Assert.StartsWith("https://duckduckgo.com/", SearchService.Search("zzz", 8).Single(result => result.IsFallback).Action.Target);
        SettingsService.Update(settings => settings with { WebSearchFallback = false });
        Assert.DoesNotContain(SearchService.Search("zzz", 8), result => result.IsFallback);
    }

    [Fact]
    public async Task Commands_of_thixfred_go_to_the_ui()
    {
        using var data = new TestData();
        var store = SearchService.Search("store", 8).First(result => result.Kind == ResultKind.Command);
        var outcome = (await LaunchService.ExecuteAsync(store, store.Action, "store"));
        Assert.Equal(LaunchOutcomeKind.AppCommand, outcome.Kind);
        Assert.Equal(AppCommand.Store, outcome.Command);
    }

    [Fact]
    public void A_switched_off_provider_finds_nothing()
    {
        using var data = new TestData();
        PluginService.SetEnabled(PluginService.AppsId, false);
        Assert.DoesNotContain(SearchService.Search("chrome", 8), result => result.Kind == ResultKind.Application);
        PluginService.SetEnabled(PluginService.CalculatorId, false);
        Assert.DoesNotContain(SearchService.Search("2+2", 8), result => result.Kind == ResultKind.Calculation);
    }

    [Fact]
    public void The_terminal_keyword_can_change()
    {
        using var data = new TestData();
        PluginService.SetKeyword(PluginService.TerminalId, "$");
        Assert.Equal(ResultKind.Terminal, SearchService.Search("$ls", 8)[0].Kind);
        Assert.NotEqual(ResultKind.Terminal, SearchService.Search(">ls", 8)[0].Kind);
        var error = Assert.Throws<BusinessException>(() => PluginService.SetKeyword(PluginService.RunningId, "$"));
        Assert.Equal(nameof(PluginPreference.Keyword), error.Field);
    }
}
