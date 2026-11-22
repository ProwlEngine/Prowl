// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Core;
using Prowl.Editor.Theming;
using Prowl.Editor.Modeling;
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
    private const string MenuActionControl = "csg_mesh_menu_action";

    private const float PickRadius = 9f;

    private static readonly Color32 IdleColor = new(150, 190, 230, 200);
    private static readonly Color32 PickedColor = new(255, 210, 80, 255);
    private static readonly Color32 FaceFill = new(255, 200, 90, 60);
    private static readonly Color32 HoverEdgeColor = new(210, 235, 255, 255);

    /// <summary>Editing a brush's points: Edit is the tool and the selection is one brush.</summary>
    public bool Active => CSGEditMode.Is(CSGMode.Edit) && CSGSelection.Brushes().Count == 1;

    /// <summary>Which element types a click will pick.</summary>
    public CSGElementFilter Filter { get; set; } = CSGElementFilter.All;

    /// <summary>The brush being edited, or null when the selection is not a single brush.</summary>
    public CSGBrush? Target { get; private set; }

    public int SelectedCount => _vertices.Count;

    public int SelectedFaceCount => _faces.Count;

    /// <summary>Whether a chamfer has something to cut: a selected edge, or a selected corner.</summary>
    public bool CanChamfer => Active && _vertices.Count > 0;

    /// <summary>Whether one whole face is selected to pull out.</summary>
    public bool CanExtrude => Active && _faces.Count == 1;

    /// <summary>Whether any face is selected to inset.</summary>
    public bool CanInset => Active && _faces.Count > 0;

    /// <summary>Whether selected faces can be dropped, which only means something on a convex brush.</summary>
    public bool CanDeleteFaces => Active && _faces.Count > 0 && Target.IsValid() && Target!.ControlMesh.IsConvex();

    public bool CanSnap => Active && _vertices.Count > 0;

    /// <summary>Starts cutting every selected edge, or every selected corner when no whole edge is selected, by as far as the mouse moves.</summary>
    public void ChamferSelected()
    {
        if (!CanChamfer) return;
        BeginAction(Context.Handles, Action.Chamfer, fromMenu: true);
    }

    /// <summary>Starts pulling the one selected face out into a brush of its own, by as far as the mouse moves.</summary>
    public void ExtrudeSelected()
    {
        if (!CanExtrude) return;
        BeginAction(Context.Handles, Action.Extrude, face: _faces[0], fromMenu: true);
    }

    /// <summary>Starts insetting every selected face, by as far as the mouse moves.</summary>
    public void InsetSelected()
    {
        if (!CanInset) return;
        BeginAction(Context.Handles, Action.Inset, fromMenu: true);
    }

    /// <summary>The start shape with every selected edge, or else every selected corner, cut off by a distance.</summary>
    private (CSGControlMesh Mesh, SurfaceData[] Surfaces)? ChamferSelection(float distance)
    {
        CSGControlMesh start = _startMesh!;
        var cuts = new List<Plane>();

        foreach ((int a, int b) in start.Edges())
        {
            if (!_actionPoints.Contains(a) || !_actionPoints.Contains(b)) continue;
            var normals = new List<Float3>();
            foreach (int p in start.PolygonsAtEdge(a, b)) normals.Add(start.PolygonNormal(p));
            if (CSGMeshEdit.ChamferPlane(normals, (start.Points[a] + start.Points[b]) * 0.5f, distance, out Plane cut)) cuts.Add(cut);
        }

        if (cuts.Count == 0)
        {
            foreach (int point in _actionPoints)
            {
                var normals = new List<Float3>();
                foreach (int p in start.PolygonsAtPoint(point)) normals.Add(start.PolygonNormal(p));
                if (CSGMeshEdit.ChamferPlane(normals, start.Points[point], distance, out Plane cut)) cuts.Add(cut);
            }
        }

        if (cuts.Count == 0) return null;

        CSGControlMesh mesh = start.Clone();
        SurfaceData[] surfaces = (SurfaceData[])_startSurfaces!.Clone();
        foreach (Plane cut in cuts)
        {
            if (CSGSelection.CutAway(mesh, surfaces, cut, CSGSelection.NearestSurface(Target!, cut.Normal, surfaces.Length)) is not { } piece) return null;
            (mesh, surfaces) = piece;
        }
        return (mesh, surfaces);
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
    private enum Action { None, Marquee, Chamfer, Extrude, Inset, Drag }

    // Pixels the cursor has to travel before a press becomes a chamfer, extrude or drag, so a click stays a click
    private const float DragThreshold = 4f;

    private CSGControlMesh? _mesh;
    private object? _meshSource;

    // The brush as an edit target, loaded again whenever its points change, and the selection as undo keeps it
    private CSGBrushTarget? _target;
    private readonly CSGEditSelection _held = new();
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

    // An action started from the menu follows the mouse with no button held, and the next click commits it. It runs
    // modal, holding the viewport it started in until it ends
    private bool _fromMenu;
    private HandleContext? _modalHandles;
    private readonly HashSet<int> _actionPoints = new();
    private readonly List<int> _actionFaces = new();

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

    public override void OnDeactivated()
    {
        CancelAction();
        ClearSelection();
    }

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
        HoldSelection();

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

        if (!ReferenceEquals(brush, Target))
        {
            Target = brush;
            ClearSelection();
            _meshSource = null;
        }
        if (Target.IsNotValid()) { Target = null; _target = null; return false; }

        // The points changed from outside the tool, by an undo, a redo or the Inspector, so the target is loaded again
        // and the selection comes back as undo kept it
        if (!ReferenceEquals(Target!.ControlMesh, _meshSource))
        {
            bool sameBrush = _meshSource != null;
            Reload();
            if (sameBrush && _action == Action.None) RestoreHeldSelection();
        }

        _mesh = Target.ControlMesh;
        _vertices.RemoveWhere(i => i >= _mesh.Points.Count);
        _faces.RemoveAll(f => f >= _mesh.Polygons.Count);

        NonPlanar = !_mesh.IsPlanar();
        return _mesh.Points.Count > 0;
    }

    /// <summary>Loads the brush's points as the edit target, after the tool or anything else changed them.</summary>
    private void Reload()
    {
        _mesh = Target!.ControlMesh;
        _meshSource = _mesh;
        _target = new CSGBrushTarget(_mesh, Target.Surfaces, Target.Transform.LocalToWorldMatrix);
        _edges = _mesh.Edges();
    }

    private void HoldSelection()
    {
        _held.Points = [.. _vertices];
        _held.Faces = [.. _faces];
    }

    private void RestoreHeldSelection()
    {
        _vertices.Clear();
        _vertices.UnionWith(_held.Points);
        _faces.Clear();
        _faces.AddRange(_held.Faces);
    }

    /// <summary>The selected points and faces as elements of the edit target.</summary>
    private ElementSelection SelectedElements()
    {
        var selected = new ElementSelection();
        var vertices = _target!.Target.Mesh.Vertices;
        foreach (int i in _vertices)
            if (i >= 0 && i < vertices.Count) selected.Vertices.Add(vertices[i]);
        foreach (GeometryData.Face face in _target.FacesOf(_faces)) selected.Faces.Add(face);
        return selected;
    }

    /// <summary>
    /// Runs an edit on the selected elements through the edit target: recorded for undo with the selection, refused with
    /// a reason when the brush could not keep it, and written back to the brush when it can.
    /// </summary>
    private bool RunEdit(string name, Action<ElementSelection> edit)
    {
        if (Target.IsNotValid() || _target == null) return false;

        ElementSelection selected = SelectedElements();
        Undo.RecordObject(Target!, name);
        HoldSelection();
        Undo.RecordObject(_held, UndoOwner.Of(Target!), name);

        string? problem = MeshEdit.Run(_target.Target, (_, _) => edit(selected), new ElementSelection());
        if (problem != null)
        {
            CSGStatus.Flash(Loc.Get(problem));
            return false;
        }

        (CSGControlMesh mesh, SurfaceData[] surfaces) = _target.ToBrush(selected, out HashSet<int> points, out List<int> faces);
        Target!.SetShape(mesh, surfaces);
        _vertices.Clear();
        _vertices.UnionWith(points);
        _faces.Clear();
        _faces.AddRange(faces);
        Reload();
        HoldSelection();
        MarkDirty();
        return true;
    }

    /// <summary>Puts a whole shape on the brush from inside the tool, keeping the edit target in step with it.</summary>
    private void ApplyShape(CSGBrush brush, CSGControlMesh mesh, SurfaceData[] surfaces)
    {
        brush.SetShape(mesh, surfaces);
        if (ReferenceEquals(brush, Target)) Reload();
        MarkDirty();
    }

    /// <summary>The surface of a polygon of the points, which is the polygon's own index.</summary>
    private int PlaneOf(int polygon) => polygon >= 0 && polygon < Target!.Surfaces.Length ? polygon : -1;

    /// <summary>Abandons a gesture in progress, putting back what a chamfer changed.</summary>
    private void CancelAction()
    {
        if (_action == Action.None) return;
        // Exactly the shape it started with, so the open undo record finds nothing changed and leaves no step
        if (_action is Action.Chamfer or Action.Inset or Action.Drag && _actionBrush.IsValid() && _startMesh != null)
            ApplyShape(_actionBrush!, _startMesh.Clone(), (SurfaceData[])_startSurfaces!.Clone());
        EndAction();
    }

    private void ClearSelection()
    {
        _vertices.Clear();
        _faces.Clear();
    }

    /// <summary>Whether the camera sees a point of the brush, rather than one of the brush's own faces in front of it.</summary>
    private bool Seen(HandleContext handles, Float3 point)
    {
        Camera camera = handles.Camera;
        Transform view = camera.GameObject.Transform;
        Float3 eye = camera.IsOrthographic
            ? Float4x4.TransformPoint(point, Target!.Transform.LocalToWorldMatrix) - view.Forward * 10000f
            : view.Position;

        Float3 origin = Float4x4.TransformPoint(eye, Target!.Transform.WorldToLocalMatrix);
        Float3 toPoint = point - origin;
        float length = Float3.Length(toPoint);

        return length < 1e-6f
            || !_mesh!.Raycast(origin, toPoint / length, out float t, out _, bothSides: true)
            || t >= length - MathF.Max(1e-3f, length * 1e-4f);
    }

    private bool[] SeenPoints(HandleContext handles)
    {
        var seen = new bool[_mesh!.Points.Count];
        for (int i = 0; i < seen.Length; i++) seen[i] = Seen(handles, _mesh.Points[i]);
        return seen;
    }

    /// <summary>An edge can be picked while either end or its middle is seen.</summary>
    private bool EdgeSeen(HandleContext handles, bool[] seen, int edge)
    {
        (int a, int b) = _edges[edge];
        return seen[a] || seen[b] || Seen(handles, (_mesh!.Points[a] + _mesh.Points[b]) * 0.5f);
    }

    private void PickElements(SceneToolContext ctx)
    {
        if (_dragging || _action != Action.None) return;

        HandleContext handles = ctx.Handles;
        Float4x4 toWorld = Target!.Transform.LocalToWorldMatrix;
        bool[] seen = SeenPoints(handles);

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
            float endGap = Filter.HasFlag(CSGElementFilter.Vertex) ? PickRadius : 0f;
            for (int i = 0; i < _edges.Count; i++)
            {
                if (!EdgeSeen(handles, seen, i)) continue;
                Float3 from = Float4x4.TransformPoint(_mesh!.Points[_edges[i].a], toWorld);
                Float3 to = Float4x4.TransformPoint(_mesh.Points[_edges[i].b], toWorld);

                ControlID id = handles.GetControlID(EdgeControl, i);
                handles.AddControl(id, from, to, PickRadius, endGap);

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
                if (!seen[i]) continue;
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

        Float4x4 toLocal = Target!.Transform.WorldToLocalMatrix;
        Float3 delta = Float4x4.TransformPoint(_dragAnchor + step, toLocal) - Float4x4.TransformPoint(_dragAnchor, toLocal);

        if (RunEdit("Move CSG Vertices", selected => Translate(selected, delta))) _dragAnchor = target;
    }

    /// <summary>How far a drag has moved, beside what is being moved.</summary>
    private static void DrawDistance(SceneToolContext ctx, Float3 at, Float3 moved)
    {
        if (Float3.LengthSquared(moved) < 1e-10f) return;
        ctx.Draw.Label(at, $"{Float3.Length(moved):0.###}  ({moved.X:0.###}, {moved.Y:0.###}, {moved.Z:0.###})", PickedColor);
    }

    private void BeginAction(HandleContext handles, Action action, int point = -1, int edge = -1, int face = -1, bool fromMenu = false)
    {
        _action = action;
        _fromMenu = fromMenu;
        if (fromMenu)
        {
            handles.Modal = true;
            _modalHandles = handles;
        }
        _actionPoints.Clear();
        _actionFaces.Clear();
        if (fromMenu)
        {
            _actionPoints.UnionWith(_vertices);
            _actionFaces.AddRange(_faces);
        }
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

        if (!_mesh!.Raycast(origin, direction, out float t, out polygon, bothSides: true)) return false;

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

        // Cursor travel in world units at the brush's distance, counted from the drag threshold. A menu action has no
        // press to tell apart from a click, so it counts from where the menu was used
        float pixels = Float2.Distance(handles.MousePosition, _dragOrigin);
        _passedThreshold |= _fromMenu || pixels >= DragThreshold;
        bool moved = _passedThreshold;
        float amount = MathF.Max(0f, pixels - (_fromMenu ? 0f : DragThreshold)) * WorldPerPixel(ctx);

        // While a menu action runs the tool holds the viewport, so the click that commits it picks nothing else
        ControlID owner = handles.GetControlID(MenuActionControl);
        if (_fromMenu) handles.AddControl(owner, HandleContext.BodyDistance, float.MaxValue - 1f);

        switch (_action)
        {
            case Action.Chamfer:
                if (moved)
                {
                    amount = CSGSnap.Value(handles, amount);
                    if (_fromMenu) PreviewChamferSelection(amount);
                    else PreviewChamfer(ctx, amount);
                    ctx.Draw.Label(_fromMenu ? handles.MouseRay.Origin + handles.MouseRay.Direction * 5f : Float4x4.TransformPoint(_actionCorner, _actionBrush!.Transform.LocalToWorldMatrix), amount.ToString("0.###"), PickedColor);
                }
                break;
            case Action.Inset:
                amount = CSGSnap.Value(handles, amount);
                PreviewInset(amount);
                ctx.Draw.Label(handles.MouseRay.Origin + handles.MouseRay.Direction * 5f, amount.ToString("0.###"), PickedColor);
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

        if (_fromMenu)
        {
            // The camera stands still while modal, so a right click is free to cancel
            if (handles.ViewportHovered && Input.GetMouseButtonDown(1))
            {
                CancelAction();
                return;
            }
            if (!handles.IsNearest(owner) || !handles.TryBeginDrag(owner)) return;
            handles.TryEndDrag(owner);
        }
        else if (!handles.PrimaryUp && handles.PrimaryHeld) return;

        switch (_action)
        {
            case Action.Extrude: CommitExtrude(amount); break;
            case Action.Marquee: ApplyMarquee(ctx); break;
            case Action.Chamfer or Action.Inset: ClearSelection(); break;
        }

        // A click on part of a selection, with no drag, narrows the selection to what was clicked
        if (_action == Action.Drag && !_passedThreshold && _clickedPoints != null)
        {
            ClearSelection();
            foreach (int point in _clickedPoints) _vertices.Add(point);
        }

        EndAction();
    }

    /// <summary>The start shape with the action's faces inset, or the start shape back again at no distance.</summary>
    private void PreviewInset(float amount)
    {
        if (amount <= 1e-4f)
        {
            RestoreStart();
            return;
        }

        var target = new CSGBrushTarget(_startMesh!, _startSurfaces!);
        List<GeometryData.Face> faces = target.FacesOf(_actionFaces);
        string? problem = MeshEdit.Run(target.Target, (mesh, selected) => Operators.Inset(mesh, faces, amount, selected), new ElementSelection());
        if (problem != null)
        {
            CSGStatus.Flash(Loc.Get(problem));
            return;
        }

        (CSGControlMesh mesh, SurfaceData[] surfaces) = target.ToBrush();
        Undo.RecordObject(Target!, "Inset CSG Face");
        ApplyShape(Target!, mesh, surfaces);
    }

    private void PreviewChamferSelection(float amount)
    {
        if (amount <= 1e-4f || ChamferSelection(amount) is not { } chamfered)
        {
            RestoreStart();
            return;
        }

        Undo.RecordObject(Target!, "Chamfer CSG Brush");
        ApplyShape(Target!, chamfered.Mesh, chamfered.Surfaces);
    }

    /// <summary>Puts the brush back as the action found it.</summary>
    private void RestoreStart()
    {
        if (ReferenceEquals(Target!.ControlMesh, _startMesh)) return;
        ApplyShape(Target, _startMesh!.Clone(), (SurfaceData[])_startSurfaces!.Clone());
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
            RestoreStart();
            return;
        }

        // The new face takes the look of the face nearest its direction
        SurfaceData cap = CSGSelection.NearestSurface(Target!, cut.Normal, _startSurfaces!.Length);
        if (CSGSelection.CutAway(_startMesh!, _startSurfaces, cut, cap) is not { } chamfered) return;

        Undo.RecordObject(Target!, "Chamfer CSG Brush");
        ApplyShape(Target!, chamfered.Mesh, chamfered.Surfaces);
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
            if (p.X < min.X || p.X > max.X || p.Y < min.Y || p.Y > max.Y || !Seen(handles, _mesh.Points[i])) continue;

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
        if (_modalHandles != null)
        {
            _modalHandles.Modal = false;
            _modalHandles = null;
        }
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
        Quaternion axes = SceneTools.Orientation == PivotOrientation.Local ? Target.Transform.Rotation : Quaternion.Identity;
        bool moved = TransformHandles.PositionHandle(handles, MoveHandle, ref centre, axes, out bool hot);
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

        // Snapped over the whole drag, along the handle's own axes, so small per frame steps do not round away.
        Float3 step = Float3.Zero;
        if (moved)
        {
            _dragRaw += centre - before;
            Float3 snapped = axes * CSGSnap.Value(handles, Quaternion.Inverse(axes) * _dragRaw);
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

        // A move the brush cannot keep is refused, and the drag waits where it was
        if (!RunEdit("Edit CSG Brush", selected => Translate(selected, delta))) _dragApplied -= step;
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
        => RunEdit("Scale CSG Face", selected =>
        {
            foreach (GeometryData.Face face in selected.Faces)
            {
                List<GeometryData.Vertex> ring = face.NeighborVertices();
                Float3 centre = Float3.Zero;
                foreach (GeometryData.Vertex vertex in ring) centre += vertex.Point;
                centre /= ring.Count;
                foreach (GeometryData.Vertex vertex in ring) vertex.Point = centre + (vertex.Point - centre) * factor;
            }
        });

    private static void Translate(ElementSelection selected, Float3 delta)
    {
        foreach (GeometryData.Vertex vertex in selected.Vertices) vertex.Point += delta;
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
        Quaternion turn = Quaternion.AxisAngle(Float3.NormalizeSafe(axis, Float3.UnitY), degrees * (MathF.PI / 180f));
        Float3 pivot = (a + b) * 0.5f;

        RunEdit("Rotate CSG Edge", selected =>
        {
            foreach (GeometryData.Vertex vertex in _target!.Target.Mesh.Vertices)
                if (!selected.Vertices.Contains(vertex)) vertex.Point = pivot + turn * (vertex.Point - pivot);
        });
    }

    private void SnapSelection()
    {
        if (_vertices.Count == 0) return;

        // Each point snaps on its own, which squares up a skewed brush
        RunEdit("Snap CSG Vertices", selected =>
        {
            foreach (GeometryData.Vertex vertex in selected.Vertices) vertex.Point = CSGSnap.Round(vertex.Point);
        });
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
        Reload();
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
            for (int i = 0; i < _edges.Count; i++)
            {
                (int a, int b) = _edges[i];
                bool picked = _vertices.Contains(a) && _vertices.Contains(b);
                bool hovered = ctx.Handles.IsNearest(ctx.Handles.GetControlID(EdgeControl, i));
                Float3 from = Float4x4.TransformPoint(_mesh!.Points[a], toWorld);
                Float3 to = Float4x4.TransformPoint(_mesh.Points[b], toWorld);
                CSGGizmos.Line(from, to, picked ? PickedColor : hovered ? HoverEdgeColor : IdleColor, picked || hovered ? 2.5f : 1f);
            }
        }

        if (!Filter.HasFlag(CSGElementFilter.Vertex)) return;

        bool[] seen = SeenPoints(ctx.Handles);
        for (int i = 0; i < _mesh!.Points.Count; i++)
        {
            if (!seen[i] && !_vertices.Contains(i)) continue;
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

/// <summary>The Edit tool's selection as point and polygon indices, recorded with each edit so undo brings it back.</summary>
internal sealed class CSGEditSelection
{
    public int[] Points = [];
    public int[] Faces = [];
}
