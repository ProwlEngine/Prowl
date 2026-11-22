// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Core;
using Prowl.Editor.Theming;
using Prowl.PaperUI;
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

    public bool Active => CSGEditMode.Is(CSGMode.Mesh);

    /// <summary>Which element types a click will pick.</summary>
    public CSGElementFilter Filter { get; set; } = CSGElementFilter.All;

    /// <summary>The brush being edited, or null when the selection is not a single brush.</summary>
    public CSGBrush? Target { get; private set; }

    public int SelectedCount => _vertices.Count;

    /// <summary>One face is selected, so the bracket keys taper it instead of changing the grid.</summary>
    public bool TapersFace => Active && _faces.Count == 1;

    /// <summary>A face of the edited brush has been pulled out of its plane, so the shape only approximates the points.</summary>
    public bool NonPlanar { get; private set; }

    private readonly HashSet<int> _vertices = new();
    private readonly List<int> _faces = new();

    /// <summary>What a drag in progress is doing.</summary>
    private enum Action { None, Marquee, Chamfer, Extrude }

    private CSGControlMesh? _mesh;
    private object? _meshSource;
    private Plane[]? _previewPlanes;
    private CSGControlMesh? _preview;
    private List<(int a, int b)> _edges = new();
    private bool _dragging;
    private Float3 _dragRaw, _dragApplied;

    private Action _action;
    private Float2 _dragOrigin;

    // What a chamfer or extrude started on, captured when it began so later renumbering cannot move it
    private Plane[]? _startPlanes;
    private Float3 _actionCorner;
    private readonly List<Float3> _actionNormals = new();
    private readonly List<Float3> _actionRing = new();
    private SurfaceData _actionSurface;

    public override bool IsAvailable(SceneToolContext ctx) => CSGSelection.ActiveModel().IsValid();

    public override bool SuppressTransformGizmo => Active;

    public override void OnDeactivated() => ClearSelection();

    public override void OnSceneInput(SceneToolContext ctx)
    {
        if (!Active) return;

        if (!ResolveTarget()) return;

        HandleContext handles = ctx.Handles;

        if (handles.KeyDown(KeyCode.Escape))
        {
            if (_vertices.Count == 0) CSGEditMode.Current = CSGMode.Object;
            ClearSelection();
            return;
        }

        // Delete always belongs to the mesh tool here, so it never falls through to deleting the brush
        if (handles.KeyDown(KeyCode.Delete) || handles.KeyDown(KeyCode.Backspace)) DeleteFaces();
        if (handles.Ctrl && handles.KeyDown(KeyCode.End)) SnapSelection();

        DrawElements(ctx);

        // A gesture in progress owns the frame, the move handle and picking stand down.
        if (_action != Action.None) { TickAction(ctx); return; }

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

        if (!ReferenceEquals(brush, Target)) { Target = brush; ClearSelection(); _preview = null; }
        if (Target.IsNotValid()) { Target = null; return false; }

        if (Target!.HasControlMesh) _mesh = Target.ControlMesh;
        else
        {
            if (_preview == null || !ReferenceEquals(_previewPlanes, Target.Planes))
            {
                _preview = CSGControlMesh.FromPlanes(Target.Planes);
                _previewPlanes = Target.Planes;
            }
            _mesh = _preview;
        }
        if (_mesh == null) { Target = null; return false; }

        // New points after a plane edit or an undo, so old indices mean nothing
        if (!ReferenceEquals(_mesh, _meshSource))
        {
            _meshSource = _mesh;
            if (_action == Action.None) ClearSelection();
        }
        _vertices.RemoveWhere(i => i >= _mesh.Points.Count);
        _faces.RemoveAll(f => f >= _mesh.Polygons.Count);

        _edges = _mesh.Edges();
        NonPlanar = Target.HasControlMesh && !_mesh.IsPlanar();
        return _mesh.Points.Count > 0;
    }

    /// <summary>Records the brush and makes its points its own, before an edit to them.</summary>
    private CSGControlMesh? BeginPointEdit(string name)
    {
        Undo.RecordObject(Target!, name);
        CSGControlMesh? mesh = Target!.ControlMesh;
        if (mesh == null) return null;

        _mesh = mesh;
        _meshSource = mesh;
        return mesh;
    }

    /// <summary>The plane a polygon of the points belongs to.</summary>
    private int PlaneOf(int polygon)
    {
        int[] map = Target!.FaceToPlane;
        return polygon >= 0 && polygon < map.Length ? map[polygon] : -1;
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
                    SelectEdge(handles, i);
                    if (handles.Shift) BeginAction(handles, Action.Chamfer, edge: i);
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
                    SelectVertex(handles, i);
                    if (handles.Shift) BeginAction(handles, Action.Chamfer, point: i);
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
        handles.AddDefaultControl(marquee);

        if (handles.IsNearest(marquee) && handles.TryBeginDrag(marquee))
            BeginAction(handles, Action.Marquee);
    }

    private void BeginAction(HandleContext handles, Action action, int point = -1, int edge = -1, int face = -1)
    {
        _action = action;
        _dragOrigin = handles.MousePosition;
        _startPlanes = (Plane[])Target!.Planes.Clone();
        _actionNormals.Clear();
        _actionRing.Clear();

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

        if (!CSGBrushGeometry.Raycast(Target.Planes, origin, direction, out float t, out int plane)) return false;

        polygon = Array.IndexOf(Target.FaceToPlane, plane);
        if (polygon < 0 || polygon >= _mesh!.Polygons.Count) return false;

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

        // Cursor travel in world units at the brush's distance.
        float pixels = Float2.Distance(handles.MousePosition, _dragOrigin);
        float amount = pixels * WorldPerPixel(ctx);

        switch (_action)
        {
            case Action.Chamfer: PreviewChamfer(ctx, amount); break;
            case Action.Extrude: PreviewExtrude(ctx, amount); break;
            case Action.Marquee: DrawMarquee(ctx); break;
        }

        if (!handles.PrimaryUp) return;

        switch (_action)
        {
            case Action.Extrude: CommitExtrude(ctx, amount); break;
            case Action.Marquee: ApplyMarquee(ctx); break;
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
        if (amount <= 1e-4f || !CSGMeshEdit.ChamferPlane(_actionNormals, _actionCorner, amount, out Plane cut)) return;

        Plane[] array = [.. _startPlanes!, cut];
        if (!GeometryGenerator.ConvexFromPlanes(array).IsValid) return;

        Undo.RecordObject(Target!, "Chamfer CSG Brush");
        Target!.Planes = array;

        // The new face takes the look of the face nearest its direction
        Target.SetSurface(array.Length - 1, CSGSelection.NearestSurface(Target, cut.Normal, array.Length - 1));
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

            ctx.Draw.Line(Float4x4.TransformPoint(ring[i], toWorld), top[i], PickedColor, 2f);
        }

        ctx.Draw.Polyline(top, PickedColor, 2f, closed: true);
    }

    private bool TryExtrudeRing(out List<Float3> ring, out Float3 normal)
    {
        ring = _actionRing;
        normal = _actionCorner;
        return ring.Count >= 3;
    }

    /// <summary>Turns the pulled face into a sibling brush of its own.</summary>
    private void CommitExtrude(SceneToolContext ctx, float amount)
    {
        if (amount <= 1e-3f || !TryExtrudeRing(out List<Float3> ring, out Float3 normal)) return;

        Plane[]? prism = CSGMeshEdit.ExtrudePrism(ring, normal, amount);
        if (prism == null) return;

        GameObject go = CSGSelection.CreateObject(Target!.GameObject.Name, Target.GameObject.Parent);
        go.Transform.LocalPosition = Target.Transform.LocalPosition;
        go.Transform.LocalRotation = Target.Transform.LocalRotation;
        go.Transform.LocalScale = Target.Transform.LocalScale;

        CSGBrush extruded = go.AddComponent<CSGBrush>();
        extruded.Planes = prism;
        extruded.Operation = Target.Operation;
        extruded.Flags = Target.Flags;
        for (int i = 0; i < prism.Length; i++) extruded.SetSurface(i, _actionSurface);

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
        _startPlanes = null;
    }

    private void MoveSelection(SceneToolContext ctx)
    {
        if (_vertices.Count == 0) { _dragging = false; return; }

        HandleContext handles = ctx.Handles;
        Float4x4 toWorld = Target!.Transform.LocalToWorldMatrix;

        Float3 centre = Float3.Zero;
        foreach (int i in _vertices) centre += _mesh!.Points[i];
        centre = Float4x4.TransformPoint(centre / _vertices.Count, toWorld);

        Float3 before = centre;
        bool moved = TransformHandles.PositionHandle(handles, MoveHandle, ref centre, out bool hot);
        if (hot && !_dragging) _dragRaw = _dragApplied = Float3.Zero;
        _dragging = hot;

        if (!moved) return;

        // Snapped over the whole drag so small per frame steps do not round away.
        _dragRaw += centre - before;
        Float3 snapped = CSGSnap.Value(handles, _dragRaw);
        Float3 step = snapped - _dragApplied;
        _dragApplied = snapped;
        if (Float3.LengthSquared(step) < 1e-12f) return;

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
            mesh.MovePoints(moving, -delta);
            Target.ApplyControlMesh();
            _dragApplied -= step;
        }

        MarkDirty();
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
        if (_vertices.Count != 2) return;

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
            mesh.ScalePolygon(_faces[0], 1f / factor);
            Target.ApplyControlMesh();
        }
        MarkDirty();
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

        Undo.RecordObject(Target!, "Remove CSG Face");

        // Planes found first, then removed highest first, so removing one does not renumber the others
        var planes = new List<int>();
        foreach (int face in _faces)
            if (PlaneOf(face) is int plane and >= 0) planes.Add(plane);
        planes.Sort();

        for (int i = planes.Count - 1; i >= 0; i--)
        {
            if (CSGMeshEdit.RemoveFace(Target!.Planes, planes[i], out Plane[] fewer, out SurfaceData[] surfaces, Target.Surfaces))
            {
                Target.Planes = fewer;
                for (int s = 0; s < surfaces.Length; s++) Target.SetSurface(s, surfaces[s]);
            }
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

            ctx.Draw.Polygon(corners, FaceFill);
        }

        if (Filter.HasFlag(CSGElementFilter.Edge))
        {
            foreach ((int a, int b) in _edges)
            {
                bool picked = _vertices.Contains(a) && _vertices.Contains(b);
                ctx.Draw.Line(Float4x4.TransformPoint(_mesh!.Points[a], toWorld),
                              Float4x4.TransformPoint(_mesh.Points[b], toWorld),
                              picked ? PickedColor : IdleColor, picked ? 2.5f : 1f);
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
