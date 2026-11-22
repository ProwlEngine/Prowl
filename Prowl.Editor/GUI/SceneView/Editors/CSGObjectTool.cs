// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.CSG;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Operation = Prowl.Vector.Geometry.CSGOperation;
using SColor = System.Drawing.Color;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>Selection and whole brush manipulation for CSG models: pick, switch operation, flip, recentre pivots, clone and hide.</summary>
[GlobalSceneTool(Group = "CSG")]
public sealed class CSGObjectTool : SceneTool
{
    public override string Name => "CSG Object";
    public override string Icon => EditorIcons.Cube;
    public override string? Tooltip => "Select and edit CSG brushes";

    private const string BrushControl = "csg_brush";
    private const string MarqueeControl = "csg_brush_marquee";
    private const string ResizeControl = "csg_brush_resize";
    private const string TransformControl = "sceneview_transform";

    private static readonly Color32 AdditiveColor = new(120, 200, 255, 255);
    private static readonly Color32 SubtractiveColor = new(255, 140, 120, 255);
    private static readonly Color32 IntersectColor = new(220, 200, 120, 255);
    private static readonly Color32 SelectedColor = new(255, 220, 90, 255);
    private static readonly Color32 InvalidColor = new(255, 80, 70, 255);

    private readonly List<CSGBrush> _brushes = new();

    public override bool IsAvailable(SceneToolContext ctx) => CSGSelection.ActiveModel().IsValid();

    public override void OnDeactivated() => _brushes.Clear();

    public override void OnSceneInput(SceneToolContext ctx)
    {
        // Brushes are picked in Select and in Edit, unless a cut is being placed or a plain brush's points are edited
        bool edit = CSGEditMode.Is(CSGMode.Edit);
        if (!edit && !CSGEditMode.Is(CSGMode.Select)) return;
        if (SceneToolManager.Get<CSGClipTool>() is { Active: true }) return;
        if (edit && SceneToolManager.Get<CSGMeshTool>() is { Active: true }) return;

        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsNotValid()) return;

        CollectBrushes(model!);
        if (edit && ResizeHandles(ctx)) { DrawBrushes(ctx); return; }
        HandleCloneDrag(ctx);
        PickBrush(ctx, model!);
        DrawBrushes(ctx);
    }

    private void CollectBrushes(CSGModel model)
    {
        _brushes.Clear();
        foreach (GameObject go in model.GameObject.GetChildrenDeep())
        {
            if (!go.EnabledInHierarchy) continue;

            CSGBrush brush = go.GetComponent<CSGBrush>();
            if (brush.IsValid() && brush.EnabledInHierarchy) _brushes.Add(brush);
        }
    }

    /// <summary>Picks against the model's generated surfaces so the inside of a carved room can be clicked.</summary>
    private void PickBrush(SceneToolContext ctx, CSGModel model)
    {
        HandleContext handles = ctx.Handles;

        if (TickMarquee(ctx)) return;

        if (!CSGSelection.PickBrush(model, _brushes, handles.MouseRay, out CSGBrush hit, out float distance))
        {
            // Empty space starts a marquee over brushes, ahead of scene picking which would select the whole model
            ControlID empty = handles.GetControlID(MarqueeControl);
            handles.AddControl(empty, HandleContext.BodyDistance, float.MaxValue - 1f);
            if (handles.IsNearest(empty) && handles.TryBeginDrag(empty))
            {
                _marqueeStart = handles.MousePosition;
                _marquee = true;
            }
            return;
        }

        ControlID id = handles.GetControlID(BrushControl);
        handles.AddControl(id, HandleContext.BodyDistance, distance);

        if (!handles.IsNearest(id) || !handles.TryBeginDrag(id)) return;

        GameObject target = SelectionTargetFor(Cycle(model, handles, hit));
        if (handles.Shift) Selection.AddToSelection(target);
        else if (handles.Ctrl)
        {
            if (Selection.IsSelected(target)) Selection.RemoveFromSelection(target);
            else Selection.AddToSelection(target);
        }
        else Selection.Select(target);
    }

    // A face being dragged by its resize handle, and the brush as it was when the drag began
    private int _resizeFace = -1;
    private CSGBrush? _resizeBrush;
    private float _resizeStart;
    private Plane[] _resizePlanes = [];
    private SurfaceData[] _resizeSurfaces = [];
    private CSGControlMesh? _resizeMesh;

    private static readonly Float3[] FaceAxes = [Float3.UnitX, -Float3.UnitX, Float3.UnitY, -Float3.UnitY, Float3.UnitZ, -Float3.UnitZ];

    /// <summary>
    /// A handle on each face of one selected brush's bounds. Dragging one moves that side while the opposite side stays,
    /// reshaping the brush rather than scaling its transform. True while a handle is dragged.
    /// </summary>
    private bool ResizeHandles(SceneToolContext ctx)
    {
        List<CSGBrush> selected = CSGSelection.Brushes();
        if (selected.Count != 1 || CSGSelection.Groups().Count > 0) { _resizeFace = -1; return false; }

        CSGBrush brush = selected[0];
        if (!brush.IsShapeValid) return false;

        HandleContext handles = ctx.Handles;
        AABB bounds = CSGBrushGeometry.Bounds(brush.Shape);
        Float3 centre = (bounds.Min + bounds.Max) * 0.5f, extent = (bounds.Max - bounds.Min) * 0.5f;
        Float4x4 toWorld = brush.Transform.LocalToWorldMatrix;

        for (int face = 0; face < 6; face++)
        {
            Float3 axis = FaceAxes[face];
            Float3 world = Float4x4.TransformPoint(centre + axis * Float3.Dot(Maths.Abs(axis), extent), toWorld);
            ControlID id = handles.GetControlID(ResizeControl, face);
            handles.AddControl(id, world, 8f);
            handles.RequestCursor(id, PaperCursor.Pointer);
            bool hot = handles.IsHot(id) && ReferenceEquals(_resizeBrush, brush) && _resizeFace == face;
            ctx.Draw.Dot(world, hot || handles.IsNearest(id) ? SelectedColor : ColorFor(brush.Operation), 7f);

            if (handles.IsNearest(id) && handles.TryBeginDrag(id))
            {
                _resizeFace = face;
                _resizeBrush = brush;
                _resizeStart = AlongAxis(handles.MouseRay, world, Float3.Normalize(Float4x4.TransformNormal(axis, toWorld)));
                _resizePlanes = (Plane[])brush.Planes.Clone();
                _resizeSurfaces = (SurfaceData[])brush.Surfaces.Clone();
                _resizeMesh = brush.HasControlMesh ? brush.ControlMesh!.Clone() : null;
            }
        }

        if (_resizeFace < 0 || !ReferenceEquals(_resizeBrush, brush)) return false;
        ControlID active = handles.GetControlID(ResizeControl, _resizeFace);
        if (!handles.IsHot(active))
        {
            _resizeFace = -1;
            return false;
        }

        DragFace(ctx, brush, bounds);
        handles.TryEndDrag(active);
        return true;
    }

    private void DragFace(SceneToolContext ctx, CSGBrush brush, AABB bounds)
    {
        Float3 axis = FaceAxes[_resizeFace];
        Float4x4 toWorld = brush.Transform.LocalToWorldMatrix;
        Float3 worldAxis = Float4x4.TransformNormal(axis, toWorld);
        float scale = Float3.Length(worldAxis);
        if (scale < 1e-6f) return;

        // The bounds the drag began with, from the brush as it was then
        AABB start = CSGBrushGeometry.Bounds(GeometryGenerator.ConvexFromPlanes(_resizePlanes).Solid);
        Float3 startExtent = start.Max - start.Min;
        float length = Float3.Dot(Maths.Abs(axis), startExtent);
        Float3 startFace = (start.Min + start.Max) * 0.5f + axis * (length * 0.5f);
        Float3 opposite = startFace - axis * length;

        float moved = AlongAxis(ctx.Handles.MouseRay, Float4x4.TransformPoint(startFace, toWorld), worldAxis / scale) - _resizeStart;
        moved = CSGSnap.Value(ctx.Handles, moved);
        float wanted = MathF.Max(CSGSnap.Grid * 0.25f, length + moved / scale);
        float factor = wanted / length;

        // Scaled along the dragged axis about the opposite face
        Float3 factors = Float3.One + Maths.Abs(axis) * (factor - 1f);
        Float4x4 reshape = Float4x4.CreateTranslation(opposite) * Float4x4.CreateScale(factors) * Float4x4.CreateTranslation(-opposite);

        Undo.RecordObject(brush, "Resize CSG Brush");
        CSGControlMesh? mesh = _resizeMesh?.Clone();
        mesh?.TransformPoints(reshape);
        brush.SetShape(mesh != null ? mesh.ToPlanes() : CSGBrushGeometry.Transform(_resizePlanes, reshape), (SurfaceData[])_resizeSurfaces.Clone(), mesh);

        ctx.Draw.Label(Float4x4.TransformPoint((start.Min + start.Max) * 0.5f + axis * (wanted - length * 0.5f), toWorld),
                       (wanted * scale).ToString("0.###"), SelectedColor);
        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsValid()) model!.SetDirty();
    }

    /// <summary>How far along a line through <paramref name="origin"/> the ray passes closest.</summary>
    private static float AlongAxis(Ray ray, Float3 origin, Float3 direction)
    {
        Float3 w = ray.Origin - origin;
        float b = Float3.Dot(direction, ray.Direction), d = Float3.Dot(direction, w), e = Float3.Dot(ray.Direction, w);
        float denominator = 1f - b * b;
        return MathF.Abs(denominator) < 1e-6f ? d : (d - b * e) / denominator;
    }

    // Brushes under the last click, nearest first, and which of them that click took
    private readonly List<CSGBrush> _underClick = new();
    private int _cycle;
    private Float2 _lastClick = new(float.NaN);

    /// <summary>
    /// Clicking the same spot again takes the next brush behind, so buried and carving brushes can be reached. A click
    /// anywhere else starts over from the nearest.
    /// </summary>
    private CSGBrush Cycle(CSGModel model, HandleContext handles, CSGBrush nearest)
    {
        bool samePlace = Float2.Distance(handles.MousePosition, _lastClick) <= 2f;
        _lastClick = handles.MousePosition;

        if (!samePlace || _underClick.Count == 0 || !ReferenceEquals(_underClick[0], nearest))
        {
            _underClick.Clear();
            _underClick.Add(nearest);
            while (_underClick.Count < 32 && model.Raycast(handles.MouseRay, out CSGBrush behind, out _, out _,
                       b => !CSGVisibility.IsHidden(b) && !_underClick.Contains(b)))
                _underClick.Add(behind);
            _cycle = 0;
            return nearest;
        }

        _cycle = (_cycle + 1) % _underClick.Count;
        return _underClick[_cycle];
    }

    // A marquee in progress: where it started, and whether it has moved far enough to be one
    private bool _marquee;
    private Float2 _marqueeStart;

    /// <summary>Draws the marquee and selects the brushes inside it on release. True while one is in progress.</summary>
    private bool TickMarquee(SceneToolContext ctx)
    {
        if (!_marquee) return false;
        HandleContext handles = ctx.Handles;

        bool dragged = Float2.Distance(handles.MousePosition, _marqueeStart) > 4f;
        if (dragged)
            ctx.Draw.ScreenRect(_marqueeStart, handles.MousePosition, Color32.FromArgb(40, 120, 170, 255), Color32.FromArgb(200, 140, 190, 255));

        if (handles.PrimaryHeld && !handles.PrimaryUp) return true;
        _marquee = false;
        handles.TryEndDrag(handles.GetControlID(MarqueeControl));

        if (!handles.Shift && !handles.Ctrl) Selection.Clear();
        if (!dragged) return true;

        // Only what lies wholly inside counts, and a group handled as one only when all of its brushes do
        Float2 min = Maths.Min(_marqueeStart, handles.MousePosition), max = Maths.Max(_marqueeStart, handles.MousePosition);
        var inside = new Dictionary<GameObject, bool>();
        foreach (CSGBrush brush in _brushes)
        {
            if (CSGVisibility.IsHidden(brush) || !brush.IsShapeValid) continue;
            GameObject target = SelectionTargetFor(brush);
            inside[target] = (!inside.TryGetValue(target, out bool sofar) || sofar) && Contained(handles, brush, min, max);
        }

        foreach ((GameObject target, bool contained) in inside)
        {
            if (!contained) continue;
            if (handles.Ctrl) Selection.RemoveFromSelection(target);
            else Selection.AddToSelection(target);
        }
        return true;
    }

    private static bool Contained(HandleContext handles, CSGBrush brush, Float2 min, Float2 max)
    {
        Float4x4 toWorld = brush.Transform.LocalToWorldMatrix;
        foreach (GeometryData.Vertex vertex in brush.Shape!.Vertices)
        {
            if (handles.WorldToScreen(Float4x4.TransformPoint(vertex.Point, toWorld)) is not Float2 p) return false;
            if (p.X < min.X || p.X > max.X || p.Y < min.Y || p.Y > max.Y) return false;
        }
        return true;
    }

    /// <summary>What a click on this brush selects. The outermost group marked to handle as one stands in for it.</summary>
    private static GameObject SelectionTargetFor(CSGBrush brush)
    {
        GameObject target = brush.GameObject;

        for (GameObject? current = brush.GameObject.Parent; current.IsValid(); current = current!.Parent)
        {
            CSGGroup group = current!.GetComponent<CSGGroup>();
            if (group.IsValid() && group.HandleAsOne) target = current;
        }

        return target;
    }


    /// <summary>Shift pressing the transform gizmo leaves a copy behind and drags the duplicate.</summary>
    private void HandleCloneDrag(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;

        // Where the copies ended up is the step Repeat Clone goes on with
        if (_cloneFrom is Float3 from && handles.PrimaryUp)
        {
            Float3 step = CSGSelection.SelectionCentre() - from;
            if (Float3.LengthSquared(step) > 1e-8f) CSGSelection.RememberClone(step);
            _cloneFrom = null;
        }

        if (!handles.PrimaryDown || !handles.Shift || handles.Blocked) return;
        if (!handles.IsNearest(handles.GetControlID(TransformControl))) return;

        CSGSelection.Duplicate();
        _cloneFrom = CSGSelection.SelectionCentre();
    }

    private Float3? _cloneFrom;

    private void DrawBrushes(SceneToolContext ctx)
    {
        foreach (CSGBrush brush in _brushes)
        {
            if (CSGVisibility.IsHidden(brush)) continue;
            if (!brush.IsShapeValid)
            {
                CSGGizmos.Circle(brush.Transform.Position, ctx.Camera.GameObject.Transform.Forward, 0.25f, InvalidColor, 2f);
                ctx.Draw.Label(brush.Transform.Position, brush.GameObject.Name, InvalidColor);
                continue;
            }

            bool selected = Selection.IsSelected(brush.GameObject);
            Color32 color = selected ? SelectedColor : ColorFor(brush.Operation);

            DrawBrushEdges(ctx, brush, color, selected ? 2f : 1f);
        }
    }

    private static void DrawBrushEdges(SceneToolContext ctx, CSGBrush brush, Color32 color, float thickness)
    {
        var shape = brush.Shape;
        if (shape == null) return;

        Float4x4 toWorld = brush.Transform.LocalToWorldMatrix;
        foreach (var edge in shape.Edges)
        {
            Float3 a = Float4x4.TransformPoint(edge.Vert1.Point, toWorld);
            Float3 b = Float4x4.TransformPoint(edge.Vert2.Point, toWorld);
            CSGGizmos.Line(a, b, color, thickness);
        }
    }

    internal static Color32 ColorFor(Operation operation) => operation switch
    {
        Operation.Subtractive => SubtractiveColor,
        Operation.Intersect => IntersectColor,
        _ => AdditiveColor,
    };
}
