using System.Diagnostics;
using ThixFred.Core.Infrastructure;
using ThixFred.Core.Models;
using ThixFred.Core.Platform.Mac;

namespace ThixFred.Core.Business;

/// <summary>A program with a window that runs now.</summary>
public sealed record RunningApp(int ProcessId, string Name, string WindowTitle, string? ExecutablePath);

/// <summary>
/// Closing programs from the launcher ("kill notepad", "beenden word"): the programs with a window (Windows) or the regular apps
/// (macOS, NSWorkspace). "Schließen" asks the program to close (it may ask to save: WM_CLOSE / terminate), "Sofort beenden"
/// ends the process (Kill / forceTerminate). ThixFred never closes itself this way.
/// </summary>
public static class ProcessService
{
    private static readonly TimeSpan CacheTime = TimeSpan.FromSeconds(2);
    private static (DateTime At, IReadOnlyList<RunningApp> Apps) _cache = (DateTime.MinValue, []);

    /// <summary>Tests: the running programs and what happened to them.</summary>
    internal static Func<IReadOnlyList<RunningApp>>? ListForTests { get; set; }

    internal static Action<int, bool>? EndForTests { get; set; }

    /// <summary>The programs with a window, briefly cached (the list is asked on every key while "kill …" is typed).</summary>
    public static IReadOnlyList<RunningApp> List()
    {
        if (ListForTests is { } list)
        {
            return list();
        }

        if (DateTime.UtcNow - _cache.At < CacheTime)
        {
            return _cache.Apps;
        }

        var apps = OperatingSystem.IsMacOS() ? MacRunningApps.List() : OperatingSystem.IsWindows() ? ListWindows() : [];
        _cache = (DateTime.UtcNow, apps);
        return apps;
    }

    /// <summary>"Schließen" (<paramref name="force"/> false) or "Sofort beenden".</summary>
    public static void End(int processId, bool force)
    {
        if (processId == Environment.ProcessId)
        {
            throw new BusinessException("ThixFred beendet sich über „ThixFred beenden“.");
        }

        _cache = (DateTime.MinValue, []);
        if (EndForTests is { } end)
        {
            end(processId, force);
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            if (!MacRunningApps.End(processId, force))
            {
                throw new BusinessException("Das Programm läuft nicht mehr.");
            }

            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            if (force)
            {
                process.Kill(entireProcessTree: false);
            }
            else if (!process.CloseMainWindow())
            {
                throw new BusinessException($"{process.ProcessName} lässt sich nicht schließen – mit Umschalt+Enter sofort beenden.");
            }

            Log.Info($"Process {processId} {(force ? "killed" : "asked to close")}");
        }
        catch (ArgumentException)
        {
            throw new BusinessException("Das Programm läuft nicht mehr.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new BusinessException("Dieses Programm darf ThixFred nicht beenden (es läuft mit Administratorrechten).");
        }
        catch (InvalidOperationException)
        {
            throw new BusinessException("Das Programm läuft nicht mehr.");
        }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IReadOnlyList<RunningApp> ListWindows()
    {
        var apps = new List<RunningApp>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == Environment.ProcessId || string.IsNullOrWhiteSpace(process.MainWindowTitle) || !Platform.Windows.VisibleWindows.IsShown(process.MainWindowHandle))
                    {
                        continue;
                    }

                    string? path = null;
                    var name = process.ProcessName;
                    try
                    {
                        path = process.MainModule?.FileName;
                        if (path is not null)
                        {
                            var description = FileVersionInfo.GetVersionInfo(path).FileDescription;
                            if (!string.IsNullOrWhiteSpace(description))
                            {
                                name = description.Trim();
                                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    name = name[..^4];
                                }
                            }
                        }
                    }
                    catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
                    {
                        // a program of another user or with administrator rights: its file stays unknown
                    }

                    apps.Add(new RunningApp(process.Id, name, process.MainWindowTitle, path));
                }
                catch (InvalidOperationException)
                {
                    // ended meanwhile
                }
            }
        }

        return apps.OrderBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
