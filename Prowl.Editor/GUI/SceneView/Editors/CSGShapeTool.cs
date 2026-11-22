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

    private void BoundsHandles(SceneToolContext ctx, CSGShape shape)
    {
        HandleContext handles = ctx.Handles;
        Transform frame = shape.Transform;

        for (int face = 0; face < 6; face++)
        {
            if (face == 3 && StandsOnBase(shape)) continue;

            Float3 axis = FaceAxes[face];
            Float3 world = frame.Position + frame.Rotation * (axis * shape.Size * 0.5f);
            ControlID id = handles.GetControlID(FaceControl, face);
            handles.AddControl(id, world, 8f);
            handles.RequestCursor(id, PaperCursor.Pointer);

            bool hot = handles.IsHot(id) || handles.IsNearest(id);
            ctx.Draw.Dot(world, hot ? HotColor : HandleColor, 8f, HandleCap.Circle);

            if (!handles.IsNearest(id) || !handles.TryBeginDrag(id)) continue;
            _dragging = face;
            _dragControl = FaceControl;
            _startSize = shape.Size;
            _startCentre = frame.Position;
            _startAlong = AlongAxis(handles.MouseRay, world, frame.Rotation * axis);
        }

        if (_dragControl != FaceControl || _dragging < 0) return;
        ControlID active = handles.GetControlID(FaceControl, _dragging);
        if (!handles.IsHot(active)) { _dragging = -1; return; }

        Float3 dragAxis = FaceAxes[_dragging];
        Float3 worldAxis = frame.Rotation * dragAxis;
        Float3 startFace = _startCentre + frame.Rotation * (dragAxis * _startSize * 0.5f);
        float startLength = Float3.Dot(Maths.Abs(dragAxis), _startSize);
        float length = startLength + AlongAxis(handles.MouseRay, startFace, worldAxis) - _startAlong;

        Resize(shape, dragAxis, startLength, SnapLength(handles, shape, dragAxis, length), _startSize, _startCentre);
        ctx.Draw.Label(frame.Position + worldAxis * (Float3.Dot(Maths.Abs(dragAxis), shape.Size) * 0.5f), Format(Float3.Dot(Maths.Abs(dragAxis), shape.Size)), HotColor);

        handles.TryEndDrag(active);
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
    {
        Float3 along = Maths.Abs(axis);
        Float3 size = startSize * (Float3.One - along) + along * length;
        Float3 centre = startCentre + shape.Transform.Rotation * (axis * ((length - startLength) * 0.5f));
        if (size.Equals(shape.Size) && centre.Equals(shape.Transform.Position)) return;

        Undo.RecordObject(shape, "Resize CSG Shape");
        Undo.RecordObject(shape.GameObject, "Resize CSG Shape");
        shape.Size = size;
        shape.Transform.Position = centre;
        CSGSelection.Regenerate(shape, "Resize CSG Shape");
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

        (Float3 Axis, Float3 At)[] labels =
        [
            (Float3.UnitX, new Float3(0f, -half.Y, -half.Z)),
            (Float3.UnitY, new Float3(half.X, 0f, -half.Z)),
            (Float3.UnitZ, new Float3(half.X, -half.Y, 0f)),
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
