using ThixFred.Core.Infrastructure;
using ThixFred.Core.Models;

namespace ThixFred.Core.Business;

/// <summary>
/// The web searches with keywords ("g heise", "wiki Alfred"): list, create, change, delete (websearches.json, synced with the
/// cloud). ThixFred brings a set to start with; the user may change, switch off and delete them and get them back.
/// Every change is checked here (name, keyword, address with {query}) and needs the configuration to be editable.
/// </summary>
public static class WebSearchService
{
    public const string QueryPlaceholder = "{query}";

    private static readonly object Gate = new();
    private static List<WebSearch>? _searches;

    /// <summary>Raised after every change (here or from the cloud).</summary>
    public static event EventHandler? Changed;

    public static IReadOnlyList<WebSearch> Defaults { get; } =
    [
        Default("google", "Google", "g", "https://www.google.com/search?q={query}"),
        Default("duckduckgo", "DuckDuckGo", "ddg", "https://duckduckgo.com/?q={query}"),
        Default("bing", "Bing", "bing", "https://www.bing.com/search?q={query}"),
        Default("wikipedia", "Wikipedia", "wiki", "https://de.wikipedia.org/w/index.php?search={query}"),
        Default("youtube", "YouTube", "yt", "https://www.youtube.com/results?search_query={query}"),
        Default("github", "GitHub", "gh", "https://github.com/search?q={query}"),
        Default("maps", "Google Maps", "maps", "https://www.google.com/maps/search/{query}"),
        Default("deepl", "DeepL Übersetzer", "tr", "https://www.deepl.com/translator#auto/de/{query}"),
        Default("amazon", "Amazon", "amz", "https://www.amazon.de/s?k={query}"),
    ];

    public static IReadOnlyList<WebSearch> List()
    {
        lock (Gate)
        {
            return Searches.ToList();
        }
    }

    public static WebSearch? Find(string id) => List().FirstOrDefault(search => search.Id == id);

    /// <summary>The enabled search with this keyword (case-insensitive).</summary>
    public static WebSearch? FindByKeyword(string keyword) =>
        List().FirstOrDefault(search => search.IsEnabled && string.Equals(search.Keyword, keyword, StringComparison.OrdinalIgnoreCase));

    /// <summary>The address for the search terms (URL-encoded into {query}).</summary>
    public static string BuildUrl(WebSearch search, string terms) =>
        search.Url.Replace(QueryPlaceholder, Uri.EscapeDataString(terms.Trim()), StringComparison.OrdinalIgnoreCase);

    /// <summary>Creates (empty id) or changes a web search.</summary>
    public static WebSearch Save(WebSearch search)
    {
        AccessPolicy.EnsureConfigurationEditable();
        var name = search.Name.Trim();
        var keyword = search.Keyword.Trim().ToLowerInvariant();
        var url = search.Url.Trim();
        if (name.Length == 0)
        {
            throw new BusinessException("Bitte einen Namen angeben.", nameof(WebSearch.Name));
        }

        if (keyword.Length == 0 || keyword.Any(char.IsWhiteSpace) || keyword.Length > 20)
        {
            throw new BusinessException("Das Schlüsselwort braucht 1 bis 20 Zeichen ohne Leerzeichen.", nameof(WebSearch.Keyword));
        }

        if (!url.Contains(QueryPlaceholder, StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(url.Replace(QueryPlaceholder, "x", StringComparison.OrdinalIgnoreCase), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new BusinessException("Die Adresse muss mit http:// oder https:// beginnen und {query} enthalten.", nameof(WebSearch.Url));
        }

        lock (Gate)
        {
            var id = search.Id.Length > 0 ? search.Id : Slug.Make(name, "suche", candidate => Searches.Any(other => other.Id == candidate));
            if (Searches.Any(other => other.Id != id && other.IsEnabled && search.IsEnabled && string.Equals(other.Keyword, keyword, StringComparison.OrdinalIgnoreCase)))
            {
                throw new BusinessException($"Das Schlüsselwort „{keyword}“ hat schon eine andere Websuche.", nameof(WebSearch.Keyword));
            }

            var saved = search with { Id = id, Name = name, Keyword = keyword, Url = url, UpdatedAt = AppClock.UtcNow };
            var index = Searches.FindIndex(other => other.Id == id);
            if (index >= 0)
            {
                saved = saved with { IsBuiltIn = Searches[index].IsBuiltIn };
                Searches[index] = saved;
            }
            else
            {
                Searches.Add(saved with { IsBuiltIn = false });
            }

            Write();
            return saved;
        }
    }

    public static void Delete(string id)
    {
        AccessPolicy.EnsureConfigurationEditable();
        if (SettingsService.Get().DefaultWebSearchId == id)
        {
            throw new BusinessException("Das ist die Websuche für „Im Web suchen“ – erst eine andere wählen (Einstellungen › Suche).");
        }

        lock (Gate)
        {
            if (Searches.RemoveAll(search => search.Id == id) > 0)
            {
                Write();
            }
        }
    }

    /// <summary>Brings the searches ThixFred came with back (the user's own stay).</summary>
    public static void RestoreDefaults()
    {
        AccessPolicy.EnsureConfigurationEditable();
        lock (Gate)
        {
            foreach (var search in Defaults)
            {
                Searches.RemoveAll(existing => existing.Id == search.Id);
                Searches.Add(search with { UpdatedAt = AppClock.UtcNow });
            }

            Write();
        }
    }

    /// <summary>The sync: the stored searches as they are now.</summary>
    internal static IReadOnlyList<WebSearch> Snapshot() => List();

    /// <summary>The sync: a search of the cloud (null = deleted there); no configuration lock, it comes from the account.</summary>
    internal static void ApplyFromCloud(string id, WebSearch? search)
    {
        lock (Gate)
        {
            Searches.RemoveAll(existing => existing.Id == id);
            if (search is not null)
            {
                Searches.Add(search with { Id = id });
            }

            Write();
        }
    }

    /// <summary>Tests start with the defaults.</summary>
    internal static void ResetForTests()
    {
        lock (Gate)
        {
            _searches = null;
        }
    }

    private static List<WebSearch> Searches => _searches ??= Load();

    private static List<WebSearch> Load()
    {
        var stored = JsonFile.Read<List<WebSearch>>(AppPaths.WebSearchesFile);
        return stored ?? Defaults.ToList();
    }

    private static void Write()
    {
        _searches = Searches.OrderBy(search => search.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        JsonFile.Write(AppPaths.WebSearchesFile, _searches);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static WebSearch Default(string id, string name, string keyword, string url) =>
        new() { Id = id, Name = name, Keyword = keyword, Url = url, IsBuiltIn = true };
}
