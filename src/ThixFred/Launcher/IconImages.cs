using Avalonia.Media.Imaging;
using ThixFred.Core.Business;

namespace ThixFred.Launcher;

/// <summary>
/// The icons of programs as bitmaps, kept in memory once loaded (the launcher shows the same programs again and again). The
/// PNG files come from <see cref="IconService"/> (the cache on disk, filled in the background).
/// </summary>
internal static class IconImages
{
    private static readonly Dictionary<string, Bitmap?> Loaded = [];

    /// <summary>The bitmap when it is ready now; otherwise null and <paramref name="ready"/> runs (UI thread) once it is.</summary>
    public static Bitmap? Get(string source, Action<Bitmap> ready)
    {
        if (Loaded.TryGetValue(source, out var bitmap))
        {
            return bitmap;
        }

        if (IconService.CachedPath(source) is { } path)
        {
            return Load(source, path);
        }

        _ = LoadLaterAsync(source, ready);
        return null;
    }

    private static async Task LoadLaterAsync(string source, Action<Bitmap> ready)
    {
        var path = await IconService.LoadAsync(source);
        Ui.Post(() =>
        {
            if (path is null)
            {
                Loaded[source] = null;
                return;
            }

            if (Load(source, path) is { } bitmap)
            {
                ready(bitmap);
            }
        });
    }

    private static Bitmap? Load(string source, string path)
    {
        try
        {
            var bitmap = new Bitmap(path);
            Loaded[source] = bitmap;
            return bitmap;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            Loaded[source] = null;
            return null;
        }
    }
}
