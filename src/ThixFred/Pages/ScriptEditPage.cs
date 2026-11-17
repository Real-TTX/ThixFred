using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixFred.Controls;
using ThixFred.Core.Business;
using ThixFred.Core.Models;

namespace ThixFred.Pages;

/// <summary>
/// Create or change one of the user's scripts (a plugin that is one script). Tabs: Allgemein (name, keyword, language, how it
/// answers, where its output goes – only for "Ausführen", a filter shows its results itself), Skript (the code), Testen (run it
/// with a text and see what it prints). Below: Speichern, Zurück … Löschen.
/// </summary>
public sealed class ScriptEditPage : UserControl
{
    private static readonly PluginMode[] Modes = [PluginMode.Run, PluginMode.Filter];
    private static readonly ScriptOutput[] Outputs = [ScriptOutput.Launcher, ScriptOutput.Clipboard, ScriptOutput.Terminal, ScriptOutput.None];

    private readonly MainWindow _window;
    private readonly UserScript _script;
    private readonly bool _isNew;
    private readonly EditLayout _layout;
    private readonly TextBox _name = new();
    private readonly TextBox _keyword = new() { MaxWidth = 240, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBox _description = new();
    private readonly ComboBox _language = new() { MinWidth = 220 };
    private readonly ComboBox _mode = new() { MinWidth = 320 };
    private readonly ComboBox _output = new() { MinWidth = 320 };
    private readonly MatFormField _outputField;
    private readonly TextBox _body;
    private readonly TextBox _testInput = new() { Watermark = "Was nach dem Schlüsselwort getippt wird" };
    private readonly SelectableTextBlock _testOutput = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Border _testResult;

    public ScriptEditPage(MainWindow window, UserScript script)
    {
        _window = window;
        _script = script;
        _isNew = script.Id.Length == 0;
        _layout = new EditLayout(_isNew ? "Neues Skript" : script.Name,
            "Ein Skript ist ein Plugin: Es läuft mit dem, was nach seinem Schlüsselwort getippt wird (als erstes Argument und in THIXFRED_QUERY).");

        _name.Text = script.Name;
        _keyword.Text = script.Keyword;
        _description.Text = script.Description;
        _language.ItemsSource = ScriptService.Languages.Select(ScriptService.DisplayName).ToList();
        _language.SelectedIndex = Math.Max(0, ScriptService.Languages.ToList().IndexOf(script.Language));
        _mode.ItemsSource = new[] { "Ausführen – Enter startet das Skript", "Filter – liefert beim Tippen die Ergebnisse" };
        _mode.SelectedIndex = Array.IndexOf(Modes, script.Mode);
        _output.ItemsSource = new[] { "Im Launcher zeigen", "In die Zwischenablage", "In einem Terminal ausführen", "Nichts (nur ausführen)" };
        _output.SelectedIndex = Math.Max(0, Array.IndexOf(Outputs, script.Output));
        foreach (var (control, name) in new (Control, string)[] { (_name, "Name"), (_keyword, "Schlüsselwort"), (_description, "Beschreibung"), (_language, "Sprache"), (_mode, "Art"), (_output, "Ausgabe") })
        {
            AutomationProperties.SetName(control, name);
        }

        _outputField = Ui.Field("Ausgabe", _output, nameof(UserScript.Output));
        var general = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        general.Children.Add(Ui.Field("Name", _name, nameof(UserScript.Name)));
        general.Children.Add(Ui.Field("Schlüsselwort", _keyword, nameof(UserScript.Keyword), "Ein Wort, das du im Launcher zuerst tippst, z. B. „ip“."));
        general.Children.Add(Ui.Field("Sprache", _language, nameof(UserScript.Language)));
        general.Children.Add(Ui.Field("Art", _mode, nameof(UserScript.Mode),
            "Ein Filter gibt JSON aus ({\"items\":[{\"title\",\"subtitle\",\"arg\",\"action\"}]}) oder eine Zeile je Ergebnis."));
        general.Children.Add(_outputField);
        general.Children.Add(Ui.Field("Beschreibung", _description, nameof(UserScript.Description), "Eine Zeile, sie steht im Launcher unter dem Namen."));
        _layout.AddTab("Allgemein", general);

        _body = new TextBox
        {
            Text = script.Body,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            MinHeight = 340,
            MaxHeight = 520,
        };
        _body.Use(TextBox.FontFamilyProperty, "Mat.Mono");
        AutomationProperties.SetName(_body, "Skript");
        _layout.AddTab("Skript", Ui.Field("Skript", _body, nameof(UserScript.Body), "Der Kopf mit Name, Schlüsselwort und Art wird beim Speichern davor geschrieben."));

        _testResult = new Border { Child = _testOutput, IsVisible = false, Margin = new Thickness(0, 12, 0, 0) }.Themed("MatCard");
        _testOutput.Use(SelectableTextBlock.FontFamilyProperty, "Mat.Mono");
        var test = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        test.Children.Add(Ui.Field("Eingabe", _testInput, hint: "Das gespeicherte Skript läuft mit diesem Text – erst speichern, dann testen."));
        test.Children.Add(new ActionBar { Children = { Ui.Button("play", "Testen", click: () => _ = TestAsync()) } });
        test.Children.Add(_testResult);
        _layout.AddTab("Testen", test);

        _mode.SelectionChanged += (_, _) => _outputField.IsVisible = Modes[Math.Max(0, _mode.SelectedIndex)] == PluginMode.Run;
        _outputField.IsVisible = script.Mode == PluginMode.Run;
        _language.SelectionChanged += (_, _) =>
        {
            // a new script follows the language with its example, a written one keeps its code
            if (_isNew && (_body.Text ?? "") == ScriptService.Template(script.Language).Body || string.IsNullOrWhiteSpace(_body.Text))
            {
                _body.Text = ScriptService.Template(SelectedLanguage).Body;
            }
        };

        _layout.AddAction("check", "Speichern", () => Save(), "MatButton.Primary");
        _layout.AddAction("back", "Zurück", window.GoBack);
        if (!_isNew)
        {
            _layout.AddAction("folder-open", "Im Ordner zeigen", () =>
            {
                if (ScriptService.FileOf(_script.Id) is { } file)
                {
                    Core.Platform.Shell.Reveal(file);
                }
            });
            _layout.AddAction("trash", "Löschen", () => _ = DeleteAsync(), "MatButton.Danger", separated: true);
        }

        Content = Ui.Page(_layout, 860);
    }

    private string? _savedId;

    public EditLayout Layout => _layout;

    /// <summary>The id of the script (after the first save of a new one: its new id).</summary>
    public string CurrentId => _savedId ?? _script.Id;

    public TextBox NameBox => _name;

    public TextBox KeywordBox => _keyword;

    public TextBox BodyBox => _body;

    private ScriptLanguage SelectedLanguage => ScriptService.Languages[Math.Max(0, _language.SelectedIndex)];

    /// <summary>Saves; true when it worked (the page goes back unless it is a test run).</summary>
    public bool Save(bool goBack = true)
    {
        try
        {
            _layout.ClearErrors();
            var saved = ScriptService.Save(_script with
            {
                Id = CurrentId,
                Name = _name.Text ?? "",
                Keyword = _keyword.Text ?? "",
                Description = _description.Text ?? "",
                Language = SelectedLanguage,
                Mode = Modes[Math.Max(0, _mode.SelectedIndex)],
                Output = Outputs[Math.Max(0, _output.SelectedIndex)],
                Body = _body.Text ?? "",
            });
            _savedId = saved.Id;
            if (goBack)
            {
                _window.GoBack();
            }

            return true;
        }
        catch (BusinessException exception)
        {
            _layout.ShowError(exception);
            return false;
        }
    }

    private async Task TestAsync()
    {
        if (!Save(goBack: false))
        {
            return;
        }

        _testResult.IsVisible = true;
        _testOutput.Text = "Läuft …";
        try
        {
            var id = $"script:{CurrentId}";
            if (Modes[Math.Max(0, _mode.SelectedIndex)] == PluginMode.Filter)
            {
                var results = await PluginService.FilterAsync(id, _testInput.Text ?? "", CancellationToken.None);
                _testOutput.Text = results.Count == 0 ? "(keine Ergebnisse)" : string.Join("\n", results.Select(result => $"• {result.Title}{(result.Subtitle.Length > 0 ? " – " + result.Subtitle : "")}  [{result.Action.Label}]"));
            }
            else
            {
                var outcome = await PluginService.RunAsync(id, _testInput.Text ?? "", isAction: true);
                _testOutput.Text = outcome.Message ?? "Fertig (ohne Ausgabe im Launcher).";
            }
        }
        catch (BusinessException exception)
        {
            _testOutput.Text = exception.Message;
        }
    }

    private async Task DeleteAsync()
    {
        if (!await Dialogs.ConfirmDialog.AskAsync(Ui.WindowOf(this), $"„{_script.Name}“ löschen?",
                "Das Skript wird gelöscht – mit der Cloud auf all deinen Computern.", "Löschen", isDestructive: true))
        {
            return;
        }

        try
        {
            ScriptService.Delete(CurrentId);
            _window.GoBack();
        }
        catch (Exception exception) when (exception is BusinessException or IOException)
        {
            _layout.Message.Show(exception.Message, InfoSeverity.Error);
        }
    }
}
