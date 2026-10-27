using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Stawn.Model;

namespace Stawn.Views;

/// <summary>Configurations list with the Configuration Properties panel for the selected one.</summary>
public sealed class ConfigurationManagerPanel : UserControl
{
    private readonly StackPanel _stack = new() { Padding = new Thickness(12), Spacing = 6 };
    private string? _editing;
    public PartModel? Model { get; set; }

    public ConfigurationManagerPanel() => Content = new ScrollViewer { Content = _stack };

    public void Refresh()
    {
        _stack.Children.Clear();
        if (Model == null) return;
        _stack.Children.Add(new TextBlock { Text = $"{Model.Name} Configuration(s)", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        foreach (var c in Model.Configurations.OfType<JsonObject>())
        {
            var name = c["name"]?.GetValue<string>() ?? "?";
            var description = c["description"]?.GetValue<string>() ?? "";
            bool active = name == Model.ActiveConfiguration;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new Thickness(4) };
            row.Children.Add(new FontIcon
            {
                Glyph = active ? "" : "", FontSize = 14,
                Foreground = active ? new SolidColorBrush(Microsoft.UI.Colors.Green) : (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
            row.Children.Add(new TextBlock { Text = name });
            if (description.Length > 0 && description != name)
                row.Children.Add(new TextBlock { Text = $"[{description}]", Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
            row.DoubleTapped += (_, _) => Model?.Activate(name);
            row.ContextFlyout = Menu(name, active);
            ToolTipService.SetToolTip(row, "Double-click to show this configuration");
            _stack.Children.Add(row);
        }
        var add = new Button { Content = "Add Configuration…", Margin = new Thickness(0, 8, 0, 0) };
        add.Click += async (_, _) =>
        {
            if (await Dialogs.TextAsync(this, "Add Configuration", "Configuration name", "") is { } n) Model?.AddConfiguration(n);
        };
        _stack.Children.Add(add);
        if (_editing != null) AddPropertiesEditor(_editing);
    }

    private MenuFlyout Menu(string name, bool active)
    {
        var menu = new MenuFlyout();
        var show = new MenuFlyoutItem { Text = "Show Configuration" };
        show.Click += (_, _) => Model?.Activate(name);
        var props = new MenuFlyoutItem { Text = "Properties…" };
        props.Click += (_, _) => { _editing = name; Refresh(); };
        var delete = new MenuFlyoutItem { Text = "Delete", IsEnabled = !active };
        delete.Click += (_, _) => Model?.DeleteConfiguration(name);
        menu.Items.Add(show);
        menu.Items.Add(props);
        menu.Items.Add(delete);
        return menu;
    }

    private void AddPropertiesEditor(string name)
    {
        var c = Model!.Configurations.OfType<JsonObject>().FirstOrDefault(x => x["name"]?.GetValue<string>() == name);
        if (c == null) { _editing = null; return; }
        _stack.Children.Add(new TextBlock { Text = "CONFIGURATION PROPERTIES", Style = (Style)Application.Current.Resources["SectionHeader"] });
        var nameBox = new TextBox { Header = "Configuration name", Text = name };
        var descriptionBox = new TextBox { Header = "Description", Text = c["description"]?.GetValue<string>() ?? "" };
        var bomBox = new TextBox { Header = "Part number in bill of materials", Text = c["bomPartNumber"]?.GetValue<string>() ?? "", PlaceholderText = Model.Name };
        _stack.Children.Add(nameBox);
        _stack.Children.Add(descriptionBox);
        _stack.Children.Add(bomBox);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var ok = new Button { Content = "OK", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        ok.Click += (_, _) =>
        {
            _editing = null;
            Model?.UpdateConfiguration(name, nameBox.Text.Trim(), descriptionBox.Text, bomBox.Text);
        };
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) => { _editing = null; Refresh(); };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        _stack.Children.Add(buttons);
    }
}
