using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Stawn.Views;

/// <summary>
/// STAWN CAD Pro's own colour icons, drawn on a 32 × 32 grid: grey isometric solids with a
/// yellow working face for features, blue for sketch geometry, red for cuts. Original artwork.
/// </summary>
public static class Icons
{
    private static readonly Color Steel = Color.FromArgb(255, 122, 138, 158);
    private static readonly Color SteelLight = Color.FromArgb(255, 201, 211, 223);
    private static readonly Color SteelDark = Color.FromArgb(255, 92, 106, 124);
    private static readonly Color Face = Color.FromArgb(255, 242, 193, 78);
    private static readonly Color Blue = Color.FromArgb(255, 31, 95, 209);
    private static readonly Color Red = Color.FromArgb(255, 210, 60, 50);
    private static readonly Color Green = Color.FromArgb(255, 46, 158, 68);
    private static readonly Color Ink = Color.FromArgb(255, 50, 56, 66);
    private static readonly Color Paper = Colors.White;

    private record Layer(string Data, Color? Fill, Color? Stroke = null, double Width = 1.2, bool Dashed = false);

    // Shared shapes
    private const string BoxTop = "M16,10 L28,15 L16,20 L4,15 Z";
    private const string BoxLeft = "M4,15 L16,20 L16,28 L4,23 Z";
    private const string BoxRight = "M16,20 L28,15 L28,23 L16,28 Z";
    private const string ArrowUp = "M16,1 L21,7 L18,7 L18,13 L14,13 L14,7 L11,7 Z";
    private const string ArrowDown = "M16,14 L21,8 L18,8 L18,1 L14,1 L14,8 L11,8 Z";

    private static Layer[] Box(Color top) => new[]
    {
        new Layer(BoxLeft, Steel, SteelDark, 0.8), new Layer(BoxRight, SteelLight, SteelDark, 0.8), new Layer(BoxTop, top, SteelDark, 0.8),
    };

    private static Layer[] Concat(params Layer[][] parts)
    {
        var all = new List<Layer>();
        foreach (var p in parts) all.AddRange(p);
        return all.ToArray();
    }

    private static readonly Dictionary<string, Layer[]> Art = new()
    {
        // Features
        ["extrude"] = Concat(Box(Face), new[] { new Layer(ArrowUp, Blue) }),
        ["cut-extrude"] = Concat(Box(SteelLight), new[]
        {
            new Layer("M11,15 L16,17.2 L21,15 L16,12.8 Z", Ink),
            new Layer(ArrowDown, Red),
        }),
        ["revolve"] = new[]
        {
            new Layer("M9,6 L15,6 L15,26 L9,26 Z", Face, SteelDark, 0.8),
            new Layer("M6,3 L6,29", null, Ink, 1, true),
            new Layer("M18,8 A10,6 0 1 1 18,24", null, Blue, 2),
            new Layer("M15,24 L20,20.5 L20.5,27 Z", Blue),
        },
        ["cut-revolve"] = new[]
        {
            new Layer("M9,6 L15,6 L15,26 L9,26 Z", SteelLight, SteelDark, 0.8),
            new Layer("M6,3 L6,29", null, Ink, 1, true),
            new Layer("M18,8 A10,6 0 1 1 18,24", null, Red, 2),
            new Layer("M15,24 L20,20.5 L20.5,27 Z", Red),
        },
        ["fillet"] = new[]
        {
            new Layer("M4,28 L4,12 A8,8 0 0 1 12,4 L28,4 L28,28 Z", Steel, SteelDark, 0.8),
            new Layer("M4,12 A8,8 0 0 1 12,4", null, Face, 3),
        },
        ["chamfer"] = new[]
        {
            new Layer("M4,28 L4,12 L12,4 L28,4 L28,28 Z", Steel, SteelDark, 0.8),
            new Layer("M4,12 L12,4", null, Face, 3),
        },
        ["import"] = new[]
        {
            new Layer("M8,12 L24,12 L24,28 L8,28 Z", SteelLight, SteelDark, 0.8),
            new Layer("M16,2 L16,18 M11,13 L16,18 L21,13", null, Green, 2.4),
        },
        ["rebuild"] = new[]
        {
            new Layer("M25,16 A9,9 0 1 1 22,9.3", null, Green, 3),
            new Layer("M19,4 L27,6 L23,13 Z", Green),
        },
        ["mass"] = new[]
        {
            new Layer("M16,4 L16,26 M8,28 L24,28", null, Ink, 1.6),
            new Layer("M5,9 L27,9", null, Ink, 1.6),
            new Layer("M2,18 L5,9 L8,18 Z M24,18 L27,9 L30,18 Z", Face, Ink, 1),
        },
        ["material"] = new[]
        {
            new Layer("M4,4 L28,4 L28,28 L4,28 Z", Face, Ink, 0.8),
            new Layer("M4,12 L28,12 M4,20 L28,20 M12,4 L12,28 M20,4 L20,28", null, SteelDark, 0.8),
        },

        // Sketch tools
        ["sketch"] = new[]
        {
            new Layer("M3,8 L22,8 L22,27 L3,27 Z", null, Blue, 1.6),
            new Layer("M14,22 L26,4 L30,7 L18,25 L13,27 Z", Face, Ink, 1),
        },
        ["exit-sketch"] = new[]
        {
            new Layer("M3,8 L18,8 L18,27 L3,27 Z", null, Blue, 1.6),
            new Layer("M14,17 L28,17 M23,12 L28,17 L23,22", null, Green, 2.4),
        },
        ["select"] = new[] { new Layer("M8,3 L8,25 L13,20 L17,29 L20,27.5 L16,18.5 L23,18 Z", Paper, Ink, 1.3) },
        ["line"] = new[]
        {
            new Layer("M5,26 L27,6", null, Blue, 2),
            new Layer("M2.5,23.5 h5 v5 h-5 Z M24.5,3.5 h5 v5 h-5 Z", Blue),
        },
        ["centerline"] = new[] { new Layer("M3,29 L29,3", null, Blue, 1.8, true) },
        ["rectangle"] = new[]
        {
            new Layer("M5,8 L27,8 L27,24 L5,24 Z", null, Blue, 2),
            new Layer("M3,6 h4 v4 h-4 Z M25,22 h4 v4 h-4 Z", Blue),
        },
        ["circle"] = new[]
        {
            new Layer("M28,16 A12,12 0 1 1 4,16 A12,12 0 1 1 28,16 Z", null, Blue, 2),
            new Layer("M14,14 h4 v4 h-4 Z", Blue),
        },
        ["arc"] = new[]
        {
            new Layer("M27,16 A11,11 0 0 0 5,16", null, Blue, 2),
            new Layer("M14,14 h4 v4 h-4 Z M3,14 h4 v4 h-4 Z M25,14 h4 v4 h-4 Z", Blue),
        },
        ["dimension"] = new[]
        {
            new Layer("M4,8 L4,26 M28,8 L28,26", null, Ink, 1.2),
            new Layer("M4,20 L28,20", null, Ink, 1.2),
            new Layer("M4,20 L9,17.5 L9,22.5 Z M28,20 L23,17.5 L23,22.5 Z", Ink),
            new Layer("M10,4 L22,4 L22,14 L10,14 Z", Paper, Blue, 1.2),
            new Layer("M13,7 L19,7 M13,11 L19,11", null, Blue, 1.2),
        },
        ["relation"] = new[]
        {
            new Layer("M4,24 L20,24 M24,4 L24,20", null, Blue, 2),
            new Layer("M8,8 L16,8 L16,16 L8,16 Z", Face, Ink, 1),
        },

        // Drawings and files
        ["drawing"] = new[]
        {
            new Layer("M3,6 L29,6 L29,26 L3,26 Z", Paper, Ink, 1.2),
            new Layer("M6,9 h8 v8 h-8 Z M17,9 h9 v8 h-9 Z", null, Blue, 1),
            new Layer("M18,20 L29,20 M18,20 L18,26", null, Ink, 1),
        },
        ["pdf"] = FileIcon(Red),
        ["dxf"] = FileIcon(Blue),
        ["svg"] = FileIcon(Green),
        ["new"] = new[] { new Layer("M7,3 L20,3 L26,9 L26,29 L7,29 Z", Paper, Ink, 1.2), new Layer("M20,3 L20,9 L26,9", null, Ink, 1.2) },
        ["open"] = new[]
        {
            new Layer("M3,8 L12,8 L14,10 L27,10 L27,27 L3,27 Z", Face, Ink, 1),
            new Layer("M6,14 L30,14 L26,27 L3,27 Z", Color.FromArgb(255, 250, 214, 120), Ink, 1),
        },
        ["save"] = new[]
        {
            new Layer("M4,4 L24,4 L28,8 L28,28 L4,28 Z", Blue, Ink, 1),
            new Layer("M9,4 L22,4 L22,12 L9,12 Z", Paper),
            new Layer("M8,18 L24,18 L24,28 L8,28 Z", Paper),
        },
        ["undo"] = new[] { new Layer("M10,12 L22,12 A6,6 0 0 1 22,24 L12,24", null, Ink, 2.2), new Layer("M4,12 L11,6 L11,18 Z", Ink) },
        ["options"] = new[]
        {
            new Layer("M16,6 A10,10 0 1 1 15.9,6 Z", SteelLight, SteelDark, 4, true),
            new Layer("M20,16 A4,4 0 1 1 12,16 A4,4 0 1 1 20,16 Z", Paper, SteelDark, 1.5),
        },

        // Feature tree
        ["part"] = Concat(new[] { new Layer("M16,4 L28,10 L16,16 L4,10 Z", Face, SteelDark, 0.8) },
                          new[] { new Layer("M4,10 L16,16 L16,28 L4,22 Z", Steel, SteelDark, 0.8), new Layer("M16,16 L28,10 L28,22 L16,28 Z", SteelLight, SteelDark, 0.8) }),
        ["plane"] = new[] { new Layer("M8,8 L30,8 L24,24 L2,24 Z", Color.FromArgb(70, 31, 95, 209), Blue, 1.2) },
        ["origin"] = new[]
        {
            new Layer("M16,16 L30,16", null, Red, 1.8), new Layer("M16,16 L16,2", null, Green, 1.8), new Layer("M16,16 L6,26", null, Blue, 1.8),
            new Layer("M14,14 h4 v4 h-4 Z", Ink),
        },
        ["history"] = new[]
        {
            new Layer("M28,16 A12,12 0 1 1 4,16 A12,12 0 1 1 28,16 Z", Paper, Ink, 1.4),
            new Layer("M16,8 L16,16 L22,19", null, Ink, 1.8),
        },
        ["annotations"] = new[] { new Layer("M4,6 L28,6 L28,22 L14,22 L8,28 L8,22 L4,22 Z", Paper, Ink, 1.2), new Layer("M8,11 L24,11 M8,16 L20,16", null, Blue, 1.4) },
        ["bodies"] = Concat(Box(SteelLight)),

        // Views
        ["zoom-fit"] = new[]
        {
            new Layer("M3,10 L3,3 L10,3 M22,3 L29,3 L29,10 M29,22 L29,29 L22,29 M10,29 L3,29 L3,22", null, Ink, 2),
            new Layer("M10,10 L22,10 L22,22 L10,22 Z", Face, Ink, 1),
        },
        ["view"] = Concat(Box(SteelLight), new[] { new Layer(BoxTop, Color.FromArgb(255, 120, 170, 240), SteelDark, 0.8) }),
        ["display"] = Concat(Box(Steel), new[] { new Layer("M4,15 L16,20 L28,15 M16,20 L16,28", null, Ink, 1.2) }),
        ["normal-to"] = new[]
        {
            new Layer("M6,6 L26,6 L26,26 L6,26 Z", Color.FromArgb(70, 31, 95, 209), Blue, 1.4),
            new Layer("M16,16 m-3,0 a3,3 0 1 0 6,0 a3,3 0 1 0 -6,0", Blue),
        },
        ["ok"] = new[] { new Layer("M5,17 L13,25 L27,7", null, Green, 4) },
        ["cancel"] = new[] { new Layer("M7,7 L25,25 M25,7 L7,25", null, Red, 4) },
    };

    private static Layer[] FileIcon(Color band) => new[]
    {
        new Layer("M7,3 L20,3 L26,9 L26,29 L7,29 Z", Paper, Ink, 1.2),
        new Layer("M20,3 L20,9 L26,9", null, Ink, 1.2),
        new Layer("M4,16 L22,16 L22,24 L4,24 Z", band),
    };

    /// <summary>The icon at the given size; unknown names draw an empty square.</summary>
    public static FrameworkElement Get(string name, double size = 32)
    {
        var canvas = new Canvas { Width = 32, Height = 32 };
        if (Art.TryGetValue(name, out var layers))
            foreach (var l in layers)
            {
                var path = new Path { Data = (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), l.Data) };
                if (l.Fill is { } f) path.Fill = new SolidColorBrush(f);
                if (l.Stroke is { } s)
                {
                    path.Stroke = new SolidColorBrush(s);
                    path.StrokeThickness = l.Width;
                    path.StrokeLineJoin = PenLineJoin.Round;
                    path.StrokeStartLineCap = path.StrokeEndLineCap = PenLineCap.Round;
                    if (l.Dashed) path.StrokeDashArray = new DoubleCollection { 3, 2 };
                }
                canvas.Children.Add(path);
            }
        return new Viewbox { Width = size, Height = size, Child = canvas };
    }
}
