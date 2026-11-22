// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Core;
using Prowl.Editor.Theming;
using Prowl.PaperUI;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.CSG;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>Which parts of a brush the mesh tool will select.</summary>
[Flags]
public enum CSGElementFilter
{
    None = 0,
    Vertex = 1 << 0,
    Edge = 1 << 1,
    Face = 1 << 2,
    All = Vertex | Edge | Face,
}

/// <summary>Vertex, edge and face editing of one brush through its control mesh.</summary>
[GlobalSceneTool(Group = "CSG")]
public sealed class CSGMeshTool : SceneTool
{
    public override string Name => "CSG Mesh";
    public override string Icon => EditorIcons.DrawPolygon;
    public override string? Tooltip => "Edit brush vertices, edges and faces";
    public override int Order => 4;

    private const string VertexControl = "csg_mesh_vertex";
    private const string EdgeControl = "csg_mesh_edge";
    private const string FaceControl = "csg_mesh_face";
    private const string MoveHandle = "csg_mesh_move";
    private const string MarqueeControl = "csg_mesh_marquee";
    private const string BrushControl = "csg_mesh_brush";

    private const float PickRadius = 9f;

    private static readonly Color32 IdleColor = new(150, 190, 230, 200);
    private static readonly Color32 PickedColor = new(255, 210, 80, 255);
    private static readonly Color32 FaceFill = new(255, 200, 90, 60);
    private static readonly Color32 EdgeHandleColor = new(150, 190, 230, 140);

    /// <summary>Editing a brush's points: Edit is the tool and the selection is one brush.</summary>
    public bool Active => CSGEditMode.Is(CSGMode.Edit) && CSGSelection.Brushes().Count == 1;

    /// <summary>Which element types a click will pick.</summary>
    public CSGElementFilter Filter { get; set; } = CSGElementFilter.All;

    /// <summary>The brush being edited, or null when the selection is not a single brush.</summary>
    public CSGBrush? Target { get; private set; }

    public int SelectedCount => _vertices.Count;

    public int SelectedFaceCount => _faces.Count;

    /// <summary>How far the chamfer and extrude buttons cut and pull.</summary>
    public float ChamferDistance => CSGSnap.Grid;
    public float ExtrudeDistance => CSGSnap.Grid;

    /// <summary>Cuts every selected edge, or every selected corner when no whole edge is selected, by <see cref="ChamferDistance"/>.</summary>
    public void ChamferSelected()
    {
        if (Target.IsNotValid() || _mesh == null || ChamferDistance <= 1e-4f) return;

        var planes = new List<Plane>(Target!.Planes);
        int original = planes.Count;

        foreach ((int a, int b) in _edges)
        {
            if (!_vertices.Contains(a) || !_vertices.Contains(b)) continue;
            var normals = new List<Float3>();
            foreach (int p in _mesh.PolygonsAtEdge(a, b)) normals.Add(_mesh.PolygonNormal(p));
            if (CSGMeshEdit.ChamferPlane(normals, (_mesh.Points[a] + _mesh.Points[b]) * 0.5f, ChamferDistance, out Plane cut)) planes.Add(cut);
        }

        if (planes.Count == original)
        {
            foreach (int point in _vertices)
            {
                var normals = new List<Float3>();
                foreach (int p in _mesh.PolygonsAtPoint(point)) normals.Add(_mesh.PolygonNormal(p));
                if (CSGMeshEdit.ChamferPlane(normals, _mesh.Points[point], ChamferDistance, out Plane cut)) planes.Add(cut);
            }
        }

        if (planes.Count == original) return;

        CSGControlMesh mesh = Target.ControlMesh.Clone();
        SurfaceData[] surfaces = (SurfaceData[])Target.Surfaces.Clone();
        for (int i = original; i < planes.Count; i++)
        {
            if (CSGSelection.CutAway(mesh, surfaces, planes[i], CSGSelection.NearestSurface(Target, planes[i].Normal, surfaces.Length)) is not { } cut) return;
            (mesh, surfaces) = cut;
        }

        Undo.RecordObject(Target, "Chamfer CSG Brush");
        Target.SetShape(mesh, surfaces);
        ClearSelection();
        MarkDirty();
    }

    /// <summary>Pulls the one selected face out into a brush of its own by <see cref="ExtrudeDistance"/>.</summary>
    public void ExtrudeSelected()
    {
        if (Target.IsNotValid() || _mesh == null || _faces.Count != 1) return;

        PrepareExtrude(_faces[0]);
        CommitExtrude(ExtrudeDistance);
    }

    /// <summary>Takes the face's corners, direction and look, ready for an extrude.</summary>
    private void PrepareExtrude(int face)
    {
        _actionRing.Clear();
        foreach (int i in _mesh!.PolygonPoints(face)) _actionRing.Add(_mesh.Points[i]);
        _actionCorner = _mesh.PolygonNormal(face);
        int plane = PlaneOf(face);
        _actionSurface = plane >= 0 ? Target!.Surfaces[plane] : SurfaceData.Default;
    }

    public void SnapSelectedToGrid() => SnapSelection();

    public void DeleteSelectedFaces() => DeleteFaces();

    /// <summary>One face is selected, so the bracket keys taper it instead of changing the grid.</summary>
    public bool TapersFace => Active && _faces.Count == 1;

    /// <summary>A face of the edited brush has been pulled out of its plane, so the shape only approximates the points.</summary>
    public bool NonPlanar { get; private set; }

    private readonly HashSet<int> _vertices = new();
    private readonly List<int> _faces = new();

    /// <summary>What a drag in progress is doing.</summary>
    private enum Action { None, Marquee, Chamfer, Extrude, Drag }

    // Pixels the cursor has to travel before a press becomes a chamfer, extrude or drag, so a click stays a click
    private const float DragThreshold = 4f;

    private CSGControlMesh? _mesh;
    private object? _meshSource;
    private List<(int a, int b)> _edges = new();
    private bool _dragging;

    // A gizmo drag that began with Shift on a whole face, extruding it rather than moving it
    private bool _gizmoExtrude;
    private Float3 _dragRaw, _dragApplied;
    private Float3 _dragAnchor, _dragAnchorStart, _dragStartHit;
    private bool _dragStarted;
    private bool _passedThreshold;

    // Points pressed while already selected, so the press drags the whole selection and a plain click narrows to them
    private int[]? _clickedPoints;

    private Action _action;
    private Float2 _dragOrigin;

    // What a chamfer or extrude started on, captured when it began so later renumbering cannot move it
    private CSGBrush? _actionBrush;
    private object? _actionView;
    private SurfaceData[]? _startSurfaces;
    private CSGControlMesh? _startMesh;
    private Float3 _actionCorner;
    private readonly List<Float3> _actionNormals = new();
    private readonly List<Float3> _actionRing = new();
    private SurfaceData _actionSurface;

    public override bool IsAvailable(SceneToolContext ctx) => CSGSelection.ActiveModel().IsValid();

    public override bool SuppressTransformGizmo => Active;

    public override void OnDeactivated() => ClearSelection();

    public override void OnSceneInput(SceneToolContext ctx)
    {
        if (!Active)
        {
            CancelAction();
            return;
        }

        CSGBrush? before = Target;
        if (!ResolveTarget() || !ReferenceEquals(before, Target))
        {
            CancelAction();
            if (Target.IsNotValid()) return;
        }

        HandleContext handles = ctx.Handles;

        if (handles.KeyDown(KeyCode.Escape))
        {
            if (_action != Action.None) { CancelAction(); return; }
            if (_vertices.Count == 0) CSGEditMode.Current = CSGMode.Select;
            ClearSelection();
            return;
        }

        // Delete always belongs to the mesh tool here, so it never falls through to deleting the brush
        if (handles.KeyDown(KeyCode.Delete) || handles.KeyDown(KeyCode.Backspace)) DeleteFaces();
        if (handles.IsPressed("CSG/SnapToGrid")) SnapSelection();

        DrawElements(ctx);

        // A gesture in progress owns the frame, the move handle and picking stand down.
        if (_action != Action.None)
        {
            // A gesture belongs to the scene view it started in
            if (ReferenceEquals(ctx.View, _actionView)) TickAction(ctx);
            return;
        }

        ShapeHandles(ctx);
        MoveSelection(ctx);
        PickElements(ctx);
    }

    /// <summary>
    /// Mesh editing works on one brush at a time. Until the first edit the points come from a preview, so
    /// just looking at a brush never rewrites it. Committing derives the same points in the same order.
    /// </summary>
    private bool ResolveTarget()
    {
        List<CSGBrush> selected = CSGSelection.Brushes();
        CSGBrush? brush = selected.Count == 1 ? selected[0] : null;

        if (!ReferenceEquals(brush, Target)) { Target = brush; ClearSelection(); }
        if (Target.IsNotValid()) { Target = null; return false; }

        _mesh = Target!.ControlMesh;

        // New points after a plane edit or an undo, so old indices mean nothing
        if (!ReferenceEquals(_mesh, _meshSource))
        {
            _meshSource = _mesh;
            if (_action == Action.None) ClearSelection();
        }
        _vertices.RemoveWhere(i => i >= _mesh.Points.Count);
        _faces.RemoveAll(f => f >= _mesh.Polygons.Count);

        _edges = _mesh.Edges();
        NonPlanar = !_mesh.IsPlanar();
        return _mesh.Points.Count > 0;
    }

    /// <summary>Records the brush and makes its points its own, before an edit to them.</summary>
    private CSGControlMesh? BeginPointEdit(string name)
    {
        Undo.RecordObject(Target!, name);
        CSGControlMesh mesh = Target!.ControlMesh;

        _mesh = mesh;
        _meshSource = mesh;
        return mesh;
    }

    /// <summary>The surface of a polygon of the points, which is the polygon's own index.</summary>
    private int PlaneOf(int polygon) => polygon >= 0 && polygon < Target!.Surfaces.Length ? polygon : -1;

    /// <summary>Abandons a gesture in progress, putting back what a chamfer changed.</summary>
    private void CancelAction()
    {
        if (_action == Action.None) return;
        // Exactly the shape it started with, so the open undo record finds nothing changed and leaves no step
        if (_action is Action.Chamfer or Action.Drag && _actionBrush.IsValid() && _startMesh != null)
        {
            _actionBrush!.SetShape(_startMesh.Clone(), (SurfaceData[])_startSurfaces!.Clone());
            MarkDirty();
        }
        EndAction();
    }

    private void ClearSelection()
    {
        _vertices.Clear();
        _faces.Clear();
    }

    private void PickElements(SceneToolContext ctx)
    {
        if (_dragging || _action != Action.None) return;

        HandleContext handles = ctx.Handles;
        Float4x4 toWorld = Target!.Transform.LocalToWorldMatrix;

        // Registered largest first so a vertex, registered last, beats the edge and face under it.
        if (Filter.HasFlag(CSGElementFilter.Face) && TryPickFace(ctx, out int face, out ControlID faceId))
        {
            if (handles.IsNearest(faceId) && handles.TryBeginDrag(faceId))
            {
                SelectFace(handles, face);

                // Shift pulls the face out as its own brush.
                if (handles.Shift) BeginAction(handles, Action.Extrude, face: face);
                return;
            }
        }

        if (Filter.HasFlag(CSGElementFilter.Edge))
        {
            for (int i = 0; i < _edges.Count; i++)
            {
                Float3 mid = Float4x4.TransformPoint(
                    (_mesh!.Points[_edges[i].a] + _mesh.Points[_edges[i].b]) * 0.5f, toWorld);

                ControlID id = handles.GetControlID(EdgeControl, i);
                handles.AddControl(id, mid, PickRadius);

                if (handles.IsNearest(id) && handles.TryBeginDrag(id))
                {
                    (int a, int b) = _edges[i];
                    bool keep = !handles.Shift && !handles.Ctrl && _vertices.Contains(a) && _vertices.Contains(b);
                    if (!keep) SelectEdge(handles, i);
                    BeginAction(handles, handles.Shift ? Action.Chamfer : Action.Drag, edge: i);
                    if (keep) _clickedPoints = [a, b];
                    return;
                }
            }
        }

        if (Filter.HasFlag(CSGElementFilter.Vertex))
        {
            for (int i = 0; i < _mesh!.Points.Count; i++)
            {
                ControlID id = handles.GetControlID(VertexControl, i);
                handles.AddControl(id, Float4x4.TransformPoint(_mesh.Points[i], toWorld), PickRadius);

                if (handles.IsNearest(id) && handles.TryBeginDrag(id))
                {
                    bool keep = !handles.Shift && !handles.Ctrl && _vertices.Contains(i);
                    if (!keep) SelectVertex(handles, i);
                    BeginAction(handles, handles.Shift ? Action.Chamfer : Action.Drag, point: i);
                    if (keep) _clickedPoints = [i];
                    return;
                }
            }
        }

        // Another brush under the cursor becomes the one being edited
        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsValid() && CSGSelection.PickBrush(model!, CSGSelection.BrushesIn(model!), handles.MouseRay, out CSGBrush other, out float distance)
            && !ReferenceEquals(other, Target))
        {
            ControlID brushId = handles.GetControlID(BrushControl);
            handles.AddControl(brushId, HandleContext.BodyDistance, distance);
            if (handles.IsNearest(brushId) && handles.TryBeginDrag(brushId))
            {
                Selection.Select(other.GameObject);
                return;
            }
        }

        // Nothing under the cursor, so a press starts a marquee.
        ControlID marquee = handles.GetControlID(MarqueeControl);
        // Behind everything else, but ahead of scene picking, so a miss never deselects the brush
        handles.AddControl(marquee, HandleContext.BodyDistance, float.MaxValue - 1f);

        if (handles.IsNearest(marquee) && handles.TryBeginDrag(marquee))
            BeginAction(handles, Action.Marquee);
    }

    /// <summary>Moves the selected points with the cursor, across the plane through them that faces the camera.</summary>
    private void DragPoints(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;
        Float3 normal = ctx.Camera.GameObject.Transform.Forward;
        Ray ray = handles.MouseRay;

        float denom = Float3.Dot(ray.Direction, normal);
        if (MathF.Abs(denom) < 1e-6f) return;
        Float3 hit = ray.Origin + ray.Direction * (Float3.Dot(_dragAnchor - ray.Origin, normal) / denom);

        if (!_dragStarted)
        {
            _dragStarted = true;
            _dragStartHit = hit;
            _dragAnchorStart = _dragAnchor;
        }

        // The pressed point itself lands on the grid, so an off grid vertex is pulled onto it
        Float3 target = CSGSnap.Value(handles, _dragAnchorStart + (hit - _dragStartHit));
        Float3 step = target - _dragAnchor;
        if (Float3.LengthSquared(step) < 1e-12f) return;

        CSGControlMesh? mesh = BeginPointEdit("Move CSG Vertices");
        if (mesh == null) return;

        Float4x4 toLocal = Target!.Transform.WorldToLocalMatrix;
        Float3 delta = Float4x4.TransformPoint(_dragAnchor + step, toLocal) - Float4x4.TransformPoint(_dragAnchor, toLocal);

        List<int> moving = [.. _vertices];
        mesh.MovePoints(moving, delta);
        Target.ApplyControlMesh();
        if (!Target.IsShapeValid)
        {
            CSGStatus.Flash(Loc.Get("csg.status_inside_out"));
            mesh.MovePoints(moving, -delta);
            Target.ApplyControlMesh();
            return;
        }

        _dragAnchor = target;
        MarkDirty();
    }

    /// <summary>How far a drag has moved, beside what is being moved.</summary>
    private static void DrawDistance(SceneToolContext ctx, Float3 at, Float3 moved)
    {
        if (Float3.LengthSquared(moved) < 1e-10f) return;
        ctx.Draw.Label(at, $"{Float3.Length(moved):0.###}  ({moved.X:0.###}, {moved.Y:0.###}, {moved.Z:0.###})", PickedColor);
    }

    private void BeginAction(HandleContext handles, Action action, int point = -1, int edge = -1, int face = -1)
    {
        _action = action;
        _actionView = Context.View;
        _dragOrigin = handles.MousePosition;
        _dragStarted = false;
        _passedThreshold = false;
        _clickedPoints = null;
        _actionBrush = Target;
        _startSurfaces = (SurfaceData[])Target!.Surfaces.Clone();
        _startMesh = Target.ControlMesh.Clone();
        _actionNormals.Clear();
        _actionRing.Clear();

        _dragAnchor = Float4x4.TransformPoint(point >= 0 ? _mesh!.Points[point] : edge >= 0 ? (_mesh!.Points[_edges[edge].a] + _mesh.Points[_edges[edge].b]) * 0.5f : Float3.Zero, Target.Transform.LocalToWorldMatrix);

        if (point >= 0)
        {
            _actionCorner = _mesh!.Points[point];
            foreach (int p in _mesh.PolygonsAtPoint(point)) _actionNormals.Add(_mesh.PolygonNormal(p));
        }
        else if (edge >= 0)
        {
            (int a, int b) = _edges[edge];
            _actionCorner = (_mesh!.Points[a] + _mesh.Points[b]) * 0.5f;
            foreach (int p in _mesh.PolygonsAtEdge(a, b)) _actionNormals.Add(_mesh.PolygonNormal(p));
        }
        else if (face >= 0)
        {
            foreach (int i in _mesh!.PolygonPoints(face)) _actionRing.Add(_mesh.Points[i]);
            _actionCorner = _mesh.PolygonNormal(face);
            int plane = PlaneOf(face);
            _actionSurface = plane >= 0 ? Target.Surfaces[plane] : SurfaceData.Default;
        }
    }

    /// <summary>The polygon under the cursor, found from the plane the ray entered through.</summary>
    private bool TryPickFace(SceneToolContext ctx, out int polygon, out ControlID id)
    {
        polygon = -1;
        id = ControlID.None;

        HandleContext handles = ctx.Handles;
        Float4x4 toLocal = Target!.Transform.WorldToLocalMatrix;

        Float3 origin = Float4x4.TransformPoint(handles.MouseRay.Origin, toLocal);
        Float3 direction = Float4x4.TransformNormal(handles.MouseRay.Direction, toLocal);

        if (!_mesh!.Raycast(origin, direction, out float t, out polygon)) return false;

        id = handles.GetControlID(FaceControl, polygon);
        handles.AddControl(id, HandleContext.BodyDistance, t);
        return true;
    }

    private void SelectVertex(HandleContext handles, int index) => Apply(handles, [index], []);

    private void SelectEdge(HandleContext handles, int index)
        => Apply(handles, [_edges[index].a, _edges[index].b], []);

    private void SelectFace(HandleContext handles, int polygon)
        => Apply(handles, _mesh!.PolygonPoints(polygon), [polygon]);

    /// <summary>Shift adds, Ctrl removes, a plain click replaces.</summary>
    private void Apply(HandleContext handles, IReadOnlyList<int> vertices, IReadOnlyList<int> faces)
    {
        if (!handles.Shift && !handles.Ctrl) ClearSelection();

        foreach (int v in vertices)
        {
            if (handles.Ctrl) _vertices.Remove(v);
            else _vertices.Add(v);
        }

        foreach (int f in faces)
        {
            if (handles.Ctrl) _faces.Remove(f);
            else if (!_faces.Contains(f)) _faces.Add(f);
        }
    }

    /// <summary>Runs whichever gesture is in progress, previewing live and committing on release.</summary>
    private void TickAction(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;

        // Cursor travel in world units at the brush's distance, counted from the drag threshold
        float pixels = Float2.Distance(handles.MousePosition, _dragOrigin);
        _passedThreshold |= pixels >= DragThreshold;
        bool moved = _passedThreshold;
        float amount = MathF.Max(0f, pixels - DragThreshold) * WorldPerPixel(ctx);

        switch (_action)
        {
            case Action.Chamfer:
                if (moved)
                {
                    amount = CSGSnap.Value(handles, amount);
                    PreviewChamfer(ctx, amount);
                    ctx.Draw.Label(Float4x4.TransformPoint(_actionCorner, _actionBrush!.Transform.LocalToWorldMatrix), amount.ToString("0.###"), PickedColor);
                }
                break;
            case Action.Extrude:
                amount = CSGSnap.Value(handles, amount);
                if (moved) PreviewExtrude(ctx, amount);
                break;
            case Action.Drag:
                if (moved) DragPoints(ctx);
                if (_dragStarted) DrawDistance(ctx, _dragAnchor, _dragAnchor - _dragAnchorStart);
                break;
            case Action.Marquee: DrawMarquee(ctx); break;
        }

        if (!handles.PrimaryUp && handles.PrimaryHeld) return;

        switch (_action)
        {
            case Action.Extrude: CommitExtrude(amount); break;
            case Action.Marquee: ApplyMarquee(ctx); break;
        }

        // A click on part of a selection, with no drag, narrows the selection to what was clicked
        if (_action == Action.Drag && !_passedThreshold && _clickedPoints != null)
        {
            ClearSelection();
            foreach (int point in _clickedPoints) _vertices.Add(point);
        }

        EndAction();
    }

    /// <summary>World units one pixel spans at the brush, so drags scale with the view.</summary>
    private float WorldPerPixel(SceneToolContext ctx)
    {
        Float3 centre = Target!.Transform.Position;
        Float2? a = ctx.Handles.WorldToScreen(centre);
        Float2? b = ctx.Handles.WorldToScreen(centre + ctx.Camera.GameObject.Transform.Right);

        if (a is null || b is null) return 0.01f;

        float pixels = Float2.Distance(a.Value, b.Value);
        return pixels > 1e-3f ? 1f / pixels : 0.01f;
    }

    private void PreviewChamfer(SceneToolContext ctx, float amount)
    {
        // Back at the start there is no cut, so the brush goes back to how it began
        if (amount <= 1e-4f || !CSGMeshEdit.ChamferPlane(_actionNormals, _actionCorner, amount, out Plane cut))
        {
            if (!ReferenceEquals(Target!.ControlMesh, _startMesh))
            {
                Target.SetShape(_startMesh!.Clone(), (SurfaceData[])_startSurfaces!.Clone());
                MarkDirty();
            }
            return;
        }

        // The new face takes the look of the face nearest its direction
        SurfaceData cap = CSGSelection.NearestSurface(Target!, cut.Normal, _startSurfaces!.Length);
        if (CSGSelection.CutAway(_startMesh!, _startSurfaces, cut, cap) is not { } chamfered) return;

        Undo.RecordObject(Target!, "Chamfer CSG Brush");
        Target!.SetShape(chamfered.Mesh, chamfered.Surfaces);
        MarkDirty();
    }

    /// <summary>Draws where the extruded brush would go, without creating it until release.</summary>
    private void PreviewExtrude(SceneToolContext ctx, float amount)
    {
        if (!TryExtrudeRing(out List<Float3> ring, out Float3 normal)) return;

        Float4x4 toWorld = Target!.Transform.LocalToWorldMatrix;
        var top = new Float3[ring.Count];

        for (int i = 0; i < ring.Count; i++)
        {
            Float3 lifted = ring[i] + normal * amount;
            top[i] = Float4x4.TransformPoint(lifted, toWorld);

            CSGGizmos.Line(Float4x4.TransformPoint(ring[i], toWorld), top[i], PickedColor, 2f);
        }

        CSGGizmos.Polyline(top, PickedColor, 2f, closed: true);
        ctx.Draw.Label(top[0], amount.ToString("0.###"), PickedColor);
    }

    private bool TryExtrudeRing(out List<Float3> ring, out Float3 normal)
    {
        ring = _actionRing;
        normal = _actionCorner;
        return ring.Count >= 3;
    }

    /// <summary>Turns the pulled face into a sibling brush of its own.</summary>
    private void CommitExtrude(float amount)
    {
        if (amount <= 1e-3f || !TryExtrudeRing(out List<Float3> ring, out Float3 normal)) return;

        CSGControlMesh? prism = CSGControlMesh.Prism(ring, normal * amount);
        if (prism == null) return;

        GameObject go = CSGSelection.CreateObject(Target!.GameObject.Name, Target.GameObject.Parent);
        go.Transform.LocalPosition = Target.Transform.LocalPosition;
        go.Transform.LocalRotation = Target.Transform.LocalRotation;
        go.Transform.LocalScale = Target.Transform.LocalScale;

        CSGBrush extruded = go.AddComponent<CSGBrush>();
        var faces = new SurfaceData[prism.Polygons.Count];
        Array.Fill(faces, _actionSurface);
        extruded.SetShape(prism, faces);
        extruded.Operation = Target.Operation;
        extruded.Flags = Target.Flags;

        CSGSelection.PlaceAfter(go, Target.GameObject);
        Undo.RegisterCreatedObjectUndo(go, "Extrude CSG Face");
        Selection.Select(go);
        MarkDirty();
    }

    private void DrawMarquee(SceneToolContext ctx)
        => ctx.Draw.ScreenRect(_dragOrigin, ctx.Handles.MousePosition,
            Color32.FromArgb(40, 120, 170, 255), Color32.FromArgb(200, 140, 190, 255));

    /// <summary>Selects every point inside the rectangle, then promotes edges and faces whose corners are all in.</summary>
    private void ApplyMarquee(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;
        if (!handles.Shift && !handles.Ctrl) ClearSelection();

        Float2 min = Maths.Min(_dragOrigin, handles.MousePosition);
        Float2 max = Maths.Max(_dragOrigin, handles.MousePosition);

        Float4x4 toWorld = Target!.Transform.LocalToWorldMatrix;

        for (int i = 0; i < _mesh!.Points.Count; i++)
        {
            Float2? screen = handles.WorldToScreen(Float4x4.TransformPoint(_mesh.Points[i], toWorld));
            if (screen is null) continue;

            Float2 p = screen.Value;
            if (p.X < min.X || p.X > max.X || p.Y < min.Y || p.Y > max.Y) continue;

            if (handles.Ctrl) _vertices.Remove(i);
            else _vertices.Add(i);
        }

        PromoteToFaces();
    }

    /// <summary>A face joins the selection once every one of its corners is in it.</summary>
    private void PromoteToFaces()
    {
        _faces.Clear();

        for (int polygon = 0; polygon < _mesh!.Polygons.Count; polygon++)
        {
            IReadOnlyList<int> ring = _mesh.PolygonPoints(polygon);
            if (ring.Count == 0) continue;

            bool all = true;
            foreach (int i in ring)
            {
                if (_vertices.Contains(i)) continue;
                all = false;
                break;
            }

            if (all) _faces.Add(polygon);
        }
    }

    private void EndAction()
    {
        _action = Action.None;
        _startSurfaces = null;
        _startMesh = null;
        _actionBrush = null;
    }

    private void MoveSelection(SceneToolContext ctx)
    {
        if (_vertices.Count == 0) { _dragging = false; return; }

        HandleContext handles = ctx.Handles;
        Float4x4 toWorld = Target!.Transform.LocalToWorldMatrix;

        Float3 centre = Float3.Zero;
        foreach (int i in _vertices) centre += _mesh!.Points[i];
        centre = Float4x4.TransformPoint(centre / _vertices.Count, toWorld);

        // While extruding the points stay put, so the handle is carried along by the drag instead
        if (_gizmoExtrude && _dragging) centre += _dragRaw;

        Float3 before = centre;
        bool moved = TransformHandles.PositionHandle(handles, MoveHandle, ref centre, out bool hot);
        if (hot && !_dragging)
        {
            _dragRaw = _dragApplied = Float3.Zero;

            // Shift on a whole face pulls it out as a brush of its own, as Shift dragging the face does
            _gizmoExtrude = handles.Shift && _faces.Count == 1 && _vertices.Count == _mesh!.PolygonPoints(_faces[0]).Count;
            if (_gizmoExtrude) PrepareExtrude(_faces[0]);
        }

        if (_dragging && !hot && _gizmoExtrude)
        {
            _gizmoExtrude = false;
            CommitExtrude(GizmoExtrudeAmount(toWorld));
        }

        _dragging = hot;

        // Snapped over the whole drag so small per frame steps do not round away.
        Float3 step = Float3.Zero;
        if (moved)
        {
            _dragRaw += centre - before;
            Float3 snapped = CSGSnap.Value(handles, _dragRaw);
            step = snapped - _dragApplied;
            _dragApplied = snapped;
        }

        if (_gizmoExtrude)
        {
            if (hot) PreviewExtrude(ctx, MathF.Max(0f, GizmoExtrudeAmount(toWorld)));
            return;
        }

        if (hot) DrawDistance(ctx, centre, _dragApplied);
        if (!moved || Float3.LengthSquared(step) < 1e-12f) return;

        Float4x4 toLocal = Target.Transform.WorldToLocalMatrix;
        Float3 delta = Float4x4.TransformPoint(before + step, toLocal) - Float4x4.TransformPoint(before, toLocal);

        CSGControlMesh? mesh = BeginPointEdit("Edit CSG Brush");
        if (mesh == null) return;

        // A whole face slides along its own normal, otherwise only the selected points move
        List<int> moving = [.. _vertices];
        if (_faces.Count == 1 && _vertices.Count == mesh.PolygonPoints(_faces[0]).Count)
            delta = mesh.PolygonNormal(_faces[0]) * Float3.Dot(delta, mesh.PolygonNormal(_faces[0]));

        mesh.MovePoints(moving, delta);
        Target.ApplyControlMesh();

        // A move that stops the points enclosing a solid is taken back, and the drag waits where it was
        if (!Target.IsShapeValid)
        {
            CSGStatus.Flash(Loc.Get("csg.status_inside_out"));
            mesh.MovePoints(moving, -delta);
            Target.ApplyControlMesh();
            _dragApplied -= step;
        }

        MarkDirty();
    }

    /// <summary>How far the gizmo has been dragged along the face being extruded, in the brush's own units.</summary>
    private float GizmoExtrudeAmount(Float4x4 toWorld)
    {
        Float3 normal = Float4x4.TransformNormal(_actionCorner, toWorld);
        float scale = Float3.Length(normal);
        return scale < 1e-6f ? 0f : Float3.Dot(_dragApplied, normal / scale) / scale;
    }

    /// <summary>Scales the selected face about its centre and rotates a selected edge about itself.</summary>
    private void ShapeHandles(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;
        if (handles.KeyboardCaptured) return;

        // A face taper on the bracket keys, matching how the grid is halved and doubled.
        if (_faces.Count == 1)
        {
            if (handles.IsPressed("CSG/HalveGrid")) ScaleFace(0.9f);
            else if (handles.IsPressed("CSG/DoubleGrid")) ScaleFace(1f / 0.9f);
        }

        // An edge tilt on the same keys the object tool rotates with.
        if (_vertices.Count != 2 || !IsEdge(_vertices)) return;

        if (handles.IsPressed("CSG/RotateLeft")) RotateEdge(-SceneTools.RotateSnap);
        else if (handles.IsPressed("CSG/RotateRight")) RotateEdge(SceneTools.RotateSnap);
    }

    private void ScaleFace(float factor)
    {
        CSGControlMesh? mesh = BeginPointEdit("Scale CSG Face");
        if (mesh == null) return;

        mesh.ScalePolygon(_faces[0], factor);
        Target!.ApplyControlMesh();
        if (!Target.IsShapeValid)
        {
            CSGStatus.Flash(Loc.Get("csg.status_inside_out"));
            mesh.ScalePolygon(_faces[0], 1f / factor);
            Target.ApplyControlMesh();
        }
        MarkDirty();
    }

    private bool IsEdge(HashSet<int> pair)
    {
        foreach ((int a, int b) in _edges)
            if (pair.Contains(a) && pair.Contains(b)) return true;
        return false;
    }

    private void RotateEdge(float degrees)
    {
        var pair = new List<int>(_vertices);
        if (pair.Count != 2) return;

        Float3 a = _mesh!.Points[pair[0]], b = _mesh.Points[pair[1]];
        Float3 axis = b - a;
        if (Float3.LengthSquared(axis) < 1e-10f) return;

        // Everything except the edge turns about it, tilting the faces either side.
        var others = new List<int>();
        for (int i = 0; i < _mesh.Points.Count; i++)
            if (!_vertices.Contains(i)) others.Add(i);

        CSGControlMesh? mesh = BeginPointEdit("Rotate CSG Edge");
        if (mesh == null) return;

        mesh.RotatePoints(others, (a + b) * 0.5f, axis, degrees);
        Target!.ApplyControlMesh();
        if (!Target.IsShapeValid)
        {
            CSGStatus.Flash(Loc.Get("csg.status_inside_out"));
            mesh.RotatePoints(others, (a + b) * 0.5f, axis, -degrees);
            Target.ApplyControlMesh();
        }
        MarkDirty();
    }

    private void SnapSelection()
    {
        if (_vertices.Count == 0) return;

        CSGControlMesh? mesh = BeginPointEdit("Snap CSG Vertices");
        if (mesh == null) return;

        // Each point snaps on its own, which squares up a skewed brush
        foreach (int i in _vertices)
            mesh.SetPoint(i, CSGSnap.Round(mesh.Points[i]));

        Target!.ApplyControlMesh();
        MarkDirty();
    }

    /// <summary>Removes a face by dropping its plane, the faces around it grow to close the gap.</summary>
    private void DeleteFaces()
    {
        if (_faces.Count == 0 || Target.IsNotValid()) return;

        // Dropping a face lets its neighbours grow over the gap, which only has a meaning for a convex brush
        if (!Target!.ControlMesh.IsConvex())
        {
            CSGStatus.Flash(Loc.Get("csg.status_delete_concave"));
            return;
        }

        Undo.RecordObject(Target!, "Remove CSG Face");

        // Planes found first, then removed highest first, so removing one does not renumber the others
        var planes = new List<int>();
        foreach (int face in _faces)
            if (PlaneOf(face) is int plane and >= 0) planes.Add(plane);
        planes.Sort();

        for (int i = planes.Count - 1; i >= 0; i--)
        {
            if (CSGMeshEdit.RemoveFace(Target!.Planes, planes[i], out Plane[] fewer, out SurfaceData[] surfaces, Target.Surfaces))
                Target.SetPlanes(fewer, surfaces, out _);
        }

        ClearSelection();
        MarkDirty();
    }

    private static void MarkDirty()
    {
        CSGModel? model = CSGSelection.ActiveModel();
        if (model.IsValid()) model!.SetDirty();
    }

    private void DrawElements(SceneToolContext ctx)
    {
        Float4x4 toWorld = Target!.Transform.LocalToWorldMatrix;

        foreach (int polygon in _faces)
        {
            IReadOnlyList<int> ring = _mesh!.PolygonPoints(polygon);
            if (ring.Count < 3) continue;

            var corners = new Float3[ring.Count];
            for (int i = 0; i < ring.Count; i++)
                corners[i] = Float4x4.TransformPoint(_mesh.Points[ring[i]], toWorld);

            CSGGizmos.Polygon(corners, FaceFill);
        }

        if (Filter.HasFlag(CSGElementFilter.Edge))
        {
            foreach ((int a, int b) in _edges)
            {
                bool picked = _vertices.Contains(a) && _vertices.Contains(b);
                Float3 from = Float4x4.TransformPoint(_mesh!.Points[a], toWorld);
                Float3 to = Float4x4.TransformPoint(_mesh.Points[b], toWorld);
                CSGGizmos.Line(from, to, picked ? PickedColor : IdleColor, picked ? 2.5f : 1f);

                // Edges are picked at their middle, so that is where their handle is
                ctx.Draw.Dot((from + to) * 0.5f, picked ? PickedColor : EdgeHandleColor, 5f);
            }
        }

        if (!Filter.HasFlag(CSGElementFilter.Vertex)) return;

        for (int i = 0; i < _mesh!.Points.Count; i++)
        {
            ctx.Draw.Dot(Float4x4.TransformPoint(_mesh.Points[i], toWorld),
                _vertices.Contains(i) ? PickedColor : IdleColor,
                _vertices.Contains(i) ? 8f : 6f);
        }
    }

    public override void OnDrawOverlay(SceneToolContext ctx, Quill.Canvas canvas)
    {
        if (Active) TransformHandles.Draw(canvas);
    }
}
