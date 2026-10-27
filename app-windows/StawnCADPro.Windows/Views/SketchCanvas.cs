using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Stawn.Model;
using Windows.System;
using Windows.UI;

namespace Stawn.Views;

/// <summary>
/// The graphics area while a sketch is open, looking normal to the sketch plane. Mouse and
/// keyboard input goes to the core's sketch editor (the sketch_input command), which adds the
/// geometry and returns the preview, snap, prompt and relation choices drawn here. Geometry that
/// can still move is blue, defined geometry black and over-defined geometry red. Pan with the
/// middle or right button, zoom with the wheel, Esc ends the current tool, Delete removes the
/// selection.
/// </summary>
public sealed class SketchCanvas : UserControl
{
    private static readonly Color Free = Color.FromArgb(255, 0, 64, 255);
    private static readonly Color Defined = Colors.Black;
    private static readonly Color Over = Color.FromArgb(255, 220, 30, 30);
    private static readonly Color Selected = Color.FromArgb(255, 255, 140, 0);
    private static readonly Color Preview = Color.FromArgb(255, 110, 110, 110);
    private static readonly Color DimensionText = Color.FromArgb(255, 30, 30, 30);

    private readonly CanvasControl _canvas = new();
    private readonly CanvasStrokeStyle _dashed = new() { DashStyle = CanvasDashStyle.Dash };
    private readonly CanvasStrokeStyle _solid = new();
    private PartModel? _model;
    private JsonObject _sketch = new();   // get_sketch
    private JsonObject _editor = new();   // last sketch_input reply
    private Vector2 _pan;                 // screen position of the sketch origin, from the centre
    private float _scale = 4;             // pixels per millimetre
    private Vector2? _panFrom;

    /// <summary>Raised after every input with the editor reply (prompt, relations, dimension request).</summary>
    public event Action<string, JsonObject>? EditorChanged;

    public string SketchName => _sketch["sketch"]?.GetValue<string>() ?? "";
    public string Status => _sketch["status"]?.GetValue<string>() ?? "";
    public int DegreesOfFreedom => _sketch["degrees_of_freedom"]?.GetValue<int>() ?? 0;

    public SketchCanvas()
    {
        Content = _canvas;
        _canvas.Draw += OnDraw;
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += (_, e) => { _panFrom = null; ReleasePointerCapture(e.Pointer); };
        PointerWheelChanged += OnWheel;
        KeyDown += OnKeyDown;
        IsTabStop = true;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Cross);
    }

    /// <summary>Shows a sketch the command session has open (after create_sketch or edit_sketch).</summary>
    public void Open(PartModel model, JsonObject sketch)
    {
        _model = model;
        _sketch = sketch;
        _editor = new JsonObject();
        ZoomToFit();
        Focus(FocusState.Programmatic);
    }

    public void SetTool(string tool) => Send(new JsonObject { ["event"] = "tool", ["tool"] = tool });
    public void AddRelation(int index) => Send(new JsonObject { ["event"] = "relation", ["index"] = index });
    public void AddDimension(int option, double value) => Send(new JsonObject { ["event"] = "dimension", ["option"] = option, ["value"] = value });
    public void Escape() => Send(new JsonObject { ["event"] = "escape" });

    /// <summary>Re-reads the sketch, e.g. after a command changed it.</summary>
    public void Reload()
    {
        if (_model == null) return;
        var s = _model.Command(new JsonObject { ["op"] = "get_sketch" });
        if (s["ok"]?.GetValue<bool>() == true) _sketch = s;
        _canvas.Invalidate();
    }

    private void Send(JsonObject e, Vector2? at = null)
    {
        if (_model == null) return;
        e["op"] = "sketch_input";
        if (at is { } p)
        {
            var m = ToSketch(p);
            e["x"] = m.X;
            e["y"] = m.Y;
        }
        e["tol"] = 8 / _scale;  // snap within 8 pixels
        var reply = _model.Command(e);
        if (reply["ok"]?.GetValue<bool>() != true) return;
        _editor = reply;
        if (reply["changed"]?.GetValue<bool>() == true) Reload();
        else _canvas.Invalidate();
        EditorChanged?.Invoke(e["event"]!.GetValue<string>(), reply);
    }

    // View

    private Vector2 Centre => new((float)_canvas.ActualWidth / 2, (float)_canvas.ActualHeight / 2);
    private Vector2 ToScreen(double x, double y) => Centre + _pan + new Vector2((float)x * _scale, -(float)y * _scale);
    private Vector2 ToSketch(Vector2 s) => new((s.X - Centre.X - _pan.X) / _scale, -(s.Y - Centre.Y - _pan.Y) / _scale);

    public void ZoomToFit()
    {
        var pts = Points();
        float minX = -10, minY = -10, maxX = 10, maxY = 10;
        foreach (var p in pts)
        {
            minX = MathF.Min(minX, (float)p.X); maxX = MathF.Max(maxX, (float)p.X);
            minY = MathF.Min(minY, (float)p.Y); maxY = MathF.Max(maxY, (float)p.Y);
        }
        foreach (var c in Array("circles"))
        {
            var ctr = pts[Int(c, "center")];
            float r = (float)Num(c, "radius");
            minX = MathF.Min(minX, (float)ctr.X - r); maxX = MathF.Max(maxX, (float)ctr.X + r);
            minY = MathF.Min(minY, (float)ctr.Y - r); maxY = MathF.Max(maxY, (float)ctr.Y + r);
        }
        float w = MathF.Max((float)_canvas.ActualWidth, 200), h = MathF.Max((float)_canvas.ActualHeight, 200);
        _scale = MathF.Min(w / (maxX - minX), h / (maxY - minY)) * 0.7f;
        _pan = new Vector2(-(minX + maxX) / 2 * _scale, (minY + maxY) / 2 * _scale);
        _canvas.Invalidate();
    }

    // Sketch JSON helpers

    private JsonArray Array(string key) => _sketch["geometry"]?[key] as JsonArray ?? new JsonArray();
    private static int Int(JsonNode? n, string key) => n?[key]?.GetValue<int>() ?? -1;
    private static double Num(JsonNode? n, string key) => n?[key]?.GetValue<double>() ?? 0;
    private static bool Flag(JsonNode? n, string key) => n?[key]?.GetValue<bool>() ?? false;

    private (double X, double Y)[] Points() =>
        Array("points").Select(p => (Num(p, "x"), Num(p, "y"))).ToArray();

    private bool PointDefined(int i) =>
        (_sketch["defined"]?["points"] as JsonArray) is { } a && i >= 0 && i < a.Count && a[i]!.GetValue<bool>();

    private bool CircleDefined(int i) =>
        (_sketch["defined"]?["circles"] as JsonArray) is { } a && i >= 0 && i < a.Count && a[i]!.GetValue<bool>();

    private bool IsSelected(string kind, int index) =>
        (_editor["selection"] as JsonArray)?.Any(s => s?["kind"]?.GetValue<string>() == kind && Int(s, "index") == index) == true;

    private Color ColourFor(string kind, int index, bool defined)
    {
        if (IsSelected(kind, index)) return Selected;
        if (Status == "Over Defined") return Over;
        return defined ? Defined : Free;
    }

    // Drawing

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        ds.Clear(Color.FromArgb(255, 250, 250, 252));
        var o = ToScreen(0, 0);
        ds.DrawLine(o, o + new Vector2(40, 0), Color.FromArgb(255, 200, 40, 40), 1.2f);
        ds.DrawLine(o, o - new Vector2(0, 40), Color.FromArgb(255, 40, 150, 40), 1.2f);
        if (_model == null) return;

        var pts = Points();
        Vector2 P(int i) => ToScreen(pts[i].X, pts[i].Y);

        var lines = Array("lines");
        for (int i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            int a = Int(l, "p1"), b = Int(l, "p2");
            var colour = ColourFor("line", i, PointDefined(a) && PointDefined(b));
            if (Flag(l, "construction")) ds.DrawLine(P(a), P(b), colour, 1.2f, _dashed);
            else ds.DrawLine(P(a), P(b), colour, IsSelected("line", i) ? 2.5f : 1.6f);
        }
        var circles = Array("circles");
        for (int i = 0; i < circles.Count; i++)
        {
            var c = circles[i];
            int ctr = Int(c, "center");
            var colour = ColourFor("circle", i, PointDefined(ctr) && CircleDefined(i));
            ds.DrawCircle(P(ctr), (float)Num(c, "radius") * _scale, colour, IsSelected("circle", i) ? 2.5f : 1.6f,
                          Flag(c, "construction") ? _dashed : null);
        }
        var arcs = Array("arcs");
        for (int i = 0; i < arcs.Count; i++)
        {
            var a = arcs[i];
            int c = Int(a, "center"), s = Int(a, "start"), e = Int(a, "end");
            var colour = ColourFor("arc", i, PointDefined(c) && PointDefined(s) && PointDefined(e));
            double a0 = Math.Atan2(pts[s].Y - pts[c].Y, pts[s].X - pts[c].X);
            double a1 = Math.Atan2(pts[e].Y - pts[c].Y, pts[e].X - pts[c].X);
            double sweep = a1 - a0;
            if (sweep <= 0) sweep += 2 * Math.PI;
            double r = Math.Sqrt(Math.Pow(pts[s].X - pts[c].X, 2) + Math.Pow(pts[s].Y - pts[c].Y, 2));
            DrawArc(ds, pts[c].X, pts[c].Y, r, a0, sweep, colour, IsSelected("arc", i) ? 2.5f : 1.6f);
        }

        // End points: every point used by geometry, plus the origin.
        var used = lines.SelectMany(l => new[] { Int(l, "p1"), Int(l, "p2") })
            .Concat(arcs.SelectMany(a => new[] { Int(a, "center"), Int(a, "start"), Int(a, "end") }))
            .Concat(circles.Select(c => Int(c, "center"))).Append(0).Distinct();
        foreach (int i in used)
        {
            var p = P(i);
            var colour = ColourFor("point", i, PointDefined(i));
            ds.FillRectangle(p.X - 2.5f, p.Y - 2.5f, 5, 5, colour);
        }

        DrawDimensions(ds, pts);
        DrawPreview(ds);

        if (_editor["snap"] is JsonObject snap)
        {
            var p = ToScreen(Num(snap, "x"), Num(snap, "y"));
            var kind = snap["kind"]?.GetValue<string>() ?? "";
            var colour = Color.FromArgb(255, 255, 170, 0);
            ds.DrawCircle(p, 6, colour, 1.5f);
            if (kind is "horizontal" or "vertical")
                ds.DrawText(kind == "horizontal" ? "—" : "|", p + new Vector2(10, 4), colour);
        }
    }

    private void DrawArc(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, double cx, double cy, double r, double start, double sweep,
                         Color colour, float width, CanvasStrokeStyle? style = null)
    {
        // Screen y points down, so counter-clockwise in the sketch is clockwise on screen.
        using var path = new CanvasPathBuilder(ds);
        path.BeginFigure(ToScreen(cx + r * Math.Cos(start), cy + r * Math.Sin(start)));
        path.AddArc(ToScreen(cx, cy), (float)r * _scale, (float)r * _scale, -(float)start, -(float)sweep);
        path.EndFigure(CanvasFigureLoop.Open);
        using var geometry = CanvasGeometry.CreatePath(path);
        if (style != null) ds.DrawGeometry(geometry, colour, width, style);
        else ds.DrawGeometry(geometry, colour, width);
    }

    private void DrawPreview(Microsoft.Graphics.Canvas.CanvasDrawingSession ds)
    {
        if (_editor["preview"] is not JsonObject pv) return;
        foreach (var l in pv["lines"] as JsonArray ?? new JsonArray())
            ds.DrawLine(ToScreen(Num(l, "x1"), Num(l, "y1")), ToScreen(Num(l, "x2"), Num(l, "y2")), Preview, 1.2f,
                        Flag(l, "construction") ? _dashed : _solid);
        foreach (var c in pv["circles"] as JsonArray ?? new JsonArray())
            ds.DrawCircle(ToScreen(Num(c, "cx"), Num(c, "cy")), (float)Num(c, "r") * _scale, Preview, 1.2f);
        foreach (var a in pv["arcs"] as JsonArray ?? new JsonArray())
        {
            double a0 = Num(a, "start_angle") * Math.PI / 180, a1 = Num(a, "end_angle") * Math.PI / 180;
            DrawArc(ds, Num(a, "cx"), Num(a, "cy"), Num(a, "r"), a0, a1 - a0, Preview, 1.2f);
        }
    }

    private void DrawDimensions(Microsoft.Graphics.Canvas.CanvasDrawingSession ds, (double X, double Y)[] pts)
    {
        var format = new CanvasTextFormat { FontSize = 12, HorizontalAlignment = CanvasHorizontalAlignment.Center };
        var lines = Array("lines");
        var circles = Array("circles");
        var arcs = Array("arcs");
        foreach (var c in Array("constraints"))
        {
            if (c?["name"] is null) continue;
            string type = c["type"]!.GetValue<string>();
            int a = Int(c, "a"), b = Int(c, "b");
            double v = Num(c, "value");
            string kindA = c["kindA"]?.GetValue<string>() ?? "point";
            (double X, double Y) at;
            string text = v.ToString("0.##", CultureInfo.InvariantCulture);
            switch (type)
            {
                case "length" when a < lines.Count:
                    var p1 = pts[Int(lines[a], "p1")]; var p2 = pts[Int(lines[a], "p2")];
                    at = ((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);
                    break;
                case "radius" or "diameter":
                    int ctr = kindA == "arc" ? Int(arcs[a], "center") : Int(circles[a], "center");
                    double r = kindA == "arc"
                        ? Math.Sqrt(Math.Pow(pts[Int(arcs[a], "start")].X - pts[ctr].X, 2) + Math.Pow(pts[Int(arcs[a], "start")].Y - pts[ctr].Y, 2))
                        : Num(circles[a], "radius");
                    at = (pts[ctr].X + r * 0.7071, pts[ctr].Y + r * 0.7071);
                    text = (type == "diameter" ? "Ø" : "R") + text;
                    break;
                case "angle":
                    var q = pts[Int(lines[a], "p1")];
                    at = (q.X, q.Y);
                    text += "°";
                    break;
                default:
                    if (a < 0 || b < 0 || a >= pts.Length || b >= pts.Length) continue;
                    at = ((pts[a].X + pts[b].X) / 2, (pts[a].Y + pts[b].Y) / 2);
                    break;
            }
            ds.DrawText(text, ToScreen(at.X, at.Y) + new Vector2(0, -20), DimensionText, format);
        }
    }

    // Input

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(this);
        Focus(FocusState.Pointer);
        var pos = new Vector2((float)p.Position.X, (float)p.Position.Y);
        if (p.Properties.IsLeftButtonPressed)
        {
            bool add = (e.KeyModifiers & VirtualKeyModifiers.Control) != 0 || (e.KeyModifiers & VirtualKeyModifiers.Shift) != 0;
            Send(new JsonObject { ["event"] = "click", ["add"] = add }, pos);
        }
        else
        {
            _panFrom = pos;
            CapturePointer(e.Pointer);
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(this).Position;
        var pos = new Vector2((float)p.X, (float)p.Y);
        if (_panFrom is { } from)
        {
            _pan += pos - from;
            _panFrom = pos;
            _canvas.Invalidate();
            return;
        }
        Send(new JsonObject { ["event"] = "move" }, pos);
    }

    private void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(this);
        float factor = MathF.Pow(1.0015f, p.Properties.MouseWheelDelta);
        var cursor = new Vector2((float)p.Position.X, (float)p.Position.Y) - Centre;
        _pan = cursor + (_pan - cursor) * factor;
        _scale *= factor;
        _canvas.Invalidate();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape: Escape(); break;
            case VirtualKey.Delete: Send(new JsonObject { ["event"] = "delete" }); break;
            case VirtualKey.F: ZoomToFit(); break;
            default: return;
        }
        e.Handled = true;
    }
}
