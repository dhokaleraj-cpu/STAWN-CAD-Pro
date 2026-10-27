using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stawn.Model;
using Windows.UI;

namespace Stawn.Views;

/// <summary>
/// FeatureManager design tree: the part, its folders, the three planes and the origin, then the
/// features in order with the sketch each one uses nested under it. A sketch that is not fully
/// defined shows (-) before its name, an over-defined one (+).
/// </summary>
public sealed class FeatureTree : UserControl
{
    private readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.Single, Padding = new Thickness(0, 4, 0, 4) };
    private readonly HashSet<string> _collapsed = new();
    private bool _refreshing;
    public PartModel? Model { get; set; }

    /// <summary>Raised on double-click or "Edit Feature" / "Edit Sketch".</summary>
    public event Action<string>? EditFeature;
    /// <summary>Raised by "Sketch" on a plane's menu, with the plane name.</summary>
    public event Action<string>? SketchOnPlane;

    public FeatureTree()
    {
        Content = _list;
        Background = new SolidColorBrush(Colors.White);
        _list.SelectionChanged += (_, _) =>
        {
            if (_refreshing || Model == null) return;
            Model.Select((_list.SelectedItem as ListViewItem)?.Tag as string);
        };
        _list.DoubleTapped += (_, _) => { if (Model?.SelectedFeature is { } f) EditFeature?.Invoke(f); };
    }

    public void Refresh()
    {
        if (Model == null) return;
        _refreshing = true;
        _list.Items.Clear();
        var bodies = Model.Mesh == null ? 0 : 1;
        var material = Model.Material.Name.Length == 0 ? "Material <not specified>" : Model.Material.Name;
        _list.Items.Add(Row(0, "part", $"{Model.Name} ({Model.ActiveConfiguration})", null, bold: true));
        _list.Items.Add(Row(1, "history", "History", null));
        _list.Items.Add(Row(1, "annotations", "Annotations", null));
        _list.Items.Add(Row(1, "bodies", $"Solid Bodies({bodies})", null));
        _list.Items.Add(Row(1, "material", material, null));
        foreach (var plane in new[] { "Front", "Top", "Right" })
        {
            var row = Row(1, "plane", $"{plane} Plane", null);
            row.ContextFlyout = PlaneMenu(plane);
            row.DoubleTapped += (_, e) => { SketchOnPlane?.Invoke(plane); e.Handled = true; };
            _list.Items.Add(row);
        }
        _list.Items.Add(Row(1, "origin", "Origin", null));

        // Sketches used by a later feature are shown under that feature, not on their own.
        var features = Model.FeatureList.ToList();
        var absorbed = features.Select(f => Model.SketchOf(f.Name)).OfType<string>().ToHashSet();
        foreach (var (name, type) in features)
        {
            if (type == "Sketch" && absorbed.Contains(name)) continue;
            AddFeature(1, name, type);
            if (Model.SketchOf(name) is { } sketch && !_collapsed.Contains(name)) AddFeature(2, sketch, "Sketch");
        }
        _refreshing = false;
    }

    private void AddFeature(int depth, string name, string type)
    {
        if (Model == null) return;
        string label = name;
        if (type == "Sketch" && Model.SketchStatus.TryGetValue(name, out var status))
            label = status switch { "Under Defined" => "(-) " + name, "Over Defined" => "(+) " + name, _ => name };
        var icon = Model.IsCut(name) ? (type == "Revolve" ? "cut-revolve" : "cut-extrude") : Icon(type);
        var row = Row(depth, icon, label, name, dimmed: Model.IsSuppressed(name), error: Model.ErrorFor(name),
                      expander: Model.SketchOf(name) != null ? !_collapsed.Contains(name) : null);
        row.ContextFlyout = Menu(name, type);
        _list.Items.Add(row);
        if (name == Model.SelectedFeature) _list.SelectedItem = row;
    }

    private MenuFlyout PlaneMenu(string plane)
    {
        var menu = new MenuFlyout();
        var sketch = new MenuFlyoutItem { Text = "Sketch" };
        sketch.Click += (_, _) => SketchOnPlane?.Invoke(plane);
        menu.Items.Add(sketch);
        return menu;
    }

    private MenuFlyout Menu(string name, string type)
    {
        var menu = new MenuFlyout();
        var edit = new MenuFlyoutItem { Text = type == "Sketch" ? "Edit Sketch" : "Edit Feature" };
        edit.Click += (_, _) => { if (Model == null) return; Model.Select(name); EditFeature?.Invoke(name); };
        var suppress = new MenuFlyoutItem { Text = Model!.IsSuppressed(name) ? "Unsuppress" : "Suppress" };
        suppress.Click += (_, _) => Model?.SetSuppressed(name, !Model.IsSuppressed(name));
        var rename = new MenuFlyoutItem { Text = "Rename…" };
        rename.Click += async (_, _) =>
        {
            if (await Dialogs.TextAsync(this, "Rename Feature", "Name", name) is { } n) Model?.RenameFeature(name, n);
        };
        menu.Items.Add(edit);
        menu.Items.Add(suppress);
        menu.Items.Add(rename);
        return menu;
    }

    private ListViewItem Row(int depth, string icon, string text, string? tag, bool bold = false, bool dimmed = false,
                             string? error = null, bool? expander = null)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(depth * 14, 0, 0, 0) };
        // Expand arrow for features with a sketch under them; a blank of the same width otherwise.
        var arrow = new TextBlock { Text = expander == null ? "" : expander.Value ? "▾" : "▸", Width = 10, FontSize = 10,
                                    VerticalAlignment = VerticalAlignment.Center, Foreground = new SolidColorBrush(Color.FromArgb(255, 90, 90, 90)) };
        if (expander != null && tag != null)
            arrow.Tapped += (_, e) =>
            {
                if (!_collapsed.Remove(tag)) _collapsed.Add(tag);
                e.Handled = true;
                Refresh();
            };
        panel.Children.Add(arrow);
        panel.Children.Add(Icons.Get(icon, 16));
        var label = new TextBlock { Text = text, FontSize = 13, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center };
        if (dimmed) label.Foreground = new SolidColorBrush(Color.FromArgb(255, 140, 140, 140));
        panel.Children.Add(label);
        if (error != null)
        {
            var warn = new TextBlock { Text = "⚠", Foreground = new SolidColorBrush(Colors.Red), VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(warn, error);
            panel.Children.Add(warn);
        }
        return new ListViewItem { Content = panel, Tag = tag, Opacity = dimmed ? 0.65 : 1, MinHeight = 24, Padding = new Thickness(6, 0, 6, 0) };
    }

    private static string Icon(string type) => type switch
    {
        "Sketch" => "sketch",
        "Revolve" => "revolve",
        "Extrude" => "extrude",
        "Fillet" => "fillet",
        "Chamfer" => "chamfer",
        "Import" => "import",
        _ => "part",
    };
}
