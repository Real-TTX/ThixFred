using System.Text.Json;
using ThixFred.Core.Business;
using ThixFred.Core.Infrastructure;
using ThixFred.Core.Models;

namespace ThixFred.Tests;

/// <summary>The ThixCloud (MatCloud) connection against a MatCloud in memory: sign-in, the configuration up and down, deletes, 401/403.</summary>
public sealed class SyncTests
{
    [Fact]
    public async Task Signing_in_with_a_password_sends_the_configuration_up()
    {
        using var data = new TestData();
        var cloud = Connect();
        await SyncService.ConnectAsync("cloud.example.com", FakeCloud.UserName, FakeCloud.Password, "ANNA-PC");
        Assert.True(SettingsService.Get().Cloud.IsConnected);
        Assert.Equal("https://cloud.example.com", SettingsService.Get().Cloud.ServerUrl);

        ThemeService.Save(new LauncherTheme { Id = "", Name = "Wald", Accent = "#FF22AA44" });
        ScriptService.Save(ScriptService.Template(ScriptLanguage.PowerShell) with { Name = "Hallo", Keyword = "hallo" });
        await SyncService.SyncOnceAsync();

        Assert.Single(cloud.Live(ConfigSync.Themes), item => item.Key == "wald");
        var script = Assert.Single(cloud.Live(ConfigSync.Scripts));
        Assert.Contains("hallo.ps1", script.Data);
        Assert.Single(cloud.Live(ConfigSync.Settings));
        Assert.Equal(WebSearchService.Defaults.Count, cloud.Live(ConfigSync.WebSearches).Count);
        Assert.All(cloud.UserAgents.Where(agent => agent.Length > 0), agent => Assert.StartsWith("ThixFred/", agent));
    }

    [Fact]
    public async Task The_sign_in_in_the_browser_waits_for_the_code()
    {
        using var data = new TestData();
        var cloud = Connect();
        string? opened = null;
        var signIn = SyncService.ConnectInBrowserAsync("https://cloud.example.com", "ANNA-PC", _ => { }, address => opened = address);
        await WaitFor(() => cloud.LastCode is not null);
        cloud.Approve(cloud.LastCode!);
        Assert.True(await signIn);
        Assert.StartsWith("https://cloud.example.com/Connect?code=", opened);
        Assert.True(SyncService.IsConnected);
    }

    [Fact]
    public async Task Changes_of_another_computer_come_down_and_deletes_go_both_ways()
    {
        using var data = new TestData();
        var cloud = Connect();
        await SyncService.ConnectAsync("https://cloud.example.com", FakeCloud.UserName, FakeCloud.Password, "ANNA-PC");
        await SyncService.SyncOnceAsync();

        // the laptop added a theme, a script and changed the settings of the account
        cloud.PutFromElsewhere(ConfigSync.Themes, "laptop", JsonFile.Serialize(new LauncherTheme { Id = "laptop", Name = "Vom Laptop", Accent = "#FFAA2200" }));
        cloud.PutFromElsewhere(ConfigSync.Scripts, "uhr", JsonSerializer.Serialize(new { fileName = "uhr.ps1", content = "# @name Uhrzeit\n# @keyword uhr\n\nGet-Date\n" }));
        var shared = cloud.Live(ConfigSync.Settings).Single().Data!.Replace("\"maxResults\":8", "\"maxResults\":12");
        cloud.PutFromElsewhere(ConfigSync.Settings, ConfigSync.SettingsKey, shared);
        await SyncService.SyncOnceAsync();

        Assert.Equal("Vom Laptop", ThemeService.Find("laptop")!.Name);
        Assert.Equal("uhr", ScriptService.Find("uhr")!.Keyword);
        Assert.Equal(12, SettingsService.Get().MaxResults);
        Assert.Equal("Alt+Space", SettingsService.Get().Hotkey); // the hotkey stays on each computer

        // deleted on the laptop: gone here; deleted here: gone in the cloud
        cloud.PutFromElsewhere(ConfigSync.Themes, "laptop", null);
        ScriptService.Delete("uhr");
        await SyncService.SyncOnceAsync();
        Assert.Null(ThemeService.Find("laptop"));
        Assert.Empty(cloud.Live(ConfigSync.Scripts));

        // nothing changed: nothing goes up again
        var revision = cloud.Revision;
        await SyncService.SyncOnceAsync();
        Assert.Equal(revision, cloud.Revision);
    }

    [Fact]
    public async Task A_newer_change_here_wins_over_an_older_one_of_the_cloud()
    {
        using var data = new TestData();
        var cloud = Connect();
        await SyncService.ConnectAsync("https://cloud.example.com", FakeCloud.UserName, FakeCloud.Password, "ANNA-PC");
        ThemeService.Save(new LauncherTheme { Id = "", Name = "Wald" });
        await SyncService.SyncOnceAsync();

        cloud.PutFromElsewhere(ConfigSync.Themes, "wald", JsonFile.Serialize(new LauncherTheme { Id = "wald", Name = "Alt" }), DateTimeOffset.UtcNow.AddHours(-1));
        ThemeService.Save(ThemeService.Find("wald")! with { Name = "Neu" });
        await SyncService.SyncOnceAsync();
        Assert.Equal("Neu", ThemeService.Find("wald")!.Name);
        Assert.Contains("\"Neu\"", cloud.Live(ConfigSync.Themes).Single().Data);
    }

    [Fact]
    public async Task A_removed_device_is_signed_out_and_a_switched_off_app_waits()
    {
        using var data = new TestData();
        var cloud = Connect();
        await SyncService.ConnectAsync("https://cloud.example.com", FakeCloud.UserName, FakeCloud.Password, "ANNA-PC");

        cloud.IsAppSwitchedOff = true;
        await SyncService.SyncOnceAsync();
        Assert.Equal(CloudState.Offline, SyncService.Status.State);
        Assert.True(SyncService.IsConnected);

        cloud.IsAppSwitchedOff = false;
        cloud.RemoveAllDevices();
        await SyncService.SyncOnceAsync();
        Assert.Equal(CloudState.SignedOut, SyncService.Status.State);
        Assert.False(SyncService.IsConnected);
    }

    [Fact]
    public async Task A_wrong_password_is_shown_at_the_password()
    {
        using var data = new TestData();
        Connect();
        var error = await Assert.ThrowsAsync<BusinessException>(() => SyncService.ConnectAsync("https://cloud.example.com", FakeCloud.UserName, "falsch", "ANNA-PC"));
        Assert.Equal("Password", error.Field);
        Assert.False(SyncService.IsConnected);
    }

    [Fact]
    public async Task The_policy_disable_cloud_keeps_everything_on_this_computer()
    {
        using var data = new TestData();
        Connect();
        data.Policies.Add("DisableCloud");
        await Assert.ThrowsAsync<BusinessException>(() => SyncService.ConnectAsync("https://cloud.example.com", FakeCloud.UserName, FakeCloud.Password, "ANNA-PC"));
    }

    private static FakeCloud Connect()
    {
        var cloud = new FakeCloud();
        SyncService.HandlerForTests = () => cloud;
        SyncService.LoopDisabledForTests = true;
        return cloud;
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var round = 0; round < 100 && !condition(); round++)
        {
            await Task.Delay(20);
        }
    }
}
