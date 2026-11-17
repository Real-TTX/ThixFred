using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using ThixFred.Core.Business;
using ThixFred.Core.Infrastructure;
using ThixFred.Core.Models;
using ThixFred.Core.Platform;

namespace ThixFred.Tests;

/// <summary>Scripts, plugin folders, the store, themes, web searches, hotkeys and the policies.</summary>
public sealed class PluginTests
{
    [Fact]
    public void A_script_is_one_file_with_its_header_and_reads_back_the_same()
    {
        using var data = new TestData();
        var saved = ScriptService.Save(new UserScript
        {
            Id = "",
            Name = "Meine IP",
            Keyword = "IP",
            Description = "Zeigt die IP",
            Language = ScriptLanguage.Python,
            Mode = PluginMode.Filter,
            Body = "print('1.2.3.4')\n",
        });
        Assert.Equal("meine-ip", saved.Id);
        Assert.Equal("ip", saved.Keyword);

        var file = ScriptService.FileOf("meine-ip")!;
        Assert.EndsWith(".py", file);
        var text = File.ReadAllText(file);
        Assert.StartsWith("#!/usr/bin/env python3\n# @name Meine IP\n# @keyword ip\n# @mode filter\n", text);

        var read = ScriptService.Find("meine-ip")!;
        Assert.Equal(saved with { UpdatedAt = read.UpdatedAt }, read);
        Assert.Equal(text, ScriptService.Compose(read));

        // a changed language renames the file
        ScriptService.Save(read with { Language = ScriptLanguage.PowerShell, Body = "'1.2.3.4'" });
        Assert.EndsWith(".ps1", ScriptService.FileOf("meine-ip"));
        Assert.Single(Directory.GetFiles(AppPaths.ScriptsDirectory));
    }

    [Fact]
    public void A_script_written_by_hand_works_and_one_without_header_takes_its_file_name()
    {
        using var data = new TestData();
        Directory.CreateDirectory(AppPaths.ScriptsDirectory);
        File.WriteAllText(Path.Combine(AppPaths.ScriptsDirectory, "hallo.cmd"), "@echo off\r\n:: @name Hallo Welt\r\n:: @keyword hw\r\n:: @output clipboard\r\necho Hallo %1\r\n");
        File.WriteAllText(Path.Combine(AppPaths.ScriptsDirectory, "notiz.sh"), "#!/bin/bash\necho \"$1\" >> ~/notizen.txt\n");
        PluginService.Invalidate();

        var hello = ScriptService.Find("hallo")!;
        Assert.Equal(("Hallo Welt", "hw", ScriptOutput.Clipboard, "echo Hallo %1"), (hello.Name, hello.Keyword, hello.Output, hello.Body.Trim()));
        var note = ScriptService.Find("notiz")!;
        Assert.Equal(("notiz", "notiz", PluginMode.Run), (note.Name, note.Keyword, note.Mode));
        Assert.Equal("hw", PluginService.Find("script:hallo")!.Keyword);
    }

    [Fact]
    public void Scripts_are_checked_before_they_are_saved()
    {
        using var data = new TestData();
        var template = ScriptService.Template(ScriptLanguage.PowerShell);
        Assert.Equal(nameof(UserScript.Name), Assert.Throws<BusinessException>(() => ScriptService.Save(template with { Keyword = "x" })).Field);
        Assert.Equal(nameof(UserScript.Keyword), Assert.Throws<BusinessException>(() => ScriptService.Save(template with { Name = "X", Keyword = "zwei worte" })).Field);
        Assert.Equal(nameof(UserScript.Body), Assert.Throws<BusinessException>(() => ScriptService.Save(template with { Name = "X", Keyword = "x", Body = " " })).Field);
        ScriptService.Save(template with { Name = "Eins", Keyword = "eins" });
        Assert.Equal(nameof(UserScript.Keyword), Assert.Throws<BusinessException>(() => ScriptService.Save(template with { Name = "Zwei", Keyword = "eins" })).Field);

        data.Policies.Add("DisableScripts");
        Assert.Throws<BusinessException>(() => ScriptService.Save(template with { Name = "Drei", Keyword = "drei" }));
    }

    [Fact]
    public async Task A_run_script_gets_the_text_after_its_keyword_and_its_output_goes_where_it_says()
    {
        using var data = new TestData();
        ScriptService.Save(ScriptService.Template(ScriptLanguage.PowerShell) with { Name = "Hallo", Keyword = "hallo" });
        ScriptRunner.RunForTests = (script, argument) => new ScriptResult(0, $"Hallo {argument}\n", "", false);

        var result = SearchService.Search("hallo Welt", 8).Single();
        Assert.Equal("Hallo: Welt", result.Title);
        var outcome = (await LaunchService.ExecuteAsync(result, result.Action, "hallo Welt"));
        Assert.Equal((LaunchOutcomeKind.Message, "Hallo Welt"), (outcome.Kind, outcome.Message));

        var script = ScriptService.Find("hallo")!;
        ScriptService.Save(script with { Output = ScriptOutput.Clipboard });
        outcome = (await LaunchService.ExecuteAsync(result, result.Action, "hallo Welt"));
        Assert.Equal(LaunchOutcomeKind.Hide, outcome.Kind);
        Assert.Equal("Hallo Welt", data.Clipboard);

        ScriptRunner.RunForTests = (_, _) => new ScriptResult(1, "", "Fehler: kaputt", false);
        outcome = (await LaunchService.ExecuteAsync(result, result.Action, "hallo Welt"));
        Assert.True(outcome.IsError);
        Assert.Equal("Fehler: kaputt", outcome.Message);
    }

    [Fact]
    public void A_filter_plugin_answers_with_json_or_lines()
    {
        var plugin = new PluginInfo { Id = "x", Name = "X", Icon = "key" };
        var json = PluginService.ParseResults(plugin, """{"items":[{"title":"Eins","subtitle":"1","arg":"https://eins.de","action":"open"},{"title":"Zwei","action":"terminal","arg":"ls"},{"title":"Drei","action":"query","arg":"x drei"},{"subtitle":"ohne Titel"}]}""");
        Assert.Equal(["Eins", "Zwei", "Drei"], json.Select(result => result.Title));
        Assert.Equal((LaunchActionKind.Open, "https://eins.de"), (json[0].Action.Kind, json[0].Action.Target));
        Assert.Equal(LaunchActionKind.Terminal, json[1].Action.Kind);
        Assert.Equal(LaunchActionKind.SetQuery, json[2].Action.Kind);

        var lines = PluginService.ParseResults(plugin, "a1b2\r\nc3d4\n\n");
        Assert.Equal(["a1b2", "c3d4"], lines.Select(result => result.Title));
        Assert.All(lines, result => Assert.Equal(LaunchActionKind.Copy, result.Action.Kind));
    }

    [Fact]
    public async Task A_filter_script_runs_while_typing()
    {
        using var data = new TestData();
        SampleData.Create();
        ScriptRunner.RunForTests = (_, argument) => new ScriptResult(0, $"{argument}-1\n{argument}-2", "", false);
        var match = SearchService.FilterPlugin("uuid 4")!.Value;
        Assert.Equal("uuid", match.Plugin.Id);
        Assert.Equal("4", match.Argument);
        Assert.Empty(SearchService.Search("uuid 4", 8)); // the launcher asks the script
        var results = (await PluginService.FilterAsync(match.Plugin.Id, match.Argument, CancellationToken.None));
        Assert.Equal(["4-1", "4-2"], results.Select(result => result.Title));
    }

    [Fact]
    public async Task Plugins_can_be_switched_off_and_policies_stop_every_script()
    {
        using var data = new TestData();
        SampleData.Create();
        Assert.Equal(PluginSource.Store, PluginService.Find("uuid")!.Source);
        PluginService.SetEnabled("uuid", false);
        Assert.Null(SearchService.FilterPlugin("uuid 4"));
        PluginService.SetEnabled("uuid", true);
        Assert.NotNull(SearchService.FilterPlugin("uuid 4"));

        data.Policies.Add("DisableScripts");
        PluginService.Invalidate();
        Assert.Null(SearchService.FilterPlugin("uuid 4"));
        Assert.True(PluginService.IsEnabled(PluginService.AppsId), "built-in providers keep working");
        await Assert.ThrowsAsync<BusinessException>(() => PluginService.RunAsync("script:meine-ip", "", true));
    }

    [Fact]
    public async Task The_store_installs_checks_updates_and_removes()
    {
        using var data = new TestData();
        var store = new FakeStore();
        StoreService.HandlerForTests = () => store;
        StoreService.ListingsForTests = null;
        SettingsService.Update(settings => settings with { StoreUrl = "https://store.example.com/store.json" });
        store.AddPlugin("wetter", "1.0.0", "wetter", ("plugin.json", Manifest("wetter", "1.0.0")), ("wetter.ps1", "'Sonne'"));
        store.AddTheme("nordlicht", "1.0.0", """{"name":"Nordlicht","isDark":true,"background":"#E6102020","accent":"#FF34D399"}""");

        var listings = (await StoreService.LoadAsync());
        Assert.Equal(["Nordlicht", "wetter"], listings.Select(listing => listing.Item.Name));
        Assert.All(listings, listing => Assert.False(listing.IsInstalled));

        await StoreService.InstallAsync(listings.Single(listing => listing.Item.Id == "wetter").Item);
        await StoreService.InstallAsync(listings.Single(listing => listing.Item.Id == "nordlicht").Item);
        Assert.Equal(PluginSource.Store, PluginService.Find("wetter")!.Source);
        Assert.Equal("#FF34D399", ThemeService.Find("nordlicht")!.Accent);
        Assert.All((await StoreService.LoadAsync(refresh: true)), listing => Assert.True(listing.IsInstalled && !listing.HasUpdate));

        // a newer version is an update
        store.AddPlugin("wetter", "1.1.0", "wetter", ("plugin.json", Manifest("wetter", "1.1.0")), ("wetter.ps1", "'Regen'"));
        var update = (await StoreService.LoadAsync(refresh: true)).Single(listing => listing.Item.Id == "wetter");
        Assert.True(update.HasUpdate);
        await StoreService.InstallAsync(update.Item);
        Assert.Equal("1.1.0", PluginService.InstalledStoreVersion("wetter"));
        Assert.Equal("'Regen'", File.ReadAllText(Path.Combine(AppPaths.PluginsDirectory, "wetter", "wetter.ps1")));

        StoreService.Uninstall("wetter");
        StoreService.Uninstall("nordlicht");
        Assert.Null(PluginService.Find("wetter"));
        Assert.Null(ThemeService.Find("nordlicht"));
    }

    [Fact]
    public async Task The_store_refuses_a_package_with_a_wrong_hash_or_files_outside_its_folder()
    {
        using var data = new TestData();
        var store = new FakeStore();
        StoreService.HandlerForTests = () => store;
        StoreService.ListingsForTests = null;
        SettingsService.Update(settings => settings with { StoreUrl = "https://store.example.com/store.json" });

        store.AddPlugin("boese", "1.0.0", "b", ("plugin.json", Manifest("boese", "1.0.0")), ("../../ausbruch.ps1", "x"));
        var listing = (await StoreService.LoadAsync()).Single();
        var error = await Assert.ThrowsAsync<BusinessException>(() => StoreService.InstallAsync(listing.Item));
        Assert.Contains("außerhalb", error.Message);
        Assert.False(File.Exists(Path.Combine(data.Folder, "ausbruch.ps1")));

        var forged = listing.Item with { Sha256 = new string('0', 64) };
        error = await Assert.ThrowsAsync<BusinessException>(() => StoreService.InstallAsync(forged));
        Assert.Contains("Prüfsumme", error.Message);
        Assert.False(Directory.Exists(Path.Combine(AppPaths.PluginsDirectory, "boese")));
    }

    [Fact]
    public async Task The_plugins_of_the_store_folder_are_valid_and_run()
    {
        // store/ is what CI publishes as the store (tools/store-build.js): every manifest must read, every script must be there,
        // and on Windows the scripts that need no network really run and answer as the launcher expects.
        using var data = new TestData();
        foreach (var folder in Directory.GetDirectories(Path.Combine(Repository.Root, "store", "plugins")))
        {
            var manifest = PluginService.ReadManifest(folder);
            Assert.Equal(Path.GetFileName(folder), manifest.Id);
            Assert.All(new[] { manifest.Script, manifest.ScriptWindows, manifest.ScriptMacOS }.OfType<string>(), script => Assert.True(File.Exists(Path.Combine(folder, script)), script));
        }

        foreach (var theme in Directory.GetFiles(Path.Combine(Repository.Root, "store", "themes"), "*.json"))
        {
            var id = Path.GetFileNameWithoutExtension(theme);
            var installed = ThemeService.InstallFromStore(await File.ReadAllTextAsync(theme), id);
            Assert.True(ThemeService.IsColor(installed.Accent), id);
        }

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        async Task<IReadOnlyList<SearchResult>> Run(string id, string argument)
        {
            var target = Path.Combine(AppPaths.PluginsDirectory, id);
            CopyFolder(Path.Combine(Repository.Root, "store", "plugins", id), target);
            PluginService.WriteStoreMarker(target, id, "1.0.0");
            PluginService.Invalidate();
            return await PluginService.FilterAsync(id, argument, CancellationToken.None);
        }

        var uuids = await Run("uuid", "3");
        Assert.Equal(3, uuids.Count);
        Assert.All(uuids, result => Assert.True(Guid.TryParse(result.Title, out _), result.Title));
        var passwords = await Run("passwort", "24");
        Assert.Equal(5, passwords.Count);
        Assert.All(passwords, result => Assert.Equal(24, result.Title.Length));
        var base64 = await Run("base64", "Grüße");
        Assert.Equal("R3LDvMOfZQ==", base64[0].Title);
        Assert.Equal("Kodiert", base64[0].Subtitle);
        var time = await Run("zeit", "1763380800");
        Assert.Contains(time, result => result.Title == "2025-11-17T12:00:00Z");
        Assert.Contains(time, result => result.Subtitle == "Ortszeit");
    }

    private static void CopyFolder(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
        }
    }

    [Fact]
    public void Versions_compare_by_their_numbers()
    {
        Assert.True(StoreService.CompareVersions("1.10.0", "1.9.2") > 0);
        Assert.Equal(0, StoreService.CompareVersions("1.0", "1.0.0"));
        Assert.True(StoreService.CompareVersions("2.0.0-beta", "1.99") > 0);
    }

    [Fact]
    public void Own_themes_are_created_checked_duplicated_and_deleted()
    {
        using var data = new TestData();
        var theme = ThemeService.Save(new LauncherTheme { Id = "", Name = "Mein Theme", Accent = "#FF00AA88" });
        Assert.Equal("mein-theme", theme.Id);
        ThemeService.ResetForTests();
        Assert.Equal("#FF00AA88", ThemeService.Find("mein-theme")!.Accent);

        Assert.Equal(nameof(LauncherTheme.Accent), Assert.Throws<BusinessException>(() => ThemeService.Save(theme with { Accent = "blau" })).Field);
        Assert.Equal(nameof(LauncherTheme.FontSize), Assert.Throws<BusinessException>(() => ThemeService.Save(theme with { FontSize = 80 })).Field);
        Assert.Throws<BusinessException>(() => ThemeService.Save(ThemeService.Find("ozean")!));
        Assert.Throws<BusinessException>(() => ThemeService.Delete("ozean"));

        var copy = ThemeService.Duplicate("ozean");
        Assert.Equal(("Ozean (Kopie)", false), (copy.Name, copy.IsBuiltIn));
        ThemeService.Use(copy.Id);
        Assert.Equal(copy.Id, ThemeService.Active(systemIsDark: false).Id);
        ThemeService.Delete(copy.Id);
        Assert.Equal(LauncherTheme.SystemId, SettingsService.Get().LauncherThemeId);
        Assert.Equal(ThemeService.DarkId, ThemeService.Active(systemIsDark: true).Id);
    }

    [Fact]
    public void Web_searches_are_created_checked_and_restored()
    {
        using var data = new TestData();
        var search = WebSearchService.Save(new WebSearch { Id = "", Name = "Geizhals", Keyword = "GH2", Url = "https://geizhals.de/?fs={query}" });
        Assert.Equal(("geizhals", "gh2"), (search.Id, search.Keyword));
        Assert.Equal(nameof(WebSearch.Keyword), Assert.Throws<BusinessException>(() => WebSearchService.Save(search with { Id = "", Keyword = "g" })).Field);
        Assert.Equal(nameof(WebSearch.Url), Assert.Throws<BusinessException>(() => WebSearchService.Save(search with { Url = "https://geizhals.de" })).Field);
        Assert.Equal(nameof(WebSearch.Url), Assert.Throws<BusinessException>(() => WebSearchService.Save(search with { Url = "ftp://x/{query}" })).Field);
        Assert.Throws<BusinessException>(() => WebSearchService.Delete("google")); // the fallback

        WebSearchService.Delete("youtube");
        Assert.Null(WebSearchService.Find("youtube"));
        WebSearchService.RestoreDefaults();
        Assert.NotNull(WebSearchService.Find("youtube"));
        Assert.NotNull(WebSearchService.Find("geizhals"));
    }

    [Fact]
    public void A_locked_configuration_cannot_be_changed()
    {
        using var data = new TestData();
        data.Policies.Add("LockConfiguration");
        Assert.Throws<BusinessException>(() => SettingsService.Update(settings => settings with { MaxResults = 5 }));
        Assert.Throws<BusinessException>(() => ThemeService.Save(new LauncherTheme { Id = "", Name = "X" }));
        Assert.Throws<BusinessException>(() => WebSearchService.RestoreDefaults());
        Assert.NotEmpty(SearchService.Search("chrome", 8)); // the launcher itself works
    }

    [Theory]
    [InlineData("Alt+Space", true)]
    [InlineData("Ctrl+Space", true)]
    [InlineData("Shift+Space", false)]
    [InlineData("Ctrl+Alt+F", true)]
    [InlineData("Ctrl+F", false)]
    [InlineData("F8", true)]
    public void Hotkeys_need_a_modifier_that_programs_do_not_use_alone(string text, bool valid)
    {
        var hotkey = HotkeyService.Parse(text)!;
        if (valid)
        {
            HotkeyService.Validate(hotkey);
        }
        else
        {
            Assert.Throws<BusinessException>(() => HotkeyService.Validate(hotkey));
        }
    }

    [Fact]
    public void The_default_hotkey_is_alt_space_and_reads_in_german()
    {
        Assert.Equal("Alt+Space", HotkeyService.DefaultHotkey);
        Assert.Equal(OperatingSystem.IsMacOS() ? "⌥Leertaste" : "Alt + Leertaste", HotkeyService.Display("Alt+Space"));
    }

    [Fact]
    public void Icons_are_written_as_png()
    {
        var pixels = new byte[4 * 4 * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index + 2] = 255; // red, opaque
            pixels[index + 3] = 255;
        }

        var png = PngWriter.Encode(pixels, 4, 4);
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], png.Take(4));
        Assert.Equal("IHDR", Encoding.ASCII.GetString(png, 12, 4));
        Assert.Equal("IEND", Encoding.ASCII.GetString(png, png.Length - 8, 4));
    }

    private static string Manifest(string id, string version) =>
        $$"""{"id":"{{id}}","name":"{{id}}","version":"{{version}}","keyword":"{{id}}","mode":"Run","script":"{{id}}.ps1","scriptMacOS":"{{id}}.ps1"}""";

    /// <summary>A store on the web in memory: store.json and the packages it names (with their real hashes).</summary>
    private sealed class FakeStore : HttpMessageHandler
    {
        private readonly Dictionary<string, (StoreItem Item, byte[] Package)> _packages = [];

        public void AddPlugin(string id, string version, string keyword, params (string Name, string Text)[] files)
        {
            using var buffer = new MemoryStream();
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, text) in files)
                {
                    using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                    writer.Write(text);
                }
            }

            Add(new StoreItem { Id = id, Kind = StoreItemKind.Plugin, Name = id, Version = version, Keyword = keyword, Download = $"packages/{id}.zip" }, buffer.ToArray());
        }

        public void AddTheme(string id, string version, string json) =>
            Add(new StoreItem { Id = id, Kind = StoreItemKind.Theme, Name = "Nordlicht", Version = version, Download = $"themes/{id}.json" }, Encoding.UTF8.GetBytes(json));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/store.json")
            {
                var catalog = new StoreCatalog { Items = _packages.Values.Select(package => package.Item).ToList() };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonFile.Serialize(catalog), Encoding.UTF8, "application/json") });
            }

            var package = _packages.Values.FirstOrDefault(entry => "/" + entry.Item.Download == path);
            return Task.FromResult(package.Package is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(package.Package) });
        }

        private void Add(StoreItem item, byte[] package) =>
            _packages[item.Id] = (item with { Sha256 = Convert.ToHexStringLower(SHA256.HashData(package)) }, package);
    }
}
