using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using MatDesktop;
using ThixFred.Controls;
using ThixFred.Core.Business;
using ThixFred.Core.Models;

namespace ThixFred.Pages;

/// <summary>
/// The store: plugins and themes to install. The toolbar searches, filters (plugins, themes, installed, updates) and sorts; a row
/// has its install / update button, opening it shows its page. The catalog loads when the page is shown (at most once an hour,
/// "Aktualisieren" asks again).
/// </summary>
public sealed class StorePage : UserControl, IRefreshable, ISearchable
{
    private static readonly string[] Filters = ["Alles", "Plugins", "Themes", "Installiert", "Updates verfügbar"];
    private static readonly string[] Sortings = ["Name", "Zuletzt aktualisiert"];

    private readonly MainWindow _window;
    private readonly ListLayout _layout;
    private IReadOnlyList<StoreListing> _listings = [];
    private bool _isLoading;

    public StorePage(MainWindow window)
    {
        _window = window;
        var toolbar = new ListToolbar("Store durchsuchen", Filters, Sortings);
        var list = new EntityList("Nichts gefunden.");
        _layout = new ListLayout("Store", "Plugins und Themes für ThixFred. Jedes Paket wird vor der Installation geprüft (Prüfsumme); mit der Cloud kommt es auch auf deine anderen Computer.", toolbar, list);
        _layout.AddActions(
            Ui.Button("refresh", "Aktualisieren", click: () => _ = LoadAsync(refresh: true)),
            Ui.Button("plug", "Installierte Plugins", click: () => _window.ShowOn(AppCommand.Plugins)));
        toolbar.Changed += (_, _) => Show();
        list.Opened += (_, id) =>
        {
            if (_listings.FirstOrDefault(listing => listing.Item.Id == id) is { } listing)
            {
                _window.OpenPage(new StoreItemPage(_window, listing));
            }
        };
        Content = Ui.Page(_layout);
        StoreService.Changed += (_, _) => Ui.Post(() => _ = LoadAsync(refresh: false));
    }

    public EntityList List => _layout.List;

    public void FocusSearch() => _layout.Toolbar.SearchBox.Focus();

    public void Refresh() => _ = LoadAsync(refresh: false);

    /// <summary>Shows a catalog without asking the store (snapshots).</summary>
    internal void ShowListings(IReadOnlyList<StoreListing> listings)
    {
        _listings = listings;
        _layout.Message.Hide();
        Show();
    }

    private async Task LoadAsync(bool refresh)
    {
        if (_isLoading)
        {
            return;
        }

        _isLoading = true;
        try
        {
            _layout.Message.Show("Lade den Store …", InfoSeverity.Info);
            _listings = await StoreService.LoadAsync(refresh);
            _layout.Message.Hide();
        }
        catch (BusinessException exception)
        {
            _layout.Message.Show(exception.Message, InfoSeverity.Warning);
        }
        finally
        {
            _isLoading = false;
        }

        Show();
    }

    private void Show()
    {
        var toolbar = _layout.Toolbar;
        var shown = _listings.Where(listing => toolbar.FilterIndex switch
            {
                1 => listing.Item.Kind == StoreItemKind.Plugin,
                2 => listing.Item.Kind == StoreItemKind.Theme,
                3 => listing.IsInstalled,
                4 => listing.HasUpdate,
                _ => true,
            })
            .Where(listing => toolbar.Matches(listing.Item.Name, listing.Item.Description, listing.Item.Keyword, listing.Item.Author));
        shown = toolbar.SortIndex == 1
            ? shown.OrderByDescending(listing => listing.Item.UpdatedAt)
            : shown.OrderBy(listing => listing.Item.Name, StringComparer.CurrentCultureIgnoreCase);
        _layout.List.Show(shown.Select(Row).ToList(), _listings.Count);
    }

    private EntityRow Row(StoreListing listing)
    {
        var item = listing.Item;
        var badges = new List<string> { item.Kind == StoreItemKind.Theme ? "Theme" : "Plugin", $"v{item.Version}" };
        if (item.Keyword.Length > 0)
        {
            badges.Insert(0, item.Keyword);
        }

        if (listing.HasUpdate)
        {
            badges.Add($"Update (installiert: v{listing.InstalledVersion})");
        }
        else if (listing.IsInstalled)
        {
            badges.Add("installiert");
        }

        return new EntityRow(item.Id, item.Kind == StoreItemKind.Theme ? "palette" : item.Icon, item.Name, item.Description)
        {
            Badges = badges,
            IsMuted = !listing.IsSupported,
            Trailing = () => InstallButton(listing, () => Show()),
        };
    }

    /// <summary>Installieren / Aktualisieren / Installiert: one button for the row and the page of an entry.</summary>
    internal static Control InstallButton(StoreListing listing, Action done, Action<string>? showError = null)
    {
        if (!listing.IsSupported)
        {
            return Ui.Text("Nicht für dieses System", "MatText.Faint", wrap: false);
        }

        if (listing.IsInstalled && !listing.HasUpdate)
        {
            return new Controls.IconLabel { Icon = "check", Text = "Installiert" };
        }

        Button? button = null;
        button = Ui.Button("download", listing.HasUpdate ? "Aktualisieren" : "Installieren", listing.HasUpdate ? "MatButton.Primary" : null, async () =>
        {
            button!.IsEnabled = false;
            try
            {
                await StoreService.InstallAsync(listing.Item);
                done();
            }
            catch (BusinessException exception)
            {
                showError?.Invoke(exception.Message);
                if (showError is null)
                {
                    App.Instance.Notify("Store", exception.Message, isWarning: true);
                }

                button.IsEnabled = true;
            }
        });
        return button;
    }
}

/// <summary>The page of a store entry: what it is and does (Übersicht), its facts (Details); Installieren / Aktualisieren, Zurück … Entfernen.</summary>
public sealed class StoreItemPage : UserControl
{
    public StoreItemPage(MainWindow window, StoreListing listing)
    {
        var item = listing.Item;
        var layout = new EditLayout(item.Name, item.Description);
        var overview = new StackPanel { Spacing = 10, MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        overview.Children.Add(Ui.Text(item.Kind == StoreItemKind.Theme
            ? "Ein Theme ändert das Aussehen des Launchers. Nach der Installation unter Themes auswählen."
            : item.Keyword.Length > 0
                ? $"Tippe im Launcher „{item.Keyword}“ und dahinter, was das Plugin bekommt."
                : "Das Plugin antwortet auf jede Suche."));
        overview.Children.Add(Ui.Text("Plugins führen Skripte auf deinem Computer aus. Installiere nur, was du verstehst oder wem du vertraust.", "MatText.Muted"));
        layout.AddTab("Übersicht", overview);

        var facts = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        var row = 0;
        foreach (var (label, value) in new[]
                 {
                     ("Art", item.Kind == StoreItemKind.Theme ? "Theme" : "Plugin"),
                     ("Version", item.Version),
                     ("Installiert", listing.InstalledVersion ?? "nein"),
                     ("Autor", item.Author),
                     ("Systeme", item.Platforms.Count == 0 ? "Windows und macOS" : string.Join(", ", item.Platforms)),
                     ("Aktualisiert", item.UpdatedAt == default ? "" : Ui.FormatDateTime(item.UpdatedAt)),
                     ("Prüfsumme (SHA-256)", item.Sha256),
                     ("Webseite", item.Homepage),
                 })
        {
            if (value.Length == 0)
            {
                continue;
            }

            facts.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = Ui.Text(label, "MatText.Muted", wrap: false);
            name.Margin = new Thickness(0, 4, 24, 4);
            var text = new SelectableTextBlock { Text = value, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(0, 4) };
            Grid.SetRow(name, row);
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 1);
            facts.Children.Add(name);
            facts.Children.Add(text);
            row++;
        }

        layout.AddTab("Details", new Border { Child = facts }.Themed("MatCard"));
        var install = StorePage.InstallButton(listing, window.GoBack, message => layout.Message.Show(message, InfoSeverity.Error));
        if (install is Button)
        {
            layout.Actions.Children.Add(install);
        }

        layout.AddAction("back", "Zurück", window.GoBack);
        if (listing.IsInstalled)
        {
            layout.AddAction("trash", "Entfernen", async () =>
            {
                if (!await Dialogs.ConfirmDialog.AskAsync(Ui.WindowOf(this), $"{item.Name} entfernen?", "Es wird von diesem Computer entfernt – mit der Cloud auch von deinen anderen.", "Entfernen", isDestructive: true))
                {
                    return;
                }

                try
                {
                    StoreService.Uninstall(item.Id);
                    window.GoBack();
                }
                catch (Exception exception) when (exception is BusinessException or IOException)
                {
                    layout.Message.Show(exception.Message, InfoSeverity.Error);
                }
            }, "MatButton.Danger", separated: true);
        }

        Content = Ui.Page(layout, 820);
    }
}
