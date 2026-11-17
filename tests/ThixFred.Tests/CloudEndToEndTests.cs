using ThixFred.Core.Business;
using ThixFred.Core.Models;

namespace ThixFred.Tests;

/// <summary>
/// Two computers against a REAL MatCloud (the ThixCloud), when THIXFRED_CLOUD_URL, THIXFRED_CLOUD_USER and THIXFRED_CLOUD_PASSWORD
/// name one (e.g. the local stack http://localhost:4950 and an account with a password); otherwise the test passes without
/// doing anything. Laptop saves a theme and a script, Desktop signs in and gets them; Desktop deletes the theme, Laptop loses it.
/// </summary>
public sealed class CloudEndToEndTests
{
    private static readonly string? Url = Environment.GetEnvironmentVariable("THIXFRED_CLOUD_URL");
    private static readonly string? User = Environment.GetEnvironmentVariable("THIXFRED_CLOUD_USER");
    private static readonly string? Password = Environment.GetEnvironmentVariable("THIXFRED_CLOUD_PASSWORD");

    [Fact]
    public async Task Two_computers_share_themes_and_scripts_through_the_cloud()
    {
        if (Url is null || User is null || Password is null)
        {
            return;
        }

        var key = Guid.NewGuid().ToString("N")[..8];
        var themeName = $"E2E {key}";

        // Laptop (its folder stays: it comes back at the end)
        var laptopFolder = Path.Combine(Path.GetTempPath(), "thixfred-tests", "e2e-laptop-" + key);
        string themeId;
        using (new TestData(laptopFolder))
        {
            SyncService.LoopDisabledForTests = true;
            await SyncService.ConnectAsync(Url, User, Password, $"E2E-Laptop-{key}");
            themeId = ThemeService.Save(new LauncherTheme { Id = "", Name = themeName, Accent = "#FF123456" }).Id;
            ScriptService.Save(ScriptService.Template(ScriptLanguage.Python) with { Name = $"E2E {key}", Keyword = $"e2e{key}" });
            await SyncService.SyncOnceAsync();
            Assert.Equal(CloudState.Online, SyncService.Status.State);
        }

        // Desktop: everything comes down
        using (new TestData())
        {
            SyncService.LoopDisabledForTests = true;
            await SyncService.ConnectAsync(Url, User, Password, $"E2E-Desktop-{key}");
            await SyncService.SyncOnceAsync();
            Assert.Equal("#FF123456", ThemeService.Find(themeId)?.Accent);
            Assert.NotNull(ScriptService.Find($"e2e-{key}"));

            // clean up: deleted here, deleted in the cloud
            ThemeService.Delete(themeId);
            ScriptService.Delete($"e2e-{key}");
            await SyncService.SyncOnceAsync();
            await SyncService.DisconnectAsync();
        }

        // Laptop again with its theme and script still on disk, signing in anew: the newer deletes of the cloud win
        using (new TestData(laptopFolder))
        {
            SyncService.LoopDisabledForTests = true;
            Assert.NotNull(ThemeService.Find(themeId));
            await SyncService.ConnectAsync(Url, User, Password, $"E2E-Laptop-{key}");
            await SyncService.SyncOnceAsync();
            Assert.Null(ThemeService.Find(themeId));
            Assert.Null(ScriptService.Find($"e2e-{key}"));
            await SyncService.DisconnectAsync();
        }

        Directory.Delete(laptopFolder, recursive: true);
    }
}
