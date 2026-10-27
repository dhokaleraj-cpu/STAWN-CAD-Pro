using System;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stawn.Model;

namespace Stawn.Views;

/// <summary>Feature tree: the part, its folders and reference geometry, then the features in order.</summary>
public sealed class FeatureTree : UserControl
{
    private readonly ListView _list = new() { SelectionMode = ListViewSelectionMode.Single };
    private bool _refreshing;
    public PartModel? Model { get; set; }

    /// <summary>Raised on double-click or "Edit Feature" so the window can show the PropertyManager.</summary>
    public event Action<string>? EditFeature;

    public FeatureTree()
    {
        Content = _list;
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
        _list.Items.Add(Row("", $"{Model.Name} ({Model.ActiveConfiguration})", null, bold: true));
        foreach (var (glyph, text) in new[] { ("", "History"), ("", "Annotations"), ("", $"Solid Bodies ({bodies})"),
                                              ("", material), ("", "Front Plane"), ("", "Top Plane"),
                                              ("", "Right Plane"), ("", "Origin") })
            _list.Items.Add(Row(glyph, text, null, secondary: true));
        foreach (var (name, type) in Model.FeatureList)
        {
            var row = Row(Icon(type), name, name, dimmed: Model.IsSuppressed(name), error: Model.ErrorFor(name));
            row.ContextFlyout = Menu(name);
            _list.Items.Add(row);
            if (name == Model.SelectedFeature) _list.SelectedItem = row;
        }
        _refreshing = false;
    }

    private MenuFlyout Menu(string name)
    {
        var menu = new MenuFlyout();
        var edit = new MenuFlyoutItem { Text = "Edit Feature" };
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

    private static ListViewItem Row(string glyph, string text, string? tag, bool bold = false, bool secondary = false,
                                    bool dimmed = false, string? error = null)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14 });
        var label = new TextBlock { Text = text, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
        if (secondary || dimmed) label.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        panel.Children.Add(label);
        if (error != null)
        {
            var warn = new FontIcon { Glyph = "", FontSize = 14, Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red) };
            ToolTipService.SetToolTip(warn, error);
            panel.Children.Add(warn);
        }
        return new ListViewItem { Content = panel, Tag = tag, IsEnabled = true, Opacity = dimmed ? 0.6 : 1, MinHeight = 28, Padding = new Thickness(8, 0, 8, 0) };
    }

    private static string Icon(string type) => type switch
    {
        "Sketch" => "",
        "Revolve" => "",
        "Extrude" => "",
        "Fillet" => "",
        "Chamfer" => "",
        "Import" => "",
        _ => "",
    };
}
