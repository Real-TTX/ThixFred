using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ThixFred.Core.Infrastructure;
using ThixFred.Core.Platform.Windows;

namespace ThixFred.Core.Business;

/// <summary>
/// The icons of programs as PNG files in the cache (cache/icons): the launcher asks <see cref="CachedPath"/> (instant) and
/// otherwise <see cref="LoadAsync"/>, which extracts the icon on ONE background thread (Windows: the shell, an STA thread;
/// macOS: the .icns of the bundle through sips) and writes it once. The name of a cached file contains the time the source
/// was written, so a changed program gets its new icon.
/// </summary>
public static class IconService
{
    public const int IconSize = 64;

    private static readonly BlockingCollection<(string Source, TaskCompletionSource<string?> Done)> Queue = new();
    private static readonly ConcurrentDictionary<string, Task<string?>> Pending = new();
    private static readonly ConcurrentDictionary<string, string?> Known = new();
    private static Thread? _worker;

    /// <summary>Tests and snapshots show the symbols only (no shell, no files of this computer).</summary>
    internal static bool IsDisabledForTests { get; set; }

    /// <summary>The PNG of a source when it is in the cache already (null: not yet, or it has no icon).</summary>
    public static string? CachedPath(string source)
    {
        if (IsDisabledForTests)
        {
            return null;
        }

        if (Known.TryGetValue(source, out var known))
        {
            return known;
        }

        var path = CacheFile(source);
        if (path is not null && File.Exists(path))
        {
            Known[source] = path;
            return path;
        }

        return null;
    }

    /// <summary>Extracts the icon in the background (once per source); null when there is none.</summary>
    public static Task<string?> LoadAsync(string source)
    {
        if (IsDisabledForTests)
        {
            return Task.FromResult<string?>(null);
        }

        if (CachedPath(source) is { } cached)
        {
            return Task.FromResult<string?>(cached);
        }

        if (Known.ContainsKey(source))
        {
            return Task.FromResult<string?>(null);
        }

        return Pending.GetOrAdd(source, key =>
        {
            EnsureWorker();
            var done = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Queue.Add((key, done));
            return done.Task;
        });
    }

    private static void EnsureWorker()
    {
        if (_worker is not null)
        {
            return;
        }

        lock (Queue)
        {
            if (_worker is not null)
            {
                return;
            }

            _worker = new Thread(Work) { IsBackground = true, Name = "ThixFred icons", Priority = ThreadPriority.BelowNormal };
            if (OperatingSystem.IsWindows())
            {
                _worker.SetApartmentState(ApartmentState.STA); // the shell's icon handlers expect a single-threaded apartment
            }

            _worker.Start();
        }
    }

    private static void Work()
    {
        foreach (var (source, done) in Queue.GetConsumingEnumerable())
        {
            string? result = null;
            try
            {
                result = Extract(source);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Log.Warning($"Icon of {Path.GetFileName(source)}: {exception.Message}");
            }

            Known[source] = result;
            Pending.TryRemove(source, out _);
            done.TrySetResult(result);
        }
    }

    private static string? Extract(string source)
    {
        var target = CacheFile(source);
        if (target is null)
        {
            return null;
        }

        Directory.CreateDirectory(AppPaths.IconCacheDirectory);
        if (OperatingSystem.IsWindows())
        {
            if (ShellIcons.Load(source, IconSize) is not { } icon)
            {
                return null;
            }

            File.WriteAllBytes(target, PngWriter.Encode(icon.Pixels, icon.Width, icon.Height));
            return target;
        }

        return OperatingSystem.IsMacOS() ? ExtractMac(source, target) : null;
    }

    /// <summary>macOS: CFBundleIconFile of the bundle's Info.plist, converted by sips (Apps with only an asset catalog keep the symbol).</summary>
    private static string? ExtractMac(string bundle, string target)
    {
        var plist = Path.Combine(bundle, "Contents", "Info.plist");
        if (!File.Exists(plist))
        {
            return null;
        }

        var iconName = Run("/usr/bin/plutil", "-extract", "CFBundleIconFile", "raw", "-o", "-", plist)?.Trim();
        if (string.IsNullOrEmpty(iconName))
        {
            return null;
        }

        var icon = Path.Combine(bundle, "Contents", "Resources", iconName.EndsWith(".icns", StringComparison.OrdinalIgnoreCase) ? iconName : iconName + ".icns");
        if (!File.Exists(icon))
        {
            return null;
        }

        Run("/usr/bin/sips", "-s", "format", "png", "-Z", IconSize.ToString(), icon, "--out", target);
        return File.Exists(target) ? target : null;
    }

    private static string? Run(string program, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(program) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(10_000);
        return process.ExitCode == 0 ? output : null;
    }

    /// <summary>cache/icons/&lt;hash of the source and its write time&gt;.png; null for a source without a file that cannot be named.</summary>
    private static string? CacheFile(string source)
    {
        long stamp = 0;
        try
        {
            if (File.Exists(source))
            {
                stamp = File.GetLastWriteTimeUtc(source).Ticks;
            }
            else if (Directory.Exists(source))
            {
                stamp = Directory.GetLastWriteTimeUtc(source).Ticks;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{source}|{stamp}|{IconSize}")))[..32];
        return Path.Combine(AppPaths.IconCacheDirectory, hash + ".png");
    }
}
