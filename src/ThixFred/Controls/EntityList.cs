using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using MatDesktop;
using ThixFred.Core.Models;

namespace ThixFred.Controls;

/// <summary>One entry of an <see cref="EntityList"/>: what it shows; <see cref="Id"/> comes back when it is opened.</summary>
public sealed record EntityRow(string Id, string Icon, string Title, string Subtitle)
{
    /// <summary>Small labels after the title ("Store", "aus", "v1.2.0").</summary>
    public IReadOnlyList<string> Badges { get; init; } = [];

    /// <summary>A switched-off or unsupported entry: shown faint.</summary>
    public bool IsMuted { get; init; }

    /// <summary>Something of its own at the left of a row instead of the icon (the colours of a theme).</summary>
    public Func<Control>? Leading { get; init; }

    /// <summary>What fills the band of a tile instead of the big icon (a small launcher in the colours of a theme).</summary>
    public Func<Control>? Banner { get; init; }

    /// <summary>Something at the right of a row / the foot of a tile (a switch, a button); it does not open the entry.</summary>
    public Func<Control>? Trailing { get; init; }
}

/// <summary>
/// The entries of a list page (plugins, store, themes, web searches), in one of two looks (<see cref="Look"/>):
/// <list type="bullet">
/// <item><b>Tiles</b> (like a store): cards side by side, the icon big on a band, name, labels, two lines of text and the switch or
/// button at the foot; a click opens the entry; "add" tiles (<see cref="AddTile"/>) at the end.</item>
/// <item><b>List</b>: card rows (MatList + MatCardRow); Enter, a double click or the chevron open a row.</item>
/// </list>
/// The number of entries below, a text when nothing is there. One code base for every list; several on a page work.
/// </summary>
public sealed class EntityList : StackPanel
{
    public const double TileWidth = 248;
    public const double TileHeight = 256;
    private const string AddPrefix = "add:";

    private readonly ListBox _list;
    private readonly TextBlock _empty;
    private readonly TextBlock _count;
    private readonly ITemplate<Panel?> _rowPanel;
    private readonly List<(string Icon, string Title, string Text, Action Click)> _addTiles = [];
    private IReadOnlyList<EntityRow> _rows = [];
    private List<Control> _items = [];
    private int _total;
    private ListLook _look = ListLook.List;

    public EntityList(string emptyText)
    {
        Spacing = 8;
        _list = new ListBox { SelectionMode = SelectionMode.Single }.Themed("MatList");
        _list.Use(ItemsControl.ItemContainerThemeProperty, "MatCardRow");
        _rowPanel = _list.ItemsPanel;
        _empty = new TextBlock { Text = emptyText, Margin = new Thickness(4, 16), TextWrapping = TextWrapping.Wrap }.Themed("MatText.Muted");
        _count = new TextBlock { Margin = new Thickness(4, 0, 0, 0) }.Themed("MatText.Faint");
        Children.Add(_list);
        Children.Add(_empty);
        Children.Add(_count);
        AddTileStyles();
        _list.ContainerPrepared += (_, e) =>
        {
            var tag = e.Index < _items.Count ? _items[e.Index].Tag as string : null;
            e.Container.Classes.Set("tile", _look == ListLook.Tiles);
            e.Container.Classes.Set("add", tag?.StartsWith(AddPrefix, StringComparison.Ordinal) == true);
        };
        _list.DoubleTapped += (_, _) =>
        {
            if (_look == ListLook.List)
            {
                OpenSelected();
            }
        };
        _list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                OpenSelected();
            }
        };
    }

    /// <summary>An entry was opened (its id).</summary>
    public event EventHandler<string>? Opened;

    public ListBox List => _list;

    /// <summary>The entries shown now (without the "add" tiles).</summary>
    public IReadOnlyList<EntityRow> Rows => _rows;

    public string? SelectedId => (_list.SelectedItem as Control)?.Tag as string is { } id && !id.StartsWith(AddPrefix, StringComparison.Ordinal) ? id : null;

    /// <summary>Tiles or list; the entries are drawn again.</summary>
    public ListLook Look
    {
        get => _look;
        set
        {
            if (_look == value)
            {
                return;
            }

            _look = value;
            _list.ItemsPanel = value == ListLook.Tiles ? new FuncTemplate<Panel?>(() => new TileGrid()) : _rowPanel;
            Show(_rows, _total);
        }
    }

    /// <summary>A tile at the end of the tiles that does something (Hinzufügen, Neues Theme).</summary>
    public void AddTile(string icon, string title, string text, Action click) => _addTiles.Add((icon, title, text, click));

    /// <summary>Shows the entries (the selection stays on the same id when it is still there).</summary>
    public void Show(IReadOnlyList<EntityRow> rows, int total)
    {
        var selected = SelectedId;
        _rows = rows;
        _total = total;
        _items = rows.Select(row => _look == ListLook.Tiles ? BuildTile(row) : BuildRow(row)).ToList();
        if (_look == ListLook.Tiles)
        {
            _items.AddRange(_addTiles.Select((tile, index) => BuildAddTile(index, tile.Icon, tile.Title, tile.Text, tile.Click)));
        }

        _list.ItemsSource = _items;
        _empty.IsVisible = rows.Count == 0;
        _list.IsVisible = _items.Count > 0;
        _count.Text = rows.Count == total ? $"{total} {(total == 1 ? "Eintrag" : "Einträge")}" : $"{rows.Count} von {total} Einträgen";
        if (selected is not null && rows.FirstOrDefault(row => row.Id == selected) is { } again)
        {
            _list.SelectedIndex = rows.ToList().IndexOf(again);
        }
    }

    private void OpenSelected()
    {
        if ((_list.SelectedItem as Control)?.Tag is not string tag)
        {
            return;
        }

        if (tag.StartsWith(AddPrefix, StringComparison.Ordinal))
        {
            _addTiles[int.Parse(tag[AddPrefix.Length..], System.Globalization.CultureInfo.InvariantCulture)].Click();
        }
        else
        {
            Opened?.Invoke(this, tag);
        }
    }

    /// <summary>The look of the tiles: the size of a card, its band in the tint of its state (pointer, chosen), an "add" tile open.</summary>
    private void AddTileStyles()
    {
        static Setter Resource(AvaloniaProperty property, string key) => new(property, new DynamicResourceExtension(key));
        Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class("tile"))
        {
            Setters =
            {
                new Setter(HeightProperty, TileHeight),
                new Setter(ListBoxItem.PaddingProperty, new Thickness(0)),
                new Setter(MarginProperty, new Thickness(0)),
            },
        });
        Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class("tile").Template().OfType<Border>().Name("Frame"))
        {
            Setters = { new Setter(ClipToBoundsProperty, true) },
        });
        Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class("tile").Descendant().OfType<Border>().Class("tile-band"))
        {
            Setters = { Resource(Border.BackgroundProperty, "Mat.Surface2") },
        });
        Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class("tile").Class(":pointerover").Descendant().OfType<Border>().Class("tile-band"))
        {
            Setters = { Resource(Border.BackgroundProperty, "Mat.Surface3") },
        });
        Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class("tile").Class(":selected").Descendant().OfType<Border>().Class("tile-band"))
        {
            Setters = { Resource(Border.BackgroundProperty, "Mat.AccentSoftStrong") },
        });
        Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class("add").Template().OfType<Border>().Name("Frame"))
        {
            Setters =
            {
                Resource(Border.BackgroundProperty, "Mat.Surface"),
                Resource(Border.BorderBrushProperty, "Mat.BorderStrong"),
            },
        });
        Styles.Add(new Style(x => x.OfType<ListBoxItem>().Class("add").Class(":pointerover").Template().OfType<Border>().Name("Frame"))
        {
            Setters = { Resource(Border.BackgroundProperty, "Mat.Surface2"), Resource(Border.BorderBrushProperty, "Mat.Accent") },
        });
    }

    private Control BuildRow(EntityRow row)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Tag = row.Id, MinHeight = 44 };
        var leading = row.Leading?.Invoke() ?? IconTile(row.Icon, 36, 18, "Mat.AccentSoft");
        leading.Margin = new Thickness(0, 0, 14, 0);
        leading.VerticalAlignment = VerticalAlignment.Center;

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new TextBlock { Text = row.Title, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        foreach (var badge in row.Badges)
        {
            title.Children.Add(Badge(badge));
        }

        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title);
        if (row.Subtitle.Length > 0)
        {
            text.Children.Add(new TextBlock { Text = row.Subtitle, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 }.Themed("MatText.Muted"));
        }

        Grid.SetColumn(text, 1);
        grid.Children.Add(leading);
        grid.Children.Add(text);
        if (row.Trailing?.Invoke() is { } trailing)
        {
            trailing.Margin = new Thickness(12, 0, 0, 0);
            trailing.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(trailing, 2);
            grid.Children.Add(trailing);
        }

        var open = new Button { Content = new MatIcon { Icon = "chevron-right", Size = 16 }, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }.Themed("MatButton.Icon");
        AutomationProperties.SetName(open, $"{row.Title} öffnen");
        ToolTip.SetTip(open, "Öffnen");
        open.Click += (_, _) => Opened?.Invoke(this, row.Id);
        Grid.SetColumn(open, 3);
        grid.Children.Add(open);
        if (row.IsMuted)
        {
            text.Opacity = 0.55;
            leading.Opacity = 0.55;
        }

        AutomationProperties.SetName(grid, row.Title);
        return grid;
    }

    /// <summary>A tile: the band with the big icon (or the banner), the name, the labels, the text and the switch / button at the foot.</summary>
    private Control BuildTile(EntityRow row)
    {
        const double radius = MatTokens.Size.RadiusLg - 1;
        var bandContent = row.Banner?.Invoke() ?? IconTile(row.Icon, 64, 30, "Mat.Surface");
        bandContent.HorizontalAlignment = HorizontalAlignment.Center;
        bandContent.VerticalAlignment = VerticalAlignment.Center;
        var band = new Border { Height = 104, CornerRadius = new CornerRadius(radius, radius, 0, 0), Child = bandContent };
        band.Classes.Add("tile-band");

        var texts = new StackPanel { Margin = new Thickness(16, 12, 16, 0), ClipToBounds = true };
        texts.Children.Add(new TextBlock { Text = row.Title, FontSize = MatTokens.Type.FsLg, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        if (row.Badges.Count > 0)
        {
            var badges = new WrapPanel { Margin = new Thickness(0, 6, 0, 0), MaxHeight = 22, ClipToBounds = true };
            foreach (var badge in row.Badges)
            {
                var label = Badge(badge);
                label.Margin = new Thickness(0, 0, 6, 4);
                badges.Children.Add(label);
            }

            ToolTip.SetTip(badges, string.Join(" · ", row.Badges));
            texts.Children.Add(badges);
        }

        if (row.Subtitle.Length > 0)
        {
            var subtitle = new TextBlock { Text = row.Subtitle, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 2, Margin = new Thickness(0, 6, 0, 0) }.Themed("MatText.Muted");
            ToolTip.SetTip(subtitle, row.Subtitle);
            texts.Children.Add(subtitle);
        }

        var tile = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Tag = row.Id };
        tile.Children.Add(band);
        Grid.SetRow(texts, 1);
        tile.Children.Add(texts);
        if (row.Trailing?.Invoke() is { } trailing)
        {
            trailing.Margin = new Thickness(16, 0, 16, 14);
            trailing.HorizontalAlignment = trailing is Button and not ToggleSwitch ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            trailing.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetRow(trailing, 2);
            tile.Children.Add(trailing);
        }

        if (row.IsMuted)
        {
            texts.Opacity = 0.55;
            bandContent.Opacity = 0.55;
        }

        // a click opens the tile – not a click on its switch or button
        tile.Tapped += (_, e) =>
        {
            if (e.Source is Visual source && (source as Button ?? source.FindAncestorOfType<Button>()) is null)
            {
                Opened?.Invoke(this, row.Id);
            }
        };
        AutomationProperties.SetName(tile, row.Title);
        ToolTip.SetTip(band, "Öffnen");
        return tile;
    }

    private Control BuildAddTile(int index, string icon, string title, string text, Action click)
    {
        var content = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0) };
        var circle = new Border
        {
            Width = 56,
            Height = 56,
            CornerRadius = new CornerRadius(28),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6),
            Child = new MatIcon { Icon = icon, Size = 26, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }.Use(MatIcon.ForegroundProperty, "Mat.AccentText"),
        }.Use(Border.BackgroundProperty, "Mat.AccentSoft");
        content.Children.Add(circle);
        content.Children.Add(new TextBlock { Text = title, FontSize = MatTokens.Type.FsLg, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = text, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap }.Themed("MatText.Muted"));
        var tile = new Grid { Tag = AddPrefix + index.ToString(System.Globalization.CultureInfo.InvariantCulture), Children = { content } };
        tile.Tapped += (_, _) => click();
        AutomationProperties.SetName(tile, title);
        return tile;
    }

    private static Border IconTile(string icon, double size, double iconSize, string background) => new Border
    {
        Width = size,
        Height = size,
        CornerRadius = new CornerRadius(size / 4),
        Child = new MatIcon { Icon = icon, Size = iconSize, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }.Use(MatIcon.ForegroundProperty, "Mat.AccentText"),
    }.Use(Border.BackgroundProperty, background);

    private static Border Badge(string text) => new Border { Child = new TextBlock { Text = text }, VerticalAlignment = VerticalAlignment.Center }.Themed("MatBadge.Soft");
}

/// <summary>
/// The panel of the tiles: as many columns as fit with at least <see cref="EntityList.TileWidth"/> each, the tiles stretched to
/// fill the width exactly, a gap between them; the arrow keys move by one tile or one row.
/// </summary>
internal sealed class TileGrid : Panel, INavigableContainer
{
    public const double Gap = 14;

    private int _columns = 1;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? EntityList.TileWidth * 3 + Gap * 2 : availableSize.Width;
        _columns = Math.Max(1, (int)((width + Gap) / (EntityList.TileWidth + Gap)));
        var tileWidth = (width - Gap * (_columns - 1)) / _columns;
        double rowHeight = 0;
        foreach (var child in Children)
        {
            child.Measure(new Size(tileWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
        }

        var rows = (Children.Count + _columns - 1) / _columns;
        return new Size(width, rows == 0 ? 0 : rows * rowHeight + (rows - 1) * Gap);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var tileWidth = (finalSize.Width - Gap * (_columns - 1)) / _columns;
        var rowHeight = Children.Count == 0 ? 0 : Children.Max(child => child.DesiredSize.Height);
        for (var index = 0; index < Children.Count; index++)
        {
            var (row, column) = Math.DivRem(index, _columns);
            Children[index].Arrange(new Rect(column * (tileWidth + Gap), row * (rowHeight + Gap), tileWidth, rowHeight));
        }

        return finalSize;
    }

    IInputElement? INavigableContainer.GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        var index = from is Control control ? Children.IndexOf(control) : -1;
        var next = direction switch
        {
            NavigationDirection.Left or NavigationDirection.Previous => index - 1,
            NavigationDirection.Right or NavigationDirection.Next => index + 1,
            NavigationDirection.Up => index - _columns,
            NavigationDirection.Down => index + _columns,
            NavigationDirection.First => 0,
            NavigationDirection.Last => Children.Count - 1,
            _ => -1,
        };
        return index < 0 && direction is not (NavigationDirection.First or NavigationDirection.Last) ? Children.FirstOrDefault()
            : next >= 0 && next < Children.Count ? Children[next] : null;
    }
}
