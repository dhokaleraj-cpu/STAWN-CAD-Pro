using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Stawn.Views;

/// <summary>
/// CommandManager: a strip of large icon buttons with their names underneath, and the tabs
/// (Features, Sketch, Evaluate...) along its bottom edge, as in the feature-based CAD apps
/// engineers already know.
/// </summary>
public sealed class Ribbon : UserControl
{
    private static readonly Color Strip = Color.FromArgb(255, 243, 244, 246);
    private static readonly Color Line = Color.FromArgb(255, 205, 209, 215);
    private static readonly Color Selected = Color.FromArgb(255, 255, 255, 255);

    private readonly Grid _commands = new() { Height = 74, Background = new SolidColorBrush(Strip) };
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Height = 24, Padding = new Thickness(4, 0, 0, 0) };
    private readonly Dictionary<string, StackPanel> _pages = new();
    private readonly Dictionary<string, Button> _tabButtons = new();
    private readonly List<(Button Button, Func<bool> Enabled)> _conditional = new();
    public string CurrentTab { get; private set; } = "";

    public Ribbon()
    {
        var root = new StackPanel { BorderBrush = new SolidColorBrush(Line), BorderThickness = new Thickness(0, 0, 0, 1) };
        root.Children.Add(_commands);
        root.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Line) });
        root.Children.Add(new Border { Child = _tabs, Background = new SolidColorBrush(Color.FromArgb(255, 232, 234, 237)) });
        Content = root;
    }

    /// <summary>Adds a tab and returns its page, to fill with <see cref="Add"/>.</summary>
    public StackPanel AddTab(string name)
    {
        var page = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Padding = new Thickness(6, 4, 6, 2), Visibility = Visibility.Collapsed };
        _pages[name] = page;
        _commands.Children.Add(page);
        var tab = new Button
        {
            Content = new TextBlock { Text = name, FontSize = 12 },
            Padding = new Thickness(12, 2, 12, 3), MinHeight = 0, CornerRadius = new CornerRadius(0, 0, 3, 3),
            BorderThickness = new Thickness(1, 0, 1, 1), BorderBrush = new SolidColorBrush(Colors.Transparent),
            Background = new SolidColorBrush(Colors.Transparent),
        };
        tab.Click += (_, _) => Select(name);
        _tabButtons[name] = tab;
        _tabs.Children.Add(tab);
        if (_pages.Count == 1) Select(name);
        return page;
    }

    public void Select(string name)
    {
        if (!_pages.ContainsKey(name)) return;
        CurrentTab = name;
        foreach (var (n, p) in _pages) p.Visibility = n == name ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (n, b) in _tabButtons)
        {
            bool on = n == name;
            b.Background = new SolidColorBrush(on ? Selected : Colors.Transparent);
            b.BorderBrush = new SolidColorBrush(on ? Line : Colors.Transparent);
            ((TextBlock)b.Content).FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    /// <summary>A large command button; <paramref name="enabled"/> is checked on <see cref="RefreshEnabled"/>.</summary>
    public Button Add(StackPanel page, string label, string icon, Action action, string? tip = null, Func<bool>? enabled = null)
    {
        var b = CommandButton(label, icon);
        b.Click += (_, _) => action();
        ToolTipService.SetToolTip(b, tip ?? label);
        page.Children.Add(b);
        if (enabled != null) _conditional.Add((b, enabled));
        return b;
    }

    /// <summary>A large button that opens a menu, such as Sketch's choice of plane.</summary>
    public Button AddMenu(StackPanel page, string label, string icon, MenuFlyout menu, string? tip = null)
    {
        var b = CommandButton(label + " ▾", icon);
        b.Flyout = menu;
        ToolTipService.SetToolTip(b, tip ?? label);
        page.Children.Add(b);
        return b;
    }

    public static void AddSeparator(StackPanel page) =>
        page.Children.Add(new Border { Width = 1, Margin = new Thickness(6, 4, 6, 4), Background = new SolidColorBrush(Line) });

    public void RefreshEnabled()
    {
        foreach (var (b, enabled) in _conditional) b.IsEnabled = enabled();
    }

    private static Button CommandButton(string label, string icon)
    {
        var stack = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center };
        var art = Icons.Get(icon, 32);
        art.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(art);
        stack.Children.Add(new TextBlock
        {
            Text = label, FontSize = 11, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
            MaxWidth = 76, MaxLines = 2, Foreground = new SolidColorBrush(Color.FromArgb(255, 40, 44, 52)),
        });
        return new Button
        {
            Content = stack, VerticalAlignment = VerticalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(6, 4, 6, 2), MinWidth = 58, BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
        };
    }
}
