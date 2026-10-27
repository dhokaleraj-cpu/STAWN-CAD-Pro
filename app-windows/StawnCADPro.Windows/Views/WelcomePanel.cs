using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Stawn.Views;

/// <summary>Welcome screen shown when no part is open: new part, open, or the sample shaft.</summary>
public sealed class WelcomePanel : UserControl
{
    public event Action? NewPart;
    public event Action? OpenPart;
    public event Action? OpenSample;

    public WelcomePanel()
    {
        Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 14, Width = 420 };
        stack.Children.Add(new TextBlock { Text = "STAWN CAD Pro", Style = (Style)Application.Current.Resources["TitleTextBlockStyle"] });
        stack.Children.Add(new TextBlock
        {
            Text = "Parametric part modelling, configurations, custom properties and drawings.",
            TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        stack.Children.Add(Tile("", "Part", "A new 3D part", () => NewPart?.Invoke()));
        stack.Children.Add(Tile("", "Open…", "Open a .stprt part", () => OpenPart?.Invoke()));
        stack.Children.Add(Tile("", "Sample shaft", "DM400002 8 mm shaft with configurations and properties", () => OpenSample?.Invoke()));
        Content = stack;
    }

    private static Button Tile(string glyph, string title, string detail, Action action)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 26 });
        var text = new StackPanel();
        text.Children.Add(new TextBlock { Text = title, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        text.Children.Add(new TextBlock { Text = detail, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] });
        row.Children.Add(text);
        var b = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(16, 12, 16, 12) };
        b.Click += (_, _) => action();
        return b;
    }
}
