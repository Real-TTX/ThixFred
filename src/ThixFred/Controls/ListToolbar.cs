using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using MatDesktop;

namespace ThixFred.Controls;

/// <summary>
/// The toolbar above every list (owner's rule): a search text, a filter and a sorting, in one row. The list page reads
/// <see cref="SearchText"/>, <see cref="FilterIndex"/> and <see cref="SortIndex"/> and draws again on <see cref="Changed"/>.
/// Searching runs on every key (lists are in memory). Several toolbars on one page work: no state outside the instance.
/// </summary>
public sealed class ListToolbar : Grid
{
    private readonly TextBox _search;
    private readonly ComboBox _filter;
    private readonly ComboBox _sort;

    public ListToolbar(string searchHint, IReadOnlyList<string> filters, IReadOnlyList<string> sortings)
    {
        ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto");
        Margin = new Thickness(0, 0, 0, 12);
        _search = new TextBox { Watermark = searchHint, HorizontalAlignment = HorizontalAlignment.Stretch }.Themed("MatSearchBox");
        AutomationProperties.SetName(_search, searchHint);
        _filter = new ComboBox { ItemsSource = filters, SelectedIndex = 0, MinWidth = 160, Margin = new Thickness(10, 0, 0, 0), IsVisible = filters.Count > 1 };
        AutomationProperties.SetName(_filter, "Filter");
        ToolTip.SetTip(_filter, "Filter");
        _sort = new ComboBox { ItemsSource = sortings, SelectedIndex = 0, MinWidth = 150, Margin = new Thickness(10, 0, 0, 0), IsVisible = sortings.Count > 1 };
        AutomationProperties.SetName(_sort, "Sortierung");
        ToolTip.SetTip(_sort, "Sortierung");

        // the search fills its column up to 420 px, left-aligned (a MaxWidth on the box itself would centre it)
        var searchCell = new Grid { ColumnDefinitions = { new ColumnDefinition(1, GridUnitType.Star) { MaxWidth = 420 }, new ColumnDefinition(0, GridUnitType.Star) } };
        searchCell.Children.Add(_search);
        Children.Add(searchCell);
        SetColumn(_filter, 1);
        SetColumn(_sort, 2);
        Children.Add(_filter);
        Children.Add(_sort);

        _search.TextChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _filter.SelectionChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _sort.SelectionChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Search, filter or sorting changed.</summary>
    public event EventHandler? Changed;

    public string SearchText => _search.Text?.Trim() ?? "";

    public int FilterIndex
    {
        get => Math.Max(0, _filter.SelectedIndex);
        set => _filter.SelectedIndex = value;
    }

    public int SortIndex
    {
        get => Math.Max(0, _sort.SelectedIndex);
        set => _sort.SelectedIndex = value;
    }

    public TextBox SearchBox => _search;

    /// <summary>True when the text is found in one of the values (case and accents do not matter).</summary>
    public bool Matches(params string?[] values)
    {
        var search = Core.Business.FuzzyMatcher.Prepare(SearchText);
        return search.Length == 0 || values.Any(value => value is not null && Core.Business.FuzzyMatcher.Prepare(value).Contains(search, StringComparison.Ordinal));
    }
}
