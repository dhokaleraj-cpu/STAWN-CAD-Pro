using System;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Stawn.Views;
using Windows.System;
using Windows.UI;

namespace Stawn;

/// <summary>
/// The part window's layout, built in code so the compiler checks it and a failure names its
/// line: menus and quick-access buttons, CommandManager, the FeatureManager / PropertyManager
/// pane, graphics area, task pane and status bar.
/// </summary>
public sealed partial class MainWindow
{
    private Grid Root = null!;
    private Grid TitleBar = null!;
    private TextBlock TitleText = null!;
    private MenuFlyoutItem SaveItem = null!, SaveAsItem = null!, ImportItem = null!;
    private MenuFlyoutSubItem ExportItem = null!, OrientationMenu = null!, DisplayStyleMenu = null!;
    private StackPanel QuickAccess = null!;
    private Ribbon CommandManager = null!;
    private ColumnDefinition TaskPaneColumn = null!;
    private StackPanel ManagerTabs = null!;
    private FeatureTree FeatureTreePanel = null!;
    private PropertyManagerPanel PropertyPanel = null!;
    private ConfigurationManagerPanel ConfigurationPanel = null!;
    private ContentControl PageHost = null!;
    private Viewport View3D = null!;
    private StackPanel HeadsUp = null!;
    private TextBlock ViewNameText = null!;
    private StackPanel ConfirmationCorner = null!;
    private Border ModifyBox = null!;
    private TextBlock ModifyName = null!;
    private RadioButtons ModifyOptions = null!;
    private Button ModifyOk = null!, ModifyCancel = null!;
    private NumberBox ModifyValue = null!;
    private WelcomePanel Welcome = null!;
    private CustomPropertiesPane PropertiesPane = null!;
    private StackPanel TaskTabs = null!;
    private TextBlock StatusLeft = null!, StatusCoordinates = null!, StatusDefinition = null!, StatusEditing = null!, StatusMass = null!;

    private static SolidColorBrush Brush(byte r, byte g, byte b, byte a = 255) => new(Color.FromArgb(a, r, g, b));
    private static readonly SolidColorBrush PaneLine = Brush(0xC9, 0xCE, 0xD6);
    private static readonly SolidColorBrush BarLine = Brush(0xD5, 0xD9, 0xDE);

    private static T At<T>(T e, int row = 0, int column = 0) where T : FrameworkElement
    {
        Grid.SetRow(e, row);
        Grid.SetColumn(e, column);
        return e;
    }

    private void BuildLayout()
    {
        Root = new Grid { Background = Brush(0xF3, 0xF4, 0xF6), RequestedTheme = ElementTheme.Light };
        foreach (var h in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            Root.RowDefinitions.Add(new RowDefinition { Height = h });

        // Title bar (draggable)
        TitleBar = new Grid { Height = 32, Padding = new Thickness(10, 0, 140, 0), Background = Brush(0xE4, 0xE7, 0xEB) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(new Image
        {
            Width = 18, Height = 18,
            Source = new BitmapImage(new Uri(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "StawnCADPro.png"))),
        });
        brand.Children.Add(new TextBlock { Text = "STAWN CAD Pro", FontWeight = FontWeights.SemiBold, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        TitleBar.Children.Add(brand);
        TitleText = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        TitleBar.Children.Add(TitleText);
        Root.Children.Add(At(TitleBar, 0));

        // Menus and quick-access buttons
        var menuRow = new Grid { BorderBrush = BarLine, BorderThickness = new Thickness(0, 0, 0, 1) };
        menuRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        menuRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        menuRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        menuRow.Children.Add(At(BuildMenuBar()));
        menuRow.Children.Add(At(new Border { Width = 1, Margin = new Thickness(6), Background = BarLine }, 0, 1));
        QuickAccess = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        menuRow.Children.Add(At(QuickAccess, 0, 2));
        Root.Children.Add(At(menuRow, 1));

        CommandManager = new Ribbon();
        Root.Children.Add(At(CommandManager, 2));

        // Main area
        var main = new Grid();
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        TaskPaneColumn = new ColumnDefinition { Width = new GridLength(300) };
        main.ColumnDefinitions.Add(TaskPaneColumn);
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left pane: FeatureManager tree, PropertyManager, ConfigurationManager and command pages
        var left = new Grid { Background = new SolidColorBrush(Colors.White), BorderBrush = PaneLine, BorderThickness = new Thickness(0, 0, 1, 0) };
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        ManagerTabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Padding = new Thickness(4, 4, 4, 2) };
        left.Children.Add(At(new Border { Child = ManagerTabs, Background = Brush(0xEE, 0xF0, 0xF3), BorderBrush = BarLine, BorderThickness = new Thickness(0, 0, 0, 1) }));
        FeatureTreePanel = new FeatureTree();
        PropertyPanel = new PropertyManagerPanel { Visibility = Visibility.Collapsed, Background = new SolidColorBrush(Colors.White) };
        ConfigurationPanel = new ConfigurationManagerPanel { Visibility = Visibility.Collapsed, Background = new SolidColorBrush(Colors.White) };
        PageHost = new ContentControl
        {
            Visibility = Visibility.Collapsed, Background = Brush(0xF7, 0xF8, 0xFA),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        foreach (FrameworkElement e in new FrameworkElement[] { FeatureTreePanel, PropertyPanel, ConfigurationPanel, PageHost })
            left.Children.Add(At(e, 1));
        main.Children.Add(At(left, 0, 0));

        // Graphics area
        var graphics = new Grid();
        View3D = new Viewport();
        graphics.Children.Add(View3D);
        HeadsUp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        graphics.Children.Add(new Border
        {
            Child = HeadsUp, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(4),
            Background = Brush(0xFF, 0xFF, 0xFF, 0xE6), BorderBrush = PaneLine, BorderThickness = new Thickness(1),
        });
        ViewNameText = new TextBlock
        {
            Text = "*Isometric", Margin = new Thickness(96, 0, 0, 14), FontSize = 12, Foreground = Brush(0x30, 0x34, 0x3A),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false,
        };
        graphics.Children.Add(ViewNameText);
        ConfirmationCorner = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 8, 10, 0), Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
        };
        graphics.Children.Add(ConfirmationCorner);
        graphics.Children.Add(BuildModifyLayer());
        Welcome = new WelcomePanel { Visibility = Visibility.Collapsed };
        graphics.Children.Add(Welcome);
        main.Children.Add(At(graphics, 0, 1));

        // Task pane and its tabs
        PropertiesPane = new CustomPropertiesPane { Background = new SolidColorBrush(Colors.White), BorderBrush = PaneLine, BorderThickness = new Thickness(1, 0, 0, 0) };
        main.Children.Add(At(PropertiesPane, 0, 2));
        TaskTabs = new StackPanel { Width = 34, Spacing = 2, Padding = new Thickness(2, 6, 2, 6) };
        main.Children.Add(At(new Border { Child = TaskTabs, Background = Brush(0xE8, 0xEA, 0xED), BorderBrush = PaneLine, BorderThickness = new Thickness(1, 0, 0, 0) }, 0, 3));
        Root.Children.Add(At(main, 3));

        // Status bar
        var status = new Grid { Padding = new Thickness(10, 3, 10, 3), Background = Brush(0xE4, 0xE7, 0xEB), BorderBrush = PaneLine, BorderThickness = new Thickness(0, 1, 0, 0) };
        StatusLeft = new TextBlock { FontSize = 12 };
        status.Children.Add(StatusLeft);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, HorizontalAlignment = HorizontalAlignment.Right };
        StatusCoordinates = new TextBlock { FontSize = 12 };
        StatusDefinition = new TextBlock { FontSize = 12 };
        StatusEditing = new TextBlock { FontSize = 12 };
        StatusMass = new TextBlock { FontSize = 12 };
        foreach (var t in new[] { StatusCoordinates, StatusDefinition, StatusEditing, StatusMass }) right.Children.Add(t);
        var units = new TextBlock { Text = "MMGS", FontSize = 12 };
        ToolTipService.SetToolTip(units, "Millimetre, gram, second");
        right.Children.Add(units);
        status.Children.Add(right);
        Root.Children.Add(At(status, 4));

        Content = Root;
    }

    private MenuBar BuildMenuBar()
    {
        var bar = new MenuBar();

        var file = new MenuBarItem { Title = "File" };
        file.Items.Add(Item("New Part", OnNew, VirtualKey.N));
        file.Items.Add(Item("Open…", OnOpen, VirtualKey.O));
        file.Items.Add(Item("Open Sample Shaft", OnSample));
        file.Items.Add(new MenuFlyoutSeparator());
        SaveItem = Item("Save", OnSave, VirtualKey.S);
        SaveAsItem = Item("Save As…", OnSaveAs);
        file.Items.Add(SaveItem);
        file.Items.Add(SaveAsItem);
        file.Items.Add(new MenuFlyoutSeparator());
        ImportItem = Item("Import STEP…", OnImportStep);
        file.Items.Add(ImportItem);
        ExportItem = new MenuFlyoutSubItem { Text = "Export" };
        ExportItem.Items.Add(Item("STEP (*.step)…", OnExportStep));
        ExportItem.Items.Add(Item("STL (*.stl)…", OnExportStl));
        file.Items.Add(ExportItem);
        file.Items.Add(new MenuFlyoutSeparator());
        file.Items.Add(Item("Exit", OnExit));
        bar.Items.Add(file);

        // F and Ctrl+8 are handled by the graphics area so they never fire while typing in a field.
        var view = new MenuBarItem { Title = "View" };
        var fit = Item("Zoom to Fit", OnZoomToFit);
        fit.KeyboardAcceleratorTextOverride = "F";
        var normal = Item("Normal To", OnNormalTo);
        normal.KeyboardAcceleratorTextOverride = "Ctrl+8";
        view.Items.Add(fit);
        view.Items.Add(normal);
        OrientationMenu = new MenuFlyoutSubItem { Text = "Orientation" };
        DisplayStyleMenu = new MenuFlyoutSubItem { Text = "Display Style" };
        view.Items.Add(OrientationMenu);
        view.Items.Add(DisplayStyleMenu);
        bar.Items.Add(view);

        var insert = new MenuBarItem { Title = "Insert" };
        insert.Items.Add(Item("Sketch on Front Plane", OnSketchFront));
        insert.Items.Add(Item("Sketch on Top Plane", OnSketchTop));
        insert.Items.Add(Item("Sketch on Right Plane", OnSketchRight));
        insert.Items.Add(new MenuFlyoutSeparator());
        insert.Items.Add(Item("Boss/Base Extrude…", OnInsertExtrude));
        insert.Items.Add(Item("Boss/Base Revolve…", OnInsertRevolve));
        insert.Items.Add(Item("Cut Extrude…", OnInsertCutExtrude));
        insert.Items.Add(Item("Cut Revolve…", OnInsertCutRevolve));
        insert.Items.Add(Item("Fillet…", OnInsertFillet));
        insert.Items.Add(Item("Chamfer…", OnInsertChamfer));
        bar.Items.Add(insert);

        var tools = new MenuBarItem { Title = "Tools" };
        tools.Items.Add(Item("Mass Properties", OnMassProperties));
        tools.Items.Add(Item("Material…", OnEditMaterial));
        tools.Items.Add(Item("Run Commands…", OnRunCommands));
        bar.Items.Add(tools);

        var help = new MenuBarItem { Title = "Help" };
        help.Items.Add(Item("About STAWN CAD Pro", OnAbout));
        bar.Items.Add(help);
        return bar;
    }

    private static MenuFlyoutItem Item(string text, RoutedEventHandler click, VirtualKey? ctrlKey = null)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += click;
        if (ctrlKey is { } key) item.KeyboardAccelerators.Add(new KeyboardAccelerator { Modifiers = VirtualKeyModifiers.Control, Key = key });
        return item;
    }

    /// <summary>Smart Dimension's Modify box, placed at the cursor.</summary>
    private Canvas BuildModifyLayer()
    {
        var layer = new Canvas();
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        title.Children.Add(new TextBlock { Text = "Modify", FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        ModifyName = new TextBlock { Foreground = Brush(0x5A, 0x62, 0x70), VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(ModifyName);
        ModifyOptions = new RadioButtons { Visibility = Visibility.Collapsed };
        ModifyOk = new Button { Padding = new Thickness(4, 2, 4, 2) };
        ToolTipService.SetToolTip(ModifyOk, "Save the value (Enter)");
        ModifyCancel = new Button { Padding = new Thickness(4, 2, 4, 2) };
        ToolTipService.SetToolTip(ModifyCancel, "Cancel (Esc)");
        ModifyValue = new NumberBox { Width = 110, AcceptsExpression = true };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        row.Children.Add(ModifyOk);
        row.Children.Add(ModifyCancel);
        row.Children.Add(ModifyValue);
        var stack = new StackPanel { Spacing = 6, Width = 190 };
        stack.Children.Add(title);
        stack.Children.Add(ModifyOptions);
        stack.Children.Add(row);
        ModifyBox = new Border
        {
            Child = stack, Visibility = Visibility.Collapsed, Padding = new Thickness(8), CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Colors.White), BorderBrush = Brush(0x8A, 0x97, 0xA8), BorderThickness = new Thickness(1),
        };
        layer.Children.Add(ModifyBox);
        return layer;
    }
}
