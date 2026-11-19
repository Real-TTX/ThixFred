using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using MatDesktop;
using ThixFred.Controls;
using ThixFred.Core.Business;
using ThixFred.Core.Infrastructure;
using ThixFred.Core.Models;

namespace ThixFred.Pages;

/// <summary>
/// Plugins and scripts: the built-in providers, the plugins of the store and of the plugins folder, the user's scripts. The
/// toolbar searches, filters (source, switched off) and sorts; tiles (default) or rows; a switch turns a plugin on or off; a
/// click opens its page (a script: the script editor). The last tiles add: "Hinzufügen" (the store, its plugins) and "Eigenes
/// Skript". Below: "Hinzufügen", "Neues Skript", "Plugin-Ordner öffnen", "Neu einlesen".
/// </summary>
public sealed class PluginsPage : UserControl, IRefreshable, ISearchable
{
    private static readonly string[] Filters = ["Alle", "Eingebaut", "Aus dem Store", "Eigene Skripte", "Aus dem Plugin-Ordner", "Ausgeschaltet"];
    private static readonly string[] Sortings = ["Name", "Schlüsselwort", "Herkunft"];

    private readonly MainWindow _window;
    private readonly ListLayout _layout;

    public PluginsPage(MainWindow window)
    {
        _window = window;
        var toolbar = new ListToolbar("Plugins durchsuchen", Filters, Sortings);
        var list = new EntityList("Kein Plugin passt zur Suche.");
        list.AddTile("plus", "Hinzufügen", "Plugins aus dem Store – geprüft und mit einem Klick installiert", () => _window.ShowStore(StoreItemKind.Plugin));
        list.AddTile("code", "Eigenes Skript", "PowerShell, Python, Shell … mit eigenem Schlüsselwort", NewScript);
        _layout = new ListLayout("Plugins", "Was der Launcher kann: eingebaute Funktionen, Plugins aus dem Store und deine eigenen Skripte. Ein Plugin antwortet auf sein Schlüsselwort, z. B. „uuid“.", toolbar, list, "plugins");
        _layout.AddActions(
            Ui.Button("plus", "Hinzufügen", "MatButton.Primary", () => _window.ShowStore(StoreItemKind.Plugin)),
            Ui.Button("code", "Neues Skript", click: NewScript),
            Ui.Button("folder-open", "Plugin-Ordner öffnen", click: OpenFolder),
            Ui.Button("refresh", "Neu einlesen", click: () =>
            {
                PluginService.Invalidate();
                Refresh();
            }));
        toolbar.Changed += (_, _) => Refresh();
        list.Opened += (_, id) => Open(id);
        Content = Ui.Page(_layout, double.PositiveInfinity);
        PluginService.Changed += (_, _) => Ui.Post(Refresh);
        Refresh();
    }

    public EntityList List => _layout.List;

    public ListToolbar Toolbar => _layout.Toolbar;

    public void FocusSearch() => _layout.Toolbar.SearchBox.Focus();

    public void Refresh()
    {
        var all = PluginService.List();
        var toolbar = _layout.Toolbar;
        var shown = all.Where(plugin => toolbar.FilterIndex switch
            {
                1 => plugin.Source == PluginSource.BuiltIn,
                2 => plugin.Source == PluginSource.Store,
                3 => plugin.Source == PluginSource.Script,
                4 => plugin.Source == PluginSource.Folder,
                5 => !plugin.IsEnabled,
                _ => true,
            })
            .Where(plugin => toolbar.Matches(plugin.Name, plugin.Keyword, plugin.Description, plugin.Author));
        shown = toolbar.SortIndex switch
        {
            1 => shown.OrderBy(plugin => plugin.Keyword.Length == 0).ThenBy(plugin => plugin.Keyword, StringComparer.CurrentCultureIgnoreCase),
            2 => shown.OrderBy(plugin => plugin.Source).ThenBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => shown.OrderBy(plugin => plugin.Name, StringComparer.CurrentCultureIgnoreCase),
        };
        _layout.List.Show(shown.Select(Row).ToList(), all.Count);
        if (AccessPolicy.AreScriptsDisabled)
        {
            _layout.Message.Show("Von deiner Organisation festgelegt: Plugins und Skripte laufen auf diesem Computer nicht (Richtlinie DisableScripts). Die eingebauten Funktionen bleiben.", InfoSeverity.Warning);
        }
    }

    private EntityRow Row(PluginInfo plugin)
    {
        var badges = new List<string> { SourceName(plugin.Source) };
        if (plugin.Keyword.Length > 0)
        {
            badges.Insert(0, plugin.Keyword);
        }

        if (!plugin.IsSupported)
        {
            badges.Add("auf diesem System nicht verfügbar");
        }

        return new EntityRow(plugin.Id, plugin.Icon, plugin.Name, plugin.Description)
        {
            Badges = badges,
            IsMuted = !plugin.IsEnabled,
            Trailing = () =>
            {
                var toggle = Ui.Switch($"{plugin.Name} einschalten", plugin.IsEnabled, enabled => Apply(() => PluginService.SetEnabled(plugin.Id, enabled)));
                toggle.IsEnabled = plugin.IsSupported && !AccessPolicy.IsConfigurationLocked && (plugin.Source == PluginSource.BuiltIn || !AccessPolicy.AreScriptsDisabled);
                ToolTip.SetTip(toggle, plugin.IsEnabled ? "Ausschalten" : "Einschalten");
                return toggle;
            },
        };
    }

    internal static string SourceName(PluginSource source) => source switch
    {
        PluginSource.BuiltIn => "Eingebaut",
        PluginSource.Store => "Store",
        PluginSource.Script => "Skript",
        _ => "Plugin-Ordner",
    };

    private void Open(string id)
    {
        if (id.StartsWith("script:", StringComparison.Ordinal) && ScriptService.Find(id["script:".Length..]) is { } script)
        {
            _window.OpenPage(new ScriptEditPage(_window, script));
        }
        else if (PluginService.Find(id) is { } plugin)
        {
            _window.OpenPage(new PluginEditPage(_window, plugin));
        }
    }

    private void NewScript()
    {
        if (AccessPolicy.AreScriptsDisabled)
        {
            _layout.Message.Show("Skripte wurden von einem Administrator deaktiviert (Richtlinie DisableScripts).", InfoSeverity.Warning);
            return;
        }

        _window.OpenPage(new ScriptEditPage(_window, ScriptService.Template(ScriptService.Languages[0])));
    }

    private static void OpenFolder()
    {
        Directory.CreateDirectory(AppPaths.PluginsDirectory);
        Ui.OpenExternal(AppPaths.PluginsDirectory);
    }

    private void Apply(Action change)
    {
        try
        {
            _layout.Message.Hide();
            change();
        }
        catch (BusinessException exception)
        {
            _layout.Message.Show(exception.Message, InfoSeverity.Error);
            Refresh();
        }
    }
}

/// <summary>The page of a built-in, store or folder plugin: on/off and keyword (tab Allgemein), what it is (tab Info).</summary>
public sealed class PluginEditPage : UserControl
{
    private readonly MainWindow _window;
    private readonly PluginInfo _plugin;
    private readonly EditLayout _layout;
    private readonly ToggleSwitch _enabled;
    private readonly TextBox _keyword;

    public PluginEditPage(MainWindow window, PluginInfo plugin)
    {
        _window = window;
        _plugin = plugin;
        _layout = new EditLayout(plugin.Name, plugin.Description);
        _enabled = Ui.Switch("Eingeschaltet", plugin.IsEnabled);
        _enabled.IsEnabled = plugin.IsSupported && !AccessPolicy.IsConfigurationLocked;
        _keyword = new TextBox { Text = plugin.Keyword, Watermark = plugin.DefaultKeyword.Length > 0 ? plugin.DefaultKeyword : "ohne Schlüsselwort", MaxWidth = 240, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        AutomationProperties.SetName(_keyword, "Schlüsselwort");
        var keywordHint = plugin.Id switch
        {
            PluginService.TerminalId => "Steht vor dem Befehl: „>ping heise.de“.",
            PluginService.RunningId => "Steht vor dem Programm: „kill word“. Auch „beenden“ und „quit“ gehen immer.",
            _ when plugin.Source == PluginSource.BuiltIn => "Eingebaute Funktionen antworten auf jede Suche; ein Schlüsselwort brauchen sie nicht.",
            _ => $"Steht vor dem, was das Plugin bekommt. Leer: „{plugin.DefaultKeyword}“, das Schlüsselwort des Plugins.",
        };

        var general = new StackPanel();
        general.Children.Add(Ui.Row("Eingeschaltet", plugin.IsSupported ? "Ausgeschaltet antwortet das Plugin nicht im Launcher." : "Auf diesem System fehlt das Programm, das sein Skript ausführt.", _enabled));
        if (plugin.Source != PluginSource.BuiltIn || plugin.Id is PluginService.TerminalId or PluginService.RunningId)
        {
            general.Children.Add(Ui.Field("Schlüsselwort", _keyword, nameof(PluginPreference.Keyword), keywordHint));
        }

        _layout.AddTab("Allgemein", general);

        var facts = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), MaxWidth = 640, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        void Fact(string label, string value)
        {
            if (value.Length == 0)
            {
                return;
            }

            var row = facts.RowDefinitions.Count;
            facts.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = Ui.Text(label, "MatText.Muted");
            name.Margin = new Thickness(0, 4, 24, 4);
            var text = new SelectableTextBlock { Text = value, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Thickness(0, 4) };
            Grid.SetRow(name, row);
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 1);
            facts.Children.Add(name);
            facts.Children.Add(text);
        }

        Fact("Herkunft", PluginsPage.SourceName(plugin.Source));
        Fact("Version", plugin.Version);
        Fact("Autor", plugin.Author);
        Fact("Art", plugin.Source == PluginSource.BuiltIn ? "Eingebaut" : plugin.Mode == PluginMode.Filter ? "Filter: läuft beim Tippen und liefert die Ergebnisse" : "Ausführen: läuft mit Enter");
        Fact("Skript", plugin.ScriptPath ?? "");
        Fact("Ordner", plugin.Source is PluginSource.Store or PluginSource.Folder ? plugin.Folder : "");
        Fact("Webseite", plugin.Homepage);
        _layout.AddTab("Info", new Border { Child = facts }.Themed("MatCard"));

        _layout.AddAction("check", "Speichern", Save, "MatButton.Primary").IsEnabled = !AccessPolicy.IsConfigurationLocked;
        _layout.AddAction("back", "Zurück", window.GoBack);
        if (plugin.Source is PluginSource.Store or PluginSource.Folder)
        {
            _layout.AddAction("folder-open", "Ordner öffnen", () => Ui.OpenExternal(plugin.Folder));
            _layout.AddAction("trash", "Entfernen", () => _ = RemoveAsync(), "MatButton.Danger", separated: true).IsEnabled = !AccessPolicy.IsConfigurationLocked;
        }

        Content = Ui.Page(_layout, 820);
    }

    private void Save()
    {
        try
        {
            _layout.ClearErrors();
            PluginService.SetKeyword(_plugin.Id, _keyword.Text ?? "");
            PluginService.SetEnabled(_plugin.Id, _enabled.IsChecked == true);
            _window.GoBack();
        }
        catch (BusinessException exception)
        {
            _layout.ShowError(exception);
        }
    }

    private async Task RemoveAsync()
    {
        if (!await Dialogs.ConfirmDialog.AskAsync(Ui.WindowOf(this), $"{_plugin.Name} entfernen?",
                "Das Plugin wird von diesem Computer gelöscht. Aus dem Store lässt es sich jederzeit wieder installieren.", "Entfernen", isDestructive: true))
        {
            return;
        }

        try
        {
            PluginService.Remove(_plugin.Id);
            _window.GoBack();
        }
        catch (Exception exception) when (exception is BusinessException or IOException or UnauthorizedAccessException)
        {
            _layout.Message.Show(exception.Message, InfoSeverity.Error);
        }
    }
}
