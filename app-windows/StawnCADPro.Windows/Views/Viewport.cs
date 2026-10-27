using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Stawn.Model;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace Stawn.Views;

public enum DisplayStyle { ShadedWithEdges, Shaded, Wireframe }

public enum StandardView { Front, Back, Top, Bottom, Right, Left, Isometric }

/// <summary>
/// Graphics area, drawn with Win2D: the part shaded with edges on a blue gradient, the
/// reference triad and the view name, and the open sketch on its plane. Rotate with the middle
/// button (or the left button when not sketching), pan with the right button or Ctrl + middle,
/// zoom with the wheel towards the cursor; F zooms to fit and Ctrl+8 looks normal to the sketch.
/// Triangles and edges are depth-sorted and painted back to front with a headlight.
/// </summary>
public sealed class Viewport : UserControl
{
    private static readonly Color BackgroundTop = Color.FromArgb(255, 176, 196, 222);
    private static readonly Color BackgroundBottom = Color.FromArgb(255, 252, 252, 253);
    private static readonly Color Free = Color.FromArgb(255, 0, 70, 255);
    private static readonly Color Defined = Colors.Black;
    private static readonly Color Over = Color.FromArgb(255, 225, 30, 30);
    private static readonly Color Highlight = Color.FromArgb(255, 255, 128, 0);
    private static readonly Color Rubber = Color.FromArgb(255, 90, 90, 90);
    private static readonly Color DimensionInk = Color.FromArgb(255, 20, 20, 20);

    private readonly CanvasControl _canvas = new();
    private readonly CanvasStrokeStyle _dashed = new() { DashStyle = CanvasDashStyle.Dash };
    private readonly CanvasStrokeStyle _centerline = new() { CustomDashStyle = new[] { 8f, 3f, 2f, 3f } };
    private MeshBuffers? _mesh;
    private Quaternion _rotation = ViewRotation(StandardView.Isometric);
    private Vector2 _pan;           // screen offset in pixels
    private float _scale = 1;       // pixels per millimetre
    private Point? _dragStart;
    private (Vector3 Min, Vector3 Max)? _framedBounds;
    public StandardView? CurrentView { get; private set; } = StandardView.Isometric;

    private DisplayStyle _style = DisplayStyle.ShadedWithEdges;
    public DisplayStyle Shading { get => _style; set { _style = value; _canvas.Invalidate(); } }

    /// <summary>Raised when the view orientation changes, with the name shown in the corner.</summary>
    public event Action<string>? ViewNameChanged;

    public Viewport()
    {
        Content = _canvas;
        _canvas.ClearColor = Colors.Transparent;
        _canvas.Draw += OnDraw;
        _canvas.SizeChanged += (_, _) => { if (_mesh != null && _framedBounds == null) ZoomToFit(); _canvas.Invalidate(); };
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += (_, e) => { _dragStart = null; ReleasePointerCapture(e.Pointer); };
        PointerWheelChanged += OnWheel;
        IsTabStop = true;
        KeyDown += OnKeyDown;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
    }

    /// <summary>Shows a new mesh; zooms to fit on first show and whenever a rebuild changes the part's size.</summary>
    public void SetMesh(MeshBuffers? mesh)
    {
        _mesh = mesh;
        if (mesh != null && BoundsChanged(mesh) && !Sketching) ZoomToFit();
        _canvas.Invalidate();
    }

    public string ViewName => CurrentView?.ToString() ?? (Sketching ? "Normal To" : "Custom");

    public void ShowView(StandardView view)
    {
        CurrentView = view;
        _rotation = ViewRotation(view);
        ZoomToFit();
        ViewNameChanged?.Invoke(ViewName);
    }

    public void ZoomToFit()
    {
        if (_canvas.ActualWidth <= 0) return;
        var (min, max) = FitBounds();
        if (_mesh != null) _framedBounds = (_mesh.Min, _mesh.Max);
        var centre = (min + max) / 2;
        float radius = MathF.Max((max - min).Length() / 2, 1);
        _scale = (float)Math.Min(_canvas.ActualWidth, _canvas.ActualHeight) / (2.3f * radius);
        _pan = -Project(centre, Vector2.Zero);
        _canvas.Invalidate();
    }

    private (Vector3 Min, Vector3 Max) FitBounds()
    {
        var pts = new List<Vector3>();
        if (_mesh != null) { pts.Add(_mesh.Min); pts.Add(_mesh.Max); }
        if (Sketching)
        {
            pts.Add(_so);
            foreach (var p in SketchPoints()) pts.Add(SketchToModel(p.X, p.Y));
            foreach (var c in Geometry("circles"))
            {
                var ctr = SketchPoints()[Int(c, "center")];
                double r = Num(c, "radius");
                pts.Add(SketchToModel(ctr.X - r, ctr.Y - r));
                pts.Add(SketchToModel(ctr.X + r, ctr.Y + r));
            }
            if (pts.Count < 4) { pts.Add(SketchToModel(-25, -25)); pts.Add(SketchToModel(25, 25)); }
        }
        if (pts.Count == 0) return (new Vector3(-25), new Vector3(25));
        var min = pts.Aggregate(Vector3.Min);
        var max = pts.Aggregate(Vector3.Max);
        return (min, max);
    }

    private bool BoundsChanged(MeshBuffers m)
    {
        if (_framedBounds is not { } old) return true;
        float size = MathF.Max((m.Max - m.Min).Length(), 1);
        return (old.Min - m.Min).Length() > 0.02f * size || (old.Max - m.Max).Length() > 0.02f * size;
    }

    /// <summary>Rotation taking model coordinates to view coordinates (x right, y up, z towards the viewer).</summary>
    private static Quaternion ViewRotation(StandardView v)
    {
        (Vector3 eye, Vector3 up) = v switch
        {
            StandardView.Front => (Vector3.UnitZ, Vector3.UnitY),
            StandardView.Back => (-Vector3.UnitZ, Vector3.UnitY),
            StandardView.Top => (Vector3.UnitY, -Vector3.UnitZ),
            StandardView.Bottom => (-Vector3.UnitY, Vector3.UnitZ),
            StandardView.Right => (Vector3.UnitX, Vector3.UnitY),
            StandardView.Left => (-Vector3.UnitX, Vector3.UnitY),
            _ => (Vector3.Normalize(Vector3.One), Vector3.UnitY),
        };
        return LookAlong(eye, up);
    }

    private static Quaternion LookAlong(Vector3 eye, Vector3 up) =>
        Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateLookAt(eye, Vector3.Zero, up));

    private Vector2 Centre => new((float)_canvas.ActualWidth / 2, (float)_canvas.ActualHeight / 2);

    private Vector2 Project(Vector3 p, Vector2 pan)
    {
        var v = Vector3.Transform(p, _rotation);
        return new Vector2(v.X * _scale, -v.Y * _scale) + pan;
    }

    private Vector2 ToScreen(Vector3 p) => Project(p, Centre + _pan);

    // ---------------------------------------------------------------- Sketch on its plane

    private PartModel? _sketchModel;
    private JsonObject _sketch = new();   // get_sketch reply
    private JsonObject _editor = new();   // last sketch_input reply
    private Vector3 _so, _sx = Vector3.UnitX, _sy = Vector3.UnitY, _sn = Vector3.UnitZ;
    private Point _lastPointer;

    public bool Sketching => _sketchModel != null;
    public string SketchName => _sketch["sketch"]?.GetValue<string>() ?? "";
    public string SketchStatus => _sketch["status"]?.GetValue<string>() ?? "";
    public int DegreesOfFreedom => _sketch["degrees_of_freedom"]?.GetValue<int>() ?? 0;
    /// <summary>Cursor position on the sketch plane in mm, for the status bar.</summary>
    public Vector2? SketchCursor { get; private set; }

    /// <summary>Raised after each sketch input with the event name, the editor reply and the pointer position.</summary>
    public event Action<string, JsonObject, Point>? SketchChanged;

    /// <summary>Opens a sketch (the reply of edit_sketch) on its plane and turns the view normal to it.</summary>
    public void BeginSketch(PartModel model, JsonObject state)
    {
        _sketchModel = model;
        _sketch = state;
        _editor = new JsonObject();
        if (state["axes"] is JsonObject axes)
        {
            static Vector3 V(JsonNode? n) => n is JsonArray a ? new Vector3((float)a[0]!.GetValue<double>(), (float)a[1]!.GetValue<double>(), (float)a[2]!.GetValue<double>()) : Vector3.Zero;
            _so = V(axes["origin"]); _sx = V(axes["x"]); _sy = V(axes["y"]); _sn = V(axes["normal"]);
        }
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Cross);
        NormalTo();
        Focus(FocusState.Programmatic);
    }

    public void EndSketch()
    {
        _sketchModel = null;
        _sketch = new JsonObject();
        _editor = new JsonObject();
        SketchCursor = null;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
        _canvas.Invalidate();
    }

    /// <summary>Looks straight at the sketch plane, its x axis to the right.</summary>
    public void NormalTo()
    {
        if (!Sketching) return;
        _rotation = LookAlong(_sn, _sy);
        CurrentView = null;
        ZoomToFit();
        ViewNameChanged?.Invoke(ViewName);
    }

    public void SetSketchTool(string tool) => SendSketch(new JsonObject { ["event"] = "tool", ["tool"] = tool });
    public void AddRelation(int index) => SendSketch(new JsonObject { ["event"] = "relation", ["index"] = index });
    public void AddDimension(int option, double value) => SendSketch(new JsonObject { ["event"] = "dimension", ["option"] = option, ["value"] = value });
    public void SketchEscape() => SendSketch(new JsonObject { ["event"] = "escape" });
    public void SketchDelete() => SendSketch(new JsonObject { ["event"] = "delete" });

    private void SendSketch(JsonObject e, Vector2? sketchPoint = null)
    {
        if (_sketchModel == null) return;
        e["op"] = "sketch_input";
        if (sketchPoint is { } p) { e["x"] = p.X; e["y"] = p.Y; }
        e["tol"] = 8 / _scale;  // snap within 8 pixels
        var reply = _sketchModel.Command(e);
        if (reply["ok"]?.GetValue<bool>() != true) return;
        _editor = reply;
        if (reply["changed"]?.GetValue<bool>() == true) ReloadSketch();
        _canvas.Invalidate();
        SketchChanged?.Invoke(e["event"]!.GetValue<string>(), reply, _lastPointer);
    }

    /// <summary>A click or pointer move at sketch coordinates (mm), as the mouse would send.</summary>
    public void SketchClick(double x, double y)
    {
        var s = S(x, y);
        _lastPointer = new Point(s.X, s.Y);
        SendSketch(new JsonObject { ["event"] = "click" }, new Vector2((float)x, (float)y));
    }

    public void SketchMove(double x, double y)
    {
        var s = S(x, y);
        _lastPointer = new Point(s.X, s.Y);
        SketchCursor = new Vector2((float)x, (float)y);
        SendSketch(new JsonObject { ["event"] = "move" }, SketchCursor);
    }

    public void ReloadSketch()
    {
        if (_sketchModel == null) return;
        var s = _sketchModel.Command(new JsonObject { ["op"] = "get_sketch" });
        if (s["ok"]?.GetValue<bool>() == true) _sketch = s;
        _canvas.Invalidate();
    }

    private Vector3 SketchToModel(double x, double y) => _so + (float)x * _sx + (float)y * _sy;
    private Vector2 S(double x, double y) => ToScreen(SketchToModel(x, y));

    /// <summary>Where the view ray through a screen point meets the sketch plane, in sketch mm.</summary>
    private Vector2? ScreenToSketch(Point screen)
    {
        var c = Centre + _pan;
        var view = new Vector3(((float)screen.X - c.X) / _scale, -((float)screen.Y - c.Y) / _scale, 0);
        var inverse = Quaternion.Inverse(_rotation);
        var a = Vector3.Transform(view, inverse);
        var d = Vector3.Transform(Vector3.UnitZ, inverse);
        float dn = Vector3.Dot(d, _sn);
        if (MathF.Abs(dn) < 1e-4f) return null;  // looking along the plane
        var p = a + Vector3.Dot(_so - a, _sn) / dn * d;
        return new Vector2(Vector3.Dot(p - _so, _sx), Vector3.Dot(p - _so, _sy));
    }

    private JsonArray Geometry(string key) => _sketch["geometry"]?[key] as JsonArray ?? new JsonArray();
    private static int Int(JsonNode? n, string key) => n?[key]?.GetValue<int>() ?? -1;
    private static double Num(JsonNode? n, string key) => n?[key]?.GetValue<double>() ?? 0;
    private static bool Flag(JsonNode? n, string key) => n?[key]?.GetValue<bool>() ?? false;
    private (double X, double Y)[] SketchPoints() => Geometry("points").Select(p => (Num(p, "x"), Num(p, "y"))).ToArray();

    private bool IsDefined(string key, int i) =>
        _sketch["defined"]?[key] is JsonArray a && i >= 0 && i < a.Count && a[i]!.GetValue<bool>();

    private bool IsSelected(string kind, int index) =>
        (_editor["selection"] as JsonArray)?.Any(s => s?["kind"]?.GetValue<string>() == kind && Int(s, "index") == index) == true;

    private Color SketchColour(string kind, int index, bool defined)
    {
        if (IsSelected(kind, index)) return Highlight;
        if (SketchStatus == "Over Defined") return Over;
        return defined ? Defined : Free;
    }

    // ---------------------------------------------------------------- Drawing

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        var size = sender.Size;
        using (var bg = new CanvasLinearGradientBrush(ds, BackgroundTop, BackgroundBottom)
               { StartPoint = new Vector2(0, 0), EndPoint = new Vector2(0, (float)size.Height) })
            ds.FillRectangle(0, 0, (float)size.Width, (float)size.Height, bg);
        if (_mesh != null) DrawMesh(ds);
        if (Sketching) DrawSketch(ds);
        DrawTriad(ds, new Vector2(48, (float)size.Height - 48));
    }

    private void DrawMesh(CanvasDrawingSession ds)
    {
        var m = _mesh!;
        var origin = Centre + _pan;
        var view = new Vector3[m.Positions.Length];
        for (int i = 0; i < view.Length; i++) view[i] = Vector3.Transform(m.Positions[i], _rotation);
        Vector2 Screen(Vector3 v) => new Vector2(v.X * _scale, -v.Y * _scale) + origin;

        // Painter's algorithm: faces and edges sorted by depth, edges nudged towards the viewer
        // so that visible edges draw over the faces they bound.
        var items = new List<(float Depth, int Index, bool Edge)>(m.Indices.Length / 3 + m.EdgeLines.Length / 2);
        if (_style != DisplayStyle.Wireframe)
            for (int t = 0; t < m.Indices.Length / 3; t++)
            {
                var a = view[m.Indices[3 * t]]; var b = view[m.Indices[3 * t + 1]]; var c = view[m.Indices[3 * t + 2]];
                items.Add(((a.Z + b.Z + c.Z) / 3, t, false));
            }
        float bias = (m.Max - m.Min).Length() * 0.004f;
        if (_style != DisplayStyle.Shaded)
            for (int s = 0; s < m.EdgeLines.Length / 2; s++)
            {
                var a = Vector3.Transform(m.EdgeLines[2 * s], _rotation);
                var b = Vector3.Transform(m.EdgeLines[2 * s + 1], _rotation);
                items.Add((MathF.Max(a.Z, b.Z) + bias, s, true));
            }
        items.Sort((x, y) => x.Depth.CompareTo(y.Depth));

        var normalRotation = Matrix4x4.CreateFromQuaternion(_rotation);
        var points = new Vector2[3];
        // While sketching the part is drawn lighter so the sketch stands out.
        float fade = Sketching ? 0.55f : 0;
        foreach (var (_, i, edge) in items)
        {
            if (edge)
            {
                var a = Vector3.Transform(m.EdgeLines[2 * i], _rotation);
                var b = Vector3.Transform(m.EdgeLines[2 * i + 1], _rotation);
                ds.DrawLine(Screen(a), Screen(b), Sketching ? Color.FromArgb(255, 110, 110, 110) : Colors.Black, 1.1f);
                continue;
            }
            uint i0 = m.Indices[3 * i], i1 = m.Indices[3 * i + 1], i2 = m.Indices[3 * i + 2];
            var n = Vector3.Normalize(Vector3.TransformNormal(m.Normals[i0] + m.Normals[i1] + m.Normals[i2], normalRotation));
            float light = 0.38f + 0.62f * MathF.Abs(n.Z);  // headlight, double-sided
            float spec = MathF.Pow(MathF.Abs(n.Z), 30) * 0.22f;
            float g = (0.80f * light + spec) * (1 - fade) + fade;
            points[0] = Screen(view[i0]); points[1] = Screen(view[i1]); points[2] = Screen(view[i2]);
            using var geometry = CanvasGeometry.CreatePolygon(ds, points);
            var colour = Color.FromArgb(255, Byte(g * 0.93f), Byte(g * 0.95f), Byte(g));
            ds.FillGeometry(geometry, colour);
            ds.DrawGeometry(geometry, colour, 0.6f);  // hides hairline seams between triangles
        }
    }

    private static byte Byte(float v) => (byte)Math.Clamp(v * 255, 0, 255);

    private void DrawTriad(CanvasDrawingSession ds, Vector2 at)
    {
        var axes = new[] { (Vector3.UnitX, "X", Color.FromArgb(255, 220, 40, 40)), (Vector3.UnitY, "Y", Color.FromArgb(255, 30, 160, 60)),
                           (Vector3.UnitZ, "Z", Color.FromArgb(255, 40, 80, 220)) };
        var format = new CanvasTextFormat { FontSize = 11, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        foreach (var (axis, label, colour) in axes.OrderBy(a => Vector3.Transform(a.Item1, _rotation).Z))
        {
            var v = Vector3.Transform(axis, _rotation);
            var end = at + new Vector2(v.X, -v.Y) * 30;
            ds.DrawLine(at, end, colour, 2.2f);
            ds.DrawText(label, end + new Vector2(v.X, -v.Y) * 6 - new Vector2(4, 7), colour, format);
        }
    }

    private void DrawSketch(CanvasDrawingSession ds)
    {
        var pts = SketchPoints();
        Vector2 P(int i) => S(pts[i].X, pts[i].Y);

        // Sketch origin: two short red arrows along the plane's axes.
        var o = S(0, 0);
        var red = Color.FromArgb(255, 200, 30, 30);
        ds.DrawLine(o, S(0, 0) + (S(1, 0) - o) / Math.Max((S(1, 0) - o).Length(), 1e-3f) * 26, red, 1.5f);
        ds.DrawLine(o, S(0, 0) + (S(0, 1) - o) / Math.Max((S(0, 1) - o).Length(), 1e-3f) * 26, red, 1.5f);

        var lines = Geometry("lines");
        for (int i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            int a = Int(l, "p1"), b = Int(l, "p2");
            var colour = SketchColour("line", i, IsDefined("points", a) && IsDefined("points", b));
            if (Flag(l, "construction")) ds.DrawLine(P(a), P(b), colour, 1.2f, _centerline);
            else ds.DrawLine(P(a), P(b), colour, IsSelected("line", i) ? 2.6f : 1.6f);
        }
        var circles = Geometry("circles");
        for (int i = 0; i < circles.Count; i++)
        {
            var c = circles[i];
            int ctr = Int(c, "center");
            var colour = SketchColour("circle", i, IsDefined("points", ctr) && IsDefined("circles", i));
            DrawArc(ds, pts[ctr].X, pts[ctr].Y, Num(c, "radius"), 0, 2 * Math.PI, colour, IsSelected("circle", i) ? 2.6f : 1.6f,
                    Flag(c, "construction") ? _dashed : null);
        }
        var arcs = Geometry("arcs");
        for (int i = 0; i < arcs.Count; i++)
        {
            var a = arcs[i];
            int c = Int(a, "center"), s = Int(a, "start"), e = Int(a, "end");
            var colour = SketchColour("arc", i, IsDefined("points", c) && IsDefined("points", s) && IsDefined("points", e));
            double a0 = Math.Atan2(pts[s].Y - pts[c].Y, pts[s].X - pts[c].X);
            double a1 = Math.Atan2(pts[e].Y - pts[c].Y, pts[e].X - pts[c].X);
            double sweep = a1 - a0;
            if (sweep <= 0) sweep += 2 * Math.PI;
            double r = Math.Sqrt(Math.Pow(pts[s].X - pts[c].X, 2) + Math.Pow(pts[s].Y - pts[c].Y, 2));
            DrawArc(ds, pts[c].X, pts[c].Y, r, a0, sweep, colour, IsSelected("arc", i) ? 2.6f : 1.6f);
        }

        var used = lines.SelectMany(l => new[] { Int(l, "p1"), Int(l, "p2") })
            .Concat(arcs.SelectMany(a => new[] { Int(a, "center"), Int(a, "start"), Int(a, "end") }))
            .Concat(circles.Select(c => Int(c, "center"))).Distinct();
        foreach (int i in used)
        {
            if (i < 0 || i >= pts.Length) continue;
            var p = P(i);
            ds.FillRectangle(p.X - 2.5f, p.Y - 2.5f, 5, 5, SketchColour("point", i, IsDefined("points", i)));
        }

        DrawDimensions(ds, pts, lines, circles, arcs);
        DrawRubberBand(ds);

        if (_editor["snap"] is JsonObject snap)
        {
            var p = S(Num(snap, "x"), Num(snap, "y"));
            var kind = snap["kind"]?.GetValue<string>() ?? "";
            var colour = Color.FromArgb(255, 255, 150, 0);
            ds.DrawCircle(p, 6, colour, 1.6f);
            // Inference symbols next to the cursor, as the relation the click will add.
            if (kind is "horizontal" or "vertical" or "origin" or "point")
            {
                var box = new Rect(p.X + 10, p.Y + 8, 18, 16);
                ds.FillRectangle(box, Color.FromArgb(255, 255, 248, 200));
                ds.DrawRectangle(box, colour, 1);
                var c = new Vector2((float)box.X + 9, (float)box.Y + 8);
                if (kind == "horizontal") ds.DrawLine(c - new Vector2(6, 0), c + new Vector2(6, 0), Colors.Black, 1.6f);
                else if (kind == "vertical") ds.DrawLine(c - new Vector2(0, 6), c + new Vector2(0, 6), Colors.Black, 1.6f);
                else { ds.DrawCircle(c - new Vector2(2, 0), 3.5f, Colors.Black, 1.2f); ds.DrawCircle(c + new Vector2(2, 0), 3.5f, Colors.Black, 1.2f); }
            }
        }
    }

    private void DrawArc(CanvasDrawingSession ds, double cx, double cy, double r, double start, double sweep, Color colour, float width,
                         CanvasStrokeStyle? style = null)
    {
        // Through the projection as a polyline, so circles stay right when the view is rotated.
        int n = Math.Max(8, (int)(64 * sweep / (2 * Math.PI)));
        using var path = new CanvasPathBuilder(ds);
        path.BeginFigure(S(cx + r * Math.Cos(start), cy + r * Math.Sin(start)));
        for (int i = 1; i <= n; i++)
        {
            double t = start + sweep * i / n;
            path.AddLine(S(cx + r * Math.Cos(t), cy + r * Math.Sin(t)));
        }
        path.EndFigure(CanvasFigureLoop.Open);
        using var geometry = CanvasGeometry.CreatePath(path);
        if (style != null) ds.DrawGeometry(geometry, colour, width, style);
        else ds.DrawGeometry(geometry, colour, width);
    }

    private void DrawRubberBand(CanvasDrawingSession ds)
    {
        if (_editor["preview"] is not JsonObject pv) return;
        foreach (var l in pv["lines"] as JsonArray ?? new JsonArray())
        {
            var a = S(Num(l, "x1"), Num(l, "y1"));
            var b = S(Num(l, "x2"), Num(l, "y2"));
            if (Flag(l, "construction")) ds.DrawLine(a, b, Rubber, 1.2f, _centerline);
            else ds.DrawLine(a, b, Free, 1.4f);
            // Length readout at the cursor, as the line is drawn.
            double len = Math.Sqrt(Math.Pow(Num(l, "x2") - Num(l, "x1"), 2) + Math.Pow(Num(l, "y2") - Num(l, "y1"), 2));
            ds.DrawText(len.ToString("0.##", CultureInfo.InvariantCulture), b + new Vector2(14, -22), Rubber,
                        new CanvasTextFormat { FontSize = 11 });
        }
        foreach (var c in pv["circles"] as JsonArray ?? new JsonArray())
            DrawArc(ds, Num(c, "cx"), Num(c, "cy"), Num(c, "r"), 0, 2 * Math.PI, Free, 1.4f);
        foreach (var a in pv["arcs"] as JsonArray ?? new JsonArray())
        {
            double a0 = Num(a, "start_angle") * Math.PI / 180, a1 = Num(a, "end_angle") * Math.PI / 180;
            DrawArc(ds, Num(a, "cx"), Num(a, "cy"), Num(a, "r"), a0, a1 - a0, Free, 1.4f);
        }
    }

    private void DrawDimensions(CanvasDrawingSession ds, (double X, double Y)[] pts, JsonArray lines, JsonArray circles, JsonArray arcs)
    {
        var format = new CanvasTextFormat { FontSize = 12, HorizontalAlignment = CanvasHorizontalAlignment.Center, VerticalAlignment = CanvasVerticalAlignment.Center };
        float px = 1 / _scale;  // one screen pixel in mm
        foreach (var c in Geometry("constraints"))
        {
            if (c?["name"] is null) continue;
            string type = c["type"]!.GetValue<string>();
            int a = Int(c, "a"), b = Int(c, "b");
            string kindA = c["kindA"]?.GetValue<string>() ?? "point";
            string text = Num(c, "value").ToString("0.##", CultureInfo.InvariantCulture);
            Vector2 label;
            switch (type)
            {
                case "length" or "distance" or "horizontalDistance" or "verticalDistance":
                {
                    (double X, double Y) p1, p2;
                    if (type == "length")
                    {
                        if (a < 0 || a >= lines.Count) continue;
                        p1 = pts[Int(lines[a], "p1")]; p2 = pts[Int(lines[a], "p2")];
                    }
                    else
                    {
                        if (a < 0 || b < 0 || a >= pts.Length || b >= pts.Length) continue;
                        p1 = pts[a]; p2 = pts[b];
                        if (type == "horizontalDistance") p2 = (p2.X, p1.Y);
                        if (type == "verticalDistance") p2 = (p1.X, p2.Y);
                    }
                    // Dimension line offset 22 px to the left of p1→p2, with extension lines and arrows.
                    double dx = p2.X - p1.X, dy = p2.Y - p1.Y, len = Math.Sqrt(dx * dx + dy * dy);
                    if (len < 1e-9) continue;
                    double nx = -dy / len * 22 * px, ny = dx / len * 22 * px;
                    var e1 = S(p1.X + nx, p1.Y + ny); var e2 = S(p2.X + nx, p2.Y + ny);
                    ds.DrawLine(S(p1.X, p1.Y), e1, DimensionInk, 0.8f);
                    ds.DrawLine(S(p2.X, p2.Y), e2, DimensionInk, 0.8f);
                    ds.DrawLine(e1, e2, DimensionInk, 0.9f);
                    Arrow(ds, e2, e1);
                    Arrow(ds, e1, e2);
                    label = (e1 + e2) / 2;
                    break;
                }
                case "radius" or "diameter":
                {
                    bool isArc = kindA == "arc";
                    if (a < 0 || a >= (isArc ? arcs.Count : circles.Count)) continue;
                    int ctr = isArc ? Int(arcs[a], "center") : Int(circles[a], "center");
                    double r = isArc
                        ? Math.Sqrt(Math.Pow(pts[Int(arcs[a], "start")].X - pts[ctr].X, 2) + Math.Pow(pts[Int(arcs[a], "start")].Y - pts[ctr].Y, 2))
                        : Num(circles[a], "radius");
                    var rim = S(pts[ctr].X + r * 0.7071, pts[ctr].Y + r * 0.7071);
                    var outside = S(pts[ctr].X + (r + 30 * px) * 0.7071, pts[ctr].Y + (r + 30 * px) * 0.7071);
                    ds.DrawLine(type == "diameter" ? S(pts[ctr].X - r * 0.7071, pts[ctr].Y - r * 0.7071) : S(pts[ctr].X, pts[ctr].Y), outside, DimensionInk, 0.9f);
                    Arrow(ds, outside, rim);
                    text = (type == "diameter" ? "Ø" : "R") + text;
                    label = outside + new Vector2(18, -8);
                    break;
                }
                case "angle":
                    if (a < 0 || a >= lines.Count) continue;
                    var q = pts[Int(lines[a], "p1")];
                    label = S(q.X, q.Y) + new Vector2(24, -18);
                    text += "°";
                    break;
                default:
                    continue;
            }
            var bounds = new Rect(label.X - 4 - text.Length * 3.6, label.Y - 9, text.Length * 7.2 + 8, 18);
            ds.FillRectangle(bounds, Color.FromArgb(230, 255, 255, 255));
            ds.DrawText(text, label, DimensionInk, format);
        }
    }

    private static void Arrow(CanvasDrawingSession ds, Vector2 from, Vector2 tip)
    {
        var d = tip - from;
        if (d.Length() < 1e-3f) return;
        d = Vector2.Normalize(d);
        var side = new Vector2(-d.Y, d.X);
        var a = tip - d * 9 + side * 3;
        var b = tip - d * 9 - side * 3;
        using var g = CanvasGeometry.CreatePolygon(ds, new[] { tip, a, b });
        ds.FillGeometry(g, DimensionInk);
    }

    // ---------------------------------------------------------------- Input

    private enum Drag { None, Orbit, Pan }
    private Drag _drag;

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(this);
        Focus(FocusState.Pointer);
        _lastPointer = p.Position;
        bool ctrl = (e.KeyModifiers & VirtualKeyModifiers.Control) != 0;
        bool shift = (e.KeyModifiers & VirtualKeyModifiers.Shift) != 0;
        var props = p.Properties;
        if (props.IsLeftButtonPressed && Sketching)
        {
            if (ScreenToSketch(p.Position) is { } at) SendSketch(new JsonObject { ["event"] = "click", ["add"] = ctrl || shift }, at);
            return;
        }
        _drag = props.IsRightButtonPressed || (props.IsMiddleButtonPressed && ctrl) ? Drag.Pan
              : props.IsMiddleButtonPressed || props.IsLeftButtonPressed ? Drag.Orbit : Drag.None;
        if (_drag == Drag.None) return;
        _dragStart = p.Position;
        CapturePointer(e.Pointer);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var pos = e.GetCurrentPoint(this).Position;
        _lastPointer = pos;
        if (_dragStart is not { } start)
        {
            if (Sketching && ScreenToSketch(pos) is { } at)
            {
                SketchCursor = at;
                SendSketch(new JsonObject { ["event"] = "move" }, at);
            }
            return;
        }
        var d = new Vector2((float)(pos.X - start.X), (float)(pos.Y - start.Y));
        _dragStart = pos;
        if (_drag == Drag.Orbit)
        {
            // Turntable orbit: horizontal drag spins about the screen's vertical axis,
            // vertical drag tilts about its horizontal axis.
            var spin = Quaternion.CreateFromAxisAngle(Vector3.UnitY, d.X * 0.01f);
            var tilt = Quaternion.CreateFromAxisAngle(Vector3.UnitX, d.Y * 0.01f);
            _rotation = Quaternion.Normalize(Quaternion.Concatenate(_rotation, Quaternion.Concatenate(spin, tilt)));
            if (CurrentView != null || !Sketching)
            {
                CurrentView = null;
                ViewNameChanged?.Invoke("Custom");
            }
        }
        else
        {
            _pan += d;
        }
        _canvas.Invalidate();
    }

    private void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(this);
        float factor = MathF.Pow(1.0015f, p.Properties.MouseWheelDelta);
        var cursor = new Vector2((float)p.Position.X, (float)p.Position.Y) - Centre;
        _pan = cursor + (_pan - cursor) * factor;  // keep the point under the cursor fixed
        _scale *= factor;
        _canvas.Invalidate();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                        .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        switch (e.Key)
        {
            case VirtualKey.F: ZoomToFit(); break;
            case VirtualKey.Number8 when ctrl: NormalTo(); break;
            case VirtualKey.Escape when Sketching: SketchEscape(); break;
            case VirtualKey.Delete when Sketching: SketchDelete(); break;
            default: return;
        }
        e.Handled = true;
    }
}
