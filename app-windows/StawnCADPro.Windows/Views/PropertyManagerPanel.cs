using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stawn.Model;

namespace Stawn.Views;

/// <summary>
/// Property panel for the selected feature: its driving dimensions, with the choice to apply a
/// change to all configurations or this one only, plus mass properties of the body.
/// </summary>
public sealed class PropertyManagerPanel : UserControl
{
    private readonly StackPanel _stack = new() { Padding = new Thickness(12), Spacing = 6 };
    private bool _thisConfigurationOnly;
    public PartModel? Model { get; set; }

    public PropertyManagerPanel() => Content = new ScrollViewer { Content = _stack };

    public void Refresh()
    {
        _stack.Children.Clear();
        if (Model == null) return;
        if (Model.SelectedFeature is { } f)
        {
            _stack.Children.Add(new TextBlock { Text = f, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"] });
            if (Model.Configurations.Count > 1)
            {
                var apply = new ComboBox { Header = "Apply changes to", HorizontalAlignment = HorizontalAlignment.Stretch };
                apply.Items.Add("All configurations");
                apply.Items.Add($"This configuration ({Model.ActiveConfiguration})");
                apply.SelectedIndex = _thisConfigurationOnly ? 1 : 0;
                apply.SelectionChanged += (_, _) => _thisConfigurationOnly = apply.SelectedIndex == 1;
                _stack.Children.Add(apply);
            }
            var dims = Model.Dimensions(f);
            if (dims.Count == 0) _stack.Children.Add(Secondary("No dimensions"));
            foreach (var (name, value) in dims) _stack.Children.Add(DimensionField(name, value));
            var suppressed = new ToggleSwitch { Header = "Suppressed", IsOn = Model.IsSuppressed(f) };
            suppressed.Toggled += (_, _) => Model?.SetSuppressed(f, suppressed.IsOn);
            _stack.Children.Add(suppressed);
            if (Model.ErrorFor(f) is { } e)
                _stack.Children.Add(new TextBlock { Text = e, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red) });
        }
        else
        {
            _stack.Children.Add(Secondary("Select a feature in the feature tree"));
        }

        _stack.Children.Add(new TextBlock { Text = "MASS PROPERTIES", Style = (Style)Application.Current.Resources["SectionHeader"] });
        var m = Model.Mass;
        var (material, density) = Model.Material;
        foreach (var (label, value) in new[]
                 {
                     ("Material", material.Length == 0 ? "–" : material), ("Density", $"{density:0} kg/m³"),
                     ("Volume", $"{m.Volume:0.00} mm³"), ("Surface area", $"{m.Area:0.00} mm²"), ("Mass", $"{m.Mass:0.000} g"),
                     ("Centre of mass", $"{m.Cx:0.00}, {m.Cy:0.00}, {m.Cz:0.00}"),
                 })
            _stack.Children.Add(Labeled(label, value));
    }

    private UIElement DimensionField(string name, double value)
    {
        var box = new NumberBox
        {
            Header = name, Value = value, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            SmallChange = 0.1, LargeChange = 1, AcceptsExpression = true,
            NumberFormatter = new Windows.Globalization.NumberFormatting.DecimalFormatter { FractionDigits = 0, IsDecimalPointAlwaysDisplayed = false,
                NumberRounder = new Windows.Globalization.NumberFormatting.IncrementNumberRounder { Increment = 0.001 } },
        };
        box.ValueChanged += (_, args) =>
        {
            if (double.IsNaN(args.NewValue) || args.NewValue == value) return;
            Model?.SetDimension(name, args.NewValue, _thisConfigurationOnly);
        };
        return box;
    }

    private static TextBlock Secondary(string text) =>
        new() { Text = text, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };

    private static Grid Labeled(string label, string value)
    {
        var g = new Grid();
        g.Children.Add(new TextBlock { Text = label });
        g.Children.Add(new TextBlock { Text = value, HorizontalAlignment = HorizontalAlignment.Right, IsTextSelectionEnabled = true });
        return g;
    }
}
