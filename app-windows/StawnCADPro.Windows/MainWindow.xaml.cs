using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Stawn.Model;
using Stawn.Views;
using Windows.Storage.Pickers;

namespace Stawn;

/// <summary>
/// Part window: menu, CommandManager, manager pane on the left, graphics area, custom
/// properties task pane and status bar. Mirrors PartWindow.swift on the Mac.
/// </summary>
public sealed partial class MainWindow : Window
{
    private PartModel? _model;

    public MainWindow(PartModel? model)
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1500, 920));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "StawnCADPro.ico"));
        BuildCommandManager();
        BuildViewMenus();
        Welcome.NewPart += () => Load(PartModel.Empty());
        Welcome.OpenSample += () => Load(PartModel.SampleShaft());
        Welcome.OpenPart += () => _ = OpenAsync();
        FeatureTreePanel.EditFeature += name =>
        {
            if (_model?.FeatureList.FirstOrDefault(f => f.Name == name).Type == "Sketch") EditSketch(name);
            else ManagerTabs.SelectedItem = PropertyTab;
        };
        SketchView.EditorChanged += OnSketchEditorChanged;
        Closed += (_, _) => _model?.Dispose();
        if (model != null) Load(model); else ShowWelcome();
    }

    private void ShowWelcome()
    {
        Welcome.Visibility = Visibility.Visible;
        SetDocumentCommandsEnabled(false);
        TitleText.Text = "STAWN CAD Pro";
        StatusLeft.Text = "";
        StatusRight.Text = "";
    }

    private void Load(PartModel model)
    {
        LeaveSketchMode();
        _model?.Dispose();
        _model = model;
        Welcome.Visibility = Visibility.Collapsed;
        SetDocumentCommandsEnabled(true);
        FeatureTreePanel.Model = model;
        PropertyPanel.Model = model;
        ConfigurationPanel.Model = model;
        PropertiesPane.Model = model;
        model.Changed += OnModelChanged;
        model.SelectionChanged += PropertyPanel.Refresh;
        OnModelChanged();
        View3D.ShowView(StandardView.Isometric);
    }

    private void OnModelChanged()
    {
        if (_model == null || InSketch) return;
        View3D.SetMesh(_model.Mesh);
        FeatureTreePanel.Refresh();
        PropertyPanel.Refresh();
        ConfigurationPanel.Refresh();
        PropertiesPane.Refresh();
        var file = _model.FilePath == null ? _model.Name : Path.GetFileName(_model.FilePath);
        TitleText.Text = $"STAWN CAD Pro - {file}{(_model.IsDirty ? " *" : "")}";
        Title = TitleText.Text;
        StatusLeft.Text = _model.RebuildErrors.Count == 0
            ? $"{_model.Name}  ·  Editing Part  ·  {_model.ActiveConfiguration}"
            : $"Rebuild errors: {string.Join("; ", _model.RebuildErrors)}";
        StatusRight.Text = $"Mass {_model.Mass.Mass:0.000} g   ·   MMGS";
    }

    private void SetDocumentCommandsEnabled(bool on)
    {
        foreach (var item in new MenuFlyoutItemBase[] { SaveItem, SaveAsItem, ImportItem, ExportItem }) item.IsEnabled = on;
        CommandManager.IsEnabled = on;
        PropertiesPane.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        TaskPaneColumn.Width = on ? new GridLength(300) : new GridLength(0);
    }

    // CommandManager

    private void BuildCommandManager()
    {
        AddButton(FeaturesBar, "Extruded Boss/Base", "", () => _ = ExtrudeAsync(false));
        AddButton(FeaturesBar, "Revolved Boss/Base", "", () => _ = RevolveAsync(false));
        AddButton(FeaturesBar, "Extruded Cut", "", () => _ = ExtrudeAsync(true));
        AddButton(FeaturesBar, "Revolved Cut", "", () => _ = RevolveAsync(true));
        FeaturesBar.PrimaryCommands.Add(new AppBarSeparator());
        AddButton(FeaturesBar, "Fillet", "", () => _ = EdgeFeatureAsync("fillet"));
        AddButton(FeaturesBar, "Chamfer", "", () => _ = EdgeFeatureAsync("chamfer"));
        FeaturesBar.PrimaryCommands.Add(new AppBarSeparator());
        AddButton(FeaturesBar, "Import STEP", "", () => _ = ImportStepAsync());

        var sketch = new AppBarButton { Label = "Sketch", Icon = new FontIcon { Glyph = "\uE70F" } };
        var planes = new MenuFlyout();
        foreach (var plane in new[] { "Front", "Top", "Right" })
        {
            var item = new MenuFlyoutItem { Text = $"{plane} Plane" };
            item.Click += (_, _) => StartSketch(plane);
            planes.Items.Add(item);
        }
        planes.Items.Add(new MenuFlyoutSeparator());
        var editSelected = new MenuFlyoutItem { Text = "Edit Selected Sketch" };
        editSelected.Click += (_, _) => { if (SelectedSketch() is { } name) EditSketch(name); };
        planes.Items.Add(editSelected);
        planes.Opening += (_, _) => editSelected.IsEnabled = SelectedSketch() != null;
        sketch.Flyout = planes;
        ToolTipService.SetToolTip(sketch, "Sketch on a plane, or edit the selected sketch");
        SketchBar.PrimaryCommands.Add(sketch);
        AddButton(SketchBar, "Exit Sketch", "\uE8FB", ExitSketch);
        SketchBar.PrimaryCommands.Add(new AppBarSeparator());
        foreach (var (label, glyph, tool) in new[] { ("Select", "\uE8B0", "select"), ("Line", "\uE7B5", "line"),
                                                     ("Centerline", "\uE738", "centerline"), ("Rectangle", "\uE739", "rectangle"),
                                                     ("Circle", "\uEA3A", "circle"), ("Arc", "\uE7AD", "arc"),
                                                     ("Smart Dimension", "\uE7C0", "dimension") })
            AddButton(SketchBar, label, glyph, () =>
            {
                if (InSketch) SketchView.SetTool(tool);
                else _ = Dialogs.MessageAsync(Root, label, "Start a sketch first: Sketch > Front, Top or Right Plane, or edit a sketch from the feature tree.");
            });

        AddButton(EvaluateBar, "Mass Properties", "", () => _ = ShowMassPropertiesAsync());
        AddButton(EvaluateBar, "Rebuild", "", () => _model?.Refresh());

        AddButton(DrawingBar, "Preview Drawing", "", () => _ = PreviewDrawingAsync());
        AddButton(DrawingBar, "Save As PDF", "", () => _ = ExportDrawingAsync("pdf"));
        AddButton(DrawingBar, "Save As DXF", "", () => _ = ExportDrawingAsync("dxf"));
        AddButton(DrawingBar, "Save As SVG", "", () => _ = ExportDrawingAsync("svg"));
    }

    private static void AddButton(CommandBar bar, string label, string glyph, Action action)
    {
        var b = new AppBarButton { Label = label, Icon = new FontIcon { Glyph = glyph } };
        b.Click += (_, _) => action();
        ToolTipService.SetToolTip(b, label);
        bar.PrimaryCommands.Add(b);
    }

    private static readonly (DisplayStyle Style, string Label)[] DisplayStyles =
        { (DisplayStyle.ShadedWithEdges, "Shaded With Edges"), (DisplayStyle.Shaded, "Shaded"), (DisplayStyle.Wireframe, "Wireframe") };

    private void BuildViewMenus()
    {
        OrientationMenu.Items.Clear();
        foreach (var item in OrientationItems()) OrientationMenu.Items.Add(item);
        foreach (var item in DisplayStyleItems()) DisplayStyleMenu.Items.Add(item);

        var fit = HeadsUpButton("\uE9A6", "Zoom to Fit (F)");
        fit.Click += (_, _) => View3D.ZoomToFit();
        var orientation = HeadsUpButton("\uF158", "View Orientation");
        orientation.Flyout = Flyout(OrientationItems());
        var style = HeadsUpButton("\uE793", "Display Style");
        style.Flyout = Flyout(DisplayStyleItems());
        HeadsUp.Children.Add(fit);
        HeadsUp.Children.Add(orientation);
        HeadsUp.Children.Add(style);
    }

    private MenuFlyoutItem[] OrientationItems() => Enum.GetValues<StandardView>().Select(v =>
    {
        var item = new MenuFlyoutItem { Text = v.ToString() };
        item.Click += (_, _) => View3D.ShowView(v);
        return item;
    }).ToArray();

    private MenuFlyoutItem[] DisplayStyleItems() => DisplayStyles.Select(d =>
    {
        var item = new MenuFlyoutItem { Text = d.Label };
        item.Click += (_, _) => View3D.Shading = d.Style;
        return item;
    }).ToArray();

    private static MenuFlyout Flyout(MenuFlyoutItem[] items)
    {
        var menu = new MenuFlyout();
        foreach (var i in items) menu.Items.Add(i);
        return menu;
    }

    private static Button HeadsUpButton(string glyph, string tip)
    {
        var b = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 14 }, Padding = new Thickness(8, 4, 8, 4), BorderThickness = new Thickness(0) };
        b.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        ToolTipService.SetToolTip(b, tip);
        return b;
    }

    private void OnManagerTabChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        FeatureTreePanel.Visibility = sender.SelectedItem == FeatureTab ? Visibility.Visible : Visibility.Collapsed;
        PropertyPanel.Visibility = sender.SelectedItem == PropertyTab ? Visibility.Visible : Visibility.Collapsed;
        ConfigurationPanel.Visibility = sender.SelectedItem == ConfigurationTab ? Visibility.Visible : Visibility.Collapsed;
    }

    // File menu

    private void OnNew(object sender, RoutedEventArgs e) => OpenInWindow(PartModel.Empty());
    private void OnSample(object sender, RoutedEventArgs e) => OpenInWindow(PartModel.SampleShaft());
    private void OnOpen(object sender, RoutedEventArgs e) => _ = OpenAsync();
    private void OnSave(object sender, RoutedEventArgs e) => _ = SaveAsync(false);
    private void OnSaveAs(object sender, RoutedEventArgs e) => _ = SaveAsync(true);
    private void OnImportStep(object sender, RoutedEventArgs e) => _ = ImportStepAsync();
    private void OnExportStep(object sender, RoutedEventArgs e) => _ = ExportAsync("STEP", ".step", (m, p) => m.ExportStep(p));
    private void OnExportStl(object sender, RoutedEventArgs e) => _ = ExportAsync("STL", ".stl", (m, p) => m.ExportStl(p));
    private void OnExit(object sender, RoutedEventArgs e) => Close();
    private void OnZoomToFit(object sender, RoutedEventArgs e) => View3D.ZoomToFit();
    private void OnRunCommands(object sender, RoutedEventArgs e) => RunCommands(Examples.Sketch);
    private void OnEditMaterial(object sender, RoutedEventArgs e) => _ = EditMaterialAsync();
    private void OnAbout(object sender, RoutedEventArgs e) =>
        _ = Dialogs.MessageAsync(Root, "STAWN CAD Pro", "Parametric part modelling for Windows 11 and macOS.\nVersion 0.1.0");

    /// <summary>Uses this window while it shows the Welcome panel, otherwise opens a new one.</summary>
    private void OpenInWindow(PartModel model)
    {
        if (_model == null) Load(model);
        else new MainWindow(model).Activate();
    }

    private async Task OpenAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".stprt");
        Dialogs.Attach(picker, this);
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;
        try { OpenInWindow(PartModel.Open(file.Path)); }
        catch (Exception ex) { await Dialogs.MessageAsync(Root, "Could not open the part", ex.Message); }
    }

    private async Task SaveAsync(bool saveAs)
    {
        if (_model == null) return;
        var path = _model.FilePath;
        if (saveAs || path == null)
        {
            path = await Dialogs.SavePathAsync(this, _model.Name, "STAWN Part", ".stprt");
            if (path == null) return;
        }
        if (!_model.Save(path)) await Dialogs.MessageAsync(Root, "Save failed", $"Could not write {path}.");
    }

    private async Task ImportStepAsync()
    {
        if (_model == null) return;
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".step");
        picker.FileTypeFilter.Add(".stp");
        Dialogs.Attach(picker, this);
        var file = await picker.PickSingleFileAsync();
        if (file == null) return;
        if (_model.ImportStep(file.Path) is { } error) await Dialogs.MessageAsync(Root, "Import failed", error);
    }

    private async Task ExportAsync(string kind, string extension, Func<PartModel, string, bool> export)
    {
        if (_model == null) return;
        var path = await Dialogs.SavePathAsync(this, _model.Name, kind, extension);
        if (path != null && !export(_model, path)) await Dialogs.MessageAsync(Root, "Export failed", $"Could not export {kind} to {path}.");
    }

    // Sketch mode

    private bool InSketch => SketchView.Visibility == Visibility.Visible;

    private string? SelectedSketch() =>
        _model?.SelectedFeature is { } f && _model.FeatureList.FirstOrDefault(x => x.Name == f).Type == "Sketch" ? f : null;

    private void StartSketch(string plane)
    {
        if (_model == null) return;
        if (InSketch) ExitSketch();
        var reply = _model.Command(new JsonObject { ["op"] = "create_sketch", ["plane"] = plane });
        if (reply["sketch"]?.GetValue<string>() is { } name) EditSketch(name);
    }

    private void EditSketch(string name)
    {
        if (_model == null) return;
        if (InSketch) ExitSketch();
        var state = _model.Command(new JsonObject { ["op"] = "edit_sketch", ["sketch"] = name });
        if (state["ok"]?.GetValue<bool>() != true) return;
        _model.Refresh();  // shows a new sketch in the feature tree
        _model.Select(name);
        View3D.Visibility = Visibility.Collapsed;
        HeadsUp.Visibility = Visibility.Collapsed;
        SketchView.Visibility = Visibility.Visible;
        SketchPanel.Visibility = Visibility.Visible;
        SketchView.Open(_model, state);
        CommandManager.SelectedIndex = 1;
        SketchView.SetTool("line");
    }

    /// <summary>Closes the sketch and rebuilds the part, so features that use it update.</summary>
    private void ExitSketch()
    {
        if (_model == null || !InSketch) return;
        _model.Command(new JsonObject { ["op"] = "close_sketch" });
        LeaveSketchMode();
        _model.Refresh();
    }

    private void LeaveSketchMode()
    {
        SketchView.Visibility = Visibility.Collapsed;
        SketchPanel.Visibility = Visibility.Collapsed;
        View3D.Visibility = Visibility.Visible;
        HeadsUp.Visibility = Visibility.Visible;
    }

    private bool _modifyOpen;

    private async void OnSketchEditorChanged(string ev, JsonObject state)
    {
        SketchPrompt.Text = state["prompt"]?.GetValue<string>() ?? "";
        StatusLeft.Text = $"Editing {SketchView.SketchName}  ·  {SketchPrompt.Text}";
        int dof = SketchView.DegreesOfFreedom;
        StatusRight.Text = SketchView.Status == "Under Defined" ? $"Under Defined ({dof} free)" : SketchView.Status;

        RelationButtons.Children.Clear();
        var relations = (state["relations"] as JsonArray ?? new JsonArray()).Select(r => r!.GetValue<string>()).ToList();
        if (relations.Count > 0) RelationButtons.Children.Add(new TextBlock { Text = "Add Relation:", VerticalAlignment = VerticalAlignment.Center });
        for (int i = 0; i < relations.Count; i++)
        {
            int index = i;
            var b = new Button { Content = relations[i] };
            b.Click += (_, _) => SketchView.AddRelation(index);
            RelationButtons.Children.Add(b);
        }

        // Smart Dimension: ask for the value as soon as the geometry is picked.
        if (ev == "click" && !_modifyOpen && state["dimension_request"] is JsonObject request)
        {
            _modifyOpen = true;
            try
            {
                if (await Dialogs.DimensionAsync(Root, request) is { } d) SketchView.AddDimension(d.Option, d.Value);
                else SketchView.Escape();
            }
            finally { _modifyOpen = false; }
        }
    }

    // Features and tools

    /// <summary>The sketch a feature uses: the open one, else the selected one, else the last one.</summary>
    private string? FeatureSketch()
    {
        if (InSketch) return SketchView.SketchName;
        if (SelectedSketch() is { } s) return s;
        return _model?.FeatureList.LastOrDefault(f => f.Type == "Sketch").Name;
    }

    private async Task ExtrudeAsync(bool cut)
    {
        if (_model == null) return;
        var title = cut ? "Extruded Cut" : "Extruded Boss/Base";
        if (FeatureSketch() is not { } sketch)
        {
            await Dialogs.MessageAsync(Root, title, "Draw a closed sketch first (Sketch tab).");
            return;
        }
        if (await Dialogs.ExtrudeAsync(Root, cut, sketch) is not { } e) return;
        ExitSketch();
        var reply = _model.Command(new JsonObject
        {
            ["op"] = cut ? "cut_extrude" : "extrude", ["sketch"] = sketch, ["depth"] = e.Depth, ["mid_plane"] = e.MidPlane, ["reverse"] = e.Reverse,
        });
        await AfterFeatureAsync(reply, title);
    }

    private async Task RevolveAsync(bool cut)
    {
        if (_model == null) return;
        var title = cut ? "Revolved Cut" : "Revolved Boss/Base";
        if (FeatureSketch() is not { } sketch)
        {
            await Dialogs.MessageAsync(Root, title, "Draw a closed sketch with a centerline first (Sketch tab).");
            return;
        }
        if (await Dialogs.RevolveAsync(Root, cut, sketch) is not { } angle) return;
        ExitSketch();
        var reply = _model.Command(new JsonObject { ["op"] = cut ? "cut_revolve" : "revolve", ["sketch"] = sketch, ["angle"] = angle });
        await AfterFeatureAsync(reply, title);
    }

    private async Task AfterFeatureAsync(JsonObject reply, string title)
    {
        _model?.Refresh();
        if (reply["ok"]?.GetValue<bool>() != true)
            await Dialogs.MessageAsync(Root, title, reply["error"]?.GetValue<string>() ?? reply.ToJsonString());
        else if (reply["rebuild_ok"]?.GetValue<bool>() == false)
            await Dialogs.MessageAsync(Root, title, string.Join("\n", (reply["rebuild_errors"] as JsonArray ?? new()).Select(x => x?.ToString())));
    }


    private void RunCommands(string example)
    {
        if (_model == null) return;
        _ = Dialogs.RunCommandsAsync(Root, _model, example);
    }

    private async Task EdgeFeatureAsync(string op)
    {
        if (_model == null) return;
        var result = await Dialogs.EdgeFeatureAsync(Root, op);
        if (result is not { } r) return;
        var size = op == "fillet" ? "radius" : "distance";
        var reply = _model.Execute($"{{\"op\":\"{op}\",\"{size}\":{r.Size.ToString(System.Globalization.CultureInfo.InvariantCulture)},\"edges\":\"{r.Edges}\"}}");
        if (reply.Contains("\"ok\":false")) await Dialogs.MessageAsync(Root, op == "fillet" ? "Fillet" : "Chamfer", reply);
    }

    private async Task ShowMassPropertiesAsync()
    {
        if (_model == null) return;
        var m = _model.Mass;
        var (name, density) = _model.Material;
        await Dialogs.MessageAsync(Root, "Mass Properties",
            $"Configuration: {_model.ActiveConfiguration}\nMaterial: {(name.Length == 0 ? "<not specified>" : name)}\n" +
            $"Density = {density:0} kg/m³\n\nMass = {m.Mass:0.000} g\nVolume = {m.Volume:0.00} mm³\nSurface area = {m.Area:0.00} mm²\n" +
            $"Centre of mass (mm): X = {m.Cx:0.000}, Y = {m.Cy:0.000}, Z = {m.Cz:0.000}");
    }

    private async Task EditMaterialAsync()
    {
        if (_model == null) return;
        if (await Dialogs.MaterialAsync(Root, _model.Material.Name) is { } material) _model.SetMaterial(material.Name, material.Density);
    }

    // Drawings

    private async Task PreviewDrawingAsync()
    {
        if (_model == null) return;
        var svg = _model.DrawingSvg("A3");
        var path = Path.Combine(Path.GetTempPath(), $"stawn-{Guid.NewGuid():N}.svg");
        await File.WriteAllTextAsync(path, svg);
        await Dialogs.DrawingPreviewAsync(Root, path, $"{_model.Name} - Sheet1 (A3)");
    }

    private async Task ExportDrawingAsync(string format)
    {
        if (_model == null) return;
        var path = await Dialogs.SavePathAsync(this, _model.Name, format.ToUpperInvariant(), "." + format);
        if (path != null && !_model.ExportDrawing(path, "A3", format))
            await Dialogs.MessageAsync(Root, "Export failed", $"Could not write the drawing to {path}.");
    }
}
