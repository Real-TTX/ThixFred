using System.Globalization;
using System.Text;

namespace ThixFred.Core.Business;

/// <summary>
/// How well a typed text matches a name, 0 (not at all) to 100 (the same). It runs for every program on every key, so it
/// allocates nothing but works on lower-case, accent-free forms prepared once (<see cref="Prepare"/>):
///   the same name 100 · the start of the name 90 · the start of a word 75 · the initials ("vsc" = Visual Studio Code) 70 ·
///   inside the name 50 · the letters in order ("chrm") 20–40.
/// </summary>
public static class FuzzyMatcher
{
    /// <summary>The form names are compared in: lower case, without accents ("Ärger" -> "arger"), "ß" -> "ss".</summary>
    public static string Prepare(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (character == 'ß')
            {
                builder.Append("ss");
                continue;
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    /// <summary>The first letters of the words: "visual studio code" -> "vsc", "ThixFred" -> "tf" (capitals inside a word start one).</summary>
    public static string Initials(string text)
    {
        var builder = new StringBuilder();
        var previous = ' ';
        foreach (var character in text)
        {
            var startsWord = char.IsLetterOrDigit(character) &&
                             (!char.IsLetterOrDigit(previous) || (char.IsUpper(character) && char.IsLower(previous)));
            if (startsWord)
            {
                builder.Append(character);
            }

            previous = character;
        }

        return Prepare(builder.ToString());
    }

    /// <summary>The score of <paramref name="query"/> (prepared) against a prepared name and its initials.</summary>
    public static int Score(string query, string name, string initials = "")
    {
        if (query.Length == 0 || name.Length == 0)
        {
            return 0;
        }

        if (query.Length > name.Length + 1)
        {
            return 0;
        }

        if (name == query)
        {
            return 100;
        }

        if (name.StartsWith(query, StringComparison.Ordinal))
        {
            // "co" for "code" beats "co" for "control panel extensions": shorter names first
            return 90 - Math.Min(8, (name.Length - query.Length) / 4);
        }

        var index = name.IndexOf(query, StringComparison.Ordinal);
        while (index > 0)
        {
            if (!char.IsLetterOrDigit(name[index - 1]))
            {
                return 75 - Math.Min(8, index / 4);
            }

            index = name.IndexOf(query, index + 1, StringComparison.Ordinal);
        }

        if (initials.Length >= query.Length && query.Length >= 2 && initials.StartsWith(query, StringComparison.Ordinal))
        {
            return initials.Length == query.Length ? 72 : 68;
        }

        if (query.Length >= 2 && name.Contains(query, StringComparison.Ordinal))
        {
            return 50;
        }

        return query.Length >= 2 ? Subsequence(query, name) : 0;
    }

    /// <summary>Every letter of the query in order; compact matches score higher (20 … 40).</summary>
    private static int Subsequence(string query, string name)
    {
        var position = 0;
        var first = -1;
        var gaps = 0;
        foreach (var character in query)
        {
            if (character == ' ')
            {
                continue;
            }

            var found = name.IndexOf(character, position);
            if (found < 0)
            {
                return 0;
            }

            if (first < 0)
            {
                first = found;
            }
            else
            {
                gaps += found - position;
            }

            position = found + 1;
        }

        // letters spread over the whole name are chance ("note" in "Windows Update"): only close ones count
        var spread = position - first;
        if (spread > query.Length * 2 + 2)
        {
            return 0;
        }

        var tightness = query.Length / (double)Math.Max(spread, 1);
        return (int)Math.Round(20 + 20 * tightness) - Math.Min(10, gaps) / 2 - (first == 0 ? 0 : 2);
    }
}
