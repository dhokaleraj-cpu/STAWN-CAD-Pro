using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Stawn.Model;
using Windows.Storage.Pickers;

namespace Stawn.Views;

/// <summary>Content dialogs and file pickers shared by the window and panels.</summary>
public static class Dialogs
{
    /// <summary>File pickers in an unpackaged WinUI app need the owning window's handle.</summary>
    public static void Attach(object picker, Window window) =>
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));

    public static async Task<string?> SavePathAsync(Window window, string suggestedName, string kind, string extension)
    {
        var picker = new FileSavePicker { SuggestedFileName = suggestedName };
        picker.FileTypeChoices.Add(kind, new[] { extension });
        Attach(picker, window);
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    private static ContentDialog Dialog(UIElement owner, string title, object content, string primary = "OK", string? close = "Cancel")
    {
        var d = new ContentDialog { Title = title, Content = content, PrimaryButtonText = primary, XamlRoot = owner.XamlRoot,
                                    DefaultButton = ContentDialogButton.Primary };
        if (close != null) d.CloseButtonText = close;
        return d;
    }

    public static async Task MessageAsync(UIElement owner, string title, string message) =>
        await Dialog(owner, title, new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true }, "OK", null).ShowAsync();

    public static async Task<string?> TextAsync(UIElement owner, string title, string label, string initial)
    {
        var box = new TextBox { Header = label, Text = initial };
        box.Loaded += (_, _) => { box.Focus(FocusState.Programmatic); box.SelectAll(); };
        var result = await Dialog(owner, title, box).ShowAsync();
        var text = box.Text.Trim();
        return result == ContentDialogResult.Primary && text.Length > 0 ? text : null;
    }

    /// <summary>Fillet or Chamfer PropertyManager: size and which edges.</summary>
    public static async Task<(double Size, string Edges)?> EdgeFeatureAsync(UIElement owner, string op)
    {
        var size = new NumberBox { Header = op == "fillet" ? "Radius (mm)" : "Distance (mm)", Value = op == "fillet" ? 0.5 : 0.3,
                                   SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, SmallChange = 0.1, Minimum = 0.001 };
        var edges = new RadioButtons { Header = "Edges", ItemsSource = new[] { "All edges", "Circular edges", "Straight edges" }, SelectedIndex = 1 };
        var panel = new StackPanel { Spacing = 12, Width = 320 };
        panel.Children.Add(size);
        panel.Children.Add(edges);
        var title = op == "fillet" ? "Fillet" : "Chamfer";
        if (await Dialog(owner, title, panel).ShowAsync() != ContentDialogResult.Primary || double.IsNaN(size.Value)) return null;
        return (size.Value, new[] { "all", "circular", "linear" }[Math.Max(0, edges.SelectedIndex)]);
    }

    public static async Task<(string Name, double Density)?> MaterialAsync(UIElement owner, string current)
    {
        var library = PartModel.MaterialLibrary();
        var list = new ListView { ItemsSource = library.Select(m => $"{m.Name}   ({m.Density:0} kg/m³)").ToList(), Height = 360, Width = 380,
                                  SelectedIndex = library.FindIndex(m => m.Name == current) };
        if (await Dialog(owner, "Material", list, "Apply").ShowAsync() != ContentDialogResult.Primary || list.SelectedIndex < 0) return null;
        return library[list.SelectedIndex];
    }

    /// <summary>Runs a list of commands from the command API (docs/COMMANDS.md) against the part.</summary>
    public static async Task RunCommandsAsync(UIElement owner, PartModel model, string example)
    {
        var editor = new TextBox
        {
            Text = example, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, Height = 320, Width = 560,
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas"), IsSpellCheckEnabled = false,
        };
        var output = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontSize = 12, MaxHeight = 140 };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "One JSON command per line, or a JSON array. See docs/COMMANDS.md.", Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] });
        panel.Children.Add(editor);
        panel.Children.Add(new ScrollViewer { Content = output, MaxHeight = 140 });
        var dialog = Dialog(owner, "Run Commands", panel, "Run", "Close");
        dialog.PrimaryButtonClick += (_, args) =>
        {
            args.Cancel = true;  // keep the dialog open to show the result
            var text = editor.Text.Trim();
            var commands = text.StartsWith("[") ? text
                : "[" + string.Join(",", text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("//"))) + "]";
            output.Text = model.Execute(commands);
        };
        await dialog.ShowAsync();
    }

    public static async Task DrawingPreviewAsync(UIElement owner, string svgPath, string title)
    {
        var image = new Image { Source = new SvgImageSource(new Uri(svgPath)), Width = 840, Height = 594, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform };
        var frame = new Border { Child = image, Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White) };
        var dialog = Dialog(owner, title, frame, "Close", null);
        dialog.Resources["ContentDialogMaxWidth"] = 960.0;
        dialog.Resources["ContentDialogMaxHeight"] = 800.0;
        await dialog.ShowAsync();
    }
}
