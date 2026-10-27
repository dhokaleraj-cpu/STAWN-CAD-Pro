using System;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Stawn.Views;

/// <summary>
/// A PropertyManager page in the left pane: the command's icon and name, OK and Cancel buttons,
/// an optional message, then collapsible groups of options. Feature commands (Extrude, Revolve,
/// Fillet...) and sketch tools use it instead of pop-up dialogs. Enter is OK, Esc is Cancel.
/// </summary>
public sealed class PropertyManagerPage : UserControl
{
    private static readonly Color HeaderBlue = Color.FromArgb(255, 32, 72, 140);
    private static readonly Color GroupLine = Color.FromArgb(255, 200, 206, 214);
    private readonly StackPanel _body = new() { Spacing = 8, Padding = new Thickness(8, 4, 8, 12) };
    private readonly Border _message = new()
    {
        Background = new SolidColorBrush(Color.FromArgb(255, 255, 250, 214)),
        BorderBrush = new SolidColorBrush(Color.FromArgb(255, 222, 200, 120)), BorderThickness = new Thickness(1),
        Padding = new Thickness(8, 6, 8, 6), Visibility = Visibility.Collapsed,
    };
    private readonly TextBlock _messageText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };

    public Action? Ok { get; }
    public Action? Cancel { get; }

    public PropertyManagerPage(string title, string icon, Action? ok, Action? cancel)
    {
        Ok = ok;
        Cancel = cancel;
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new Thickness(8, 8, 8, 4) };
        header.Children.Add(Icons.Get(icon, 24));
        header.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Padding = new Thickness(8, 0, 8, 6) };
        if (ok != null) buttons.Children.Add(IconButton("ok", "OK (Enter)", ok));
        if (cancel != null) buttons.Children.Add(IconButton("cancel", "Cancel (Esc)", cancel));

        _message.Child = _messageText;
        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(buttons);
        stack.Children.Add(new Border { Child = _message, Padding = new Thickness(8, 0, 8, 4) });
        stack.Children.Add(_body);
        Content = new ScrollViewer { Content = stack };
        KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter && ok != null) { ok(); e.Handled = true; }
            else if (e.Key == Windows.System.VirtualKey.Escape && cancel != null) { cancel(); e.Handled = true; }
        };
    }

    public void SetMessage(string? text)
    {
        _messageText.Text = text ?? "";
        _message.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>A collapsible group box with a blue title, returning the panel to fill.</summary>
    public StackPanel AddGroup(string title)
    {
        var content = new StackPanel { Spacing = 6, Padding = new Thickness(10, 6, 6, 8) };
        var arrow = new TextBlock { Text = "▾", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        head.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(HeaderBlue) });
        var toggle = new Button
        {
            Content = head, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), Padding = new Thickness(6, 3, 6, 3),
        };
        head.Children.Insert(0, arrow);
        toggle.Click += (_, _) =>
        {
            bool open = content.Visibility == Visibility.Visible;
            content.Visibility = open ? Visibility.Collapsed : Visibility.Visible;
            arrow.Text = open ? "▸" : "▾";
        };
        var box = new StackPanel();
        box.Children.Add(toggle);
        box.Children.Add(content);
        _body.Children.Add(new Border
        {
            Child = box, BorderBrush = new SolidColorBrush(GroupLine), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Colors.White),
        });
        return content;
    }

    private static Button IconButton(string icon, string tip, Action action)
    {
        var b = new Button { Content = Icons.Get(icon, 18), Padding = new Thickness(6, 3, 6, 3) };
        b.Click += (_, _) => action();
        ToolTipService.SetToolTip(b, tip);
        return b;
    }
}
