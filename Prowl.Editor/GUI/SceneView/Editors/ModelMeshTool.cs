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

    /// <summary>
    /// An operator run with an amount from the mouse and a count from the wheel. A distance amount is the mouse's travel
    /// in world units at the selection and snaps to the grid, any other is the travel in pixels.
    /// </summary>
    private sealed record AmountOperator(string Name, bool Distance, Func<float, float> Amount, string Format,
                                         Action<GeometryData, ElementSelection, float, int> Run, SelectMode? Selects, int StartCount = 1, int MaxCount = 1);

    public SelectMode Mode { get; private set; } = SelectMode.Vertex;
    public bool XRay { get; set; }

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
            split.RefreshPreview();
            Undo.RegisterCreatedObjectUndo(go, Name);
        }
        HoldSelection();
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
        HoldSelection();
        Undo.RecordObject(_held, UndoOwner.OfSession, name);
        foreach (ModelObject model in _edited)
        {
            Undo.RecordObject(model, name);
            try
            {
                edit(model.Mesh, _selection[model]);
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
        float amount = op.Amount(op.Distance ? pixels * WorldPerPixel(ctx) : pixels);
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
            foreach ((ModelObject model, (GeometrySnapshot mesh, ModelEditPart selection)) in _start)
            {
                model.Mesh.Restore(mesh);
                _selection[model] = FromIndices(model.Mesh, selection);
                model.RefreshPreview();
            }
            _start.Clear();
            return;
        }
        bool confirm = handles.GetKeyDown(KeyCode.Enter) || handles.IsNearest(owner) && handles.TryBeginDrag(owner);
        if (!confirm) return;
        handles.TryEndDrag(owner);

        // Back to the start, then once more as one undo step
        EndOperator();
        foreach ((ModelObject model, (GeometrySnapshot mesh, ModelEditPart selection)) in _start)
        {
            model.Mesh.Restore(mesh);
            _selection[model] = FromIndices(model.Mesh, selection);
        }
        _start.Clear();
        int final = _operatorCount;
        Edit(op.Name, (mesh, s) => op.Run(mesh, s, amount, final), op.Selects);
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
        _session = new TransformSession(parts) { Individual = SceneTools.Pivot == PivotMode.Individual };
        _sessionMove = null;
        return _session;
    }

    private void PreviewSession(System.Action move)
    {
        _session!.Restore();
        move();
        _sessionMove = move;
        foreach (ModelObject model in _edited) model.RefreshPreview();
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
        foreach (ModelObject model in _edited) Undo.RecordObject(model, name);
        move();
        foreach (ModelObject model in _edited)
        {
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

    private void StartModal(SceneToolContext ctx, TransformKind kind)
    {
        if (StartSession() is not { } session) return;
        _modal = new TransformModal(kind, session, Quaternion.Identity, null);
        ctx.Handles.Modal = true;
        _modalHandles = ctx.Handles;
    }

    private void TickModal(SceneToolContext ctx)
    {
        TransformModal modal = _modal!;
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
        bool moved = TransformHandles.PositionHandle(handles, MoveHandle, ref centre, Quaternion.Identity, out bool hot);
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
    private float NearestHit(Ray ray)
    {
        float nearest = float.MaxValue;
        foreach (ModelObject model in _edited)
        {
            ElementSelection hidden = _hidden[model];
            if (MeshPicking.Raycast(model.Mesh, model.Transform.LocalToWorldMatrix, ray, out _, out float distance, hidden.Faces.Count > 0 ? hidden.Faces.Contains : null))
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

            if (Mode == SelectMode.Face && MeshPicking.Raycast(mesh, toWorld, handles.MouseRay, out GeometryData.Face? face, out float distance, hidden.Faces.Count > 0 ? hidden.Faces.Contains : null))
            {
                ControlID id = handles.GetControlID(FaceControl, o * 1_000_003 + face!.Index);
                handles.AddControl(id, HandleContext.BodyDistance, distance);
                if (handles.IsNearest(id) && handles.TryBeginDrag(id))
                {
                    Choose(handles, model, s => s.Faces, face);
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
    private void Choose<T>(HandleContext handles, ModelObject model, Func<ElementSelection, HashSet<T>> set, T element)
    {
        if (!handles.Shift && !handles.Ctrl)
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
                        if (!hidden.Faces.Contains(f) && Inside(Float4x4.TransformPoint(f.Center(), toWorld)))
                            Mark(selection.Faces, f, handles.Ctrl);
                    break;
            }
            Selecting.Normalize(selection, Mode);
        }
    }

    private static void Mark<T>(HashSet<T> set, T item, bool remove)
    {
        if (remove) set.Remove(item);
        else set.Add(item);
    }

    // ================================================================
    //  Drawing
    // ================================================================

    private void DrawElements(SceneToolContext ctx)
    {
        foreach (ModelObject model in _edited)
        {
            Float4x4 toWorld = model.Transform.LocalToWorldMatrix;
            ElementSelection selection = _selection[model], hidden = _hidden[model];

            foreach (GeometryData.Face face in selection.Faces)
            {
                if (hidden.Faces.Contains(face)) continue;
                List<GeometryData.Vertex> ring = face.NeighborVertices();
                var corners = new Float3[ring.Count];
                for (int i = 0; i < ring.Count; i++) corners[i] = Float4x4.TransformPoint(ring[i].Point, toWorld);
                ctx.Draw.Polygon(corners, FaceFill);
            }

            foreach (GeometryData.Edge edge in model.Mesh.Edges)
            {
                if (hidden.Edges.Contains(edge) || edge.IsHidden) continue;
                bool picked = selection.Edges.Contains(edge);
                ctx.Draw.Line(Float4x4.TransformPoint(edge.Vert1.Point, toWorld), Float4x4.TransformPoint(edge.Vert2.Point, toWorld),
                    picked ? PickedColor : IdleColor, picked ? 2.5f : 1f);
            }

            if (Mode == SelectMode.Vertex)
            {
                foreach (GeometryData.Vertex vertex in model.Mesh.Vertices)
                {
                    if (hidden.Vertices.Contains(vertex)) continue;
                    bool picked = selection.Vertices.Contains(vertex);
                    Float3 p = Float4x4.TransformPoint(vertex.Point, toWorld);
                    if (!picked && !Seen(ctx, p)) continue;
                    ctx.Draw.Dot(p, picked ? PickedColor : IdleColor, picked ? 8f : 6f);
                }
            }
            else if (Mode == SelectMode.Face)
            {
                foreach (GeometryData.Face face in model.Mesh.Faces)
                {
                    if (hidden.Faces.Contains(face)) continue;
                    bool picked = selection.Faces.Contains(face);
                    Float3 p = Float4x4.TransformPoint(face.Center(), toWorld);
                    if (!picked && !Seen(ctx, p)) continue;
                    ctx.Draw.Dot(p, picked ? PickedColor : IdleColor, picked ? 7f : 5f);
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
