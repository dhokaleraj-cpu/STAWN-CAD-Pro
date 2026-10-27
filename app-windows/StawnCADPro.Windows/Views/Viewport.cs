using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Stawn.Model;
using Windows.Foundation;
using Windows.UI;

namespace Stawn.Views;

public enum DisplayStyle { ShadedWithEdges, Shaded, Wireframe }

public enum StandardView { Front, Back, Top, Bottom, Right, Left, Isometric }

/// <summary>
/// 3D view of the part, drawn with Win2D. Orbit with the left mouse button, pan with the
/// middle or right button and zoom with the wheel (towards the cursor). Triangles and edges
/// are depth-sorted and painted back to front with a headlight, which is enough for single
/// parts; a Direct3D renderer replaces this for assemblies.
/// </summary>
public sealed class Viewport : UserControl
{
    private readonly CanvasControl _canvas = new();
    private MeshBuffers? _mesh;
    private Quaternion _rotation = ViewRotation(StandardView.Isometric);
    private Vector2 _pan;           // screen offset in pixels
    private float _scale = 1;       // pixels per millimetre
    private Point? _dragStart;
    private bool _orbiting;
    private (Vector3 Min, Vector3 Max)? _framedBounds;
    public StandardView CurrentView { get; private set; } = StandardView.Isometric;

    private DisplayStyle _style = DisplayStyle.ShadedWithEdges;
    public DisplayStyle Shading { get => _style; set { _style = value; _canvas.Invalidate(); } }

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
        KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.F) { ZoomToFit(); e.Handled = true; } };
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
    }

    /// <summary>Shows a new mesh; zooms to fit on first show and whenever a rebuild changes the part's size.</summary>
    public void SetMesh(MeshBuffers? mesh)
    {
        _mesh = mesh;
        if (mesh != null && BoundsChanged(mesh)) ZoomToFit();
        _canvas.Invalidate();
    }

    public void ShowView(StandardView view)
    {
        CurrentView = view;
        _rotation = ViewRotation(view);
        ZoomToFit();
    }

    public void ZoomToFit()
    {
        if (_mesh == null || _canvas.ActualWidth <= 0) return;
        _framedBounds = (_mesh.Min, _mesh.Max);
        var centre = (_mesh.Min + _mesh.Max) / 2;
        float radius = MathF.Max((_mesh.Max - _mesh.Min).Length() / 2, 1);
        _scale = (float)Math.Min(_canvas.ActualWidth, _canvas.ActualHeight) / (2.3f * radius);
        var c = Project(centre, Vector2.Zero);
        _pan = -c;
        _canvas.Invalidate();
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
        var look = Matrix4x4.CreateLookAt(eye, Vector3.Zero, up);
        return Quaternion.CreateFromRotationMatrix(look);
    }

    private Vector2 Project(Vector3 p, Vector2 pan)
    {
        var v = Vector3.Transform(p, _rotation);
        return new Vector2(v.X * _scale, -v.Y * _scale) + pan;
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        var size = sender.Size;
        using (var bg = new CanvasLinearGradientBrush(ds, Color.FromArgb(255, 247, 247, 247), Color.FromArgb(255, 199, 212, 230))
               { StartPoint = new Vector2(0, 0), EndPoint = new Vector2(0, (float)size.Height) })
            ds.FillRectangle(0, 0, (float)size.Width, (float)size.Height, bg);
        if (_mesh == null) return;

        var origin = new Vector2((float)size.Width / 2, (float)size.Height / 2) + _pan;
        var m = _mesh;
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
        foreach (var (_, i, edge) in items)
        {
            if (edge)
            {
                var a = Vector3.Transform(m.EdgeLines[2 * i], _rotation);
                var b = Vector3.Transform(m.EdgeLines[2 * i + 1], _rotation);
                ds.DrawLine(Screen(a), Screen(b), Colors.Black, 1.1f);
                continue;
            }
            uint i0 = m.Indices[3 * i], i1 = m.Indices[3 * i + 1], i2 = m.Indices[3 * i + 2];
            var n = Vector3.Normalize(Vector3.TransformNormal(m.Normals[i0] + m.Normals[i1] + m.Normals[i2], normalRotation));
            float light = 0.35f + 0.65f * MathF.Abs(n.Z);  // headlight, double-sided
            float spec = MathF.Pow(MathF.Abs(n.Z), 24) * 0.25f;
            byte g = (byte)Math.Clamp((0.78f * light + spec) * 255, 0, 255);
            points[0] = Screen(view[i0]); points[1] = Screen(view[i1]); points[2] = Screen(view[i2]);
            using var geometry = CanvasGeometry.CreatePolygon(ds, points);
            var colour = Color.FromArgb(255, g, g, (byte)Math.Min(255, g + 6));
            ds.FillGeometry(geometry, colour);
            ds.DrawGeometry(geometry, colour, 0.6f);  // hides hairline seams between triangles
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(this);
        Focus(FocusState.Pointer);
        _dragStart = p.Position;
        _orbiting = p.Properties.IsLeftButtonPressed;
        CapturePointer(e.Pointer);
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragStart is not { } start) return;
        var pos = e.GetCurrentPoint(this).Position;
        var d = new Vector2((float)(pos.X - start.X), (float)(pos.Y - start.Y));
        _dragStart = pos;
        if (_orbiting)
        {
            // Turntable orbit: horizontal drag spins about the screen's vertical axis,
            // vertical drag tilts about its horizontal axis.
            var spin = Quaternion.CreateFromAxisAngle(Vector3.UnitY, d.X * 0.01f);
            var tilt = Quaternion.CreateFromAxisAngle(Vector3.UnitX, d.Y * 0.01f);
            _rotation = Quaternion.Normalize(Quaternion.Concatenate(_rotation, Quaternion.Concatenate(spin, tilt)));  // model rotation first, then the drag
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
        var centre = new Vector2((float)ActualWidth / 2, (float)ActualHeight / 2);
        var cursor = new Vector2((float)p.Position.X, (float)p.Position.Y) - centre;
        _pan = cursor + (_pan - cursor) * factor;  // keep the point under the cursor fixed
        _scale *= factor;
        _canvas.Invalidate();
    }
}
