// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.PropertyEditors;
using Prowl.Editor.GUI.Widgets;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.AnimationNodes;
using Prowl.Vector;

using TransitionEasing = Prowl.Motion.TransitionEasing;
using TransitionSync = Prowl.Motion.TransitionSync;

namespace Prowl.Editor.Inspector;

/// <summary>The editor for an <see cref="AnimationGraph"/> asset, its state machines and its parameters.</summary>
public class AnimationGraphWindow : DockPanel
{
    /// <summary>Every window currently open, so opening a graph can reuse one instead of stacking up.</summary>
    private static readonly List<AnimationGraphWindow> s_open = new();

    /// <summary>Copied nodes, shared by every window so a copy in one graph pastes into another.</summary>
    private static AnimationGraphView.Fragment? s_clipboard;

    /// <summary>The graphs opened on the way here through sub graph nodes, for the breadcrumb back.</summary>
    private readonly Stack<Guid> _parents = new();

    private Guid _assetGuid;
    private AnimationGraph? _graph;

    /// <summary>A locked window keeps its graph, so another one opens somewhere else.</summary>
    private bool _locked;

    private readonly AnimationGraphView _view = new();
    private readonly AnimationGraphStateView _stateView = new();
    private readonly NodeGraphController _controller = new();
    private readonly AnimationGraphProbe _probe = new();

    /// <summary>The latest edits, so entering play mode does not quietly throw them away.</summary>
    private EchoObject? _unsaved;

    /// <summary>True while this window is writing the editor selection, so it does not answer itself.</summary>
    private bool _publishing;

    // The add parameter popup.
    private bool _parameterPopup;
    private Float2 _parameterPopupAt;
    private string _newParameterName = "";
    private int _newParameterKind;

    /// <summary>Where the graph canvas is on screen, for taking a parameter dropped onto it.</summary>
    private Rect _graphRect;

    /// <summary>The state machine being looked inside, or null in the graph itself.</summary>
    private GraphNodeRecord? _insideMachine;

    /// <summary>Which graph inside the asset is shown: empty for the root, or a state or embedded sub graph id.</summary>
    private string _scope = string.Empty;

    /// <summary>A place on the breadcrumb: a graph inside the asset, and the state machine open in it, if any.</summary>
    private sealed record Place(string Scope, GraphNodeRecord? Machine);

    private bool _dirty;

    // The create popup, opened on the canvas or by dropping a wire in empty space.
    private bool _pickerOpen;
    private Float2 _pickerScreen, _pickerGraph;
    private string _pickerSearch = "";
    private NodePinKind? _pickerKind;
    private string? _pickerWireNode;

    // Which end a wire dropped in empty space came from: an output the new node takes in, or an input it feeds.
    private bool _pickerFromOutput = true;
    private string? _pickerWirePort;
    private bool _pickerFrame;
    private float _rootX, _rootY, _rootW, _rootH;

    public override string Title => _graph.IsValid() ? $"Graph: {_graph!.Name}" : "Animation Graph";

    public override string Icon => EditorIcons.DiagramProject;

    static AnimationGraphWindow()
    {
        SaveManager.OnSave += () =>
        {
            var saved = new List<string>();
            foreach (AnimationGraphWindow window in s_open)
            {
                if (!window._dirty || window._graph.IsNotValid()) continue;
                string name = window._graph!.Name;
                window.Save();
                if (!window._dirty) saved.Add(name);
            }
            return saved.Count == 0 ? null : "Graph: " + string.Join(", ", saved);
        };
    }

    public AnimationGraphWindow()
    {
        s_open.Add(this);
        Selection.OnSelectionChanged += OnEditorSelectionChanged;

        _view.ShowsCards = true;
        _view.Editing.EditHandler = (description, change, rebuild) => Set(description, change, rebuild);
        _view.Editing.EnterStatesHandler = EnterMachine;
        _view.Editing.OpenSubGraphHandler = OpenSubGraph;
        _view.Editing.ExtractHandler = ExtractToAsset;
    }

    public override void OnClosed()
    {
        s_open.Remove(this);
        Selection.OnSelectionChanged -= OnEditorSelectionChanged;
        ClearSelection();

        // Another window on the same graph carries the unsaved edits on, otherwise they are saved here.
        if (_dirty && !s_open.Exists(other => other._assetGuid == _assetGuid)) Save();
    }

    /// <summary>Shows a graph in the first window not locked to something else.</summary>
    public static void OpenFor(Guid assetGuid)
    {
        foreach (AnimationGraphWindow window in s_open)
        {
            if (window._locked) continue;

            window._parents.Clear();
            if (window._assetGuid != assetGuid) window.SwitchTo(assetGuid);
            EditorApplication.Instance?.FocusPanelInstance(window);
            return;
        }

        var panel = new AnimationGraphWindow();
        panel.Load(assetGuid);
        EditorApplication.Instance?.OpenPanelInstance(panel, 1240, 780);
    }

    public override bool SerializeState(System.Text.Json.Nodes.JsonObject state)
    {
        if (_assetGuid == Guid.Empty) return false;
        state["graph"] = _assetGuid.ToString();
        return true;
    }

    public override void RestoreState(System.Text.Json.Nodes.JsonObject state)
    {
        if (state["graph"]?.GetValue<string>() is { } text && Guid.TryParse(text, out Guid guid)) Load(guid);
    }

    /// <summary>Moves the window to another graph, saving unsaved edits first.</summary>
    private void SwitchTo(Guid assetGuid)
    {
        if (_dirty) Save();
        Load(assetGuid);
    }

    private void Load(Guid assetGuid)
    {
        _assetGuid = assetGuid;
        _graph = null;
        _seenContent = -1;
        _dirty = false;
        _unsaved = null;
        _scope = string.Empty;
        Resolve(keepPlace: false);
    }

    // The graph's content when the view was last built from it, so a refill (a reimport, a save, a revert) rebuilds it.
    private int _seenContent = -1;

    /// <summary>Loads the graph, and rebuilds the view after its content was refilled, carrying unsaved edits onto it.</summary>
    private void Resolve(bool keepPlace = true)
    {
        if (_assetGuid == Guid.Empty) return;
        if (_graph is { IsLoaded: true } current && current.ContentVersion == _seenContent) return;

        string? machine = keepPlace ? _insideMachine?.Id : null;
        List<string> selected = keepPlace ? new List<string>(_controller.SelectedNodes) : new List<string>();

        _graph = AssetDatabase.Load<AnimationGraph>(_assetGuid);
        if (_graph is not { IsLoaded: true }) _graph = null;
        _seenContent = _graph is null ? -1 : _graph.ContentVersion;
        _view.Graph = _graph;
        ClearSelection();

        if (_graph.IsValid() && _dirty && _unsaved != null) CopyRecords(_unsaved, _graph!, compiles: true);
        Reattach(machine);
        Reselect(selected);
    }

    /// <summary>Finds the open state machine again by id after its records were replaced, or steps out.</summary>
    private void Reattach(string? machineId)
    {
        if (!ScopeExists(_scope)) _scope = string.Empty;
        _insideMachine = machineId == null || _graph.IsNotValid() ? null
            : _graph!.Nodes.Find(node => node.Id == machineId && node.Type == AnimationNodeIds.StateMachine && node.Owner == _scope);
        _stateView.Machine = _insideMachine;
        _view.Invalidate();
        _stateView.Invalidate();
    }

    private void Reselect(List<string> ids)
    {
        if (ids.Count > 0) _controller.SelectNodes(ids);
        else _controller.ClearSelection();
    }

    /// <summary>Redraws other windows showing the same graph and shares the unsaved state with them.</summary>
    private void ShareWithSiblings()
    {
        foreach (AnimationGraphWindow other in s_open)
        {
            if (ReferenceEquals(other, this) || other._assetGuid != _assetGuid) continue;
            other._dirty = _dirty;
            other._unsaved = _unsaved;
            other.Reattach(other._insideMachine?.Id);
        }
    }

    private void ClearSelection()
    {
        if (OwnsSelection()) Selection.Clear();
    }

    private bool OwnsSelection()
    {
        foreach (object selected in Selection.Selected)
            if (selected is AnimationGraphSelection mine && ReferenceEquals(mine.Window, this)) return true;
        return false;
    }

    /// <summary>Puts what is selected in the graph into the editor selection, so the inspector shows it.</summary>
    private void Publish(List<AnimationGraphSelection> picked)
    {
        _publishing = true;
        try
        {
            if (picked.Count == 0)
            {
                ClearSelection();
                return;
            }

            Selection.Select(picked[0]);
            for (int i = 1; i < picked.Count; i++) Selection.AddToSelection(picked[i]);
        }
        finally
        {
            _publishing = false;
        }
    }

    /// <summary>Something else was selected in the editor, so the graph lets go of its own selection.</summary>
    private void OnEditorSelectionChanged()
    {
        if (_publishing || OwnsSelection()) return;
        _controller.ClearSelection();
    }

    private void PublishNodes(GraphSelection selection)
    {
        var picked = new List<AnimationGraphSelection>();
        foreach (GraphNode node in selection.Nodes)
            if (_view.RecordOf(node.Id) is { } record) picked.Add(new AnimationGraphSelection(this, node: record));
        Publish(picked);
    }

    private void PublishStates(GraphSelection selection)
    {
        var picked = new List<AnimationGraphSelection>();
        GraphNodeRecord? machine = _insideMachine;

        foreach (GraphNode node in selection.Nodes)
            if (_stateView.StateOf(node.Id) is { } state)
                picked.Add(new AnimationGraphSelection(this, machine: machine, state: state));

        foreach (GraphConnection wire in selection.Edges)
            if (_stateView.TransitionOf(wire) is { } transition && _stateView.StateOf(wire.FromNode) is { } from)
                picked.Add(new AnimationGraphSelection(this, machine: machine, state: from, transition: transition));

        Publish(picked);
    }

    /// <summary>Runs one edit and makes it undoable by snapshotting the whole graph.</summary>
    private void Edit(string description, Action change, bool rebuild = true, bool compiles = true)
    {
        if (_graph.IsNotValid()) return;

        EchoObject before = Snapshot();
        change();
        EditDone(description, before, coalesce: false, rebuild, compiles);
    }

    // Where things sit, groups and notes: nothing the graph compiles, so running animators keep going.
    private void EditLayout(string description, Action change) => Edit(description, change, compiles: false);

    /// <summary>Records an edit already made. A coalescing edit folds into the one before it.</summary>
    private void EditDone(string description, EchoObject before, bool coalesce, bool rebuild = true, bool compiles = true)
    {
        if (_graph.IsNotValid()) return;

        EchoObject after = Snapshot();
        _unsaved = after;

        // Tied to the asset the edit was made on, which may no longer be the one showing.
        Guid asset = _assetGuid;
        Action undo = () => Restore(asset, before, compiles);
        Action redo = () => Restore(asset, after, compiles);

        if (coalesce) Undo.RegisterCoalescableAction(description, undo, redo);
        else Undo.RegisterAction(description, undo, redo);
        Touch(rebuild, compiles);
    }

    private EchoObject Snapshot() => Serializer.Serialize(typeof(object), _graph!);

    private void Restore(Guid asset, EchoObject snapshot, bool compiles)
    {
        // The undo goes to whichever open window shows that graph, which need not be this one.
        AnimationGraphWindow? showing = s_open.Contains(this) && asset == _assetGuid ? this
            : s_open.Find(window => window._assetGuid == asset);

        if (showing == null)
        {
            // Nothing shows the graph any more, and it was saved when it was left, so the undo is saved too.
            AnimationGraph? other = AssetDatabase.Get<AnimationGraph>(asset);
            if (other.IsValid()) CopyRecords(snapshot, other!, compiles);
            WriteToDisk(asset, snapshot);
            return;
        }

        if (!showing.Apply(snapshot, compiles)) return;

        showing._unsaved = snapshot;
        showing._dirty = true;
        showing.ShareWithSiblings();
    }

    /// <summary>Writes a snapshot's records onto the live asset, leaving the asset itself in place.</summary>
    private bool Apply(EchoObject snapshot, bool compiles)
    {
        if (_graph.IsNotValid()) return false;

        string? machine = _insideMachine?.Id;
        var selected = new List<string>(_controller.SelectedNodes);
        if (!CopyRecords(snapshot, _graph!, compiles)) return false;

        ClearSelection();
        Reattach(machine);
        Reselect(selected);
        return true;
    }

    /// <summary>Copies a snapshot's records into a graph asset, keeping the asset itself.</summary>
    private static bool CopyRecords(EchoObject snapshot, AnimationGraph target, bool compiles)
    {
        var restored = Serializer.Deserialize<AnimationGraph>(snapshot.Clone());
        if (restored == null) return false;

        target.Nodes = restored.Nodes;
        target.Parameters = restored.Parameters;
        target.RootNode = restored.RootNode;
        target.Groups = restored.Groups;
        target.Notes = restored.Notes;
        if (compiles) target.Invalidate();
        return true;
    }

    /// <summary>After any edit: drops the compiled graph, when the edit changes it, so running animators pick it up.</summary>
    private void Touch(bool rebuild = true, bool compiles = true)
    {
        _dirty = true;
        if (compiles && _graph.IsValid()) _graph!.Invalidate();
        if (!rebuild) return;

        _view.Invalidate();
        _stateView.Invalidate();
        ShareWithSiblings();
    }

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;
        var m = Origami.Current.Metrics;

        Resolve();
        HandleShortcuts(paper);

        if (_graph.IsNotValid())
        {
            paper.Box("ag_empty").Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
                .Text("No animation graph is open.", font)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleCenter);
            return;
        }

        // A state machine or a graph that was deleted while it was open leaves nowhere to be.
        if (!ScopeExists(_scope)) GoTo(new Place(string.Empty, null));
        if (_insideMachine != null && !_graph!.Nodes.Contains(_insideMachine)) LeaveMachine();

        _probe.Refresh(_assetGuid);
        _view.Probe = _probe;
        _view.ShowLive = _probe.Live;
        _stateView.Probe = _probe;

        _view.Scope = _scope;
        _view.Sync();
        _stateView.Machine = _insideMachine;
        _stateView.Sync(_graph);

        _view.Tint();
        _stateView.Tint(_graph);

        using (paper.Column("ag_root").Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
            .OnPostLayout((h, r) =>
            {
                _rootX = (float)r.Min.X; _rootY = (float)r.Min.Y;
                _rootW = (float)r.Size.X; _rootH = (float)r.Size.Y;
            }).Enter())
        {
            DrawToolbar(paper, font, m);

            using (paper.Row("ag_body").Width(UnitValue.Stretch()).Height(UnitValue.Stretch()).Enter())
            {
                float panelW = Math.Clamp(width * 0.14f, 150f, 210f);
                if (_insideMachine != null) DrawStateGraph(paper, width - panelW, height - 44f);
                else DrawNodeGraph(paper, width - panelW, height - 44f);

                DrawPanel(paper, font, m, panelW, height - 44f);
            }

            if (_parameterPopup) DrawParameterPopup(paper, font, m);

            if (_pickerOpen)
            {
                if (paper.IsKeyPressed(PaperKey.Escape)) ClosePicker();
                else DrawPicker(paper, font, m);
            }
        }
    }

    private void DrawToolbar(Paper paper, Scribe.FontFile font, OrigamiMetrics m)
    {
        using (paper.Row("ag_toolbar").Width(UnitValue.Stretch()).Height(40)
            .Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).Gap(m.SpacingMedium)
            .BackgroundColor(EditorTheme.Neutral200)
            .AlignItems(LayoutAlignment.Center).Enter())
        {
            Origami.Breadcrumb(paper, "ag_path", Trail(), OnCrumb).ShowIcons().HighlightLast().Show();
            paper.Box("ag_gap").Width(UnitValue.Stretch()).Height(1).IsNotInteractable();

            DrawWatch(paper, font);
            EditorGUI.PillButton(paper, "ag_add", "Add Node", false, () => OpenPicker(paper, NextFreeSpot(), null, null, frame: true));
            EditorGUI.PillButton(paper, "ag_frame", "Frame All", false, () => _controller.FrameAll());
            EditorGUI.PillButton(paper, "ag_lock", _locked ? "Locked" : "Lock", _locked, () => _locked = !_locked)
                .Tooltip("A locked window keeps this graph, so opening another one uses a new window");
            EditorGUI.PillButton(paper, "ag_save", _dirty ? "Save" : "Saved", _dirty, Save);
        }
    }

    /// <summary>Which animator the live readouts come from, when the scene has more than one on this graph.</summary>
    private void DrawWatch(Paper paper, Scribe.FontFile font)
    {
        if (!Application.IsPlaying) return;

        if (_probe.Candidates.Count == 0)
        {
            paper.Box("ag_watchNone").Width(UnitValue.Auto).Height(26)
                .Padding(10, 10, 0, 0).IsNotInteractable()
                .Text("nothing is running this graph", font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
            return;
        }

        string[] names = new string[_probe.Candidates.Count];
        for (int i = 0; i < names.Length; i++) names[i] = _probe.Candidates[i].GameObject.Name;

        int selected = 0;
        for (int i = 0; i < _probe.Candidates.Count; i++)
            if (ReferenceEquals(_probe.Candidates[i], _probe.Animator)) selected = i;

        Origami.Dropdown(paper, "ag_watch", selected, v => _probe.Watch(_probe.Candidates[v]), names)
            .Width(160).Show();
    }

    /// <summary>The breadcrumb trail: the graph assets passed through, then each graph down to what is showing.</summary>
    private List<BreadcrumbItem> Trail()
    {
        var path = new List<BreadcrumbItem>();

        Guid[] parents = _parents.ToArray();
        for (int i = parents.Length - 1; i >= 0; i--)
        {
            AnimationGraph? graph = AssetDatabase.Get<AnimationGraph>(parents[i]);
            path.Add(new BreadcrumbItem(graph.IsValid() ? graph!.Name : "Graph", EditorIcons.DiagramProject_I, parents[i]));
        }

        // Walked from where the window is back up to the asset's own graph, then turned round.
        var inside = new List<BreadcrumbItem>();
        if (_insideMachine != null)
            inside.Add(new BreadcrumbItem(_insideMachine.Title, EditorIcons.CircleNodes_I, new Place(_insideMachine.Owner, _insideMachine)));

        string scope = _scope;
        while (scope.Length > 0)
        {
            if (StateOwning(scope, out GraphNodeRecord? machine) is { } state)
            {
                inside.Add(new BreadcrumbItem(state.Name, EditorIcons.CirclePlay_I, new Place(scope, null)));
                inside.Add(new BreadcrumbItem(machine!.Title, EditorIcons.CircleNodes_I, new Place(machine!.Owner, machine)));
                scope = machine.Owner;
            }
            else if (_graph!.Find(scope) is { } sub)
            {
                inside.Add(new BreadcrumbItem(sub.Title, EditorIcons.DiagramProject_I, new Place(scope, null)));
                scope = sub.Owner;
            }
            else break;
        }

        path.Add(new BreadcrumbItem(_graph!.Name, EditorIcons.DiagramProject_I, new Place(string.Empty, null)));
        inside.Reverse();
        path.AddRange(inside);
        return path;
    }

    private void OnCrumb(BreadcrumbItem item)
    {
        if (item.UserData is Place place) GoTo(place);
        else if (item.UserData is Guid guid && guid != _assetGuid) OpenParent(guid);
    }

    /// <summary>The state a scope id names, and the machine it belongs to.</summary>
    private GraphStateRecord? StateOwning(string scope, out GraphNodeRecord? machine)
    {
        machine = null;
        if (_graph.IsNotValid() || scope.Length == 0) return null;

        foreach (GraphNodeRecord record in _graph!.Nodes)
            foreach (GraphStateRecord state in record.States)
                if (state.Id == scope)
                {
                    machine = record;
                    return state;
                }
        return null;
    }

    private bool ScopeExists(string scope)
        => scope.Length == 0 || _graph.IsValid() && (_graph!.Find(scope) != null || StateOwning(scope, out _) != null);

    /// <summary>Shows one of the graphs inside the asset, with a state machine open in it or not.</summary>
    private void GoTo(Place place)
    {
        _scope = ScopeExists(place.Scope) ? place.Scope : string.Empty;
        if (place.Machine != null && place.Machine.Owner == _scope) EnterMachine(place.Machine);
        else LeaveMachine();
        _view.Scope = _scope;
        _controller.FrameAll();
    }

    /// <summary>Opens a state's graph: its own nodes, or the graph asset it plays.</summary>
    private void EnterState(GraphStateRecord state)
    {
        if (state.UsesAsset) OpenAsset(state.Graph.AssetID);
        else GoTo(new Place(state.Id, null));
    }

    private void EnterEmbedded(GraphNodeRecord record) => GoTo(new Place(record.Id, null));

    private void HandleShortcuts(Paper paper)
    {
        if (_pickerOpen || ShortcutManager.IsRebinding) return;

        float x = (float)paper.PointerPos.X, y = (float)paper.PointerPos.Y;
        if (x < _rootX || y < _rootY || x > _rootX + _rootW || y > _rootY + _rootH) return;

        if (ShortcutManager.IsPressed("GraphEditor/Delete")) _controller.DeleteSelection();
        else if (ShortcutManager.IsPressed("GraphEditor/SelectAll")) _controller.SelectAll();
        else if (ShortcutManager.IsPressed("GraphEditor/FrameSelection")) _controller.FrameSelection();
        else if (ShortcutManager.IsPressed("GraphEditor/Recenter")) _controller.FrameAll();
        else if (ShortcutManager.IsPressed("GraphEditor/GroupSelection")) GroupNodes(SelectedNodes());

        if (_insideMachine != null) return;

        if (ShortcutManager.IsPressed("GraphEditor/Copy")) CopyNodes(SelectedNodes());
        else if (ShortcutManager.IsPressed("GraphEditor/Paste")) PasteNodes();
        else if (ShortcutManager.IsPressed("GraphEditor/Duplicate")) DuplicateNodes(SelectedNodes());
    }

    private void LeaveMachine()
    {
        _insideMachine = null;
        _stateView.Machine = null;
        ClearSelection();
    }

    private void EnterMachine(GraphNodeRecord machine)
    {
        _insideMachine = machine;
        _stateView.Machine = machine;
        _stateView.Invalidate();
        ClearSelection();
        _controller.FrameAll();
    }

    private void Save()
    {
        if (!_dirty || _graph.IsNotValid()) return;
        if (!WriteToDisk(_assetGuid, Snapshot())) return;

        _dirty = false;
        _unsaved = null;
        _seenContent = _graph!.ContentVersion;
        ShareWithSiblings();
    }

    private static bool WriteToDisk(Guid asset, EchoObject snapshot)
        => EditorAssetBackend.Instance?.SaveAsset(asset, snapshot) ?? false;

    private void DrawNodeGraph(Paper paper, float width, float height)
    {
        using (paper.Box("ag_graphHost").Width(width).Height(height).OnPostLayout((_, r) => _graphRect = r).Enter())
        {
            DrawNodeGraphWidget(paper, width, height);
            AcceptParameterDrop(paper);
        }
    }

    /// <summary>A parameter dragged in from the list becomes a node reading it, where it was let go.</summary>
    private void AcceptParameterDrop(Paper paper)
    {
        if (!DragDrop.IsDropFrame || DragDrop.Payload is not AnimationParameterDrag) return;

        Float2 pointer = new((float)paper.PointerPos.X, (float)paper.PointerPos.Y);
        if (pointer.X < _graphRect.Min.X || pointer.Y < _graphRect.Min.Y || pointer.X > _graphRect.Max.X || pointer.Y > _graphRect.Max.Y)
            return;

        AnimationParameterDrag? drag = DragDrop.AcceptDrop<AnimationParameterDrag>(true);
        if (drag == null) return;

        Float2 at = _controller.ScreenToGraph(pointer);
        GraphNodeRecord? added = null;
        Edit($"Add {drag.Name}", () =>
        {
            added = _view.Add(AnimationNodeIds.Parameter, at);
            if (added != null) added.Properties[ParameterNode.NameSetting.Key] = NodeValue.FromText(drag.Name);
        });
        if (added != null) _controller.SelectNodes(new[] { added.Id });
    }

    private void DrawNodeGraphWidget(Paper paper, float width, float height)
    {
        Organise(Origami.NodeGraph(paper, "ag_graph", width, height), paper, _view.Move)
            .Nodes(_view.Nodes)
            .Connections(_view.Wires)
            .Groups(_view.Groups)
            .Stickies(_view.Notes)
            .Controller(_controller)
            .Minimap()
            .SnapToGrid(12f)
            .HostShortcuts()
            .OnSelectionChanged(PublishNodes)
            .OnNodesMoved((nodes, delta) => EditLayout("Move Nodes", () => _view.Move(nodes, delta)))
            .OnValidateConnection(_view.Validate)
            .OnConnect(request => Edit("Connect Node", () => _view.Connect(request)))
            .OnDisconnect(wire => Edit("Disconnect Node", () => _view.Disconnect(wire)))
            .OnDeleteSelection(sel => Edit("Delete", () => DeleteSelection(sel)))
            .OnNodeToggleCollapsed(node => EditLayout("Collapse Node", () => _view.ToggleCollapsed(node)))
            .OnNodeDoubleClick(node =>
            {
                if (_view.RecordOf(node.Id) is { } record && AnimationNodeRegistry.Get(record.Type) is { } type)
                    AnimationNodeEditor.For(type).Open(_view.Editing, record);
            })
            .OnNodesContext((nodes, _) => OpenNodesMenu(paper, nodes))
            .OnBackgroundContext(at => OpenPicker(paper, at, null, null))
            .OnDropWireInEmpty((at, fromNode, fromPort, fromOutput) =>
            {
                // Dropping a wire says what the new node has to fit, so the catalog is filtered to it.
                GraphNodeRecord? end = _view.RecordOf(fromNode);
                if (end == null) return;

                NodePinKind? kind = fromOutput ? _view.OutputKind(end)
                    : AnimationNodeRegistry.Get(end.Type)?.PinAt(AnimationGraphView.PinIndex(fromPort))?.Kind;

                OpenPicker(paper, at, kind, fromNode);
                _pickerFromOutput = fromOutput;
                _pickerWirePort = fromOutput ? null : fromPort;
            })
            .OnNodeContext((node, _) => OpenNodeMenu(paper, node))
            .Show();
    }

    private void OpenNodeMenu(Paper paper, GraphNode node)
    {
        GraphNodeRecord? record = _view.RecordOf(node.Id);
        if (record == null) return;

        AnimationGraphNode? type = AnimationNodeRegistry.Get(record.Type);
        bool isPose = type is { IsPose: true };

        Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b =>
        {
            b.Header(record.Title);
            if (AnimationGraphView.IsOutputNode(record)) return;

            if (type != null) AnimationNodeEditor.For(type).AddMenuItems(_view.Editing, record, b);
            if (type != null && HasDrivable(type))
                b.Submenu("Expose As Input", sub =>
                {
                    foreach (NodeSetting property in type.Properties)
                    {
                        if (!property.Drivable || !type.ShowsProperty(record, property.Key)) continue;
                        NodeSetting setting = property;
                        sub.Toggle(setting.Label,
                            () => Edit($"Expose {setting.Label}", () => AnimationNodeCard.Expose(record, setting,
                                !record.PropertyInputs.ContainsKey(setting.Key))),
                            () => record.PropertyInputs.ContainsKey(setting.Key));
                    }
                });

            if (_scope.Length == 0)
                b.Item("Set As Output", () => Edit("Set Output Node", () => _view.SetRoot(record.Id)), enabled: isPose);
            b.Separator();
            b.Item("Copy", () => CopyNodes(new[] { node }), shortcut: Keys("GraphEditor/Copy"));
            b.Item("Duplicate", () => DuplicateNodes(new[] { node }), shortcut: Keys("GraphEditor/Duplicate"));
            b.Item("Group", () => GroupNodes(new[] { node }), shortcut: Keys("GraphEditor/GroupSelection"));
            b.Separator();
            b.Item("Delete", () => Edit("Delete Node", () =>
            {
                _view.Delete(new[] { node }, Array.Empty<GraphConnection>());
                ClearSelection();
            }), danger: true);
        });
    }

    /// <summary>The rig the editor picks bones and clips from. Nothing the graph compiles reads it.</summary>
    private Avatar? Rig => _graph.IsValid() ? _graph!.Rig : null;

    private static bool HasDrivable(AnimationGraphNode type)
    {
        foreach (NodeSetting property in type.Properties)
            if (property.Drivable) return true;
        return false;
    }

    private void OpenNodesMenu(Paper paper, IReadOnlyList<GraphNode> nodes)
    {
        Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b => b
            .Header($"{nodes.Count} nodes")
            .Item("Copy", () => CopyNodes(nodes), shortcut: Keys("GraphEditor/Copy"))
            .Item("Duplicate", () => DuplicateNodes(nodes), shortcut: Keys("GraphEditor/Duplicate"))
            .Item("Group", () => GroupNodes(nodes), shortcut: Keys("GraphEditor/GroupSelection"))
            .Separator()
            .Item("Delete", () => Edit("Delete Nodes", () =>
            {
                _view.Delete(nodes, Array.Empty<GraphConnection>());
                ClearSelection();
            }), danger: true));
    }

    private void MoveGroup(GraphGroup group, IReadOnlyList<GraphNode> members, Float2 delta, Action<IReadOnlyList<GraphNode>, Float2> moveMembers)
        => EditLayout("Move Group", () =>
        {
            if (AnimationGraphView.GroupOf(group) is { } record) record.Position += delta;
            moveMembers(members, delta);
        });

    private void ResizeGroup(GraphGroup group, Float2 position, Float2 size) => EditLayout("Resize Group", () =>
    {
        if (AnimationGraphView.GroupOf(group) is not { } record) return;
        record.Position = position;
        record.Size = size;
    });

    private void RenameGroup(GraphGroup group, string title) => EditLayout("Rename Group", () =>
    {
        if (AnimationGraphView.GroupOf(group) is { } record) record.Title = title;
    });

    private void MoveNote(GraphSticky note, Float2 delta) => EditLayout("Move Note", () =>
    {
        if (AnimationGraphView.NoteOf(note) is { } record) record.Position += delta;
    });

    private void ResizeNote(GraphSticky note, Float2 position, Float2 size) => EditLayout("Resize Note", () =>
    {
        if (AnimationGraphView.NoteOf(note) is not { } record) return;
        record.Position = position;
        record.Size = size;
    });

    private void EditNote(GraphSticky note, string text) => EditLayout("Edit Note", () =>
    {
        if (AnimationGraphView.NoteOf(note) is { } record) record.Text = text;
    });

    private void OpenGroupMenu(Paper paper, GraphGroup group)
    {
        Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b => b
            .Header(group.Title)
            .Item("Delete Group", () => EditLayout("Delete Group", () =>
            {
                if (AnimationGraphView.GroupOf(group) is { } record) _graph!.Groups.Remove(record);
            }), danger: true));
    }

    private void OpenNoteMenu(Paper paper, GraphSticky note)
    {
        Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b => b
            .Header("Note")
            .Item("Delete Note", () => EditLayout("Delete Note", () =>
            {
                if (AnimationGraphView.NoteOf(note) is { } record) _graph!.Notes.Remove(record);
            }), danger: true));
    }

    /// <summary>What the canvas right click offers inside a state machine: a state, a note or a group.</summary>
    private void OpenStateBackgroundMenu(Paper paper, Float2 at)
    {
        string owner = _insideMachine!.Id;
        Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b => b
            .Item("Add State", () =>
            {
                GraphStateRecord? added = null;
                Edit("Add State", () => added = _stateView.Add(at));
                if (added != null) _controller.SelectNodes(new[] { AnimationGraphStateView.StateId(added.Name) });
            })
            .Item("Add Any State", () =>
            {
                GraphStateRecord? any = null;
                Edit("Add Any State", () => any = _stateView.AddAny(at));
                if (any != null) _controller.SelectNodes(new[] { AnimationGraphStateView.StateId(any.Name) });
            }, enabled: !_stateView.HasAny)
            .Separator()
            .Item("Add Note", () => EditLayout("Add Note", () => _view.AddNote(at, owner)))
            .Item("Add Group", () => Edit("Add Group", () => _view.AddGroup(SelectedNodes(), at, owner)),
                shortcut: Keys("GraphEditor/GroupSelection")));
    }

    /// <summary>Everything the widget reports as selected goes: nodes, wires, groups and notes.</summary>
    private void DeleteSelection(GraphSelection selection)
    {
        if (_insideMachine != null) _stateView.Delete(selection.Nodes, selection.Edges);
        else _view.Delete(selection.Nodes, selection.Edges);

        foreach (GraphGroup group in selection.Groups)
            if (AnimationGraphView.GroupOf(group) is { } record) _graph!.Groups.Remove(record);
        foreach (GraphSticky note in selection.Stickies)
            if (AnimationGraphView.NoteOf(note) is { } record) _graph!.Notes.Remove(record);
        ClearSelection();
    }

    /// <summary>The selected cards of whichever graph is showing, the node graph or a state machine.</summary>
    private List<GraphNode> SelectedNodes()
    {
        var selected = new List<GraphNode>();
        IReadOnlyList<GraphNode> nodes = _insideMachine != null ? _stateView.Nodes : _view.Nodes;
        foreach (GraphNode node in nodes)
            if (_controller.SelectedNodes.Contains(node.Id)) selected.Add(node);
        return selected;
    }

    private List<GraphNodeRecord> RecordsOf(IReadOnlyList<GraphNode> nodes)
    {
        var records = new List<GraphNodeRecord>();
        foreach (GraphNode node in nodes)
            if (_view.RecordOf(node.Id) is { } record) records.Add(record);
        return records;
    }

    private void CopyNodes(IReadOnlyList<GraphNode> nodes)
    {
        List<GraphNodeRecord> records = RecordsOf(nodes);
        if (records.Count > 0) s_clipboard = _view.Capture(records);
    }

    private void PasteNodes()
    {
        if (s_clipboard == null || s_clipboard.Nodes.Count == 0) return;

        List<GraphNodeRecord> copies = new();
        Edit("Paste Nodes", () => copies = _view.Paste(s_clipboard, new Float2(40f, 40f)));
        SelectCopies(copies, frame: true);
    }

    private void DuplicateNodes(IReadOnlyList<GraphNode> nodes)
    {
        List<GraphNodeRecord> records = RecordsOf(nodes);
        if (records.Count == 0) return;

        List<GraphNodeRecord> copies = new();
        Edit(records.Count == 1 ? "Duplicate Node" : "Duplicate Nodes", () => copies = _view.Copy(records, new Float2(28f, 28f)));
        SelectCopies(copies, frame: false);
    }

    private void GroupNodes(IReadOnlyList<GraphNode> nodes)
    {
        Float2 at = nodes.Count > 0 ? nodes[0].Position : NextFreeSpot();
        string owner = _insideMachine?.Id ?? _scope;
        EditLayout("Group Nodes", () => _view.AddGroup(nodes, at, owner));
    }

    private void SelectCopies(List<GraphNodeRecord> copies, bool frame)
    {
        if (copies.Count == 0) return;

        var ids = new List<string>(copies.Count);
        foreach (GraphNodeRecord copy in copies) ids.Add(copy.Id);

        _controller.SelectNodes(ids);
        if (frame) _controller.FrameSelection();
    }

    /// <summary>Opens the graph a sub graph node runs, with the way back on the breadcrumb.</summary>
    private void OpenSubGraph(GraphNodeRecord record)
    {
        if (record.Get(SubGraphNode.Embedded))
        {
            EnterEmbedded(record);
            return;
        }

        if (record.Get(SubGraphNode.GraphAssetSetting) is { } value) OpenAsset(value.Graph.AssetID);
    }

    private void OpenAsset(Guid guid)
    {
        if (guid == Guid.Empty || guid == _assetGuid) return;

        Guid from = _assetGuid;
        SwitchTo(guid);
        _parents.Push(from);
    }

    /// <summary>Saves an embedded graph as its own asset, with the parameters, and points the state or node at it.</summary>
    private void ExtractToAsset(string owner, string name, Action<AnimationGraph> use)
    {
        if (_graph.IsNotValid() || Project.Current == null || EditorAssetBackend.Instance == null) return;

        EditorApplication.OpenFileDialog(FileDialogMode.Save, path =>
        {
            if (path == null || Project.Current == null || _graph.IsNotValid()) return;

            string relative = EditorAssetBackend.NormalizePath(Path.GetRelativePath(Project.Current.AssetsPath, path));
            if (!relative.EndsWith(".animgraph", StringComparison.OrdinalIgnoreCase)) relative += ".animgraph";

            Edit("Extract To Asset", () =>
            {
                AnimationGraph extracted = Extract(owner, name);
                EditorAssetBackend.Instance!.CreateAsset(extracted, relative);
                use(extracted);
                AnimationGraphView.RemoveInside(_graph!, new[] { owner });
            });
        }, Project.Current.AssetsPath, new[] { "*.animgraph" }, new[] { "Animation Graph" });
    }

    /// <summary>A new graph asset holding what is inside one owner, lifted to be that asset's own graph.</summary>
    private AnimationGraph Extract(string owner, string name)
    {
        var extracted = new AnimationGraph { Name = name };
        HashSet<string> scopes = AnimationGraphView.Inside(_graph!, new[] { owner });

        foreach (GraphNodeRecord record in _graph!.Nodes)
        {
            if (!scopes.Contains(record.Owner)) continue;
            GraphNodeRecord copy = record.Clone();
            if (copy.Owner == owner) copy.Owner = string.Empty;
            extracted.Nodes.Add(copy);
        }
        foreach (GraphGroupRecord group in _graph.Groups)
            if (scopes.Contains(group.Owner))
            {
                GraphGroupRecord copy = group.Clone();
                if (copy.Owner == owner) copy.Owner = string.Empty;
                extracted.Groups.Add(copy);
            }
        foreach (GraphNoteRecord note in _graph.Notes)
            if (scopes.Contains(note.Owner))
            {
                GraphNoteRecord copy = note.Clone();
                if (copy.Owner == owner) copy.Owner = string.Empty;
                extracted.Notes.Add(copy);
            }

        extracted.Parameters.AddRange(_graph.Parameters.ConvertAll(parameter => parameter.Clone()));

        // A sub graph's output becomes the root. A state's output stays, since the state still reads its Enter and Exit.
        GraphNodeRecord? output = extracted.OwnedNode(string.Empty, AnimationNodeIds.GraphOutput);
        if (output != null)
        {
            extracted.RootNode = output.Inputs.Count > 0 ? output.Inputs[0].Node : string.Empty;
            extracted.Nodes.Remove(output);
        }
        return extracted;
    }

    /// <summary>Steps back up the breadcrumb to a graph opened earlier.</summary>
    private void OpenParent(Guid guid)
    {
        while (_parents.Count > 0)
            if (_parents.Pop() == guid) break;
        SwitchTo(guid);
    }

    private static string Keys(string id)
        => ShortcutManager.GetBinding(id) is { } binding ? ShortcutManager.GetDisplayString(binding) : "";

    private void DrawStateGraph(Paper paper, float width, float height)
    {
        Organise(Origami.NodeGraph(paper, "ag_states", width, height), paper, _stateView.Move)
            .Nodes(_stateView.Nodes)
            .Connections(_stateView.Wires)
            .Groups(_stateView.Groups)
            .Stickies(_stateView.Notes)
            .Controller(_controller)
            .Minimap()
            .SnapToGrid(12f)
            .HostShortcuts()
            .Arrows()
            .OnSelectionChanged(PublishStates)
            .OnNodesMoved((nodes, delta) => EditLayout("Move States", () => _stateView.Move(nodes, delta)))
            .OnValidateConnection(_stateView.CanConnect)
            .OnConnect(request => Edit("Add Transition", () => _stateView.Connect(request)))
            .OnDisconnect(wire => Edit("Remove Transition", () => _stateView.Disconnect(wire)))
            .OnDeleteSelection(sel => Edit("Delete", () => DeleteSelection(sel)))
            .OnNodeDoubleClick(node =>
            {
                if (_stateView.StateOf(node.Id) is { IsAny: false } state) EnterState(state);
            })
            .OnBackgroundContext(at => OpenStateBackgroundMenu(paper, at))
            .OnNodeContext((node, _) => OpenStateMenu(paper, node))
            .Show();
    }

    private void OpenStateMenu(Paper paper, GraphNode node)
    {
        GraphStateRecord? state = _stateView.StateOf(node.Id);
        if (state == null) return;

        Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b =>
        {
            b.Header(state.Name);
            if (!state.IsAny)
                b.Item("Open Graph", () => EnterState(state))
                    .Item("Extract To Asset", () => ExtractToAsset(state.Id, state.Name, asset => state.Graph = asset), enabled: !state.UsesAsset)
                    .Item("Set As Default", () => Edit("Set Default State", () => _stateView.SetDefault(state)), enabled: !state.IsDefault)
                    .Separator();
            b.Item("Delete", () => Edit(state.IsAny ? "Delete Any State" : "Delete State", () =>
            {
                _stateView.Delete(new[] { node }, Array.Empty<GraphConnection>());
                ClearSelection();
            }), danger: true);
        });
    }

    /// <summary>Where a node added from the toolbar goes: below everything already placed.</summary>
    private Float2 NextFreeSpot()
    {
        if (_view.Nodes.Count == 0) return new Float2(0f, 0f);

        float x = float.MaxValue, y = float.MinValue;
        foreach (GraphNode node in _view.Nodes)
        {
            x = MathF.Min(x, node.Position.X);
            y = MathF.Max(y, node.Position.Y);
        }
        return new Float2(x, y + 140f);
    }

    private void OpenPicker(Paper paper, Float2 graphPosition, NodePinKind? kind, string? wireFrom, bool frame = false)
    {
        if (_insideMachine != null) return;

        _pickerOpen = true;
        _pickerGraph = graphPosition;
        _pickerScreen = new Float2((float)paper.PointerPos.X, (float)paper.PointerPos.Y);
        _pickerSearch = "";
        _pickerKind = kind;
        _pickerWireNode = wireFrom;
        _pickerFrame = frame;
        _pickerFromOutput = true;
        _pickerWirePort = null;
    }

    private void ClosePicker()
    {
        _pickerOpen = false;
        _pickerWireNode = null;
        _pickerKind = null;
    }

    private void DrawPicker(Paper paper, Scribe.FontFile font, OrigamiMetrics m)
    {
        const float w = 280f, h = 360f;
        float x = Math.Clamp(_pickerScreen.X - _rootX, 4f, MathF.Max(4f, _rootW - w - 4f));
        float y = Math.Clamp(_pickerScreen.Y - _rootY, 4f, MathF.Max(4f, _rootH - h - 4f));

        paper.Box("ag_pickBackdrop").PositionType(PositionType.SelfDirected).Left(0).Top(0)
            .Width(UnitValue.Percentage(100)).Height(UnitValue.Percentage(100)).Layer(Layer.Overlay)
            .OnClick(0, (_, _) => ClosePicker());

        using (paper.Column("ag_picker").PositionType(PositionType.SelfDirected).Left(x).Top(y)
            .Width(w).Height(UnitValue.Auto).MaxHeight(h).Layer(Layer.Overlay + 10)
            .BackgroundColor(EditorTheme.Neutral200).Rounded(m.ContainerRounding)
            .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .Padding(m.Padding, m.Padding, m.Padding, m.Padding).Gap(m.Spacing)
            .BoxShadow(0, 8, 24, 0, System.Drawing.Color.FromArgb(120, 0, 0, 0)).Enter())
        {
            Origami.SearchField(paper, "ag_pickSearch", _pickerSearch, v => _pickerSearch = v,
                _pickerKind.HasValue ? $"{AnimationGraphView.KindLabel(_pickerKind.Value)} nodes..." : "Search nodes...").Show();

            List<AnimationGraphNode> matches = Matches();
            List<(string Label, string Hint, Action Create)> extras = Extras();
            if (matches.Count == 0 && extras.Count == 0)
            {
                paper.Box("ag_pickNone").Width(UnitValue.Stretch()).Height(m.RowHeight).IsNotInteractable()
                    .Text("Nothing matches.", font).TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
                return;
            }

            Origami.ScrollView(paper, "ag_pickList", w - 16f, h - 60f).Body(() =>
            {
                if (extras.Count > 0)
                {
                    paper.Box("ag_pickCatExtra").Width(UnitValue.Stretch()).Height(20).IsNotInteractable()
                        .Margin(m.Padding, 0, m.Spacing, 0)
                        .Text("Organise", font).TextColor(EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSizeSmall * 0.9f).Alignment(TextAlignment.MiddleLeft);

                    for (int i = 0; i < extras.Count; i++)
                    {
                        (string label, string hint, Action create) = extras[i];
                        paper.Box($"ag_pickExtra_{i}").Width(UnitValue.Stretch()).Height(m.RowHeight)
                            .Padding(m.Padding, m.Padding, 0, 0).Rounded(m.SmallRounding)
                            .Hovered.BackgroundColor(EditorTheme.Neutral400).End()
                            .Text(label, font).TextColor(EditorTheme.Ink700)
                            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft)
                            .Tooltip(hint)
                            .OnClick(0, (_, _) => { ClosePicker(); create(); });
                    }
                }

                string? category = null;
                for (int i = 0; i < matches.Count; i++)
                {
                    AnimationGraphNode type = matches[i];
                    if (type.Category != category)
                    {
                        category = type.Category;
                        paper.Box($"ag_pickCat_{i}").Width(UnitValue.Stretch()).Height(20).IsNotInteractable()
                            .Margin(m.Padding, 0, m.Spacing, 0)
                            .Text(category, font).TextColor(EditorTheme.Ink300)
                            .FontSize(EditorTheme.FontSizeSmall * 0.9f).Alignment(TextAlignment.MiddleLeft);
                    }

                    AnimationGraphNode captured = type;
                    paper.Box($"ag_pick_{i}").Width(UnitValue.Stretch()).Height(m.RowHeight)
                        .Padding(m.Padding, m.Padding, 0, 0).Rounded(m.SmallRounding)
                        .Hovered.BackgroundColor(EditorTheme.Neutral400).End()
                        .Text(captured.DisplayName, font).TextColor(EditorTheme.Ink700)
                        .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft)
                        .Tooltip(captured.Description)
                        .OnClick(0, (_, _) => Create(captured));
                }
            });
        }
    }

    /// <summary>The things that are not nodes: a note and a group. Left out when a wire was dropped.</summary>
    private List<(string Label, string Hint, Action Create)> Extras()
    {
        var extras = new List<(string, string, Action)>();
        if (_pickerWireNode != null || _pickerKind.HasValue) return extras;

        Float2 at = _pickerGraph;
        if (Wanted("Note"))
            extras.Add(("Note", "A note left on the canvas", () => EditLayout("Add Note", () => _view.AddNote(at, _scope))));
        if (Wanted("Group"))
            extras.Add(("Group", "A box around the selected nodes, or an empty one here",
                () => Edit("Add Group", () => _view.AddGroup(SelectedNodes(), at, _scope))));
        return extras;
    }

    private bool Wanted(string label) => _pickerSearch.Length == 0 || label.Contains(_pickerSearch, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a node type can take the dropped wire.</summary>
    private bool Fits(AnimationGraphNode type, NodePinKind kind)
    {
        if (!_pickerFromOutput) return type.Output == kind;

        foreach (InputPin pin in type.Inputs)
            if (pin.Kind == kind) return true;
        return false;
    }

    private List<AnimationGraphNode> Matches()
    {
        var matches = new List<AnimationGraphNode>();
        foreach (AnimationGraphNode type in AnimationNodeRegistry.All)
        {
            if (type.Hidden) continue;
            if (_pickerKind.HasValue && !Fits(type, _pickerKind.Value)) continue;
            if (_pickerSearch.Length > 0
                && !type.DisplayName.Contains(_pickerSearch, StringComparison.OrdinalIgnoreCase)
                && !type.Category.Contains(_pickerSearch, StringComparison.OrdinalIgnoreCase))
                continue;
            matches.Add(type);
        }

        return matches;
    }

    /// <summary>Creates the picked node, wired straight into whatever the wire was dragged from.</summary>
    private void Create(AnimationGraphNode type)
    {
        Float2 at = _pickerGraph;
        string? wireFrom = _pickerWireNode;
        bool fromOutput = _pickerFromOutput;
        string? port = _pickerWirePort;
        bool frame = _pickerFrame;
        ClosePicker();

        Edit($"Add {type.DisplayName}", () =>
        {
            GraphNodeRecord? record = _view.Add(type.Id, at);
            if (record == null) return;

            _controller.SelectNodes(new[] { record.Id });
            if (frame) _controller.FocusNode(record.Id);

            if (wireFrom == null || _view.RecordOf(wireFrom) is not { } end) return;

            if (!fromOutput)
            {
                int into = AnimationGraphView.PinIndex(port ?? "");
                if (into < 0) return;
                while (end.Inputs.Count <= into) end.Inputs.Add(new GraphInputRecord());
                end.Inputs[into].Node = record.Id;
                return;
            }

            // Dragged from an output: the new node takes it in its first pin of that kind.
            NodePinKind? kind = _view.OutputKind(end);
            for (int pin = 0; pin < record.Inputs.Count; pin++)
                if (type.PinAt(pin)?.Kind == kind)
                {
                    record.Inputs[pin].Node = wireFrom;
                    return;
                }
        });
    }

    /// <summary>Draws a selected piece of this graph in the inspector, editing through this window's undo.</summary>
    internal void DrawInspector(Paper paper, AnimationGraphSelection selection)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;
        var m = Origami.Current.Metrics;

        if (_graph.IsNotValid() || !StillInGraph(selection))
        {
            EditorGUI.Note(paper, "agi_gone", "This is no longer part of the graph.");
            return;
        }

        if (selection.Parameter != null) DrawParameterInspector(paper, font, m, selection.Parameter);
        else if (selection.Transition != null) DrawTransitionSection(paper, font, m, selection.State!, selection.Transition);
        else if (selection.State != null) DrawStateSection(paper, font, m, selection.State);
        else if (selection.Node != null) DrawNodeSection(paper, font, selection.Node);
    }

    private bool StillInGraph(AnimationGraphSelection selection)
    {
        if (selection.Parameter != null) return _graph!.Parameters.Contains(selection.Parameter);
        if (selection.State != null)
            return selection.Machine != null && _graph!.Nodes.Contains(selection.Machine) && selection.Machine.States.Contains(selection.State)
                && (selection.Transition == null || selection.State.Transitions.Contains(selection.Transition));
        return selection.Node != null && _graph!.Nodes.Contains(selection.Node);
    }

    private void DrawNodeSection(Paper paper, Scribe.FontFile font, GraphNodeRecord node)
    {
        AnimationGraphNode? type = AnimationNodeRegistry.Get(node.Type);
        if (type == null)
        {
            EditorGUI.Note(paper, "ag_unknown", $"'{node.Type}' is not a node type this build knows about.");
            return;
        }

        AnimationNodeEditor editor = AnimationNodeEditor.For(type);
        editor.DrawInspector(new AnimationNodeInspector(this, paper, node, editor, _view.Editing), node);
    }

    internal void DrawNodeProperty(Paper paper, GraphNodeRecord record, NodeSetting property)
        => DrawProperty(paper, EditorTheme.DefaultFont!, record, property);

    private void DrawProperty(Paper paper, Scribe.FontFile font, GraphNodeRecord record, NodeSetting property)
    {
        string id = $"ag_p_{property.Key}";

        if (record.PropertyInputs.TryGetValue(property.Key, out string? driver))
        {
            DrawDrivenProperty(paper, font, id, record, property, driver);
            return;
        }

        if (property is TextSetting { List: true } list)
        {
            NodeValue value = AnimationGraphView.ValueOf(record, list);
            NodeValue? paired = list.PairedWith is { } other ? AnimationGraphView.ValueOf(record, other) : null;

            ListEditor(paper, id, list.Label, list.Bone, Rig, value.Text,
                v => Set("Set " + list.Label, () => value.Text = v),
                paired?.Text, paired == null ? null : v => Set("Set " + list.PairedWith!.Label, () => paired.Text = v));
            return;
        }

        switch (property.Kind)
        {
            case NodeValueKind.Flag:
                NodeValue flag = AnimationGraphView.ValueOf(record, property);
                EditorGUI.SettingsToggle(paper, id, property.Label, flag.Flag,
                    v => Set("Set " + property.Label, () => flag.Flag = v, false), separator: false);
                break;

            case NodeValueKind.Clip or NodeValueKind.Mask or NodeValueKind.Avatar or NodeValueKind.Graph:
                AnimationNodeCard.AssetField(_view.Editing, paper, $"{id}_v", property.Label, record, property);
                break;

            default:
                EditorGUI.Row(paper, id, property.Label, () =>
                    AnimationNodeCard.ValueField(_view.Editing, paper, $"{id}_v", record, property, curveHeight: 28f));
                break;
        }
    }

    /// <summary>A setting exposed as a port: what drives it, and a button to bring it back.</summary>
    private void DrawDrivenProperty(Paper paper, Scribe.FontFile font, string id, GraphNodeRecord record,
        NodeSetting property, string driver)
    {
        GraphNodeRecord? source = driver.Length > 0 ? _view.RecordOf(driver) : null;
        string text = source?.Title ?? "nothing wired";

        EditorGUI.Row(paper, id, property.Label, () =>
        {
            paper.Box($"{id}_v").Width(UnitValue.Stretch()).Height(Origami.Current.Metrics.RowHeight).IsNotInteractable()
                .Text(text, font).TextColor(source != null ? EditorTheme.Ink600 : EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

            Origami.Button(paper, $"{id}_hide", "Unexpose",
                () => Edit($"Unexpose {property.Label}", () => AnimationNodeCard.Expose(record, property, false)))
                .Subtle().Width(72).Show();
        });
    }

    /// <summary>The parameter a parameter node reads. Fixed when the node is made, so it is shown, not edited.</summary>
    internal void DrawParameterPicker(Paper paper, string id, string name)
    {
        var font = EditorTheme.DefaultFont!;
        GraphParameterRecord? declared = _graph!.Parameters.Find(p => p.Name == name);
        bool isVirtual = declared == null && AnimationGraphView.VirtualParameters(_graph).Contains(name);

        string text = declared != null ? $"{name}  ({KindName(declared)})"
            : isVirtual ? $"{name}  (Virtual)"
            : $"{name} (missing)";

        EditorGUI.Row(paper, id, "Parameter", () =>
            paper.Box($"{id}_v").Width(UnitValue.Stretch()).Height(Origami.Current.Metrics.RowHeight).IsNotInteractable()
                .Text(text, font)
                .TextColor(declared == null && !isVirtual ? EditorTheme.Red400 : EditorTheme.Ink600)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft));
    }

    /// <summary>A one field edit, made undoable. Pass rebuild false when no card shows the value.</summary>
    private void Set(string description, Action change, bool rebuild = true)
    {
        EchoObject before = Snapshot();
        change();
        EditDone(description, before, coalesce: true, rebuild);
    }

    private void DrawStateSection(Paper paper, Scribe.FontFile font, OrigamiMetrics m, GraphStateRecord state)
    {
        if (state.IsAny)
        {
            EditorGUI.Note(paper, "ag_anyHow", "Transitions from here can leave any other state. Each fires as soon as the state it leads to wants in, and they are checked before a state's own.");
            return;
        }

        EditorGUI.Row(paper, "ag_stateName", "Name", () =>
            Origami.TextField(paper, "ag_stateName_v", state.Name, v => Set("Rename State", () => _stateView.Rename(state, v))).Show());

        EditorGUI.SettingsToggle(paper, "ag_stateDefault", "Default State", state.IsDefault,
            v => { if (v) Set("Set Default State", () => _stateView.SetDefault(state)); }, separator: false);

        // Switching to an asset keeps the embedded nodes for switching back.
        bool asset = state.UsesAsset || ReferenceEquals(_pickingAssetFor, state);
        EditorGUI.Row(paper, "ag_stateSource", "Graph", () =>
            Origami.ButtonGroup(paper, "ag_stateSource_v", asset ? 1 : 0, v =>
            {
                if ((v == 1) == asset) return;
                if (v == 1)
                {
                    _pickingAssetFor = state;
                    return;
                }

                _pickingAssetFor = null;
                if (state.UsesAsset) Set("Embed State Graph", () => state.Graph = default);
            })
                .Height(EditorTheme.RowHeight).FullWidth()
                .Item("Embedded", tooltip: "Its own nodes, inside this graph")
                .Item("Asset", tooltip: "A graph asset")
                .Show());

        if (asset)
            AnimationNodeCard.AssetField<AnimationGraph>(paper, "ag_stateAsset", "Asset", state.Graph, v =>
            {
                _pickingAssetFor = null;
                Set("Set State Graph", () => state.Graph = v);
            });

        using (paper.Row("ag_stateActions").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
            .Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).Gap(m.Padding).Margin(0, 0, 0, m.SpacingLarge).Enter())
        {
            Origami.Button(paper, "ag_stateOpen", "Open Graph", () => EnterState(state)).Subtle().Width(UnitValue.Stretch()).Show();
            if (!state.UsesAsset)
                Origami.Button(paper, "ag_stateExtract", "Extract To Asset",
                    () => ExtractToAsset(state.Id, state.Name, asset => state.Graph = asset)).Subtle().Width(UnitValue.Stretch()).Show();
        }

        EditorGUI.Note(paper, "ag_stateHow", "Its graph's output decides what it plays, when it wants to be entered, and when it lets go.");
    }

    // The state whose source was switched to Asset while no asset has been chosen for it yet.
    private GraphStateRecord? _pickingAssetFor;

    private void DrawTransitionSection(Paper paper, Scribe.FontFile font, OrigamiMetrics m, GraphStateRecord from, GraphTransitionRecord transition)
    {
        EditorGUI.Row(paper, "ag_trDuration", "Duration (s)", () =>
            Origami.NumericField<float>(paper, "ag_trDuration_v", transition.Duration,
                v => Set("Set Transition Duration", () => transition.Duration = MathF.Max(0f, v), false)).Min(0f).Show());

        string[] easings = Enum.GetNames<TransitionEasing>();
        EditorGUI.Row(paper, "ag_trEasing", "Easing", () =>
            Origami.Dropdown(paper, "ag_trEasing_v", (int)transition.Easing,
                v => Set("Set Transition Easing", () => transition.Easing = (TransitionEasing)v), easings).Show());

        string[] syncs = { "Start Fresh", "Match Time", "Synchronized" };
        EditorGUI.Row(paper, "ag_trSync", "Timing", () =>
            Origami.Dropdown(paper, "ag_trSync_v", (int)transition.Sync,
                v => Set("Set Transition Timing", () => transition.Sync = (TransitionSync)v), syncs).Show());

        EditorGUI.SettingsToggle(paper, "ag_trClamp", "Fit Remaining Time", transition.ClampToSource,
            v => Set("Set Transition Clamp", () => transition.ClampToSource = v, false), separator: false);
        EditorGUI.SettingsToggle(paper, "ag_trInterrupt", "Can Interrupt", transition.CanInterrupt,
            v => Set("Set Transition Interrupt", () => transition.CanInterrupt = v, false), separator: false);

        EditorGUI.Note(paper, "ag_trWhen", from.IsAny
            ? $"Fires from any other state as soon as '{transition.To}' wants in, as its graph says."
            : $"Fires when '{from.Name}' lets go and '{transition.To}' wants in, as each state's graph says.");
    }

    /// <summary>The side panel: the graph's rig and parameters.</summary>
    private void DrawPanel(Paper paper, Scribe.FontFile font, OrigamiMetrics m, float width, float height)
    {
        using (paper.Column("ag_panel").Width(width).Height(height)
            .BackgroundColor(EditorTheme.Neutral200)
            .BorderColor(EditorTheme.BorderSoft).BorderWidth(1).Enter())
        {
            using (paper.Row("ag_paramHeader").Width(UnitValue.Stretch()).Height(30)
                .Padding(m.Padding, m.Padding, 0, 0).AlignItems(LayoutAlignment.Center).Enter())
            {
                paper.Box("ag_paramTitle").Width(UnitValue.Stretch()).Height(24).IsNotInteractable()
                    .Text("Parameters", EditorTheme.FontSemiBold ?? font).TextColor(EditorTheme.Ink600)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

                paper.Box("ag_paramAdd").Width(22).Height(22).Rounded(m.SmallRounding)
                    .BackgroundColor(EditorTheme.Neutral300)
                    .Hovered.BackgroundColor(EditorTheme.Neutral400).End()
                    .Cursor(PaperCursor.Pointer).Tooltip("Add a parameter")
                    .OnClick(0, (_, e) => OpenParameterPopup(e.PointerPosition))
                    .Text(EditorIcons.Plus, font).TextColor(EditorTheme.Ink600)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
            }

            Hairline(paper, "ag_paramSep");

            List<GraphProblem> problems = GraphProblems();
            float footer = FooterHeight(problems);

            Origami.ScrollView(paper, "ag_paramScroll", width - 2f, height - 31f - footer).Body(() =>
            {
                AnimationNodeCard.AssetField(paper, "ag_rig", "Rig", _graph!.Rig, v => Set("Set Rig", () => _graph!.Rig = v));

                if (_graph!.Parameters.Count == 0)
                    EditorGUI.Note(paper, "ag_noParams", "Parameters are the values the game sets on the Animator. Add one, then drag it onto the graph to read it.");

                for (int i = 0; i < _graph.Parameters.Count; i++)
                    DrawParameterRow(paper, font, m, _graph.Parameters[i], i);

                List<string> named = AnimationGraphView.VirtualParameters(_graph);
                if (named.Count > 0)
                {
                    EditorGUI.SectionHeader(paper, "ag_virtualHeader", "Virtual", compact: true);
                    for (int i = 0; i < named.Count; i++)
                        DrawVirtualParameterRow(paper, font, m, named[i], i);
                }
            });

            DrawFooter(paper, font, m, problems, footer);
        }
    }

    private static void Hairline(Paper paper, string id)
        => paper.Box(id).Width(UnitValue.Stretch()).Height(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

    /// <summary>The problems that belong to the graph as a whole rather than to one node, like having no output.</summary>
    private List<GraphProblem> GraphProblems()
    {
        var problems = new List<GraphProblem>();
        if (_insideMachine != null) return problems;
        foreach (GraphProblem problem in _view.Problems)
            if (problem.NodeId.Length == 0) problems.Add(problem);
        return problems;
    }

    private static float FooterHeight(List<GraphProblem> problems) => 1f + 26f + Math.Max(0, problems.Count - 1) * 20f;

    /// <summary>The graph's problems that belong to no node, pinned to the bottom of the panel.</summary>
    private void DrawFooter(Paper paper, Scribe.FontFile font, OrigamiMetrics m, List<GraphProblem> problems, float height)
    {
        Hairline(paper, "ag_footSep");

        using (paper.Column("ag_foot").Width(UnitValue.Stretch()).Height(height - 1f)
            .Padding(m.Padding, m.Padding, 5, 5).Gap(2).Enter())
        {
            if (problems.Count == 0)
            {
                FooterLine(paper, font, "ag_foot_ok", "Graph is ready", EditorTheme.Green400);
                return;
            }

            for (int i = 0; i < problems.Count; i++)
                FooterLine(paper, font, $"ag_foot_{i}", problems[i].Message,
                    problems[i].Blocking ? EditorTheme.Red400 : EditorTheme.Amber400);
        }
    }

    private static void FooterLine(Paper paper, Scribe.FontFile font, string id, string text, Color colour)
    {
        using (paper.Row(id).Width(UnitValue.Stretch()).Height(16).Gap(6).AlignItems(LayoutAlignment.Center)
            .Tooltip(text).Enter())
        {
            paper.Box(id + "_dot").Width(7).Height(7).Rounded(4).BackgroundColor(colour).IsNotInteractable();
            paper.Box(id + "_text").Width(UnitValue.Stretch()).Height(16).IsNotInteractable()
                .Text(text, font).TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSizeSmall * 0.95f)
                .Alignment(TextAlignment.MiddleLeft).TextTruncate();
        }
    }

    /// <summary>One parameter row. Clicking selects it, dragging it onto the graph makes a node that reads it.</summary>
    private void DrawParameterRow(Paper paper, Scribe.FontFile font, OrigamiMetrics m, GraphParameterRecord parameter, int index)
    {
        bool selected = Selection.IsSelected(new AnimationGraphSelection(this, parameter: parameter));
        string live = _probe.ReadParameter(parameter);

        var row = ParameterRow(paper, m, "ag_param", index, parameter.Name, selected)
            .OnClick(0, (_, _) => Publish(new List<AnimationGraphSelection> { new(this, parameter: parameter) }));

        using (row.Enter())
        {
            ParameterRowName(paper, font, m, parameter.Name, AnimationGraphNode.PinKindOf(parameter.Kind), EditorTheme.Ink700);

            paper.Box("value").Width(UnitValue.Auto).Height(m.RowHeight).IsNotInteractable()
                .Text(live.Length > 0 ? live : DefaultText(parameter), font)
                .TextColor(live.Length > 0 ? EditorTheme.Green400 : EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);

            paper.Box("del").Width(20).Height(m.RowHeight).Rounded(m.SmallRounding)
                .Hovered.BackgroundColor(EditorTheme.Neutral400).End()
                .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
                .Tooltip("Delete the parameter and the nodes reading it")
                .StopEventPropagation()
                .OnClick(0, (_, _) => DeleteParameter(parameter));
        }
    }

    /// <summary>A Virtual Parameter's name, which drags onto the graph like a parameter.</summary>
    private void DrawVirtualParameterRow(Paper paper, Scribe.FontFile font, OrigamiMetrics m, string name, int index)
    {
        var row = ParameterRow(paper, m, "ag_virtual", index, name, selected: false)
            .Tooltip("Set by the Virtual Parameter node of this name. Drag it onto the graph to read it.");

        using (row.Enter())
            ParameterRowName(paper, font, m, name, NodePinKind.Number, EditorTheme.Ink600);
    }

    // A row that drags a parameter's name onto the graph, where it becomes a node reading it. Its
    // children are scoped to the row, so they use constant ids.
    private static ElementBuilder ParameterRow(Paper paper, OrigamiMetrics m, string key, int index, string name, bool selected)
        => paper.Row(key, index).Width(UnitValue.Stretch()).Height(m.RowHeight + 4)
            .Margin(m.Padding, m.Padding, 1, 1).Padding(m.Padding, 4, 0, 0).Gap(m.SpacingMedium)
            .Rounded(m.SmallRounding)
            .BackgroundColor(selected ? EditorTheme.WithAlpha(EditorTheme.Accent, 50) : Color.Transparent)
            .Hovered.BackgroundColor(selected ? EditorTheme.WithAlpha(EditorTheme.Accent, 70) : EditorTheme.Neutral300).End()
            .AlignItems(LayoutAlignment.Center)
            .Cursor(PaperCursor.Grab)
            .OnDragStart(_ => DragDrop.StartDrag(new AnimationParameterDrag(name)));

    private static void ParameterRowName(Paper paper, Scribe.FontFile font, OrigamiMetrics m, string name, NodePinKind kind, Color ink)
    {
        paper.Box("kind").Width(8).Height(8).Rounded(4).BackgroundColor(AnimationGraphView.KindColor(kind)).IsNotInteractable();
        paper.Box("name").Width(UnitValue.Stretch()).Height(m.RowHeight).IsNotInteractable()
            .Text(name, font).TextColor(ink)
            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate();
    }

    /// <summary>A parameter goes, and so does every node reading it, which would read nothing now.</summary>
    private void DeleteParameter(GraphParameterRecord parameter)
    {
        Edit("Delete Parameter", () =>
        {
            var readers = new List<GraphNode>();
            foreach (GraphNode node in _view.Nodes)
                if (_view.RecordOf(node.Id) is { Type: AnimationNodeIds.Parameter } record
                    && record.Get(ParameterNode.NameSetting) == parameter.Name)
                    readers.Add(node);

            _view.Delete(readers, Array.Empty<GraphConnection>());
            _graph!.Parameters.Remove(parameter);
            ClearSelection();
        });
    }

    private static string DefaultText(GraphParameterRecord parameter) => parameter.Kind switch
    {
        NodeValueKind.Flag when parameter.Trigger => "trigger",
        NodeValueKind.Flag => parameter.Flag ? "true" : "false",
        NodeValueKind.Integer => parameter.Integer.ToString(),
        NodeValueKind.Vector => $"{parameter.Vector.X:0.##}, {parameter.Vector.Y:0.##}, {parameter.Vector.Z:0.##}",
        NodeValueKind.Id => parameter.Text,
        NodeValueKind.Target => "",
        _ => parameter.Number.ToString("0.###"),
    };

    /// <summary>A parameter in the inspector. Its name and type are fixed once made; the default is not.</summary>
    private void DrawParameterInspector(Paper paper, Scribe.FontFile font, OrigamiMetrics m, GraphParameterRecord parameter)
    {
        EditorGUI.Note(paper, "agi_fixed", "A parameter keeps its name and type. To change either, delete it and add it again.");

        string live = _probe.ReadParameter(parameter);
        if (live.Length > 0)
            EditorGUI.Row(paper, "agi_live", "Live", () =>
                paper.Box("agi_live_v").Width(UnitValue.Stretch()).Height(m.RowHeight).IsNotInteractable()
                    .Text(live, font).TextColor(EditorTheme.Green400)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft));

        DrawParameterDefault(paper, "agi_param", parameter);
        EditorGUI.PillButton(paper, "agi_delete", "Delete Parameter", false, () => DeleteParameter(parameter)).Margin(0, 0, m.Spacing, m.Spacing);
    }

    private void OpenParameterPopup(Float2 screen)
    {
        _parameterPopup = true;
        _parameterPopupAt = screen;
        _newParameterName = "";
        _newParameterKind = 0;
    }

    /// <summary>The popup that asks for a new parameter's name and type.</summary>
    private void DrawParameterPopup(Paper paper, Scribe.FontFile font, OrigamiMetrics m)
    {
        const float w = 240f;
        float x = Math.Clamp(_parameterPopupAt.X - _rootX - w, 4f, MathF.Max(4f, _rootW - w - 4f));
        float y = Math.Clamp(_parameterPopupAt.Y - _rootY + 14f, 4f, MathF.Max(4f, _rootH - 160f));

        if (paper.IsKeyPressed(PaperKey.Escape)) { _parameterPopup = false; return; }

        paper.Box("ag_ppBackdrop").PositionType(PositionType.SelfDirected).Left(0).Top(0)
            .Width(UnitValue.Percentage(100)).Height(UnitValue.Percentage(100)).Layer(Layer.Overlay)
            .OnClick(0, (_, _) => _parameterPopup = false);

        string name = _newParameterName.Trim();
        bool taken = _graph!.Parameters.Exists(p => p.Name == name);
        bool valid = name.Length > 0 && !taken;

        using (paper.Column("ag_pp").PositionType(PositionType.SelfDirected).Left(x).Top(y)
            .Width(w).Height(UnitValue.Auto).Layer(Layer.Overlay + 10)
            .BackgroundColor(EditorTheme.Neutral200).Rounded(m.ContainerRounding)
            .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .Padding(m.PaddingLarge, m.PaddingLarge, m.PaddingLarge, m.PaddingLarge).Gap(m.Spacing)
            .BoxShadow(0, 8, 24, 0, System.Drawing.Color.FromArgb(120, 0, 0, 0)).Enter())
        {
            paper.Box("ag_ppTitle").Width(UnitValue.Stretch()).Height(20).IsNotInteractable()
                .Text("New Parameter", EditorTheme.FontSemiBold ?? font).TextColor(EditorTheme.Ink600)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

            Origami.TextField(paper, "ag_ppName", _newParameterName, v => _newParameterName = v)
                .Placeholder("Name").Width(UnitValue.Stretch()).Show();

            Origami.Dropdown(paper, "ag_ppKind", _newParameterKind, v => _newParameterKind = v, ParameterKindNames)
                .Width(UnitValue.Stretch()).Show();

            if (taken)
                paper.Box("ag_ppTaken").Width(UnitValue.Stretch()).Height(16).IsNotInteractable()
                    .Text("A parameter already has that name.", font).TextColor(EditorTheme.Red400)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

            Origami.Button(paper, "ag_ppCreate", "Create", () => { if (valid) CreateParameter(name); })
                .Width(UnitValue.Stretch()).Show();
        }

        if (valid && paper.IsKeyPressed(PaperKey.Enter)) CreateParameter(name);
    }

    private void CreateParameter(string name)
    {
        int choice = Math.Clamp(_newParameterKind, 0, ParameterKinds.Length - 1);
        var parameter = new GraphParameterRecord { Name = name, Kind = ParameterKinds[choice], Trigger = choice == TriggerChoice };

        Edit("Add Parameter", () => _graph!.Parameters.Add(parameter));
        _parameterPopup = false;
        Publish(new List<AnimationGraphSelection> { new(this, parameter: parameter) });
    }

    private void DrawParameterDefault(Paper paper, string id, GraphParameterRecord parameter)
    {
        switch (parameter.Kind)
        {
            case NodeValueKind.Flag when parameter.Trigger:
                break; // A trigger always starts off, and turns off again once used.
            case NodeValueKind.Flag:
                EditorGUI.SettingsToggle(paper, $"{id}_def", "Default", parameter.Flag,
                    v => Set("Set Parameter Default", () => parameter.Flag = v, false), separator: false);
                break;
            case NodeValueKind.Integer:
                EditorGUI.Row(paper, $"{id}_def", "Default", () =>
                    Origami.NumericField<int>(paper, $"{id}_def_v", parameter.Integer,
                        v => Set("Set Parameter Default", () => parameter.Integer = v, false)).Show());
                break;
            case NodeValueKind.Vector:
                EditorGUI.Row(paper, $"{id}_def", "Default", () =>
                    Origami.Float3Field(paper, $"{id}_def_v", parameter.Vector,
                        v => Set("Set Parameter Default", () => parameter.Vector = v, false)).Show());
                break;
            case NodeValueKind.Id:
                EditorGUI.Row(paper, $"{id}_def", "Default", () =>
                    Origami.TextField(paper, $"{id}_def_v", parameter.Text,
                        v => Set("Set Parameter Default", () => parameter.Text = v, false)).Show());
                break;
            case NodeValueKind.Target:
                break; // A target starts unset, and there is nothing to type for that.
            default:
                EditorGUI.Row(paper, $"{id}_def", "Default", () =>
                    Origami.NumericField<float>(paper, $"{id}_def_v", parameter.Number,
                        v => Set("Set Parameter Default", () => parameter.Number = v, false)).Show());
                break;
        }
    }

    private static readonly NodeValueKind[] ParameterKinds =
    {
        NodeValueKind.Number, NodeValueKind.Flag, NodeValueKind.Integer,
        NodeValueKind.Vector, NodeValueKind.Id, NodeValueKind.Target, NodeValueKind.Flag,
    };

    private static readonly string[] ParameterKindNames = { "Number", "Flag", "Int", "Vector", "Name", "Target", "Trigger" };

    /// <summary>The popup entry that makes a trigger, a flag that turns itself off once used.</summary>
    private const int TriggerChoice = 6;

    /// <summary>What a parameter type is called, in the add popup and the inspector.</summary>
    private static string KindName(NodeValueKind kind)
    {
        int index = Array.IndexOf(ParameterKinds, kind);
        return index < 0 ? kind.ToString() : ParameterKindNames[index];
    }

    private static string KindName(GraphParameterRecord parameter)
        => parameter.Kind == NodeValueKind.Flag && parameter.Trigger ? ParameterKindNames[TriggerChoice] : KindName(parameter.Kind);

    // Group and note handling, the same for the node graph and a state machine's graph.
    private NodeGraphBuilder Organise(NodeGraphBuilder graph, Paper paper, Action<IReadOnlyList<GraphNode>, Float2> moveMembers)
        => graph
            .OnGroupMoved((group, members, delta) => MoveGroup(group, members, delta, moveMembers))
            .OnGroupResized(ResizeGroup)
            .OnGroupRenamed(RenameGroup)
            .OnGroupContext((group, _) => OpenGroupMenu(paper, group))
            .OnStickyMoved(MoveNote)
            .OnStickyResized(ResizeNote)
            .OnStickyEdited(EditNote)
            .OnStickyContext((note, _) => OpenNoteMenu(paper, note));

    private static string Join(IReadOnlyList<string> entries) => string.Join(", ", entries);

    private static string JoinWeight(string bone, float weight)
        => Math.Abs(weight - 1f) < 0.0001f ? bone : $"{bone}:{weight.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>A list of names, a row each, with a paired list's number beside each name.</summary>
    private static void ListEditor(Paper paper, string id, string label, bool bones, Avatar? rig,
        string text, Action<string> setter, string? pairedText = null, Action<string>? pairedSetter = null)
    {
        var entries = new List<string>(GraphCompileContext.Split(text));
        var paired = new List<string>(pairedText != null ? GraphCompileContext.Split(pairedText) : Array.Empty<string>());

        void Write()
        {
            setter(Join(entries));
            if (pairedSetter != null)
            {
                while (paired.Count > entries.Count) paired.RemoveAt(paired.Count - 1);
                while (paired.Count < entries.Count) paired.Add("0");
                pairedSetter(Join(paired));
            }
        }

        EditorGUI.SectionHeader(paper, $"{id}_h", label);

        for (int i = 0; i < entries.Count; i++)
        {
            int index = i;
            using (paper.Row($"{id}_r{index}").Width(UnitValue.Stretch()).Height(Origami.Current.Metrics.RowHeight)
                .Margin(0, 0, 0, 4).AlignItems(LayoutAlignment.Center).Enter())
            {
                DrawEntry(paper, $"{id}_e{index}", bones, rig, entries, index, Write);

                if (pairedSetter != null)
                {
                    while (paired.Count <= index) paired.Add("0");
                    float.TryParse(paired[index], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float number);

                    Origami.NumericField<float>(paper, $"{id}_n{index}", number, v =>
                    {
                        paired[index] = v.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        Write();
                    }).Width(72).Show();
                }

                Origami.Button(paper, $"{id}_x{index}", "X", () =>
                {
                    entries.RemoveAt(index);
                    if (index < paired.Count) paired.RemoveAt(index);
                    Write();
                }).Subtle().Width(24).Show();
            }
        }

        Origami.Button(paper, $"{id}_add", "Add", () =>
        {
            entries.Add(bones ? FirstBone(rig) : "Name");
            Write();
        }).Subtle().Width(UnitValue.Stretch()).Show();
    }

    private static void DrawEntry(Paper paper, string id, bool bones, Avatar? rig,
        List<string> entries, int index, Action write)
    {
        if (!bones)
        {
            Origami.TextField(paper, id, entries[index], v => { entries[index] = v; write(); })
                .Width(UnitValue.Stretch()).Show();
            return;
        }

        (string bone, float weight) = GraphCompileContext.SplitWeight(entries[index]);
        AnimationNodeCard.BoneField(paper, id, rig, bone, v => { entries[index] = JoinWeight(v, weight); write(); });

        Origami.NumericField<float>(paper, $"{id}_w", weight, v =>
        {
            entries[index] = JoinWeight(bone, v);
            write();
        }).Width(60).Show();
    }

    private static string FirstBone(Avatar? rig)
    {
        List<string> options = AnimationNodeCard.BoneOptions(rig);
        return options.Count > 0 ? options[0] : "bone";
    }
}

/// <summary>
/// One piece of a graph in the editor's selection: a node, a state, a transition, or a parameter. The
/// inspector edits it through the window that selected it.
/// </summary>
public sealed class AnimationGraphSelection : IEquatable<AnimationGraphSelection>
{
    internal AnimationGraphSelection(AnimationGraphWindow window, GraphNodeRecord? node = null,
        GraphNodeRecord? machine = null, GraphStateRecord? state = null, GraphTransitionRecord? transition = null,
        GraphParameterRecord? parameter = null)
    {
        Window = window;
        Node = node;
        Machine = machine;
        State = state;
        Transition = transition;
        Parameter = parameter;
    }

    internal AnimationGraphWindow Window { get; }

    /// <summary>A node of the graph itself.</summary>
    internal GraphNodeRecord? Node { get; }

    /// <summary>The state machine a state or a transition belongs to.</summary>
    internal GraphNodeRecord? Machine { get; }

    /// <summary>A state, or the state a transition leaves.</summary>
    internal GraphStateRecord? State { get; }

    internal GraphTransitionRecord? Transition { get; }

    internal GraphParameterRecord? Parameter { get; }

    public bool Equals(AnimationGraphSelection? other)
        => other != null && ReferenceEquals(Window, other.Window) && ReferenceEquals(Node, other.Node)
            && ReferenceEquals(State, other.State) && ReferenceEquals(Transition, other.Transition)
            && ReferenceEquals(Parameter, other.Parameter);

    public override bool Equals(object? obj) => obj is AnimationGraphSelection other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Window, Node, State, Transition, Parameter);

    public override string ToString()
    {
        if (Parameter != null) return Parameter.Name;
        if (Transition != null) return $"{State!.Name} to {Transition.To}";
        if (State != null) return State.Name;
        return Node?.Title ?? "Graph";
    }
}

/// <summary>Shows a selected piece of a graph in the inspector, drawn by the window it came from.</summary>
[CustomEditor(typeof(AnimationGraphSelection))]
internal sealed class AnimationGraphSelectionEditor : CustomEditor
{
    public override void OnGUI(Paper paper, string id, object target)
    {
        if (target is AnimationGraphSelection selection) selection.Window.DrawInspector(paper, selection);
    }
}

/// <summary>A parameter being dragged out of the parameters list, to be dropped onto the graph as a node reading it.</summary>
internal sealed class AnimationParameterDrag : DragPayload
{
    public AnimationParameterDrag(string name) => Name = name;

    public string Name { get; }

    public override string DisplayName => Name;

    public override string Icon => EditorIcons.Sliders;
}

/// <summary>The inspector for a graph asset: a summary and a button to open the graph window.</summary>
[CustomAssetEditor(typeof(AnimationGraph))]
public class AnimationGraphAssetEditor : AssetImporterEditor
{
    [AssetDoubleClickHandler(".animgraph")]
    private static bool OnDoubleClick(string relativePath, Guid guid)
    {
        AnimationGraphWindow.OpenFor(guid);
        return true;
    }

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        var graph = asset as AnimationGraph;
        var m = Origami.Current.Metrics;

        using (paper.Row($"{id}_open").Width(UnitValue.Stretch()).Height(34)
            .Margin(m.PaddingLarge, m.PaddingLarge, m.Spacing, m.SpacingMedium).Enter())
        {
            paper.Box($"{id}_openBtn").Width(UnitValue.Stretch()).Height(30)
                .Rounded(m.SmallRounding).BackgroundColor(EditorTheme.Accent)
                .Hovered.BackgroundColor(EditorTheme.AccentBright).End()
                .Text("Open Graph", font).TextColor(System.Drawing.Color.White)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
                .OnClick(0, (_, _) => AnimationGraphWindow.OpenFor(entry.Guid));
        }

        if (graph.IsNotValid()) return;

        EditorGUI.SectionHeader(paper, $"{id}_h", "Summary", first: true);
        Line(paper, font, m, $"{id}_nodes", "Nodes", graph!.Nodes.Count.ToString());
        Line(paper, font, m, $"{id}_params", "Parameters", graph.Parameters.Count.ToString());
        Line(paper, font, m, $"{id}_root", "Output", RootName(graph));

        if (graph.Parameters.Count == 0) return;

        EditorGUI.SectionHeader(paper, $"{id}_hp", "Parameters");
        for (int i = 0; i < graph.Parameters.Count; i++)
        {
            GraphParameterRecord parameter = graph.Parameters[i];
            Line(paper, font, m, $"{id}_p{i}", parameter.Name, parameter.Kind.ToString());
        }
    }

    private static string RootName(AnimationGraph graph)
    {
        if (graph.RootNode.Length == 0) return "not set";

        return graph.Find(graph.RootNode)?.Title ?? "missing";
    }

    private static void Line(Paper paper, Scribe.FontFile font, OrigamiMetrics m, string id, string label, string value)
    {
        EditorGUI.Row(paper, id, label, () =>
            paper.Box($"{id}_v").Width(UnitValue.Stretch()).Height(m.RowHeight).IsNotInteractable()
                .Text(value, font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft));
    }
}
