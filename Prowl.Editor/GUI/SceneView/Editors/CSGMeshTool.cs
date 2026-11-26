// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Theming;
using Prowl.Editor.Modeling;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.CSG;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Editor.GUI.SceneView.Editors;

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
    public bool Active => CSGEditMode.Is(CSGMode.Edit) && CSGSelection.SingleBrush().IsValid();

    /// <summary>Which elements clicks pick and commands act on.</summary>
    public SelectMode Mode { get; private set; } = SelectMode.Vertex;

    /// <summary>Changes the mode, converting the selection to it.</summary>
    public void SetMode(SelectMode mode)
    {
        if (mode == Mode) return;
        if (_target != null) EditSelection(selection => Selecting.ToMode(selection, Mode, mode));
        Mode = mode;
    }

    /// <summary>The brush being edited, or null when the selection is not a single brush.</summary>
    public CSGBrush? Target { get; private set; }

    public int SelectedCount => _vertices.Count;

    public int SelectedFaceCount => _faces.Count;

    public int SelectedEdgeCount => _selectedEdges.Count;

    /// <summary>Whether a chamfer has something to cut: a selected edge, or a selected corner.</summary>
    public bool CanBevel => Active && _vertices.Count > 0;

    /// <summary>Whether one whole face is selected to pull out.</summary>
    public bool CanExtrude => Active && _faces.Count == 1;

    /// <summary>Whether any face is selected to inset.</summary>
    public bool CanInset => Active && _faces.Count > 0;

    /// <summary>Whether selected faces can be dropped, which only means something on a convex brush.</summary>
    public bool CanDeleteFaces => Active && _faces.Count > 0 && Target.IsValid() && Target!.ControlMesh.IsConvex();

    public bool CanSnap => Active && _vertices.Count > 0;

    /// <summary>Starts cutting every selected edge, or every selected corner when no whole edge is selected, by as far as the mouse moves.</summary>
    public void BevelSelected()
    {
        if (!CanBevel) return;
        BeginAction(Context.Handles, Action.Chamfer, fromMenu: true);
    }

    /// <summary>Starts a loop cut: the ring under the mouse is shown cut, the wheel sets how many, a click picks it and slides it.</summary>
    public void LoopCut()
    {
        if (!Active) return;
        BeginAction(Context.Handles, Action.LoopCut, fromMenu: true);
        _actionCount = 1;
        _loopEdge = null;
        _loopPicked = false;
    }

    /// <summary>Starts the knife: clicks place points along the cut, Backspace takes one back, Enter cuts.</summary>
    public void Knife()
    {
        if (!Active) return;
        BeginAction(Context.Handles, Action.Knife, fromMenu: true);
        _knife.Clear();
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

    public bool CanExtrudeRegion => Active && Mode == SelectMode.Face && _faces.Count > 0;

    /// <summary>Starts pulling the selected faces out as one region of the brush, by as far as the mouse moves along their normal.</summary>
    public void ExtrudeRegionSelected()
    {
        if (!CanExtrudeRegion) return;
        BeginAction(Context.Handles, Action.ExtrudeRegion, fromMenu: true);
    }

    public bool CanDissolve => Active && Mode switch
    {
        SelectMode.Vertex => _vertices.Count > 0,
        SelectMode.Edge => _selectedEdges.Count > 0,
        _ => _faces.Count > 0,
    };

    /// <summary>Removes the selected points, edges or faces and joins the faces around them.</summary>
    public void DissolveSelected()
    {
        if (!CanDissolve) return;
        SelectMode mode = Mode;
        RunEdit("Dissolve CSG Elements", selected => Operators.Dissolve(_target!.Target.Mesh, selected, mode, selected), SelectMode.Face);
    }

    /// <summary>Joins faces split in one plane, among the selection's edges or the whole brush when nothing is selected.</summary>
    public void LimitedDissolveSelected()
    {
        if (!Active) return;
        BeginAction(Context.Handles, Action.LimitedDissolve, fromMenu: true);
    }

    // The angle limited dissolve starts at, and how many degrees each pixel of mouse travel adds
    private const float LimitedDissolveDegrees = 5f;
    private const float DegreesPerPixel = 0.2f;
    private const float TrisToQuadsDegrees = 40f;
    private const float StrengthPerPixel = 0.005f;

    public bool CanMerge => Active && (Mode == SelectMode.Vertex && _vertices.Count >= 2 || Mode != SelectMode.Vertex && _selectedEdges.Count > 0);

    /// <summary>Joins the selected points into one at their centre.</summary>
    public void MergeAtCentre()
    {
        if (!CanMerge || _target == null) return;
        Float3 centre = Float3.Zero;
        foreach (int i in _vertices) centre += _mesh!.Points[i];
        centre /= _vertices.Count;
        RunEdit("Merge CSG Points", selected => Operators.Merge(_target.Target.Mesh, [.. selected.Vertices], centre, selected), SelectMode.Vertex);
    }

    /// <summary>Joins the selected points into one at the point last clicked.</summary>
    public void MergeAtActive()
    {
        if (!CanMerge || _target == null || _active is not { Kind: SelectMode.Vertex } active || !_vertices.Contains(active.A)) return;
        Float3 at = _mesh!.Points[active.A];
        RunEdit("Merge CSG Points", selected => Operators.Merge(_target.Target.Mesh, [.. selected.Vertices], at, selected), SelectMode.Vertex);
    }

    public bool CanMergeAtActive => CanMerge && _active is { Kind: SelectMode.Vertex } active && _vertices.Contains(active.A);

    public bool CanMergeAtFirst => CanMerge && FirstPicked() >= 0;

    /// <summary>Joins the selected points into one at the point clicked first.</summary>
    public void MergeAtFirst()
    {
        int first = FirstPicked();
        if (!CanMerge || _target == null || first < 0) return;
        Float3 at = _mesh!.Points[first];
        RunEdit("Merge CSG Points", selected => Operators.Merge(_target.Target.Mesh, [.. selected.Vertices], at, selected), SelectMode.Vertex);
    }

    /// <summary>The selected point clicked earliest, or -1 when no selected point was clicked.</summary>
    private int FirstPicked()
    {
        foreach (int point in _pickOrder)
            if (_vertices.Contains(point)) return point;
        return -1;
    }

    /// <summary>Collapses each joined group of selected edges to a point at its centre.</summary>
    public void CollapseSelected()
    {
        if (!Active || _selectedEdges.Count == 0) return;
        RunEdit("Collapse CSG Edges", selected => Operators.Collapse(_target!.Target.Mesh, [.. selected.Edges], selected), SelectMode.Vertex);
    }

    public bool CanConnect => Active && Mode == SelectMode.Vertex && _vertices.Count >= 2;

    /// <summary>Joins the selected points that share a face with new edges.</summary>
    public void ConnectSelected()
    {
        if (!CanConnect) return;
        Float3 view = Context.Camera.GameObject.Transform.Forward;
        RunEdit("Connect CSG Points", selected => Operators.Connect(_target!.Target.Mesh, [.. selected.Vertices], view, selected), SelectMode.Edge);
    }

    public bool CanSubdivide => Active && _selectedEdges.Count > 0;

    /// <summary>Adds points along the selected edges, or in Face mode splits the selected faces, the wheel setting how many.</summary>
    public void SubdivideSelected()
    {
        if (!CanSubdivide) return;
        BeginAction(Context.Handles, Action.Subdivide, fromMenu: true);
        if (Mode != SelectMode.Face) _actionFaces.Clear();
        _actionCount = 1;
    }

    // Cutting a four sided face makes a grid of the cuts squared, so the wheel stops sooner than for edges
    private const int MaxFaceCuts = 16;

    public bool CanTriangulate => Active && Mode == SelectMode.Face && _faces.Count > 0;

    /// <summary>Splits the selected faces into triangles.</summary>
    public void TriangulateSelected()
    {
        if (!CanTriangulate) return;
        RunEdit("Triangulate CSG Faces", selected => Operators.TriangulateFaces(_target!.Target.Mesh, [.. selected.Faces], selected), SelectMode.Face);
    }

    /// <summary>Starts joining triangles into quads, among the selected faces or all, the angle following the mouse.</summary>
    public void TrisToQuadsSelected()
    {
        if (!Active) return;
        BeginAction(Context.Handles, Action.TrisToQuads, fromMenu: true);
    }

    public bool CanBridge => Active && Mode == SelectMode.Face && _faces.Count == 2;

    /// <summary>Starts removing the two selected faces and joining their outlines with a strip, the wheel setting its segments.</summary>
    public void BridgeSelected()
    {
        if (!CanBridge) return;
        BeginAction(Context.Handles, Action.Bridge, fromMenu: true);
        _actionCount = 1;
    }

    /// <summary>
    /// Makes the brush the same on both sides of a plane across one of its axes, keeping one side. The plane goes through
    /// the scene view's pivot: the middle of the selection, the element last clicked, or the brush's origin.
    /// </summary>
    public void Symmetrize(int axis, bool keepPositive)
    {
        if (!Active) return;
        float offset = SymmetrizePlane()[axis];
        RunEdit("Symmetrize CSG Brush", selected =>
        {
            Operators.Symmetrize(_target!.Target.Mesh, axis, keepPositive, offset);
            selected.Clear();
        });
    }

    /// <summary>Where the symmetrize plane passes, in the brush's own space.</summary>
    private Float3 SymmetrizePlane()
    {
        if (SceneTools.Pivot == PivotMode.Active && ActiveCentre() is { } active)
            return Float4x4.TransformPoint(active, Target!.Transform.WorldToLocalMatrix);
        if (SceneTools.Pivot == PivotMode.Cursor)
            return Float4x4.TransformPoint(SceneTools.Cursor, Target!.Transform.WorldToLocalMatrix);
        if (SceneTools.Pivot == PivotMode.Center && _vertices.Count > 0)
        {
            Float3 sum = Float3.Zero;
            foreach (int i in _vertices) sum += _mesh!.Points[i];
            return sum / _vertices.Count;
        }
        return Float3.Zero;
    }

    /// <summary>The axes and sides to symmetrize by, as a menu at the mouse.</summary>
    private void ShowSymmetrizeMenu(HandleContext handles)
    {
        if (!Active) return;
        ContextMenu.Show(handles.MousePosition.X, handles.MousePosition.Y, menu =>
        {
            for (int axis = 0; axis < 3; axis++)
            {
                int chosen = axis;
                string name = axis == 0 ? "X" : axis == 1 ? "Y" : "Z";
                menu.Item(Loc.Get("csg.symmetrize_side", new { from = "+" + name, to = "-" + name }), () => Symmetrize(chosen, true));
                menu.Item(Loc.Get("csg.symmetrize_side", new { from = "-" + name, to = "+" + name }), () => Symmetrize(chosen, false));
            }
        });
    }

    /// <summary>Starts joining points near each other, among the selected or all, the distance following the mouse.</summary>
    public void MergeByDistanceSelected()
    {
        if (!Active) return;
        BeginAction(Context.Handles, Action.MergeByDistance, fromMenu: true);
    }

    public bool CanSmooth => Active && Mode == SelectMode.Vertex && _vertices.Count > 0;

    /// <summary>Starts smoothing the selected points, the strength following the mouse.</summary>
    public void SmoothSelected()
    {
        if (!CanSmooth) return;
        BeginAction(Context.Handles, Action.Smooth, fromMenu: true);
    }

    /// <summary>Starts evening out the selected points along the surface, the strength following the mouse.</summary>
    public void RelaxSelected()
    {
        if (!CanSmooth) return;
        BeginAction(Context.Handles, Action.Relax, fromMenu: true);
    }

    public bool CanSeparate => Active && Mode == SelectMode.Face && _faces.Count > 0;

    /// <summary>Splits the selected faces off as a brush of their own, closing both, and selects it.</summary>
    public void SeparateSelected()
    {
        if (!CanSeparate || Target.IsNotValid()) return;
        CSGBrush brush = Target!;
        if (!CSGBrushTarget.Separate(brush.ControlMesh, brush.Surfaces, _faces, out var kept, out var separated))
        {
            CSGStatus.Flash(Loc.Get("csg.status_separate"));
            return;
        }

        Undo.RecordObject(brush, "Separate CSG Faces");
        brush.SetShape(kept.Mesh, kept.Surfaces);

        GameObject go = CSGSelection.CreateObject(brush.GameObject.Name, brush.GameObject.Parent);
        go.Transform.LocalPosition = brush.Transform.LocalPosition;
        go.Transform.LocalRotation = brush.Transform.LocalRotation;
        go.Transform.LocalScale = brush.Transform.LocalScale;
        CSGBrush split = go.AddComponent<CSGBrush>();
        split.SetShape(separated.Mesh, separated.Surfaces);
        split.Operation = brush.Operation;
        split.Flags = brush.Flags;
        CSGSelection.PlaceAfter(go, brush.GameObject);
        Undo.RegisterCreatedObjectUndo(go, "Separate CSG Faces");

        ClearSelection();
        Selection.Select(go);
        MarkDirty();
    }

    public bool CanFlatten => Active && _vertices.Count >= 3;

    /// <summary>Moves the selected points onto one plane.</summary>
    public void FlattenSelected()
    {
        if (!CanFlatten) return;
        RunEdit("Flatten CSG Points", selected => Operators.Flatten(_target!.Target.Mesh, selected));
    }

    /// <summary>The ways to merge, as a menu at the mouse.</summary>
    private void ShowMergeMenu(HandleContext handles)
    {
        if (!CanMerge) return;
        ContextMenu.Show(handles.MousePosition.X, handles.MousePosition.Y, menu =>
        {
            if (Mode == SelectMode.Vertex)
            {
                menu.Item(Loc.Get("csg.merge_centre"), MergeAtCentre);
                if (CanMergeAtFirst) menu.Item(Loc.Get("csg.merge_first"), MergeAtFirst);
                if (CanMergeAtActive) menu.Item(Loc.Get("csg.merge_active"), MergeAtActive);
            }
            if (_selectedEdges.Count > 0) menu.Item(Loc.Get("csg.collapse"), CollapseSelected);
        });
    }

    /// <summary>The start shape with every selected edge, or else every selected corner, cut off by a distance.</summary>
    private (CSGControlMesh Mesh, SurfaceData[] Surfaces)? ChamferSelection(float distance)
    {
        CSGControlMesh start = _startMesh!;
        var cuts = new List<Plane>();

        foreach ((int a, int b) in _actionEdges)
        {
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

    public void SnapSelectedToGrid() => SnapSelection();

    /// <summary>Moves the brush's pivot to the middle of the selected points, its shape staying where it is.</summary>
    public void PivotToSelection()
    {
        if (!Active || _vertices.Count == 0) return;
        Float3 sum = Float3.Zero;
        foreach (int i in _vertices) sum += _mesh!.Points[i];
        MovePivot(sum / _vertices.Count);
    }

    /// <summary>Moves the brush's pivot to the 3D cursor, its shape staying where it is.</summary>
    public void PivotToCursor()
    {
        if (Active) MovePivot(Float4x4.TransformPoint(SceneTools.Cursor, Target!.Transform.WorldToLocalMatrix));
    }

    private void MovePivot(Float3 local)
    {
        CSGBrush brush = Target!;
        CSGSelection.Record("Move CSG Pivot", [brush.GameObject, .. brush.GameObject.Children]);
        CSGSelection.MovePivot(brush, local);
        Reload();
        MarkDirty();
    }

    public void DeleteSelectedFaces() => DeleteFaces();

    /// <summary>One face is selected, so the bracket keys taper it instead of changing the grid.</summary>
    public bool TapersFace => Active && _faces.Count == 1;

    // The selection by index: points, edges as point pairs with the lower first, and polygons. All three are kept in
    // step through Selecting, led by the mode's own set
    private readonly HashSet<int> _vertices = new();
    private readonly HashSet<(int A, int B)> _selectedEdges = new();
    private readonly List<int> _faces = new();

    /// <summary>What a drag in progress is doing.</summary>
    private enum Action { None, Marquee, Chamfer, Extrude, Inset, ExtrudeRegion, Subdivide, LimitedDissolve, TrisToQuads, MergeByDistance, Smooth, Relax, Bridge, LoopCut, EdgeSlide, Knife, Drag }

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
    private Float3 _gizmoStart;
    private Float3 _dragRaw, _dragApplied;

    // The selected points in the world where a drag began, which a snapped move lands on the grid by their bounds
    private List<Float3> _dragPoints = new();

    private List<Float3> SelectedWorld()
    {
        Float4x4 toWorld = Target!.Transform.LocalToWorldMatrix;
        var points = new List<Float3>(_vertices.Count);
        foreach (int i in _vertices) points.Add(Float4x4.TransformPoint(_mesh!.Points[i], toWorld));
        return points;
    }
    private Float3 _dragAnchor, _dragAnchorStart, _dragStartHit;
    private bool _dragStarted;
    private bool _passedThreshold;

    // Hidden parts of the brush, for this session only, by index as the selection is
    private readonly HashSet<int> _hiddenPoints = new();
    private readonly HashSet<(int A, int B)> _hiddenEdges = new();
    private readonly HashSet<int> _hiddenFaces = new();
    private readonly Func<int, bool> _isHiddenFace;

    // The last press, for telling a double click
    private long _lastPress;
    private Float2 _lastPressAt;
    private const long DoubleClickMs = 350;

    /// <summary>Draws and picks through the brush, with no occlusion test.</summary>
    public bool XRay { get; set; }

    /// <summary>Moved points snap to the corners, edges and faces of the model's brushes under the mouse.</summary>
    public bool SnapToBrushes { get; set; }

    /// <summary>Unselected points near the selection move with it, less the further they are.</summary>
    public bool Proportional { get; set; }

    /// <summary>How far proportional editing reaches, in world units. The wheel changes it while a transform runs.</summary>
    public float ProportionalRadius { get; set; } = 1f;

    public CSGMeshTool() => _isHiddenFace = _hiddenFaces.Contains;

    // Points being moved, by a modal transform, a drag or the gizmo: the session, the brush it runs on, the shape it
    // started from and the move to run again when it is committed
    private TransformSession? _session;
    private CSGBrush? _sessionBrush;
    private object? _actionUndo;
    private CSGControlMesh? _sessionStart;
    private SurfaceData[]? _sessionSurfaces;
    private System.Action? _sessionMove;

    // A modal grab, rotate or scale and the scene view it runs in
    private TransformModal? _transform;
    private object? _transformView;

    // The element last clicked, for the active element pivot: a point, an edge's points or a polygon
    private (SelectMode Kind, int A, int B)? _active;

    // An element pressed while already selected, so the press drags the whole selection and a plain click narrows to it
    private int _clicked = -1;

    private Action _action;
    private Float2 _dragOrigin;
    private readonly SlowPointer _pointer = new();

    // An action started from the menu follows the mouse with no button held, and the next click commits it. It runs
    // modal, holding the viewport it started in until it ends
    private bool _fromMenu;
    private HandleContext? _modalHandles;
    private readonly HashSet<int> _actionPoints = new();
    private readonly List<(int A, int B)> _actionEdges = new();
    private readonly List<int> _actionFaces = new();
    private readonly HashSet<int> _actionAfterPoints = new();
    private readonly List<int> _actionAfterFaces = new();
    private SelectMode _actionAfterLead;
    private int _actionCount;
    private (float Amount, int Count)? _previewKey;
    private bool _previewFailed;

    // How a bevel cuts, chosen once when it starts: Vector's bevel where it can, else planes cut across the corners
    private enum BevelKind { Planes, Edges, Points }
    private BevelKind _bevelKind;

    // A loop cut's edge in the start shape's numbering, whether a click has picked it, and where the slide starts
    private (int A, int B)? _loopEdge;
    private bool _loopPicked;
    private List<(int a, int b)> _startEdges = new();

    // An edge slide's +1 direction on screen
    private Float2 _slideDirection;

    // The knife's points, where each was clicked and what of the brush it sits on
    private readonly List<(Float2 Pixel, KnifePoint Point)> _knife = new();

    // Points in the order they were clicked, for merging at the first
    private readonly List<int> _pickOrder = new();

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
        ForgetVisibility();

        HandleContext handles = ctx.Handles;

        // A modal transform owns every key and click until it ends
        if (_transform != null)
        {
            if (ReferenceEquals(ctx.View, _transformView)) TickTransform(ctx);
            DrawElements(ctx);
            return;
        }

        if (handles.KeyDown(KeyCode.Escape))
        {
            if (_action != Action.None) { CancelAction(); return; }
            if (_vertices.Count == 0) CSGEditMode.Current = CSGMode.Select;
            ClearSelection();
            return;
        }

        // Delete always belongs to the mesh tool here, so it never falls through to deleting the brush. While an action
        // runs the keys are only taken, the action reads them itself, as the knife does Backspace
        if (_action != Action.None)
        {
            handles.KeyDown(KeyCode.Delete);
            handles.KeyDown(KeyCode.Backspace);
        }
        else
        {
            if (handles.IsPressed("MeshEdit/DeleteFace")) DeleteFaces();
            else if (handles.KeyDown(KeyCode.Delete) || handles.KeyDown(KeyCode.Backspace))
            {
                if (Mode == SelectMode.Face && _faces.Count > 0) DeleteFaces();
                else DissolveSelected();
            }
            if (handles.IsPressed("CSG/SnapToGrid")) SnapSelection();
            if (handles.IsPressed("MeshEdit/VertexMode")) SetMode(SelectMode.Vertex);
            if (handles.IsPressed("MeshEdit/EdgeMode")) SetMode(SelectMode.Edge);
            if (handles.IsPressed("MeshEdit/FaceMode")) SetMode(SelectMode.Face);
            SelectionKeys(ctx);
        }

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
        CSGBrush? brush = CSGSelection.SingleBrush();

        if (!ReferenceEquals(brush, Target))
        {
            Target = brush;
            ClearSelection();
            ClearHidden();
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
        int points = _mesh.Points.Count;
        _vertices.RemoveWhere(i => i >= points);
        _selectedEdges.RemoveWhere(e => e.B >= points);
        _faces.RemoveAll(f => f >= _mesh.Polygons.Count);
        return _mesh.Points.Count > 0;
    }

    /// <summary>Loads the brush's points as the edit target, after the tool or anything else changed them.</summary>
    private void Reload()
    {
        // Points renumbered by an edit or an undo no longer name what was clicked
        if (_mesh == null || Target!.ControlMesh.Points.Count != _mesh.Points.Count) _pickOrder.Clear();
        _mesh = Target!.ControlMesh;
        _meshSource = _mesh;
        _target = new CSGBrushTarget(_mesh, Target.Surfaces, Target.Transform.LocalToWorldMatrix);
        _edges = _mesh.Edges();
    }

    private void HoldSelection()
    {
        if (HeldMatchesSelection()) return;
        _held.Points = [.. _vertices];
        _held.Faces = [.. _faces];
        _held.Edges = new int[_selectedEdges.Count * 2];
        int i = 0;
        foreach ((int a, int b) in _selectedEdges)
        {
            _held.Edges[i++] = a;
            _held.Edges[i++] = b;
        }
    }

    private bool HeldMatchesSelection()
    {
        if (_held.Points.Length != _vertices.Count || _held.Faces.Length != _faces.Count || _held.Edges.Length != _selectedEdges.Count * 2) return false;
        foreach (int point in _held.Points)
            if (!_vertices.Contains(point)) return false;
        for (int i = 0; i < _faces.Count; i++)
            if (_held.Faces[i] != _faces[i]) return false;
        for (int i = 0; i + 1 < _held.Edges.Length; i += 2)
            if (!_selectedEdges.Contains((_held.Edges[i], _held.Edges[i + 1]))) return false;
        return true;
    }

    private void RestoreHeldSelection()
    {
        ClearSelection();
        _vertices.UnionWith(_held.Points);
        _faces.AddRange(_held.Faces);
        for (int i = 0; i + 1 < _held.Edges.Length; i += 2) _selectedEdges.Add(EdgeKey(_held.Edges[i], _held.Edges[i + 1]));
    }

    private static (int A, int B) EdgeKey(int a, int b) => a < b ? (a, b) : (b, a);

    /// <summary>The selection as elements of the edit target.</summary>
    private ElementSelection SelectedElements() => Elements(_vertices, _selectedEdges, _faces);

    private ElementSelection HiddenElements() => Elements(_hiddenPoints, _hiddenEdges, _hiddenFaces);

    private Func<int, bool>? HiddenSkip => _hiddenFaces.Count > 0 ? _isHiddenFace : null;

    private ElementSelection Elements(IEnumerable<int> points, IEnumerable<(int A, int B)> edges, IEnumerable<int> polygons)
    {
        var elements = new ElementSelection();
        GeometryData mesh = _target!.Target.Mesh;
        foreach (int i in points)
            if (i >= 0 && i < mesh.Vertices.Count) elements.Vertices.Add(mesh.Vertices[i]);
        foreach ((int a, int b) in edges)
            if (b < mesh.Vertices.Count && mesh.FindEdge(mesh.Vertices[a], mesh.Vertices[b]) is { } edge) elements.Edges.Add(edge);
        foreach (GeometryData.Face face in _target.FacesOf(polygons)) elements.Faces.Add(face);
        return elements;
    }

    /// <summary>Takes a selection of the edit target's elements back as indices.</summary>
    private void TakeSelection(ElementSelection selection)
    {
        ClearSelection();
        TakeIndices(selection, _vertices, _selectedEdges, _faces);
    }

    private static void TakeIndices(ElementSelection elements, HashSet<int> points, HashSet<(int A, int B)> edges, ICollection<int> polygons)
    {
        foreach (GeometryData.Vertex vertex in elements.Vertices) points.Add(vertex.Index);
        foreach (GeometryData.Edge edge in elements.Edges) edges.Add(EdgeKey(edge.Vert1.Index, edge.Vert2.Index));
        foreach (GeometryData.Face face in elements.Faces)
        {
            int polygon = CSGBrushTarget.PolygonOf(face);
            if (polygon >= 0 && !polygons.Contains(polygon)) polygons.Add(polygon);
        }
    }

    /// <summary>Runs a selection command over the brush, leaving hidden parts out.</summary>
    private void Command(Action<GeometryData, ElementSelection> command)
    {
        if (_target == null) return;
        GeometryData mesh = _target.Target.Mesh;
        ElementSelection hidden = HiddenElements();
        EditSelection(selection =>
        {
            command(mesh, selection);
            Selecting.Without(selection, hidden, Mode);
        });
    }

    private void SelectionKeys(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;

        // The scene view's tool keys stand down, Edit has no use for the transform gizmo's modes
        handles.IsPressed("Scene/ToolTranslate");
        handles.IsPressed("Scene/ToolRotate");

        if (handles.IsPressed("MeshEdit/Extrude")) ExtrudeRegionSelected();
        else if (handles.IsPressed("MeshEdit/Inset")) InsetSelected();
        else if (handles.IsPressed("MeshEdit/Bevel")) BevelSelected();
        else if (handles.IsPressed("MeshEdit/LoopCut")) LoopCut();
        else if (handles.IsPressed("MeshEdit/Knife")) Knife();
        else if (handles.IsPressed("MeshEdit/Triangulate")) TriangulateSelected();
        else if (handles.IsPressed("MeshEdit/TrisToQuads")) TrisToQuadsSelected();
        else if (handles.IsPressed("MeshEdit/Bridge")) BridgeSelected();
        else if (handles.IsPressed("MeshEdit/Symmetrize")) ShowSymmetrizeMenu(handles);
        else if (handles.IsPressed("MeshEdit/MergeByDistance")) MergeByDistanceSelected();
        else if (handles.IsPressed("MeshEdit/Separate")) SeparateSelected();
        else if (handles.IsPressed("MeshEdit/Smooth")) SmoothSelected();
        else if (handles.IsPressed("MeshEdit/LimitedDissolve")) LimitedDissolveSelected();
        else if (handles.IsPressed("MeshEdit/Dissolve")) DissolveSelected();
        else if (handles.IsPressed("MeshEdit/Merge")) ShowMergeMenu(handles);
        else if (handles.IsPressed("MeshEdit/Connect")) ConnectSelected();
        else if (handles.IsPressed("MeshEdit/Flatten")) FlattenSelected();
        else if (handles.IsPressed("MeshEdit/Grab")) StartTransform(ctx, TransformKind.Translate);
        else if (handles.IsPressed("MeshEdit/Rotate")) StartTransform(ctx, TransformKind.Rotate);
        else if (handles.IsPressed("MeshEdit/Scale")) StartTransform(ctx, TransformKind.Scale);
        else if (handles.IsPressed("MeshEdit/SelectAll")) Command((mesh, s) => Selecting.All(mesh, s, Mode));
        else if (handles.IsPressed("MeshEdit/SelectNone")) ClearSelection();
        else if (handles.IsPressed("MeshEdit/Invert")) Command((mesh, s) => Selecting.Invert(mesh, s, Mode));
        else if (handles.IsPressed("MeshEdit/More")) Command((_, s) => Selecting.More(s, Mode));
        else if (handles.IsPressed("MeshEdit/Less")) Command((mesh, s) => Selecting.Less(mesh, s, Mode));
        else if (handles.IsPressed("MeshEdit/Linked")) SelectLinked(ctx);
        else if (handles.IsPressed("MeshEdit/Similar")) Command((mesh, s) => Selecting.Similar(mesh, s, Mode));
        else if (handles.IsPressed("MeshEdit/XRay")) XRay = !XRay;
        else if (handles.IsPressed("MeshEdit/SnapToBrushes")) SnapToBrushes = !SnapToBrushes;
        else if (handles.IsPressed("MeshEdit/Proportional")) Proportional = !Proportional;
        else if (handles.IsPressed("MeshEdit/HideUnselected")) Hide(unselected: true);
        else if (handles.IsPressed("MeshEdit/Reveal")) ClearHidden();
        else if (handles.IsPressed("MeshEdit/Hide")) Hide(unselected: false);
        else if (handles.IsPressed("MeshEdit/Frame")) FrameSelected(ctx);
    }

    /// <summary>Adds the whole shell under the cursor to the selection, or grows the selection's own shells when the cursor is off the brush.</summary>
    private void SelectLinked(SceneToolContext ctx)
    {
        if (_target == null) return;
        if (!FaceUnderCursor(ctx.Handles, out int polygon, out _))
        {
            Command((_, s) => Selecting.Linked(s, Mode));
            return;
        }

        List<GeometryData.Face> seed = _target.FacesOf([polygon]);
        Command((_, s) =>
        {
            var shell = new ElementSelection();
            foreach (GeometryData.Face face in seed) shell.Vertices.UnionWith(face.NeighborVertices());
            Selecting.Linked(shell, SelectMode.Vertex);
            s.Vertices.UnionWith(shell.Vertices);
            s.Edges.UnionWith(shell.Edges);
            s.Faces.UnionWith(shell.Faces);
        });
    }

    /// <summary>
    /// What a moved point snaps to with the mouse at a screen point: a corner, a point of an edge or a face of a brush in
    /// the model, the brush being edited included. The selected points, their edges and faces, and hidden parts are
    /// never targets. Brushes whose bounds are not near the mouse on screen are passed over.
    /// </summary>
    private Float3? FindSnap(HandleContext handles, Float2 mouse)
    {
        if (CSGSelection.ActiveModel() is not { } model || model.IsNotValid() || Target.IsNotValid()) return null;

        var corners = new List<Float3>();
        var edges = new List<(Float3, Float3)>();
        Float3? face = null;
        float nearestHit = float.MaxValue;
        Ray ray = handles.MouseRay;

        foreach (CSGBrush brush in CSGSelection.BrushesIn(model))
        {
            if (CSGVisibility.IsHidden(brush)) continue;
            CSGControlMesh mesh = brush.ControlMesh;
            if (mesh.Points.Count == 0) continue;
            Float4x4 toWorld = brush.Transform.LocalToWorldMatrix;
            if (!NearOnScreen(handles, mesh, toWorld, mouse)) continue;

            bool edited = ReferenceEquals(brush, Target);
            for (int i = 0; i < mesh.Points.Count; i++)
                if (!edited || !_vertices.Contains(i) && !_hiddenPoints.Contains(i)) corners.Add(Float4x4.TransformPoint(mesh.Points[i], toWorld));
            foreach ((int a, int b) in mesh.Edges())
            {
                if (edited && (_vertices.Contains(a) || _vertices.Contains(b) || _hiddenEdges.Contains(EdgeKey(a, b)))) continue;
                edges.Add((Float4x4.TransformPoint(mesh.Points[a], toWorld), Float4x4.TransformPoint(mesh.Points[b], toWorld)));
            }

            Float4x4 toLocal = brush.Transform.WorldToLocalMatrix;
            Func<int, bool>? skip = edited ? p => _hiddenFaces.Contains(p) || TouchesSelection(mesh, p) : null;
            if (mesh.Raycast(Float4x4.TransformPoint(ray.Origin, toLocal), Float4x4.TransformNormal(ray.Direction, toLocal), out float t, out _, skip: skip))
            {
                Float3 hit = Float4x4.TransformPoint(Float4x4.TransformPoint(ray.Origin, toLocal) + Float4x4.TransformNormal(ray.Direction, toLocal) * t, toWorld);
                float distance = Float3.Distance(hit, ray.Origin);
                if (distance < nearestHit)
                {
                    nearestHit = distance;
                    face = hit;
                }
            }
        }

        return Snapping.Find(corners, edges, face, handles.WorldToScreen, mouse, out Float3 point) ? point : null;
    }

    private bool TouchesSelection(CSGControlMesh mesh, int polygon)
    {
        foreach (int i in mesh.PolygonPoints(polygon))
            if (_vertices.Contains(i)) return true;
        return false;
    }

    /// <summary>Whether a brush's bounds, on screen, come within snapping reach of the mouse.</summary>
    private static bool NearOnScreen(HandleContext handles, CSGControlMesh mesh, Float4x4 toWorld, Float2 mouse)
    {
        Float3 min = new(float.MaxValue), max = new(float.MinValue);
        foreach (Float3 point in mesh.Points)
        {
            min = Maths.Min(min, point);
            max = Maths.Max(max, point);
        }

        Float2 low = new(float.MaxValue), high = new(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            Float3 corner = new((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
            if (handles.WorldToScreen(Float4x4.TransformPoint(corner, toWorld)) is not { } at) return true;
            low = Maths.Min(low, at);
            high = Maths.Max(high, at);
        }
        float reach = Snapping.CornerReach;
        return mouse.X >= low.X - reach && mouse.X <= high.X + reach && mouse.Y >= low.Y - reach && mouse.Y <= high.Y + reach;
    }

    /// <summary>Hides the selected parts, or every other part, while editing. The shape is untouched.</summary>
    private void Hide(bool unselected)
    {
        if (_target == null) return;
        ElementSelection hidden = HiddenElements();
        ElementSelection selection = SelectedElements();
        ElementSelection kept = SelectedElements();
        if (unselected)
        {
            Selecting.Invert(_target.Target.Mesh, selection, Mode);
            Selecting.Without(selection, hidden, Mode);
        }
        Selecting.Hide(hidden, selection, Mode);

        ClearHidden();
        TakeIndices(hidden, _hiddenPoints, _hiddenEdges, _hiddenFaces);
        if (unselected) TakeSelection(kept);
        else ClearSelection();
    }

    private void ClearHidden()
    {
        _hiddenPoints.Clear();
        _hiddenEdges.Clear();
        _hiddenFaces.Clear();
    }

    /// <summary>After an edit renumbers the brush, the hidden points and faces carried through it, and the edges they hide.</summary>
    private void TakeHidden(HashSet<int> points, List<int> faces)
    {
        ClearHidden();
        _hiddenPoints.UnionWith(points);
        _hiddenFaces.UnionWith(faces);
        if (_target == null || points.Count + faces.Count == 0) return;
        foreach (GeometryData.Edge edge in _target.Target.Mesh.Edges)
        {
            bool allFaces = true;
            foreach (GeometryData.Face face in edge.NeighborFaces())
                if (!_hiddenFaces.Contains(CSGBrushTarget.PolygonOf(face))) { allFaces = false; break; }
            if (allFaces || _hiddenPoints.Contains(edge.Vert1.Index) || _hiddenPoints.Contains(edge.Vert2.Index))
                _hiddenEdges.Add(EdgeKey(edge.Vert1.Index, edge.Vert2.Index));
        }
    }

    /// <summary>Frames the selected points, or the shown part of the brush when nothing is selected.</summary>
    private void FrameSelected(SceneToolContext ctx)
    {
        if (ctx.View is not SceneViewPanel { EditorCamera: { } camera } || _mesh == null) return;
        Float4x4 toWorld = Target!.Transform.LocalToWorldMatrix;
        Float3 min = new(float.MaxValue), max = new(float.MinValue);
        bool any = false;
        for (int i = 0; i < _mesh.Points.Count; i++)
        {
            if (_vertices.Count > 0 ? !_vertices.Contains(i) : _hiddenPoints.Contains(i)) continue;
            Float3 p = Float4x4.TransformPoint(_mesh.Points[i], toWorld);
            min = Maths.Min(min, p);
            max = Maths.Max(max, p);
            any = true;
        }
        if (any) camera.Frame((min + max) * 0.5f, MathF.Max(0.25f, Float3.Length(max - min) * 0.5f));
    }

    /// <summary>
    /// A double click selects the loop through the edge under the cursor, Ctrl makes it the ring, Shift adds to the
    /// selection. The edges are taken into the current mode.
    /// </summary>
    private bool SelectLoop(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;
        int index = NearestEdge(handles, PickRadius * 1.5f);
        if (index < 0 || _target == null) return false;

        GeometryData mesh = _target.Target.Mesh;
        (int a, int b) = _edges[index];
        if (mesh.FindEdge(mesh.Vertices[a], mesh.Vertices[b]) is not { } edge) return false;

        GeometryData.Face? side = null;
        if (FaceUnderCursor(handles, out int polygon, out _))
            foreach (GeometryData.Face face in _target.FacesOf([polygon]))
                if (face.NeighborEdges().Contains(edge)) side = face;

        List<GeometryData.Edge> edges = handles.Ctrl ? GeometryOperators.EdgeRingAcross(edge) : GeometryOperators.EdgeLoopOrOutline(edge, side);
        ElementSelection hidden = HiddenElements();
        ElementSelection current = SelectedElements();
        EditSelection(selection =>
        {
            selection.Clear();
            selection.Edges.UnionWith(edges);
            Selecting.ToMode(selection, SelectMode.Edge, Mode);
            if (handles.Shift)
            {
                selection.Vertices.UnionWith(current.Vertices);
                selection.Edges.UnionWith(current.Edges);
                selection.Faces.UnionWith(current.Faces);
            }
            Selecting.Without(selection, hidden, Mode);
        });
        return true;
    }

    /// <summary>
    /// The seen edge nearest the cursor on screen within <paramref name="reach"/> pixels, or -1. Only edges in reach are
    /// tested for sight, at the point on them nearest the cursor.
    /// </summary>
    private int NearestEdge(HandleContext handles, float reach)
    {
        Float4x4 toWorld = Target!.Transform.LocalToWorldMatrix;
        Float2 mouse = handles.MousePosition;
        _near.Clear();
        for (int i = 0; i < _edges.Count; i++)
        {
            (int a, int b) = _edges[i];
            if (_hiddenEdges.Contains(EdgeKey(a, b))) continue;
            if (handles.WorldToScreen(Float4x4.TransformPoint(_mesh!.Points[a], toWorld)) is not { } from
                || handles.WorldToScreen(Float4x4.TransformPoint(_mesh.Points[b], toWorld)) is not { } to) continue;

            Float2 along = to - from;
            float lengthSq = Float2.Dot(along, along);
            float t = lengthSq > 1e-6f ? Maths.Clamp(Float2.Dot(mouse - from, along) / lengthSq, 0f, 1f) : 0f;
            float distance = Float2.Distance(mouse, from + along * t);
            if (distance < reach) _near.Add((i, distance, t));
        }
        _near.Sort((x, y) => x.Distance.CompareTo(y.Distance));
        foreach ((int i, _, float t) in _near)
        {
            (int a, int b) = _edges[i];
            if (Seen(handles, _mesh!.Points[a] + (_mesh.Points[b] - _mesh.Points[a]) * t)) return i;
        }
        return -1;
    }

    /// <summary>Changes the selection through the edit target's elements, then keeps the three sets in step with the mode.</summary>
    private void EditSelection(Action<ElementSelection> change)
    {
        if (_target == null) return;
        ElementSelection selection = SelectedElements();
        change(selection);
        TakeSelection(selection);
    }

    /// <summary>Fills the other two sets in from the one the mode leads with.</summary>
    private void Normalize() => EditSelection(selection => Selecting.Normalize(selection, Mode));

    /// <summary>
    /// Runs an edit on the selected elements through the edit target: recorded for undo with the selection, refused with
    /// a reason when the brush could not keep it, and written back to the brush when it can.
    /// </summary>
    /// <param name="selects">For an operator that chooses what is selected after, which set it fills.</param>
    private bool RunEdit(string name, Action<ElementSelection> edit, SelectMode? selects = null)
    {
        if (Target.IsNotValid() || _target == null) return false;

        ElementSelection selected = SelectedElements();
        ElementSelection hidden = HiddenElements();
        Undo.RecordObject(Target!, name);
        HoldSelection();
        Undo.RecordObject(_held, UndoOwner.Of(Target!), name);

        string? problem = MeshEdit.Run(_target.Target, (_, _) => edit(selected), new ElementSelection());
        if (problem != null)
        {
            CSGStatus.Flash(Loc.Get(problem));
            return false;
        }

        if (selects is { } lead) Selecting.Normalize(selected, lead);
        (CSGControlMesh mesh, SurfaceData[] surfaces) = _target.ToBrush(selected, out HashSet<int> points, out List<int> faces);
        _target.ToBrush(hidden, out HashSet<int> hiddenPoints, out List<int> hiddenFaces);
        Target!.SetShape(mesh, surfaces);
        ClearSelection();
        _vertices.UnionWith(points);
        _faces.AddRange(faces);
        Reload();
        TakeHidden(hiddenPoints, hiddenFaces);
        SelectMode carried = (selects ?? Mode) == SelectMode.Face ? SelectMode.Face : SelectMode.Vertex;
        EditSelection(selection => Selecting.ToMode(selection, carried, Mode));
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
        CancelTransform();
        CancelSession();
        if (_action == Action.None) return;
        // Exactly the shape it started with, so the open undo record finds nothing changed and leaves no step
        if (_action is Action.Chamfer or Action.Inset or Action.ExtrudeRegion or Action.Subdivide or Action.LimitedDissolve
            or Action.TrisToQuads or Action.MergeByDistance or Action.Smooth or Action.Relax or Action.Bridge or Action.LoopCut
            or Action.EdgeSlide or Action.Drag
            && _actionBrush.IsValid() && _startMesh != null)
            ApplyShape(_actionBrush!, _startMesh.Clone(), (SurfaceData[])_startSurfaces!.Clone());
        EndAction();
    }

    private void ClearSelection()
    {
        _vertices.Clear();
        _selectedEdges.Clear();
        _faces.Clear();
    }

    /// <summary>Whether the camera sees a point of the brush, rather than one of the brush's own faces in front of it.</summary>
    private bool Seen(HandleContext handles, Float3 point, CSGControlMesh? mesh = null)
    {
        Camera camera = handles.Camera;
        Transform view = camera.GameObject.Transform;
        Float3 eye = camera.IsOrthographic
            ? Float4x4.TransformPoint(point, Target!.Transform.LocalToWorldMatrix) - view.Forward * 10000f
            : view.Position;

        Float3 origin = Float4x4.TransformPoint(eye, Target!.Transform.WorldToLocalMatrix);
        Float3 toPoint = point - origin;
        float length = Float3.Length(toPoint);

        if (XRay || length < 1e-6f) return true;

        CSGControlMesh against = (mesh ?? _mesh)!;
        if (!ReferenceEquals(against, _fittedOf))
        {
            _fittedOf = against;
            _fitted = against.FitPlanes();
        }
        return !against.Raycast(origin, toPoint / length, out float t, out _, bothSides: true, HiddenSkip, _fitted)
            || t >= length - MathF.Max(1e-3f, length * 1e-4f);
    }

    // Visibility found this frame, for the view being handled: the planes rays are cast against and each point's answer
    private CSGControlMesh? _fittedOf;
    private Plane?[]? _fitted;
    private sbyte[] _pointSeen = [];

    private void ForgetVisibility()
    {
        _fittedOf = null;
        _fitted = null;
        Array.Clear(_pointSeen);
    }

    private bool PointSeen(HandleContext handles, int point)
    {
        if (_pointSeen.Length != _mesh!.Points.Count) _pointSeen = new sbyte[_mesh.Points.Count];
        if (_pointSeen[point] == 0) _pointSeen[point] = Seen(handles, _mesh.Points[point]) ? (sbyte)1 : (sbyte)-1;
        return _pointSeen[point] > 0;
    }

    /// <summary>The seen point nearest the cursor on screen within <paramref name="reach"/> pixels, or -1. Only points in reach are tested for sight.</summary>
    private int NearestPoint(HandleContext handles, float reach)
    {
        Float4x4 toWorld = Target!.Transform.LocalToWorldMatrix;
        Float2 mouse = handles.MousePosition;
        _near.Clear();
        for (int i = 0; i < _mesh!.Points.Count; i++)
        {
            if (_hiddenPoints.Contains(i) || handles.WorldToScreen(Float4x4.TransformPoint(_mesh.Points[i], toWorld)) is not { } at) continue;
            float distance = Float2.Distance(mouse, at);
            if (distance < reach) _near.Add((i, distance, 0f));
        }
        _near.Sort((x, y) => x.Distance.CompareTo(y.Distance));
        foreach ((int i, _, _) in _near)
            if (PointSeen(handles, i)) return i;
        return -1;
    }

    private readonly List<(int Index, float Distance, float T)> _near = new();

    private void PickElements(SceneToolContext ctx)
    {
        if (_dragging || _action != Action.None) return;

        HandleContext handles = ctx.Handles;
        Float4x4 toWorld = Target!.Transform.LocalToWorldMatrix;

        bool doubleClick = false;
        if (handles.PrimaryDown && handles.ViewportHovered)
        {
            long now = Environment.TickCount64;
            doubleClick = now - _lastPress < DoubleClickMs && Float2.Distance(handles.MousePosition, _lastPressAt) < DragThreshold;
            _lastPress = doubleClick ? 0 : now;
            _lastPressAt = handles.MousePosition;
        }

        // A double click on an edge takes the press for a loop, so nothing else starts from it
        bool Pressed(ControlID id)
        {
            if (!handles.IsNearest(id) || !handles.TryBeginDrag(id)) return false;
            if (!doubleClick || !SelectLoop(ctx)) return true;
            handles.TryEndDrag(id);
            return false;
        }

        if (Mode == SelectMode.Face && TryPickFace(ctx, out int face, out ControlID faceId))
        {
            if (Pressed(faceId))
            {
                SelectFace(handles, face);
                return;
            }
        }

        // Only the nearest seen element in reach is offered to the handles, so nothing else is tested for sight
        if (Mode == SelectMode.Edge && NearestEdge(handles, PickRadius) is var edge and >= 0)
        {
            Float3 from = Float4x4.TransformPoint(_mesh!.Points[_edges[edge].a], toWorld);
            Float3 to = Float4x4.TransformPoint(_mesh.Points[_edges[edge].b], toWorld);

            ControlID id = handles.GetControlID(EdgeControl, edge);
            handles.AddControl(id, from, to, PickRadius, 0f);

            if (Pressed(id))
            {
                if (handles.Shift || handles.Ctrl)
                {
                    SelectEdge(handles, edge);
                    handles.TryEndDrag(id);
                    return;
                }

                bool keep = _selectedEdges.Contains(EdgeKey(_edges[edge].a, _edges[edge].b));
                if (!keep) SelectEdge(handles, edge);
                BeginAction(handles, Action.Drag, edge: edge);
                if (keep) _clicked = edge;
                return;
            }
        }

        if (Mode == SelectMode.Vertex && NearestPoint(handles, PickRadius) is var point and >= 0)
        {
            ControlID id = handles.GetControlID(VertexControl, point);
            handles.AddControl(id, Float4x4.TransformPoint(_mesh!.Points[point], toWorld), PickRadius);

            if (Pressed(id))
            {
                if (handles.Shift || handles.Ctrl)
                {
                    SelectVertex(handles, point);
                    handles.TryEndDrag(id);
                    return;
                }

                bool keep = _vertices.Contains(point);
                if (!keep) SelectVertex(handles, point);
                BeginAction(handles, Action.Drag, point: point);
                if (keep) _clicked = point;
                return;
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
                Selection.Ping(other.GameObject.Identifier);
                return;
            }
        }

        // Nothing under the cursor, so a press starts a marquee.
        ControlID marquee = handles.GetControlID(MarqueeControl);
        // Behind everything else, but ahead of scene picking, so a miss never deselects the brush
        handles.AddControl(marquee, HandleContext.BodyDistance, float.MaxValue - 1f);

        if (Pressed(marquee))
            BeginAction(handles, Action.Marquee);
    }

    /// <summary>Moves the selected points with the cursor, across the plane through them that faces the camera.</summary>
    private void DragPoints(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;
        Float3 normal = ctx.Camera.GameObject.Transform.Forward;
        Ray ray = handles.RayThrough(_pointer.Position);

        float denom = Float3.Dot(ray.Direction, normal);
        if (MathF.Abs(denom) < 1e-6f) return;
        Float3 hit = ray.Origin + ray.Direction * (Float3.Dot(_dragAnchor - ray.Origin, normal) / denom);

        if (!_dragStarted)
        {
            _dragStarted = true;
            _dragStartHit = hit;
            _dragAnchorStart = _dragAnchor;
            _dragPoints = SelectedWorld();
        }

        // The selection lands on the grid, so off grid points are pulled onto it, or the pressed point on what it snaps to
        Float3 target = _dragAnchorStart + CSGSnap.Move(handles, _dragPoints, hit - _dragStartHit, Quaternion.Identity);
        if (SnapToBrushes && FindSnap(handles, handles.MousePosition) is { } snapped)
        {
            target = snapped;
            ctx.Draw.Dot(snapped, new Color32(255, 220, 90, 255), 9f);
        }
        if (Float3.LengthSquared(target - _dragAnchor) < 1e-12f) return;
        if ((_session ?? StartSession()) is not { } session) return;

        Float3 delta = target - _dragAnchorStart;
        _dragAnchor = target;
        PreviewSession(() => session.Translate(delta));
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
        if (fromMenu || action == Action.Chamfer)
        {
            handles.Modal = true;
            _modalHandles = handles;
        }
        _actionPoints.Clear();
        _actionEdges.Clear();
        _actionFaces.Clear();
        _previewKey = null;
        if (fromMenu || action == Action.Chamfer)
        {
            _actionPoints.UnionWith(_vertices);
            _actionEdges.AddRange(_selectedEdges);
            _actionFaces.AddRange(_faces);
        }
        _actionView = Context.View;
        _dragOrigin = handles.MousePosition;
        _pointer.Reset(_dragOrigin);
        _dragStarted = false;
        _passedThreshold = false;
        _clicked = -1;
        _actionBrush = Target;
        _pendingLast = null;
        _startSurfaces = (SurfaceData[])Target!.Surfaces.Clone();
        _startMesh = Target.ControlMesh.Clone();
        _startEdges = _startMesh.Edges();
        if (action == Action.Chamfer)
        {
            _bevelKind = ChooseBevel();
            _actionCount = 1;
        }
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
        id = ControlID.None;
        if (!FaceUnderCursor(ctx.Handles, out polygon, out float t)) return false;

        id = ctx.Handles.GetControlID(FaceControl, polygon);
        ctx.Handles.AddControl(id, HandleContext.BodyDistance, t);
        return true;
    }

    private bool FaceUnderCursor(HandleContext handles, out int polygon, out float distance)
    {
        Float4x4 toLocal = Target!.Transform.WorldToLocalMatrix;
        Float3 origin = Float4x4.TransformPoint(handles.MouseRay.Origin, toLocal);
        Float3 direction = Float4x4.TransformNormal(handles.MouseRay.Direction, toLocal);
        return _mesh!.Raycast(origin, direction, out distance, out polygon, bothSides: true, HiddenSkip);
    }

    private void SelectVertex(HandleContext handles, int index)
    {
        if (!handles.Shift && !handles.Ctrl) _pickOrder.Clear();
        _pickOrder.Remove(index);
        Pick(handles, _vertices, index);
        if (_vertices.Contains(index)) _pickOrder.Add(index);
        _active = (SelectMode.Vertex, index, index);
    }

    private void SelectEdge(HandleContext handles, int index)
    {
        (int a, int b) = EdgeKey(_edges[index].a, _edges[index].b);
        Pick(handles, _selectedEdges, (a, b));
        _active = (SelectMode.Edge, a, b);
    }

    private void SelectFace(HandleContext handles, int polygon)
    {
        Pick(handles, _faces, polygon);
        _active = (SelectMode.Face, polygon, polygon);
    }

    /// <summary>The centre of the element last clicked, in the world, while it is still selected.</summary>
    private Float3? ActiveCentre()
    {
        if (_active is not { } active || _mesh == null) return null;
        Float3 centre;
        switch (active.Kind)
        {
            case SelectMode.Vertex when _vertices.Contains(active.A): centre = _mesh.Points[active.A]; break;
            case SelectMode.Edge when _selectedEdges.Contains((active.A, active.B)): centre = (_mesh.Points[active.A] + _mesh.Points[active.B]) * 0.5f; break;
            case SelectMode.Face when _faces.Contains(active.A): centre = FaceCentre(active.A); break;
            default: return null;
        }
        return Float4x4.TransformPoint(centre, Target!.Transform.LocalToWorldMatrix);
    }

    /// <summary>A frame whose Z axis is the selection's average normal, in the world, or null when it has none.</summary>
    private Quaternion? NormalFrame()
    {
        Float3 sum = Float3.Zero;
        if (_faces.Count > 0)
            foreach (int polygon in _faces) sum += _mesh!.PolygonNormal(polygon);
        else if (_selectedEdges.Count > 0)
            foreach ((int a, int b) in _selectedEdges)
                foreach (int polygon in _mesh!.PolygonsAtEdge(a, b)) sum += _mesh.PolygonNormal(polygon);
        else
            foreach (int point in _vertices)
                foreach (int polygon in _mesh!.PolygonsAtPoint(point)) sum += _mesh.PolygonNormal(polygon);

        Float3 normal = Float4x4.TransformNormal(sum, Target!.Transform.LocalToWorldMatrix);
        if (Float3.LengthSquared(normal) < 1e-10f) return null;
        normal = Float3.Normalize(normal);
        return Quaternion.LookRotation(normal, MathF.Abs(normal.Y) < 0.99f ? Float3.UnitY : Float3.UnitX);
    }

    /// <summary>Starts moving the selected points through a session, shown on the brush until it is committed or cancelled.</summary>
    private TransformSession? StartSession()
    {
        if (_target == null || _vertices.Count == 0 || Target.IsNotValid()) return null;

        _session = new TransformSession(_target.Target, SelectedElements(), Proportional ? ProportionalRadius : 0f)
        {
            Individual = SceneTools.Pivot == PivotMode.Individual,
        };
        if (SceneTools.Pivot == PivotMode.Active && ActiveCentre() is { } active) _session.Pivot = active;
        if (SceneTools.Pivot == PivotMode.Cursor) _session.Pivot = SceneTools.Cursor;
        _sessionBrush = Target;
        _sessionStart = Target!.ControlMesh.Clone();
        _sessionSurfaces = (SurfaceData[])Target.Surfaces.Clone();
        _sessionMove = null;
        return _session;
    }

    /// <summary>Runs a move on the session's points and shows it, without checking or recording anything.</summary>
    private void PreviewSession(System.Action move)
    {
        move();
        _sessionMove = move;

        CSGControlMesh preview = _sessionStart!.Clone();
        var vertices = _target!.Target.Mesh.Vertices;
        for (int i = 0; i < vertices.Count && i < preview.Points.Count; i++) preview.SetPoint(i, vertices[i].Point);
        ShowShape(preview);
    }

    /// <summary>
    /// Puts the brush back where the session started and runs the last move once from there, so it is one undo step
    /// and the brush's rules judge the whole move. With no move made the session just ends.
    /// </summary>
    private void CommitSession(string name)
    {
        if (_session == null) return;
        System.Action? move = _sessionMove;
        if (move == null)
        {
            CancelSession();
            return;
        }

        ShowShape(_sessionStart!.Clone());
        _session.Restore();
        _session = null;
        _sessionMove = null;
        RunEdit(name, _ => move());
    }

    /// <summary>Puts the brush back where the session started.</summary>
    private void CancelSession()
    {
        if (_session == null) return;
        ShowShape(_sessionStart!.Clone());
        _session.Restore();
        _session = null;
        _sessionMove = null;
        if (_target != null && ReferenceEquals(_sessionBrush, Target)) Reload();
    }

    /// <summary>Shows a shape on the moved brush without loading it as the edit target again.</summary>
    private void ShowShape(CSGControlMesh mesh)
    {
        if (_sessionBrush.IsNotValid()) return;
        _sessionBrush!.SetShape(mesh, (SurfaceData[])_sessionSurfaces!.Clone());
        if (ReferenceEquals(_sessionBrush, Target))
        {
            _mesh = _sessionBrush.ControlMesh;
            _meshSource = _mesh;
        }
        MarkDirty();
    }

    /// <summary>Starts a modal grab, rotate or scale of the selected points, about the scene view's pivot.</summary>
    private void StartTransform(SceneToolContext ctx, TransformKind kind)
    {
        if (StartSession() is not { } session) return;
        _transform = new TransformModal(kind, session, Target!.Transform.Rotation, NormalFrame())
        {
            Snap = SnapToBrushes ? mouse => FindSnap(ctx.Handles, mouse) : null,
        };
        _transformView = ctx.View;
        ctx.Handles.Modal = true;
        _modalHandles = ctx.Handles;
    }

    private void TickTransform(SceneToolContext ctx)
    {
        TransformModal modal = _transform!;

        // The wheel changes how far proportional editing reaches, shown as a circle round the pivot
        if (Proportional && _session != null)
        {
            float wheel = Input.MouseWheelDelta;
            if (wheel != 0f)
            {
                ProportionalRadius = Math.Clamp(ProportionalRadius * MathF.Pow(1.15f, wheel), 0.01f, 1000f);
                _session.Falloff = ProportionalRadius;
            }
            CSGGizmos.Circle(_session.Pivot, ctx.Camera.GameObject.Transform.Forward, ProportionalRadius, new Color32(255, 255, 255, 150), 1f);
        }

        // G again while grabbing a single point slides it along an edge instead
        if (modal.Kind == TransformKind.Translate && ctx.Handles.GetKeyDown(KeyCode.G))
        {
            if (StartSlide()) return;
            if (Mode == SelectMode.Edge && _selectedEdges.Count > 0)
            {
                EndTransform();
                CancelSession();
                StartEdgeSlide(ctx.Handles);
                return;
            }
        }

        ModalState state = modal.Tick(ctx);
        ctx.Draw.Label(_session!.Pivot, modal.Status, PickedColor);

        if (state == ModalState.Running)
        {
            PreviewSession(modal.Replay);
            return;
        }

        EndTransform();
        if (state == ModalState.Cancelled)
        {
            CancelSession();
            return;
        }
        CommitSession(modal.Kind switch
        {
            TransformKind.Translate => "Move CSG Points",
            TransformKind.Rotate => "Rotate CSG Points",
            TransformKind.Slide => "Slide CSG Point",
            _ => "Scale CSG Points",
        });
    }

    /// <summary>Turns a grab of one point into a slide along one of its edges, from where the point started.</summary>
    private bool StartSlide()
    {
        if (Mode != SelectMode.Vertex || _vertices.Count != 1 || _session == null || _sessionStart == null) return false;

        int point = 0;
        foreach (int i in _vertices) point = i;
        Float4x4 toWorld = Target!.Transform.LocalToWorldMatrix;
        var ends = new List<Float3>();
        foreach ((int a, int b) in _edges)
        {
            if (a != point && b != point || _hiddenEdges.Contains(EdgeKey(a, b))) continue;
            ends.Add(Float4x4.TransformPoint(_sessionStart.Points[a == point ? b : a], toWorld));
        }
        if (ends.Count == 0) return false;

        _session.Restore();
        ShowShape(_sessionStart.Clone());
        _sessionMove = null;
        _transform = new TransformModal(TransformKind.Slide, _session, Target.Transform.Rotation, null) { SlideEnds = ends };
        return true;
    }

    /// <summary>Puts the brush back as the transform found it and ends it.</summary>
    private void CancelTransform()
    {
        if (_transform == null) return;
        EndTransform();
        CancelSession();
    }

    private void EndTransform()
    {
        if (_modalHandles != null)
        {
            _modalHandles.Modal = false;
            _modalHandles = null;
        }
        _transform = null;
        _transformView = null;
    }

    /// <summary>Shift toggles, Ctrl removes, a plain click replaces.</summary>
    private void Pick<T>(HandleContext handles, ICollection<T> set, T item)
    {
        if (!handles.Shift && !handles.Ctrl) ClearSelection();
        if (handles.Ctrl || handles.Shift && set.Contains(item)) set.Remove(item);
        else if (!set.Contains(item)) set.Add(item);
        Normalize();
    }

    /// <summary>Runs whichever gesture is in progress, previewing live and committing on release.</summary>
    private void TickAction(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;

        // Cursor travel in world units at the brush's distance, counted from the drag threshold. A menu action has no
        // press to tell apart from a click, so it counts from where the menu was used
        float pixels = Float2.Distance(_pointer.Follow(handles), _dragOrigin);
        _passedThreshold |= _fromMenu || pixels >= DragThreshold;
        bool moved = _passedThreshold;
        float amount = MathF.Max(0f, pixels - (_fromMenu ? 0f : DragThreshold)) * WorldPerPixel(ctx);

        // While a menu action runs the tool holds the viewport, so the click that commits it picks nothing else
        ControlID owner = handles.GetControlID(MenuActionControl);
        if (_fromMenu) handles.AddControl(owner, HandleContext.BodyDistance, float.MaxValue - 1f);

        switch (_action)
        {
            case Action.Chamfer when _bevelKind != BevelKind.Planes:
            {
                float wheel = Input.MouseWheelDelta;
                if (wheel != 0f) _actionCount = Math.Clamp(_actionCount + (wheel > 0f ? 1 : -1), 1, 32);
                float width = CSGSnap.Value(handles, amount);
                int segments = _actionCount;
                BevelKind kind = _bevelKind;
                List<(int A, int B)> edges = [.. _actionEdges];
                List<int> points = [.. _actionPoints];
                PreviewOperator("Bevel CSG Brush", width, segments, kind == BevelKind.Edges ? 32 : 1, width <= 1e-4f, (w, n) => (target, selected) =>
                {
                    if (kind == BevelKind.Edges) Operators.Bevel(target.Target.Mesh, EdgesOf(target, edges), w, n, selected);
                    else Operators.BevelPoints(target.Target.Mesh, PointsOf(target, points), w, selected);
                }, SelectMode.Face);
                string text = kind == BevelKind.Edges ? Loc.Get("csg.bevel_amount", new { width = width.ToString("0.###"), count = segments }) : width.ToString("0.###");
                ctx.Draw.Label(handles.MouseRay.Origin + handles.MouseRay.Direction * 5f, text, PickedColor);
                break;
            }
            case Action.Chamfer:
                if (moved)
                {
                    amount = CSGSnap.Value(handles, amount);
                    if (_fromMenu) PreviewChamferSelection(amount);
                    else PreviewChamfer(ctx, amount);
                    ctx.Draw.Label(_fromMenu ? handles.MouseRay.Origin + handles.MouseRay.Direction * 5f : Float4x4.TransformPoint(_actionCorner, _actionBrush!.Transform.LocalToWorldMatrix), amount.ToString("0.###"), PickedColor);
                }
                break;
            case Action.ExtrudeRegion:
            {
                float distance = CSGSnap.Value(handles, amount) * ExtrudeSign(handles);
                List<int> faces = [.. _actionFaces];
                PreviewOperator("Extrude CSG Faces", distance, 1, 1, MathF.Abs(distance) <= 1e-4f,
                    (d, _) => (target, selected) => Operators.ExtrudeRegion(target.Target.Mesh, target.FacesOf(faces), d, selected), SelectMode.Face);
                ctx.Draw.Label(handles.MouseRay.Origin + handles.MouseRay.Direction * 5f, distance.ToString("0.###"), PickedColor);
                break;
            }
            case Action.Inset:
            {
                float width = CSGSnap.Value(handles, amount);
                List<int> faces = [.. _actionFaces];
                PreviewOperator("Inset CSG Face", width, 1, 1, width <= 1e-4f,
                    (w, _) => (target, selected) => Operators.Inset(target.Target.Mesh, target.FacesOf(faces), w, selected), SelectMode.Face);
                ctx.Draw.Label(handles.MouseRay.Origin + handles.MouseRay.Direction * 5f, width.ToString("0.###"), PickedColor);
                break;
            }
            case Action.Subdivide:
            {
                float wheel = Input.MouseWheelDelta;
                if (wheel != 0f) _actionCount = Math.Clamp(_actionCount + (wheel > 0f ? 1 : -1), 1, 64);
                if (_actionFaces.Count > 0)
                {
                    _actionCount = Math.Min(_actionCount, MaxFaceCuts);
                    int faceCuts = _actionCount;
                    List<int> faces = [.. _actionFaces];
                    PreviewOperator("Subdivide CSG Faces", float.NaN, faceCuts, MaxFaceCuts, false,
                        (_, n) => (target, selected) => Operators.SubdivideFaces(target.Target.Mesh, target.FacesOf(faces), n, selected), SelectMode.Face);
                    ctx.Draw.Label(handles.MouseRay.Origin + handles.MouseRay.Direction * 5f, Loc.Get("csg.cuts", new { count = faceCuts }), PickedColor);
                    break;
                }
                int cuts = _actionCount;
                List<(int A, int B)> edges = [.. _actionEdges];
                PreviewOperator("Subdivide CSG Edges", float.NaN, cuts, 64, false,
                    (_, n) => (target, selected) => Operators.SubdivideEdges(target.Target.Mesh, EdgesOf(target, edges), n, selected), SelectMode.Vertex);
                ctx.Draw.Label(handles.MouseRay.Origin + handles.MouseRay.Direction * 5f, Loc.Get("csg.cuts", new { count = cuts }), PickedColor);
                break;
            }
            case Action.LimitedDissolve:
            {
                float degrees = Math.Clamp(LimitedDissolveDegrees + pixels * DegreesPerPixel, 0f, 90f);
                if (CSGSnap.Active(handles)) degrees = MathF.Round(degrees / 5f) * 5f;
                List<(int A, int B)>? scope = _actionEdges.Count > 0 ? [.. _actionEdges] : null;
                PreviewOperator("Limited Dissolve CSG Brush", degrees, 1, 1, false,
                    (d, _) => (target, selected) => Operators.LimitedDissolve(target.Target.Mesh, scope == null ? null : EdgesOf(target, scope), d, selected), SelectMode.Face);
                ctx.Draw.Label(handles.MouseRay.Origin + handles.MouseRay.Direction * 5f, Loc.Get("csg.degrees", new { degrees = degrees.ToString("0.#") }), PickedColor);
                break;
            }
            case Action.Extrude:
                amount = CSGSnap.Value(handles, amount);
                if (moved) PreviewExtrude(ctx, amount);
                break;
            case Action.Drag:
                if (moved) DragPoints(ctx);
                if (_dragStarted) DrawDistance(ctx, _dragAnchor, _dragAnchor - _dragAnchorStart);
                break;
            case Action.Marquee: DrawMarquee(ctx); break;
            case Action.TrisToQuads:
            {
                float degrees = Math.Clamp(TrisToQuadsDegrees + pixels * DegreesPerPixel, 0f, 180f);
                if (CSGSnap.Active(handles)) degrees = MathF.Round(degrees / 5f) * 5f;
                List<int>? faces = _actionFaces.Count == 0 ? null : [.. _actionFaces];
                PreviewOperator("Join CSG Triangles", degrees, 1, 1, false,
                    (d, _) => (target, selected) => Operators.TrisToQuads(target.Target.Mesh, faces == null ? null : target.FacesOf(faces), d, selected), SelectMode.Face);
                ctx.Draw.Label(handles.MouseRay.Origin + handles.MouseRay.Direction * 5f, Loc.Get("csg.degrees", new { degrees = degrees.ToString("0.#") }), PickedColor);
                break;
            }
            case Action.MergeByDistance:
            {
                float distance = CSGSnap.Value(handles, amount);
                List<int>? points = _actionPoints.Count == 0 ? null : [.. _actionPoints];
                PreviewOperator("Merge CSG Points By Distance", distance, 1, 1, distance <= 1e-5f,
                    (d, _) => (target, selected) => Operators.MergeByDistance(target.Target.Mesh, points == null ? null : PointsOf(target, points), d, selected), SelectMode.Vertex);
                ctx.Draw.Label(handles.MouseRay.Origin + handles.MouseRay.Direction * 5f, distance.ToString("0.###"), PickedColor);
                break;
            }
            case Action.Smooth or Action.Relax:
            {
                float strength = Math.Clamp(pixels * StrengthPerPixel, 0f, 1f);
                bool relax = _action == Action.Relax;
                List<int> points = [.. _actionPoints];
                PreviewOperator(relax ? "Relax CSG Points" : "Smooth CSG Points", strength, 1, 1, strength <= 1e-4f, (f, _) => (target, selected) =>
                {
                    if (relax) Operators.Relax(target.Target.Mesh, PointsOf(target, points), f, selected);
                    else Operators.Smooth(target.Target.Mesh, PointsOf(target, points), f, selected);
                }, SelectMode.Vertex);
                ctx.Draw.Label(handles.MouseRay.Origin + handles.MouseRay.Direction * 5f, strength.ToString("0.##"), PickedColor);
                break;
            }
            case Action.Bridge:
            {
                float wheel = Input.MouseWheelDelta;
                if (wheel != 0f) _actionCount = Math.Clamp(_actionCount + (wheel > 0f ? 1 : -1), 1, 64);
                int segments = _actionCount;
                List<int> faces = [.. _actionFaces];
                PreviewOperator("Bridge CSG Faces", float.NaN, segments, 64, false, (_, n) => (target, selected) =>
                {
                    List<GeometryData.Face> ends = target.FacesOf(faces);
                    if (ends.Count == 2) Operators.Bridge(target.Target.Mesh, ends[0], ends[1], n, selected);
                }, SelectMode.Face);
                ctx.Draw.Label(handles.MouseRay.Origin + handles.MouseRay.Direction * 5f, Loc.Get("csg.segments", new { count = segments }), PickedColor);
                break;
            }
            case Action.LoopCut:
                TickLoopCut(ctx, owner);
                return;
            case Action.Knife:
                TickKnife(ctx, owner);
                return;
            case Action.EdgeSlide:
            {
                float length = Float2.Dot(_slideDirection, _slideDirection);
                float factor = length > 1e-6f ? Math.Clamp(Float2.Dot(_pointer.Position - _dragOrigin, _slideDirection) / length, -1f, 1f) : 0f;
                List<(int A, int B)> slid = [.. _actionEdges];
                PreviewOperator("Slide CSG Edges", factor, 1, 1, MathF.Abs(factor) <= 1e-4f, (f, _) => (target, selected) =>
                {
                    List<GeometryData.Edge> edges = EdgesOf(target, slid);
                    Operators.SlideEdges(target.Target.Mesh, edges, f);
                    selected.Edges.UnionWith(edges);
                }, SelectMode.Edge);
                ctx.Draw.Label(handles.MouseRay.Origin + handles.MouseRay.Direction * 5f, factor.ToString("0.###"), PickedColor);
                break;
            }
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

        ReleaseActionUndo();
        switch (_action)
        {
            case Action.Extrude: CommitExtrude(amount); break;
            case Action.Marquee: ApplyMarquee(ctx); break;
            case Action.Chamfer when _bevelKind != BevelKind.Planes: TakeActionResult(); KeepLast(); break;
            case Action.Chamfer: ClearSelection(); break;
            case Action.EdgeSlide: TakeActionResult(); KeepLast(); break;
            case Action.Inset or Action.ExtrudeRegion or Action.Subdivide or Action.LimitedDissolve
                or Action.TrisToQuads or Action.MergeByDistance or Action.Smooth or Action.Relax or Action.Bridge: TakeActionResult(); KeepLast(); break;
            case Action.Drag: CommitSession("Move CSG Points"); break;
        }

        // A click on part of a selection, with no drag, narrows the selection to what was clicked
        if (_action == Action.Drag && !_passedThreshold && _clicked >= 0)
        {
            ClearSelection();
            if (Mode == SelectMode.Edge) _selectedEdges.Add(EdgeKey(_edges[_clicked].a, _edges[_clicked].b));
            else _vertices.Add(_clicked);
            Normalize();
        }

        EndAction();
    }

    /// <summary>
    /// The start shape with an operator run on it, or the start shape back again when <paramref name="unchanged"/>.
    /// Runs again only when <paramref name="key"/> changes. What the operator selects, filled into the set
    /// <paramref name="lead"/> names, is kept for when the action ends.
    /// </summary>
    private bool PreviewOperator(string name, (float Amount, int Count) key, bool unchanged, Action<CSGBrushTarget, ElementSelection> run, SelectMode lead)
    {
        if (key == _previewKey) return !_previewFailed;
        _previewKey = key;
        _previewFailed = false;

        if (!unchanged)
        {
            var target = new CSGBrushTarget(_startMesh!, _startSurfaces!);
            var after = new ElementSelection();
            string? problem = MeshEdit.Run(target.Target, (_, selected) => run(target, selected), after);
            if (problem != null)
            {
                CSGStatus.Flash(Loc.Get(problem));
                _previewFailed = true;
                return false;
            }

            Selecting.Normalize(after, lead);
            (CSGControlMesh mesh, SurfaceData[] surfaces) = target.ToBrush(after, out HashSet<int> points, out List<int> faces);
            if (after.IsEmpty) KeepActionResult(_actionPoints, _actionFaces, Mode);
            else KeepActionResult(points, faces, lead);
            HoldActionUndo(name);
            ApplyShape(Target!, mesh, surfaces);
            return true;
        }

        RestoreStart();
        KeepActionResult(_actionPoints, _actionFaces, Mode);
        return true;
    }

    /// <summary>
    /// Previews an operator made from an amount and a count, NaN for an operator with no amount, keeping how to make it
    /// so it can be run again with other settings once the action ends.
    /// </summary>
    private void PreviewOperator(string name, float amount, int count, int maxCount, bool unchanged,
                                 Func<float, int, Action<CSGBrushTarget, ElementSelection>> make, SelectMode lead)
    {
        if (PreviewOperator(name, (float.IsNaN(amount) ? 0f : amount, count), unchanged, make(amount, count), lead))
            _pendingLast = unchanged ? null : new LastOperator(name, amount, count, maxCount, make, lead, _startMesh!, _startSurfaces!, Target!, null);
    }

    // ================================================================
    //  Adjust last
    // ================================================================

    // How to run the last operator again from the shape before it, and the shape it left, so a later change ends it
    private sealed record LastOperator(string Name, float Amount, int Count, int MaxCount, Func<float, int, Action<CSGBrushTarget, ElementSelection>> Make,
                                       SelectMode Lead, CSGControlMesh Start, SurfaceData[] StartSurfaces, CSGBrush Brush, CSGControlMesh? Left, long Step = -1);
    private LastOperator? _pendingLast, _last;

    private void KeepLast()
    {
        _last = _pendingLast is { } pending && Target.IsValid() ? pending with { Left = Target!.ControlMesh, Step = TopStep() } : null;
        _pendingLast = null;
    }

    // The undo step on top once pending records are in, which a later edit of the brush in place replaces
    private static long TopStep()
    {
        Undo.FlushUndoRecordObjects();
        IReadOnlyList<UndoStepInfo> steps = Undo.UndoSteps;
        return steps.Count > 0 ? steps[^1].Id : -1;
    }

    /// <summary>The last operator run, while its brush still has the shape it left. A NaN amount means it has none.</summary>
    public (string Name, float Amount, int Count, int MaxCount)? Last
        => _last is { } last && LastStillHolds(last) ? (last.Name, last.Amount, last.Count, last.MaxCount) : null;

    private bool LastStillHolds(LastOperator last)
        => _action == Action.None && ReferenceEquals(last.Brush, Target) && last.Brush.IsValid() && ReferenceEquals(last.Brush.ControlMesh, last.Left)
           && Undo.UndoSteps is { Count: > 0 } steps && steps[^1].Id == last.Step;

    /// <summary>Runs the last operator again from the shape before it with a new amount and count, as one undo step.</summary>
    public void AdjustLast(float amount, int count)
    {
        if (_last is not { } last || !LastStillHolds(last)) return;
        count = Math.Clamp(count, 1, Math.Max(1, last.MaxCount));
        var target = new CSGBrushTarget(last.Start.Clone(), (SurfaceData[])last.StartSurfaces.Clone());
        var after = new ElementSelection();
        Action<CSGBrushTarget, ElementSelection> run = last.Make(amount, count);
        string? problem = MeshEdit.Run(target.Target, (_, selected) => run(target, selected), after);
        if (problem != null)
        {
            CSGStatus.Flash(Loc.Get(problem));
            return;
        }

        Selecting.Normalize(after, last.Lead);
        (CSGControlMesh mesh, SurfaceData[] surfaces) = target.ToBrush(after, out HashSet<int> points, out List<int> faces);
        Undo.RecordObject(last.Brush, last.Name);
        ApplyShape(last.Brush, mesh, surfaces);
        KeepActionResult(points, faces, last.Lead);
        TakeActionResult();
        _last = last with { Amount = amount, Count = count, Left = last.Brush.ControlMesh, Step = TopStep() };
    }

    // ================================================================
    //  Selection as numbers
    // ================================================================

    /// <summary>The middle of the selected points in the world, or null with nothing selected.</summary>
    public Float3? SelectionMedian
    {
        get
        {
            if (_vertices.Count == 0 || Target.IsNotValid() || _mesh == null) return null;
            Float3 centre = Float3.Zero;
            foreach (int i in _vertices) centre += _mesh.Points[i];
            return Float4x4.TransformPoint(centre / _vertices.Count, Target!.Transform.LocalToWorldMatrix);
        }
    }

    /// <summary>Moves the selected points so their middle lands on a point in the world, as one checked edit.</summary>
    public void MoveSelectionTo(Float3 world)
    {
        if (_action != Action.None || _transform != null || SelectionMedian is not { } median) return;
        Float3 delta = world - median;
        if (StartSession() is not { } session) return;
        _sessionMove = () => session.Translate(delta);
        CommitSession("Move CSG Points");
    }

    private void KeepActionResult(IEnumerable<int> points, IEnumerable<int> faces, SelectMode lead)
    {
        _actionAfterPoints.Clear();
        _actionAfterPoints.UnionWith(points);
        _actionAfterFaces.Clear();
        _actionAfterFaces.AddRange(faces);
        _actionAfterLead = lead;
    }

    /// <summary>Selects what the action's operator chose, in the current mode.</summary>
    private void TakeActionResult()
    {
        ClearSelection();
        _vertices.UnionWith(_actionAfterPoints);
        _faces.AddRange(_actionAfterFaces);
        SelectMode carried = _actionAfterLead == SelectMode.Face ? SelectMode.Face : SelectMode.Vertex;
        EditSelection(selection => Selecting.ToMode(selection, carried, Mode));
    }

    private static List<GeometryData.Vertex> PointsOf(CSGBrushTarget target, IEnumerable<int> points)
    {
        var vertices = target.Target.Mesh.Vertices;
        var result = new List<GeometryData.Vertex>();
        foreach (int i in points)
            if (i >= 0 && i < vertices.Count) result.Add(vertices[i]);
        return result;
    }

    /// <summary>
    /// Bevels with Vector's operator when a small bevel of the selection works, edges first, then corners. Where it
    /// cannot, the bevel cuts planes across the corners as the chamfer always has. Chosen once, so the amount keeps one
    /// meaning for the whole drag.
    /// </summary>
    private BevelKind ChooseBevel()
    {
        const float Probe = 1e-3f;
        var target = new CSGBrushTarget(_startMesh!, _startSurfaces!);
        if (_actionEdges.Count > 0)
        {
            List<GeometryData.Edge> edges = EdgesOf(target, _actionEdges);
            if (MeshEdit.Run(target.Target, (mesh, selected) => Operators.Bevel(mesh, edges, Probe, 1, selected), new ElementSelection()) == null)
                return BevelKind.Edges;
            return BevelKind.Planes;
        }

        List<GeometryData.Vertex> points = PointsOf(target, _actionPoints);
        if (points.Count > 0 && MeshEdit.Run(target.Target, (mesh, selected) => Operators.BevelPoints(mesh, points, Probe, selected), new ElementSelection()) == null)
            return BevelKind.Points;
        return BevelKind.Planes;
    }

    /// <summary>Turns a grab of edges into a slide of them along the edges beside them, its direction found by trying it.</summary>
    private void StartEdgeSlide(HandleContext handles)
    {
        BeginAction(handles, Action.EdgeSlide, fromMenu: true);
        _slideDirection = Float2.Zero;

        var probe = new CSGBrushTarget(_startMesh!, _startSurfaces!);
        List<GeometryData.Edge> edges = EdgesOf(probe, _actionEdges);
        if (edges.Count == 0) return;
        GeometryData.Vertex end = edges[0].Vert1;
        Float3 from = end.Point;
        Operators.SlideEdges(probe.Target.Mesh, edges, 1f);
        Float4x4 toWorld = Target!.Transform.LocalToWorldMatrix;
        if (handles.WorldToScreen(Float4x4.TransformPoint(from, toWorld)) is { } a && handles.WorldToScreen(Float4x4.TransformPoint(end.Point, toWorld)) is { } b)
            _slideDirection = b - a;
    }

    /// <summary>
    /// Before a click, shows the ring under the mouse cut and the wheel sets how many cuts. The click keeps the ring and
    /// the mouse then slides the cuts along it, until a second click keeps them.
    /// </summary>
    private void TickLoopCut(SceneToolContext ctx, ControlID owner)
    {
        HandleContext handles = ctx.Handles;
        float wheel = Input.MouseWheelDelta;
        if (wheel != 0f) _actionCount = Math.Clamp(_actionCount + (wheel > 0f ? 1 : -1), 1, 64);

        Float4x4 toWorld = _actionBrush!.Transform.LocalToWorldMatrix;
        if (!_loopPicked) _loopEdge = NearestStartEdge(handles, toWorld);

        _pointer.Follow(handles);
        float slide = 0f;
        if (_loopPicked && _loopEdge is { } picked
            && handles.WorldToScreen(Float4x4.TransformPoint(_startMesh!.Points[picked.A], toWorld)) is { } a
            && handles.WorldToScreen(Float4x4.TransformPoint(_startMesh.Points[picked.B], toWorld)) is { } b)
        {
            Float2 along = b - a;
            float length = Float2.Dot(along, along);
            slide = length > 1e-6f ? Math.Clamp(Float2.Dot(_pointer.Position - _dragOrigin, along) / length * 2f, -1f, 1f) : 0f;
        }

        if (_loopEdge is { } edge)
        {
            int cuts = _actionCount;
            float s = slide;
            PreviewOperator("Loop Cut CSG Brush", (slide, HashCode.Combine(edge.A, edge.B, cuts)), false, (target, selected) =>
            {
                if (EdgesOf(target, [edge]) is [GeometryData.Edge ring]) Operators.LoopCut(target.Target.Mesh, ring, cuts, s, selected);
            }, SelectMode.Edge);
        }
        else
        {
            RestoreStart();
            _previewKey = null;
        }
        ctx.Draw.Label(handles.MouseRay.Origin + handles.MouseRay.Direction * 5f, Loc.Get("csg.cuts", new { count = _actionCount }), PickedColor);

        if (handles.ViewportHovered && Input.GetMouseButtonDown(1))
        {
            CancelAction();
            return;
        }
        if (!handles.IsNearest(owner) || !handles.TryBeginDrag(owner)) return;
        handles.TryEndDrag(owner);

        if (!_loopPicked)
        {
            if (_loopEdge == null) return;
            _loopPicked = true;
            _dragOrigin = handles.MousePosition;
            _pointer.Reset(_dragOrigin);
            return;
        }
        TakeActionResult();
        EndAction();
    }

    /// <summary>The start shape's edge nearest the mouse on screen and in sight, as its two points, or null.</summary>
    private (int A, int B)? NearestStartEdge(HandleContext handles, Float4x4 toWorld)
    {
        (int A, int B)? best = null;
        float bestDistance = PickRadius * 2f;
        Float2 mouse = handles.MousePosition;
        foreach ((int a, int b) in _startEdges)
        {
            Float3 pa = _startMesh!.Points[a], pb = _startMesh.Points[b];
            if (handles.WorldToScreen(Float4x4.TransformPoint(pa, toWorld)) is not { } from
                || handles.WorldToScreen(Float4x4.TransformPoint(pb, toWorld)) is not { } to) continue;
            Float2 along = to - from;
            float lengthSq = Float2.Dot(along, along);
            float t = lengthSq > 1e-6f ? Math.Clamp(Float2.Dot(mouse - from, along) / lengthSq, 0f, 1f) : 0f;
            float distance = Float2.Distance(mouse, from + along * t);
            if (distance >= bestDistance || !Seen(handles, pa + (pb - pa) * t, _startMesh)) continue;
            bestDistance = distance;
            best = (a, b);
        }
        return best;
    }

    /// <summary>Places knife points on clicks and cuts on Enter. Backspace takes the last point back.</summary>
    private void TickKnife(SceneToolContext ctx, ControlID owner)
    {
        HandleContext handles = ctx.Handles;
        (Float2 pixel, KnifePoint landed) = Land(handles);

        Float3 Near(Float2 screen)
        {
            Ray ray = ctx.Camera.ScreenPointToRay(screen - (handles.MousePosition - handles.MouseLocal), handles.ViewportSize);
            return ray.Origin + ray.Direction * MathF.Max(ctx.Camera.NearClipPlane * 4f, 0.05f);
        }
        var colour = new Color32(255, 220, 90, 255);
        for (int i = 1; i < _knife.Count; i++) ctx.Draw.Line(Near(_knife[i - 1].Pixel), Near(_knife[i].Pixel), colour, 2f);
        if (_knife.Count > 0) ctx.Draw.Line(Near(_knife[^1].Pixel), Near(pixel), new Color32(255, 220, 90, 140), 1.5f);
        foreach ((Float2 placed, _) in _knife) ctx.Draw.Dot(Near(placed), colour, 7f);
        ctx.Draw.Dot(Near(pixel), landed.Vertex != null ? new Color32(120, 255, 140, 255) : landed.Edge != null ? new Color32(90, 200, 255, 255) : colour, 9f);
        ctx.Draw.Label(handles.MouseRay.Origin + handles.MouseRay.Direction * 5f, Loc.Get("csg.knife_status", new { count = _knife.Count }), PickedColor);

        if (handles.GetKeyDown(KeyCode.Backspace) && _knife.Count > 0) _knife.RemoveAt(_knife.Count - 1);
        if (handles.GetKeyDown(KeyCode.Enter) || handles.GetKeyDown(KeyCode.KeypadEnter))
        {
            CutWithKnife(ctx);
            EndAction();
            return;
        }
        if (handles.ViewportHovered && Input.GetMouseButtonDown(1))
        {
            CancelAction();
            return;
        }
        if (!handles.IsNearest(owner) || !handles.TryBeginDrag(owner)) return;
        handles.TryEndDrag(owner);
        _knife.Add((pixel, landed));
    }

    /// <summary>What the mouse is on: a point near it, else an edge near it, else the face under it, else nothing, and where that is on screen.</summary>
    private (Float2 Pixel, KnifePoint Point) Land(HandleContext handles)
    {
        var point = new KnifePoint();
        GeometryData mesh = _target!.Target.Mesh;
        Float4x4 toWorld = Target!.Transform.LocalToWorldMatrix, toLocal = Target.Transform.WorldToLocalMatrix;
        Float2 mouse = handles.MousePosition;

        int nearest = NearestPoint(handles, PickRadius);
        if (nearest >= 0 && nearest < mesh.Vertices.Count)
        {
            point.Vertex = mesh.Vertices[nearest];
            point.Point = _mesh.Points[nearest];
            return (handles.WorldToScreen(Float4x4.TransformPoint(point.Point, toWorld)) ?? mouse, point);
        }

        Ray sight = handles.MouseRay;
        int edgeIndex = NearestEdge(handles, PickRadius * 1.5f);
        if (edgeIndex >= 0)
        {
            (int a, int b) = _edges[edgeIndex];
            Float3 start = Float4x4.TransformPoint(_mesh.Points[a], toWorld), run = Float4x4.TransformPoint(_mesh.Points[b], toWorld) - start, toStart = start - sight.Origin;
            float runRun = Float3.Dot(run, run), runSight = Float3.Dot(run, sight.Direction), sightSight = Float3.Dot(sight.Direction, sight.Direction);
            float span = runRun * sightSight - runSight * runSight;
            float t = span > 1e-12f ? Math.Clamp((runSight * Float3.Dot(sight.Direction, toStart) - sightSight * Float3.Dot(run, toStart)) / span, 0f, 1f) : 0f;
            if (mesh.FindEdge(mesh.Vertices[a], mesh.Vertices[b]) is { } edge)
            {
                point.Edge = edge;
                point.Point = _mesh.Points[a] + (_mesh.Points[b] - _mesh.Points[a]) * t;
                return (handles.WorldToScreen(start + run * t) ?? mouse, point);
            }
        }

        if (FaceUnderCursor(handles, out int polygon, out float hit))
        {
            List<GeometryData.Face> faces = _target.FacesOf([polygon]);
            if (faces.Count > 0) point.Face = faces[0];
            Float3 origin = Float4x4.TransformPoint(sight.Origin, toLocal);
            point.Point = origin + Float4x4.TransformNormal(sight.Direction, toLocal) * hit;
        }
        return (mouse, point);
    }

    /// <summary>Cuts along the knife's points, each stretch in the plane of the lines of sight through its ends.</summary>
    private void CutWithKnife(SceneToolContext ctx)
    {
        if (_knife.Count < 2 || _target == null) return;
        HandleContext handles = ctx.Handles;
        Camera camera = ctx.Camera;
        Float4x4 toWorld = Target!.Transform.LocalToWorldMatrix, toLocal = Target.Transform.WorldToLocalMatrix;
        Float2 corner = handles.MousePosition - handles.MouseLocal;

        var planes = new List<(Float3 Point, Float3 Normal)>();
        for (int i = 1; i < _knife.Count; i++)
        {
            Ray sightA = camera.ScreenPointToRay(_knife[i - 1].Pixel - corner, handles.ViewportSize);
            Ray sightB = camera.ScreenPointToRay(_knife[i].Pixel - corner, handles.ViewportSize);
            Float3 origin = Float4x4.TransformPoint(sightA.Origin, toLocal);
            Float3 first = Float4x4.TransformPoint(sightA.Origin + sightA.Direction, toLocal);
            Float3 second = camera.IsOrthographic
                ? Float4x4.TransformPoint(sightB.Origin, toLocal)
                : Float4x4.TransformPoint(sightB.Origin + sightB.Direction, toLocal);
            planes.Add((origin, Float3.Cross(first - origin, second - origin)));
        }

        var pixels = new List<Float2>();
        var points = new List<KnifePoint>();
        foreach ((Float2 pixel, KnifePoint point) in _knife)
        {
            pixels.Add(pixel);
            points.Add(point);
        }

        float? Along(int stretch, Float3 local)
        {
            Float2 a = pixels[stretch], run = pixels[stretch + 1] - a;
            float lengthSq = Float2.Dot(run, run);
            if (lengthSq < 4f || handles.WorldToScreen(Float4x4.TransformPoint(local, toWorld)) is not { } at) return null;
            float t = Float2.Dot(at - a, run) / lengthSq;
            if (t < -0.002f || t > 1.002f || !Seen(handles, local)) return null;
            return Math.Clamp(t, 0f, 1f);
        }

        bool Cuts(GeometryData.Face face) => !_hiddenFaces.Contains(CSGBrushTarget.PolygonOf(face));
        RunEdit("Knife CSG Brush", selected => Operators.Knife(_target.Target.Mesh, points, planes, Along, Cuts, selected), SelectMode.Edge);
    }

    private static List<GeometryData.Edge> EdgesOf(CSGBrushTarget target, IEnumerable<(int A, int B)> pairs)
    {
        GeometryData mesh = target.Target.Mesh;
        var edges = new List<GeometryData.Edge>();
        foreach ((int a, int b) in pairs)
            if (b < mesh.Vertices.Count && mesh.FindEdge(mesh.Vertices[a], mesh.Vertices[b]) is { } edge) edges.Add(edge);
        return edges;
    }

    /// <summary>Which way along the faces' normal the mouse has moved on screen, out when the normal faces the camera.</summary>
    private float ExtrudeSign(HandleContext handles)
    {
        Float3 normal = Float3.Zero;
        foreach (int polygon in _actionFaces) normal += _startMesh!.PolygonNormal(polygon);
        Float4x4 toWorld = _actionBrush!.Transform.LocalToWorldMatrix;
        Float3 centre = Float3.Zero;
        int count = 0;
        foreach (int polygon in _actionFaces)
            foreach (int i in _startMesh!.PolygonPoints(polygon)) { centre += _startMesh.Points[i]; count++; }
        if (count == 0) return 1f;
        centre = Float4x4.TransformPoint(centre / count, toWorld);
        Float3 tip = centre + Float4x4.TransformNormal(normal, toWorld);

        if (handles.WorldToScreen(centre) is not { } from || handles.WorldToScreen(tip) is not { } to) return 1f;
        Float2 along = to - from;
        if (Float2.LengthSquared(along) < 16f) return 1f;
        return Float2.Dot(_pointer.Position - _dragOrigin, along) < 0f ? -1f : 1f;
    }

    private void PreviewChamferSelection(float amount)
    {
        if (amount <= 1e-4f || ChamferSelection(amount) is not { } chamfered)
        {
            RestoreStart();
            return;
        }

        HoldActionUndo("Chamfer CSG Brush");
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

        HoldActionUndo("Chamfer CSG Brush");
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

    /// <summary>Selects what the mode picks inside the rectangle: points, edges with both ends in, faces with their centre in.</summary>
    private void ApplyMarquee(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;
        if (!handles.Shift && !handles.Ctrl) ClearSelection();

        Float2 min = Maths.Min(_dragOrigin, handles.MousePosition);
        Float2 max = Maths.Max(_dragOrigin, handles.MousePosition);
        Float4x4 toWorld = Target!.Transform.LocalToWorldMatrix;

        bool InRect(Float3 point)
            => handles.WorldToScreen(Float4x4.TransformPoint(point, toWorld)) is { } p && p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y;

        bool PointInside(int point) => InRect(_mesh!.Points[point]) && PointSeen(handles, point);

        void Mark<T>(ICollection<T> set, T item)
        {
            if (handles.Ctrl) set.Remove(item);
            else if (!set.Contains(item)) set.Add(item);
        }

        switch (Mode)
        {
            case SelectMode.Vertex:
                for (int i = 0; i < _mesh!.Points.Count; i++)
                    if (!_hiddenPoints.Contains(i) && PointInside(i)) Mark(_vertices, i);
                break;
            case SelectMode.Edge:
                foreach ((int a, int b) in _edges)
                    if (!_hiddenEdges.Contains(EdgeKey(a, b)) && PointInside(a) && PointInside(b)) Mark(_selectedEdges, EdgeKey(a, b));
                break;
            case SelectMode.Face:
                for (int polygon = 0; polygon < _mesh!.Polygons.Count; polygon++)
                {
                    if (_hiddenFaces.Contains(polygon) || _mesh.PolygonPoints(polygon).Count == 0) continue;
                    Float3 centre = FaceCentre(polygon);
                    if (InRect(centre) && Seen(handles, centre)) Mark(_faces, polygon);
                }
                break;
        }
        Normalize();
    }

    private Float3 FaceCentre(int polygon)
    {
        IReadOnlyList<int> ring = _mesh!.PolygonPoints(polygon);
        Float3 sum = Float3.Zero;
        foreach (int i in ring) sum += _mesh.Points[i];
        return ring.Count > 0 ? sum / ring.Count : sum;
    }

    // Previews of an action write the brush over many frames, so its record stays open until the action ends
    private void HoldActionUndo(string name)
    {
        _actionUndo ??= Undo.HoldRecords([Target!], name);
    }

    private void ReleaseActionUndo()
    {
        if (_actionUndo == null) return;
        Undo.ReleaseRecords(_actionUndo, revert: false);
        _actionUndo = null;
    }

    private void EndAction()
    {
        ReleaseActionUndo();
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

        Float3 before = centre;
        Quaternion axes = SceneTools.Orientation switch
        {
            PivotOrientation.Global => Quaternion.Identity,
            PivotOrientation.Normal => NormalFrame() ?? Target.Transform.Rotation,
            _ => Target.Transform.Rotation,
        };
        bool moved = TransformHandles.PositionHandle(handles, MoveHandle, ref centre, axes, out bool hot);
        if (hot && !_dragging)
        {
            _dragRaw = _dragApplied = Float3.Zero;
            _gizmoStart = before;
            _dragPoints = SelectedWorld();
        }
        if (_dragging && !hot) CommitSession("Edit CSG Brush");

        _dragging = hot;

        // Snapped over the whole drag so small per frame steps do not round away, the selection landing on the grid
        Float3 step = Float3.Zero;
        if (moved)
        {
            // Shift slows the drag for fine moves
            _dragRaw += (centre - before) * (handles.Shift ? 0.1f : 1f);
            Float3 snapped = CSGSnap.Move(handles, _dragPoints, _dragRaw, axes);

            // Snapping to a brush carries the gizmo's starting centre onto the target, along only the axes being dragged
            if (SnapToBrushes && FindSnap(handles, handles.MousePosition) is { } target)
            {
                Float3 raw = Quaternion.Inverse(axes) * _dragRaw, onto = Quaternion.Inverse(axes) * (target - _gizmoStart);
                float still = 1e-6f + Float3.Length(raw) * 1e-4f;
                for (int i = 0; i < 3; i++)
                    if (MathF.Abs(raw[i]) < still) onto[i] = 0f;
                snapped = axes * onto;
                ctx.Draw.Dot(target, new Color32(255, 220, 90, 255), 9f);
            }
            step = snapped - _dragApplied;
            _dragApplied = snapped;
        }

        if (hot) DrawDistance(ctx, centre, _dragApplied);
        if (!moved || Float3.LengthSquared(step) < 1e-12f) return;
        if ((_session ?? StartSession()) is not { } session) return;

        Float3 total = _dragApplied;
        PreviewSession(() => session.Translate(total));
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

        // Each point snaps on its own to the world grid, which squares up a skewed brush
        Float4x4 toWorld = Target!.Transform.LocalToWorldMatrix, toLocal = Target.Transform.WorldToLocalMatrix;
        RunEdit("Snap CSG Vertices", selected =>
        {
            foreach (GeometryData.Vertex vertex in selected.Vertices)
                vertex.Point = Float4x4.TransformPoint(CSGSnap.Round(Float4x4.TransformPoint(vertex.Point, toWorld)), toLocal);
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

    /// <summary>Draws the brush's elements into the scene's gizmos, hidden behind surfaces by depth unless X-ray is on.</summary>
    private void DrawElements(SceneToolContext ctx)
    {
        Prowl.Graphite.Rendering.GizmoBatch batch = XRay ? Runtime.Resources.Scene.Active.Gizmos.Batch : Runtime.Resources.Scene.Active.Gizmos.SurfaceBatch;
        Float4x4 toWorld = Target!.Transform.LocalToWorldMatrix;

        foreach (int polygon in _faces)
        {
            IReadOnlyList<int> ring = _mesh!.PolygonPoints(polygon);
            if (ring.Count < 3) continue;
            Float3 first = Float4x4.TransformPoint(_mesh.Points[ring[0]], toWorld);
            for (int i = 1; i + 1 < ring.Count; i++)
                batch.Triangle(first, Float4x4.TransformPoint(_mesh.Points[ring[i]], toWorld), Float4x4.TransformPoint(_mesh.Points[ring[i + 1]], toWorld), FaceFill);
        }

        for (int i = 0; i < _edges.Count; i++)
        {
            (int a, int b) = _edges[i];
            if (_hiddenEdges.Contains(EdgeKey(a, b))) continue;
            bool picked = _selectedEdges.Contains(EdgeKey(a, b));
            bool hovered = Mode == SelectMode.Edge && ctx.Handles.IsNearest(ctx.Handles.GetControlID(EdgeControl, i));
            batch.Line(Float4x4.TransformPoint(_mesh!.Points[a], toWorld), Float4x4.TransformPoint(_mesh.Points[b], toWorld),
                picked ? PickedColor : hovered ? HoverEdgeColor : IdleColor, picked || hovered ? 2.5f : 1f);
        }

        if (Mode == SelectMode.Vertex)
        {
            for (int i = 0; i < _mesh!.Points.Count; i++)
            {
                if (_hiddenPoints.Contains(i)) continue;
                bool picked = _vertices.Contains(i);
                batch.Point(Float4x4.TransformPoint(_mesh.Points[i], toWorld), picked ? PickedColor : IdleColor, picked ? 8f : 6f);
            }
        }
        else if (Mode == SelectMode.Face)
        {
            for (int polygon = 0; polygon < _mesh!.Polygons.Count; polygon++)
            {
                if (_hiddenFaces.Contains(polygon) || _mesh.PolygonPoints(polygon).Count < 3) continue;
                bool picked = _faces.Contains(polygon);
                batch.Point(Float4x4.TransformPoint(FaceCentre(polygon), toWorld), picked ? PickedColor : IdleColor, picked ? 7f : 5f);
            }
        }
    }

    public override void OnDrawOverlay(SceneToolContext ctx, Quill.Canvas canvas)
    {
        if (Active) TransformHandles.Draw(canvas);
    }
}

/// <summary>The Edit tool's selection as point, edge and polygon indices, recorded with each edit so undo brings it back.</summary>
internal sealed class CSGEditSelection
{
    public int[] Points = [];
    public int[] Edges = [];
    public int[] Faces = [];
}
