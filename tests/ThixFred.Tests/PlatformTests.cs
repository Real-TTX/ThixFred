using Microsoft.Win32;
using ThixFred.Core.Business;
using ThixFred.Core.Infrastructure;
using ThixFred.Core.Models;

namespace ThixFred.Tests;

/// <summary>Setup, autostart and updates. Nothing here touches the real program folder, registry values or LaunchAgents.</summary>
public sealed class PlatformTests : IDisposable
{
    private readonly TestData _data = new();

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            InstallService.UseLocationsForTests(null);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\ThixFredTests", throwOnMissingSubKey: false);
        }

        AutostartService.UseLaunchAgentFolderForTests(null);
        _data.Dispose();
    }

    [Fact]
    public void The_setup_installs_into_the_program_folder_and_uninstalls_everything()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var program = Path.Combine(_data.Folder, "Programs", "ThixFred");
        var shortcuts = Path.Combine(_data.Folder, "StartMenu");
        InstallService.UseLocationsForTests(program, shortcuts, @"Software\ThixFredTests\Uninstall");
        var source = Path.Combine(_data.Folder, "Downloads", "ThixFredSetup.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllBytes(source, new byte[1024 * 8]);

        var steps = new List<string>();
        InstallService.Install(source, new InstallOptions(StartWithWindows: false, DesktopShortcut: true), steps.Add);

        Assert.True(File.Exists(InstallService.InstalledExecutable));
        Assert.True(File.Exists(InstallService.StartMenuShortcut));
        Assert.True(File.Exists(InstallService.DesktopShortcut));
        Assert.True(InstallService.IsInstalled);
        Assert.Equal(AppInfo.Version, InstallService.InstalledVersion);
        Assert.Contains("Installation abgeschlossen", steps);

        // An update keeps the shortcuts as they are.
        InstallService.Install(source, options: null, _ => { });
        Assert.True(File.Exists(InstallService.DesktopShortcut));

        var removeLater = InstallService.Uninstall(deleteUserData: false, _ => { });
        Assert.False(removeLater);
        Assert.False(InstallService.IsInstalled);
        Assert.False(File.Exists(InstallService.StartMenuShortcut));
        Assert.False(Directory.Exists(program));
        Assert.True(Directory.Exists(AppPaths.DataDirectory), "the data stays unless the user deletes it");
    }

    [Fact]
    public void The_launch_agent_starts_thixfred_hidden_at_sign_in()
    {
        var plist = AutostartService.LaunchAgent("/Applications/ThixFred.app/Contents/MacOS/ThixFred");
        Assert.Contains("<string>info.schmoldt.thixfred</string>", plist);
        Assert.Contains("<string>/Applications/ThixFred.app/Contents/MacOS/ThixFred</string>", plist);
        Assert.Contains($"<string>{AutostartService.StartupArgument}</string>", plist);
        Assert.Contains("<key>RunAtLoad</key>", plist);
    }

    [Fact]
    public void Autostart_on_macos_writes_and_removes_the_launch_agent()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        AutostartService.UseLaunchAgentFolderForTests(_data.Folder);
        AutostartService.Enable("/Applications/ThixFred.app/Contents/MacOS/ThixFred");
        Assert.True(AutostartService.IsEnabled());
        AutostartService.Disable();
        Assert.False(AutostartService.IsEnabled());
    }

    [Fact]
    public void Updates_only_come_from_the_own_channel_and_a_higher_build()
    {
        var manifest = new UpdateManifest("0.1.50-20261201", "release", 50, "ThixFredSetup.exe", "AB", DateTimeOffset.UtcNow,
            new Dictionary<string, UpdatePackage> { ["osx-arm64"] = new("ThixFred-macOS-arm64.zip", "CD") });

        Assert.True(UpdateService.IsNewer(manifest, "release", 49));
        Assert.False(UpdateService.IsNewer(manifest, "release", 50));
        Assert.False(UpdateService.IsNewer(manifest, "nightly", 1));
        Assert.Equal("ThixFredSetup.exe", UpdateService.PackageFor(manifest, "win-x64")!.File);
        Assert.Equal("ThixFred-macOS-arm64.zip", UpdateService.PackageFor(manifest, "osx-arm64")!.File);
        Assert.Null(UpdateService.PackageFor(manifest, "osx-x64"));
        Assert.True(UpdateService.IsSafeFileName("ThixFredSetup.exe"));
        Assert.False(UpdateService.IsSafeFileName("../evil.exe"));
        Assert.False(UpdateService.IsSafeFileName("setup.ps1"));
    }

    [Fact]
    public async Task A_local_build_or_the_policy_stops_updates()
    {
        // CI builds are release or nightly builds; a local build never updates itself.
        if (AppInfo.IsLocalBuild)
        {
            Assert.False(UpdateService.CanUpdate);
        }

        AccessPolicy.UsePolicyReaderForTests(name => name == "DisableUpdates");
        Assert.False(UpdateService.CanUpdate);
        await Assert.ThrowsAsync<BusinessException>(() => UpdateService.CheckAsync());
    }

    [Fact]
    public void The_mac_update_script_swaps_the_bundle_and_restarts_in_the_menu_bar()
    {
        var script = MacAppBundle.ReplacementScript(42, "/Applications/ThixFred.app", "/tmp/new/ThixFred.app", "/tmp/update.log");
        Assert.Contains("while kill -0 42", script);
        Assert.Contains("BUNDLE='/Applications/ThixFred.app'", script);
        Assert.Contains("--args --tray", script);
        Assert.EndsWith("Applications" + Path.DirectorySeparatorChar + "ThixFred.app",
            MacAppBundle.BundleOf("/Applications/ThixFred.app/Contents/MacOS/ThixFred")!.Replace('/', Path.DirectorySeparatorChar));
        Assert.Null(MacAppBundle.BundleOf("/usr/local/bin/ThixFred"));
    }
}
