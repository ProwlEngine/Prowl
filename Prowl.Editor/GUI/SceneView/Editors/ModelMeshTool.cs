// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Modeling;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Editor.Theming;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Prowl.Unwrapper;

namespace Prowl.Editor.GUI.SceneView.Editors;

/// <summary>
/// Edit mode of the model world, which a model opens in. It edits the selected model objects, or all of them, at once:
/// each keeps its own mesh and transform, the selection spans them, and transforms move points of every object together
/// in world space. Tab, or the object button of the mode strip, goes back to moving whole objects.
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

    // A grab, rotate or scale of whole objects in object mode, with its undo record held open until it ends
    private TransformModal? _objectModal;
    private ObjectTransformSession? _objectSession;
    private object? _objectUndo;
    private bool _gizmoDragging;
    private Float3 _gizmoRaw, _gizmoApplied, _gizmoStart;
    private Quaternion _gizmoAxes = Quaternion.Identity, _gizmoTurn = Quaternion.Identity;
    private Float3 _gizmoScale = Float3.One;
    private bool _marquee;
    private Float2 _marqueeFrom;

    // An operator whose amount follows the mouse: the meshes and selections it started from, what it runs, and its count
    private AmountOperator? _operator;
    private readonly Dictionary<ModelObject, (GeometrySnapshot Mesh, ModelEditPart Selection)> _start = new();
    private Float2 _operatorFrom, _operatorVirtual, _operatorLast;
    private Float3 _operatorPivot, _operatorNormal;
    private int _operatorCount;
    private (float Amount, int Count)? _operatorKey;
    private (float Amount, int Count) _operatorGood;
    private string _operatorTyped = "";
    private HandleContext? _operatorHandles;

    // A loop cut: the object and edge of its ring by point indices, whether a click has picked it, and how many cuts
    private bool _loopCut;
    private (ModelObject Model, int A, int B)? _loopEdge;
    private int _loopCuts = 1;

    // The knife: the object it cuts, and where each point was clicked and what of the mesh it sits on
    private ModelObject? _knifeModel;
    private readonly List<(Float2 Pixel, KnifePoint Point)> _knife = new();

    /// <summary>How the mouse sets a distance operator's amount.</summary>
    private enum OperatorDrag
    {
        /// <summary>How much further from the middle of the selection the mouse is than where it started.</summary>
        AwayFromPivot,

        /// <summary>How far the mouse moves along the selection's normal on screen, either way.</summary>
        AlongNormal,

        /// <summary>How much nearer the middle of the selection the mouse is than where it started.</summary>
        TowardPivot,
    }

    /// <summary>
    /// An operator run with an amount from the mouse and a count from the wheel. A distance amount is in world units at
    /// the selection, measured as <see cref="Drag"/> says, and snaps to the grid. Any other is the mouse's travel in pixels.
    /// </summary>
    private sealed record AmountOperator(string Name, bool Distance, Func<float, float> Amount, string Format,
                                         Action<GeometryData, ElementSelection, float, int> Run, SelectMode? Selects, int StartCount = 1, int MaxCount = 1)
    {
        /// <summary>When set the amount is the mouse's travel along this screen direction, from -1 to 1, as for a slide.</summary>
        public Float2? Along { get; init; }

        public OperatorDrag Drag { get; init; }

        /// <summary>Further settings, by locale key and the value they start at, shown when adjusting the step after.</summary>
        public LastField[] Extras { get; init; } = [];

        /// <summary>How the operator runs given its extra settings, in place of <see cref="Run"/>.</summary>
        public Action<GeometryData, ElementSelection, float, int, float[]>? RunWithExtras { get; init; }

        public void Invoke(GeometryData mesh, ElementSelection selection, float amount, int count, float[] extras)
        {
            if (RunWithExtras != null) RunWithExtras(mesh, selection, amount, count, extras);
            else Run(mesh, selection, amount, count);
        }

        public float[] ExtraDefaults => [.. Extras.Select(e => e.Value)];
    }

    public SelectMode Mode { get; private set; } = SelectMode.Vertex;
    public bool XRay { get; set; }

    /// <summary>Moved points snap to the corners, edges and faces of every model object under the mouse.</summary>
    public bool SnapToObjects { get; set; }

    /// <summary>Unselected points near the selection move with it, less the further they are.</summary>
    public bool Proportional { get; set; }

    /// <summary>With a mirror modifier on, moved points on its plane stay there and the rest do not cross it.</summary>
    public bool MirrorClip { get; set; } = true;

    public enum EditGizmo { Move, Rotate, Scale, Off }

    /// <summary>Which gizmo the selected points show.</summary>
    public EditGizmo Gizmo { get; set; } = EditGizmo.Move;

    public enum SnapKind { Anything, Points, Edges, Faces }

    /// <summary>What a snapping move lands on.</summary>
    public SnapKind SnapTo { get; set; } = SnapKind.Anything;

    /// <summary>After every move, turn and scale the moved points land on the nearest other model, for retopology.</summary>
    public bool AutoShrinkwrap { get; set; }
    public float ProportionalRadius { get; set; } = 1f;

    /// <summary>Whether edit mode is on.</summary>
    public bool Editing => _edited.Count > 0;

    public override bool IsAvailable(SceneToolContext ctx) => ModelEditingMode.IsEditing;

    public override bool SuppressTransformGizmo => Editing;

    public override void OnDeactivated() => Leave();

    // ================================================================
    //  Entering and leaving
    // ================================================================

    // The model session edit mode last opened itself for, so opening a model starts in edit mode once
    private IsolatedSession? _enteredFor;

    /// <summary>Edit mode on the selected model objects, or on every one in the world when none is selected.</summary>
    private void Enter()
    {
        _edited.Clear();
        foreach (GameObject go in Selection.GetSelected<GameObject>())
            if (go.GetComponent<ModelObject>() is { } model && model.IsValid()) _edited.Add(model);
        if (_edited.Count == 0 && ModelEditingMode.World is { } world)
            foreach (GameObject go in world.Objects)
                if (go.GetComponent<ModelObject>() is { } model && model.IsValid() && !model.Hidden) _edited.Add(model);
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
        EndObjectModal(cancel: true);
        if (_loopCut)
        {
            RestoreStart();
            EndLoopCut();
        }
        if (!ReferenceEquals(_knifeModel, null)) EndKnife();
        if (!ReferenceEquals(_lineModel, null)) EndLine();
        if (!ReferenceEquals(_quadModel, null)) EndQuads();
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

        if (!Editing && !ReferenceEquals(_enteredFor, IsolatedWorld.Current))
        {
            _enteredFor = IsolatedWorld.Current;
            Enter();
        }
        if (_modal == null && _objectModal == null && handles.IsPressed("ModelEdit/Toggle"))
        {
            if (Editing) Leave();
            else Enter();
            return;
        }
        if (!Editing)
        {
            if (_objectModal != null) TickObjectModal(ctx);
            else ObjectKeys(ctx);
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
        if (!ReferenceEquals(_lineModel, null))
        {
            TickLine(ctx);
            DrawElements(ctx);
            return;
        }
        if (!ReferenceEquals(_quadModel, null))
        {
            TickQuads(ctx);
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
        SelectionGizmo(ctx);
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
        else if (handles.IsPressed("MeshEdit/XRay")) XRay = !XRay;
        else if (handles.IsPressed("MeshEdit/SnapToBrushes")) SnapToObjects = !SnapToObjects;
        else if (handles.IsPressed("MeshEdit/Proportional")) Proportional = !Proportional;
        else if (handles.IsPressed("MeshEdit/Inset")) Inset(ctx);
        else if (handles.IsPressed("MeshEdit/LoopCut")) StartLoopCut(ctx);
        else if (handles.IsPressed("MeshEdit/Knife")) StartKnife(ctx);
        else if (handles.IsPressed("MeshEdit/DrawLine")) StartLine(ctx);
        else if (handles.IsPressed("MeshEdit/DrawQuads")) StartQuads(ctx);
        else if (handles.IsPressed("MeshEdit/HideUnselected")) Hide(unselected: true);
        else if (handles.IsPressed("MeshEdit/Reveal")) Reveal();
        else if (handles.IsPressed("MeshEdit/Hide")) Hide(unselected: false);
        else if (handles.IsPressed("MeshEdit/Frame") || handles.IsPressed("Scene/FrameSelected")) Frame(ctx);
        else if (handles.IsPressed("MeshEdit/Fill")) Fill();
        else if (handles.IsPressed("MeshEdit/Grab")) StartModal(ctx, TransformKind.Translate);
        else if (handles.IsPressed("MeshEdit/Rotate")) StartModal(ctx, TransformKind.Rotate);
        else if (handles.IsPressed("MeshEdit/Scale")) StartModal(ctx, TransformKind.Scale);
        else if (handles.IsPressed("MeshEdit/Dissolve")) Dissolve();
        else if (handles.IsPressed("MeshEdit/DeleteMenu") || handles.IsPressed("Scene/Delete")) DeleteMenu(ctx);
        else if (handles.IsPressed("MeshEdit/Rip")) Rip(ctx);
        else if (handles.IsPressed("MeshEdit/Slide")) Slide(ctx);
        else if (handles.IsPressed("MeshEdit/Crease")) Crease(ctx);
        else if (handles.IsPressed("MeshEdit/Duplicate")) Duplicate(ctx);
        else if (handles.IsPressed("MeshEdit/Unwrap")) Edit("Unwrap", (mesh, s) => GeometryUnwrap.Unwrap(mesh, UVFaces(mesh, s)));
        else if (handles.IsPressed("MeshEdit/Connect")) Edit("Connect Model Points", (mesh, s) => Operators.Connect(mesh, [.. s.Vertices], ctx.Camera.GameObject.Transform.Forward, s), SelectMode.Edge);
        else if (handles.IsPressed("MeshEdit/Triangulate")) Edit("Triangulate Model Faces", (mesh, s) => Operators.TriangulateFaces(mesh, [.. s.Faces], s), SelectMode.Face);
        else if (handles.IsPressed("MeshEdit/Flatten")) Edit("Flatten Model Points", (mesh, s) => Operators.Flatten(mesh, s));
        else if (handles.IsPressed("MeshEdit/Merge")) Edit("Merge Model Points", MergeAtCentre, SelectMode.Vertex);
        else if (handles.IsPressed("MeshEdit/Bridge"))
        {
            if (Mode == SelectMode.Edge) Edit("Bridge Model Edges", Operators.BridgeEdges);
            else Edit("Bridge Model Faces", Bridge, SelectMode.Face);
        }
        else if (handles.IsPressed("MeshEdit/Separate")) SeparateSelected();
        else if (handles.IsPressed("MeshEdit/Extrude")) Extrude(ctx);
        else if (handles.IsPressed("MeshEdit/Bevel"))
            BevelEdges(ctx);
        else if (handles.IsPressed("MeshEdit/BevelPoints"))
            BevelPoints(ctx);
        else if (handles.IsPressed("MeshEdit/ExtrudeMenu"))
            ExtrudeMenu(ctx);
        else if (handles.IsPressed("MeshEdit/ShrinkFatten")) ShrinkFatten(ctx);
        else if (handles.IsPressed("MeshEdit/ToSphere")) ToSphere(ctx);
        else if (handles.IsPressed("MeshEdit/Split")) Split();
        else if (handles.IsPressed("MeshEdit/LimitedDissolve")) LimitedDissolve(ctx);
        else if (handles.IsPressed("MeshEdit/TrisToQuads")) TrisToQuads(ctx);
        else if (handles.IsPressed("MeshEdit/MergeByDistance")) MergeByDistance(ctx);
        else if (handles.IsPressed("MeshEdit/Smooth")) SmoothPoints(ctx);
        else if (handles.IsPressed("MeshEdit/PickLinked")) PickLinked(ctx, remove: false);
        else if (handles.IsPressed("MeshEdit/UnpickLinked")) PickLinked(ctx, remove: true);
        else if (handles.IsPressed("MeshEdit/SnapMenu")) SnapMenu();
        else if (handles.IsPressed("MeshEdit/EdgeMenu")) EdgeMenu(ctx);
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

    /// <summary>
    /// Extrudes what the select mode picks: faces pull out by the mouse along their normals, while edges grow faces and
    /// points grow edges to copies that a grab then carries with the mouse.
    /// </summary>
    private void Extrude(SceneToolContext ctx)
    {
        switch (Mode)
        {
            case SelectMode.Face:
                ExtrudeFaces(ctx, ExtrudeMode.AverageNormal);
                break;
            case SelectMode.Edge:
                Edit("Extrude Model Edges", (mesh, s) => Operators.ExtrudeEdges(mesh, [.. s.Edges], s), SelectMode.Edge);
                StartModal(ctx, TransformKind.Translate);
                break;
            default:
                Edit("Extrude Model Points", (mesh, s) => Operators.ExtrudePoints(mesh, [.. s.Vertices], s), SelectMode.Vertex);
                StartModal(ctx, TransformKind.Translate);
                break;
        }
    }

    private void Dissolve() => Edit("Dissolve Model Elements", (mesh, s) => Operators.Dissolve(mesh, s, Mode, s), SelectMode.Face);

    private void LimitedDissolve(SceneToolContext ctx)
        => StartOperator(ctx, new AmountOperator("Limited Dissolve Model", false, p => 5f + p * 0.2f, "0.#", (mesh, s, deg, _) =>
            Operators.LimitedDissolve(mesh, s.Edges.Count > 0 ? [.. s.Edges] : null, Math.Clamp(deg, 0f, 90f), s), SelectMode.Face));

    /// <summary>X: a menu of what to take away.</summary>
    private void DeleteMenu(SceneToolContext ctx)
    {
        Float2 at = (Float2)Input.MousePosition;
        OrigamiUI.Origami.ContextMenu(at.X, at.Y, menu =>
        {
            menu.Title(Loc.Get("model.menu.delete"));
            menu.Item(Loc.Get("model.menu.delete_points"), () => Edit("Delete Model Points", (mesh, s) => DeleteAs(mesh, s, SelectMode.Vertex)), danger: true);
            menu.Item(Loc.Get("model.menu.delete_edges"), () => Edit("Delete Model Edges", (mesh, s) => DeleteAs(mesh, s, SelectMode.Edge)), danger: true);
            menu.Item(Loc.Get("model.menu.delete_faces"), () => Edit("Delete Model Faces", (mesh, s) => DeleteAs(mesh, s, SelectMode.Face)), danger: true);
            menu.Separator();
            menu.Item(Loc.Get("csg.dissolve"), Dissolve, shortcut: Chord("MeshEdit/Dissolve"));
            menu.Item(Loc.Get("csg.limited_dissolve"), () => LimitedDissolve(ctx), shortcut: Chord("MeshEdit/LimitedDissolve"));
        });
    }

    private static void DeleteAs(GeometryData mesh, ElementSelection selection, SelectMode mode)
    {
        switch (mode)
        {
            case SelectMode.Face: GeometryOperators.DeleteFaces(mesh, [.. selection.Faces]); break;
            case SelectMode.Edge: GeometryOperators.DeleteEdges(mesh, [.. selection.Edges]); break;
            default: GeometryOperators.DeleteVertices(mesh, [.. selection.Vertices]); break;
        }
        selection.Clear();
    }

    /// <summary>V: rips along the selected edges, then grabs the copies.</summary>
    private void Rip(SceneToolContext ctx)
    {
        Float2 mouse = ctx.Handles.MousePosition;
        Edit("Rip Model", (mesh, s) =>
        {
            Float4x4 toWorld = _edited.Find(m => ReferenceEquals(m.Mesh, mesh)) is { } model && model.IsValid() ? model.Transform.LocalToWorldMatrix : Float4x4.Identity;
            Operators.Rip(mesh, s, p => ctx.Handles.WorldToScreen(Float4x4.TransformPoint(p, toWorld)) is { } at ? Float2.Distance(at, mouse) : float.PositiveInfinity);
        }, SelectMode.Vertex);
        StartModal(ctx, TransformKind.Translate);
    }

    /// <summary>Shift V: slides one point along an edge, or the selected edges along the edges beside them.</summary>
    private void Slide(SceneToolContext ctx)
    {
        StartModal(ctx, TransformKind.Translate);
        if (_modal != null) StartSlide(ctx);
    }

    /// <summary>Shift E: sets the selected edges' crease by how far the mouse moves.</summary>
    private void Crease(SceneToolContext ctx)
        => StartOperator(ctx, new AmountOperator("Crease Model Edges", false, p => Math.Clamp(p * 0.005f, 0f, 1f), "0.##", (mesh, s, t, _) => Operators.Crease(s.Edges, t), SelectMode.Edge));

    /// <summary>Shift D: copies the selection in place and grabs the copy.</summary>
    private void Duplicate(SceneToolContext ctx)
    {
        Edit("Duplicate Model Elements", Operators.Duplicate);
        StartModal(ctx, TransformKind.Translate);
    }

    private void TrisToQuads(SceneToolContext ctx)
        => StartOperator(ctx, new AmountOperator("Join Model Triangles", false, p => 40f + p * 0.2f, "0.#", (mesh, s, deg, _) =>
            Operators.TrisToQuads(mesh, s.Faces.Count > 0 ? [.. s.Faces] : null, Math.Clamp(deg, 0f, 180f), s), SelectMode.Face));

    private void MergeByDistance(SceneToolContext ctx)
        => StartOperator(ctx, new AmountOperator("Merge Model Points By Distance", true, d => d, "0.###", (mesh, s, d, _) =>
            Operators.MergeByDistance(mesh, s.Vertices.Count > 0 ? [.. s.Vertices] : null, d, s), SelectMode.Vertex));

    private void SmoothPoints(SceneToolContext ctx)
        => StartOperator(ctx, new AmountOperator("Smooth Model Points", false, p => p * 0.005f, "0.##",
            (mesh, s, t, repeats) => Operators.Smooth(mesh, [.. s.Vertices], t, s, repeats), SelectMode.Vertex, 1, 50));

    private void RelaxPoints(SceneToolContext ctx)
        => StartOperator(ctx, new AmountOperator("Relax Model Points", false, p => p * 0.005f, "0.##",
            (mesh, s, t, repeats) => Operators.Relax(mesh, [.. s.Vertices], t, s, repeats), SelectMode.Vertex, 1, 50));

    private void SubdivideSmooth(SceneToolContext ctx)
        => StartOperator(ctx, new AmountOperator("Subdivide Smooth", false, _ => 0f, "0", (mesh, s, _, levels) => Operators.SubdivideSmooth(mesh, levels, s), null, 1, 4));

    private void Fill() => Edit("Fill", Operators.Fill);

    private void Reveal()
    {
        foreach (ModelObject model in _edited) _hidden[model] = new ElementSelection();
    }

    private void Inset(SceneToolContext ctx)
        => StartOperator(ctx, new AmountOperator("Inset Model Faces", true, d => d, "0.###", (mesh, s, d, _) => Operators.Inset(mesh, [.. s.Faces], d, s), SelectMode.Face)
            { Drag = OperatorDrag.TowardPivot });

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
        if (SnapTo is SnapKind.Edges or SnapKind.Faces) corners.Clear();
        if (SnapTo is SnapKind.Points or SnapKind.Faces) edges.Clear();
        if (SnapTo is SnapKind.Points or SnapKind.Edges) face = null;
        return Snapping.Find(corners, edges, face, handles.WorldToScreen, mouse, out Float3 point) ? point : null;
    }

    // ================================================================
    //  Right click menu
    // ================================================================

    public override bool ReplacesSceneContextMenu => Editing;

    // F is Fill in edit mode, so the camera does not frame the object on it
    public override bool TakesShortcut(string shortcut) => Editing && shortcut == "Scene/Focus";

    private static string Chord(string shortcut) => ShortcutManager.Find(shortcut) is { } definition ? ShortcutManager.GetDisplayString(definition.Binding) : "";

    /// <summary>
    /// The right click menu. In edit mode it follows the select mode and what is selected, in object mode it adds the
    /// model world's object commands to the scene view's own.
    /// </summary>
    public override void OnSceneContextMenu(SceneToolContext ctx, OrigamiUI.ContextBuilder menu)
    {
        if (!Editing)
        {
            ObjectMenu(menu);
            return;
        }

        int points = 0, edges = 0, faces = 0;
        foreach (ModelObject model in _edited)
        {
            points += _selection[model].Vertices.Count;
            edges += _selection[model].Edges.Count;
            faces += _selection[model].Faces.Count;
        }
        bool any = points > 0;
        switch (Mode)
        {
            case SelectMode.Vertex:
                menu.Title(Count(points, "model.menu.point", "model.menu.points"));
                if (any) PointItems(ctx, menu);
                break;
            case SelectMode.Edge:
                menu.Title(Count(edges, "model.menu.edge", "model.menu.edges"));
                if (any) EdgeItems(ctx, menu);
                break;
            default:
                menu.Title(Count(faces, "model.menu.face", "model.menu.faces"));
                if (any) FaceItems(ctx, menu);
                break;
        }
        if (any)
        {
            menu.Separator();
            menu.Item(Loc.Get("model.menu.duplicate"), () => Duplicate(ctx), shortcut: Chord("MeshEdit/Duplicate"));
            menu.Item(Loc.Get("model.menu.separate"), SeparateSelected, shortcut: Chord("MeshEdit/Separate"));
            menu.Item(Loc.Get("model.origin_to_selection"), OriginToSelection);
            menu.Submenu(Loc.Get("model.menu.transform"), m =>
            {
                m.Item(Loc.Get("model.menu.grab"), () => StartModal(ctx, TransformKind.Translate), shortcut: Chord("MeshEdit/Grab"));
                m.Item(Loc.Get("model.menu.rotate"), () => StartModal(ctx, TransformKind.Rotate), shortcut: Chord("MeshEdit/Rotate"));
                m.Item(Loc.Get("model.menu.scale"), () => StartModal(ctx, TransformKind.Scale), shortcut: Chord("MeshEdit/Scale"));
            });
        }
        menu.Submenu(Loc.Get("model.menu.symmetrize"), m =>
        {
            for (int axis = 0; axis < 3; axis++)
            {
                int chosen = axis;
                string name = axis == 0 ? "X" : axis == 1 ? "Y" : "Z";
                m.Item(Loc.Get("csg.symmetrize_side", new { from = "+" + name, to = "-" + name }),
                    () => EditMeshes("Symmetrize Model", (model, mesh, s) => { Operators.Symmetrize(mesh, chosen, true, 0f, [.. model.Bones.Select(b => b.Name)]); s.Clear(); }));
            }
        });
        menu.Separator();
        menu.Submenu(Loc.Get("model.menu.select"), m => SelectItems(m));
        menu.Submenu(Loc.Get("model.menu.tools"), m =>
        {
            m.Item(Loc.Get("csg.loop_cut"), () => StartLoopCut(ctx), shortcut: Chord("MeshEdit/LoopCut"));
            m.Item(Loc.Get("csg.knife"), () => StartKnife(ctx), shortcut: Chord("MeshEdit/Knife"));
            m.Item(Loc.Get("model.menu.draw_lines"), () => StartLine(ctx), shortcut: Chord("MeshEdit/DrawLine"));
            m.Item(Loc.Get("model.menu.draw_quads"), () => StartQuads(ctx), shortcut: Chord("MeshEdit/DrawQuads"));
        });
        menu.Submenu(Loc.Get("model.menu.hide"), m =>
        {
            m.Item(Loc.Get("model.menu.hide_selected"), () => Hide(unselected: false), shortcut: Chord("MeshEdit/Hide"));
            m.Item(Loc.Get("model.menu.hide_unselected"), () => Hide(unselected: true), shortcut: Chord("MeshEdit/HideUnselected"));
            m.Item(Loc.Get("model.menu.reveal"), Reveal, shortcut: Chord("MeshEdit/Reveal"));
        });
        menu.Submenu(Loc.Get("model.menu.view"), m =>
        {
            m.Item(Loc.Get("model.menu.frame"), () => Frame(ctx), shortcut: Chord("MeshEdit/Frame"));
            m.Separator();
            m.Toggle(Loc.Get("csg.xray"), () => XRay = !XRay, () => XRay);
            m.Toggle(Loc.Get("model.menu.snap"), () => SnapToObjects = !SnapToObjects, () => SnapToObjects);
            m.Submenu(Loc.Get("model.menu.snap_to"), sub =>
            {
                foreach (SnapKind kind in Enum.GetValues<SnapKind>())
                {
                    SnapKind chosen = kind;
                    sub.Item(Loc.Get($"model.menu.snap_{kind.ToString().ToLowerInvariant()}"), () => SnapTo = chosen, on: SnapTo == chosen);
                }
            });
            m.Submenu(Loc.Get("model.menu.gizmo"), sub =>
            {
                foreach (EditGizmo kind in Enum.GetValues<EditGizmo>())
                {
                    EditGizmo chosen = kind;
                    sub.Item(Loc.Get($"model.menu.gizmo_{kind.ToString().ToLowerInvariant()}"), () => Gizmo = chosen, on: Gizmo == chosen);
                }
            });
            m.Toggle(Loc.Get("csg.proportional"), () => Proportional = !Proportional, () => Proportional);
            m.Toggle(Loc.Get("model.menu.mirror_clip"), () => MirrorClip = !MirrorClip, () => MirrorClip);
            m.Toggle(Loc.Get("model.menu.auto_shrinkwrap"), () => AutoShrinkwrap = !AutoShrinkwrap, () => AutoShrinkwrap);
            m.Toggle(Loc.Get("uv.checker_world"), () => ModelEditingMode.SetChecker(!ModelObject.ShowChecker), () => ModelObject.ShowChecker);
        });
        menu.Separator();
        menu.Item(Loc.Get("model.menu.object_mode"), Leave, shortcut: Chord("ModelEdit/Toggle"));
    }

    private static string Count(int count, string one, string many)
        => count == 1 ? Loc.Get(one) : Loc.Get(many, new { count });

    private void PointItems(SceneToolContext ctx, OrigamiUI.ContextBuilder menu)
    {
        menu.Item(Loc.Get("model.menu.extrude"), () => Extrude(ctx), shortcut: Chord("MeshEdit/Extrude"));
        menu.Item(Loc.Get("model.menu.fill"), Fill, shortcut: Chord("MeshEdit/Fill"));
        menu.Item(Loc.Get("csg.bevel"), () => BevelPoints(ctx), shortcut: Chord("MeshEdit/BevelPoints"));
        menu.Item(Loc.Get("model.menu.rip"), () => Rip(ctx), shortcut: Chord("MeshEdit/Rip"));
        menu.Item(Loc.Get("model.menu.slide"), () => Slide(ctx), shortcut: Chord("MeshEdit/Slide"));
        menu.Item(Loc.Get("csg.merge_centre"), () => Edit("Merge Model Points", MergeAtCentre, SelectMode.Vertex), shortcut: Chord("MeshEdit/Merge"));
        menu.Item(Loc.Get("csg.merge_by_distance"), () => MergeByDistance(ctx), shortcut: Chord("MeshEdit/MergeByDistance"));
        menu.Item(Loc.Get("csg.connect"), () => Edit("Connect Model Points", (mesh, s) => Operators.Connect(mesh, [.. s.Vertices], ctx.Camera.GameObject.Transform.Forward, s), SelectMode.Edge), shortcut: Chord("MeshEdit/Connect"));
        menu.Item(Loc.Get("csg.smooth"), () => SmoothPoints(ctx), shortcut: Chord("MeshEdit/Smooth"));
        menu.Item(Loc.Get("model.menu.shrink_fatten"), () => ShrinkFatten(ctx), shortcut: Chord("MeshEdit/ShrinkFatten"));
        menu.Item(Loc.Get("model.menu.to_sphere"), () => ToSphere(ctx), shortcut: Chord("MeshEdit/ToSphere"));
        menu.Item(Loc.Get("csg.relax"), () => RelaxPoints(ctx));
        menu.Item(Loc.Get("csg.flatten"), () => Edit("Flatten Model Points", (mesh, s) => Operators.Flatten(mesh, s)), shortcut: Chord("MeshEdit/Flatten"));
        menu.Item(Loc.Get("model.menu.shrinkwrap"), Shrinkwrap);
        menu.Separator();
        menu.Item(Loc.Get("csg.dissolve"), () => Edit("Dissolve Model Elements", (mesh, s) => Operators.Dissolve(mesh, s, Mode, s), SelectMode.Face), shortcut: Chord("MeshEdit/Dissolve"));
        menu.Item(Loc.Get("model.menu.delete_points"), () => Edit("Delete Model Elements", Delete), danger: true);
    }

    private void EdgeItems(SceneToolContext ctx, OrigamiUI.ContextBuilder menu)
    {
        menu.Item(Loc.Get("model.menu.extrude"), () => Extrude(ctx), shortcut: Chord("MeshEdit/Extrude"));
        menu.Item(Loc.Get("model.menu.fill"), Fill, shortcut: Chord("MeshEdit/Fill"));
        menu.Item(Loc.Get("model.menu.bridge_edges"), () => Edit("Bridge Model Edges", Operators.BridgeEdges), shortcut: Chord("MeshEdit/Bridge"));
        menu.Item(Loc.Get("model.menu.grid_fill"), GridFill);
        menu.Item(Loc.Get("csg.bevel"), () => BevelEdges(ctx), shortcut: Chord("MeshEdit/Bevel"));
        menu.Item(Loc.Get("model.menu.slide"), () => Slide(ctx), shortcut: Chord("MeshEdit/Slide"));
        menu.Item(Loc.Get("model.menu.crease"), () => Crease(ctx), shortcut: Chord("MeshEdit/Crease"));
        menu.Item(Loc.Get("model.menu.rip"), () => Rip(ctx), shortcut: Chord("MeshEdit/Rip"));
        menu.Item(Loc.Get("csg.subdivide"), () => Subdivide(ctx));
        menu.Item(Loc.Get("model.menu.subdivide_smooth"), () => SubdivideSmooth(ctx));
        menu.Separator();
        menu.Item(Loc.Get("uv.mark_seam"), () => Edit("Mark Seam", (mesh, s) => GeometryUVs.SetSeams(mesh, s.Edges, true)));
        menu.Item(Loc.Get("uv.clear_seam"), () => Edit("Clear Seam", (mesh, s) => GeometryUVs.SetSeams(mesh, s.Edges, false)));
        menu.Separator();
        menu.Item(Loc.Get("csg.dissolve"), () => Edit("Dissolve Model Elements", (mesh, s) => Operators.Dissolve(mesh, s, Mode, s), SelectMode.Face), shortcut: Chord("MeshEdit/Dissolve"));
        menu.Item(Loc.Get("model.menu.delete_edges"), () => Edit("Delete Model Elements", Delete), danger: true);
    }

    private void FaceItems(SceneToolContext ctx, OrigamiUI.ContextBuilder menu)
    {
        menu.Item(Loc.Get("model.menu.extrude"), () => Extrude(ctx), shortcut: Chord("MeshEdit/Extrude"));
        menu.Item(Loc.Get("csg.inset"), () => Inset(ctx), shortcut: Chord("MeshEdit/Inset"));
        menu.Item(Loc.Get("model.menu.split"), Split, shortcut: Chord("MeshEdit/Split"));
        menu.Item(Loc.Get("model.menu.shrink_fatten"), () => ShrinkFatten(ctx), shortcut: Chord("MeshEdit/ShrinkFatten"));
        menu.Item(Loc.Get("csg.bevel"), () => BevelEdges(ctx), shortcut: Chord("MeshEdit/Bevel"));
        menu.Submenu(Loc.Get("model.menu.extrude_as"), m => ExtrudeItems(ctx, m));
        menu.Item(Loc.Get("csg.subdivide"), () => Subdivide(ctx));
        menu.Item(Loc.Get("model.menu.subdivide_smooth"), () => SubdivideSmooth(ctx));
        menu.Item(Loc.Get("csg.triangulate"), () => Edit("Triangulate Model Faces", (mesh, s) => Operators.TriangulateFaces(mesh, [.. s.Faces], s), SelectMode.Face), shortcut: Chord("MeshEdit/Triangulate"));
        menu.Item(Loc.Get("csg.tris_to_quads"), () => TrisToQuads(ctx), shortcut: Chord("MeshEdit/TrisToQuads"));
        menu.Item(Loc.Get("csg.bridge"), () => Edit("Bridge Model Faces", Bridge, SelectMode.Face), shortcut: Chord("MeshEdit/Bridge"));
        menu.Item(Loc.Get("csg.flatten"), () => Edit("Flatten Model Points", (mesh, s) => Operators.Flatten(mesh, s)), shortcut: Chord("MeshEdit/Flatten"));
        menu.Item(Loc.Get("model.menu.shade_smooth"), () => Edit("Shade Smooth", (mesh, s) => Operators.Shade(mesh, [.. s.Faces], true)));
        menu.Item(Loc.Get("model.menu.shade_flat"), () => Edit("Shade Flat", (mesh, s) => Operators.Shade(mesh, [.. s.Faces], false)));
        menu.Submenu(Loc.Get("model.menu.normals"), m =>
        {
            m.Item(Loc.Get("model.menu.recalculate_outside"), () => Edit("Recalculate Outside", (mesh, _) => Operators.RecalculateOutside(mesh)));
            m.Item(Loc.Get("model.menu.flip"), () => Edit("Flip Faces", (mesh, s) => Operators.Flip(mesh, [.. s.Faces])));
        });
        menu.Item(Loc.Get("model.menu.intersect"), () => Edit("Intersect Faces", (mesh, s) => GeometryOperators.IntersectFaces(mesh, [.. s.Faces])));
        if (SlotCount > 1)
            menu.Submenu(Loc.Get("model.face_slot"), m =>
            {
                int current = SelectedFaceSlot ?? -1;
                for (int i = 0; i < SlotCount; i++)
                {
                    int slot = i;
                    m.Item(Loc.Get("model.slot", new { index = i }), () => SetSelectedFaceSlot(slot), on: current == slot);
                }
            });
        menu.Separator();
        menu.Item(Loc.Get("uv.unwrap"), () => Edit("Unwrap", (mesh, s) => GeometryUnwrap.Unwrap(mesh, UVFaces(mesh, s))), shortcut: Chord("MeshEdit/Unwrap"));
        menu.Submenu(Loc.Get("model.menu.map_uvs"), m => MapUVItems(ctx, m));
        menu.Separator();
        menu.Item(Loc.Get("csg.dissolve"), () => Edit("Dissolve Model Elements", (mesh, s) => Operators.Dissolve(mesh, s, Mode, s), SelectMode.Face), shortcut: Chord("MeshEdit/Dissolve"));
        menu.Item(Loc.Get("model.menu.delete_faces"), () => Edit("Delete Model Elements", Delete), danger: true);
    }

    private void MapUVItems(SceneToolContext ctx, OrigamiUI.ContextBuilder menu)
    {
        menu.Item(Loc.Get("uv.unwrap"), () => Edit("Unwrap", (mesh, s) => GeometryUnwrap.Unwrap(mesh, UVFaces(mesh, s))));
        menu.Separator();
        menu.Item(Loc.Get("uv.project_view"), () => EditMeshes("Project From View", (model, mesh, s) =>
        {
            Float4x4 toWorld = model.Transform.LocalToWorldMatrix;
            GeometryUVs.FromView(mesh, UVFaces(mesh, s), p => ctx.Handles.WorldToScreen(Float4x4.TransformPoint(p, toWorld)) is { } at ? new Float2(at.X, -at.Y) : Float2.Zero);
        }));
        menu.Item(Loc.Get("uv.project_angle"), () => RunRemembered("Project By Angle", [new(Loc.Get("model.field_degrees"), 66f), new(Loc.Get("uv.margin"), 1f / 256f)],
            v => Edit("Project By Angle", (mesh, s) => GeometryUnwrap.ByAngle(mesh, UVFaces(mesh, s), v[0], MathF.Max(0f, v[1])))));
        menu.Item(Loc.Get("uv.follow_quad"), () => Edit("Follow Quad", (mesh, s) =>
        {
            List<GeometryData.Face> faces = UVFaces(mesh, s);
            if (faces.Where(f => f.VertCount == 4).OrderBy(f => f.Index).FirstOrDefault() is { } active) GeometryUVs.FollowQuad(mesh, active, faces);
        }));
        menu.Item(Loc.Get("uv.project_cube"), () => Edit("Cube Projection", (mesh, s) => GeometryUVs.Cube(mesh, UVFaces(mesh, s))));
        menu.Item(Loc.Get("uv.project_cylinder"), () => Edit("Cylinder Projection", (mesh, s) => GeometryUVs.Cylinder(mesh, UVFaces(mesh, s))));
        menu.Item(Loc.Get("uv.project_sphere"), () => Edit("Sphere Projection", (mesh, s) => GeometryUVs.Sphere(mesh, UVFaces(mesh, s))));
        menu.Item(Loc.Get("uv.reset"), () => Edit("Reset UVs", (mesh, s) => GeometryUVs.Reset(mesh, UVFaces(mesh, s))));
        menu.Item(Loc.Get("uv.clear"), () => Edit("Clear UVs", (mesh, _) => GeometryUVs.Clear(mesh)));
        menu.Separator();
        menu.Item(Loc.Get("uv.seams_from_sharp"), () => RunRemembered("Seams From Sharp Edges", [new(Loc.Get("model.field_degrees"), 60f)],
            v => Edit("Seams From Sharp Edges", (mesh, _) => GeometryUVs.SeamsFromSharp(mesh, v[0]))));
        menu.Item(Loc.Get("uv.seams_from_materials"), () => Edit("Seams From Materials", (mesh, _) => GeometryUVs.SeamsFromMaterials(mesh, Operators.SlotOf)));
        menu.Item(Loc.Get("uv.seams_from_islands"), () => Edit("Seams From Islands", (mesh, _) => GeometryUVs.SeamsFromIslands(mesh)));
    }

    private void SelectItems(OrigamiUI.ContextBuilder menu)
    {
        menu.Item(Loc.Get("model.menu.select_all"), () => Command((mesh, s) => Selecting.All(mesh, s, Mode)), shortcut: Chord("MeshEdit/SelectAll"));
        menu.Item(Loc.Get("model.menu.select_none"), () => Command((_, s) => s.Clear()), shortcut: Chord("MeshEdit/SelectNone"));
        menu.Item(Loc.Get("model.menu.select_invert"), () => Command((mesh, s) => Selecting.Invert(mesh, s, Mode)), shortcut: Chord("MeshEdit/Invert"));
        menu.Item(Loc.Get("model.menu.select_linked"), () => Command((_, s) => Selecting.Linked(s, Mode)), shortcut: Chord("MeshEdit/Linked"));
        menu.Item(Loc.Get("model.menu.select_more"), () => Command((_, s) => Selecting.More(s, Mode)), shortcut: Chord("MeshEdit/More"));
        menu.Item(Loc.Get("model.menu.select_less"), () => Command((mesh, s) => Selecting.Less(mesh, s, Mode)), shortcut: Chord("MeshEdit/Less"));
        menu.Item(Loc.Get("model.menu.select_loops"), () => SelectAlong(ring: false));
        menu.Item(Loc.Get("model.menu.select_rings"), () => SelectAlong(ring: true));
        menu.Item(Loc.Get("model.menu.select_non_quads"), () => Command((mesh, s) => Operators.SelectNonQuads(mesh, s)));
        menu.Separator();
        menu.Item(Loc.Get("csg.mode_vertex"), () => SetMode(SelectMode.Vertex), shortcut: Chord("MeshEdit/VertexMode"), on: Mode == SelectMode.Vertex);
        menu.Item(Loc.Get("csg.mode_edge"), () => SetMode(SelectMode.Edge), shortcut: Chord("MeshEdit/EdgeMode"), on: Mode == SelectMode.Edge);
        menu.Item(Loc.Get("csg.mode_face"), () => SetMode(SelectMode.Face), shortcut: Chord("MeshEdit/FaceMode"), on: Mode == SelectMode.Face);
    }

    /// <summary>Grows the selected edges into their edge loops, or rings.</summary>
    private void SelectAlong(bool ring)
    {
        foreach (ModelObject model in _edited)
        {
            ElementSelection selection = _selection[model];
            List<GeometryData.Edge> edges = [.. selection.Edges.SelectMany(e => ring ? Selecting.Ring(e) : Selecting.Loop(e, null))];
            selection.Edges.UnionWith(edges);
            foreach (GeometryData.Edge edge in edges) selection.Vertices.UnionWith([edge.Vert1, edge.Vert2]);
            Selecting.ToMode(selection, SelectMode.Edge, Mode);
            Selecting.Without(selection, _hidden[model], Mode);
        }
    }

    /// <summary>
    /// Alt click takes the edge loop through the edge under the mouse, with Ctrl its ring, Shift adding to the selection.
    /// In face mode a loop is the band of faces the edge's ring runs across.
    /// </summary>
    private void LoopClick(SceneToolContext ctx)
    {
        if (NearestEdge(ctx, throughSurface: XRay) is not { } at || EdgeOf(at) is not { } edge) return;
        bool add = ctx.Handles.Shift, ring = ctx.Handles.Ctrl;
        if (!add)
            foreach (ModelObject other in _edited) _selection[other].Clear();
        ElementSelection selection = _selection[at.Model];
        if (Mode == SelectMode.Face)
        {
            var across = new HashSet<GeometryData.Edge>(Selecting.Ring(edge));
            foreach (GeometryData.Face face in across.SelectMany(e => e.NeighborFaces()).Distinct())
                if (face.NeighborEdges().Count(across.Contains) >= 2)
                {
                    selection.Faces.Add(face);
                    selection.Vertices.UnionWith(face.NeighborVertices());
                }
        }
        else
            foreach (GeometryData.Edge e in ring ? Selecting.Ring(edge) : Selecting.Loop(edge, null))
            {
                selection.Edges.Add(e);
                selection.Vertices.UnionWith([e.Vert1, e.Vert2]);
            }
        Selecting.ToMode(selection, Mode == SelectMode.Face ? SelectMode.Face : SelectMode.Edge, Mode);
        Selecting.Without(selection, _hidden[at.Model], Mode);
    }

    /// <summary>The model world's object commands, after the scene view's own items.</summary>
    private void ObjectMenu(OrigamiUI.ContextBuilder menu)
    {
        if (!ModelEditingMode.IsEditing) return;
        menu.Separator();
        menu.Item(Loc.Get("model.menu.edit_mode"), Enter, shortcut: Chord("ModelEdit/Toggle"));
        List<GameObject> selected = [.. Selection.GetSelected<GameObject>()];
        if (selected.Count > 1 && Selection.ActiveObject is GameObject active)
            menu.Item(Loc.Get("model.join"), () => ModelEditingMode.Join(selected, active), shortcut: Chord("ModelEdit/Join"));
        if (selected.Count > 0)
        {
            menu.Item(Loc.Get("model.origin_to_geometry"), () => ModelEditingMode.OriginToGeometry(selected));
            menu.Submenu(Loc.Get("model.menu.apply"), m => ApplyItems(m, selected));
            menu.Submenu(Loc.Get("model.menu.clear"), m => ClearItems(m, selected));
        }
        List<ModelObject> models = [.. selected.Select(go => go.GetComponent<ModelObject>()).Where(m => m.IsValid())];
        if (models.Count > 0)
        {
            bool mirrored = models.TrueForAll(m => m.Modifiers.Exists(x => x.Enabled && x is MirrorModifier));
            menu.Toggle(Loc.Get("model.menu.mirror_x"), () => SetMirror(models, !mirrored), () => mirrored);
            if (models.Exists(m => m.Modifiers.Exists(x => x.Enabled)))
                menu.Item(Loc.Get("model.menu.apply_modifiers"), () => ApplyModifiers(models));
        }
        menu.Item(Loc.Get("model.export"), ModelEditingMode.ExportGlb);
    }

    // ================================================================
    //  Object mode
    // ================================================================

    private void ObjectKeys(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;
        List<GameObject> selected = [.. Selection.GetSelected<GameObject>()];

        // The scene view's scale tool key stands down, object mode takes R for rotate
        handles.IsPressed("Scene/ToolScale");

        if (handles.IsPressed("ModelEdit/Join") && Selection.ActiveObject is GameObject active) ModelEditingMode.Join(selected, active);
        else if (handles.IsPressed("ModelEdit/Add")) AddMenu();
        else if (handles.IsPressed("ModelEdit/Apply") && selected.Count > 0) ApplyMenu(selected);
        else if (handles.IsPressed("MeshEdit/SnapMenu") && selected.Count > 0) SnapMenu();
        else if (handles.IsPressed("ModelEdit/ClearLocation")) ModelEditingMode.ClearTransform(selected, ModelEditingMode.TransformParts.Location);
        else if (handles.IsPressed("ModelEdit/ClearRotation")) ModelEditingMode.ClearTransform(selected, ModelEditingMode.TransformParts.Rotation);
        else if (handles.IsPressed("ModelEdit/ClearScale")) ModelEditingMode.ClearTransform(selected, ModelEditingMode.TransformParts.Scale);
        else if (handles.IsPressed("MeshEdit/SelectAll")) Selection.ReplaceSelection(WorldObjects());
        else if (handles.IsPressed("MeshEdit/SelectNone")) Selection.Clear();
        else if (handles.IsPressed("MeshEdit/Invert")) Selection.ReplaceSelection(WorldObjects().Where(go => !Selection.IsSelected(go)).ToList());
        else if (handles.IsPressed("MeshEdit/HideUnselected")) HideObjects(WorldObjects().Where(go => !Selection.IsSelected(go)));
        else if (handles.IsPressed("MeshEdit/Reveal")) RevealObjects();
        else if (handles.IsPressed("MeshEdit/Hide")) HideObjects(selected);
        else if (handles.IsPressed("MeshEdit/DeleteMenu")) ObjectDeleteMenu();
        else if (handles.IsPressed("MeshEdit/Grab")) StartObjectModal(ctx, TransformKind.Translate);
        else if (handles.IsPressed("MeshEdit/Rotate")) StartObjectModal(ctx, TransformKind.Rotate);
        else if (handles.IsPressed("MeshEdit/Scale")) StartObjectModal(ctx, TransformKind.Scale);
    }

    private static List<GameObject> WorldObjects() => ModelEditingMode.World is { } world ? [.. world.Objects] : [];

    private static void AddMenu()
    {
        Float2 at = (Float2)Input.MousePosition;
        OrigamiUI.Origami.ContextMenu(at.X, at.Y, menu =>
        {
            menu.Title(Loc.Get("model.menu.add"));
            ModelEditingMode.BuildCreateMenu(menu);
        });
    }

    private static void ApplyMenu(List<GameObject> selected)
    {
        Float2 at = (Float2)Input.MousePosition;
        OrigamiUI.Origami.ContextMenu(at.X, at.Y, menu =>
        {
            menu.Title(Loc.Get("model.menu.apply"));
            ApplyItems(menu, selected);
        });
    }

    private static void ApplyItems(OrigamiUI.ContextBuilder menu, List<GameObject> selected)
    {
        void Item(string label, ModelEditingMode.TransformParts parts) => menu.Item(Loc.Get(label), () => ModelEditingMode.ApplyTransform(selected, parts));
        Item("model.menu.apply_location", ModelEditingMode.TransformParts.Location);
        Item("model.menu.apply_rotation", ModelEditingMode.TransformParts.Rotation);
        Item("model.menu.apply_scale", ModelEditingMode.TransformParts.Scale);
        Item("model.menu.apply_all", ModelEditingMode.TransformParts.All);
        Item("model.menu.apply_rotation_scale", ModelEditingMode.TransformParts.Rotation | ModelEditingMode.TransformParts.Scale);
    }

    private static void ClearItems(OrigamiUI.ContextBuilder menu, List<GameObject> selected)
    {
        menu.Item(Loc.Get("model.menu.clear_location"), () => ModelEditingMode.ClearTransform(selected, ModelEditingMode.TransformParts.Location), shortcut: Chord("ModelEdit/ClearLocation"));
        menu.Item(Loc.Get("model.menu.clear_rotation"), () => ModelEditingMode.ClearTransform(selected, ModelEditingMode.TransformParts.Rotation), shortcut: Chord("ModelEdit/ClearRotation"));
        menu.Item(Loc.Get("model.menu.clear_scale"), () => ModelEditingMode.ClearTransform(selected, ModelEditingMode.TransformParts.Scale), shortcut: Chord("ModelEdit/ClearScale"));
    }

    private static void HideObjects(IEnumerable<GameObject> objects)
    {
        foreach (GameObject go in objects)
        {
            if (go.GetComponent<ModelObject>() is not { } model || model.IsNotValid()) continue;
            model.Hidden = true;
            Selection.RemoveFromSelection(go);
        }
    }

    private static void RevealObjects()
    {
        foreach (GameObject go in WorldObjects())
            if (go.GetComponent<ModelObject>() is { } model && model.IsValid() && model.Hidden)
            {
                model.Hidden = false;
                Selection.AddToSelection(go);
            }
    }

    private static void ObjectDeleteMenu()
    {
        List<GameObject> selected = HierarchyPanel.ExcludeNestedSelections([.. Selection.GetSelected<GameObject>()]);
        if (selected.Count == 0) return;
        Float2 at = (Float2)Input.MousePosition;
        OrigamiUI.Origami.ContextMenu(at.X, at.Y, menu =>
        {
            menu.Title(Loc.Get("model.menu.delete"));
            menu.Item(Loc.Get("model.menu.delete_objects", new { count = selected.Count }), () =>
            {
                HierarchyPanel.DeleteGameObjects(selected);
                Selection.Clear();
            }, danger: true);
        });
    }

    private void StartObjectModal(SceneToolContext ctx, TransformKind kind)
    {
        List<GameObject> moving = HierarchyPanel.ExcludeNestedSelections([.. Selection.GetSelected<GameObject>()]);
        if (moving.Count == 0) return;
        _objectSession = new ObjectTransformSession(moving);
        Quaternion local = Selection.ActiveObject is GameObject active ? active.Transform.Rotation : Quaternion.Identity;
        _objectModal = new TransformModal(kind, _objectSession, local, null);
        _objectUndo = Undo.HoldRecords([.. moving], kind switch
        {
            TransformKind.Translate => "Move Objects",
            TransformKind.Rotate => "Rotate Objects",
            _ => "Scale Objects",
        });
        ctx.Handles.Modal = true;
        _modalHandles = ctx.Handles;
    }

    private void TickObjectModal(SceneToolContext ctx)
    {
        ModalState state = _objectModal!.Tick(ctx);
        ctx.Draw.Label(_objectSession!.Pivot, _objectModal.Status, PickedColor);
        if (state == ModalState.Running) _objectModal.Replay();
        else EndObjectModal(cancel: state == ModalState.Cancelled);
    }

    private void EndObjectModal(bool cancel)
    {
        if (_objectModal == null) return;
        if (cancel) _objectSession!.Restore();
        Undo.ReleaseRecords(_objectUndo!, revert: false);
        _objectModal = null;
        _objectSession = null;
        _objectUndo = null;
        if (_modalHandles != null) _modalHandles.Modal = false;
        _modalHandles = null;
    }

    /// <summary>Turns mirroring on, by a mirror modifier first in the stack, or off by taking every mirror away.</summary>
    private static void SetMirror(List<ModelObject> models, bool on)
    {
        Undo.RecordObjects([.. models], on ? "Mirror On" : "Mirror Off");
        foreach (ModelObject model in models)
        {
            if (!on) model.Modifiers.RemoveAll(m => m is MirrorModifier);
            else if (model.Modifiers.OfType<MirrorModifier>().FirstOrDefault() is { } off) off.Enabled = true;
            else model.Modifiers.Insert(0, new MirrorModifier());
            model.RefreshPreview();
        }
    }

    /// <summary>Bakes every enabled modifier into the mesh and takes it out of the stack.</summary>
    private static void ApplyModifiers(List<ModelObject> models)
    {
        Undo.RecordObjects([.. models], "Apply Modifiers");
        foreach (ModelObject model in models)
        {
            model.Mesh = ModelModifier.Run(model.Mesh, model.Modifiers, model.ModifierContext());
            model.Mesh.MarkChanged();
            model.Modifiers.RemoveAll(m => m.Enabled);
            model.RefreshPreview();
        }
    }

    // ================================================================
    //  Mode strip
    // ================================================================

    private static readonly EditorGlyphIcon ObjectIcon = new(EditorIcons.ArrowPointer), PointIcon = new(EditorIcons.CircleDot),
        EdgeIcon = new(EditorIcons.Slash), FaceIcon = new(EditorIcons.Square);

    /// <summary>Object mode and the three select modes, beside the scene view's tool buttons.</summary>
    public override void OnToolStripGUI(SceneToolContext ctx, Paper paper, string id)
    {
        EditorGUI.ToolStripGroup(paper, $"{id}_modes", () =>
        {
            EditorGUI.ToolStripButton(paper, $"{id}_object", ObjectIcon, ShortcutManager.GetTooltip(Loc.Get("model.menu.object_mode"), "ModelEdit/Toggle"), !Editing, Leave);
            EditorGUI.ToolStripButton(paper, $"{id}_vertex", PointIcon, ShortcutManager.GetTooltip(Loc.Get("csg.mode_vertex"), "MeshEdit/VertexMode"),
                Editing && Mode == SelectMode.Vertex, () => EditIn(SelectMode.Vertex));
            EditorGUI.ToolStripButton(paper, $"{id}_edge", EdgeIcon, ShortcutManager.GetTooltip(Loc.Get("csg.mode_edge"), "MeshEdit/EdgeMode"),
                Editing && Mode == SelectMode.Edge, () => EditIn(SelectMode.Edge));
            EditorGUI.ToolStripButton(paper, $"{id}_face", FaceIcon, ShortcutManager.GetTooltip(Loc.Get("csg.mode_face"), "MeshEdit/FaceMode"),
                Editing && Mode == SelectMode.Face, () => EditIn(SelectMode.Face));
        }, null);
    }

    private void EditIn(SelectMode mode)
    {
        if (!Editing) Enter();
        SetMode(mode);
    }

    // ================================================================
    //  Adjust last
    // ================================================================

    /// <summary>The last operator's settings in a small box at the foot of the viewport, changed to run it again.</summary>
    public override void OnSceneOverlayGUI(SceneToolContext ctx, Paper paper, string id, Float2 viewportSize)
    {
        if (!Editing || Last is not { } last) return;
        using (paper.Column($"{id}_last").PositionType(PositionType.SelfDirected).Position(12, viewportSize.Y - 40 - 24 * last.Fields.Length).Width(220).Height(UnitValue.Auto)
                   .BackgroundColor(EditorTheme.Neutral200).Rounded(6).Padding(8, 8, 6, 6).Gap(2).Enter())
        {
            Origami.Label(paper, $"{id}_last_name", last.Name).Show();
            for (int i = 0; i < last.Fields.Length; i++)
            {
                int index = i;
                LastField field = last.Fields[i];
                switch (field.Kind)
                {
                    case FieldKind.Whole:
                        PropertyGridUtils.DrawField(paper, $"{id}_last_{i}", field.Label, typeof(int), (int)field.Value, v => AdjustLast(index, v is int n ? n : field.Value));
                        break;
                    case FieldKind.Toggle:
                        PropertyGridUtils.DrawField(paper, $"{id}_last_{i}", field.Label, typeof(bool), field.Value != 0f, v => AdjustLast(index, v is true ? 1f : 0f));
                        break;
                    default:
                        PropertyGridUtils.DrawField(paper, $"{id}_last_{i}", field.Label, typeof(float), field.Value, v => AdjustLast(index, v is float f ? f : field.Value));
                        break;
                }
            }
        }
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

    // The last amount operator: its settings, its undo step, and the meshes it left, so a later change to them ends it
    // The last step that can be run again with other settings: what it ran with, how to run it, and the step and meshes it left
    public enum FieldKind { Number, Whole, Toggle }

    /// <summary>One setting of the last step: a number, a whole number, or on and off as 1 and 0.</summary>
    public readonly record struct LastField(string Label, float Value, FieldKind Kind = FieldKind.Number);

    private sealed record LastStep(string Name, LastField[] Fields, Action<float[]> Run, int Group, long Step,
                                   List<(ModelObject Model, GeometryData Mesh, int Version)> Left);
    private LastStep? _last;

    /// <summary>The last operator or transform run, while nothing else has changed the meshes or the history since.</summary>
    public (string Name, LastField[] Fields)? Last
        => _last is { } last && LastStillHolds(last) ? (last.Name, last.Fields) : null;

    private static bool LastStillHolds(LastStep last)
    {
        IReadOnlyList<UndoStepInfo> steps = Undo.UndoSteps;
        if (steps.Count == 0 || steps[^1].Id != last.Step) return false;
        foreach ((ModelObject model, GeometryData mesh, int version) in last.Left)
            if (model.IsNotValid() || !ReferenceEquals(model.Mesh, mesh) || mesh.Version != version) return false;
        return true;
    }

    /// <summary>Runs the last step again from before it, with one setting changed, in place of its step.</summary>
    public void AdjustLast(int field, float value)
    {
        if (_last is not { } last || !LastStillHolds(last) || field < 0 || field >= last.Fields.Length) return;
        var fields = (LastField[])last.Fields.Clone();
        fields[field] = fields[field] with { Value = value };
        Undo.RevertAllDownToGroup(last.Group);
        RestoreHeld();
        RunRemembered(last.Name, fields, last.Run);
    }

    /// <summary>Runs an amount operator as one undo step of its own, remembered for adjusting.</summary>
    private void RunAsLast(AmountOperator op, float amount, int count)
    {
        // An operator whose amount ignores the mouse, such as a subdivide, has only its count to adjust
        bool hasAmount = op.Along != null || op.Amount(0f) != op.Amount(1f);
        bool hasCount = op.MaxCount > 1;
        var fields = new List<LastField>();
        if (hasAmount) fields.Add(new(Loc.Get("model.amount"), amount));
        if (hasCount) fields.Add(new(Loc.Get("model.count"), count, FieldKind.Whole));
        int first = fields.Count;
        foreach (LastField extra in op.Extras) fields.Add(extra with { Label = Loc.Get(extra.Label) });
        RunRemembered(op.Name, [.. fields], v =>
        {
            float runAmount = hasAmount ? v[0] : amount;
            int runCount = hasCount ? Math.Clamp((int)v[first - 1], 1, op.MaxCount) : count;
            Edit(op.Name, (mesh, s) => op.Invoke(mesh, s, runAmount, runCount, v[first..]), op.Selects);
        });
    }

    /// <summary>Runs a step as one undo step of its own, remembered with its settings for adjusting.</summary>
    public void RunRemembered(string name, LastField[] fields, Action<float[]> run)
    {
        Undo.FlushUndoRecordObjects();
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        run([.. fields.Select(f => f.Value)]);
        Undo.FlushUndoRecordObjects();
        // A step that changed nothing pushed none, and the step on top belongs to something else
        IReadOnlyList<UndoStepInfo> steps = Undo.UndoSteps;
        _last = steps.Count == 0 || steps[^1].Group != group ? null
            : new LastStep(name, fields, run, group, steps[^1].Id, [.. _edited.Select(m => (m, m.Mesh, m.Mesh.Version))]);
    }

    /// <summary>Moves, turns or scales the selected points as one remembered step, with every setting adjustable after.</summary>
    private void TransformAsLast(TransformKind kind, Float3 vector, float degrees, Quaternion axes, float falloff)
    {
        string x = Loc.Get("model.field_x"), y = Loc.Get("model.field_y"), z = Loc.Get("model.field_z"), reach = Loc.Get("model.field_falloff");
        switch (kind)
        {
            case TransformKind.Rotate:
                RunRemembered("Rotate Model Points",
                    [new(Loc.Get("model.field_axis_x"), vector.X), new(Loc.Get("model.field_axis_y"), vector.Y), new(Loc.Get("model.field_axis_z"), vector.Z),
                     new(Loc.Get("model.field_degrees"), degrees), new(reach, falloff)],
                    v => RunTransform("Rotate Model Points", v[4], s =>
                    {
                        var axis = new Float3(v[0], v[1], v[2]);
                        if (Float3.LengthSquared(axis) > 1e-12f) s.Rotate(axis, v[3] * MathF.PI / 180f);
                    }));
                break;
            case TransformKind.Scale:
                RunRemembered("Scale Model Points", [new(x, vector.X), new(y, vector.Y), new(z, vector.Z), new(reach, falloff)],
                    v => RunTransform("Scale Model Points", v[3], s => s.Scale(new Float3(v[0], v[1], v[2]), axes)));
                break;
            default:
                string name = kind == TransformKind.Slide ? "Slide Model Points" : "Move Model Points";
                RunRemembered(name, [new(x, vector.X), new(y, vector.Y), new(z, vector.Z), new(reach, falloff)],
                    v => RunTransform(name, v[3], s => s.Translate(new Float3(v[0], v[1], v[2]))));
                break;
        }
    }

    // A session over the selected points with a falloff, moved and committed as one step
    private void RunTransform(string name, float falloff, Action<TransformSession> move)
    {
        if (StartSession() is not { } session) return;
        session.Falloff = MathF.Max(0f, falloff);
        _sessionMove = () => move(session);
        CommitSession(name);
    }

    // ================================================================
    //  Material slots
    // ================================================================

    /// <summary>The material slot of the selected faces, -1 when they differ, null with none selected.</summary>
    public int? SelectedFaceSlot
    {
        get
        {
            int? found = null;
            foreach (ModelObject model in _edited)
                foreach (GeometryData.Face face in _selection[model].Faces)
                {
                    int slot = Operators.SlotOf(face);
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

    /// <summary>Moves the origin of each edited object with something selected to the middle of its selected points.</summary>
    private void OriginToSelection()
    {
        foreach (ModelObject model in _edited)
        {
            ElementSelection selection = _selection[model];
            if (selection.Vertices.Count > 0) ModelEditingMode.MoveOrigin(model, GeometryData.Centroid(selection.Vertices), "Origin To Selection");
        }
    }

    private static void MergeAtCentre(GeometryData mesh, ElementSelection selection)
    {
        if (selection.Vertices.Count >= 2) Operators.Merge(mesh, [.. selection.Vertices], GeometryData.Centroid(selection.Vertices), selection);
    }

    private static void Bridge(GeometryData mesh, ElementSelection selection)
    {
        if (selection.Faces.Count != 2) return;
        GeometryData.Face[] ends = [.. selection.Faces];
        Operators.Bridge(mesh, ends[0], ends[1], 1, selection);
    }

    private void BevelEdges(SceneToolContext ctx)
        => StartOperator(ctx, new AmountOperator("Bevel Model Edges", true, d => d, "0.###", (_, _, _, _) => { }, SelectMode.Face, 1, 32)
        {
            Extras = [new("model.field_profile", 0.5f)],
            RunWithExtras = (mesh, s, width, segments, extras) =>
            {
                if (s.Edges.Count > 0) Operators.Bevel(mesh, [.. s.Edges], width, segments, s, extras[0]);
                else Operators.BevelPoints(mesh, [.. s.Vertices], width, s);
            },
        });

    /// <summary>L: everything joined to the surface under the mouse is selected, or with Shift no longer selected.</summary>
    private void PickLinked(SceneToolContext ctx, bool remove)
    {
        Ray ray = ctx.Handles.MouseRay;
        ModelObject? best = null;
        GeometryData.Face? under = null;
        float nearest = float.MaxValue;
        foreach (ModelObject model in _edited)
        {
            ElementSelection hidden = _hidden[model];
            if (Tree(model.Mesh).Raycast(model.Transform.LocalToWorldMatrix, ray, out GeometryData.Face? face, out float distance, hidden.Faces.Count > 0 ? hidden.Faces.Contains : null)
                && distance < nearest)
            {
                nearest = distance;
                best = model;
                under = face;
            }
        }
        if (best.IsNotValid() || under == null) return;

        var linked = new ElementSelection();
        linked.Vertices.UnionWith(under.NeighborVertices());
        Selecting.Linked(linked, SelectMode.Vertex);
        Selecting.ToMode(linked, SelectMode.Vertex, Mode);
        Command((mesh, s) =>
        {
            if (!ReferenceEquals(mesh, best!.Mesh)) return;
            if (remove)
            {
                s.Vertices.ExceptWith(linked.Vertices);
                s.Edges.ExceptWith(linked.Edges);
                s.Faces.ExceptWith(linked.Faces);
            }
            else
            {
                s.Vertices.UnionWith(linked.Vertices);
                s.Edges.UnionWith(linked.Edges);
                s.Faces.UnionWith(linked.Faces);
            }
        });
    }

    /// <summary>Shift S: puts the selection on the grid, or whole objects onto the active one.</summary>
    private void SnapMenu()
    {
        Float2 at = (Float2)Input.MousePosition;
        OrigamiUI.Origami.ContextMenu(at.X, at.Y, menu =>
        {
            menu.Title(Loc.Get("model.menu.snap_menu"));
            if (Editing)
            {
                menu.Item(Loc.Get("model.menu.selection_to_grid"), () => EditMeshes("Snap Points To Grid", (model, mesh, s) =>
                {
                    Float4x4 toWorld = model.Transform.LocalToWorldMatrix, toLocal = toWorld.Invert();
                    foreach (GeometryData.Vertex vertex in s.Vertices)
                        vertex.Point = Float4x4.TransformPoint(Round(Float4x4.TransformPoint(vertex.Point, toWorld), SceneTools.MoveSnap), toLocal);
                }));
                return;
            }
            List<GameObject> selected = HierarchyPanel.ExcludeNestedSelections([.. Selection.GetSelected<GameObject>()]);
            menu.Item(Loc.Get("model.menu.selection_to_grid"), () =>
            {
                Undo.RecordObjects([.. selected], "Snap Objects To Grid");
                foreach (GameObject go in selected) go.Transform.Position = Round(go.Transform.Position, SceneTools.MoveSnap);
            });
            if (Selection.ActiveObject is GameObject active && selected.Count > 1)
                menu.Item(Loc.Get("model.menu.selection_to_active"), () =>
                {
                    Undo.RecordObjects([.. selected], "Snap Objects To Active");
                    foreach (GameObject go in selected) go.Transform.Position = active.Transform.Position;
                });
        });
    }

    /// <summary>Ctrl E: the edge menu, whatever the select mode.</summary>
    private void EdgeMenu(SceneToolContext ctx)
    {
        Float2 at = (Float2)Input.MousePosition;
        OrigamiUI.Origami.ContextMenu(at.X, at.Y, menu =>
        {
            menu.Title(Loc.Get("model.menu.edge_menu"));
            EdgeItems(ctx, menu);
        });
    }

    private void ShrinkFatten(SceneToolContext ctx)
        => StartOperator(ctx, new AmountOperator("Shrink Fatten Model Points", true, d => -d, "0.###", (mesh, s, d, _) => GeometryOperators.ShrinkFatten(mesh, s.Vertices, d), null)
            { Drag = OperatorDrag.TowardPivot });

    private void ToSphere(SceneToolContext ctx)
        => StartOperator(ctx, new AmountOperator("To Sphere", false, p => Math.Clamp(p * 0.005f, 0f, 1f), "0.##", (mesh, s, t, _) => GeometryOperators.ToSphere(mesh, s.Vertices, t), null));

    /// <summary>Y: the selected faces no longer share points with the rest, and stay selected.</summary>
    private void Split() => Edit("Split Model Faces", (mesh, s) =>
    {
        List<GeometryData.Face> split = GeometryOperators.SplitFaces(mesh, s.Faces);
        s.Clear();
        foreach (GeometryData.Face face in split)
        {
            s.Faces.Add(face);
            s.Vertices.UnionWith(face.NeighborVertices());
            s.Edges.UnionWith(face.NeighborEdges());
        }
    });

    private void BevelPoints(SceneToolContext ctx)
        => StartOperator(ctx, new AmountOperator("Bevel Model Points", true, d => d, "0.###", (mesh, s, d, _) => Operators.BevelPoints(mesh, [.. s.Vertices], d, s), SelectMode.Face));

    private void ExtrudeFaces(SceneToolContext ctx, ExtrudeMode mode)
        => StartOperator(ctx, new AmountOperator("Extrude Model Faces", true, d => d, "0.###", (mesh, s, d, _) => Operators.ExtrudeRegion(mesh, [.. s.Faces], d, s, mode), SelectMode.Face)
            { Drag = OperatorDrag.AlongNormal });

    /// <summary>Alt E: with faces, how they extrude. Otherwise the plain extrude.</summary>
    private void ExtrudeMenu(SceneToolContext ctx)
    {
        if (Mode != SelectMode.Face)
        {
            Extrude(ctx);
            return;
        }
        Float2 at = (Float2)Input.MousePosition;
        OrigamiUI.Origami.ContextMenu(at.X, at.Y, menu =>
        {
            menu.Title(Loc.Get("model.menu.extrude_as"));
            ExtrudeItems(ctx, menu);
        });
    }

    private void ExtrudeItems(SceneToolContext ctx, OrigamiUI.ContextBuilder menu)
    {
        menu.Item(Loc.Get("model.menu.extrude_region"), () => ExtrudeFaces(ctx, ExtrudeMode.AverageNormal), shortcut: Chord("MeshEdit/Extrude"));
        menu.Item(Loc.Get("model.menu.extrude_along_normals"), () => ExtrudeFaces(ctx, ExtrudeMode.AlongNormals));
        menu.Item(Loc.Get("model.menu.extrude_individual"), () => ExtrudeFaces(ctx, ExtrudeMode.PerFace));
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
            bool skinned = Operators.SkinToWeights(model.Mesh);
            try
            {
                edit(model.Mesh, _selection[model]);
            }
            catch (InvalidOperationException e)
            {
                CSGStatus.Flash(e.Message);
            }
            if (skinned) Operators.WeightsToSkin(model.Mesh);
            Operators.FinishEdit(model.Mesh, points, faces);
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
        _operatorFrom = _operatorVirtual = _operatorLast = ctx.Handles.MousePosition;
        _operatorCount = op.StartCount;
        _operatorKey = null;
        _operatorGood = (0f, op.StartCount);
        _operatorTyped = "";
        (_operatorPivot, _operatorNormal) = SelectionPivotAndNormal();
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

        // Shift slows the mouse, a typed number stands in for it
        _operatorVirtual += (handles.MousePosition - _operatorLast) * (handles.Shift ? 0.1f : 1f);
        _operatorLast = handles.MousePosition;
        TypeNumber(handles, ref _operatorTyped);
        if (handles.GetKeyDown(KeyCode.Minus) || handles.GetKeyDown(KeyCode.KeypadSubtract))
            _operatorTyped = _operatorTyped.StartsWith('-') ? _operatorTyped[1..] : "-" + _operatorTyped;

        float amount;
        if (float.TryParse(_operatorTyped, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float typed)) amount = typed;
        else
        {
            amount = op.Along is { } along ? Along(_operatorVirtual - _operatorFrom, along) : op.Amount(op.Distance ? DragDistance(ctx, op.Drag) : Float2.Distance(_operatorVirtual, _operatorFrom));
            if (op.Distance && SceneTools.GridSnap != handles.Ctrl && SceneTools.MoveSnap > 0f) amount = MathF.Round(amount / SceneTools.MoveSnap) * SceneTools.MoveSnap;
        }

        if (_operatorKey != (amount, _operatorCount))
        {
            _operatorKey = (amount, _operatorCount);
            // An amount the operator refuses, such as a bevel too wide, falls back to the last one that worked
            if (RunFromStart(op, amount, _operatorCount)) _operatorGood = (amount, _operatorCount);
            else RunFromStart(op, _operatorGood.Amount, _operatorGood.Count);
        }
        string count = op.MaxCount > 1 ? $"  {_operatorGood.Count}" : "";
        string entry = _operatorTyped.Length > 0 ? $"  [{_operatorTyped}]" : "";
        ctx.Draw.Label(handles.MouseRay.Origin + handles.MouseRay.Direction * 5f, _operatorGood.Amount.ToString(op.Format) + count + entry, PickedColor);

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
        RunAsLast(op, _operatorGood.Amount, _operatorGood.Count);
    }

    /// <summary>Runs the operator on every mesh from where it started. False when it could not be made on one, which is put back.</summary>
    private bool RunFromStart(AmountOperator op, float amount, int count)
    {
        bool made = true;
        foreach ((ModelObject model, (GeometrySnapshot mesh, ModelEditPart selection)) in _start)
        {
            model.Mesh.Restore(mesh);
            ElementSelection running = FromIndices(model.Mesh, selection);
            try
            {
                op.Invoke(model.Mesh, running, amount, count, op.ExtraDefaults);
            }
            catch (InvalidOperationException)
            {
                model.Mesh.Restore(mesh);
                running = FromIndices(model.Mesh, selection);
                made = false;
            }
            if (op.Selects is { } lead) Selecting.ToMode(running, lead, Mode);
            _selection[model] = running;
            model.RefreshPreview();
        }
        return made;
    }

    // A distance in world units at the selection from how the mouse has moved, as the operator measures it
    private float DragDistance(SceneToolContext ctx, OperatorDrag drag)
    {
        HandleContext handles = ctx.Handles;
        float worldPerPixel = WorldPerPixel(ctx);
        if (handles.WorldToScreen(_operatorPivot) is not { } centre) return Float2.Distance(_operatorVirtual, _operatorFrom) * worldPerPixel;
        switch (drag)
        {
            case OperatorDrag.AlongNormal:
                if (handles.WorldToScreen(_operatorPivot + _operatorNormal) is { } tip && Float2.Distance(tip, centre) is float perUnit && perUnit > 1f)
                    return Float2.Dot(_operatorVirtual - _operatorFrom, (tip - centre) / perUnit) / perUnit;
                return -(_operatorVirtual.Y - _operatorFrom.Y) * worldPerPixel;
            case OperatorDrag.TowardPivot:
                return (Float2.Distance(_operatorFrom, centre) - Float2.Distance(_operatorVirtual, centre)) * worldPerPixel;
            default:
                return MathF.Abs(Float2.Distance(_operatorVirtual, centre) - Float2.Distance(_operatorFrom, centre)) * worldPerPixel;
        }
    }

    // The middle of the selected points of every edited object in the world, and how many there are
    private (Float3 Centre, int Count) SelectedCentre()
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
        return (count > 0 ? sum / count : Float3.Zero, count);
    }

    // The middle of the selected points and the average normal of the selected faces, in the world
    private (Float3 Pivot, Float3 Normal) SelectionPivotAndNormal()
    {
        Float3 normal = Float3.Zero;
        foreach (ModelObject model in _edited)
            foreach (GeometryData.Face face in _selection[model].Faces) normal += Float4x4.TransformNormal(face.Normal(), model.Transform.LocalToWorldMatrix);
        return (SelectedCentre().Centre, Float3.LengthSquared(normal) > 1e-12f ? Float3.Normalize(normal) : Float3.UnitY);
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
        Float3 centre = SelectedCentre().Centre;
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
                parts.Add((new EditTarget(model.Mesh, model.Transform.LocalToWorldMatrix) { MirrorClip = ClipOf(model) }, _selection[model]));
        if (parts.Count == 0) return null;
        _session = new TransformSession(parts, Proportional ? ProportionalRadius : 0f) { Individual = SceneTools.Pivot == PivotMode.Individual };
        _sessionMove = null;
        return _session;
    }

    private (int Axis, float Offset)? ClipOf(ModelObject model)
        => MirrorClip && model.Modifiers.Find(m => m.Enabled && m is MirrorModifier) is MirrorModifier mirror ? (Math.Clamp(mirror.Axis, 0, 2), mirror.Offset) : null;

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
        if (AutoShrinkwrap && OtherModels() is { Count: > 0 } surfaces)
            foreach (ModelObject model in _edited)
                if (_selection[model].Vertices.Count > 0)
                    Operators.Shrinkwrap(model.Mesh, [.. _selection[model].Vertices], model.Transform.LocalToWorldMatrix, SurfaceHit(surfaces), 0.002f, 1f);
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
        float falloff = _session?.Falloff ?? 0f;
        CancelSession();
        if (state == ModalState.Cancelled) return;
        switch (modal.Kind)
        {
            case TransformKind.Rotate: TransformAsLast(modal.Kind, modal.Turned.Axis, modal.Turned.Degrees, Quaternion.Identity, falloff); break;
            case TransformKind.Scale: TransformAsLast(modal.Kind, modal.Scaled.Factors, 0f, modal.Scaled.Axes, falloff); break;
            default: TransformAsLast(modal.Kind, modal.Moved, 0f, Quaternion.Identity, falloff); break;
        }
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
        GeometryData copy = model.Mesh.Clone();
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
        _loopCut = true;
        _loopEdge = null;
        _loopCuts = 1;
        ctx.Handles.Modal = true;
        _operatorHandles = ctx.Handles;
    }

    /// <summary>
    /// The ring through the edge under the mouse, through the surface, shows where the cuts go, the wheel setting how
    /// many. A click cuts, and one cut then slides with the mouse until the next click.
    /// </summary>
    private void TickLoopCut(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;
        ControlID owner = handles.GetControlID(ModalControl);
        handles.AddControl(owner, HandleContext.BodyDistance, float.MaxValue - 1f);

        float wheel = Input.MouseWheelDelta;
        if (wheel != 0f) _loopCuts = Math.Clamp(_loopCuts + (wheel > 0f ? 1 : -1), 1, 64);
        _loopEdge = NearestEdge(ctx, throughSurface: true);
        if (_loopEdge is { } hovered && EdgeOf(hovered) is { } edge) DrawRing(hovered.Model, edge);
        ctx.Draw.Label(handles.MouseRay.Origin + handles.MouseRay.Direction * 5f, Loc.Get("csg.cuts", new { count = _loopCuts }), PickedColor);

        if (handles.GetKeyDown(KeyCode.Escape) || handles.ViewportHovered && Input.GetMouseButtonDown(1))
        {
            EndLoopCut();
            return;
        }
        if (!handles.IsNearest(owner) || !handles.TryBeginDrag(owner)) return;
        handles.TryEndDrag(owner);
        if (_loopEdge is not { } chosen) return;
        int cuts = _loopCuts;
        EndLoopCut();
        // The cuts slide together along the ring as the mouse moves, the wheel still sets how many
        StartOperator(ctx, new AmountOperator("Loop Cut Model", false, f => f, "0.###", (mesh, s, slide, count) =>
        {
            if (!ReferenceEquals(mesh, chosen.Model.Mesh) || EdgeOf(chosen) is not { } cut) return;
            Operators.LoopCut(mesh, cut, count, Math.Clamp(slide, -1f, 1f), s);
        }, SelectMode.Edge, cuts, 64)
        {
            Along = LoopSlideDirection(ctx, chosen, cuts),
        });
    }

    // Which way on screen the cuts of a loop cut move as their slide goes from 0 to 1
    private static Float2 LoopSlideDirection(SceneToolContext ctx, (ModelObject Model, int A, int B) at, int cuts)
    {
        Float3? CutPoint(float slide)
        {
            GeometryData copy = at.Model.Mesh.Clone();
            if (at.A >= copy.Vertices.Count || at.B >= copy.Vertices.Count || copy.FindEdge(copy.Vertices[at.A], copy.Vertices[at.B]) is not { } edge) return null;
            List<GeometryData.Edge> made = GeometryOperators.LoopCut(copy, edge, cuts, slide);
            return made.Count > 0 ? made[0].Vert1.Point : null;
        }
        Float4x4 toWorld = at.Model.Transform.LocalToWorldMatrix;
        if (CutPoint(0f) is { } from && CutPoint(1f) is { } to
            && ctx.Handles.WorldToScreen(Float4x4.TransformPoint(from, toWorld)) is { } a && ctx.Handles.WorldToScreen(Float4x4.TransformPoint(to, toWorld)) is { } b)
            return b - a;
        return Float2.Zero;
    }

    /// <summary>Lines across the ring of four sided faces through an edge, where each cut will run.</summary>
    private void DrawRing(ModelObject model, GeometryData.Edge edge)
    {
        List<GeometryData.Edge> ring = GeometryOperators.EdgeRing(edge);
        if (ring.Count < 2) return;
        Prowl.Graphite.Rendering.GizmoBatch batch = Runtime.Resources.Scene.Active.Gizmos.Batch;
        Float4x4 toWorld = model.Transform.LocalToWorldMatrix;
        var near = new GeometryData.Vertex[ring.Count];
        near[0] = ring[0].Vert1;
        for (int i = 1; i < ring.Count; i++)
            near[i] = model.Mesh.FindEdge(near[i - 1], ring[i].Vert1) != null ? ring[i].Vert1 : ring[i].Vert2;
        Float3 At(int i, float t)
        {
            GeometryData.Vertex far = ring[i].Vert1 == near[i] ? ring[i].Vert2 : ring[i].Vert1;
            return Float4x4.TransformPoint(near[i].Point + (far.Point - near[i].Point) * t, toWorld);
        }
        bool closed = ring.Count > 2 && ring[0].NeighborFaces().Exists(ring[^1].NeighborFaces().Contains);
        var colour = new Color32(255, 220, 90, 255);
        for (int cut = 1; cut <= _loopCuts; cut++)
        {
            float t = (float)cut / (_loopCuts + 1);
            for (int i = 1; i < ring.Count; i++) batch.Line(At(i - 1, t), At(i, t), colour, 2f);
            if (closed) batch.Line(At(ring.Count - 1, t), At(0, t), colour, 2f);
        }
    }

    private void EndLoopCut()
    {
        _loopCut = false;
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
    private (ModelObject Model, int A, int B)? NearestEdge(SceneToolContext ctx, bool throughSurface = false)
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
                float distance = Polygon2D.DistanceToSegment(handles.MousePosition, from, to, out float t);
                if (distance >= bestDistance || !throughSurface && !Seen(ctx, a + (b - a) * t)) continue;
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
        if (handles.GetKeyDown(KeyCode.Enter) || handles.GetKeyDown(KeyCode.KeypadEnter) || handles.GetKeyDown(KeyCode.Space))
        {
            Cut(ctx, model);
            EndKnife();
            return;
        }
        if (!handles.IsNearest(owner) || !handles.TryBeginDrag(owner)) return;
        handles.TryEndDrag(owner);
        _knife.Add((pixel, landed));
    }

    // ================================================================
    //  Shrinkwrap
    // ================================================================

    /// <summary>
    /// Moves the selected points, or all shown ones when none is selected, onto the nearest surface of the model
    /// objects not being edited, along their normals.
    /// </summary>
    private void Shrinkwrap()
    {
        List<ModelObject> surfaces = OtherModels();
        if (surfaces.Count == 0)
        {
            CSGStatus.Flash(Loc.Get("model.shrinkwrap_nothing"));
            return;
        }
        RunRemembered("Shrinkwrap", [new(Loc.Get("model.field_offset"), 0.002f), new(Loc.Get("model.field_reach"), 1f)], v => EditMeshes("Shrinkwrap", (model, mesh, s) =>
        {
            IEnumerable<GeometryData.Vertex> points = s.Vertices.Count > 0 ? s.Vertices : mesh.Vertices.Where(v => !_hidden[model].Vertices.Contains(v));
            Operators.Shrinkwrap(mesh, [.. points], model.Transform.LocalToWorldMatrix, SurfaceHit(surfaces), v[0], MathF.Max(0f, v[1]));
        }));
    }

    private void GridFill()
        => RunRemembered("Grid Fill", [new(Loc.Get("model.field_span"), 1f, FieldKind.Whole), new(Loc.Get("model.field_offset"), 0f, FieldKind.Whole)],
            v => Edit("Grid Fill", (mesh, s) => Operators.GridFill(mesh, s, (int)v[0], (int)v[1]), SelectMode.Face));

    // How far along a world ray the nearest of the surfaces is, within a reach
    private Func<Ray, float, float?> SurfaceHit(List<ModelObject> surfaces) => (ray, reach) =>
    {
        float? nearest = null;
        foreach (ModelObject other in surfaces)
            if (Tree(other.Mesh).Raycast(other.Transform.LocalToWorldMatrix, ray, out _, out float distance) && distance <= reach && (nearest == null || distance < nearest))
                nearest = distance;
        return nearest;
    };

    /// <summary>The model objects of the world that are not being edited, which drawing and shrinkwrap land on.</summary>
    private List<ModelObject> OtherModels()
    {
        var others = new List<ModelObject>();
        if (ModelEditingMode.World is not { } world) return others;
        foreach (GameObject go in world.Objects)
            if (go.GetComponent<ModelObject>() is { } model && model.IsValid() && !_edited.Contains(model)) others.Add(model);
        return others;
    }

    /// <summary>The edited object new drawing goes into: the first with something selected, else the first.</summary>
    private ModelObject DrawTarget() => _edited.Find(m => _selection[m].Vertices.Count > 0) is { } selected ? selected : _edited[0];

    // ================================================================
    //  Line drawing
    // ================================================================

    private const float LineVertexRadius = 14f, LineMiddleRadius = 12f, LineEdgeRadius = 9f, LineHandleRadius = 12f, LineMinDrag = 5f;
    private static readonly Color32 LineColor = new(255, 220, 90, 255), SnapColor = new(120, 255, 140, 255), HandleColor = new(90, 200, 255, 255);

    // An end of a drawn line in the world, on a point or an edge of the drawn object when it snapped to one
    private readonly record struct DrawEnd(Float3 World, GeometryData.Vertex? Vertex = null, GeometryData.Edge? Edge = null)
    {
        public bool Snapped => Vertex != null || Edge != null;
    }

    private ModelObject? _lineModel;
    private DrawEnd _lineStart, _lineEnd;
    private readonly Float3[] _lineHandles = new Float3[2];
    private Float3 _linePlane, _lineDirection;
    private Float2 _lineFrom;
    private bool _lineHasPlane, _lineDragging, _linePending, _lineCurved;
    private int _lineGrabbed = -1, _lineSegments = 12;
    private string _lineTyped = "";

    /// <summary>
    /// Draws edges into the object on a plane facing the view, as for tracing a reference. Drag from one end to the
    /// other; ends snap to points, to the middle of edges and to edges. Two handles then bend the line and the wheel
    /// sets how many edges the bend is made of, or a typed number sets its length. Enter or Space keeps it, as does
    /// starting the next. Escape drops it, and leaves when there is none.
    /// </summary>
    private void StartLine(SceneToolContext ctx)
    {
        _lineModel = DrawTarget();
        _lineDragging = _linePending = _lineCurved = _lineHasPlane = false;
        _lineGrabbed = -1;
        ctx.Handles.Modal = true;
        _operatorHandles = ctx.Handles;
    }

    private void EndLine()
    {
        _lineModel = null;
        if (_operatorHandles != null) _operatorHandles.Modal = false;
        _operatorHandles = null;
    }

    private void TickLine(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;
        if (_lineModel is not { } model || model.IsNotValid())
        {
            EndLine();
            return;
        }
        ControlID owner = handles.GetControlID(ModalControl);
        handles.AddControl(owner, HandleContext.BodyDistance, float.MaxValue - 1f);
        DrawEnd under = LineSnap(ctx, model);
        bool pressed = handles.ViewportHovered && Input.GetMouseButtonDown(0), held = Input.GetMouseButton(0);

        if (_lineDragging)
        {
            _lineEnd = handles.Shift && !under.Snapped ? Stepped(ctx, under) : under;
            if (!held)
            {
                _lineDragging = false;
                if (Float2.Distance(handles.MousePosition, _lineFrom) >= LineMinDrag)
                {
                    _linePending = true;
                    _lineCurved = false;
                    _lineTyped = "";
                    _lineDirection = Float3.NormalizeSafe(_lineEnd.World - _lineStart.World, Float3.UnitX);
                    StraightHandles();
                }
            }
        }
        else if (_lineGrabbed >= 0)
        {
            _lineHandles[_lineGrabbed] = OnLinePlane(ctx);
            _lineCurved = true;
            if (!held) _lineGrabbed = -1;
        }
        else if (pressed)
        {
            _lineGrabbed = _linePending ? HandleUnder(ctx) : -1;
            if (_lineGrabbed < 0)
            {
                KeepLine();
                _lineStart = _lineEnd = under;
                _lineFrom = handles.MousePosition;
                _linePlane = under.World;
                _lineHasPlane = true;
                _lineDragging = true;
            }
        }

        // A length typed after the drag puts the end that far from the start, the way the line was drawn
        if (_linePending && !_lineCurved && TypeNumber(handles, ref _lineTyped) && float.TryParse(_lineTyped, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float length) && length > 0f)
        {
            _lineEnd = new DrawEnd(_lineStart.World + _lineDirection * length);
            StraightHandles();
        }
        float wheel = Input.MouseWheelDelta;
        if (_linePending && wheel != 0f) _lineSegments = Math.Clamp(_lineSegments + (wheel > 0f ? 1 : -1), 2, 64);
        DrawLine(ctx, under);

        if (handles.GetKeyDown(KeyCode.Enter) || handles.GetKeyDown(KeyCode.KeypadEnter) || handles.GetKeyDown(KeyCode.Space))
        {
            if (_linePending) KeepLine();
            else EndLine();
        }
        else if (handles.GetKeyDown(KeyCode.Escape) || handles.ViewportHovered && Input.GetMouseButtonDown(1))
        {
            if (_linePending || _lineDragging) _linePending = _lineDragging = false;
            else EndLine();
        }
    }

    private void StraightHandles()
    {
        _lineHandles[0] = _lineStart.World + (_lineEnd.World - _lineStart.World) / 3f;
        _lineHandles[1] = _lineStart.World + (_lineEnd.World - _lineStart.World) * (2f / 3f);
    }

    /// <summary>Digits, a point and Backspace typed into a number. True when it changed.</summary>
    private static bool TypeNumber(HandleContext handles, ref string text)
    {
        string before = text;
        for (int d = 0; d <= 9; d++)
            if (handles.GetKeyDown((KeyCode)((int)KeyCode.Number0 + d)) || handles.GetKeyDown((KeyCode)((int)KeyCode.Keypad0 + d))) text += d;
        if ((handles.GetKeyDown(KeyCode.Period) || handles.GetKeyDown(KeyCode.KeypadDecimal)) && !text.Contains('.')) text += ".";
        if (handles.GetKeyDown(KeyCode.Backspace) && text.Length > 0) text = text[..^1];
        return text != before;
    }

    // The end turned to the nearest fifteen degrees from level in the view, at the length it has
    private DrawEnd Stepped(SceneToolContext ctx, DrawEnd end)
    {
        Transform view = ctx.Camera.GameObject.Transform;
        Float3 right = view.Right, up = view.Up, line = end.World - _lineStart.World;
        float length = Float3.Length(line);
        if (length < 1e-6f) return end;
        float step = MathF.PI / 12f;
        float angle = MathF.Round(MathF.Atan2(Float3.Dot(line, up), Float3.Dot(line, right)) / step) * step;
        return new DrawEnd(_lineStart.World + (right * MathF.Cos(angle) + up * MathF.Sin(angle)) * length);
    }

    /// <summary>The line as the points its edges run through: its ends when straight, else along the bend of its handles.</summary>
    private Float3[] LinePoints()
    {
        if (!_lineCurved) return [_lineStart.World, _lineEnd.World];
        var points = new Float3[_lineSegments + 1];
        for (int i = 0; i <= _lineSegments; i++)
        {
            float t = (float)i / _lineSegments, u = 1f - t;
            points[i] = _lineStart.World * (u * u * u) + _lineHandles[0] * (3f * u * u * t) + _lineHandles[1] * (3f * u * t * t) + _lineEnd.World * (t * t * t);
        }
        return points;
    }

    private void KeepLine()
    {
        if (!_linePending || _lineModel is not { } model) return;
        _linePending = false;
        Float4x4 toLocal = model.Transform.LocalToWorldMatrix.Invert();
        Float3[] points = [.. LinePoints().Select(p => Float4x4.TransformPoint(p, toLocal))];
        var start = new LineAnchor(points[0], _lineStart.Vertex, _lineStart.Edge);
        var end = new LineAnchor(points[^1], _lineEnd.Vertex, _lineEnd.Edge);
        EditMeshes("Draw Line", (owner, mesh, s) =>
        {
            if (owner == model) Operators.AddLine(mesh, points, start, end, s);
        });
    }

    private void DrawLine(SceneToolContext ctx, DrawEnd under)
    {
        Prowl.Graphite.Rendering.GizmoBatch batch = Runtime.Resources.Scene.Active.Gizmos.Batch;
        if (_lineDragging || _linePending)
        {
            Float3[] points = LinePoints();
            for (int i = 1; i < points.Length; i++) batch.Line(points[i - 1], points[i], LineColor, 2f);
            batch.Point(_lineStart.World, _lineStart.Snapped ? SnapColor : LineColor, 8f);
            batch.Point(_lineEnd.World, _lineEnd.Snapped ? SnapColor : LineColor, 8f);
            if (_lineCurved)
                foreach (Float3 point in points[1..^1]) batch.Point(point, LineColor, 4f);
        }
        if (_linePending)
        {
            batch.Line(_lineStart.World, _lineHandles[0], HandleColor, 1f);
            batch.Line(_lineEnd.World, _lineHandles[1], HandleColor, 1f);
            batch.Point(_lineHandles[0], HandleColor, 10f);
            batch.Point(_lineHandles[1], HandleColor, 10f);
            string length = _lineTyped.Length > 0 ? _lineTyped : Float3.Distance(_lineStart.World, _lineEnd.World).ToString("0.###");
            ctx.Draw.Label(_lineEnd.World, length, LineColor);
        }
        if (!_lineDragging && _lineGrabbed < 0) batch.Point(under.World, under.Snapped ? SnapColor : LineColor, under.Snapped ? 9f : 5f);
    }

    private int HandleUnder(SceneToolContext ctx)
    {
        for (int i = 0; i < 2; i++)
            if (ctx.Handles.WorldToScreen(_lineHandles[i]) is { } at && Float2.Distance(at, ctx.Handles.MousePosition) < LineHandleRadius) return i;
        return -1;
    }

    /// <summary>The mouse on the plane facing the view through where the line started, or through the selection before any line.</summary>
    private Float3 OnLinePlane(SceneToolContext ctx)
    {
        Ray ray = ctx.Handles.MouseRay;
        Float3 normal = ctx.Camera.GameObject.Transform.Forward;
        float facing = Float3.Dot(ray.Direction, normal);
        Float3 through = _lineHasPlane ? _linePlane : Focus();
        float distance = MathF.Abs(facing) > 1e-4f ? Float3.Dot(through - ray.Origin, normal) / facing : 1f;
        return ray.Origin + ray.Direction * distance;
    }

    // The middle of the selected points, or of the drawn object
    private Float3 Focus()
    {
        (Float3 centre, int count) = SelectedCentre();
        if (count > 0) return centre;
        return _lineModel is { } target ? target.Transform.Position : Float3.Zero;
    }

    /// <summary>What the mouse is on for a line end: a point of the drawn object, the middle of an edge, an edge, else the drawing plane.</summary>
    private DrawEnd LineSnap(SceneToolContext ctx, ModelObject model)
    {
        HandleContext handles = ctx.Handles;
        Float4x4 toWorld = model.Transform.LocalToWorldMatrix;
        Float2 mouse = handles.MousePosition;
        ElementSelection hidden = _hidden[model];

        GeometryData.Vertex? nearVertex = null;
        float best = LineVertexRadius;
        foreach (GeometryData.Vertex vertex in model.Mesh.Vertices)
        {
            if (hidden.Vertices.Contains(vertex)) continue;
            if (handles.WorldToScreen(Float4x4.TransformPoint(vertex.Point, toWorld)) is not { } at || Float2.Distance(at, mouse) >= best) continue;
            best = Float2.Distance(at, mouse);
            nearVertex = vertex;
        }
        if (nearVertex != null) return new DrawEnd(Float4x4.TransformPoint(nearVertex.Point, toWorld), nearVertex);

        GeometryData.Edge? nearEdge = null;
        Float3 onEdge = default;
        best = float.PositiveInfinity;
        foreach (GeometryData.Edge edge in model.Mesh.Edges)
        {
            if (hidden.Edges.Contains(edge) || edge.IsHidden) continue;
            Float3 a = Float4x4.TransformPoint(edge.Vert1.Point, toWorld), b = Float4x4.TransformPoint(edge.Vert2.Point, toWorld);
            if (handles.WorldToScreen(a) is not { } pa || handles.WorldToScreen(b) is not { } pb) continue;
            if (Float2.Distance(pa, pb) < 1e-3f) continue;
            // The middle of an edge pulls from further away than the rest of it, and wins when both are in reach
            float toMiddle = Float2.Distance((pa + pb) * 0.5f, mouse) - LineMiddleRadius - LineEdgeRadius;
            float toEdge = Polygon2D.DistanceToSegment(mouse, pa, pb, out float t) - LineEdgeRadius;
            if (toMiddle < -LineEdgeRadius && toMiddle < best)
            {
                best = toMiddle;
                nearEdge = edge;
                onEdge = (a + b) * 0.5f;
            }
            else if (toEdge < 0f && toEdge < best)
            {
                best = toEdge;
                nearEdge = edge;
                onEdge = a + (b - a) * t;
            }
        }
        return nearEdge != null ? new DrawEnd(onEdge, null, nearEdge) : new DrawEnd(OnLinePlane(ctx));
    }

    // ================================================================
    //  Quad drawing
    // ================================================================

    private const float QuadSnapRadius = 14f;

    // New corners sit this far in front of the surface they are drawn on, in metres, so the faces are not drawn through it
    private const float QuadLift = 0.002f;

    private ModelObject? _quadModel;
    private readonly List<(Float3 World, GeometryData.Vertex? Existing)> _quadCorners = new();
    private GeometryData.Vertex? _quadHeld;
    private Float2 _quadPress;
    private Float3 _quadHeldStart;
    private bool _quadHeldMoved;

    /// <summary>
    /// Draws quads over the surfaces of the other model objects, as for retopology. Click four corners on a surface, near
    /// a point of the drawn object to share it. Drag such a point to slide it over the surface. Backspace takes back a
    /// corner, Escape leaves.
    /// </summary>
    private void StartQuads(SceneToolContext ctx)
    {
        _quadModel = DrawTarget();
        _quadCorners.Clear();
        _quadHeld = null;
        ctx.Handles.Modal = true;
        _operatorHandles = ctx.Handles;
    }

    private void EndQuads()
    {
        _quadModel = null;
        _quadCorners.Clear();
        _quadHeld = null;
        if (_operatorHandles != null) _operatorHandles.Modal = false;
        _operatorHandles = null;
    }

    private void TickQuads(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;
        if (_quadModel is not { } model || model.IsNotValid())
        {
            EndQuads();
            return;
        }
        ControlID owner = handles.GetControlID(ModalControl);
        handles.AddControl(owner, HandleContext.BodyDistance, float.MaxValue - 1f);
        Float4x4 toWorld = model.Transform.LocalToWorldMatrix;

        GeometryData.Vertex? near = NearestPoint(ctx, model);
        Float3 point = default;
        bool onSurface = near != null || SurfaceUnder(handles.MouseRay, out point);
        if (near != null) point = Float4x4.TransformPoint(near.Point, toWorld);
        else if (onSurface) point -= handles.MouseRay.Direction * QuadLift;

        Prowl.Graphite.Rendering.GizmoBatch batch = Runtime.Resources.Scene.Active.Gizmos.Batch;
        for (int i = 1; i < _quadCorners.Count; i++) batch.Line(_quadCorners[i - 1].World, _quadCorners[i].World, LineColor, 2f);
        if (onSurface)
        {
            if (_quadCorners.Count > 0) batch.Line(_quadCorners[^1].World, point, LineColor, 1.5f);
            if (_quadCorners.Count == 3) batch.Line(point, _quadCorners[0].World, LineColor, 1.5f);
            batch.Point(point, near != null ? SnapColor : LineColor, 8f);
        }
        ctx.Draw.Label(handles.MouseRay.Origin + handles.MouseRay.Direction * 5f, Loc.Get("model.quads_status", new { count = _quadCorners.Count }), PickedColor);

        if (handles.GetKeyDown(KeyCode.Backspace) && _quadCorners.Count > 0) _quadCorners.RemoveAt(_quadCorners.Count - 1);

        // A point pressed and dragged slides over the surface, pressed and let go it becomes a corner
        if (_quadHeld is { } held)
        {
            if (!_quadHeldMoved && Float2.Distance(handles.MousePosition, _quadPress) > 5f) _quadHeldMoved = true;
            if (_quadHeldMoved && SurfaceUnder(handles.MouseRay, out Float3 hit))
            {
                held.Point = Float4x4.TransformPoint(hit - handles.MouseRay.Direction * QuadLift, toWorld.Invert());
                model.RefreshPreview(dragging: true);
            }
            if (!Input.GetMouseButton(0))
            {
                _quadHeld = null;
                if (_quadHeldMoved)
                {
                    Float3 moved = held.Point;
                    held.Point = _quadHeldStart;
                    EditMeshes("Move Corner", (owner, mesh, _) =>
                    {
                        if (owner == model) held.Point = moved;
                    });
                }
                else AddCorner(ctx, model, Float4x4.TransformPoint(held.Point, toWorld), held);
            }
            return;
        }
        if (handles.ViewportHovered && Input.GetMouseButtonDown(0))
        {
            if (near != null && !_quadCorners.Exists(c => c.Existing == near))
            {
                _quadHeld = near;
                _quadPress = handles.MousePosition;
                _quadHeldStart = near.Point;
                _quadHeldMoved = false;
            }
            else if (onSurface && near == null) AddCorner(ctx, model, point, null);
        }
        else if (handles.GetKeyDown(KeyCode.Escape) || handles.GetKeyDown(KeyCode.Enter) || handles.ViewportHovered && Input.GetMouseButtonDown(1))
            EndQuads();
    }

    private void AddCorner(SceneToolContext ctx, ModelObject model, Float3 world, GeometryData.Vertex? existing)
    {
        _quadCorners.Add((world, existing));
        if (_quadCorners.Count < 4) return;
        Float4x4 toLocal = model.Transform.LocalToWorldMatrix.Invert();
        Float3[] corners = [.. _quadCorners.Select(c => Float4x4.TransformPoint(c.World, toLocal))];
        GeometryData.Vertex?[] shared = [.. _quadCorners.Select(c => c.Existing)];
        Float3 view = Float4x4.TransformNormal(ctx.Camera.GameObject.Transform.Forward, toLocal);
        _quadCorners.Clear();
        EditMeshes("Draw Quad", (owner, mesh, s) =>
        {
            if (owner == model) Operators.AddQuad(mesh, corners, shared, view, s);
        });
    }

    /// <summary>The nearest point of the other model objects' surfaces under a ray.</summary>
    private bool SurfaceUnder(Ray ray, out Float3 hit)
    {
        float? nearest = SurfaceHit(OtherModels())(ray, float.PositiveInfinity);
        hit = ray.Origin + ray.Direction * (nearest ?? 0f);
        return nearest != null;
    }

    private GeometryData.Vertex? NearestPoint(SceneToolContext ctx, ModelObject model)
    {
        Float4x4 toWorld = model.Transform.LocalToWorldMatrix;
        GeometryData.Vertex? best = null;
        float nearest = QuadSnapRadius;
        foreach (GeometryData.Vertex vertex in model.Mesh.Vertices)
        {
            if (_hidden[model].Vertices.Contains(vertex)) continue;
            if (ctx.Handles.WorldToScreen(Float4x4.TransformPoint(vertex.Point, toWorld)) is not { } at || Float2.Distance(at, ctx.Handles.MousePosition) >= nearest) continue;
            nearest = Float2.Distance(at, ctx.Handles.MousePosition);
            best = vertex;
        }
        return best;
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
            float distance = Polygon2D.DistanceToSegment(mouse, from, to, out float t);
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

    /// <summary>The gizmo at the middle of the selected points, moving, turning or scaling them through a session.</summary>
    private void SelectionGizmo(SceneToolContext ctx)
    {
        HandleContext handles = ctx.Handles;
        (Float3 centre, int count) = SelectedCentre();
        if (count == 0 || Gizmo == EditGizmo.Off)
        {
            _gizmoDragging = false;
            return;
        }

        if (!_gizmoDragging)
            _gizmoAxes = SceneTools.Orientation switch
            {
                PivotOrientation.Global => Quaternion.Identity,
                PivotOrientation.Normal => NormalFrame() ?? LocalFrame(),
                _ => LocalFrame(),
            };
        // A turn or scale keeps the gizmo where the drag began, a move follows the points
        Float3 position = Gizmo == EditGizmo.Move || !_gizmoDragging ? centre : _gizmoStart, before = position;
        Quaternion rotation = _gizmoTurn * _gizmoAxes;
        Float3 scale = _gizmoScale;
        OrigamiUI.Gizmo.TransformGizmoMode mode = Gizmo switch
        {
            EditGizmo.Rotate => OrigamiUI.Gizmo.TransformGizmoMode.Rotate,
            EditGizmo.Scale => OrigamiUI.Gizmo.TransformGizmoMode.ScaleAll,
            _ => OrigamiUI.Gizmo.TransformGizmoMode.Translate,
        };
        bool changed = TransformHandles.TransformHandle(handles, MoveHandle, mode, ref position, ref rotation, ref scale, out bool hot);
        if (hot && !_gizmoDragging)
        {
            _gizmoRaw = _gizmoApplied = Float3.Zero;
            _gizmoStart = before;
        }
        if (_gizmoDragging && !hot)
        {
            float falloff = _session?.Falloff ?? 0f;
            bool moved = _session != null;
            CancelSession();
            if (moved)
            {
                if (Gizmo == EditGizmo.Rotate)
                {
                    (Float3 axis, float degrees) = AxisAngle(_gizmoTurn);
                    TransformAsLast(TransformKind.Rotate, axis, degrees, Quaternion.Identity, falloff);
                }
                else if (Gizmo == EditGizmo.Scale) TransformAsLast(TransformKind.Scale, _gizmoScale, 0f, _gizmoAxes, falloff);
                else TransformAsLast(TransformKind.Translate, _gizmoApplied, 0f, Quaternion.Identity, falloff);
            }
            _gizmoTurn = Quaternion.Identity;
            _gizmoScale = Float3.One;
        }
        _gizmoDragging = hot;
        if (!changed || (_session ?? StartSession()) is not { } session) return;

        session.Pivot = _gizmoStart;
        Quaternion axes = _gizmoAxes;
        switch (Gizmo)
        {
            case EditGizmo.Rotate:
                _gizmoTurn = rotation * Quaternion.Inverse(_gizmoAxes);
                Quaternion turn = _gizmoTurn;
                PreviewSession(() => session.Rotate(turn));
                break;
            case EditGizmo.Scale:
                _gizmoScale = scale;
                PreviewSession(() => session.Scale(scale, axes));
                break;
            default:
                _gizmoRaw += position - before;
                _gizmoApplied = SceneTools.GridSnap != handles.Ctrl ? Round(_gizmoRaw, SceneTools.MoveSnap) : _gizmoRaw;
                Float3 total = _gizmoApplied;
                PreviewSession(() => session.Translate(total));
                break;
        }
    }

    private static (Float3 Axis, float Degrees) AxisAngle(Quaternion turn)
    {
        turn = Quaternion.Normalize(turn);
        if (turn.W < 0f) turn = new Quaternion(-turn.X, -turn.Y, -turn.Z, -turn.W);
        float half = MathF.Acos(Math.Clamp(turn.W, -1f, 1f)), sin = MathF.Sin(half);
        return sin > 1e-6f ? (new Float3(turn.X, turn.Y, turn.Z) / sin, half * 2f * 180f / MathF.PI) : (Float3.UnitY, 0f);
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
        if (handles.Alt && handles.ViewportHovered && Input.GetMouseButtonDown(0))
        {
            LoopClick(ctx);
            return;
        }

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
                    bool remove = handles.Ctrl || handles.Shift && _selection[model].Faces.Contains(face);
                    Choose(handles, model, s => s.Faces, face, remove: remove);
                    foreach (GeometryData.Face part in face.LogicalFace())
                        if (part != face) Choose(handles, model, s => s.Faces, part, adding: true, remove: remove);
                    handles.TryEndDrag(id);
                    return;
                }
            }

            if (Mode == SelectMode.Edge)
            {
                foreach (GeometryData.Edge edge in NearestFirst(ctx, model, mesh.Edges.Where(e => !hidden.Edges.Contains(e) && !e.IsHidden),
                             e => (Float4x4.TransformPoint(e.Vert1.Point, toWorld), Float4x4.TransformPoint(e.Vert2.Point, toWorld))))
                {
                    Float3 a = Float4x4.TransformPoint(edge.Vert1.Point, toWorld), b = Float4x4.TransformPoint(edge.Vert2.Point, toWorld);
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
                foreach (GeometryData.Vertex vertex in NearestFirst(ctx, model, mesh.Vertices.Where(v => !hidden.Vertices.Contains(v)),
                             v => (Float4x4.TransformPoint(v.Point, toWorld), Float4x4.TransformPoint(v.Point, toWorld))))
                {
                    Float3 p = Float4x4.TransformPoint(vertex.Point, toWorld);
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

    /// <summary>
    /// The element nearest the mouse on screen within reach that is in sight, as at most one, so only it is tested
    /// against the surface rather than every element each frame.
    /// </summary>
    private IEnumerable<T> NearestFirst<T>(SceneToolContext ctx, ModelObject model, IEnumerable<T> elements, Func<T, (Float3 A, Float3 B)> ends)
    {
        HandleContext handles = ctx.Handles;
        Float2 mouse = handles.MousePosition;
        var near = new List<(T Element, float Distance, Float3 At)>();
        foreach (T element in elements)
        {
            (Float3 a, Float3 b) = ends(element);
            if (handles.WorldToScreen(a) is not { } from || handles.WorldToScreen(b) is not { } to) continue;
            float distance = Polygon2D.DistanceToSegment(mouse, from, to, out float t);
            if (distance < PickRadius) near.Add((element, distance, a + (b - a) * t));
        }
        near.Sort((x, y) => x.Distance.CompareTo(y.Distance));
        foreach ((T element, _, Float3 at) in near)
            if (Seen(ctx, at))
            {
                yield return element;
                yield break;
            }
    }

    /// <summary>Shift toggles, Ctrl removes, a plain click replaces the selection of every edited object.</summary>
    private void Choose<T>(HandleContext handles, ModelObject model, Func<ElementSelection, HashSet<T>> set, T element, bool adding = false, bool? remove = null)
    {
        if (!adding && !handles.Shift && !handles.Ctrl)
            foreach (ModelObject other in _edited) _selection[other].Clear();
        ElementSelection selection = _selection[model];
        bool off = remove ?? (handles.Ctrl || handles.Shift && set(selection).Contains(element));
        if (off) set(selection).Remove(element);
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

    private static Float3 Centre(List<GeometryData.Face> faces) => GeometryData.Centroid(faces.SelectMany(f => f.NeighborVertices()));

    private static void Mark<T>(HashSet<T> set, T item, bool remove)
    {
        if (remove) set.Remove(item);
        else set.Add(item);
    }

    // ================================================================
    //  Drawing
    // ================================================================

    /// <summary>
    /// Draws the edited meshes into the scene's gizmos, which the renderer draws in one batch tested against the scene's
    /// depth: what is behind the surface is left out, or shown faint with X-ray.
    /// </summary>
    private void DrawElements(SceneToolContext ctx)
    {
        Prowl.Graphite.Rendering.GizmoBatch batch = XRay ? Runtime.Resources.Scene.Active.Gizmos.Batch : Runtime.Resources.Scene.Active.Gizmos.SurfaceBatch;
        foreach (ModelObject model in _edited)
        {
            Float4x4 toWorld = model.Transform.LocalToWorldMatrix;
            ElementSelection selection = _selection[model], hidden = _hidden[model];

            foreach (GeometryData.Face face in selection.Faces)
            {
                if (hidden.Faces.Contains(face)) continue;
                foreach ((GeometryData.Loop a, GeometryData.Loop b, GeometryData.Loop c) in GeometryUVs.Triangles(face))
                    batch.Triangle(Float4x4.TransformPoint(a.Vert.Point, toWorld), Float4x4.TransformPoint(b.Vert.Point, toWorld),
                        Float4x4.TransformPoint(c.Vert.Point, toWorld), FaceFill);
            }

            foreach (GeometryData.Edge edge in model.Mesh.Edges)
            {
                if (hidden.Edges.Contains(edge) || edge.IsHidden) continue;
                bool picked = selection.Edges.Contains(edge), seam = GeometryUVs.IsSeam(edge);
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
