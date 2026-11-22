// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.Theming;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.CSG;
using Prowl.Vector;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>
/// Edit on a generated shape: a slider on each face of its bounds and measurements that can be clicked and typed into,
/// or for a polygon a point on each corner and a height arrow. Every change builds the shape again.
/// </summary>
[GlobalSceneTool(Group = "CSG")]
public sealed class CSGShapeTool : SceneTool
{
    public override string Name => "CSG Shape";
    public override string Icon => EditorIcons.DrawPolygon;
    public override int Order => 3;

    private const string FaceControl = "csg_shape_face";
    private const string LabelControl = "csg_shape_label";
    private const string CornerControl = "csg_shape_corner";
    private const string InsertControl = "csg_shape_insert";
    private const string HeightControl = "csg_shape_height";
    private const string EdgeControl = "csg_shape_edge";
    private const string RingControl = "csg_shape_ring";
    private const string QuarterControl = "csg_shape_quarter";
    private const string InnerControl = "csg_shape_inner";
    private const string TurnControl = "csg_shape_turn";
    private const string WallControl = "csg_shape_wall";

    private static readonly Color32 HandleColor = new(120, 200, 255, 255);
    private static readonly Color32 HotColor = new(255, 220, 90, 255);
    private static readonly Color32 LabelColor = new(230, 240, 255, 255);

    private static readonly Float3[] FaceAxes = [Float3.UnitX, -Float3.UnitX, Float3.UnitY, -Float3.UnitY, Float3.UnitZ, -Float3.UnitZ];

    // A drag in progress: which handle, and the shape as it was when the drag began
    private int _dragging = -1;
    private string _dragControl = "";
    private Float3 _startSize, _startCentre;
    private float _startAlong, _startHeight;
    private Float2[] _startOutline = [];
    private Float3 _startPoint, _startDirection;
    private float _startValue;

    // A measurement being typed: the axis it sizes, and the text so far
    private int _typingAxis = -1;
    private string _typed = "";

    public override bool IsAvailable(SceneToolContext ctx) => CSGSelection.ActiveModel().IsValid();

    /// <summary>The shape Edit works on: the one selected object's, or the shape its brush belongs to.</summary>
    public static CSGShape? Target()
    {
        if (!CSGEditMode.Is(CSGMode.Edit)) return null;
        List<GameObject> selected = Selection.GetSelected<GameObject>().ToList();
        if (selected.Count != 1) return null;

        CSGShape shape = selected[0].GetComponent<CSGShape>();
        if (shape.IsValid()) return shape;
        CSGBrush brush = selected[0].GetComponent<CSGBrush>();
        return brush.IsValid() ? CSGSelection.ShapeOf(brush) : null;
    }

    public override void OnDeactivated() => _typingAxis = -1;

    public override void OnSceneInput(SceneToolContext ctx)
    {
        CSGShape? shape = Target();
        if (shape.IsNotValid())
        {
            _dragging = -1;
            _typingAxis = -1;
            return;
        }

        if (shape!.Kind == CSGGeneratorKind.Polygon) PolygonHandles(ctx, shape);
        else
        {
            BoundsHandles(ctx, shape);
            Measurements(ctx, shape);
        }
    }

    // ================================================================
    //  Bounds
    // ================================================================

    private static bool StandsOnBase(CSGShape shape) => shape.Kind is CSGGeneratorKind.LinearStairs or CSGGeneratorKind.SpiralStairs;

    private static bool IsRound(CSGShape shape) => shape.Kind is CSGGeneratorKind.Cylinder or CSGGeneratorKind.Sphere or CSGGeneratorKind.SpiralStairs;

    // Every edge of the bounds as the two signed axes that meet there
    private static readonly (Float3 A, Float3 B)[] Edges = BuildEdges();

    private static (Float3, Float3)[] BuildEdges()
    {
        var edges = new List<(Float3, Float3)>();
        Float3[] axes = [Float3.UnitX, Float3.UnitY, Float3.UnitZ];
        for (int i = 0; i < 3; i++)
            for (int j = i + 1; j < 3; j++)
                foreach (float sa in new[] { 1f, -1f })
                    foreach (float sb in new[] { 1f, -1f })
                        edges.Add((axes[i] * sa, axes[j] * sb));
        return [.. edges];
    }

    private static readonly Float3[] Quarters = [Float3.UnitX, -Float3.UnitX, Float3.UnitZ, -Float3.UnitZ];

    /// <summary>
    /// The handles a shape's bounds offer. Boxes, rooms and stairs get a slider on every face and edge, round shapes
    /// their top and bottom sliders plus radius handles. Stairs also turn, and a room's wall thickness has a slider.
    /// </summary>
    private void BoundsHandles(SceneToolContext ctx, CSGShape shape)
    {
        HandleContext handles = ctx.Handles;
        Transform frame = shape.Transform;
        Float3 half = shape.Size * 0.5f;
        bool round = IsRound(shape);

        for (int face = 0; face < 6; face++)
        {
            if (face == 3 && StandsOnBase(shape)) continue;
            if (round && face != 2 && face != 3) continue;

            Float3 world = frame.Position + frame.Rotation * (FaceAxes[face] * half);
            if (Handle(ctx, FaceControl, face, world, 8f, HandleCap.Circle))
                BeginDrag(handles, shape, FaceControl, face, AlongAxis(handles.MouseRay, world, frame.Rotation * FaceAxes[face]), world);
        }

        if (!round)
        {
            for (int edge = 0; edge < Edges.Length; edge++)
            {
                (Float3 a, Float3 b) = Edges[edge];
                if (StandsOnBase(shape) && (a.Y < 0f || b.Y < 0f)) continue;

                Float3 world = frame.Position + frame.Rotation * ((a + b) * half);
                if (Handle(ctx, EdgeControl, edge, world, 6f, HandleCap.Square))
                    BeginDrag(handles, shape, EdgeControl, edge, 0f, world);
            }
        }

        if (shape.Kind is CSGGeneratorKind.Cylinder or CSGGeneratorKind.SpiralStairs) RadiusRing(ctx, shape);
        if (shape.Kind == CSGGeneratorKind.Sphere) QuarterHandles(ctx, shape);
        if (shape.Kind == CSGGeneratorKind.SpiralStairs) InnerRing(ctx, shape);
        if (shape.Kind == CSGGeneratorKind.LinearStairs) TurnHandle(ctx, shape);
        if (shape.Kind == CSGGeneratorKind.Room) WallSlider(ctx, shape);

        ContinueDrag(ctx, shape);
    }

    /// <summary>Draws a grab point and reports a press on it.</summary>
    private static bool Handle(SceneToolContext ctx, string control, int index, Float3 world, float size, HandleCap cap)
    {
        HandleContext handles = ctx.Handles;
        ControlID id = handles.GetControlID(control, index);
        handles.AddControl(id, world, size);
        handles.RequestCursor(id, PaperCursor.Pointer);
        ctx.Draw.Dot(world, handles.IsHot(id) || handles.IsNearest(id) ? HotColor : HandleColor, size, cap);
        return handles.IsNearest(id) && handles.TryBeginDrag(id);
    }

    private void BeginDrag(HandleContext handles, CSGShape shape, string control, int index, float along, Float3 point)
    {
        _dragControl = control;
        _dragging = index;
        _startSize = shape.Size;
        _startCentre = shape.Transform.Position;
        _startAlong = along;
        _startPoint = point;
    }

    /// <summary>The direction across the view in the shape's own horizontal plane, where radius handles sit.</summary>
    private static Float3 AcrossView(SceneToolContext ctx, CSGShape shape)
    {
        Float3 right = Quaternion.Inverse(shape.Transform.Rotation) * ctx.Camera.GameObject.Transform.Right;
        right = new Float3(right.X, 0f, right.Z);
        return Float3.LengthSquared(right) < 1e-6f ? Float3.UnitX : Float3.Normalize(right);
    }

    /// <summary>How far an ellipse of these half extents reaches along a direction in its plane.</summary>
    private static float RadiusAlong(Float3 direction, float halfX, float halfZ)
    {
        float x = direction.X / MathF.Max(1e-5f, halfX), z = direction.Z / MathF.Max(1e-5f, halfZ);
        return 1f / MathF.Max(1e-5f, MathF.Sqrt(x * x + z * z));
    }

    private static void DrawEllipse(Transform frame, float y, float halfX, float halfZ, Color32 colour)
    {
        Span<Float3> ring = stackalloc Float3[48];
        for (int i = 0; i < ring.Length; i++)
        {
            float a = i / (float)ring.Length * MathF.Tau;
            ring[i] = frame.Position + frame.Rotation * new Float3(MathF.Cos(a) * halfX, y, MathF.Sin(a) * halfZ);
        }
        CSGGizmos.Polyline(ring, colour, 1.5f, closed: true);
    }

    /// <summary>A ring round the middle whose handle, across the view, scales the width and depth together.</summary>
    private void RadiusRing(SceneToolContext ctx, CSGShape shape)
    {
        Transform frame = shape.Transform;
        Float3 half = shape.Size * 0.5f;
        Float3 direction = _dragControl == RingControl && _dragging >= 0 ? _startDirection : AcrossView(ctx, shape);
        float radius = RadiusAlong(direction, half.X, half.Z);

        DrawEllipse(frame, 0f, half.X, half.Z, HandleColor);
        Float3 world = frame.Position + frame.Rotation * (direction * radius);
        if (!Handle(ctx, RingControl, 0, world, 8f, HandleCap.Diamond)) return;

        BeginDrag(ctx.Handles, shape, RingControl, 0, AlongAxis(ctx.Handles.MouseRay, world, frame.Rotation * direction), world);
        _startDirection = direction;
    }

    /// <summary>A handle on each side of the equator, each changing that one diameter about the centre.</summary>
    private void QuarterHandles(SceneToolContext ctx, CSGShape shape)
    {
        Transform frame = shape.Transform;
        Float3 half = shape.Size * 0.5f;
        DrawEllipse(frame, 0f, half.X, half.Z, HandleColor);

        for (int i = 0; i < Quarters.Length; i++)
        {
            Float3 world = frame.Position + frame.Rotation * (Quarters[i] * half);
            if (Handle(ctx, QuarterControl, i, world, 8f, HandleCap.Diamond))
                BeginDrag(ctx.Handles, shape, QuarterControl, i, AlongAxis(ctx.Handles.MouseRay, world, frame.Rotation * Quarters[i]), world);
        }
    }

    /// <summary>A spiral staircase's open middle as a ring at its base, dragged out or in.</summary>
    private void InnerRing(SceneToolContext ctx, CSGShape shape)
    {
        Transform frame = shape.Transform;
        Float3 half = shape.Size * 0.5f;
        float inner = shape.Settings.InnerRadius;
        Float3 direction = _dragControl == InnerControl && _dragging >= 0 ? _startDirection : AcrossView(ctx, shape);

        if (inner > 1e-3f) DrawEllipse(frame, -half.Y, inner, inner, HandleColor);
        Float3 world = frame.Position + frame.Rotation * (direction * inner + new Float3(0f, -half.Y, 0f));
        if (!Handle(ctx, InnerControl, 0, world, 7f, HandleCap.Circle)) return;

        BeginDrag(ctx.Handles, shape, InnerControl, 0, AlongAxis(ctx.Handles.MouseRay, world, frame.Rotation * direction), world);
        _startDirection = direction;
        _startValue = inner;
    }

    /// <summary>A button past the top corner that turns the stairs a quarter, so they climb toward the next side.</summary>
    private void TurnHandle(SceneToolContext ctx, CSGShape shape)
    {
        Transform frame = shape.Transform;
        Float3 half = shape.Size * 0.5f;
        float reach = MathF.Max(0.25f, CSGSnap.Grid * 0.5f);
        Float3 world = frame.Position + frame.Rotation * new Float3(half.X + reach, half.Y, half.Z + reach);

        if (!Handle(ctx, TurnControl, 0, world, 9f, HandleCap.Diamond)) return;
        ctx.Handles.TryEndDrag(ctx.Handles.GetControlID(TurnControl, 0));

        Undo.RecordObject(shape, "Turn CSG Stairs");
        Undo.RecordObject(shape.GameObject, "Turn CSG Stairs");
        shape.Transform.Rotation = frame.Rotation * Quaternion.AxisAngle(Float3.UnitY, MathF.PI * 0.5f);
        shape.Size = new Float3(shape.Size.Z, shape.Size.Y, shape.Size.X);
        CSGSelection.Regenerate(shape, "Turn CSG Stairs");
    }

    /// <summary>A slider on the inside of a room's side wall that sets how thick its walls are.</summary>
    private void WallSlider(SceneToolContext ctx, CSGShape shape)
    {
        Transform frame = shape.Transform;
        float wall = shape.Settings.WallThickness;
        Float3 world = frame.Position + frame.Rotation * new Float3(shape.Size.X * 0.5f - wall, 0f, 0f);
        if (!Handle(ctx, WallControl, 0, world, 7f, HandleCap.Square)) return;

        BeginDrag(ctx.Handles, shape, WallControl, 0, AlongAxis(ctx.Handles.MouseRay, world, frame.Rotation * Float3.UnitX), world);
        _startValue = wall;
    }

    private void ContinueDrag(SceneToolContext ctx, CSGShape shape)
    {
        if (_dragging < 0) return;

        HandleContext handles = ctx.Handles;
        ControlID active = handles.GetControlID(_dragControl, _dragging);
        if (!handles.IsHot(active)) { _dragging = -1; return; }

        Quaternion rotation = shape.Transform.Rotation;
        switch (_dragControl)
        {
            case FaceControl:
            {
                Float3 axis = FaceAxes[_dragging];
                float startLength = Float3.Dot(Maths.Abs(axis), _startSize);
                float length = startLength + AlongAxis(handles.MouseRay, _startPoint, rotation * axis) - _startAlong;
                length = SnapLength(handles, shape, axis, length);
                SetBounds(shape, WithLength(_startSize, axis, length), _startCentre + rotation * (axis * ((length - startLength) * 0.5f)));
                ctx.Draw.Label(_startPoint, Format(length), HotColor);
                break;
            }

            case EdgeControl:
            {
                (Float3 a, Float3 b) = Edges[_dragging];
                Float3 worldA = rotation * a, worldB = rotation * b;
                if (!OnPlane(handles.MouseRay, _startPoint, Float3.Cross(worldA, worldB), out Float3 hit)) break;

                float startA = Float3.Dot(Maths.Abs(a), _startSize), startB = Float3.Dot(Maths.Abs(b), _startSize);
                float lengthA = SnapLength(handles, shape, a, startA + Float3.Dot(hit - _startPoint, worldA));
                float lengthB = SnapLength(handles, shape, b, startB + Float3.Dot(hit - _startPoint, worldB));
                Float3 size = WithLength(WithLength(_startSize, a, lengthA), b, lengthB);
                SetBounds(shape, size, _startCentre + worldA * ((lengthA - startA) * 0.5f) + worldB * ((lengthB - startB) * 0.5f));
                ctx.Draw.Label(_startPoint, $"{Format(lengthA)} x {Format(lengthB)}", HotColor);
                break;
            }

            case RingControl:
            {
                float startRadius = RadiusAlong(_startDirection, _startSize.X * 0.5f, _startSize.Z * 0.5f);
                float radius = startRadius + AlongAxis(handles.MouseRay, _startPoint, rotation * _startDirection) - _startAlong;
                float diameter = SnapLength(handles, shape, Float3.UnitX, radius * 2f);
                float factor = diameter / MathF.Max(1e-4f, startRadius * 2f);
                SetBounds(shape, new Float3(_startSize.X * factor, _startSize.Y, _startSize.Z * factor), _startCentre);
                ctx.Draw.Label(_startPoint, Format(diameter), HotColor);
                break;
            }

            case QuarterControl:
            {
                Float3 axis = Quarters[_dragging];
                float startLength = Float3.Dot(Maths.Abs(axis), _startSize);
                float length = SnapLength(handles, shape, axis, startLength + 2f * (AlongAxis(handles.MouseRay, _startPoint, rotation * axis) - _startAlong));
                SetBounds(shape, WithLength(_startSize, axis, length), _startCentre);
                ctx.Draw.Label(_startPoint, Format(length), HotColor);
                break;
            }

            case InnerControl:
            {
                float limit = MathF.Max(0f, MathF.Min(shape.Size.X, shape.Size.Z) * 0.5f - 0.05f);
                float inner = _startValue + AlongAxis(handles.MouseRay, _startPoint, rotation * _startDirection) - _startAlong;
                inner = Math.Clamp(CSGSnap.Value(handles, inner), 0f, limit);
                SetSettings(shape, (ref CSGGeneratorSettings s) => s.InnerRadius = inner);
                ctx.Draw.Label(_startPoint, Format(inner), HotColor);
                break;
            }

            case WallControl:
            {
                float limit = MathF.Max(0.01f, MathF.Min(shape.Size.X, MathF.Min(shape.Size.Y, shape.Size.Z)) * 0.5f - 0.01f);
                float wall = _startValue - (AlongAxis(handles.MouseRay, _startPoint, rotation * Float3.UnitX) - _startAlong);
                float step = CSGSnap.Grid * 0.125f;
                if (CSGSnap.Active(handles)) wall = MathF.Round(wall / step) * step;
                wall = Math.Clamp(wall, 0.01f, limit);
                SetSettings(shape, (ref CSGGeneratorSettings s) => s.WallThickness = wall);
                ctx.Draw.Label(_startPoint, Format(wall), HotColor);
                break;
            }
        }

        handles.TryEndDrag(active);
    }

    private static Float3 WithLength(Float3 size, Float3 axis, float length)
    {
        Float3 along = Maths.Abs(axis);
        return size * (Float3.One - along) + along * length;
    }

    private static bool OnPlane(Ray ray, Float3 point, Float3 normal, out Float3 hit)
    {
        hit = point;
        float denom = Float3.Dot(normal, ray.Direction);
        if (MathF.Abs(denom) < 1e-6f) return false;
        hit = ray.Origin + ray.Direction * (Float3.Dot(point - ray.Origin, normal) / denom);
        return true;
    }

    /// <summary>Lengths snap to the grid, and a staircase's height to whole steps.</summary>
    private static float SnapLength(HandleContext handles, CSGShape shape, Float3 axis, float length)
    {
        float step = StandsOnBase(shape) && MathF.Abs(axis.Y) > 0.5f ? MathF.Max(1e-3f, shape.Settings.StepHeight) : CSGSnap.Grid;
        if (CSGSnap.Active(handles)) length = MathF.Round(length / step) * step;
        return MathF.Max(MathF.Max(0.01f, CSGSnap.Active(handles) ? step : 0.01f), length);
    }

    /// <summary>Sets one side's length, the side opposite the moved one staying where it was.</summary>
    private static void Resize(CSGShape shape, Float3 axis, float startLength, float length, Float3 startSize, Float3 startCentre)
        => SetBounds(shape, WithLength(startSize, axis, length), startCentre + shape.Transform.Rotation * (axis * ((length - startLength) * 0.5f)));

    private static void SetBounds(CSGShape shape, Float3 size, Float3 centre)
    {
        if (size.Equals(shape.Size) && centre.Equals(shape.Transform.Position)) return;

        Undo.RecordObject(shape, "Resize CSG Shape");
        Undo.RecordObject(shape.GameObject, "Resize CSG Shape");
        shape.Size = size;
        shape.Transform.Position = centre;
        CSGSelection.Regenerate(shape, "Resize CSG Shape");
    }

    private delegate void SettingsEdit(ref CSGGeneratorSettings settings);

    private static void SetSettings(CSGShape shape, SettingsEdit edit)
    {
        CSGGeneratorSettings settings = shape.Settings;
        edit(ref settings);
        if (settings.Equals(shape.Settings)) return;

        Undo.RecordObject(shape, "Edit CSG Shape");
        shape.Settings = settings;
        CSGSelection.Regenerate(shape, "Edit CSG Shape");
    }

    // ================================================================
    //  Measurements
    // ================================================================

    /// <summary>Width, height and depth along the bounds. Clicking one types a new length, Enter applies and Escape cancels.</summary>
    private void Measurements(SceneToolContext ctx, CSGShape shape)
    {
        HandleContext handles = ctx.Handles;
        Transform frame = shape.Transform;
        Float3 half = shape.Size * 0.5f;

        // Just outside the edges they measure, clear of the edge sliders
        float o = MathF.Max(0.2f, CSGSnap.Grid * 0.35f);
        (Float3 Axis, Float3 At)[] labels =
        [
            (Float3.UnitX, new Float3(0f, -half.Y - o, -half.Z - o)),
            (Float3.UnitY, new Float3(half.X + o, 0f, -half.Z - o)),
            (Float3.UnitZ, new Float3(half.X + o, -half.Y - o, 0f)),
        ];

        for (int i = 0; i < labels.Length; i++)
        {
            Float3 world = frame.Position + frame.Rotation * labels[i].At;
            float length = Float3.Dot(labels[i].Axis, shape.Size);
            bool typing = _typingAxis == i;

            string text = typing ? $"[{(_typed.Length > 0 ? _typed : "_")}]" : Format(length);
            ControlID id = handles.GetControlID(LabelControl, i);
            handles.AddControl(id, world, 14f);
            handles.RequestCursor(id, PaperCursor.Text);
            ctx.Draw.Label(world, text, typing || handles.IsNearest(id) ? HotColor : LabelColor);

            if (handles.IsNearest(id) && handles.TryBeginDrag(id))
            {
                handles.TryEndDrag(id);
                _typingAxis = i;
                _typed = "";
            }
        }

        if (_typingAxis >= 0) Type(handles, shape, labels[_typingAxis].Axis);
    }

    private void Type(HandleContext handles, CSGShape shape, Float3 axis)
    {
        if (handles.KeyDown(KeyCode.Escape)) { _typingAxis = -1; return; }

        foreach (char c in Input.InputString)
            if (char.IsDigit(c) || c == '.' || c == '-') _typed += c;
        if (handles.KeyDown(KeyCode.Backspace) && _typed.Length > 0) _typed = _typed[..^1];

        if (!handles.KeyDown(KeyCode.Enter)) return;

        if (float.TryParse(_typed, NumberStyles.Float, CultureInfo.InvariantCulture, out float length) && length > 0f)
            Resize(shape, axis, Float3.Dot(axis, shape.Size), length, shape.Size, shape.Transform.Position);
        _typingAxis = -1;
    }

    private static string Format(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    // ================================================================
    //  Polygon
    // ================================================================

    /// <summary>A point on each corner, dragged on the outline's plane. Shift shows points on the edges that insert a corner.</summary>
    private void PolygonHandles(SceneToolContext ctx, CSGShape shape)
    {
        HandleContext handles = ctx.Handles;
        Transform frame = shape.Transform;
        Float2[] outline = shape.Outline;
        Float3 up = frame.Rotation * Float3.UnitY;

        Float3 World(Float2 p, float y = 0f) => frame.Position + frame.Rotation * new Float3(p.X, y, p.Y);

        for (int i = 0; i < outline.Length; i++)
        {
            Float3 world = World(outline[i]);
            ControlID id = handles.GetControlID(CornerControl, i);
            handles.AddControl(id, world, 8f);
            handles.RequestCursor(id, PaperCursor.Pointer);
            ctx.Draw.Dot(world, handles.IsHot(id) || handles.IsNearest(id) ? HotColor : HandleColor, 8f);

            if (!handles.IsNearest(id) || !handles.TryBeginDrag(id)) continue;
            _dragging = i;
            _dragControl = CornerControl;
            _startOutline = (Float2[])outline.Clone();
        }

        if (handles.Shift && _dragging < 0)
        {
            for (int i = 0; i < outline.Length; i++)
            {
                Float2 middle = (outline[i] + outline[(i + 1) % outline.Length]) * 0.5f;
                Float3 world = World(middle);
                ControlID id = handles.GetControlID(InsertControl, i);
                handles.AddControl(id, world, 7f);
                ctx.Draw.Dot(world, handles.IsNearest(id) ? HotColor : HandleColor, 6f, HandleCap.Diamond);

                if (!handles.IsNearest(id) || !handles.TryBeginDrag(id)) continue;
                handles.TryEndDrag(id);
                Undo.RecordObject(shape, "Insert CSG Shape Corner");
                shape.Outline = [.. outline[..(i + 1)], middle, .. outline[(i + 1)..]];
                CSGSelection.Regenerate(shape, "Insert CSG Shape Corner");
                return;
            }
        }

        // The height arrow, on the middle of the outline
        Float2 centre = Float2.Zero;
        foreach (Float2 p in outline) centre += p;
        centre /= Math.Max(1, outline.Length);
        Float3 top = World(centre, shape.Height);
        ControlID height = handles.GetControlID(HeightControl);
        handles.AddControl(height, top, 9f);
        handles.RequestCursor(height, PaperCursor.Pointer);
        bool heightHot = handles.IsHot(height) || handles.IsNearest(height);
        CSGGizmos.Line(World(centre), top, heightHot ? HotColor : HandleColor, 1.5f);
        ctx.Draw.Dot(top, heightHot ? HotColor : HandleColor, 9f, HandleCap.Diamond);
        if (handles.IsNearest(height) && handles.TryBeginDrag(height))
        {
            _dragging = 0;
            _dragControl = HeightControl;
            _startHeight = shape.Height;
            _startAlong = AlongAxis(handles.MouseRay, top, up);
        }

        if (_dragging < 0) return;

        if (_dragControl == CornerControl) DragCorner(ctx, shape, up);
        else if (_dragControl == HeightControl) DragHeight(ctx, shape, up, World(centre, _startHeight));
    }

    private void DragCorner(SceneToolContext ctx, CSGShape shape, Float3 up)
    {
        HandleContext handles = ctx.Handles;
        ControlID active = handles.GetControlID(CornerControl, _dragging);
        if (!handles.IsHot(active) || _dragging >= _startOutline.Length) { _dragging = -1; return; }

        Transform frame = shape.Transform;
        Ray ray = handles.MouseRay;
        float denom = Float3.Dot(up, ray.Direction);
        if (MathF.Abs(denom) > 1e-6f)
        {
            float t = Float3.Dot(frame.Position - ray.Origin, up) / denom;
            Float3 local = Quaternion.Inverse(frame.Rotation) * (ray.Origin + ray.Direction * t - frame.Position);
            var point = new Float2(CSGSnap.Value(handles, local.X), CSGSnap.Value(handles, local.Z));

            if (!point.Equals(shape.Outline[_dragging]))
            {
                Undo.RecordObject(shape, "Move CSG Shape Corner");
                Float2[] outline = (Float2[])_startOutline.Clone();
                outline[_dragging] = point;
                shape.Outline = outline;
                CSGSelection.Regenerate(shape, "Move CSG Shape Corner");
            }
        }

        handles.TryEndDrag(active);
    }

    private void DragHeight(SceneToolContext ctx, CSGShape shape, Float3 up, Float3 startTop)
    {
        HandleContext handles = ctx.Handles;
        ControlID active = handles.GetControlID(HeightControl);
        if (!handles.IsHot(active)) { _dragging = -1; return; }

        float height = CSGSnap.Value(handles, _startHeight + AlongAxis(handles.MouseRay, startTop, up) - _startAlong);
        if (MathF.Abs(height) < 0.01f) height = MathF.Sign(_startHeight) * 0.01f;
        if (height != shape.Height)
        {
            Undo.RecordObject(shape, "Raise CSG Shape");
            shape.Height = height;
            CSGSelection.Regenerate(shape, "Raise CSG Shape");
        }
        ctx.Draw.Label(startTop + up * (height - _startHeight), Format(MathF.Abs(height)), HotColor);

        handles.TryEndDrag(active);
    }

    /// <summary>How far along a line through <paramref name="origin"/> the ray passes closest.</summary>
    private static float AlongAxis(Ray ray, Float3 origin, Float3 direction)
    {
        Float3 w = ray.Origin - origin;
        float b = Float3.Dot(direction, ray.Direction), d = Float3.Dot(direction, w), e = Float3.Dot(ray.Direction, w);
        float denominator = 1f - b * b;
        return MathF.Abs(denominator) < 1e-6f ? d : (d - b * e) / denominator;
    }
}
