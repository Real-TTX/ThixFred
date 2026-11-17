using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MatDesktop;
using ThixFred.Core.Models;

namespace ThixFred.Launcher;

/// <summary>
/// What the launcher shows, in the colours and the form of a <see cref="LauncherTheme"/> (the user's theme: the only place of
/// ThixFred where colours come from data and not from MatDesktop's tokens): the search line, the results, a message or a
/// question, and a line of hints at the bottom. The rows are made once and reused (no list control, no templates): a search
/// on every key only changes texts. Used by the launcher window, the preview of the theme editor and the snapshots.
/// </summary>
public sealed class LauncherView : Border
{
    public const int MaxRows = 20;

    private readonly TextBox _search;
    private readonly MatIcon _searchIcon;
    private readonly TextBlock _keyHint;
    private readonly StackPanel _rows = new();
    private readonly List<ResultRow> _rowPool = [];
    private readonly Border _separator;
    private readonly Border _messagePanel;
    private readonly SelectableTextBlock _messageText;
    private readonly MatIcon _messageIcon;
    private readonly TextBlock _messageHint;
    private readonly TextBlock _footer;
    private readonly TextBlock _footerRight;
    private readonly Border _footerBar;
    private IReadOnlyList<SearchResult> _results = [];
    private LauncherTheme _theme = Core.Business.ThemeService.BuiltIn[0];

    public LauncherView()
    {
        ClipToBounds = true;
        _searchIcon = new MatIcon { Icon = "search", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 12, 0) };
        _search = new TextBox
        {
            Watermark = "ThixFred – Programme, Rechnen, >Befehle …",
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            MinHeight = 0,
        };
        AutomationProperties.SetName(_search, "Suche");
        _keyHint = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0), FontSize = 12 };

        var searchLine = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        searchLine.Children.Add(_searchIcon);
        Grid.SetColumn(_search, 1);
        searchLine.Children.Add(_search);
        Grid.SetColumn(_keyHint, 2);
        searchLine.Children.Add(_keyHint);

        _separator = new Border { Height = 1, Margin = new Thickness(0, 8, 0, 6), IsVisible = false };

        _messageIcon = new MatIcon { Icon = "info", Size = 18, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4, 2, 12, 0) };
        _messageText = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, monospace") };
        _messageHint = new TextBlock { Margin = new Thickness(0, 10, 0, 0), FontSize = 12 };
        var messageBody = new StackPanel { Children = { new ScrollViewer { MaxHeight = 300, Content = _messageText }, _messageHint } };
        var messageGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        messageGrid.Children.Add(_messageIcon);
        Grid.SetColumn(messageBody, 1);
        messageGrid.Children.Add(messageBody);
        _messagePanel = new Border { Padding = new Thickness(10, 8), Child = messageGrid, IsVisible = false };

        _footer = new TextBlock { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        _footerRight = new TextBlock { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Text = "ThixFred" };
        var footerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        footerGrid.Children.Add(_footer);
        Grid.SetColumn(_footerRight, 1);
        footerGrid.Children.Add(_footerRight);
        _footerBar = new Border { Child = footerGrid, Padding = new Thickness(12, 7), Margin = new Thickness(0, 6, 0, 0), IsVisible = false };

        Child = new StackPanel { Children = { new Border { Padding = new Thickness(8, 6), Child = searchLine }, _separator, _rows, _messagePanel, _footerBar } };
        ApplyTheme(_theme);
    }

    /// <summary>A row was clicked (its index).</summary>
    public event EventHandler<int>? RowClicked;

    /// <summary>The mouse rests on a row: it becomes the chosen one.</summary>
    public event EventHandler<int>? RowHovered;

    public TextBox SearchBox => _search;

    public LauncherTheme LauncherTheme => _theme;

    public IReadOnlyList<SearchResult> Results => _results;

    public int SelectedIndex { get; private set; } = -1;

    public SearchResult? Selected => SelectedIndex >= 0 && SelectedIndex < _results.Count ? _results[SelectedIndex] : null;

    /// <summary>True while a message or a question is shown instead of the results.</summary>
    public bool IsShowingMessage => _messagePanel.IsVisible;

    /// <summary>The text of the shown message (tests, Ctrl/Cmd+C).</summary>
    public string? MessageText => _messagePanel.IsVisible ? _messageText.Text : null;

    /// <summary>The key hint at the right of the search line ("Alt + Leertaste").</summary>
    public string KeyHint
    {
        get => _keyHint.Text ?? "";
        set => _keyHint.Text = value;
    }

    /// <summary>The colours, the corners, the sizes of the theme.</summary>
    public void ApplyTheme(LauncherTheme theme)
    {
        _theme = theme;
        var text = Brush(theme.Text);
        var muted = Brush(theme.MutedText);
        var accent = Brush(theme.Accent);
        Background = Brush(theme.Background);
        BorderBrush = Brush(theme.Border);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(theme.CornerRadius);
        Padding = new Thickness(theme.Padding);
        Width = theme.Width;

        _search.FontSize = theme.FontSize;
        _search.Foreground = text;
        _search.CaretBrush = accent;
        _search.SelectionBrush = Brush(theme.Selection);
        _search.SelectionForegroundBrush = text;
        // Fluent paints the field on hover and focus: here it stays the window
        foreach (var key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "TextControlBackgroundDisabled",
                     "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" })
        {
            _search.Resources[key] = Brushes.Transparent;
        }

        foreach (var key in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused" })
        {
            _search.Resources[key] = text;
        }

        foreach (var key in new[] { "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver", "TextControlPlaceholderForegroundFocused" })
        {
            _search.Resources[key] = muted;
        }

        _searchIcon.Size = Math.Round(theme.FontSize * 0.85);
        _searchIcon.Foreground = muted;
        _keyHint.Foreground = muted;
        _separator.Background = Brush(theme.Border);
        _messageText.Foreground = text;
        _messageHint.Foreground = muted;
        _footer.Foreground = muted;
        _footerRight.Foreground = muted;
        _footerBar.BorderBrush = Brush(theme.Border);
        _footerBar.BorderThickness = new Thickness(0, 1, 0, 0);
        foreach (var row in _rowPool)
        {
            row.Apply(theme);
        }

        Refresh();
    }

    /// <summary>Shows results; the chosen one is <paramref name="selected"/> (clamped).</summary>
    public void ShowResults(IReadOnlyList<SearchResult> results, int selected = 0)
    {
        _results = results.Take(MaxRows).ToList();
        HideMessage();
        while (_rowPool.Count < _results.Count)
        {
            var row = new ResultRow(_rowPool.Count);
            row.Apply(_theme);
            row.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
                {
                    e.Handled = true;
                    RowClicked?.Invoke(this, row.Index);
                }
            };
            row.PointerMoved += (_, _) =>
            {
                if (SelectedIndex != row.Index)
                {
                    RowHovered?.Invoke(this, row.Index);
                }
            };
            _rowPool.Add(row);
            _rows.Children.Add(row);
        }

        for (var index = 0; index < _rowPool.Count; index++)
        {
            var row = _rowPool[index];
            row.IsVisible = index < _results.Count;
            if (index < _results.Count)
            {
                row.Show(_results[index], ShortcutFor(index));
            }
        }

        SelectedIndex = _results.Count == 0 ? -1 : Math.Clamp(selected, 0, _results.Count - 1);
        Refresh();
    }

    public void Select(int index)
    {
        if (_results.Count == 0)
        {
            return;
        }

        SelectedIndex = Math.Clamp(index, 0, _results.Count - 1);
        Refresh();
    }

    /// <summary>A message instead of the results (the output of a script, an error); the hint says what the keys do now.</summary>
    public void ShowMessage(string message, bool isError, string hint)
    {
        _messageText.Text = message;
        _messageIcon.Icon = isError ? "alert" : "info";
        _messageIcon.Foreground = Brush(_theme.Accent);
        _messageHint.Text = hint;
        _messagePanel.IsVisible = true;
        _rows.IsVisible = false;
        Refresh();
    }

    public void HideMessage()
    {
        _messagePanel.IsVisible = false;
        _rows.IsVisible = true;
    }

    private void Refresh()
    {
        var hasRows = _rows.IsVisible && _results.Count > 0;
        _separator.IsVisible = hasRows || _messagePanel.IsVisible;
        for (var index = 0; index < _rowPool.Count; index++)
        {
            _rowPool[index].SetSelected(index == SelectedIndex);
        }

        _footerBar.IsVisible = hasRows || _messagePanel.IsVisible;
        if (_messagePanel.IsVisible)
        {
            _footer.Text = "Esc zurück · " + (OperatingSystem.IsMacOS() ? "⌘C" : "Strg+C") + " kopiert";
        }
        else if (Selected is { } selected)
        {
            var parts = new List<string> { $"↵ {selected.Action.Label}" };
            if (selected.AlternateAction is { } alternate)
            {
                parts.Add($"⇧↵ {alternate.Label}");
            }

            if (selected.Autocomplete is not null)
            {
                parts.Add("⇥ Vervollständigen");
            }

            _footer.Text = string.Join("   ", parts);
        }
    }

    private static string? ShortcutFor(int index) => index < 9 ? (OperatingSystem.IsMacOS() ? $"⌘{index + 1}" : $"Strg+{index + 1}") : null;

    internal static IBrush Brush(string color) => Color.TryParse(color, out var parsed) ? new SolidColorBrush(parsed) : Brushes.Transparent;

    /// <summary>One result: the icon, the title, the subtitle and its shortcut (Ctrl/Cmd+1 … 9).</summary>
    private sealed class ResultRow : Border
    {
        private readonly Panel _iconHost = new() { Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0) };
        private readonly Image _image = new() { Width = 32, Height = 32 };
        private readonly MatIcon _glyph = new() { Size = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        private readonly Border _glyphTile = new() { Width = 32, Height = 32, CornerRadius = new CornerRadius(8) };
        private readonly TextBlock _title = new() { FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _subtitle = new() { TextTrimming = TextTrimming.CharacterEllipsis };
        private readonly TextBlock _shortcut = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) };
        private IBrush _selection = Brushes.Transparent;
        private string? _iconSource;

        public ResultRow(int index)
        {
            Index = index;
            Padding = new Thickness(6, 6);
            Cursor = new Cursor(StandardCursorType.Hand);
            _glyphTile.Child = _glyph;
            _iconHost.Children.Add(_glyphTile);
            _iconHost.Children.Add(_image);
            RenderOptions.SetBitmapInterpolationMode(_image, BitmapInterpolationMode.HighQuality);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1, Children = { _title, _subtitle } };
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
            grid.Children.Add(_iconHost);
            Grid.SetColumn(texts, 1);
            grid.Children.Add(texts);
            Grid.SetColumn(_shortcut, 2);
            grid.Children.Add(_shortcut);
            Child = grid;
        }

        public int Index { get; }

        public void Apply(LauncherTheme theme)
        {
            var scale = theme.FontSize / 24.0;
            _title.FontSize = Math.Round(15 * Math.Clamp(scale, 0.85, 1.25), 1);
            _subtitle.FontSize = Math.Round(12 * Math.Clamp(scale, 0.9, 1.2), 1);
            _shortcut.FontSize = 11.5;
            _title.Foreground = Brush(theme.Text);
            _subtitle.Foreground = Brush(theme.MutedText);
            _shortcut.Foreground = Brush(theme.MutedText);
            _glyph.Foreground = Brush(theme.Accent);
            _glyphTile.Background = Brush(theme.Selection);
            _selection = Brush(theme.Selection);
            CornerRadius = new CornerRadius(Math.Max(4, theme.CornerRadius - theme.Padding / 2));
            MinHeight = Math.Round(48 * Math.Clamp(scale, 0.8, 1.2));
        }

        public void Show(SearchResult result, string? shortcut)
        {
            _title.Text = result.Title;
            _subtitle.Text = result.Subtitle;
            _subtitle.IsVisible = result.Subtitle.Length > 0;
            _shortcut.Text = shortcut ?? "";
            _glyph.Icon = result.Icon.Glyph;
            AutomationProperties.SetName(this, result.Title);
            _iconSource = result.Icon.IconSource;
            var bitmap = _iconSource is { } source ? IconImages.Get(source, loaded =>
            {
                if (_iconSource == source)
                {
                    ShowBitmap(loaded);
                }
            }) : null;
            ShowBitmap(bitmap);
        }

        public void SetSelected(bool selected) => Background = selected ? _selection : Brushes.Transparent;

        private void ShowBitmap(Bitmap? bitmap)
        {
            _image.Source = bitmap;
            _image.IsVisible = bitmap is not null;
            _glyphTile.IsVisible = bitmap is null;
        }
    }
}
