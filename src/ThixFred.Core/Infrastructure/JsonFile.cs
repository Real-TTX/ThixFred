using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThixFred.Core.Infrastructure;

/// <summary>
/// Reading and writing the JSON files of the data folder. A write goes to a temporary file first and replaces the old one,
/// so a crash never leaves half a file; a file that cannot be read is kept aside (".broken-…") and the defaults are used.
/// Only the infrastructure and the business layer use it.
/// </summary>
internal static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Compact, for the cloud (the same content gives the same text: the hash compares it).</summary>
    public static readonly JsonSerializerOptions Compact = new(Options) { WriteIndented = false };

    public static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, Options);
        }
        catch (JsonException exception)
        {
            Log.Warning($"{Path.GetFileName(path)} could not be read ({exception.Message}); it is kept aside");
            TryKeepAside(path);
            return null;
        }
        catch (IOException exception)
        {
            Log.Warning($"{Path.GetFileName(path)} could not be read ({exception.Message})");
            return null;
        }
    }

    public static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, Options));
        File.Move(temporaryPath, path, overwrite: true);
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Compact);

    public static T? Deserialize<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void TryKeepAside(string path)
    {
        try
        {
            File.Copy(path, path + $".broken-{DateTime.Now:yyyyMMdd-HHmmss}", overwrite: true);
        }
        catch (IOException)
        {
        }
    }
}
