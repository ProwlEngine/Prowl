// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Modeling;
using Prowl.Editor.Theming;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>
/// Edit mode of the model world. Tab on selected model objects edits all of their meshes at once: each keeps its own mesh
/// and transform, the selection spans them, and transforms move points of every object together in world space.
/// </summary>
[GlobalSceneTool]
public sealed class ModelMeshTool : SceneTool
{
    public override string Name => "Model Edit";
    public override string Icon => EditorIcons.DrawPolygon;
    public override string? Tooltip => "Edit the meshes of the selected model objects (Tab)";

    private const string VertexControl = "model_vertex";
    private const string EdgeControl = "model_edge";
    private const string FaceControl = "model_face";
    private const string MarqueeControl = "model_marquee";
    private const string MoveHandle = "model_move";
    private const string ModalControl = "model_modal";
    private const float PickRadius = 9f;
    private const float DragThreshold = 4f;

    private static readonly Color32 IdleColor = new(150, 190, 230, 200);
    private static readonly Color32 PickedColor = new(255, 210, 80, 255);
    private static readonly Color32 SeamColor = new(235, 70, 55, 255);
    private static readonly Color32 FaceFill = new(255, 200, 90, 60);

    /// <summary>The objects being edited, empty outside edit mode.</summary>
    private readonly List<ModelObject> _edited = new();

    // The mesh each object had when last looked at, so a mesh put back by undo is noticed
    private readonly Dictionary<ModelObject, GeometryData> _meshes = new();
    private readonly Dictionary<ModelObject, ElementSelection> _selection = new();
    private readonly Dictionary<ModelObject, ElementSelection> _hidden = new();
    private readonly ModelEditSelection _held = new();

    // A modal grab, rotate or scale, or a gizmo or box drag in progress
    private TransformModal? _modal;
    private HandleContext? _modalHandles;
    private TransformSession? _session;
    private System.Action? _sessionMove;
    private bool _gizmoDragging;
    private Float3 _gizmoRaw, _gizmoApplied, _gizmoStart;
    private bool _marquee;
    private Float2 _marqueeFrom;

    // An operator whose amount follows the mouse: the meshes and selections it started from, what it runs, and its count
    private AmountOperator? _operator;
    private readonly Dictionary<ModelObject, (GeometrySnapshot Mesh, ModelEditPart Selection)> _start = new();
    private Float2 _operatorFrom;
    private int _operatorCount;
    private float _operatorKey = float.NaN;
    private HandleContext? _operatorHandles;

    // A loop cut: the object and edge of its ring by point indices, whether a click has picked it, and how many cuts
    private bool _loopCut;
    private (ModelObject Model, int A, int B)? _loopEdge;
    private bool _loopPicked;
    private int _loopCuts = 1;

    // The knife: the object it cuts, and where each point was clicked and what of the mesh it sits on
    private ModelObject? _knifeModel;
    private readonly List<(Float2 Pixel, KnifePoint Point)> _knife = new();

    /// <summary>
    /// An operator run with an amount from the mouse and a count from the wheel. A distance amount is the mouse's travel
    /// in world units at the selection and snaps to the grid, any other is the travel in pixels.
    /// </summary>
    private sealed record AmountOperator(string Name, bool Distance, Func<float, float> Amount, string Format,
                                         Action<GeometryData, ElementSelection, float, int> Run, SelectMode? Selects, int StartCount = 1, int MaxCount = 1)
    {
        /// <summary>When set the amount is the mouse's travel along this screen direction, from -1 to 1, as for a slide.</summary>
        public Float2? Along { get; init; }
    }

    public SelectMode Mode { get; private set; } = SelectMode.Vertex;
    public bool XRay { get; set; }

    /// <summary>Moved points snap to the corners, edges and faces of every model object under the mouse.</summary>
    public bool SnapToObjects { get; set; }

    /// <summary>Unselected points near the selection move with it, less the further they are.</summary>
    public bool Proportional { get; set; }
    public float ProportionalRadius { get; set; } = 1f;

    /// <summary>Whether edit mode is on.</summary>
    public bool Editing => _edited.Count > 0;

    public override bool IsAvailable(SceneToolContext ctx) => ModelEditingMode.IsEditing;

    public override bool SuppressTransformGizmo => Editing;

    public override void OnDeactivated() => Leave();

    // ================================================================
    //  Entering and leaving
    // ================================================================

    private void Enter()
    {
        _edited.Clear();
        foreach (GameObject go in Selection.GetSelected<GameObject>())
            if (go.GetComponent<ModelObject>() is { } model && model.IsValid()) _edited.Add(model);
        _selection.Clear();
        _hidden.Clear();
        _meshes.Clear();
        foreach (ModelObject model in _edited)
        {
            _selection[model] = new ElementSelection();
            _hidden[model] = new ElementSelection();
            _meshes[model] = model.Mesh;
        }
    }

    private void Leave()
    {
        CancelModal();
        if (_loopCut)
        {
            RestoreStart();
            EndLoopCut();
        }
        if (!ReferenceEquals(_knifeModel, null)) EndKnife();
        if (_operator != null)
        {
            EndOperator();
            foreach ((ModelObject model, (GeometrySnapshot mesh, _)) in _start)
                if (model.IsValid())
                {
                    model.Mesh.Restore(mesh);
                    model.RefreshPreview();
                }
            _start.Clear();
        }
        _edited.Clear();
        _selection.Clear();
        _hidden.Clear();
        _meshes.Clear();
    }

    public override void OnSceneInput(SceneToolContext ctx)
    {
        _trees.Clear();
        HandleContext handles = ctx.Handles;
        if (!ModelEditingMode.IsEditing)
        {
            Leave();
            return;
        }

        if (_modal == null && handles.IsPressed("ModelEdit/Toggle"))
        {
            if (Editing) Leave();
            else Enter();
            return;
        }
        if (!Editing)
        {
            if (handles.IsPressed("ModelEdit/Join") && Selection.ActiveObject is GameObject active)
                ModelEditingMode.Join([.. Selection.GetSelected<GameObject>()], active);
            return;
        }

        _edited.RemoveAll(m => m.IsNotValid());
        if (_edited.Count == 0)
        {
            Leave();
            return;
        }
        CatchUpWithUndo();

        if (_modal != null)
        {
            TickModal(ctx);
            DrawElements(ctx);
            return;
        }
        if (_operator != null)
        {
            TickOperator(ctx);
            DrawElements(ctx);
            return;
        }
        if (_loopCut)
        {
            TickLoopCut(ctx);
            DrawElements(ctx);
            return;
        }
        if (!ReferenceEquals(_knifeModel, null))
        {
            TickKnife(ctx);
            DrawElements(ctx);
            return;
        }

        Keys(ctx);
        DrawElements(ctx);
        if (_marquee)
        {
            TickMarquee(ctx);
            return;
        }
        MoveGizmo(ctx);
        Pick(ctx);
    }

    /// <summary>After an undo or redo a mesh comes back as a new one, and the selection comes back with it by index.</summary>
    private void CatchUpWithUndo()
    {
        bool changed = false;
        foreach (ModelObject model in _edited)
        {
            if (ReferenceEquals(_meshes[model], model.Mesh)) continue;
            _meshes[model] = model.Mesh;
            _hidden[model] = new ElementSelection();
            changed = true;
        }
        if (changed) RestoreHeld();
    }

    // ================================================================
    //  Keys
    // ================================================================

    private void Keys(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;

        // The scene view's tool keys stand down, edit mode takes R and E
        handles.IsPressed("Scene/ToolTranslate");
        handles.IsPressed("Scene/ToolRotate");
        handles.IsPressed("Scene/ToolScale");

        if (handles.IsPressed("MeshEdit/VertexMode")) SetMode(SelectMode.Vertex);
        else if (handles.IsPressed("MeshEdit/EdgeMode")) SetMode(SelectMode.Edge);
        else if (handles.IsPressed("MeshEdit/FaceMode")) SetMode(SelectMode.Face);
        else if (handles.IsPressed("MeshEdit/SelectAll")) Command((mesh, s) => Selecting.All(mesh, s, Mode));
        else if (handles.IsPressed("MeshEdit/SelectNone")) Command((_, s) => s.Clear());
        else if (handles.IsPressed("MeshEdit/Invert")) Command((mesh, s) => Selecting.Invert(mesh, s, Mode));
        else if (handles.IsPressed("MeshEdit/More")) Command((_, s) => Selecting.More(s, Mode));
        else if (handles.IsPressed("MeshEdit/Less")) Command((mesh, s) => Selecting.Less(mesh, s, Mode));
        else if (handles.IsPressed("MeshEdit/Linked")) Command((_, s) => Selecting.Linked(s, Mode));
        else if (handles.IsPressed("MeshEdit/Similar")) Command((mesh, s) => Selecting.Similar(mesh, s, Mode));
        else if (handles.IsPressed("MeshEdit/XRay")) XRay = !XRay;
        else if (handles.IsPressed("MeshEdit/SnapToBrushes")) SnapToObjects = !SnapToObjects;
        else if (handles.IsPressed("MeshEdit/Proportional")) Proportional = !Proportional;
        else if (handles.IsPressed("MeshEdit/Inset")) Inset(ctx);
        else if (handles.IsPressed("MeshEdit/LoopCut")) StartLoopCut(ctx);
        else if (handles.IsPressed("MeshEdit/Knife")) StartKnife(ctx);
        else if (handles.IsPressed("MeshEdit/HideUnselected")) Hide(unselected: true);
        else if (handles.IsPressed("MeshEdit/Reveal")) foreach (ModelObject model in _edited) _hidden[model] = new ElementSelection();
        else if (handles.IsPressed("MeshEdit/Hide")) Hide(unselected: false);
        else if (handles.IsPressed("MeshEdit/Frame")) Frame(ctx);
        else if (handles.IsPressed("MeshEdit/Grab")) StartModal(ctx, TransformKind.Translate);
        else if (handles.IsPressed("MeshEdit/Rotate")) StartModal(ctx, TransformKind.Rotate);
        else if (handles.IsPressed("MeshEdit/Scale")) StartModal(ctx, TransformKind.Scale);
        else if (handles.IsPressed("MeshEdit/Dissolve")) Edit("Dissolve Model Elements", (mesh, s) => Operators.Dissolve(mesh, s, Mode, s), SelectMode.Face);
        else if (handles.IsPressed("MeshEdit/Connect")) Edit("Connect Model Points", (mesh, s) => Operators.Connect(mesh, [.. s.Vertices], ctx.Camera.GameObject.Transform.Forward, s), SelectMode.Edge);
        else if (handles.IsPressed("MeshEdit/Triangulate")) Edit("Triangulate Model Faces", (mesh, s) => Operators.TriangulateFaces(mesh, [.. s.Faces], s), SelectMode.Face);
        else if (handles.IsPressed("MeshEdit/Flatten")) Edit("Flatten Model Points", (mesh, s) => Operators.Flatten(mesh, s));
        else if (handles.IsPressed("MeshEdit/Merge")) Edit("Merge Model Points", MergeAtCentre, SelectMode.Vertex);
        else if (handles.IsPressed("MeshEdit/Bridge")) Edit("Bridge Model Faces", Bridge, SelectMode.Face);
        else if (handles.IsPressed("MeshEdit/Separate")) SeparateSelected();
        else if (handles.IsPressed("MeshEdit/Extrude"))
            StartOperator(ctx, new AmountOperator("Extrude Model Faces", true, d => d, "0.###", (mesh, s, d, _) => Operators.ExtrudeRegion(mesh, [.. s.Faces], d, s), SelectMode.Face));
        else if (handles.IsPressed("MeshEdit/Bevel"))
            StartOperator(ctx, new AmountOperator("Bevel Model Edges", true, d => d, "0.###", Bevel, SelectMode.Face, 1, 32));
        else if (handles.IsPressed("MeshEdit/LimitedDissolve"))
            StartOperator(ctx, new AmountOperator("Limited Dissolve Model", false, p => 5f + p * 0.2f, "0.#", (mesh, s, deg, _) =>
                Operators.LimitedDissolve(mesh, s.Edges.Count > 0 ? [.. s.Edges] : null, Math.Clamp(deg, 0f, 90f), s), SelectMode.Face));
        else if (handles.IsPressed("MeshEdit/TrisToQuads"))
            StartOperator(ctx, new AmountOperator("Join Model Triangles", false, p => 40f + p * 0.2f, "0.#", (mesh, s, deg, _) =>
                Operators.TrisToQuads(mesh, s.Faces.Count > 0 ? [.. s.Faces] : null, Math.Clamp(deg, 0f, 180f), s), SelectMode.Face));
        else if (handles.IsPressed("MeshEdit/MergeByDistance"))
            StartOperator(ctx, new AmountOperator("Merge Model Points By Distance", true, d => d, "0.###", (mesh, s, d, _) =>
                Operators.MergeByDistance(mesh, s.Vertices.Count > 0 ? [.. s.Vertices] : null, d, s), SelectMode.Vertex));
        else if (handles.IsPressed("MeshEdit/Smooth"))
            StartOperator(ctx, new AmountOperator("Smooth Model Points", false, p => p * 0.005f, "0.##", (mesh, s, t, _) =>
                Operators.Smooth(mesh, [.. s.Vertices], Math.Clamp(t, 0f, 1f), s), SelectMode.Vertex));
        else if (handles.KeyDown(KeyCode.Delete) || handles.KeyDown(KeyCode.Backspace)) Edit("Delete Model Elements", Delete);
    }

    public void SetMode(SelectMode mode)
    {
        if (mode == Mode) return;
        foreach (ModelObject model in _edited) Selecting.ToMode(_selection[model], Mode, mode);
        Mode = mode;
    }

    /// <summary>A selection command on every edited object, hidden parts left out.</summary>
    private void Command(Action<GeometryData, ElementSelection> command)
    {
        foreach (ModelObject model in _edited)
        {
            command(model.Mesh, _selection[model]);
            Selecting.Without(_selection[model], _hidden[model], Mode);
        }
    }

    private void Hide(bool unselected)
    {
        foreach (ModelObject model in _edited)
        {
            ElementSelection selection = _selection[model];
            if (unselected)
            {
                var others = new ElementSelection();
                others.Vertices.UnionWith(selection.Vertices);
                others.Edges.UnionWith(selection.Edges);
                others.Faces.UnionWith(selection.Faces);
                Selecting.Invert(model.Mesh, others, Mode);
                Selecting.Without(others, _hidden[model], Mode);
                Selecting.Hide(_hidden[model], others, Mode);
            }
            else Selecting.Hide(_hidden[model], selection, Mode);
        }
    }

    /// <summary>Frames the selected points, or the shown parts of the objects when nothing is selected.</summary>
    private void Frame(SceneToolContext ctx)
    {
        if (ctx.View is not SceneViewPanel { EditorCamera: { } camera }) return;
        Float3 min = new(float.MaxValue), max = new(float.MinValue);
        bool any = false, selectedOnly = _edited.Any(m => _selection[m].Vertices.Count > 0);
        foreach (ModelObject model in _edited)
        {
            Float4x4 toWorld = model.Transform.LocalToWorldMatrix;
            IEnumerable<GeometryData.Vertex> points = selectedOnly ? _selection[model].Vertices : model.Mesh.Vertices.Where(v => !_hidden[model].Vertices.Contains(v));
            foreach (GeometryData.Vertex vertex in points)
            {
                Float3 p = Float4x4.TransformPoint(vertex.Point, toWorld);
                min = Maths.Min(min, p);
                max = Maths.Max(max, p);
                any = true;
            }
        }
        if (any) camera.Frame((min + max) * 0.5f, MathF.Max(0.25f, Float3.Length(max - min) * 0.5f));
    }

    /// <summary>Moves the selected faces of each object into a new object beside it, with the same transform and materials.</summary>
    private void SeparateSelected()
    {
        const string Name = "Separate Model Faces";
        HoldSelection();
        Undo.RecordObject(_held, UndoOwner.OfSession, Name);
        foreach (ModelObject model in _edited.ToList())
        {
            ElementSelection selection = _selection[model];
            if (selection.Faces.Count == 0) continue;

            Undo.RecordObject(model, Name);
            GeometryData part = Operators.Separate(model.Mesh, [.. selection.Faces]);
            selection.Clear();
            model.RefreshPreview();

            GameObject source = model.GameObject;
            var go = new GameObject($"{source.Name} Part");
            EditorSceneManager.EditedWorld.Add(go);
            if (source.Parent.IsValid()) go.SetParent(source.Parent!, false);
            go.Transform.LocalPosition = source.Transform.LocalPosition;
            go.Transform.LocalRotation = source.Transform.LocalRotation;
            go.Transform.LocalScale = source.Transform.LocalScale;
            ModelObject split = go.AddComponent<ModelObject>();
            split.Mesh = part;
            split.Materials = new List<Guid>(model.Materials);
            split.SmoothingAngle = model.SmoothingAngle;
            split.Modifiers = [.. model.Modifiers.Select(m => m.Clone())];
            split.Bones = new List<PMeshBone>(model.Bones);
            split.Shapes = new List<PMeshShape>(model.Shapes);
            split.RefreshPreview();
            Undo.RegisterCreatedObjectUndo(go, Name);
        }
        HoldSelection();
    }

    private void Inset(SceneToolContext ctx)
        => StartOperator(ctx, new AmountOperator("Inset Model Faces", true, d => d, "0.###", (mesh, s, d, _) => Operators.Inset(mesh, [.. s.Faces], d, s), SelectMode.Face));

    private void Subdivide(SceneToolContext ctx)
    {
        if (Mode == SelectMode.Face)
            StartOperator(ctx, new AmountOperator("Subdivide Model Faces", false, _ => 0f, "0", (mesh, s, _, cuts) =>
                Operators.SubdivideFaces(mesh, [.. s.Faces], cuts, s), SelectMode.Face, 1, 16));
        else
            StartOperator(ctx, new AmountOperator("Subdivide Model Edges", false, _ => 0f, "0", (mesh, s, _, cuts) =>
                Operators.SubdivideEdges(mesh, [.. s.Edges], cuts, s), SelectMode.Vertex, 1, 64));
    }

    /// <summary>
    /// What a moved point snaps to with the mouse at a screen point: a corner, a point of an edge or a face of any model
    /// object, the edited ones included. Selected points, their edges and faces, and hidden parts are never targets.
    /// </summary>
    private Float3? FindSnap(HandleContext handles, Float2 mouse)
    {
        if (ModelEditingMode.World is not { } world) return null;
        var corners = new List<Float3>();
        var edges = new List<(Float3, Float3)>();
        Float3? face = null;
        float nearest = float.MaxValue;
        foreach (GameObject go in world.Objects)
        {
            if (go.GetComponent<ModelObject>() is not { } model || model.IsNotValid()) continue;
            Float4x4 toWorld = model.Transform.LocalToWorldMatrix;
            ElementSelection? selected = _selection.TryGetValue(model, out ElementSelection? s) ? s : null;
            ElementSelection? hidden = _hidden.TryGetValue(model, out ElementSelection? h) ? h : null;
            foreach (GeometryData.Vertex vertex in model.Mesh.Vertices)
                if (selected?.Vertices.Contains(vertex) != true && hidden?.Vertices.Contains(vertex) != true) corners.Add(Float4x4.TransformPoint(vertex.Point, toWorld));
            foreach (GeometryData.Edge edge in model.Mesh.Edges)
            {
                if (selected != null && (selected.Vertices.Contains(edge.Vert1) || selected.Vertices.Contains(edge.Vert2)) || hidden?.Edges.Contains(edge) == true) continue;
                edges.Add((Float4x4.TransformPoint(edge.Vert1.Point, toWorld), Float4x4.TransformPoint(edge.Vert2.Point, toWorld)));
            }
            Func<GeometryData.Face, bool>? skip = selected == null ? null
                : f => hidden?.Faces.Contains(f) == true || f.NeighborVertices().Exists(selected.Vertices.Contains);
            if (Tree(model.Mesh).Raycast(toWorld, handles.MouseRay, out _, out float distance, skip) && distance < nearest)
            {
                nearest = distance;
                face = handles.MouseRay.Origin + handles.MouseRay.Direction * distance;
            }
        }
        return Snapping.Find(corners, edges, face, handles.WorldToScreen, mouse, out Float3 point) ? point : null;
    }

    // ================================================================
    //  Right click menu
    // ================================================================

    public override bool ReplacesSceneContextMenu => Editing;

    public override void OnSceneContextMenu(SceneToolContext ctx, OrigamiUI.ContextBuilder menu)
    {
        if (!Editing) return;
        menu.Title(Loc.Get("model.edit_mode"));
        foreach ((string label, System.Action run) in Operations(ctx)) menu.Item(label, run);
        menu.Separator();
        menu.Item(Loc.Get("csg.xray"), () => XRay = !XRay, on: XRay);
        menu.Item(Loc.Get("csg.snap_brushes"), () => SnapToObjects = !SnapToObjects, on: SnapToObjects);
        menu.Item(Loc.Get("csg.proportional"), () => Proportional = !Proportional, on: Proportional);
    }

    /// <summary>The operations the right click menu and the Inspector's Tool tab offer, by label.</summary>
    private List<(string Label, System.Action Run)> Operations(SceneToolContext ctx)
    {
        var list = new List<(string, System.Action)>
        {
            (Loc.Get("csg.extrude_region"), () => StartOperator(ctx, new AmountOperator("Extrude Model Faces", true, d => d, "0.###", (mesh, s, d, _) => Operators.ExtrudeRegion(mesh, [.. s.Faces], d, s), SelectMode.Face))),
            (Loc.Get("csg.inset"), () => Inset(ctx)),
            (Loc.Get("csg.bevel"), () => StartOperator(ctx, new AmountOperator("Bevel Model Edges", true, d => d, "0.###", Bevel, SelectMode.Face, 1, 32))),
            (Loc.Get("csg.loop_cut"), () => StartLoopCut(ctx)),
            (Loc.Get("csg.knife"), () => StartKnife(ctx)),
            (Loc.Get("csg.subdivide"), () => Subdivide(ctx)),
            (Loc.Get("csg.dissolve"), () => Edit("Dissolve Model Elements", (mesh, s) => Operators.Dissolve(mesh, s, Mode, s), SelectMode.Face)),
            (Loc.Get("csg.merge_centre"), () => Edit("Merge Model Points", MergeAtCentre, SelectMode.Vertex)),
            (Loc.Get("csg.connect"), () => Edit("Connect Model Points", (mesh, s) => Operators.Connect(mesh, [.. s.Vertices], ctx.Camera.GameObject.Transform.Forward, s), SelectMode.Edge)),
            (Loc.Get("csg.triangulate"), () => Edit("Triangulate Model Faces", (mesh, s) => Operators.TriangulateFaces(mesh, [.. s.Faces], s), SelectMode.Face)),
            (Loc.Get("csg.flatten"), () => Edit("Flatten Model Points", (mesh, s) => Operators.Flatten(mesh, s))),
            (Loc.Get("csg.bridge"), () => Edit("Bridge Model Faces", Bridge, SelectMode.Face)),
            (Loc.Get("csg.separate"), SeparateSelected),
            (Loc.Get("model.origin_to_selection"), OriginToSelection),
        };
        for (int axis = 0; axis < 3; axis++)
        {
            int chosen = axis;
            string name = axis == 0 ? "X" : axis == 1 ? "Y" : "Z";
            list.Add((Loc.Get("csg.symmetrize") + "  " + Loc.Get("csg.symmetrize_side", new { from = "+" + name, to = "-" + name }),
                () => Edit("Symmetrize Model", (mesh, s) => { Operators.Symmetrize(mesh, chosen, true, 0f); s.Clear(); })));
        }

        list.Add((Loc.Get("uv.mark_seam"), () => Edit("Mark Seam", (mesh, s) => UVs.SetSeams(mesh, s.Edges, true))));
        list.Add((Loc.Get("uv.clear_seam"), () => Edit("Clear Seam", (mesh, s) => UVs.SetSeams(mesh, s.Edges, false))));
        list.Add((Loc.Get("uv.seams_from_sharp"), () => Edit("Seams From Sharp Edges", (mesh, _) => UVs.SeamsFromSharp(mesh))));
        list.Add((Loc.Get("uv.seams_from_materials"), () => Edit("Seams From Materials", (mesh, _) => UVs.SeamsFromMaterials(mesh))));
        list.Add((Loc.Get("uv.seams_from_islands"), () => Edit("Seams From Islands", (mesh, _) => UVs.SeamsFromIslands(mesh))));
        list.Add((Loc.Get("uv.unwrap"), () => Edit("Unwrap", (mesh, s) => UVs.Unwrap(mesh, UVFaces(mesh, s)))));
        list.Add((Loc.Get("uv.project_cube"), () => Edit("Cube Projection", (mesh, s) => UVs.Cube(mesh, UVFaces(mesh, s)))));
        list.Add((Loc.Get("uv.project_cylinder"), () => Edit("Cylinder Projection", (mesh, s) => UVs.Cylinder(mesh, UVFaces(mesh, s)))));
        list.Add((Loc.Get("uv.project_sphere"), () => Edit("Sphere Projection", (mesh, s) => UVs.Sphere(mesh, UVFaces(mesh, s)))));
        list.Add((Loc.Get("uv.project_angle"), () => Edit("Project By Angle", (mesh, s) => UVs.ByAngle(mesh, UVFaces(mesh, s), 66f))));
        list.Add((Loc.Get("uv.project_view"), () => Edit("Project From View", (mesh, s) =>
        {
            if (_edited.Find(m => ReferenceEquals(m.Mesh, mesh)) is not { } model) return;
            Float4x4 toWorld = model.Transform.LocalToWorldMatrix;
            UVs.FromView(mesh, UVFaces(mesh, s), p => ctx.Handles.WorldToScreen(Float4x4.TransformPoint(p, toWorld)) is { } at ? new Float2(at.X, -at.Y) : Float2.Zero);
        })));
        list.Add((Loc.Get("uv.reset"), () => Edit("Reset UVs", (mesh, s) => UVs.Reset(mesh, UVFaces(mesh, s)))));
        list.Add((Loc.Get("uv.follow_quad"), () => Edit("Follow Quad", (mesh, s) =>
        {
            List<GeometryData.Face> faces = UVFaces(mesh, s);
            if (faces.Where(f => f.VertCount == 4).OrderBy(f => f.Index).FirstOrDefault() is { } active) UVs.FollowQuad(mesh, active, faces);
        })));
        return list;
    }

    // ================================================================
    //  For the UV editor
    // ================================================================

    /// <summary>The model objects in edit mode.</summary>
    public IReadOnlyList<ModelObject> EditedObjects => _edited;

    public ElementSelection SelectionOf(ModelObject model) => _selection.TryGetValue(model, out ElementSelection? selection) ? selection : new ElementSelection();

    public bool IsHidden(ModelObject model, GeometryData.Face face) => _hidden.TryGetValue(model, out ElementSelection? hidden) && hidden.Faces.Contains(face);

    /// <summary>Selects points of the edited objects, adding to the selection or replacing it.</summary>
    public void SelectPoints(IReadOnlyDictionary<ModelObject, List<GeometryData.Vertex>> points, bool add)
    {
        foreach (ModelObject model in _edited)
        {
            ElementSelection selection = _selection[model];
            if (!add) selection.Clear();
            if (points.TryGetValue(model, out List<GeometryData.Vertex>? picked)) selection.Vertices.UnionWith(picked);
            Selecting.ToMode(selection, SelectMode.Vertex, Mode);
            Selecting.Without(selection, _hidden[model], Mode);
        }
    }

    /// <summary>Changes the edited meshes as one undo step, each with its object and selection.</summary>
    public void EditMeshes(string name, Action<ModelObject, GeometryData, ElementSelection> edit)
        => Edit(name, (mesh, s) =>
        {
            if (_edited.Find(m => ReferenceEquals(m.Mesh, mesh)) is { } model) edit(model, mesh, s);
        });

    /// <summary>The faces a UV operation works on: the selected ones, or every face when none is selected.</summary>
    private static List<GeometryData.Face> UVFaces(GeometryData mesh, ElementSelection selection)
        => selection.Faces.Count > 0 ? [.. selection.Faces] : [.. mesh.Faces];

    /// <summary>The operations for the Inspector's Tool tab, started in the scene view as from its menu.</summary>
    public List<(string Label, System.Action Run)> PanelOperations() => Editing ? Operations(Context) : [];

    // ================================================================
    //  Adjust last
    // ================================================================

    // The last amount operator: its settings, its undo step, and the meshes it left, so a later change to them ends it
    private sealed record LastOperator(AmountOperator Op, float Amount, int Count, int Group, long Step, List<(ModelObject Model, GeometryData Mesh, int Version)> Left);
    private LastOperator? _last;

    /// <summary>The last operator run, while nothing else has changed the meshes or the history since.</summary>
    public (string Name, float Amount, bool Distance, string Format, int Count, int MaxCount)? Last
        => _last is { } last && LastStillHolds(last) ? (last.Op.Name, last.Amount, last.Op.Distance, last.Op.Format, last.Count, last.Op.MaxCount) : null;

    private static bool LastStillHolds(LastOperator last)
    {
        IReadOnlyList<UndoStepInfo> steps = Undo.UndoSteps;
        if (steps.Count == 0 || steps[^1].Id != last.Step) return false;
        foreach ((ModelObject model, GeometryData mesh, int version) in last.Left)
            if (model.IsNotValid() || !ReferenceEquals(model.Mesh, mesh) || mesh.Version != version) return false;
        return true;
    }

    /// <summary>Runs the last operator again from before it, with a new amount and count, in place of its step.</summary>
    public void AdjustLast(float amount, int count)
    {
        if (_last is not { } last || !LastStillHolds(last)) return;
        Undo.RevertAllDownToGroup(last.Group);
        RestoreHeld();
        RunAsLast(last.Op, amount, Math.Clamp(count, 1, Math.Max(1, last.Op.MaxCount)));
    }

    /// <summary>Runs an amount operator as one undo step of its own, remembered for adjusting.</summary>
    private void RunAsLast(AmountOperator op, float amount, int count)
    {
        Undo.FlushUndoRecordObjects();
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Edit(op.Name, (mesh, s) => op.Run(mesh, s, amount, count), op.Selects);
        Undo.FlushUndoRecordObjects();
        IReadOnlyList<UndoStepInfo> steps = Undo.UndoSteps;
        _last = steps.Count == 0 ? null : new LastOperator(op, amount, count, group, steps[^1].Id, [.. _edited.Select(m => (m, m.Mesh, m.Mesh.Version))]);
    }

    // ================================================================
    //  Selection for the Inspector
    // ================================================================

    /// <summary>The middle of every selected point in the world, or null with nothing selected.</summary>
    public Float3? SelectionMedian
    {
        get
        {
            Float3 sum = Float3.Zero;
            int count = 0;
            foreach (ModelObject model in _edited)
            {
                Float4x4 toWorld = model.Transform.LocalToWorldMatrix;
                foreach (GeometryData.Vertex vertex in _selection[model].Vertices)
                {
                    sum += Float4x4.TransformPoint(vertex.Point, toWorld);
                    count++;
                }
            }
            return count == 0 ? null : sum / count;
        }
    }

    /// <summary>Moves the selected points so their middle lands on a point in the world.</summary>
    public void MoveSelectionTo(Float3 world)
    {
        if (SelectionMedian is not { } median) return;
        Float3 delta = world - median;
        Edit("Move Model Points", (mesh, s) =>
        {
            if (_edited.Find(m => ReferenceEquals(m.Mesh, mesh)) is not { } model) return;
            Float4x4 toWorld = model.Transform.LocalToWorldMatrix, toLocal = toWorld.Invert();
            foreach (GeometryData.Vertex vertex in s.Vertices)
                vertex.Point = Float4x4.TransformPoint(Float4x4.TransformPoint(vertex.Point, toWorld) + delta, toLocal);
        });
    }

    /// <summary>The material slot of the selected faces, -1 when they differ, null with none selected.</summary>
    public int? SelectedFaceSlot
    {
        get
        {
            int? found = null;
            foreach (ModelObject model in _edited)
                foreach (GeometryData.Face face in _selection[model].Faces)
                {
                    int slot = SlotOf(face);
                    if (found == null) found = slot;
                    else if (found != slot) return -1;
                }
            return found;
        }
    }

    /// <summary>How many material slots the edited objects have at most.</summary>
    public int SlotCount => _edited.Count == 0 ? 0 : _edited.Max(m => Math.Max(1, m.Materials.Count));

    public void SetSelectedFaceSlot(int slot)
        => Edit("Set Face Material", (mesh, s) =>
        {
            foreach (GeometryData.Face face in s.Faces) face.SetAttribute(MeshGeometry.SubMesh, new GeometryData.IntAttributeValue(slot));
        });

    private static int SlotOf(GeometryData.Face face)
        => face.TryGetAttribute(MeshGeometry.SubMesh, out GeometryData.AttributeValue? value) && value is GeometryData.IntAttributeValue n && n.Data.Length > 0 ? n.Data[0] : 0;

    /// <summary>Moves the origin of each edited object with something selected to the middle of its selected points.</summary>
    private void OriginToSelection()
    {
        foreach (ModelObject model in _edited)
        {
            ElementSelection selection = _selection[model];
            if (selection.Vertices.Count == 0) continue;
            Float3 centre = Float3.Zero;
            foreach (GeometryData.Vertex vertex in selection.Vertices) centre += vertex.Point;
            ModelEditingMode.MoveOrigin(model, centre / selection.Vertices.Count, "Origin To Selection");
        }
    }

    private static void MergeAtCentre(GeometryData mesh, ElementSelection selection)
    {
        if (selection.Vertices.Count < 2) return;
        Float3 centre = Float3.Zero;
        foreach (GeometryData.Vertex vertex in selection.Vertices) centre += vertex.Point;
        Operators.Merge(mesh, [.. selection.Vertices], centre / selection.Vertices.Count, selection);
    }

    private static void Bridge(GeometryData mesh, ElementSelection selection)
    {
        if (selection.Faces.Count != 2) return;
        GeometryData.Face[] ends = [.. selection.Faces];
        Operators.Bridge(mesh, ends[0], ends[1], 1, selection);
    }

    private static void Bevel(GeometryData mesh, ElementSelection selection, float width, int segments)
    {
        if (selection.Edges.Count > 0) Operators.Bevel(mesh, [.. selection.Edges], width, segments, selection);
        else Operators.BevelPoints(mesh, [.. selection.Vertices], width, selection);
    }

    private static void Delete(GeometryData mesh, ElementSelection selection)
    {
        switch (selection)
        {
            case { Faces.Count: > 0 }: GeometryOperators.DeleteFaces(mesh, [.. selection.Faces]); break;
            case { Edges.Count: > 0 }: GeometryOperators.DeleteEdges(mesh, [.. selection.Edges]); break;
            default: GeometryOperators.DeleteVertices(mesh, [.. selection.Vertices]); break;
        }
        selection.Clear();
    }

    // ================================================================
    //  Edits, recorded for undo with the selection
    // ================================================================

    /// <summary>
    /// Runs an edit on every edited object's mesh with its selection, as one undo step that keeps the selection, and
    /// shows the result. <paramref name="selects"/> names the set the edit fills with what it selects after.
    /// </summary>
    private void Edit(string name, Action<GeometryData, ElementSelection> edit, SelectMode? selects = null)
    {
        _trees.Clear();
        HoldSelection();
        Undo.RecordObject(_held, UndoOwner.OfSession, name);
        foreach (ModelObject model in _edited)
        {
            Undo.RecordObject(model, name);
            var (points, faces) = Before(model.Mesh);
            try
            {
                edit(model.Mesh, _selection[model]);
                Operators.FinishEdit(model.Mesh, points, faces);
            }
            catch (InvalidOperationException e)
            {
                CSGStatus.Flash(e.Message);
            }
            ElementSelection selection = _selection[model];
            if (selects is { } lead) Selecting.ToMode(selection, lead, Mode);
            Selecting.Without(selection, _hidden[model], Mode);
            model.Mesh.MarkChanged();
            model.RefreshPreview();
        }
        HoldSelection();
    }

    private static (Dictionary<GeometryData.Vertex, Float3> Points, HashSet<GeometryData.Face> Faces) Before(GeometryData mesh)
    {
        var points = new Dictionary<GeometryData.Vertex, Float3>(mesh.Vertices.Count);
        foreach (GeometryData.Vertex vertex in mesh.Vertices) points[vertex] = vertex.Point;
        return (points, new HashSet<GeometryData.Face>(mesh.Faces));
    }

    /// <summary>The selection by index, which is what survives the mesh being put back by undo.</summary>
    private void HoldSelection()
    {
        _held.Parts.Clear();
        foreach (ModelObject model in _edited) _held.Parts.Add(Indices(model));
    }

    private void RestoreHeld()
    {
        foreach (ModelObject model in _edited)
            _selection[model] = _held.Parts.FirstOrDefault(p => p.Object == model.Id) is { } part ? FromIndices(model.Mesh, part) : new ElementSelection();
    }

    // ================================================================
    //  Operators with an amount
    // ================================================================

    private void StartOperator(SceneToolContext ctx, AmountOperator op)
    {
        _start.Clear();
        foreach (ModelObject model in _edited)
            _start[model] = (model.Mesh.Capture(), Indices(model));
        _operator = op;
        _operatorFrom = ctx.Handles.MousePosition;
        _operatorCount = op.StartCount;
        _operatorKey = float.NaN;
        ctx.Handles.Modal = true;
        _operatorHandles = ctx.Handles;
    }

    private void TickOperator(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;
        AmountOperator op = _operator!;

        ControlID owner = handles.GetControlID(ModalControl);
        handles.AddControl(owner, HandleContext.BodyDistance, float.MaxValue - 1f);

        float wheel = Input.MouseWheelDelta;
        if (wheel != 0f && op.MaxCount > 1) _operatorCount = Math.Clamp(_operatorCount + (wheel > 0f ? 1 : -1), 1, op.MaxCount);

        float pixels = Float2.Distance(handles.MousePosition, _operatorFrom);
        float amount = op.Along is { } along ? Along(handles.MousePosition - _operatorFrom, along) : op.Amount(op.Distance ? pixels * WorldPerPixel(ctx) : pixels);
        if (op.Distance && SceneTools.GridSnap != handles.Ctrl && SceneTools.MoveSnap > 0f) amount = MathF.Round(amount / SceneTools.MoveSnap) * SceneTools.MoveSnap;

        float key = amount + _operatorCount * 100000f;
        if (key != _operatorKey)
        {
            _operatorKey = key;
            RunFromStart(op, amount);
        }
        string count = op.MaxCount > 1 ? $"  {_operatorCount}" : "";
        ctx.Draw.Label(handles.MouseRay.Origin + handles.MouseRay.Direction * 5f, amount.ToString(op.Format) + count, PickedColor);

        if (handles.GetKeyDown(KeyCode.Escape) || handles.ViewportHovered && Input.GetMouseButtonDown(1))
        {
            EndOperator();
            RestoreStart();
            _start.Clear();
            return;
        }
        bool confirm = handles.GetKeyDown(KeyCode.Enter) || handles.IsNearest(owner) && handles.TryBeginDrag(owner);
        if (!confirm) return;
        handles.TryEndDrag(owner);

        // Back to the start, then once more as one undo step
        EndOperator();
        RestoreStart();
        _start.Clear();
        RunAsLast(op, amount, _operatorCount);
    }

    private void RunFromStart(AmountOperator op, float amount)
    {
        foreach ((ModelObject model, (GeometrySnapshot mesh, ModelEditPart selection)) in _start)
        {
            model.Mesh.Restore(mesh);
            ElementSelection running = FromIndices(model.Mesh, selection);
            try
            {
                op.Run(model.Mesh, running, amount, _operatorCount);
            }
            catch (InvalidOperationException)
            {
                model.Mesh.Restore(mesh);
                running = FromIndices(model.Mesh, selection);
            }
            if (op.Selects is { } lead) Selecting.ToMode(running, lead, Mode);
            _selection[model] = running;
            model.RefreshPreview();
        }
    }

    private void EndOperator()
    {
        _operator = null;
        if (_operatorHandles != null) _operatorHandles.Modal = false;
        _operatorHandles = null;
    }

    /// <summary>World units one pixel spans at the selection, so amounts scale with the view.</summary>
    private float WorldPerPixel(SceneToolContext ctx)
    {
        Float3 centre = Float3.Zero;
        int count = 0;
        foreach (ModelObject model in _edited)
        {
            Float4x4 toWorld = model.Transform.LocalToWorldMatrix;
            foreach (GeometryData.Vertex vertex in _selection[model].Vertices)
            {
                centre += Float4x4.TransformPoint(vertex.Point, toWorld);
                count++;
            }
        }
        if (count > 0) centre /= count;
        if (ctx.Handles.WorldToScreen(centre) is not { } a || ctx.Handles.WorldToScreen(centre + ctx.Camera.GameObject.Transform.Right) is not { } b) return 0.01f;
        float pixels = Float2.Distance(a, b);
        return pixels > 1e-3f ? 1f / pixels : 0.01f;
    }

    private ModelEditPart Indices(ModelObject model)
    {
        GeometryData mesh = model.Mesh;
        ElementSelection selection = _selection[model];
        return new ModelEditPart
        {
            Object = model.Id,
            Vertices = [.. selection.Vertices.Where(mesh.Contains).Select(v => v.Index)],
            Edges = [.. selection.Edges.Where(mesh.Contains).SelectMany(e => new[] { e.Vert1.Index, e.Vert2.Index })],
            Faces = [.. selection.Faces.Where(mesh.Contains).Select(f => f.Index)],
        };
    }

    private ElementSelection FromIndices(GeometryData mesh, ModelEditPart part)
    {
        var selection = new ElementSelection();
        foreach (int i in part.Vertices)
            if (i >= 0 && i < mesh.Vertices.Count) selection.Vertices.Add(mesh.Vertices[i]);
        for (int i = 0; i + 1 < part.Edges.Length; i += 2)
            if (part.Edges[i + 1] < mesh.Vertices.Count && mesh.FindEdge(mesh.Vertices[part.Edges[i]], mesh.Vertices[part.Edges[i + 1]]) is { } edge)
                selection.Edges.Add(edge);
        foreach (int i in part.Faces)
            if (i >= 0 && i < mesh.Faces.Count) selection.Faces.Add(mesh.Faces[i]);
        Selecting.Normalize(selection, Mode);
        return selection;
    }

    // ================================================================
    //  Transforms
    // ================================================================

    private TransformSession? StartSession()
    {
        var parts = new List<(EditTarget, ElementSelection)>();
        foreach (ModelObject model in _edited)
            if (_selection[model].Vertices.Count > 0)
                parts.Add((new EditTarget(model.Mesh, model.Transform.LocalToWorldMatrix), _selection[model]));
        if (parts.Count == 0) return null;
        _session = new TransformSession(parts, Proportional ? ProportionalRadius : 0f) { Individual = SceneTools.Pivot == PivotMode.Individual };
        _sessionMove = null;
        return _session;
    }

    private void PreviewSession(System.Action move)
    {
        _session!.Restore();
        move();
        _sessionMove = move;
        foreach (ModelObject model in _edited) model.RefreshPreview(dragging: true);
    }

    /// <summary>Back to where the points started, then the whole move once, as one undo step.</summary>
    private void CommitSession(string name)
    {
        if (_session is not { } session) return;
        System.Action? move = _sessionMove;
        session.Restore();
        _session = null;
        _sessionMove = null;
        if (move == null)
        {
            foreach (ModelObject model in _edited) model.RefreshPreview();
            return;
        }
        HoldSelection();
        Undo.RecordObject(_held, UndoOwner.OfSession, name);
        var before = new Dictionary<ModelObject, (Dictionary<GeometryData.Vertex, Float3>, HashSet<GeometryData.Face>)>();
        foreach (ModelObject model in _edited)
        {
            Undo.RecordObject(model, name);
            before[model] = Before(model.Mesh);
        }
        move();
        foreach (ModelObject model in _edited)
        {
            (Dictionary<GeometryData.Vertex, Float3> points, HashSet<GeometryData.Face> faces) = before[model];
            Operators.FinishEdit(model.Mesh, points, faces);
            model.Mesh.MarkChanged();
            model.RefreshPreview();
        }
    }

    private void CancelSession()
    {
        if (_session == null) return;
        _session.Restore();
        _session = null;
        _sessionMove = null;
        foreach (ModelObject model in _edited) model.RefreshPreview();
    }

    /// <summary>The rotation of the first edited object with something selected, which Local orientation follows.</summary>
    private Quaternion LocalFrame()
        => _edited.Find(m => _selection[m].Vertices.Count > 0) is { } model ? model.Transform.Rotation : Quaternion.Identity;

    /// <summary>A frame whose Z axis is the selection's average normal in the world, or null when it has none.</summary>
    private Quaternion? NormalFrame()
    {
        Float3 sum = Float3.Zero;
        foreach (ModelObject model in _edited)
        {
            ElementSelection selection = _selection[model];
            IEnumerable<GeometryData.Face> faces = selection.Faces.Count > 0 ? selection.Faces
                : selection.Edges.Count > 0 ? selection.Edges.SelectMany(e => e.NeighborFaces()).Distinct()
                : selection.Vertices.SelectMany(v => v.NeighborFaces()).Distinct();
            Float3 local = Float3.Zero;
            foreach (GeometryData.Face face in faces) local += face.Normal();
            sum += Float4x4.TransformNormal(local, model.Transform.LocalToWorldMatrix);
        }
        if (Float3.LengthSquared(sum) < 1e-10f) return null;
        Float3 normal = Float3.Normalize(sum);
        return Quaternion.LookRotation(normal, MathF.Abs(normal.Y) < 0.99f ? Float3.UnitY : Float3.UnitX);
    }

    private void StartModal(SceneToolContext ctx, TransformKind kind)
    {
        if (StartSession() is not { } session) return;
        _modal = new TransformModal(kind, session, LocalFrame(), NormalFrame())
        {
            Snap = SnapToObjects ? mouse => FindSnap(ctx.Handles, mouse) : null,
        };
        ctx.Handles.Modal = true;
        _modalHandles = ctx.Handles;
    }

    private void TickModal(SceneToolContext ctx)
    {
        TransformModal modal = _modal!;
        if (modal.Kind == TransformKind.Translate && ctx.Handles.GetKeyDown(KeyCode.G) && StartSlide(ctx)) return;
        if (Proportional && _session != null)
        {
            float wheel = Input.MouseWheelDelta;
            if (wheel != 0f)
            {
                ProportionalRadius = Math.Clamp(ProportionalRadius * MathF.Pow(1.15f, wheel), 0.01f, 1000f);
                _session.Falloff = ProportionalRadius;
            }
            ctx.Draw.Circle(_session.Pivot, ctx.Camera.GameObject.Transform.Forward, ProportionalRadius, new Color32(255, 255, 255, 150), 1f);
        }
        ModalState state = modal.Tick(ctx);
        ctx.Draw.Label(_session!.Pivot, modal.Status, PickedColor);
        if (state == ModalState.Running)
        {
            PreviewSession(modal.Replay);
            return;
        }

        EndModal();
        if (state == ModalState.Cancelled) CancelSession();
        else CommitSession(modal.Kind switch
        {
            TransformKind.Translate => "Move Model Points",
            TransformKind.Rotate => "Rotate Model Points",
            _ => "Scale Model Points",
        });
    }

    /// <summary>
    /// G again while grabbing: one selected point slides along one of its edges, selected edges slide along the edges
    /// beside them. The grab is put back first.
    /// </summary>
    private bool StartSlide(SceneToolContext ctx)
    {
        var points = _edited.Where(m => _selection[m].Vertices.Count > 0).ToList();
        if (points.Count != 1) return false;
        ModelObject model = points[0];
        ElementSelection selection = _selection[model];

        if (Mode == SelectMode.Vertex && selection.Vertices.Count == 1)
        {
            GeometryData.Vertex point = selection.Vertices.First();
            Float4x4 toWorld = model.Transform.LocalToWorldMatrix;
            var ends = new List<Float3>();
            foreach (GeometryData.Edge edge in point.NeighborEdges())
                if (!_hidden[model].Edges.Contains(edge)) ends.Add(Float4x4.TransformPoint(edge.OtherVertex(point).Point, toWorld));
            if (ends.Count == 0) return false;
            _session!.Restore();
            _sessionMove = null;
            _modal = new TransformModal(TransformKind.Slide, _session, LocalFrame(), NormalFrame()) { SlideEnds = ends };
            return true;
        }

        if (Mode == SelectMode.Edge && selection.Edges.Count > 0)
        {
            EndModal();
            CancelSession();
            StartOperator(ctx, new AmountOperator("Slide Model Edges", false, f => f, "0.###", (mesh, s, f, _) => Operators.SlideEdges(mesh, [.. s.Edges], f), SelectMode.Edge)
            {
                Along = SlideDirection(ctx, model),
            });
            return true;
        }
        return false;
    }

    /// <summary>Which way on screen the edges slide at +1, found by trying a slide on a copy.</summary>
    private Float2 SlideDirection(SceneToolContext ctx, ModelObject model)
    {
        var copy = new GeometryData();
        copy.Restore(model.Mesh.Capture());
        ModelEditPart part = Indices(model);
        ElementSelection selection = FromIndices(copy, part);
        if (selection.Edges.Count == 0) return Float2.Zero;
        GeometryData.Edge first = selection.Edges.First();
        GeometryData.Vertex end = first.Vert1;
        Float3 from = end.Point;
        Operators.SlideEdges(copy, [.. selection.Edges], 1f);
        Float4x4 toWorld = model.Transform.LocalToWorldMatrix;
        if (ctx.Handles.WorldToScreen(Float4x4.TransformPoint(from, toWorld)) is { } a && ctx.Handles.WorldToScreen(Float4x4.TransformPoint(end.Point, toWorld)) is { } b)
            return b - a;
        return Float2.Zero;
    }

    // ================================================================
    //  Loop cut
    // ================================================================

    private void StartLoopCut(SceneToolContext ctx)
    {
        _start.Clear();
        foreach (ModelObject model in _edited) _start[model] = (model.Mesh.Capture(), Indices(model));
        _loopCut = true;
        _loopEdge = null;
        _loopPicked = false;
        _loopCuts = 1;
        _operatorKey = float.NaN;
        ctx.Handles.Modal = true;
        _operatorHandles = ctx.Handles;
    }

    /// <summary>
    /// Before a click the ring of the edge under the mouse shows cut, the wheel setting how many. The click keeps the
    /// ring, and the mouse then slides the cuts until a second click keeps them.
    /// </summary>
    private void TickLoopCut(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;
        ControlID owner = handles.GetControlID(ModalControl);
        handles.AddControl(owner, HandleContext.BodyDistance, float.MaxValue - 1f);

        float wheel = Input.MouseWheelDelta;
        if (wheel != 0f) _loopCuts = Math.Clamp(_loopCuts + (wheel > 0f ? 1 : -1), 1, 64);

        RestoreStart();
        if (!_loopPicked) _loopEdge = NearestEdge(ctx);

        float slide = 0f;
        if (_loopPicked && _loopEdge is { } held && EdgeOf(held) is { } ring)
        {
            Float4x4 toWorld = held.Model.Transform.LocalToWorldMatrix;
            if (handles.WorldToScreen(Float4x4.TransformPoint(ring.Vert1.Point, toWorld)) is { } a && handles.WorldToScreen(Float4x4.TransformPoint(ring.Vert2.Point, toWorld)) is { } b)
                slide = Along((handles.MousePosition - _operatorFrom) * 2f, b - a);
        }
        if (_loopEdge is { } picked && EdgeOf(picked) is { } edge)
        {
            try
            {
                Operators.LoopCut(picked.Model.Mesh, edge, _loopCuts, slide, _selection[picked.Model]);
            }
            catch (InvalidOperationException)
            {
                RestoreStart();
            }
            picked.Model.RefreshPreview();
        }
        ctx.Draw.Label(handles.MouseRay.Origin + handles.MouseRay.Direction * 5f, Loc.Get("csg.cuts", new { count = _loopCuts }), PickedColor);

        if (handles.GetKeyDown(KeyCode.Escape) || handles.ViewportHovered && Input.GetMouseButtonDown(1))
        {
            RestoreStart();
            EndLoopCut();
            return;
        }
        if (!handles.IsNearest(owner) || !handles.TryBeginDrag(owner)) return;
        handles.TryEndDrag(owner);
        if (!_loopPicked)
        {
            if (_loopEdge == null) return;
            _loopPicked = true;
            _operatorFrom = handles.MousePosition;
            return;
        }

        (ModelObject Model, int A, int B)? chosen = _loopEdge;
        int cuts = _loopCuts;
        RestoreStart();
        EndLoopCut();
        if (chosen is not { } kept) return;
        Edit("Loop Cut Model", (mesh, s) =>
        {
            if (!ReferenceEquals(mesh, kept.Model.Mesh) || EdgeOf(kept) is not { } cut) return;
            Operators.LoopCut(mesh, cut, cuts, slide, s);
        }, SelectMode.Edge);
    }

    private void EndLoopCut()
    {
        _loopCut = false;
        _start.Clear();
        if (_operatorHandles != null) _operatorHandles.Modal = false;
        _operatorHandles = null;
    }

    private void RestoreStart()
    {
        foreach ((ModelObject model, (GeometrySnapshot mesh, ModelEditPart selection)) in _start)
        {
            model.Mesh.Restore(mesh);
            _selection[model] = FromIndices(model.Mesh, selection);
            model.RefreshPreview();
        }
    }

    /// <summary>How far a mouse travel goes along a screen direction, from -1 to 1 at its full length.</summary>
    private static float Along(Float2 travel, Float2 direction)
    {
        float length = Float2.Dot(direction, direction);
        return length > 1e-6f ? Math.Clamp(Float2.Dot(travel, direction) / length, -1f, 1f) : 0f;
    }

    private static GeometryData.Edge? EdgeOf((ModelObject Model, int A, int B) at)
    {
        GeometryData mesh = at.Model.Mesh;
        return at.A < mesh.Vertices.Count && at.B < mesh.Vertices.Count ? mesh.FindEdge(mesh.Vertices[at.A], mesh.Vertices[at.B]) : null;
    }

    /// <summary>The shown edge nearest the mouse on screen, within reach and in sight, as its object and point indices.</summary>
    private (ModelObject Model, int A, int B)? NearestEdge(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;
        (ModelObject, int, int)? best = null;
        float bestDistance = PickRadius * 2f;
        foreach (ModelObject model in _edited)
        {
            Float4x4 toWorld = model.Transform.LocalToWorldMatrix;
            foreach (GeometryData.Edge edge in model.Mesh.Edges)
            {
                if (_hidden[model].Edges.Contains(edge) || edge.IsHidden) continue;
                Float3 a = Float4x4.TransformPoint(edge.Vert1.Point, toWorld), b = Float4x4.TransformPoint(edge.Vert2.Point, toWorld);
                if (handles.WorldToScreen(a) is not { } from || handles.WorldToScreen(b) is not { } to) continue;
                Float2 along = to - from;
                float lengthSq = Float2.Dot(along, along);
                float t = lengthSq > 1e-6f ? Math.Clamp(Float2.Dot(handles.MousePosition - from, along) / lengthSq, 0f, 1f) : 0f;
                float distance = Float2.Distance(handles.MousePosition, from + along * t);
                if (distance >= bestDistance || !Seen(ctx, a + (b - a) * t)) continue;
                bestDistance = distance;
                best = (model, edge.Vert1.Index, edge.Vert2.Index);
            }
        }
        return best;
    }

    // ================================================================
    //  Knife
    // ================================================================

    /// <summary>Starts the knife on the edited object under the mouse: clicks place points, Backspace takes one back, Enter cuts.</summary>
    private void StartKnife(SceneToolContext ctx)
    {
        ModelObject? under = null;
        float nearest = float.MaxValue;
        foreach (ModelObject model in _edited)
            if (Tree(model.Mesh).Raycast(model.Transform.LocalToWorldMatrix, ctx.Handles.MouseRay, out _, out float distance) && distance < nearest)
            {
                nearest = distance;
                under = model;
            }
        _knifeModel = under.IsValid() ? under : _edited.Find(m => _selection[m].Vertices.Count > 0) is { } selected ? selected : _edited[0];
        _knife.Clear();
        ctx.Handles.Modal = true;
        _operatorHandles = ctx.Handles;
    }

    private void TickKnife(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;
        if (_knifeModel is not { } model || model.IsNotValid())
        {
            EndKnife();
            return;
        }
        ControlID owner = handles.GetControlID(ModalControl);
        handles.AddControl(owner, HandleContext.BodyDistance, float.MaxValue - 1f);
        (Float2 pixel, KnifePoint landed) = Land(ctx, model);

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
        if (handles.GetKeyDown(KeyCode.Escape) || handles.ViewportHovered && Input.GetMouseButtonDown(1))
        {
            EndKnife();
            return;
        }
        if (handles.GetKeyDown(KeyCode.Enter) || handles.GetKeyDown(KeyCode.KeypadEnter))
        {
            Cut(ctx, model);
            EndKnife();
            return;
        }
        if (!handles.IsNearest(owner) || !handles.TryBeginDrag(owner)) return;
        handles.TryEndDrag(owner);
        _knife.Add((pixel, landed));
    }

    private void EndKnife()
    {
        _knifeModel = null;
        _knife.Clear();
        if (_operatorHandles != null) _operatorHandles.Modal = false;
        _operatorHandles = null;
    }

    /// <summary>What of the object the mouse is on: a point near it, else an edge near it, else the face under it, and where that is on screen.</summary>
    private (Float2 Pixel, KnifePoint Point) Land(SceneToolContext ctx, ModelObject model)
    {
        HandleContext handles = ctx.Handles;
        var point = new KnifePoint();
        Float4x4 toWorld = model.Transform.LocalToWorldMatrix, toLocal = toWorld.Invert();
        Float2 mouse = handles.MousePosition;
        ElementSelection hidden = _hidden[model];

        GeometryData.Vertex? nearest = null;
        float best = PickRadius;
        foreach (GeometryData.Vertex vertex in model.Mesh.Vertices)
        {
            if (hidden.Vertices.Contains(vertex)) continue;
            Float3 world = Float4x4.TransformPoint(vertex.Point, toWorld);
            if (handles.WorldToScreen(world) is not { } at || Float2.Distance(at, mouse) >= best || !Seen(ctx, world)) continue;
            best = Float2.Distance(at, mouse);
            nearest = vertex;
        }
        if (nearest != null)
        {
            point.Vertex = nearest;
            point.Point = nearest.Point;
            return (handles.WorldToScreen(Float4x4.TransformPoint(nearest.Point, toWorld)) ?? mouse, point);
        }

        Ray sight = handles.MouseRay;
        GeometryData.Edge? near = null;
        float nearT = 0f;
        best = PickRadius;
        foreach (GeometryData.Edge edge in model.Mesh.Edges)
        {
            if (hidden.Edges.Contains(edge) || edge.IsHidden) continue;
            Float3 a = Float4x4.TransformPoint(edge.Vert1.Point, toWorld), b = Float4x4.TransformPoint(edge.Vert2.Point, toWorld);
            if (handles.WorldToScreen(a) is not { } from || handles.WorldToScreen(b) is not { } to) continue;
            Float2 along = to - from;
            float lengthSq = Float2.Dot(along, along);
            float t = lengthSq > 1e-6f ? Math.Clamp(Float2.Dot(mouse - from, along) / lengthSq, 0f, 1f) : 0f;
            float distance = Float2.Distance(mouse, from + along * t);
            if (distance >= best || !Seen(ctx, a + (b - a) * t)) continue;
            best = distance;
            near = edge;
            nearT = t;
        }
        if (near != null)
        {
            Float3 a = Float4x4.TransformPoint(near.Vert1.Point, toWorld), run = Float4x4.TransformPoint(near.Vert2.Point, toWorld) - a, toStart = a - sight.Origin;
            float runRun = Float3.Dot(run, run), runSight = Float3.Dot(run, sight.Direction), sightSight = Float3.Dot(sight.Direction, sight.Direction);
            float span = runRun * sightSight - runSight * runSight;
            float t = span > 1e-12f ? Math.Clamp((runSight * Float3.Dot(sight.Direction, toStart) - sightSight * Float3.Dot(run, toStart)) / span, 0f, 1f) : nearT;
            point.Edge = near;
            point.Point = near.Vert1.Point + (near.Vert2.Point - near.Vert1.Point) * t;
            return (handles.WorldToScreen(a + run * t) ?? mouse, point);
        }

        if (Tree(model.Mesh).Raycast(toWorld, sight, out GeometryData.Face? face, out float hit, hidden.Faces.Count > 0 ? hidden.Faces.Contains : null))
        {
            point.Face = face;
            point.Point = Float4x4.TransformPoint(sight.Origin + sight.Direction * hit, toLocal);
        }
        return (mouse, point);
    }

    /// <summary>Cuts along the knife's points, each stretch in the plane of the lines of sight through its ends.</summary>
    private void Cut(SceneToolContext ctx, ModelObject model)
    {
        if (_knife.Count < 2) return;
        HandleContext handles = ctx.Handles;
        Camera camera = ctx.Camera;
        Float4x4 toWorld = model.Transform.LocalToWorldMatrix, toLocal = toWorld.Invert();
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

        var pixels = _knife.Select(k => k.Pixel).ToList();
        var points = _knife.Select(k => k.Point).ToList();
        float? Along(int stretch, Float3 local)
        {
            Float2 a = pixels[stretch], run = pixels[stretch + 1] - a;
            float lengthSq = Float2.Dot(run, run);
            Float3 world = Float4x4.TransformPoint(local, toWorld);
            if (lengthSq < 4f || handles.WorldToScreen(world) is not { } at) return null;
            float t = Float2.Dot(at - a, run) / lengthSq;
            if (t < -0.002f || t > 1.002f || !Seen(ctx, world)) return null;
            return Math.Clamp(t, 0f, 1f);
        }

        ElementSelection hidden = _hidden[model];
        Edit("Knife Model", (mesh, s) =>
        {
            if (!ReferenceEquals(mesh, model.Mesh)) return;
            Operators.Knife(mesh, points, planes, Along, f => !hidden.Faces.Contains(f), s);
        }, SelectMode.Edge);
    }

    private void CancelModal()
    {
        if (_modal == null) return;
        EndModal();
        CancelSession();
    }

    private void EndModal()
    {
        _modal = null;
        if (_modalHandles != null) _modalHandles.Modal = false;
        _modalHandles = null;
    }

    /// <summary>The move gizmo at the middle of the selected points, moving them through a session.</summary>
    private void MoveGizmo(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;
        Float3 sum = Float3.Zero;
        int count = 0;
        foreach (ModelObject model in _edited)
        {
            Float4x4 toWorld = model.Transform.LocalToWorldMatrix;
            foreach (GeometryData.Vertex vertex in _selection[model].Vertices)
            {
                sum += Float4x4.TransformPoint(vertex.Point, toWorld);
                count++;
            }
        }
        if (count == 0)
        {
            _gizmoDragging = false;
            return;
        }

        Float3 centre = sum / count, before = centre;
        Quaternion axes = SceneTools.Orientation switch
        {
            PivotOrientation.Global => Quaternion.Identity,
            PivotOrientation.Normal => NormalFrame() ?? LocalFrame(),
            _ => LocalFrame(),
        };
        bool moved = TransformHandles.PositionHandle(handles, MoveHandle, ref centre, axes, out bool hot);
        if (hot && !_gizmoDragging)
        {
            _gizmoRaw = _gizmoApplied = Float3.Zero;
            _gizmoStart = before;
        }
        if (_gizmoDragging && !hot) CommitSession("Move Model Points");
        _gizmoDragging = hot;
        if (!moved) return;

        _gizmoRaw += centre - before;
        _gizmoApplied = SceneTools.GridSnap != handles.Ctrl ? Round(_gizmoRaw, SceneTools.MoveSnap) : _gizmoRaw;
        if ((_session ?? StartSession()) is not { } session) return;
        Float3 total = _gizmoApplied;
        PreviewSession(() => session.Translate(total));
    }

    private static Float3 Round(Float3 value, float step)
        => step > 0f ? new Float3(MathF.Round(value.X / step) * step, MathF.Round(value.Y / step) * step, MathF.Round(value.Z / step) * step) : value;

    // ================================================================
    //  Picking
    // ================================================================

    /// <summary>The nearest distance along a ray to a shown face of any edited object.</summary>
    // The picking tree of each mesh looked at this frame, found again next frame in case points moved
    private readonly Dictionary<GeometryData, MeshBvh> _trees = new();

    private MeshBvh Tree(GeometryData mesh)
    {
        if (!_trees.TryGetValue(mesh, out MeshBvh? tree)) _trees[mesh] = tree = MeshBvh.For(mesh);
        return tree;
    }

    private float NearestHit(Ray ray)
    {
        float nearest = float.MaxValue;
        foreach (ModelObject model in _edited)
        {
            ElementSelection hidden = _hidden[model];
            if (Tree(model.Mesh).Raycast(model.Transform.LocalToWorldMatrix, ray, out _, out float distance, hidden.Faces.Count > 0 ? hidden.Faces.Contains : null))
                nearest = MathF.Min(nearest, distance);
        }
        return nearest;
    }

    private bool Seen(SceneToolContext ctx, Float3 world)
    {
        if (XRay) return true;
        Camera camera = ctx.Camera;
        Transform view = camera.GameObject.Transform;
        Float3 eye = camera.IsOrthographic ? world - view.Forward * 10000f : view.Position;
        return MeshPicking.Seen(world, eye, NearestHit);
    }

    private void Pick(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;
        if (_gizmoDragging) return;

        for (int o = 0; o < _edited.Count; o++)
        {
            ModelObject model = _edited[o];
            Float4x4 toWorld = model.Transform.LocalToWorldMatrix;
            ElementSelection hidden = _hidden[model];
            GeometryData mesh = model.Mesh;

            if (Mode == SelectMode.Face && Tree(mesh).Raycast(toWorld, handles.MouseRay, out GeometryData.Face? face, out float distance, hidden.Faces.Count > 0 ? hidden.Faces.Contains : null))
            {
                ControlID id = handles.GetControlID(FaceControl, o * 1_000_003 + face!.Index);
                handles.AddControl(id, HandleContext.BodyDistance, distance);
                if (handles.IsNearest(id) && handles.TryBeginDrag(id))
                {
                    // Faces joined by hidden edges are one face, so the whole of it is picked
                    Choose(handles, model, s => s.Faces, face);
                    foreach (GeometryData.Face part in face.LogicalFace())
                        if (part != face) Choose(handles, model, s => s.Faces, part, adding: true);
                    handles.TryEndDrag(id);
                    return;
                }
            }

            if (Mode == SelectMode.Edge)
            {
                foreach (GeometryData.Edge edge in mesh.Edges)
                {
                    if (hidden.Edges.Contains(edge)) continue;
                    Float3 a = Float4x4.TransformPoint(edge.Vert1.Point, toWorld), b = Float4x4.TransformPoint(edge.Vert2.Point, toWorld);
                    if (!Seen(ctx, (a + b) * 0.5f)) continue;
                    ControlID id = handles.GetControlID(EdgeControl, o * 1_000_003 + edge.Index);
                    handles.AddControl(id, a, b, PickRadius, 0f);
                    if (handles.IsNearest(id) && handles.TryBeginDrag(id))
                    {
                        Choose(handles, model, s => s.Edges, edge);
                        handles.TryEndDrag(id);
                        return;
                    }
                }
            }

            if (Mode == SelectMode.Vertex)
            {
                foreach (GeometryData.Vertex vertex in mesh.Vertices)
                {
                    if (hidden.Vertices.Contains(vertex)) continue;
                    Float3 p = Float4x4.TransformPoint(vertex.Point, toWorld);
                    if (!Seen(ctx, p)) continue;
                    ControlID id = handles.GetControlID(VertexControl, o * 1_000_003 + vertex.Index);
                    handles.AddControl(id, p, PickRadius);
                    if (handles.IsNearest(id) && handles.TryBeginDrag(id))
                    {
                        Choose(handles, model, s => s.Vertices, vertex);
                        handles.TryEndDrag(id);
                        return;
                    }
                }
            }
        }

        // Behind everything else, but ahead of scene picking, so a miss starts a box rather than leaving edit mode's objects
        ControlID marquee = handles.GetControlID(MarqueeControl);
        handles.AddControl(marquee, HandleContext.BodyDistance, float.MaxValue - 1f);
        if (handles.IsNearest(marquee) && handles.TryBeginDrag(marquee))
        {
            _marquee = true;
            _marqueeFrom = handles.MousePosition;
        }
    }

    /// <summary>Shift adds, Ctrl removes, a plain click replaces the selection of every edited object.</summary>
    private void Choose<T>(HandleContext handles, ModelObject model, Func<ElementSelection, HashSet<T>> set, T element, bool adding = false)
    {
        if (!adding && !handles.Shift && !handles.Ctrl)
            foreach (ModelObject other in _edited) _selection[other].Clear();
        ElementSelection selection = _selection[model];
        if (handles.Ctrl) set(selection).Remove(element);
        else set(selection).Add(element);
        Selecting.Normalize(selection, Mode);
    }

    private void TickMarquee(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;
        ctx.Draw.ScreenRect(_marqueeFrom, handles.MousePosition, Color32.FromArgb(40, 120, 170, 255), Color32.FromArgb(200, 140, 190, 255));
        if (handles.PrimaryHeld && !handles.PrimaryUp) return;
        _marquee = false;

        Float2 min = Maths.Min(_marqueeFrom, handles.MousePosition), max = Maths.Max(_marqueeFrom, handles.MousePosition);
        bool click = Float2.Distance(_marqueeFrom, handles.MousePosition) < DragThreshold;
        if (!handles.Shift && !handles.Ctrl)
            foreach (ModelObject model in _edited) _selection[model].Clear();
        if (click) return;

        bool Inside(Float3 world)
            => handles.WorldToScreen(world) is { } p && p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y && Seen(ctx, world);

        foreach (ModelObject model in _edited)
        {
            Float4x4 toWorld = model.Transform.LocalToWorldMatrix;
            ElementSelection selection = _selection[model], hidden = _hidden[model];
            switch (Mode)
            {
                case SelectMode.Vertex:
                    foreach (GeometryData.Vertex v in model.Mesh.Vertices)
                        if (!hidden.Vertices.Contains(v) && Inside(Float4x4.TransformPoint(v.Point, toWorld)))
                            Mark(selection.Vertices, v, handles.Ctrl);
                    break;
                case SelectMode.Edge:
                    foreach (GeometryData.Edge e in model.Mesh.Edges)
                        if (!hidden.Edges.Contains(e) && Inside(Float4x4.TransformPoint(e.Vert1.Point, toWorld)) && Inside(Float4x4.TransformPoint(e.Vert2.Point, toWorld)))
                            Mark(selection.Edges, e, handles.Ctrl);
                    break;
                case SelectMode.Face:
                    foreach (GeometryData.Face f in model.Mesh.Faces)
                    {
                        if (hidden.Faces.Contains(f) || !IsFirstOfItsFace(f)) continue;
                        List<GeometryData.Face> whole = f.LogicalFace();
                        if (Inside(Float4x4.TransformPoint(Centre(whole), toWorld)))
                            foreach (GeometryData.Face part in whole) Mark(selection.Faces, part, handles.Ctrl);
                    }
                    break;
            }
            Selecting.Normalize(selection, Mode);
        }
    }

    /// <summary>Whether a face stands for the face it is part of, joined by hidden edges: the one listed first.</summary>
    private static bool IsFirstOfItsFace(GeometryData.Face face)
    {
        foreach (GeometryData.Edge edge in face.NeighborEdges())
            if (edge.IsHidden) return face.LogicalFace().Min(f => f.Index) == face.Index;
        return true;
    }

    private static Float3 Centre(List<GeometryData.Face> faces)
    {
        Float3 sum = Float3.Zero;
        int count = 0;
        foreach (GeometryData.Face face in faces)
            foreach (GeometryData.Vertex vertex in face.NeighborVertices())
            {
                sum += vertex.Point;
                count++;
            }
        return count > 0 ? sum / count : sum;
    }

    private static void Mark<T>(HashSet<T> set, T item, bool remove)
    {
        if (remove) set.Remove(item);
        else set.Add(item);
    }

    // ================================================================
    //  Drawing
    // ================================================================

    /// <summary>
    /// Draws the edited meshes into the scene's gizmos, which the renderer draws in one batch and tests against the
    /// scene's depth, so the parts behind the surface show dashed without testing each against the mesh here.
    /// </summary>
    private void DrawElements(SceneToolContext ctx)
    {
        Prowl.Graphite.Rendering.GizmoBatch batch = Runtime.Resources.Scene.Active.Gizmos.Batch;
        foreach (ModelObject model in _edited)
        {
            Float4x4 toWorld = model.Transform.LocalToWorldMatrix;
            ElementSelection selection = _selection[model], hidden = _hidden[model];

            foreach (GeometryData.Face face in selection.Faces)
            {
                if (hidden.Faces.Contains(face)) continue;
                foreach ((GeometryData.Loop a, GeometryData.Loop b, GeometryData.Loop c) in UVs.Triangles(face))
                    batch.Triangle(Float4x4.TransformPoint(a.Vert.Point, toWorld), Float4x4.TransformPoint(b.Vert.Point, toWorld),
                        Float4x4.TransformPoint(c.Vert.Point, toWorld), FaceFill);
            }

            foreach (GeometryData.Edge edge in model.Mesh.Edges)
            {
                if (hidden.Edges.Contains(edge) || edge.IsHidden) continue;
                bool picked = selection.Edges.Contains(edge), seam = UVs.IsSeam(edge);
                batch.Line(Float4x4.TransformPoint(edge.Vert1.Point, toWorld), Float4x4.TransformPoint(edge.Vert2.Point, toWorld),
                    picked ? PickedColor : seam ? SeamColor : IdleColor, picked || seam ? 2.5f : 1f);
            }

            if (Mode == SelectMode.Vertex)
            {
                foreach (GeometryData.Vertex vertex in model.Mesh.Vertices)
                {
                    if (hidden.Vertices.Contains(vertex)) continue;
                    bool picked = selection.Vertices.Contains(vertex);
                    batch.Point(Float4x4.TransformPoint(vertex.Point, toWorld), picked ? PickedColor : IdleColor, picked ? 8f : 6f);
                }
            }
            else if (Mode == SelectMode.Face)
            {
                foreach (GeometryData.Face face in model.Mesh.Faces)
                {
                    if (hidden.Faces.Contains(face) || !IsFirstOfItsFace(face)) continue;
                    bool picked = selection.Faces.Contains(face);
                    batch.Point(Float4x4.TransformPoint(Centre(face.LogicalFace()), toWorld), picked ? PickedColor : IdleColor, picked ? 7f : 5f);
                }
            }
        }
    }

    public override void OnDrawOverlay(SceneToolContext ctx, Quill.Canvas canvas)
    {
        if (Editing) TransformHandles.Draw(canvas);
    }
}

/// <summary>Model edit mode's selection by index, recorded with each edit so undo brings it back.</summary>
internal sealed class ModelEditSelection
{
    public List<ModelEditPart> Parts = new();
}

internal sealed class ModelEditPart
{
    public Guid Object;
    public int[] Vertices = [];
    public int[] Edges = [];
    public int[] Faces = [];
}
