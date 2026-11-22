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
using Prowl.Vector.Geometry;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>
/// Edit on the selected brush or group: a slider on each face and edge of its bounds, and measurements that can be
/// clicked and typed into. Moving a side reshapes the brushes inside, the opposite side staying where it was, rather
/// than scaling their transforms.
/// </summary>
[GlobalSceneTool(Group = "CSG")]
public sealed class CSGBoundsTool : SceneTool
{
    public override string Name => "CSG Bounds";
    public override string Icon => EditorIcons.VectorSquare;
    public override int Order => 3;

    private const string FaceControl = "csg_bounds_face";
    private const string EdgeControl = "csg_bounds_edge";
    private const string LabelControl = "csg_bounds_label";

    private static readonly Color32 HandleColor = new(120, 200, 255, 255);
    private static readonly Color32 HotColor = new(255, 220, 90, 255);
    private static readonly Color32 LabelColor = new(230, 240, 255, 255);

    private static readonly Float3[] FaceAxes = [Float3.UnitX, -Float3.UnitX, Float3.UnitY, -Float3.UnitY, Float3.UnitZ, -Float3.UnitZ];
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

    /// <summary>A brush as the drag found it, and how its space relates to the bounds' frame.</summary>
    private readonly record struct Start(CSGBrush Brush, SurfaceData[] Surfaces, CSGControlMesh Mesh, Float4x4 ToFrame);

    private readonly List<Start> _start = new();
    private string _dragControl = "";
    private int _dragging = -1;
    private AABB _startBounds;
    private Float3 _startPoint;
    private float _startAlong;

    // A measurement being typed: the axis it sizes, and the text so far
    private int _typingAxis = -1;
    private string _typed = "";

    public override bool IsAvailable(SceneToolContext ctx) => CSGSelection.ActiveModel().IsValid();

    public override void OnDeactivated() => _typingAxis = -1;

    /// <summary>The one selected object, when it is a brush or holds brushes.</summary>
    private static GameObject? Target()
    {
        if (!CSGEditMode.Is(CSGMode.Select)) return null;
        List<GameObject> selected = Selection.GetSelected<GameObject>().ToList();
        if (selected.Count != 1 || selected[0].GetComponent<CSGModel>().IsValid()) return null;
        return Brushes(selected[0]).Count > 0 ? selected[0] : null;
    }

    private static List<CSGBrush> Brushes(GameObject root)
    {
        var brushes = new List<CSGBrush>();
        CSGBrush own = root.GetComponent<CSGBrush>();
        if (own.IsValid() && own.IsShapeValid) brushes.Add(own);
        foreach (GameObject child in root.GetChildrenDeep())
        {
            CSGBrush brush = child.GetComponent<CSGBrush>();
            if (brush.IsValid() && brush.EnabledInHierarchy && brush.IsShapeValid) brushes.Add(brush);
        }
        return brushes;
    }

    /// <summary>The bounds of every brush under the target, in the target's own space.</summary>
    private static bool TryBounds(GameObject root, out AABB bounds)
    {
        Float4x4 toRoot = root.Transform.WorldToLocalMatrix;
        Float3 min = new(float.MaxValue), max = new(float.MinValue);
        bool any = false;
        foreach (CSGBrush brush in Brushes(root))
        {
            Float4x4 toFrame = toRoot * brush.Transform.LocalToWorldMatrix;
            foreach (GeometryData.Vertex vertex in brush.Shape!.Vertices)
            {
                Float3 p = Float4x4.TransformPoint(vertex.Point, toFrame);
                min = Maths.Min(min, p);
                max = Maths.Max(max, p);
                any = true;
            }
        }
        bounds = new AABB(min, max);
        return any;
    }

    public override void OnSceneInput(SceneToolContext ctx)
    {
        GameObject? root = Target();
        if (root.IsNotValid() || !TryBounds(root!, out AABB bounds))
        {
            _dragging = -1;
            _typingAxis = -1;
            return;
        }

        HandleContext handles = ctx.Handles;
        Float4x4 toWorld = root!.Transform.LocalToWorldMatrix;
        Float3 centre = (bounds.Min + bounds.Max) * 0.5f, half = (bounds.Max - bounds.Min) * 0.5f;
        Float3 World(Float3 local) => Float4x4.TransformPoint(centre + local, toWorld);

        foreach ((Float3 a, Float3 b) in Edges)
        {
            Float3 along = Float3.One - a * a - b * b;
            CSGGizmos.Line(World((a + b - along) * half), World((a + b + along) * half), HandleColor, 1.5f);
        }

        for (int face = 0; face < 6; face++)
        {
            Float3 world = World(FaceAxes[face] * half);
            if (Handle(ctx, FaceControl, face, world, 8f, HandleCap.Circle))
                BeginDrag(root, bounds, FaceControl, face, world, AlongAxis(handles.MouseRay, world, Direction(toWorld, FaceAxes[face])));
        }

        for (int edge = 0; edge < Edges.Length; edge++)
        {
            (Float3 a, Float3 b) = Edges[edge];
            Float3 world = World((a + b) * half);
            if (Handle(ctx, EdgeControl, edge, world, 6f, HandleCap.Square))
                BeginDrag(root, bounds, EdgeControl, edge, world, 0f);
        }

        if (_dragging >= 0) Drag(ctx, root, toWorld);
        else Measurements(ctx, root, bounds, toWorld);
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

    private void BeginDrag(GameObject root, AABB bounds, string control, int index, Float3 point, float along)
    {
        _dragControl = control;
        _dragging = index;
        _startBounds = bounds;
        _startPoint = point;
        _startAlong = along;

        _start.Clear();
        Float4x4 toRoot = root.Transform.WorldToLocalMatrix;
        foreach (CSGBrush brush in Brushes(root))
        {
            _start.Add(new Start(brush, (SurfaceData[])brush.Surfaces.Clone(), brush.ControlMesh.Clone(), toRoot * brush.Transform.LocalToWorldMatrix));
        }
    }

    private void Drag(SceneToolContext ctx, GameObject root, Float4x4 toWorld)
    {
        HandleContext handles = ctx.Handles;
        ControlID active = handles.GetControlID(_dragControl, _dragging);
        if (!handles.IsHot(active)) { _dragging = -1; return; }

        Float3 size = _startBounds.Max - _startBounds.Min;
        if (_dragControl == FaceControl)
        {
            Float3 axis = FaceAxes[_dragging];
            float scale = AxisScale(toWorld, axis);
            float moved = (AlongAxis(handles.MouseRay, _startPoint, Direction(toWorld, axis)) - _startAlong) / scale;
            float length = Snap(handles, Length(size, axis) + moved, scale);
            Reshape(Resized(axis, length));
            ctx.Draw.Label(_startPoint, Format(length * scale), HotColor);
        }
        else
        {
            (Float3 a, Float3 b) = Edges[_dragging];
            Float3 worldA = Direction(toWorld, a), worldB = Direction(toWorld, b);
            if (OnPlane(handles.MouseRay, _startPoint, Float3.Cross(worldA, worldB), out Float3 hit))
            {
                float scaleA = AxisScale(toWorld, a), scaleB = AxisScale(toWorld, b);
                float lengthA = Snap(handles, Length(size, a) + Float3.Dot(hit - _startPoint, worldA) / scaleA, scaleA);
                float lengthB = Snap(handles, Length(size, b) + Float3.Dot(hit - _startPoint, worldB) / scaleB, scaleB);
                Reshape(Resized(a, lengthA) * Resized(b, lengthB));
                ctx.Draw.Label(_startPoint, $"{Format(lengthA * scaleA)} x {Format(lengthB * scaleB)}", HotColor);
            }
        }

        handles.TryEndDrag(active);
    }

    /// <summary>A stretch along one side of the start bounds that leaves the opposite side where it was.</summary>
    private Float4x4 Resized(Float3 axis, float length)
    {
        Float3 along = Maths.Abs(axis);
        float start = Length(_startBounds.Max - _startBounds.Min, axis);
        float factor = length / MathF.Max(1e-5f, start);

        // The side opposite the moved one is the anchor
        Float3 anchor = Float3.Dot(axis, Float3.One) > 0f ? _startBounds.Min : _startBounds.Max;
        Float3 factors = Float3.One + along * (factor - 1f);
        return Float4x4.CreateTranslation(anchor) * Float4x4.CreateScale(factors) * Float4x4.CreateTranslation(-anchor);
    }

    /// <summary>Applies a reshape, given in the bounds' frame, to every brush from how it was when the drag began.</summary>
    private void Reshape(Float4x4 inFrame)
    {
        foreach (Start start in _start)
        {
            if (start.Brush.IsNotValid()) continue;

            Float4x4 local = start.ToFrame.Invert() * inFrame * start.ToFrame;
            Undo.RecordObject(start.Brush, "Resize CSG Brush");
            CSGControlMesh mesh = start.Mesh.Clone();
            mesh.TransformPoints(local);
            start.Brush.SetShape(mesh, (SurfaceData[])start.Surfaces.Clone());
        }

        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsValid()) model!.SetDirty();
    }

    // ================================================================
    //  Measurements
    // ================================================================

    /// <summary>Width, height and depth along the bounds. Clicking one types a new length, Enter applies and Escape cancels.</summary>
    private void Measurements(SceneToolContext ctx, GameObject root, AABB bounds, Float4x4 toWorld)
    {
        HandleContext handles = ctx.Handles;
        Float3 centre = (bounds.Min + bounds.Max) * 0.5f, half = (bounds.Max - bounds.Min) * 0.5f;
        Float3 size = bounds.Max - bounds.Min;

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
            Float3 world = Float4x4.TransformPoint(centre + labels[i].At, toWorld);
            float length = Length(size, labels[i].Axis) * AxisScale(toWorld, labels[i].Axis);
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

        if (_typingAxis >= 0) Type(handles, root, bounds, labels[_typingAxis].Axis, toWorld);
    }

    private void Type(HandleContext handles, GameObject root, AABB bounds, Float3 axis, Float4x4 toWorld)
    {
        if (handles.KeyDown(KeyCode.Escape)) { _typingAxis = -1; return; }

        foreach (char c in Input.InputString)
            if (char.IsDigit(c) || c == '.' || c == '-') _typed += c;
        if (handles.KeyDown(KeyCode.Backspace) && _typed.Length > 0) _typed = _typed[..^1];

        if (!handles.KeyDown(KeyCode.Enter)) return;
        _typingAxis = -1;
        if (!float.TryParse(_typed, NumberStyles.Float, CultureInfo.InvariantCulture, out float length) || length <= 0f) return;

        BeginDrag(root, bounds, FaceControl, 0, Float3.Zero, 0f);
        Reshape(Resized(axis, length / AxisScale(toWorld, axis)));
        _dragging = -1;
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private static float Length(Float3 size, Float3 axis) => Float3.Dot(Maths.Abs(axis), size);

    private static float AxisScale(Float4x4 toWorld, Float3 axis) => MathF.Max(1e-5f, Float3.Length(Float4x4.TransformNormal(Maths.Abs(axis), toWorld)));

    private static Float3 Direction(Float4x4 toWorld, Float3 axis) => Float3.Normalize(Float4x4.TransformNormal(axis, toWorld));

    /// <summary>A length in the frame, snapped so its world length lands on the grid.</summary>
    private static float Snap(HandleContext handles, float length, float scale)
    {
        float world = length * scale;
        if (CSGSnap.Active(handles)) world = MathF.Max(CSGSnap.Grid, MathF.Round(world / CSGSnap.Grid) * CSGSnap.Grid);
        return MathF.Max(0.01f, world) / scale;
    }

    private static string Format(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static bool OnPlane(Ray ray, Float3 point, Float3 normal, out Float3 hit)
    {
        hit = point;
        float denom = Float3.Dot(normal, ray.Direction);
        if (MathF.Abs(denom) < 1e-6f) return false;
        hit = ray.Origin + ray.Direction * (Float3.Dot(point - ray.Origin, normal) / denom);
        return true;
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
