using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stawn.Model;

namespace Stawn.Views;

/// <summary>A property template (.stprp.json): groups of controls bound to custom property keys.</summary>
public sealed class PropertyTemplate
{
    public sealed class Control
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Type { get; set; } = "text";  // text, list, radio, date
        public List<string>? Options { get; set; }
        public string? Default { get; set; }
        public bool? Linked { get; set; }
    }

    public sealed class Group
    {
        public string Title { get; set; } = "";
        public List<Control> Controls { get; set; } = new();
    }

    public string Name { get; set; } = "";
    public List<Group> Groups { get; set; } = new();

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static List<PropertyTemplate> Bundled()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Templates");
        if (!Directory.Exists(dir)) return new();
        return Directory.GetFiles(dir, "*.stprp.json").Select(Load).OfType<PropertyTemplate>().ToList();
    }

    public static PropertyTemplate? Load(string path)
    {
        try { return JsonSerializer.Deserialize<PropertyTemplate>(File.ReadAllText(path), Options); }
        catch (Exception) { return null; }
    }
}

/// <summary>Right-hand task pane that fills custom properties through a template.</summary>
public sealed class CustomPropertiesPane : UserControl
{
    private readonly List<PropertyTemplate> _templates = PropertyTemplate.Bundled();
    private readonly HashSet<string> _collapsed = new();
    private readonly StackPanel _body = new() { Padding = new Thickness(12, 4, 12, 12), Spacing = 4 };
    private readonly ComboBox _templatePicker = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox _configurationSpecific = new();
    private bool _refreshing;
    public PartModel? Model { get; set; }

    public CustomPropertiesPane()
    {
        Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"];
        var header = new StackPanel { Padding = new Thickness(12, 8, 12, 4), Spacing = 6 };
        header.Children.Add(new TextBlock { Text = "Custom Properties", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        foreach (var t in _templates) _templatePicker.Items.Add(t.Name);
        _templatePicker.Items.Add("All properties");
        _templatePicker.SelectedIndex = 0;
        _templatePicker.SelectionChanged += (_, _) => { if (!_refreshing) Refresh(); };
        header.Children.Add(_templatePicker);
        _configurationSpecific.Checked += (_, _) => Refresh();
        _configurationSpecific.Unchecked += (_, _) => Refresh();
        header.Children.Add(_configurationSpecific);
        var root = new Grid { RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition() } };
        root.Children.Add(header);
        var scroll = new ScrollViewer { Content = _body };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        Content = root;
    }

    private PropertyTemplate? ActiveTemplate =>
        _templatePicker.SelectedIndex >= 0 && _templatePicker.SelectedIndex < _templates.Count ? _templates[_templatePicker.SelectedIndex] : null;

    public void Refresh()
    {
        if (Model == null) return;
        _refreshing = true;
        _configurationSpecific.Content = $"Configuration specific ({Model.ActiveConfiguration})";
        _body.Children.Clear();
        if (ActiveTemplate is { } t)
        {
            foreach (var g in t.Groups) AddGroup(g);
        }
        else
        {
            foreach (var (k, v) in Model.ResolvedProperties.OrderBy(kv => kv.Key)) _body.Children.Add(Caption(k, v));
        }
        _refreshing = false;
    }

    private void AddGroup(PropertyTemplate.Group g)
    {
        bool collapsed = _collapsed.Contains(g.Title);
        var header = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(0, 8, 0, 4),
        };
        var row = new Grid();
        row.Children.Add(new TextBlock { Text = g.Title.ToUpperInvariant(), FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        row.Children.Add(new FontIcon { Glyph = collapsed ? "" : "", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right });
        header.Content = row;
        header.Click += (_, _) => { if (!_collapsed.Remove(g.Title)) _collapsed.Add(g.Title); Refresh(); };
        _body.Children.Add(header);
        if (!collapsed)
            foreach (var c in g.Controls) _body.Children.Add(ControlFor(c));
        _body.Children.Add(new Border { Height = 1, Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"], Margin = new Thickness(0, 6, 0, 0) });
    }

    private UIElement ControlFor(PropertyTemplate.Control c)
    {
        EnsureDefault(c);
        var resolved = Model!.ResolvedProperties.GetValueOrDefault(c.Key, "");
        var stored = Model.Properties.GetValueOrDefault(c.Key, "");
        var panel = new StackPanel { Spacing = 2, Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(new TextBlock { Text = c.Label.ToUpperInvariant(), FontSize = 10, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        switch (c.Type)
        {
            case "list":
            {
                var box = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "" }.Concat(c.Options ?? new()).ToList() };
                box.SelectedItem = resolved;
                box.SelectionChanged += (_, _) => { if (!_refreshing && box.SelectedItem is string s) Set(c.Key, s); };
                panel.Children.Add(box);
                break;
            }
            case "radio":
            {
                var radios = new RadioButtons { ItemsSource = c.Options ?? new() };
                radios.SelectedItem = resolved;
                radios.SelectionChanged += (_, _) => { if (!_refreshing && radios.SelectedItem is string s) Set(c.Key, s); };
                panel.Children.Add(radios);
                break;
            }
            case "date":
            {
                var picker = new CalendarDatePicker { HorizontalAlignment = HorizontalAlignment.Stretch, DateFormat = "{day.integer(2)}-{month.integer(2)}-{year.full}" };
                if (DateTimeOffset.TryParseExact(resolved, "dd-MM-yyyy", null, System.Globalization.DateTimeStyles.None, out var d)) picker.Date = d;
                picker.DateChanged += (_, args) => { if (!_refreshing && args.NewDate is { } nd) Set(c.Key, nd.ToString("dd-MM-yyyy")); };
                panel.Children.Add(picker);
                break;
            }
            default:
                if (c.Linked == true)
                {
                    var linked = new Grid();
                    linked.Children.Add(new TextBlock { Text = resolved.Length == 0 ? "–" : resolved });
                    var icon = new FontIcon { Glyph = "", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right };
                    ToolTipService.SetToolTip(icon, $"Linked to {c.Default}");
                    linked.Children.Add(icon);
                    panel.Children.Add(linked);
                }
                else
                {
                    panel.Children.Add(CommitField(stored, resolved, v => Set(c.Key, v)));
                }
                break;
        }
        return panel;
    }

    /// <summary>
    /// Text box that writes its value when editing ends instead of on every keystroke. While not
    /// focused it shows the evaluated value (e.g. the part name for ${PartName}); while focused it
    /// shows the stored text so links can be edited.
    /// </summary>
    private static TextBox CommitField(string stored, string display, Action<string> commit)
    {
        var box = new TextBox { Text = display };
        box.GotFocus += (_, _) => box.Text = stored;
        box.LostFocus += (_, _) => { if (box.Text != stored) commit(box.Text); else box.Text = display; };
        box.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter && box.Text != stored) commit(box.Text); };
        return box;
    }

    private void Set(string key, string value) => Model?.SetProperty(key, value, _configurationSpecific.IsChecked == true);

    private void EnsureDefault(PropertyTemplate.Control c)
    {
        if (c.Default != null && !Model!.Properties.ContainsKey(c.Key))
            DispatcherQueue.TryEnqueue(() => Set(c.Key, c.Default));
    }

    private static UIElement Caption(string key, string value)
    {
        var g = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        g.Children.Add(new TextBlock { Text = key, FontSize = 10, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        g.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        return g;
    }
}
