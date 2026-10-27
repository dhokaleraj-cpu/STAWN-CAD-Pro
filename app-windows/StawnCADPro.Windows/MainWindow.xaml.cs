using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stawn.Model;
using Stawn.Views;
using Windows.Foundation;
using Windows.Storage.Pickers;

namespace Stawn;

/// <summary>
/// Part window: menus and quick-access buttons, CommandManager, FeatureManager / PropertyManager
/// pane, graphics area with the heads-up toolbar and confirmation corner, task pane and status
/// bar. Commands that need options open a PropertyManager page in the left pane with OK and
/// Cancel; sketches are drawn on their plane inside the graphics area.
/// </summary>
public sealed partial class MainWindow : Window
{
    private PartModel? _model;
    private Button? _exitSketchButton;
    private string _tool = "select";

    public MainWindow(PartModel? model)
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1600, 960));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "StawnCADPro.ico"));
        BuildQuickAccess();
        BuildCommandManager();
        BuildManagerTabs();
        BuildViewMenus();
        BuildTaskTabs();
        BuildModifyBox();
        Welcome.NewPart += () => Load(PartModel.Empty());
        Welcome.OpenSample += () => Load(PartModel.SampleShaft());
        Welcome.OpenPart += () => _ = OpenAsync();
        FeatureTreePanel.EditFeature += name =>
        {
            if (_model?.FeatureList.FirstOrDefault(f => f.Name == name).Type == "Sketch") EditSketch(name);
            else ShowManager(PropertyPanel);
        };
        FeatureTreePanel.SketchOnPlane += StartSketch;
        View3D.SketchChanged += OnSketchChanged;
        View3D.ViewNameChanged += name => ViewNameText.Text = "*" + name;
        Closed += (_, _) => _model?.Dispose();
        if (model != null) Load(model); else ShowWelcome();
    }

    private void ShowWelcome()
    {
        Welcome.Visibility = Visibility.Visible;
        SetDocumentCommandsEnabled(false);
        TitleText.Text = "";
        StatusLeft.Text = "STAWN CAD Pro";
        StatusEditing.Text = StatusMass.Text = StatusDefinition.Text = StatusCoordinates.Text = "";
    }

    private void Load(PartModel model)
    {
        LeaveSketchMode();
        ClosePage();
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
        if (_model == null) return;
        View3D.SetMesh(_model.Mesh);
        FeatureTreePanel.Refresh();
        PropertyPanel.Refresh();
        ConfigurationPanel.Refresh();
        PropertiesPane.Refresh();
        var file = _model.FilePath == null ? _model.Name : Path.GetFileName(_model.FilePath);
        TitleText.Text = $"{file}{(_model.IsDirty ? " *" : "")}";
        Title = $"STAWN CAD Pro - {TitleText.Text}";
        UpdateStatus();
        CommandManager.RefreshEnabled();
    }

    private void UpdateStatus(string? prompt = null)
    {
        if (_model == null) return;
        StatusLeft.Text = prompt ?? (_model.RebuildErrors.Count == 0 ? "STAWN CAD Pro" : $"Rebuild errors: {string.Join("; ", _model.RebuildErrors)}");
        StatusMass.Text = $"{_model.Mass.Mass:0.000} g";
        if (View3D.Sketching)
        {
            StatusEditing.Text = $"Editing {View3D.SketchName}";
            StatusDefinition.Text = View3D.SketchStatus;
            StatusDefinition.Foreground = new SolidColorBrush(View3D.SketchStatus switch
            {
                "Fully Defined" => Colors.Black,
                "Under Defined" => Windows.UI.Color.FromArgb(255, 0, 70, 220),
                _ => Colors.Red,
            });
            StatusCoordinates.Text = View3D.SketchCursor is { } c
                ? string.Format(CultureInfo.InvariantCulture, "{0:0.##}mm   {1:0.##}mm   0mm", c.X, c.Y) : "";
        }
        else
        {
            StatusEditing.Text = $"Editing Part  ·  {_model.ActiveConfiguration}";
            StatusDefinition.Text = "";
            StatusCoordinates.Text = "";
        }
    }

    private void SetDocumentCommandsEnabled(bool on)
    {
        foreach (var item in new MenuFlyoutItemBase[] { SaveItem, SaveAsItem, ImportItem, ExportItem }) item.IsEnabled = on;
        CommandManager.IsEnabled = on;
        PropertiesPane.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        TaskPaneColumn.Width = on ? new GridLength(300) : new GridLength(0);
    }

    // ---------------------------------------------------------------- Toolbars

    private void BuildQuickAccess()
    {
        foreach (var (icon, tip, action) in new (string, string, Action)[]
                 {
                     ("new", "New (Ctrl+N)", () => OpenInWindow(PartModel.Empty())),
                     ("open", "Open (Ctrl+O)", () => _ = OpenAsync()),
                     ("save", "Save (Ctrl+S)", () => _ = SaveAsync(false)),
                     ("rebuild", "Rebuild (Ctrl+B)", () => _model?.Refresh()),
                     ("options", "Run Commands", () => RunCommands()),
                 })
        {
            var b = SmallButton(icon, tip, action);
            QuickAccess.Children.Add(b);
        }
        var rebuild = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.B, Modifiers = Windows.System.VirtualKeyModifiers.Control };
        rebuild.Invoked += (_, e) => { _model?.Refresh(); e.Handled = true; };
        Root.KeyboardAccelerators.Add(rebuild);
    }

    private void BuildCommandManager()
    {
        bool HasPart() => _model != null;
        bool HasSketch() => FeatureSketch() != null;

        var features = CommandManager.AddTab("Features");
        CommandManager.Add(features, "Extruded Boss/Base", "extrude", () => ShowExtrudePage(false), "Extrude a sketch to add material", HasSketch);
        CommandManager.Add(features, "Revolved Boss/Base", "revolve", () => ShowRevolvePage(false), "Revolve a sketch about a centerline", HasSketch);
        Ribbon.AddSeparator(features);
        CommandManager.Add(features, "Extruded Cut", "cut-extrude", () => ShowExtrudePage(true), "Extrude a sketch to remove material", HasSketch);
        CommandManager.Add(features, "Revolved Cut", "cut-revolve", () => ShowRevolvePage(true), "Revolve a sketch to remove material", HasSketch);
        Ribbon.AddSeparator(features);
        CommandManager.Add(features, "Fillet", "fillet", () => ShowEdgePage("fillet"), "Round the edges of the body", HasPart);
        CommandManager.Add(features, "Chamfer", "chamfer", () => ShowEdgePage("chamfer"), "Bevel the edges of the body", HasPart);
        Ribbon.AddSeparator(features);
        CommandManager.Add(features, "Import STEP", "import", () => _ = ImportStepAsync());

        var sketch = CommandManager.AddTab("Sketch");
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
        CommandManager.AddMenu(sketch, "Sketch", "sketch", planes, "Sketch on a plane, or edit the selected sketch");
        _exitSketchButton = CommandManager.Add(sketch, "Exit Sketch", "exit-sketch", ExitSketch, "Close the sketch and rebuild", () => View3D.Sketching);
        CommandManager.Add(sketch, "Smart Dimension", "dimension", () => SketchTool("dimension"), "Dimension the selected geometry");
        Ribbon.AddSeparator(sketch);
        foreach (var (label, icon, tool) in new[] { ("Line", "line", "line"), ("Centerline", "centerline", "centerline"),
                                                     ("Corner Rectangle", "rectangle", "rectangle"), ("Circle", "circle", "circle"),
                                                     ("Centerpoint Arc", "arc", "arc") })
            CommandManager.Add(sketch, label, icon, () => SketchTool(tool));
        Ribbon.AddSeparator(sketch);
        CommandManager.Add(sketch, "Select", "select", () => SketchTool("select"), "Select sketch geometry to add relations or delete it");
        CommandManager.Add(sketch, "Add Relation", "relation", () => SketchTool("select"), "Select two or more entities, then choose the relation");
        CommandManager.Add(sketch, "Normal To", "normal-to", () => View3D.NormalTo(), "Look straight at the sketch (Ctrl+8)", () => View3D.Sketching);

        var evaluate = CommandManager.AddTab("Evaluate");
        CommandManager.Add(evaluate, "Mass Properties", "mass", () => _ = ShowMassPropertiesAsync());
        CommandManager.Add(evaluate, "Material", "material", () => _ = EditMaterialAsync());
        CommandManager.Add(evaluate, "Rebuild", "rebuild", () => _model?.Refresh());

        var drawing = CommandManager.AddTab("Drawing");
        CommandManager.Add(drawing, "Make Drawing", "drawing", () => _ = PreviewDrawingAsync(), "Standard three views on an A3 sheet");
        CommandManager.Add(drawing, "Save as PDF", "pdf", () => _ = ExportDrawingAsync("pdf"));
        CommandManager.Add(drawing, "Save as DXF", "dxf", () => _ = ExportDrawingAsync("dxf"));
        CommandManager.Add(drawing, "Save as SVG", "svg", () => _ = ExportDrawingAsync("svg"));
    }

    private void BuildManagerTabs()
    {
        foreach (var (icon, tip, panel) in new (string, string, UIElement)[]
                 {
                     ("part", "FeatureManager design tree", FeatureTreePanel),
                     ("options", "PropertyManager", PropertyPanel),
                     ("bodies", "ConfigurationManager", ConfigurationPanel),
                 })
            ManagerTabs.Children.Add(SmallButton(icon, tip, () => { ClosePage(); ShowManager(panel); }));
    }

    private void ShowManager(UIElement panel)
    {
        FeatureTreePanel.Visibility = panel == FeatureTreePanel ? Visibility.Visible : Visibility.Collapsed;
        PropertyPanel.Visibility = panel == PropertyPanel ? Visibility.Visible : Visibility.Collapsed;
        ConfigurationPanel.Visibility = panel == ConfigurationPanel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildTaskTabs()
    {
        TaskTabs.Children.Add(SmallButton("annotations", "Custom Properties", () =>
        {
            bool open = TaskPaneColumn.Width.Value > 0;
            TaskPaneColumn.Width = new GridLength(open ? 0 : 300);
            PropertiesPane.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
        }));
        TaskTabs.Children.Add(SmallButton("material", "Material", () => _ = EditMaterialAsync()));
    }

    private static readonly (DisplayStyle Style, string Label)[] DisplayStyles =
        { (DisplayStyle.ShadedWithEdges, "Shaded With Edges"), (DisplayStyle.Shaded, "Shaded"), (DisplayStyle.Wireframe, "Wireframe") };

    private void BuildViewMenus()
    {
        foreach (var item in OrientationItems()) OrientationMenu.Items.Add(item);
        foreach (var item in DisplayStyleItems()) DisplayStyleMenu.Items.Add(item);
        HeadsUp.Children.Add(SmallButton("zoom-fit", "Zoom to Fit (F)", () => View3D.ZoomToFit()));
        var orientation = SmallButton("view", "View Orientation", () => { });
        orientation.Flyout = Flyout(OrientationItems());
        HeadsUp.Children.Add(orientation);
        var style = SmallButton("display", "Display Style", () => { });
        style.Flyout = Flyout(DisplayStyleItems());
        HeadsUp.Children.Add(style);
        HeadsUp.Children.Add(SmallButton("normal-to", "Normal To (Ctrl+8)", () => View3D.NormalTo()));
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

    private static Button SmallButton(string icon, string tip, Action action)
    {
        var b = new Button
        {
            Content = Icons.Get(icon, 20), Padding = new Thickness(5, 3, 5, 3), BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
        };
        b.Click += (_, _) => action();
        ToolTipService.SetToolTip(b, tip);
        return b;
    }

    // ---------------------------------------------------------------- Confirmation corner

    private void SetConfirmationCorner(Action? ok, Action? cancel, string okTip = "OK")
    {
        ConfirmationCorner.Children.Clear();
        if (ok != null) ConfirmationCorner.Children.Add(CornerButton(View3D.Sketching && PageHost.Content == null ? "exit-sketch" : "ok", okTip, ok));
        if (cancel != null) ConfirmationCorner.Children.Add(CornerButton("cancel", "Cancel", cancel));
        ConfirmationCorner.Visibility = ok != null || cancel != null ? Visibility.Visible : Visibility.Collapsed;
    }

    private static Button CornerButton(string icon, string tip, Action action)
    {
        var b = new Button
        {
            Content = Icons.Get(icon, 28), Padding = new Thickness(6), CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 255, 255, 255)),
        };
        b.Click += (_, _) => action();
        ToolTipService.SetToolTip(b, tip);
        return b;
    }

    // ---------------------------------------------------------------- PropertyManager pages

    private void ShowPage(PropertyManagerPage page)
    {
        PageHost.Content = page;
        PageHost.Visibility = Visibility.Visible;
        SetConfirmationCorner(page.Ok, page.Cancel);
    }

    private void ClosePage()
    {
        PageHost.Content = null;
        PageHost.Visibility = Visibility.Collapsed;
        if (View3D.Sketching) SetConfirmationCorner(ExitSketch, null, "Exit Sketch");
        else SetConfirmationCorner(null, null);
    }

    private static NumberBox Number(string header, double value, double min, double step) => new()
    {
        Header = header, Value = value, Minimum = min, SmallChange = step, LargeChange = step * 10, AcceptsExpression = true,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
    };

    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12 };

    private void ShowExtrudePage(bool cut)
    {
        if (_model == null || FeatureSketch() is not { } sketch) return;
        var title = cut ? "Cut-Extrude" : "Boss-Extrude";
        var condition = new ComboBox { Header = "End Condition", ItemsSource = new[] { "Blind", "Mid Plane" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var reverse = new CheckBox { Content = "Reverse Direction" };
        var depth = Number("Depth (mm)", 10, 0.001, 1);
        PropertyManagerPage? page = null;
        page = new PropertyManagerPage(title, cut ? "cut-extrude" : "extrude", () =>
        {
            if (double.IsNaN(depth.Value) || depth.Value <= 0) { page!.SetMessage("Enter a depth greater than zero."); return; }
            RunFeature(title, new JsonObject
            {
                ["op"] = cut ? "cut_extrude" : "extrude", ["sketch"] = sketch, ["depth"] = depth.Value,
                ["mid_plane"] = condition.SelectedIndex == 1, ["reverse"] = reverse.IsChecked == true,
            }, page!);
        }, ClosePage);
        page.SetMessage($"Extrudes {sketch} normal to its plane.");
        page.AddGroup("From").Children.Add(Note("Sketch Plane"));
        var d1 = page.AddGroup("Direction 1");
        d1.Children.Add(condition);
        d1.Children.Add(reverse);
        d1.Children.Add(depth);
        page.AddGroup("Selected Contours").Children.Add(Note(sketch));
        ShowPage(page);
    }

    private void ShowRevolvePage(bool cut)
    {
        if (_model == null || FeatureSketch() is not { } sketch) return;
        var title = cut ? "Cut-Revolve" : "Revolve";
        var angle = Number("Angle (°)", 360, 0.1, 15);
        angle.Maximum = 360;
        PropertyManagerPage? page = null;
        page = new PropertyManagerPage(title, cut ? "cut-revolve" : "revolve", () =>
        {
            if (double.IsNaN(angle.Value)) return;
            RunFeature(title, new JsonObject { ["op"] = cut ? "cut_revolve" : "revolve", ["sketch"] = sketch, ["angle"] = angle.Value }, page!);
        }, ClosePage);
        page.SetMessage($"Revolves {sketch} about its first centerline.");
        page.AddGroup("Axis of Revolution").Children.Add(Note("First centerline of the sketch"));
        var d1 = page.AddGroup("Direction 1");
        d1.Children.Add(Note("Blind"));
        d1.Children.Add(angle);
        page.AddGroup("Selected Contours").Children.Add(Note(sketch));
        ShowPage(page);
    }

    private void ShowEdgePage(string op)
    {
        if (_model == null) return;
        bool fillet = op == "fillet";
        var title = fillet ? "Fillet" : "Chamfer";
        var size = Number(fillet ? "Radius (mm)" : "Distance (mm)", fillet ? 0.5 : 0.3, 0.001, 0.1);
        var edges = new RadioButtons { ItemsSource = new[] { "All edges", "Circular edges", "Straight edges" }, SelectedIndex = 1 };
        PropertyManagerPage? page = null;
        page = new PropertyManagerPage(title, op, () =>
        {
            if (double.IsNaN(size.Value) || size.Value <= 0) { page!.SetMessage("Enter a size greater than zero."); return; }
            RunFeature(title, new JsonObject
            {
                ["op"] = op, [fillet ? "radius" : "distance"] = size.Value,
                ["edges"] = new[] { "all", "circular", "linear" }[Math.Max(0, edges.SelectedIndex)],
            }, page!);
        }, ClosePage);
        page.AddGroup(fillet ? "Fillet Type" : "Chamfer Type").Children.Add(Note(fillet ? "Constant Size Fillet" : "Distance Distance (equal)"));
        var items = page.AddGroup(fillet ? "Items To Fillet" : "Items To Chamfer");
        items.Children.Add(edges);
        items.Children.Add(size);
        ShowPage(page);
    }

    /// <summary>Closes any open sketch, adds the feature and rebuilds; errors stay on the page.</summary>
    private void RunFeature(string title, JsonObject command, PropertyManagerPage page)
    {
        if (_model == null) return;
        if (View3D.Sketching) ExitSketch();
        var reply = _model.Command(command);
        _model.Refresh();
        if (reply["ok"]?.GetValue<bool>() != true)
        {
            page.SetMessage(reply["error"]?.GetValue<string>() ?? $"{title} failed.");
            ShowPage(page);
            return;
        }
        if (reply["rebuild_ok"]?.GetValue<bool>() == false)
        {
            page.SetMessage(string.Join("\n", (reply["rebuild_errors"] as JsonArray ?? new()).Select(x => x?.ToString())));
            ShowPage(page);
            return;
        }
        ClosePage();
        ShowManager(FeatureTreePanel);
    }

    // ---------------------------------------------------------------- Sketch mode

    private string? SelectedSketch() =>
        _model?.SelectedFeature is { } f && _model.FeatureList.FirstOrDefault(x => x.Name == f).Type == "Sketch" ? f : null;

    /// <summary>The sketch a feature uses: the open one, else the selected one, else the last one.</summary>
    private string? FeatureSketch()
    {
        if (View3D.Sketching) return View3D.SketchName;
        if (SelectedSketch() is { } s) return s;
        return _model?.FeatureList.LastOrDefault(f => f.Type == "Sketch").Name;
    }

    private void StartSketch(string plane)
    {
        if (_model == null) return;
        if (View3D.Sketching) ExitSketch();
        var reply = _model.Command(new JsonObject { ["op"] = "create_sketch", ["plane"] = plane });
        if (reply["sketch"]?.GetValue<string>() is { } name) EditSketch(name);
    }

    private void EditSketch(string name)
    {
        if (_model == null) return;
        if (View3D.Sketching) ExitSketch();
        var state = _model.Command(new JsonObject { ["op"] = "edit_sketch", ["sketch"] = name });
        if (state["ok"]?.GetValue<bool>() != true) return;
        _model.Refresh();  // shows a new sketch in the feature tree
        _model.Select(name);
        ClosePage();
        View3D.BeginSketch(_model, state);
        CommandManager.Select("Sketch");
        CommandManager.RefreshEnabled();
        SetConfirmationCorner(ExitSketch, null, "Exit Sketch");
        SketchTool("line");
    }

    /// <summary>Closes the sketch and rebuilds the part, so features that use it update.</summary>
    private void ExitSketch()
    {
        if (_model == null || !View3D.Sketching) return;
        HideModifyBox();
        _model.Command(new JsonObject { ["op"] = "close_sketch" });
        LeaveSketchMode();
        _model.Refresh();
    }

    private void LeaveSketchMode()
    {
        if (!View3D.Sketching) return;
        View3D.EndSketch();
        ClosePage();
        ShowManager(FeatureTreePanel);
        CommandManager.RefreshEnabled();
    }

    private void SketchTool(string tool)
    {
        if (!View3D.Sketching)
        {
            if (tool != "select")
                _ = Dialogs.MessageAsync(Root, "Sketch", "Start a sketch first: Sketch > Front, Top or Right Plane, or double-click a plane or sketch in the tree.");
            return;
        }
        View3D.SetSketchTool(tool);
    }

    private static readonly System.Collections.Generic.Dictionary<string, (string Title, string Icon)> ToolPages = new()
    {
        ["line"] = ("Insert Line", "line"), ["centerline"] = ("Insert Centerline", "centerline"),
        ["rectangle"] = ("Rectangle", "rectangle"), ["circle"] = ("Circle", "circle"), ["arc"] = ("Arc", "arc"),
        ["dimension"] = ("Dimension", "dimension"),
    };

    private string? _pageKey;

    private void OnSketchChanged(string ev, JsonObject state, Point at)
    {
        var prompt = state["prompt"]?.GetValue<string>() ?? "";
        _tool = state["tool"]?.GetValue<string>() ?? "select";
        UpdateStatus(prompt);
        if (ev == "move") return;  // the page and relations only change on clicks and keys

        // Left pane: Add Relations for a selection, the tool's page while drawing, else the tree.
        var relations = (state["relations"] as JsonArray ?? new JsonArray()).Select(r => r!.GetValue<string>()).ToList();
        var selection = (state["selection"] as JsonArray)?.Count ?? 0;
        var key = selection > 0 ? "relations:" + string.Join(",", relations) + selection : _tool;
        if (key != _pageKey)
        {
            _pageKey = key;
            if (selection > 0)
            {
                var page = new PropertyManagerPage("Properties", "relation", () => View3D.SketchEscape(), null);
                var add = page.AddGroup("Add Relations");
                if (relations.Count == 0) add.Children.Add(Note("Select another entity to see the relations that apply."));
                for (int i = 0; i < relations.Count; i++)
                {
                    int index = i;
                    var b = new Button { Content = relations[i], HorizontalAlignment = HorizontalAlignment.Stretch };
                    b.Click += (_, _) => View3D.AddRelation(index);
                    add.Children.Add(b);
                }
                page.AddGroup("Options").Children.Add(Note("Delete removes the selected geometry. Esc clears the selection."));
                ShowPage(page);
            }
            else if (ToolPages.TryGetValue(_tool, out var t))
            {
                var page = new PropertyManagerPage(t.Title, t.Icon, () => View3D.SetSketchTool("select"), null);
                page.SetMessage(prompt);
                page.AddGroup("Options").Children.Add(Note(_tool switch
                {
                    "line" => "Click to place points; the chain ends on its start point or with Esc. Lines snap to points and become horizontal or vertical near those directions.",
                    "dimension" => "Click a line, circle or arc, or two points or two lines, then type the value.",
                    _ => "Click to place points. Esc cancels.",
                }));
                ShowPage(page);
            }
            else
            {
                ClosePage();
                ShowManager(FeatureTreePanel);
            }
        }
        else if (PageHost.Content is PropertyManagerPage current && ToolPages.ContainsKey(_tool))
        {
            current.SetMessage(prompt);
        }
        if (ev is "relation" or "dimension" or "delete") FeatureTreePanel.Refresh();

        // Smart Dimension: the Modify box opens at the cursor as soon as the geometry is picked.
        if (ev == "click" && state["dimension_request"] is JsonObject request) ShowModifyBox(request, at);
    }

    // ---------------------------------------------------------------- Modify box

    private JsonArray _modifyOptions = new();

    private void BuildModifyBox()
    {
        ModifyOk.Content = Icons.Get("ok", 16);
        ModifyCancel.Content = Icons.Get("cancel", 16);
        ModifyOk.Click += (_, _) => CommitModify();
        ModifyCancel.Click += (_, _) => { HideModifyBox(); View3D.SketchEscape(); };
        ModifyOptions.SelectionChanged += (_, _) =>
        {
            int i = Math.Max(0, ModifyOptions.SelectedIndex);
            if (i < _modifyOptions.Count) ModifyValue.Value = _modifyOptions[i]!["value"]!.GetValue<double>();
        };
        ModifyValue.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter) { CommitModify(); e.Handled = true; }
            else if (e.Key == Windows.System.VirtualKey.Escape) { HideModifyBox(); View3D.SketchEscape(); e.Handled = true; }
        };
    }

    private void ShowModifyBox(JsonObject request, Point at)
    {
        _modifyOptions = request["options"] as JsonArray ?? new JsonArray();
        if (_modifyOptions.Count == 0) return;
        ModifyOptions.ItemsSource = _modifyOptions.Select(o => o!["label"]!.GetValue<string>()).ToList();
        ModifyOptions.Visibility = _modifyOptions.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        ModifyOptions.SelectedIndex = 0;
        ModifyName.Text = _modifyOptions[0]!["unit"]?.GetValue<string>() == "deg" ? "degrees" : "mm";
        ModifyValue.Value = _modifyOptions[0]!["value"]!.GetValue<double>();
        Canvas.SetLeft(ModifyBox, Math.Min(at.X + 16, Math.Max(0, View3D.ActualWidth - 220)));
        Canvas.SetTop(ModifyBox, Math.Min(at.Y + 16, Math.Max(0, View3D.ActualHeight - 160)));
        ModifyBox.Visibility = Visibility.Visible;
        ModifyValue.Focus(FocusState.Programmatic);
    }

    private void CommitModify()
    {
        if (double.IsNaN(ModifyValue.Value)) return;
        int option = Math.Max(0, ModifyOptions.SelectedIndex);
        double value = ModifyValue.Value;
        HideModifyBox();
        View3D.AddDimension(option, value);
        View3D.Focus(FocusState.Programmatic);
    }

    private void HideModifyBox() => ModifyBox.Visibility = Visibility.Collapsed;

    // ---------------------------------------------------------------- Demo (CI screenshots)

    /// <summary>Maximises the window and, for "sketch", draws and dimensions a rectangle on the Front plane.</summary>
    public void RunDemo(string what)
    {
        (AppWindow.Presenter as Microsoft.UI.Windowing.OverlappedPresenter)?.Maximize();
        if (what != "sketch") return;
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(3);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            StartSketch("Front");
            SketchTool("rectangle");
            View3D.SketchClick(10, 12);
            View3D.SketchClick(40, 30);
            SketchTool("dimension");
            View3D.SketchClick(25, 12);
            View3D.AddDimension(0, 30);
            View3D.SketchClick(40, 21);
            SketchTool("line");
            View3D.SketchMove(55, 40);
        };
        timer.Start();
    }

    // ---------------------------------------------------------------- Menus

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
    private void OnNormalTo(object sender, RoutedEventArgs e) => View3D.NormalTo();
    private void OnSketchFront(object sender, RoutedEventArgs e) => StartSketch("Front");
    private void OnSketchTop(object sender, RoutedEventArgs e) => StartSketch("Top");
    private void OnSketchRight(object sender, RoutedEventArgs e) => StartSketch("Right");
    private void OnInsertExtrude(object sender, RoutedEventArgs e) => ShowExtrudePage(false);
    private void OnInsertRevolve(object sender, RoutedEventArgs e) => ShowRevolvePage(false);
    private void OnInsertCutExtrude(object sender, RoutedEventArgs e) => ShowExtrudePage(true);
    private void OnInsertCutRevolve(object sender, RoutedEventArgs e) => ShowRevolvePage(true);
    private void OnInsertFillet(object sender, RoutedEventArgs e) => ShowEdgePage("fillet");
    private void OnInsertChamfer(object sender, RoutedEventArgs e) => ShowEdgePage("chamfer");
    private void OnMassProperties(object sender, RoutedEventArgs e) => _ = ShowMassPropertiesAsync();
    private void OnRunCommands(object sender, RoutedEventArgs e) => RunCommands();
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
        if (View3D.Sketching) ExitSketch();
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

    private void RunCommands()
    {
        if (_model == null) return;
        _ = Dialogs.RunCommandsAsync(Root, _model, Examples.Sketch);
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

    // ---------------------------------------------------------------- Drawings

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
