using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using ThixFred.Controls;
using ThixFred.Core.Business;
using ThixFred.Core.Models;

namespace ThixFred.Pages;

/// <summary>
/// The web searches with keywords ("g heise", "wiki Alfred"): list with toolbar, a switch per row, opening a row edits it.
/// Below: Neue Websuche, Standard wiederherstellen.
/// </summary>
public sealed class WebSearchesPage : UserControl, IRefreshable, ISearchable
{
    private static readonly string[] Filters = ["Alle", "Eingeschaltet", "Ausgeschaltet", "Eigene"];
    private static readonly string[] Sortings = ["Name", "Schlüsselwort"];

    private readonly MainWindow _window;
    private readonly ListLayout _layout;

    public WebSearchesPage(MainWindow window)
    {
        _window = window;
        var toolbar = new ListToolbar("Websuchen durchsuchen", Filters, Sortings);
        var list = new EntityList("Keine Websuche passt zur Suche.");
        _layout = new ListLayout("Websuchen", "Schlüsselwort, Leerzeichen, Suchbegriff: „g heise“ sucht mit Google, „wiki Alfred“ in der Wikipedia. Ohne Schlüsselwort bietet der Launcher die Standardsuche an (Einstellungen › Suche).", toolbar, list);
        _layout.AddActions(
            Ui.Button("plus", "Neue Websuche", "MatButton.Primary", () => _window.OpenPage(new WebSearchEditPage(_window, null))),
            Ui.Button("restore", "Standard wiederherstellen", click: () =>
            {
                try
                {
                    WebSearchService.RestoreDefaults();
                    Refresh();
                }
                catch (BusinessException exception)
                {
                    _layout.Message.Show(exception.Message, InfoSeverity.Error);
                }
            }));
        toolbar.Changed += (_, _) => Refresh();
        list.Opened += (_, id) => _window.OpenPage(new WebSearchEditPage(_window, WebSearchService.Find(id)));
        Content = Ui.Page(_layout);
        WebSearchService.Changed += (_, _) => Ui.Post(Refresh);
        Refresh();
    }

    public EntityList List => _layout.List;

    public void FocusSearch() => _layout.Toolbar.SearchBox.Focus();

    public void Refresh()
    {
        var all = WebSearchService.List();
        var toolbar = _layout.Toolbar;
        var fallback = SettingsService.Get().DefaultWebSearchId;
        var shown = all.Where(search => toolbar.FilterIndex switch
            {
                1 => search.IsEnabled,
                2 => !search.IsEnabled,
                3 => !search.IsBuiltIn,
                _ => true,
            })
            .Where(search => toolbar.Matches(search.Name, search.Keyword, search.Url));
        shown = toolbar.SortIndex == 1 ? shown.OrderBy(search => search.Keyword, StringComparer.CurrentCultureIgnoreCase) : shown.OrderBy(search => search.Name, StringComparer.CurrentCultureIgnoreCase);
        _layout.List.Show(shown.Select(search => new EntityRow(search.Id, "globe", search.Name, search.Url)
        {
            Badges = search.Id == fallback ? [search.Keyword, "Standardsuche"] : [search.Keyword],
            IsMuted = !search.IsEnabled,
            Trailing = () => Ui.Switch($"{search.Name} einschalten", search.IsEnabled, enabled =>
            {
                try
                {
                    WebSearchService.Save(search with { IsEnabled = enabled });
                }
                catch (BusinessException exception)
                {
                    _layout.Message.Show(exception.Message, InfoSeverity.Error);
                    Refresh();
                }
            }),
        }).ToList(), all.Count);
    }
}

/// <summary>Create or change a web search: name, keyword, address with {query}, on/off. Speichern, Zurück … Löschen.</summary>
public sealed class WebSearchEditPage : UserControl
{
    private readonly MainWindow _window;
    private readonly WebSearch? _search;
    private readonly EditLayout _layout;
    private readonly TextBox _name = new();
    private readonly TextBox _keyword = new() { MaxWidth = 200, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _url = new() { Watermark = "https://www.example.com/search?q={query}" };
    private readonly ToggleSwitch _enabled;

    public WebSearchEditPage(MainWindow window, WebSearch? search)
    {
        _window = window;
        _search = search;
        _layout = new EditLayout(search is null ? "Neue Websuche" : search.Name);
        _name.Text = search?.Name;
        _keyword.Text = search?.Keyword;
        _url.Text = search?.Url;
        AutomationProperties.SetName(_name, "Name");
        AutomationProperties.SetName(_keyword, "Schlüsselwort");
        AutomationProperties.SetName(_url, "Adresse");
        _enabled = Ui.Switch("Eingeschaltet", search?.IsEnabled ?? true);
        var general = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        general.Children.Add(Ui.Field("Name", _name, nameof(WebSearch.Name)));
        general.Children.Add(Ui.Field("Schlüsselwort", _keyword, nameof(WebSearch.Keyword), "Ein Wort ohne Leerzeichen: „g“, „wiki“."));
        general.Children.Add(Ui.Field("Adresse", _url, nameof(WebSearch.Url), "{query} steht für den Suchbegriff."));
        general.Children.Add(Ui.Row("Eingeschaltet", null, _enabled, last: true));
        _layout.AddTab("Allgemein", general);

        _layout.AddAction("check", "Speichern", Save, "MatButton.Primary");
        _layout.AddAction("back", "Zurück", window.GoBack);
        if (search is not null)
        {
            _layout.AddAction("trash", "Löschen", async () =>
            {
                if (!await Dialogs.ConfirmDialog.AskAsync(Ui.WindowOf(this), $"„{search.Name}“ löschen?", "Die Websuche wird gelöscht – mit der Cloud auf all deinen Computern.", "Löschen", isDestructive: true))
                {
                    return;
                }

                try
                {
                    WebSearchService.Delete(search.Id);
                    window.GoBack();
                }
                catch (BusinessException exception)
                {
                    _layout.ShowError(exception);
                }
            }, "MatButton.Danger", separated: true);
        }

        Content = Ui.Page(_layout, 820);
    }

    private void Save()
    {
        try
        {
            _layout.ClearErrors();
            WebSearchService.Save(new WebSearch
            {
                Id = _search?.Id ?? "",
                Name = _name.Text ?? "",
                Keyword = _keyword.Text ?? "",
                Url = _url.Text ?? "",
                IsEnabled = _enabled.IsChecked == true,
            });
            _window.GoBack();
        }
        catch (BusinessException exception)
        {
            _layout.ShowError(exception);
        }
    }
}
