using System.Runtime.Versioning;
using ThixFred.Core.Business;

namespace ThixFred.Core.Platform.Mac;

/// <summary>
/// The regular apps that run now (NSWorkspace.runningApplications with the activation policy "regular": the ones in the Dock)
/// and ending one: terminate (the app may ask to save) or forceTerminate.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacRunningApps
{
    public static IReadOnlyList<RunningApp> List()
    {
        ObjC.LoadFramework("AppKit");
        using var pool = ObjC.Pool();
        var workspace = ObjC.Send(ObjC.Class("NSWorkspace"), ObjC.Selector("sharedWorkspace"));
        var apps = ObjC.Send(workspace, ObjC.Selector("runningApplications"));
        var result = new List<RunningApp>();
        for (var index = 0; index < ObjC.Count(apps); index++)
        {
            var app = ObjC.At(apps, index);
            if (ObjC.SendInteger(app, ObjC.Selector("activationPolicy")) != 0)
            {
                continue; // accessory and background apps (menu bar tools, agents)
            }

            var processId = ObjC.SendInt32(app, ObjC.Selector("processIdentifier"));
            if (processId == Environment.ProcessId)
            {
                continue;
            }

            var name = ObjC.ToManagedString(ObjC.Send(app, ObjC.Selector("localizedName"))) ?? "";
            var url = ObjC.Send(app, ObjC.Selector("bundleURL"));
            var path = url == IntPtr.Zero ? null : ObjC.ToManagedString(ObjC.Send(url, ObjC.Selector("path")));
            if (name.Length > 0)
            {
                result.Add(new RunningApp(processId, name, name, path));
            }
        }

        return result.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static bool End(int processId, bool force)
    {
        ObjC.LoadFramework("AppKit");
        using var pool = ObjC.Pool();
        var app = ObjC.SendWithInt32(ObjC.Class("NSRunningApplication"), ObjC.Selector("runningApplicationWithProcessIdentifier:"), processId);
        return app != IntPtr.Zero && ObjC.SendBool(app, ObjC.Selector(force ? "forceTerminate" : "terminate"));
    }
}
