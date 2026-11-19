using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using MatDesktop;
using ThixFred.Core.Models;

namespace ThixFred.Controls;

/// <summary>
/// The list page of an entity (owner's rule): the title, a line about it, a message, THE toolbar (search, filter, sort), the
/// list and the actions below it, left-aligned from positive to negative. With a <c>lookKey</c> the list shows tiles or rows,
/// switched in the toolbar and remembered per page (SettingsService.RememberLook); rows keep the width of a page, tiles use it all.
/// </summary>
public sealed class ListLayout : StackPanel
{
    public const double RowsWidth = 920;

    public ListLayout(string title, string description, ListToolbar toolbar, EntityList list, string? lookKey = null)
    {
        Spacing = 0;
        Toolbar = toolbar;
        List = list;
        MaxWidth = RowsWidth;
        if (lookKey is not null)
        {
            var look = Core.Business.SettingsService.LookOf(lookKey);
            ShowLook(look);
            toolbar.ShowLookSwitch(look);
            toolbar.LookChanged += (_, chosen) =>
            {
                ShowLook(chosen);
                Core.Business.SettingsService.RememberLook(lookKey, chosen);
            };
        }

        Children.Add(new TextBlock { Text = title }.Themed("MatText.PageTitle"));
        Children.Add(new TextBlock { Text = description, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 14) }.Themed("MatText.Muted"));
        Children.Add(Message);
        Children.Add(toolbar);
        Children.Add(list);
        Children.Add(Actions);
    }

    public InfoBar Message { get; } = new() { Margin = new Thickness(0, 0, 0, 12) };

    public ListToolbar Toolbar { get; }

    public EntityList List { get; }

    public ActionBar Actions { get; } = new() { Margin = new Thickness(0, 14, 0, 0) };

    /// <summary>Tiles or rows (also from the switch of the toolbar).</summary>
    public void ShowLook(ListLook look)
    {
        List.Look = look;
        MaxWidth = look == ListLook.Tiles ? double.PositiveInfinity : RowsWidth;
    }

    /// <summary>The buttons below the list, positive to negative.</summary>
    public void AddActions(params Control[] buttons)
    {
        foreach (var button in buttons)
        {
            Actions.Children.Add(button);
        }
    }
}

/// <summary>
/// The create / change page of an entity (owner's rule): the title, a message, tabs for its areas, and one row of buttons below,
/// positive to negative: Speichern, Zurück … (gap) … Löschen. Errors of the business layer appear at their field
/// (<see cref="ShowError"/>) or above the tabs.
/// </summary>
public sealed class EditLayout : StackPanel
{
    public EditLayout(string title, string? description = null)
    {
        TitleText = new TextBlock { Text = title }.Themed("MatText.PageTitle");
        Children.Add(TitleText);
        if (description is not null)
        {
            Children.Add(new TextBlock { Text = description, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) }.Themed("MatText.Muted"));
        }

        Message.Margin = new Thickness(0, 14, 0, 0);
        Children.Add(Message);
        Tabs.Margin = new Thickness(0, 12, 0, 0);
        Children.Add(Tabs);
        Actions.Margin = new Thickness(0, 18, 0, 0);
        Children.Add(Actions);
    }

    public TextBlock TitleText { get; }

    public InfoBar Message { get; } = new();

    public TabControl Tabs { get; } = new();

    public ActionBar Actions { get; } = new();

    public void AddTab(string header, Control content) =>
        Tabs.Items.Add(new TabItem { Header = header, Content = new StackPanel { Margin = new Thickness(0, 8, 0, 0), Children = { content } } });

    /// <summary>Adds a button to the row below; <paramref name="separated"/> puts the gap before it (delete).</summary>
    public Button AddAction(string icon, string text, Action click, string? theme = null, bool separated = false)
    {
        var button = Ui.Button(icon, text, theme, click);
        ActionBar.SetSeparated(button, separated);
        Actions.Children.Add(button);
        return button;
    }

    /// <summary>A business error at its field (its tab is chosen), else above the tabs.</summary>
    public void ShowError(BusinessException exception)
    {
        ClearErrors();
        var target = exception.Field is null ? null : Fields().FirstOrDefault(formField => string.Equals(FormErrors.GetField(formField), exception.Field, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            Message.Show(exception.Message, InfoSeverity.Error);
            return;
        }

        target.Error = exception.Message;
        foreach (var tab in Tabs.Items.OfType<TabItem>())
        {
            if (tab.Content is Control content && content.GetLogicalDescendants().Contains(target))
            {
                Tabs.SelectedItem = tab;
            }
        }
    }

    public void ClearErrors()
    {
        foreach (var formField in Fields())
        {
            formField.Error = null;
        }

        Message.Hide();
    }

    /// <summary>The fields of every tab (also those not shown now: the logical tree has them).</summary>
    private IEnumerable<MatFormField> Fields() => this.GetLogicalDescendants().OfType<MatFormField>().Distinct();
}

/// <summary>Logical descendants without the extension namespace of Avalonia.</summary>
internal static class LogicalTreeExtensions
{
    public static IEnumerable<Avalonia.LogicalTree.ILogical> GetLogicalDescendants(this Control control) =>
        Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(control);
}
