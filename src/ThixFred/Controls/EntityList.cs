using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;

namespace ThixFred.Controls;

/// <summary>One row of an <see cref="EntityList"/>: what it shows; <see cref="Id"/> comes back when it is opened.</summary>
public sealed record EntityRow(string Id, string Icon, string Title, string Subtitle)
{
    /// <summary>Small labels after the title ("Store", "aus", "v1.2.0").</summary>
    public IReadOnlyList<string> Badges { get; init; } = [];

    /// <summary>A switched-off or unsupported entry: shown faint.</summary>
    public bool IsMuted { get; init; }

    /// <summary>Something of its own at the left instead of the icon (the colours of a theme).</summary>
    public Func<Control>? Leading { get; init; }

    /// <summary>Something at the right of the row (a switch, a button); it does not open the row.</summary>
    public Func<Control>? Trailing { get; init; }
}

/// <summary>
/// The list of a list page (plugins, store, themes, web searches): rows as cards in MatDesktop's list (MatList + MatCardRow),
/// the number of entries below, a text when nothing is there. Enter, a double click or the chevron open a row
/// (<see cref="Opened"/>). One code base for every list; several on a page work.
/// </summary>
public sealed class EntityList : StackPanel
{
    private readonly ListBox _list;
    private readonly TextBlock _empty;
    private readonly TextBlock _count;
    private IReadOnlyList<EntityRow> _rows = [];

    public EntityList(string emptyText)
    {
        Spacing = 8;
        _list = new ListBox { SelectionMode = SelectionMode.Single }.Themed("MatList");
        _list.Use(ItemsControl.ItemContainerThemeProperty, "MatCardRow");
        _empty = new TextBlock { Text = emptyText, Margin = new Thickness(4, 16), TextWrapping = TextWrapping.Wrap }.Themed("MatText.Muted");
        _count = new TextBlock { Margin = new Thickness(4, 0, 0, 0) }.Themed("MatText.Faint");
        Children.Add(_list);
        Children.Add(_empty);
        Children.Add(_count);
        _list.DoubleTapped += (_, _) => OpenSelected();
        _list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                OpenSelected();
            }
        };
    }

    /// <summary>A row was opened (its id).</summary>
    public event EventHandler<string>? Opened;

    public ListBox List => _list;

    public string? SelectedId => (_list.SelectedItem as Control)?.Tag as string;

    /// <summary>Shows the rows (the selection stays on the same id when it is still there).</summary>
    public void Show(IReadOnlyList<EntityRow> rows, int total)
    {
        var selected = SelectedId;
        _rows = rows;
        _list.ItemsSource = rows.Select(Build).ToList();
        _empty.IsVisible = rows.Count == 0;
        _list.IsVisible = rows.Count > 0;
        _count.Text = rows.Count == total ? $"{total} {(total == 1 ? "Eintrag" : "Einträge")}" : $"{rows.Count} von {total} Einträgen";
        if (selected is not null && rows.FirstOrDefault(row => row.Id == selected) is { } again)
        {
            _list.SelectedIndex = rows.ToList().IndexOf(again);
        }
    }

    private void OpenSelected()
    {
        if (SelectedId is { } id)
        {
            Opened?.Invoke(this, id);
        }
    }

    private Control Build(EntityRow row)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Tag = row.Id, MinHeight = 44 };
        var leading = row.Leading?.Invoke() ?? new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(9),
            Child = new MatIcon { Icon = row.Icon, Size = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }.Use(MatIcon.ForegroundProperty, "Mat.AccentText"),
        }.Use(Border.BackgroundProperty, "Mat.AccentSoft");
        leading.Margin = new Thickness(0, 0, 14, 0);
        leading.VerticalAlignment = VerticalAlignment.Center;

        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        title.Children.Add(new TextBlock { Text = row.Title, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        foreach (var badge in row.Badges)
        {
            title.Children.Add(new Border { Child = new TextBlock { Text = badge }, VerticalAlignment = VerticalAlignment.Center }.Themed("MatBadge.Soft"));
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
}
