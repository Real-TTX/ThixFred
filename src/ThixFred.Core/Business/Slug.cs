using System.Text.RegularExpressions;

namespace ThixFred.Core.Business;

/// <summary>Ids made of names ("Mein Theme" → "mein-theme"): small letters, digits and '-', unique among the existing ones.</summary>
internal static partial class Slug
{
    public static string Make(string name, string fallback, Func<string, bool> isTaken, int maxLength = 40)
    {
        var slug = NonWordRegex().Replace(FuzzyMatcher.Prepare(name), "-").Trim('-');
        slug = slug.Length == 0 ? fallback : slug[..Math.Min(maxLength, slug.Length)].Trim('-');
        var id = slug;
        for (var number = 2; isTaken(id); number++)
        {
            id = $"{slug}-{number}";
        }

        return id;
    }

    /// <summary>A valid id: 1 to 64 small letters, digits, '-', '_' or '.', starting with a letter or digit.</summary>
    public static bool IsValid(string? id) => id is not null && IdRegex().IsMatch(id);

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonWordRegex();

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,63}$")]
    private static partial Regex IdRegex();
}
