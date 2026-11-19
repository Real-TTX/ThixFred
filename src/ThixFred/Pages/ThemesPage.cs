using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixFred.Controls;
using ThixFred.Core.Business;
using ThixFred.Core.Models;
using ThixFred.Launcher;

namespace ThixFred.Pages;

/// <summary>
/// The themes of the launcher: the built-in ones and the user's own (also from the store). A row shows the colours of the theme
/// and "Verwenden"; opening it shows the editor (built-in themes: to look at and to duplicate). "Wie das System" follows light
/// and dark of the system.
/// </summary>
public sealed class ThemesPage : UserControl, IRefreshable, ISearchable
{
    private static readonly string[] Filters = ["Alle", "Eingebaut", "Eigene", "Hell", "Dunkel"];
    private static readonly string[] Sortings = ["Name", "Zuletzt geändert"];

    private readonly MainWindow _window;
    private readonly ListLayout _layout;

    public ThemesPage(MainWindow window)
    {
        _window = window;
        var toolbar = new ListToolbar("Themes durchsuchen", Filters, Sortings);
        var list = new EntityList("Kein Theme passt zur Suche.");
        list.AddTile("plus", "Neues Theme", "Eigene Farben, Ecken, Schrift – mit Live-Vorschau", () => Open(ThemeService.LightId, asNew: true));
        list.AddTile("store", "Mehr im Store", "Weitere Themes zum Installieren", () => _window.ShowStore(StoreItemKind.Theme));
        _layout = new ListLayout("Themes", "So sieht der Launcher aus. Eigene Themes kommen mit der Cloud auf deine anderen Computer, weitere gibt es im Store.", toolbar, list, "themes");
        _layout.AddActions(
            Ui.Button("plus", "Neues Theme", "MatButton.Primary", () => Open(ThemeService.LightId, asNew: true)),
            Ui.Button("monitor", "Wie das System verwenden", click: () => Apply(() => ThemeService.Use(LauncherTheme.SystemId))),
            Ui.Button("store", "Themes im Store", click: () => _window.ShowStore(StoreItemKind.Theme)));
        toolbar.Changed += (_, _) => Refresh();
        list.Opened += (_, id) => Open(id, asNew: false);
        Content = Ui.Page(_layout, double.PositiveInfinity);
        ThemeService.Changed += (_, _) => Ui.Post(Refresh);
        Refresh();
    }

    public EntityList List => _layout.List;

    public void FocusSearch() => _layout.Toolbar.SearchBox.Focus();

    public void Refresh()
    {
        var chosen = Core.Business.SettingsService.Get().LauncherThemeId;
        var all = ThemeService.List();
        var toolbar = _layout.Toolbar;
        var shown = all.Where(theme => toolbar.FilterIndex switch
            {
                1 => theme.IsBuiltIn,
                2 => !theme.IsBuiltIn,
                3 => !theme.IsDark,
                4 => theme.IsDark,
                _ => true,
            })
            .Where(theme => toolbar.Matches(theme.Name, theme.Author, theme.Id));
        shown = toolbar.SortIndex == 1 ? shown.OrderByDescending(theme => theme.UpdatedAt) : shown.OrderBy(theme => theme.Name, StringComparer.CurrentCultureIgnoreCase);
        _layout.List.Show(shown.Select(theme => Row(theme, chosen)).ToList(), all.Count);
        _layout.Message.Show(chosen == LauncherTheme.SystemId
            ? "Der Launcher folgt dem System: ThixFred Hell oder ThixFred Dunkel."
            : $"Der Launcher verwendet „{ThemeService.Find(chosen)?.Name ?? chosen}“.", InfoSeverity.Info);
    }

    private EntityRow Row(LauncherTheme theme, string chosen)
    {
        var badges = new List<string> { theme.IsBuiltIn ? "Eingebaut" : "Eigenes", theme.IsDark ? "Dunkel" : "Hell" };
        if (theme.Id == chosen)
        {
            badges.Insert(0, "in Verwendung");
        }

        var subtitle = $"{theme.Backdrop switch { ThemeBackdrop.Acrylic => "Glas (Acryl)", ThemeBackdrop.Mica => "Glas (Mica)", _ when !theme.Shadow && theme.CornerRadius == 0 => "Flach und eckig", _ => "Deckend" }} · Schrift {theme.FontSize:0} px · Breite {theme.Width:0} px"
                       + (theme.Author.Length > 0 && !theme.IsBuiltIn ? $" · {theme.Author}" : "");
        return new EntityRow(theme.Id, "palette", theme.Name, subtitle)
        {
            Badges = badges,
            Leading = () => Swatch(theme),
            Banner = () => Preview(theme),
            Trailing = () =>
            {
                var use = Ui.Button("check", "Verwenden", click: () => Apply(() => ThemeService.Use(theme.Id)));
                use.IsEnabled = theme.Id != chosen;
                return use;
            },
        };
    }

    /// <summary>The launcher of a theme in small (the band of its tile): the search line, the chosen row, another row.</summary>
    internal static Control Preview(LauncherTheme theme)
    {
        var radius = Math.Min(theme.CornerRadius, 10);
        var selectionText = theme.SelectionText.Length > 0 ? theme.SelectionText : theme.Text;
        Border Bar(double width, string colour, double height = 5) => new()
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(radius == 0 ? 0 : height / 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Background = LauncherView.Brush(colour),
        };

        Border Glyph(string colour) => new()
        {
            Width = 10,
            Height = 10,
            Margin = new Thickness(0, 0, 7, 0),
            CornerRadius = new CornerRadius(Math.Min(radius, 3)),
            Background = LauncherView.Brush(colour),
        };

        var chosen = new Border
        {
            Height = 20,
            Padding = new Thickness(7, 0),
            CornerRadius = new CornerRadius(Math.Min(radius, 5)),
            Background = LauncherView.Brush(theme.Selection),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { Glyph(theme.SelectionText.Length > 0 ? theme.SelectionText : theme.Accent), Bar(70, selectionText) } },
        };
        var other = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(7, 0), Children = { Glyph(theme.MutedText), Bar(56, theme.MutedText) } };
        return new Border
        {
            Width = 180,
            Height = 76,
            Padding = new Thickness(8, 9),
            CornerRadius = new CornerRadius(radius),
            BorderThickness = new Thickness(1),
            Background = LauncherView.Brush(theme.Background.Length == 9 ? "#FF" + theme.Background[3..] : theme.Background),
            BorderBrush = LauncherView.Brush(theme.Border),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 0, 0, 2), Children = { Bar(2, theme.Accent, 12), new Border { Width = 6 }, Bar(84, theme.Text, 6) } },
                    chosen,
                    other,
                },
            },
        };
    }

    /// <summary>The colours of a theme as a small tile (background, a line of text, the accent).</summary>
    internal static Control Swatch(LauncherTheme theme) => new Border
    {
        Width = 36,
        Height = 36,
        CornerRadius = new CornerRadius(9),
        BorderThickness = new Thickness(1),
        Background = LauncherView.Brush(theme.Background.Length == 9 ? "#FF" + theme.Background[3..] : theme.Background),
        BorderBrush = LauncherView.Brush(theme.Border),
        Child = new StackPanel
        {
            Margin = new Thickness(7, 9),
            Spacing = 4,
            Children =
            {
                new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = LauncherView.Brush(theme.Text) },
                new Border { Height = 4, Width = 14, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2), Background = LauncherView.Brush(theme.Accent) },
            },
        },
    };

    private void Open(string id, bool asNew)
    {
        if (ThemeService.Find(id) is not { } theme)
        {
            return;
        }

        _window.OpenPage(new ThemeEditPage(_window, asNew ? theme with { Id = "", Name = "", IsBuiltIn = false, Author = "" } : theme));
    }

    private void Apply(Action change)
    {
        try
        {
            change();
            Refresh();
        }
        catch (BusinessException exception)
        {
            _layout.Message.Show(exception.Message, InfoSeverity.Error);
        }
    }
}

/// <summary>
/// The editor of a theme, with the launcher as a live preview at its side. Tabs: Farben (six colours) and Form (backdrop, corners,
/// type, width, inner space, dark). Built-in themes cannot be changed: Duplizieren makes an own copy.
/// Buttons: Speichern, Verwenden, Duplizieren, Zurück … Löschen.
/// </summary>
public sealed class ThemeEditPage : UserControl
{
    private static readonly ThemeBackdrop[] Backdrops = [ThemeBackdrop.Solid, ThemeBackdrop.Acrylic, ThemeBackdrop.Mica];

    private readonly MainWindow _window;
    private readonly LauncherTheme _theme;
    private readonly EditLayout _layout;
    private readonly LauncherView _preview = new() { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Dictionary<string, TextBox> _colors = [];
    private readonly TextBox _name = new();
    private readonly ComboBox _backdrop = new() { MinWidth = 260 };
    private readonly NumericUpDown _radius = Number(0, 32, 1);
    private readonly NumericUpDown _fontSize = Number(14, 40, 1);
    private readonly NumericUpDown _width = Number(480, 1200, 10);
    private readonly NumericUpDown _padding = Number(0, 32, 1);
    private readonly ToggleSwitch _dark;
    private readonly ToggleSwitch _shadow;

    public ThemeEditPage(MainWindow window, LauncherTheme theme)
    {
        _window = window;
        _theme = theme;
        var isNew = theme.Id.Length == 0;
        _layout = new EditLayout(isNew ? "Neues Theme" : theme.Name,
            theme.IsBuiltIn ? "Ein eingebautes Theme – zum Ändern „Duplizieren“." : "Farben sind #RRGGBB oder mit Deckkraft #AARRGGBB (AA: 00 durchsichtig … FF deckend).");

        _name.Text = theme.Name;
        AutomationProperties.SetName(_name, "Name");
        _backdrop.ItemsSource = new[] { "Deckend", "Glas (Acryl)", "Glas (Mica, nur Windows 11)" };
        _backdrop.SelectedIndex = Array.IndexOf(Backdrops, theme.Backdrop);
        _radius.Value = (decimal)theme.CornerRadius;
        _fontSize.Value = (decimal)theme.FontSize;
        _width.Value = (decimal)theme.Width;
        _padding.Value = (decimal)theme.Padding;
        _dark = Ui.Switch("Dunkles Theme", theme.IsDark, _ => Preview());
        _shadow = Ui.Switch("Schatten", theme.Shadow, _ => Preview());

        var colorGrid = new Avalonia.Controls.Primitives.UniformGrid { Columns = 2, MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
        var colors = new StackPanel { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
        if (!theme.IsBuiltIn)
        {
            colors.Children.Add(Ui.Field("Name", _name, nameof(LauncherTheme.Name)));
        }

        colors.Children.Add(colorGrid);

        foreach (var (field, label, value) in new[]
                 {
                     (nameof(LauncherTheme.Background), "Hintergrund", theme.Background),
                     (nameof(LauncherTheme.Text), "Text", theme.Text),
                     (nameof(LauncherTheme.MutedText), "Text, gedämpft", theme.MutedText),
                     (nameof(LauncherTheme.Accent), "Akzent", theme.Accent),
                     (nameof(LauncherTheme.Selection), "Auswahl", theme.Selection),
                     (nameof(LauncherTheme.Border), "Rand", theme.Border),
                     (nameof(LauncherTheme.SelectionText), "Text der Auswahl (leer: wie Text)", theme.SelectionText),
                 })
        {
            colorGrid.Children.Add(ColorField(field, label, value));
        }

        var form = new StackPanel { MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left };
        form.Children.Add(Ui.Field("Hintergrund des Fensters", _backdrop, nameof(LauncherTheme.Backdrop), "Glas lässt den Desktop verschwommen durchscheinen; der Hintergrund färbt es."));
        form.Children.Add(Ui.Field("Ecken (px)", _radius, nameof(LauncherTheme.CornerRadius)));
        form.Children.Add(Ui.Field("Schriftgröße der Suche (px)", _fontSize, nameof(LauncherTheme.FontSize)));
        form.Children.Add(Ui.Field("Breite (px)", _width, nameof(LauncherTheme.Width)));
        form.Children.Add(Ui.Field("Innenabstand (px)", _padding, nameof(LauncherTheme.Padding)));
        form.Children.Add(Ui.Row("Schatten", "Nur bei deckendem Hintergrund (Glas wirft den des Systems). Flache, eckige Themes wie „Kante“ haben keinen.", _shadow));
        form.Children.Add(Ui.Row("Dunkles Theme", "Bildlaufleisten und Tooltips dunkel.", _dark, last: true));

        _layout.AddTab("Farben", colors);
        _layout.AddTab("Form", form);
        foreach (var control in _colors.Values.Cast<Control>().Concat([_name, _backdrop, _radius, _fontSize, _width, _padding]))
        {
            control.IsEnabled = !theme.IsBuiltIn;
        }

        _dark.IsEnabled = !theme.IsBuiltIn;
        _shadow.IsEnabled = !theme.IsBuiltIn;
        _backdrop.SelectionChanged += (_, _) => Preview();
        foreach (var number in new[] { _radius, _fontSize, _width, _padding })
        {
            number.ValueChanged += (_, _) => Preview();
        }

        if (!theme.IsBuiltIn)
        {
            _layout.AddAction("check", "Speichern", () => Save(use: false), "MatButton.Primary");
        }

        _layout.AddAction("palette", theme.IsBuiltIn ? "Verwenden" : "Speichern und verwenden", () =>
        {
            if (theme.IsBuiltIn)
            {
                Run(() => ThemeService.Use(theme.Id));
            }
            else
            {
                Save(use: true);
            }
        }, theme.IsBuiltIn ? "MatButton.Primary" : null);
        if (!isNew)
        {
            _layout.AddAction("copy", "Duplizieren", () => Run(() => _window.OpenPage(new ThemeEditPage(_window, ThemeService.Duplicate(theme.Id)))));
        }

        _layout.AddAction("back", "Zurück", window.GoBack);
        if (!isNew && !theme.IsBuiltIn)
        {
            _layout.AddAction("trash", "Löschen", () => _ = DeleteAsync(), "MatButton.Danger", separated: true);
        }

        // the preview with sample results above the tabs: every change shows at once
        _preview.ShowResults(SampleResults());
        _preview.SearchBox.Text = "vis";
        _preview.KeyHint = HotkeyService.Display(SettingsService.Get().Hotkey);
        var previewHost = new Border
        {
            Padding = new Thickness(20),
            Margin = new Thickness(0, 14, 0, 0),
            CornerRadius = new CornerRadius(12),
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new LayoutTransformControl { LayoutTransform = new ScaleTransform(0.75, 0.75), Child = _preview },
        }.Use(Border.BackgroundProperty, "Mat.Bg2");
        AutomationProperties.SetName(previewHost, "Vorschau");
        _layout.Children.Insert(_layout.Children.IndexOf(_layout.Message), previewHost);
        Content = Ui.Page(_layout, 900);
        Preview();
    }

    public LauncherView PreviewView => _preview;

    /// <summary>What the editor holds now (not saved).</summary>
    private LauncherTheme Current => _theme with
    {
        Name = _name.Text ?? "",
        Background = _colors[nameof(LauncherTheme.Background)].Text ?? "",
        Text = _colors[nameof(LauncherTheme.Text)].Text ?? "",
        MutedText = _colors[nameof(LauncherTheme.MutedText)].Text ?? "",
        Accent = _colors[nameof(LauncherTheme.Accent)].Text ?? "",
        Selection = _colors[nameof(LauncherTheme.Selection)].Text ?? "",
        Border = _colors[nameof(LauncherTheme.Border)].Text ?? "",
        SelectionText = (_colors[nameof(LauncherTheme.SelectionText)].Text ?? "").Trim(),
        Shadow = _shadow.IsChecked == true,
        Backdrop = Backdrops[Math.Max(0, _backdrop.SelectedIndex)],
        CornerRadius = (double)(_radius.Value ?? 0),
        FontSize = (double)(_fontSize.Value ?? 24),
        Width = (double)(_width.Value ?? 720),
        Padding = (double)(_padding.Value ?? 10),
        IsDark = _dark.IsChecked == true,
    };

    private Control ColorField(string field, string label, string value)
    {
        var box = new TextBox { Text = value, MaxWidth = 200, HorizontalAlignment = HorizontalAlignment.Left };
        box.Use(TextBox.FontFamilyProperty, "Mat.Mono");
        AutomationProperties.SetName(box, label);
        var swatch = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Margin = new Thickness(10, 0, 0, 0) }
            .Use(Border.BorderBrushProperty, "Mat.Border");
        void Paint() => swatch.Background = ThemeService.IsColor(box.Text) ? LauncherView.Brush(box.Text!) : null;
        box.TextChanged += (_, _) =>
        {
            Paint();
            Preview();
        };
        Paint();
        _colors[field] = box;
        var formField = Ui.Field(label, new StackPanel { Orientation = Orientation.Horizontal, Children = { box, swatch } }, field);
        formField.Margin = new Thickness(0, 0, 24, 0);
        return formField;
    }

    private void Preview()
    {
        if (_colors.Count < 7)
        {
            return;
        }

        var current = Current;
        if (new[] { current.Background, current.Text, current.MutedText, current.Accent, current.Selection, current.Border }.All(ThemeService.IsColor) &&
            (current.SelectionText.Length == 0 || ThemeService.IsColor(current.SelectionText)))
        {
            // the glass is not shown in the preview: the background as the system would tint it, without the shadow
            _preview.ApplyTheme(current with { Background = current.Background.Length == 9 && current.Backdrop != ThemeBackdrop.Solid ? "#FF" + current.Background[3..] : current.Background });
            _preview.ShowResults(SampleResults());
        }
    }

    private void Save(bool use)
    {
        try
        {
            _layout.ClearErrors();
            var saved = ThemeService.Save(Current);
            if (use)
            {
                ThemeService.Use(saved.Id);
            }

            _window.GoBack();
        }
        catch (BusinessException exception)
        {
            _layout.ShowError(exception);
        }
    }

    private void Run(Action action)
    {
        try
        {
            action();
        }
        catch (BusinessException exception)
        {
            _layout.Message.Show(exception.Message, InfoSeverity.Error);
        }
    }

    private async Task DeleteAsync()
    {
        if (!await Dialogs.ConfirmDialog.AskAsync(Ui.WindowOf(this), $"„{_theme.Name}“ löschen?", "Das Theme wird gelöscht – mit der Cloud auf all deinen Computern.", "Löschen", isDestructive: true))
        {
            return;
        }

        Run(() =>
        {
            ThemeService.Delete(_theme.Id);
            _window.GoBack();
        });
    }

    private static NumericUpDown Number(double minimum, double maximum, double step) => new()
    {
        Minimum = (decimal)minimum,
        Maximum = (decimal)maximum,
        Increment = (decimal)step,
        FormatString = "0",
        MinWidth = 160,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    /// <summary>What the preview shows (and the snapshots): a search for "vis".</summary>
    internal static IReadOnlyList<SearchResult> SampleResults() =>
    [
        new SearchResult { Id = "a", Title = "Visual Studio Code", Subtitle = "C:\\Program Files\\Microsoft VS Code\\Code.exe", Kind = ResultKind.Application, Icon = ResultIcon.Of("code"), Action = new LaunchAction("Öffnen", LaunchActionKind.Open, "x"), AlternateAction = new LaunchAction("Als Administrator öffnen", LaunchActionKind.OpenAsAdministrator, "x") },
        new SearchResult { Id = "b", Title = "Visio", Subtitle = "Microsoft 365", Kind = ResultKind.Application, Icon = ResultIcon.Of("layers"), Action = new LaunchAction("Öffnen", LaunchActionKind.Open, "x") },
        new SearchResult { Id = "c", Title = "„vis“ mit Google suchen", Subtitle = "https://www.google.com/search?q=vis", Kind = ResultKind.WebSearch, Icon = ResultIcon.Of("search"), Action = new LaunchAction("Suchen", LaunchActionKind.Open, "x") },
    ];
}
