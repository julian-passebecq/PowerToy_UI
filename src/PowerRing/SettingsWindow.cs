using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using PowerRing.Core;

namespace PowerRing;

// "Réglages Power Ring": edits a working copy of ring.json with a live preview of the ring beside it (re-rendered ~50 ms
// after the last change). Nothing is written until Enregistrer (validated, previous file kept as ring.json.bak).
internal sealed class SettingsWindow : Window
{
    private static readonly string[] ThemeIds = ["system", "dark", "light", "fluent"], ThemeNames = ["Système", "Sombre", "Clair", "Fluent neutre"];
    private static readonly string[] Swatches = ["#0078D4", "#3B82F6", "#8B5CF6", "#EC4899", "#EF4444", "#F97316", "#EAB308", "#22C55E", "#14B8A6", "#64748B"];

    private sealed record SliderDef(string Label, double Min, double Max, double Step, Func<RingAppearance, double> Get, Action<RingAppearance, double> Set);

    private static readonly SliderDef[] Sliders =
    [
        new("Échelle", 0.5, 2.5, 0.05, a => a.Scale, (a, v) => a.Scale = v),
        new("Espacement", 0, 60, 1, a => a.Spacing, (a, v) => a.Spacing = v),
        new("Écart cercle 1 → 2", 0, 60, 1, a => a.SatelliteGap ?? Math.Round(a.Spacing * 0.7, 1), (a, v) => a.SatelliteGap = v),
        new("Boutons cercle 1", 28, 160, 1, a => a.SlotSize, (a, v) => a.SlotSize = v),
        new("Boutons cercle 2", 16, 100, 1, a => a.SatelliteSize, (a, v) => a.SatelliteSize = v),
        new("Boutons cercle 3", 12, 80, 1, a => a.ThirdSize, (a, v) => a.ThirdSize = v),
        new("Centre", 28, 200, 1, a => a.CenterSize, (a, v) => a.CenterSize = v),
        new("Boutons du bord", 16, 80, 1, a => a.WorkspaceButtonSize ?? RimDefault(a), (a, v) => a.WorkspaceButtonSize = v),
        new("Icône du bord", 6, 48, 1, a => a.RimIconSize ?? Math.Clamp(Math.Round((a.WorkspaceButtonSize ?? RimDefault(a)) * 0.5), 6, 48), (a, v) => a.RimIconSize = v),
        new("Icônes cercle 1", 10, 96, 1, a => a.IconSize, (a, v) => a.IconSize = v),
        new("Icônes cercle 2", 8, 48, 1, a => a.SatelliteIconSize, (a, v) => a.SatelliteIconSize = v),
        new("Icônes cercle 3", 6, 40, 1, a => a.ThirdIconSize, (a, v) => a.ThirdIconSize = v),
        new("Texte", 8, 32, 1, a => a.FontSize, (a, v) => a.FontSize = v),
        new("Opacité", 0.3, 1, 0.05, a => a.Opacity, (a, v) => a.Opacity = v),
        new("Animation (ms)", 0, 1000, 10, a => a.AnimationMs, (a, v) => a.AnimationMs = (int)v),
        new("Largeur tableau", 300, 1400, 10, a => a.BoardWidth, (a, v) => a.BoardWidth = v),
        new("Hauteur tableau", 200, 1000, 10, a => a.BoardHeight, (a, v) => a.BoardHeight = v),
        new("Boutons d'espace", 0, 4, 1, a => a.WorkspaceButtons, (a, v) => a.WorkspaceButtons = (int)v),
    ];

    private static readonly (string Label, Func<RingAppearance, bool> Get, Action<RingAppearance, bool> Set)[] Toggles =
    [
        ("Ombre", a => a.Shadow, (a, v) => a.Shadow = v),
        ("Numéros", a => a.ShowNumbers, (a, v) => a.ShowNumbers = v),
        ("Nom du bouton survolé", a => a.ShowLabels, (a, v) => a.ShowLabels = v),
        ("Cercle 2", a => a.ShowSatellites, (a, v) => a.ShowSatellites = v),
        ("Cercle 3", a => a.ShowThirdRing, (a, v) => a.ShowThirdRing = v),
        ("Icônes des sites web", a => a.WebIcons, (a, v) => a.WebIcons = v),
    ];

    private readonly RingConfigStore _store;
    private readonly Action _saved;
    private readonly RingWindow _preview;
    private readonly DispatcherTimer _debounce;
    private readonly StackPanel _body = new() { Margin = new Thickness(16, 8, 16, 8) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 6, 16, 0) };
    private RingConfig _copy;
    private string? _previewId, _layoutId;
    private int _profileSelected = -1, _itemSelected = -1;

    public SettingsWindow(RingConfig live, RingConfigStore store, IBoardSource board, Action saved)
    {
        _store = store;
        _saved = saved;
        _copy = RingConfigs.Parse(RingConfigs.Serialize(live));
        _previewId = _layoutId = new RingNavigator(_copy).Profile.Id;
        _preview = new RingWindow(_copy, board, preview: true);
        _debounce = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Normal, (_, _) => { _debounce!.Stop(); UpdatePreview(); }, Dispatcher);
        _debounce.Stop();

        Title = "Réglages Power Ring";
        FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
        FontSize = 13;
        Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3));
        Rect work = SystemParameters.WorkArea;
        Width = 470;
        Height = Math.Min(880, work.Height - 40);
        Left = work.Right - Width - 24;
        Top = work.Top + 20;

        var buttons = new WrapPanel { Margin = new Thickness(16, 8, 16, 12) };
        buttons.Children.Add(Btn("Enregistrer", Save, primary: true));
        buttons.Children.Add(Btn("Annuler", Close));
        buttons.Children.Add(Btn("Réinitialiser l'apparence", () => { _copy.Appearance = new RingAppearance(); Build(); Schedule(); Status("Apparence remise par défaut (pas encore enregistrée)."); }));
        var dock = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        DockPanel.SetDock(_status, Dock.Bottom);
        dock.Children.Add(buttons);
        dock.Children.Add(_status);
        dock.Children.Add(new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = dock;
        Build();
        Loaded += (_, _) => UpdatePreview();
        LocationChanged += (_, _) => Schedule();
        Closed += (_, _) => { _debounce.Stop(); _preview.Close(); };
    }

    private static double RimDefault(RingAppearance a) => Math.Clamp(Math.Round(Math.Max(20 / a.Scale, a.CenterSize * 0.36)), 16, 80);

    // ---------------------------------------------------------------- content

    private void Build()
    {
        _body.Children.Clear();
        RingAppearance a = _copy.Appearance;
        List<RingProfile> enabled = _copy.Profiles.Where(p => p.Enabled).ToList();
        if (!enabled.Any(p => p.Id == _previewId)) _previewId = enabled[0].Id;

        Header("Aperçu");
        _body.Children.Add(Row("Espace montré", Combo("Espace de l'aperçu", enabled.Select(p => p.Name), enabled.FindIndex(p => p.Id == _previewId), i => { _previewId = enabled[i].Id; Schedule(); })));

        Header("Apparence");
        _body.Children.Add(Row("Thème", Combo("Thème", ThemeNames, Array.IndexOf(ThemeIds, a.Theme.ToLowerInvariant()), i => { a.Theme = ThemeIds[i]; Schedule(); })));
        var accent = new WrapPanel();
        foreach (string hex in Swatches)
        {
            var swatch = new Button { Width = 22, Height = 22, Margin = new Thickness(0, 2, 4, 2), Background = RingTheme.Brush(RingTheme.Parse(hex)!.Value), ToolTip = hex, BorderThickness = new Thickness(hex.Equals(a.Accent, StringComparison.OrdinalIgnoreCase) ? 2 : 0) };
            AutomationProperties.SetName(swatch, "Accent " + hex);
            swatch.Click += (_, _) => { a.Accent = hex; Build(); Schedule(); };
            accent.Children.Add(swatch);
        }
        var box = new TextBox { Text = a.Accent ?? "", Width = 90, Margin = new Thickness(0, 2, 4, 2), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "#RRGGBB" };
        AutomationProperties.SetName(box, "Accent hexadécimal");
        box.TextChanged += (_, _) => { if (RingConfigs.ColorPattern().IsMatch(box.Text.Trim())) { a.Accent = box.Text.Trim(); Schedule(); } };
        accent.Children.Add(box);
        accent.Children.Add(Btn("Windows", () => { a.Accent = null; Build(); Schedule(); }, small: true));
        _body.Children.Add(Row("Accent", accent));
        foreach (SliderDef def in Sliders) _body.Children.Add(SliderRow(def, a));
        var toggles = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        foreach (var (label, get, set) in Toggles)
        {
            var check = new CheckBox { Content = label, IsChecked = get(a), Margin = new Thickness(0, 3, 14, 3) };
            check.Click += (_, _) => { set(a, check.IsChecked == true); Schedule(); };
            toggles.Children.Add(check);
        }
        _body.Children.Add(toggles);

        Header("Disposition");
        _body.Children.Add(Note("Ordre des espaces (le premier est l'accueil) :"));
        _body.Children.Add(Orderer("Espaces", _copy.Profiles.Select(p => p.Enabled ? p.Name : p.Name + " (masqué)").ToList(), _profileSelected, (i, d) => Move(_copy.Profiles, i, d)));
        List<RingProfile> rings = _copy.Profiles.Where(p => !p.IsPanel).ToList();
        if (rings.Count > 0)
        {
            if (!rings.Any(p => p.Id == _layoutId)) _layoutId = rings[0].Id;
            RingProfile layout = rings.First(p => p.Id == _layoutId);
            _body.Children.Add(Row("Cercle 1 de", Combo("Espace à ordonner", rings.Select(p => p.Name), rings.IndexOf(layout), i => { _layoutId = rings[i].Id; _itemSelected = -1; _previewId = rings[i].Enabled ? rings[i].Id : _previewId; Build(); Schedule(); })));
            _body.Children.Add(Orderer("Boutons du cercle 1", layout.Items.Select(x => x.IsGroup ? x.Label + " ›" : x.Label).ToList(), _itemSelected, (i, d) => Move(layout.Items, i, d)));
        }

        Header("Pour l'IA");
        _body.Children.Add(Note("Exporter copie une consigne, le tutoriel AI_TUTORIAL.md et votre ring.json : collez-les dans ChatGPT ou Claude, puis importez le JSON qu'il renvoie. Il est vérifié et montré dans l'aperçu ; Enregistrer l'applique."));
        var ai = new WrapPanel();
        ai.Children.Add(Btn("Exporter (presse-papier)", Export));
        ai.Children.Add(Btn("Exporter en .txt…", ExportFile));
        ai.Children.Add(Btn("Importer (presse-papier)", () => Import(Clipboard.ContainsText() ? Clipboard.GetText() : null)));
        ai.Children.Add(Btn("Importer un fichier…", ImportFile));
        _body.Children.Add(ai);
    }

    private UIElement SliderRow(SliderDef def, RingAppearance a)
    {
        var slider = new Slider { Minimum = def.Min, Maximum = def.Max, Value = Math.Clamp(def.Get(a), def.Min, def.Max), SmallChange = def.Step, LargeChange = def.Step * 5, TickFrequency = def.Step, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(slider, def.Label);
        var value = new TextBlock { Text = Format(slider.Value), Width = 44, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, e) =>
        {
            double v = Math.Round(Math.Round(e.NewValue / def.Step) * def.Step, 2);
            def.Set(a, v);
            value.Text = Format(v);
            Schedule();
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(value, 1);
        grid.Children.Add(slider);
        grid.Children.Add(value);
        return Row(def.Label, grid);
    }

    private static string Format(double v) => v.ToString(v % 1 == 0 ? "0" : "0.##", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));

    /// <summary>A list with ↑/↓: the selection follows the moved entry, then the preview updates.</summary>
    private UIElement Orderer(string name, List<string> entries, int selected, Func<int, int, bool> move)
    {
        var list = new ListBox { Height = Math.Min(170, 24 * Math.Max(2, entries.Count) + 6), Margin = new Thickness(0, 2, 6, 6) };
        foreach (string entry in entries) list.Items.Add(entry);
        list.SelectedIndex = Math.Min(selected, entries.Count - 1);
        AutomationProperties.SetName(list, name);
        bool isProfiles = name == "Espaces";
        list.SelectionChanged += (_, _) => { if (isProfiles) _profileSelected = list.SelectedIndex; else _itemSelected = list.SelectedIndex; };
        var arrows = new StackPanel { VerticalAlignment = VerticalAlignment.Top };
        foreach (int delta in new[] { -1, 1 })
        {
            Button b = Btn(delta < 0 ? "↑" : "↓", () =>
            {
                int i = list.SelectedIndex;
                if (i < 0 || !move(i, delta)) return;
                if (isProfiles) _profileSelected = i + delta; else _itemSelected = i + delta;
                Build();
                Schedule();
            }, small: true);
            AutomationProperties.SetName(b, $"{name} : {(delta < 0 ? "monter" : "descendre")}");
            arrows.Children.Add(b);
        }
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(arrows, 1);
        grid.Children.Add(list);
        grid.Children.Add(arrows);
        return grid;
    }

    private static bool Move<T>(List<T> list, int index, int delta)
    {
        int other = index + delta;
        if (other < 0 || other >= list.Count) return false;
        (list[index], list[other]) = (list[other], list[index]);
        return true;
    }

    private void Header(string text) => _body.Children.Add(new TextBlock { Text = text, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 6) });

    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = RingTheme.Brush(Color.FromRgb(0x5F, 0x5F, 0x5F)), Margin = new Thickness(0, 0, 0, 6) };

    private static UIElement Row(string label, UIElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn((FrameworkElement)control, 1);
        grid.Children.Add(text);
        grid.Children.Add(control);
        return grid;
    }

    private static ComboBox Combo(string name, IEnumerable<string> entries, int selected, Action<int> changed)
    {
        var combo = new ComboBox { Margin = new Thickness(0, 2, 6, 2) };
        foreach (string entry in entries) combo.Items.Add(entry);
        combo.SelectedIndex = Math.Max(0, selected);
        AutomationProperties.SetName(combo, name);
        combo.SelectionChanged += (_, _) => { if (combo.SelectedIndex >= 0) changed(combo.SelectedIndex); };
        return combo;
    }

    private static Button Btn(string text, Action click, bool primary = false, bool small = false)
    {
        var button = new Button
        {
            Content = text,
            Padding = small ? new Thickness(8, 1, 8, 1) : new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 2, 6, 2),
            MinWidth = small ? 28 : 0,
        };
        if (primary)
        {
            Color accent = RingTheme.WindowsAccent() ?? Color.FromRgb(0x00, 0x78, 0xD4);
            button.Background = RingTheme.Brush(accent);
            button.Foreground = RingTheme.Brush(RingTheme.OnColor(accent));
        }
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => click();
        return button;
    }

    // ---------------------------------------------------------------- preview, save, AI

    private void Schedule()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void UpdatePreview()
    {
        if (!IsLoaded) return;
        try
        {
            RingConfigs.Validate(_copy);
            _preview.ShowPreview(_copy, _previewId, new Point(Left - 12, Top));
        }
        catch (RingConfigException ex) { Status("Aperçu non mis à jour : " + French(ex.Message), error: true); }
    }

    private void Save()
    {
        try
        {
            _store.Save(_copy);
            _saved();
            Close();
            Toast.Show("Réglages enregistrés (l'ancien ring.json est gardé en ring.json.bak)", 2500);
        }
        catch (Exception ex) when (ex is RingConfigException or IOException or UnauthorizedAccessException)
        {
            Status("Non enregistré : " + French(ex.Message), error: true);
        }
    }

    /// <summary>The prompt bundle for an AI: what to do, the full tutorial, then the current ring.json.</summary>
    private string Bundle()
    {
        string current = File.Exists(_store.FilePath) ? File.ReadAllText(_store.FilePath) : RingConfigs.Serialize(_copy);
        return "Tu modifies la configuration de Power Ring (lanceur radial Windows). Lis le tutoriel ci-dessous, applique la demande que je vais te donner, "
            + "et réponds avec le fichier ring.json COMPLET et valide, dans un seul bloc de code json (aucun champ inventé).\n\nMa demande : \n\n"
            + "===== AI_TUTORIAL.md =====\n" + RingConfigStore.Resource(RingConfigStore.TutorialFileName)
            + "\n\n===== ring.json actuel =====\n" + current + "\n";
    }

    private void Export()
    {
        string bundle = Bundle();
        ActionRunner.SetClipboard(() => Clipboard.SetText(bundle));
        Status($"Copié dans le presse-papier ({bundle.Length / 1000} k caractères) : collez-le dans votre IA et ajoutez votre demande.");
    }

    private void ExportFile()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "power-ring-pour-ia.txt", Filter = "Texte (*.txt)|*.txt" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, Bundle()); Status("Enregistré : " + dialog.FileName); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status("Non enregistré : " + ex.Message, error: true); }
    }

    private void ImportFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "JSON ou texte (*.json;*.txt)|*.json;*.txt|Tous les fichiers|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try { Import(File.ReadAllText(dialog.FileName)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Status("Lecture impossible : " + ex.Message, error: true); }
    }

    /// <summary>Takes the JSON out of an AI answer (code fences and text around it are ignored), validates it and previews it.</summary>
    public void Import(string? text)
    {
        int start = text?.IndexOf('{') ?? -1, end = text?.LastIndexOf('}') ?? -1;
        if (text is null || start < 0 || end <= start) { Status("Import refusé : aucun JSON trouvé (il doit commencer par { et finir par }).", error: true); return; }
        try
        {
            _copy = RingConfigs.Parse(text[start..(end + 1)]);
            _profileSelected = _itemSelected = -1;
            Build();
            Schedule();
            Status("JSON importé et vérifié : l'aperçu le montre. Enregistrer l'applique, Annuler l'abandonne.");
        }
        catch (RingConfigException ex) { Status("Import refusé : " + French(ex.Message), error: true); }
    }

    /// <summary>"profiles[1].items[0].target: give ..." → the field path first, in French.</summary>
    private static string French(string message)
    {
        int colon = message.IndexOf(": ", StringComparison.Ordinal);
        if (colon > 0 && !message[..colon].Contains(' ')) return $"champ en cause « {message[..colon]} » — {message[(colon + 2)..]}";
        return message;
    }

    private void Status(string text, bool error = false)
    {
        _status.Text = text;
        _status.Foreground = RingTheme.Brush(error ? Color.FromRgb(0xC4, 0x2B, 0x1C) : Color.FromRgb(0x1A, 0x1A, 0x1A));
    }
}
