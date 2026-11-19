using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using MatDesktop;
using ThixFred.Core.Models;

namespace ThixFred.Controls;

/// <summary>
/// The toolbar above every list (owner's rule): a search text, a filter and a sorting, in one row. The list page reads
/// <see cref="SearchText"/>, <see cref="FilterIndex"/> and <see cref="SortIndex"/> and draws again on <see cref="Changed"/>.
/// Searching runs on every key (lists are in memory). A list page with two looks also gets the switch tiles / list at the right
/// (<see cref="ShowLookSwitch"/>). Several toolbars on one page work: no state outside the instance.
/// </summary>
public sealed class ListToolbar : Grid
{
    private readonly TextBox _search;
    private readonly ComboBox _filter;
    private readonly ComboBox _sort;
    private Button? _tiles;
    private Button? _rows;

    public ListToolbar(string searchHint, IReadOnlyList<string> filters, IReadOnlyList<string> sortings)
    {
        ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto");
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

    /// <summary>The switch tiles / list was clicked.</summary>
    public event EventHandler<ListLook>? LookChanged;

    /// <summary>Shows the switch tiles / list (two icon buttons in one frame, the current one in the tint of the accent).</summary>
    public void ShowLookSwitch(ListLook look)
    {
        if (_tiles is null)
        {
            _tiles = LookButton("grid", "Kacheln", ListLook.Tiles);
            _rows = LookButton("list", "Liste", ListLook.List);
            var frame = new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { _tiles, _rows } },
            }.Use(Border.BorderBrushProperty, "Mat.BorderStrong").Use(Border.CornerRadiusProperty, "Mat.Radius");
            SetColumn(frame, 3);
            Children.Add(frame);
        }

        Mark(_tiles, look == ListLook.Tiles);
        Mark(_rows!, look == ListLook.List);
    }

    private Button LookButton(string icon, string name, ListLook look)
    {
        var button = new Button { Content = new MatIcon { Icon = icon, Size = 16 }, Width = 32, Height = 28, MinHeight = 28, Padding = new Thickness(0) }.Themed("MatButton.Icon");
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, name);
        button.Click += (_, _) =>
        {
            ShowLookSwitch(look);
            LookChanged?.Invoke(this, look);
        };
        return button;
    }

    private static void Mark(Button button, bool isCurrent)
    {
        if (isCurrent)
        {
            button.Use(Button.BackgroundProperty, "Mat.AccentSoftStrong").Use(Button.ForegroundProperty, "Mat.AccentText");
        }
        else
        {
            button.ClearValue(Button.BackgroundProperty);
            button.ClearValue(Button.ForegroundProperty);
        }
    }

    /// <summary>Clicks tiles or list (tests).</summary>
    public void ChooseLook(ListLook look) => (look == ListLook.Tiles ? _tiles : _rows)?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

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
