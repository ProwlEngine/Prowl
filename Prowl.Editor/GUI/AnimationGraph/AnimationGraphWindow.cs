// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Editor.Core;
using Prowl.Editor.GraphEditor;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.Widgets;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.AnimationNodes;
using Prowl.Vector;

using Color = System.Drawing.Color;
using TransitionEasing = Prowl.Motion.TransitionEasing;
using TransitionSync = Prowl.Motion.TransitionSync;

namespace Prowl.Editor.Inspector;

/// <summary>The editor for an <see cref="AnimationGraph"/> asset, its state machines and its parameters.</summary>
public class AnimationGraphWindow : GraphWindow<AnimationGraphWindow, GraphNodeRecord>
{
    private readonly AnimationGraphView _view = new();
    private readonly AnimationGraphStateView _stateView = new();
    private readonly AnimationGraphProbe _probe = new();

    // The add parameter popup.
    private bool _parameterPopup;
    private Float2 _parameterPopupAt;
    private string _newParameterName = "";
    private int _newParameterKind;

    /// <summary>The state machine being looked inside, or null in the graph itself.</summary>
    private GraphNodeRecord? _insideMachine;

    // The state whose source was switched to Asset while no asset has been chosen for it yet.
    private GraphStateRecord? _pickingAssetFor;

    /// <summary>A place on the breadcrumb: a graph inside the asset, and the state machine open in it, if any.</summary>
    private sealed record Place(string Scope, GraphNodeRecord? Machine);

    /// <summary>A state of a state machine, in the editor selection.</summary>
    private sealed record StateItem(GraphNodeRecord Machine, GraphStateRecord State);

    /// <summary>A transition, and the state it leaves, in the editor selection.</summary>
    private sealed record TransitionItem(GraphNodeRecord Machine, GraphStateRecord State, GraphTransitionRecord Transition);

    public AnimationGraphWindow()
    {
        _view.ShowsCards = true;
        _view.Editing.EditHandler = (description, change, rebuild) => Set(description, change, rebuild);
        _view.Editing.EnterStatesHandler = EnterMachine;
        _view.Editing.OpenSubGraphHandler = OpenSubGraph;
        _view.Editing.ExtractHandler = ExtractToAsset;
        _view.Editing.PropertyHandler = (paper, record, setting) => DrawProperty(paper, EditorTheme.DefaultFont!, record, setting);
    }

    public override string Title => Asset.IsValid() ? base.Title : "Animation Graph";

    public override string Icon => EditorIcons.DiagramProject;

    protected override string ToolName => "Graph";

    protected override string EmptyText => "No animation graph is open.";

    protected override GraphView<GraphNodeRecord> View => _view;

    protected override GraphCanvas Canvas => _insideMachine != null ? _stateView : _view;

    protected override IEnumerable<GraphCanvas> Canvases => [_view, _stateView];

    protected override IGraphProbe? Probe => _probe;

    protected override bool PopupOpen => _parameterPopup;

    private AnimationGraph? Graph => Asset as AnimationGraph;

    private AnimationGraphDomain? Animation => Domain as AnimationGraphDomain;

    protected override Asset? LoadAsset(Guid guid) => AssetDatabase.Load<AnimationGraph>(guid);

    protected override Asset? LoadedAsset(Guid guid) => AssetDatabase.Get<AnimationGraph>(guid);

    protected override GraphDomain<GraphNodeRecord> CreateDomain(Asset asset) => new AnimationGraphDomain((AnimationGraph)asset);

    /// <summary>Finds the open state machine again by id after its records were replaced, or steps out.</summary>
    protected override void Reattach(bool keepPlace)
    {
        string? machine = keepPlace ? _insideMachine?.Id : null;
        base.Reattach(keepPlace);

        _insideMachine = machine == null || Graph.IsNotValid() ? null
            : Graph!.Nodes.Find(node => node.Id == machine && node.Type == AnimationNodeIds.StateMachine && node.Owner == Scope);
        _stateView.Animation = Animation;
        _stateView.Machine = _insideMachine;
        _stateView.Invalidate();
    }

    protected override bool ScopeExists(string scope) => base.ScopeExists(scope) || StateOwning(scope, out _) != null;

    protected override void BeforeDraw()
    {
        // A state machine deleted while it was open leaves nowhere to be.
        if (_insideMachine != null && !Graph!.Nodes.Contains(_insideMachine)) LeaveMachine();

        _stateView.Probe = _probe;
        _stateView.Animation = Animation;
        _stateView.Machine = _insideMachine;

        // A state's card names the asset it plays, which can change or go missing without an edit here.
        _stateView.Invalidate();
    }

    // The breadcrumb

    protected override void AddPlaces(List<BreadcrumbItem> path)
    {
        // Walked from where the window is back up to the asset's own graph, then turned round.
        var inside = new List<BreadcrumbItem>();
        if (_insideMachine != null)
            inside.Add(new BreadcrumbItem(_insideMachine.Title, EditorIcons.CircleNodes_I, new Place(_insideMachine.Owner, _insideMachine)));

        string scope = Scope;
        while (scope.Length > 0)
        {
            if (StateOwning(scope, out GraphNodeRecord? machine) is { } state)
            {
                inside.Add(new BreadcrumbItem(state.Name, EditorIcons.CirclePlay_I, new Place(scope, null)));
                inside.Add(new BreadcrumbItem(machine!.Title, EditorIcons.CircleNodes_I, new Place(machine!.Owner, machine)));
                scope = machine.Owner;
            }
            else if (Graph!.Find(scope) is { } sub)
            {
                inside.Add(new BreadcrumbItem(sub.Title, EditorIcons.DiagramProject_I, new Place(scope, null)));
                scope = sub.Owner;
            }
            else break;
        }

        path.Add(new BreadcrumbItem(Graph!.Name, EditorIcons.DiagramProject_I, new Place(string.Empty, null)));
        inside.Reverse();
        path.AddRange(inside);
    }

    /// <summary>The state a scope id names, and the machine it belongs to.</summary>
    private GraphStateRecord? StateOwning(string scope, out GraphNodeRecord? machine)
    {
        machine = null;
        if (Graph.IsNotValid() || scope.Length == 0) return null;

        foreach (GraphNodeRecord record in Graph!.Nodes)
            foreach (GraphStateRecord state in record.States)
                if (state.Id == scope)
                {
                    machine = record;
                    return state;
                }
        return null;
    }

    /// <summary>Shows one of the graphs inside the asset, with a state machine open in it or not.</summary>
    protected override void GoTo(object place)
    {
        if (place is not Place target)
        {
            base.GoTo(place);
            LeaveMachine();
            return;
        }

        Scope = ScopeExists(target.Scope) ? target.Scope : string.Empty;
        if (target.Machine != null && target.Machine.Owner == Scope) EnterMachine(target.Machine);
        else LeaveMachine();
        Controller.FrameAll();
    }

    /// <summary>Opens a state's graph: its own nodes, or the graph asset it plays.</summary>
    private void EnterState(GraphStateRecord state)
    {
        if (state.UsesAsset) OpenAsset(state.Graph!.AssetID);
        else GoTo(new Place(state.Id, null));
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
        Controller.FrameAll();
    }

    /// <summary>Opens the graph a sub graph node runs, with the way back on the breadcrumb.</summary>
    private void OpenSubGraph(GraphNodeRecord record)
    {
        if (record.Get(SubGraphNode.Embedded))
        {
            GoTo(new Place(record.Id, null));
            return;
        }

        if (record.Get(SubGraphNode.GraphAssetSetting) is { } value) OpenAsset(value.Graph.AssetID);
    }

    // Selection

    protected override void PublishSelection(GraphCanvas canvas, GraphSelection selection)
    {
        if (!ReferenceEquals(canvas, _stateView))
        {
            base.PublishSelection(canvas, selection);
            return;
        }

        var picked = new List<object>();
        if (_insideMachine is not { } machine)
        {
            Publish(picked);
            return;
        }

        foreach (GraphNode node in selection.Nodes)
            if (_stateView.StateOf(node.Id) is { } state)
                picked.Add(new StateItem(machine, state));

        foreach (GraphConnection wire in selection.Edges)
            if (_stateView.TransitionOf(wire) is { } transition && _stateView.StateOf(wire.FromNode) is { } from)
                picked.Add(new TransitionItem(machine, from, transition));

        Publish(picked);
    }

    protected override string Describe(object item) => item switch
    {
        GraphParameterRecord parameter => parameter.Name,
        TransitionItem transition => $"{transition.State.Name} to {transition.Transition.To}",
        StateItem state => state.State.Name,
        GraphNodeRecord node => node.Title,
        _ => "Graph",
    };

    protected override bool StillInGraph(object item) => item switch
    {
        GraphParameterRecord parameter => Graph!.Parameters.Contains(parameter),
        TransitionItem transition => Graph!.Nodes.Contains(transition.Machine) && transition.Machine.States.Contains(transition.State)
            && transition.State.Transitions.Contains(transition.Transition),
        StateItem state => Graph!.Nodes.Contains(state.Machine) && state.Machine.States.Contains(state.State),
        _ => base.StillInGraph(item),
    };

    protected override void DrawInspector(Paper paper, object item)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;
        OrigamiMetrics m = Origami.Current.Metrics;

        switch (item)
        {
            case GraphParameterRecord parameter: DrawParameterInspector(paper, font, m, parameter); break;
            case TransitionItem transition: DrawTransitionSection(paper, font, m, transition.State, transition.Transition); break;
            case StateItem state: DrawStateSection(paper, font, m, state.State); break;
            case GraphNodeRecord node: DrawNodeSection(paper, node); break;
        }
    }

    // Canvas events

    protected override void NodeDoubleClick(GraphCanvas canvas, GraphNode node)
    {
        if (ReferenceEquals(canvas, _stateView))
        {
            if (_stateView.StateOf(node.Id) is { IsAny: false } state) EnterState(state);
            return;
        }

        if (_view.RecordOf(node.Id) is { } record && AnimationNodeRegistry.Get(record.Type) is { } type)
            AnimationNodeEditor.For(type).Open(_view.Editing, record);
    }

    protected override void BackgroundMenu(Paper paper, GraphCanvas canvas, Float2 at)
    {
        if (ReferenceEquals(canvas, _stateView)) OpenStateBackgroundMenu(paper, at);
        else base.BackgroundMenu(paper, canvas, at);
    }

    protected override void NodeMenu(Paper paper, GraphCanvas canvas, GraphNode node)
    {
        if (ReferenceEquals(canvas, _stateView)) OpenStateMenu(paper, node);
        else base.NodeMenu(paper, canvas, node);
    }

    protected override void AddNodeMenuItems(ContextBuilder menu, GraphNodeRecord record)
    {
        AnimationGraphNode? type = AnimationNodeRegistry.Get(record.Type);
        if (type != null) AnimationNodeEditor.For(type).AddMenuItems(_view.Editing, record, menu);
        if (type != null && HasDrivable(type))
            menu.Submenu("Expose As Input", sub =>
            {
                foreach (NodeSetting property in type.Properties)
                {
                    if (!property.Drivable || !type.ShowsProperty(record, property.Key)) continue;
                    NodeSetting setting = property;
                    sub.Toggle(setting.Label,
                        () => Edit($"Expose {setting.Label}", () => AnimationNodeFields.Expose(record, setting,
                            !record.PropertyInputs.ContainsKey(setting.Key))),
                        () => record.PropertyInputs.ContainsKey(setting.Key));
                }
            });

        if (Scope.Length == 0)
            menu.Item("Set As Output", () => Edit("Set Output Node", () => Graph!.RootNode = record.Id), enabled: type is { IsPose: true });
    }

    private static bool HasDrivable(AnimationGraphNode type)
    {
        foreach (NodeSetting property in type.Properties)
            if (property.Drivable) return true;
        return false;
    }

    /// <summary>A parameter dragged in from the list becomes a node reading it, where it was let go.</summary>
    protected override void Drop(DragPayload payload, Float2 at)
    {
        if (payload is not AnimationParameterDrag || ReferenceEquals(Canvas, _stateView)) return;

        AnimationParameterDrag? drag = DragDrop.AcceptDrop<AnimationParameterDrag>(true);
        if (drag == null) return;

        GraphNodeRecord? added = null;
        Edit($"Add {drag.Name}", () =>
        {
            added = _view.Add(AnimationNodeIds.Parameter, at);
            if (added != null) added.Properties[ParameterNode.NameSetting.Key] = NodeValue.FromText(drag.Name);
        });
        if (added != null) Controller.SelectNodes([added.Id]);
    }

    // State machines

    /// <summary>What the canvas right click offers inside a state machine: a state, a note or a group.</summary>
    private void OpenStateBackgroundMenu(Paper paper, Float2 at)
    {
        Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b => b
            .Item("Add State", () =>
            {
                GraphStateRecord? added = null;
                Edit("Add State", () => added = _stateView.Add(at));
                if (added != null) Controller.SelectNodes([AnimationGraphStateView.StateId(added.Name)]);
            })
            .Item("Add Any State", () =>
            {
                GraphStateRecord? any = null;
                Edit("Add Any State", () => any = _stateView.AddAny(at));
                if (any != null) Controller.SelectNodes([AnimationGraphStateView.StateId(any.Name)]);
            }, enabled: !_stateView.HasAny)
            .Separator()
            .Item("Add Note", () => EditLayout("Add Note", () => _stateView.AddNote(at)))
            .Item("Add Group", () => Edit("Add Group", () => _stateView.AddGroup(SelectedNodes(), at)),
                shortcut: Keys("GraphEditor/GroupSelection")));
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
                _stateView.Delete([node], []);
                ClearSelection();
            }), danger: true);
        });
    }

    /// <summary>Saves an embedded graph as its own asset, with the parameters, and points the state or node at it.</summary>
    private void ExtractToAsset(string owner, string name, Action<AnimationGraph> use)
    {
        if (Graph.IsNotValid() || Project.Current == null || EditorAssetBackend.Instance == null) return;

        EditorApplication.OpenFileDialog(FileDialogMode.Save, path =>
        {
            if (path == null || Project.Current == null || Graph.IsNotValid() || Animation is not { } domain) return;

            string relative = EditorAssetBackend.NormalizePath(Path.GetRelativePath(Project.Current.AssetsPath, path));
            if (!relative.EndsWith(".animgraph", StringComparison.OrdinalIgnoreCase)) relative += ".animgraph";

            Edit("Extract To Asset", () =>
            {
                AnimationGraph extracted = Extract(domain, owner, name);
                EditorAssetBackend.Instance!.CreateAsset(extracted, relative);
                use(extracted);
                domain.RemoveInside([owner]);
            });
        }, Project.Current.AssetsPath, ["*.animgraph"], ["Animation Graph"]);
    }

    /// <summary>A new graph asset holding what is inside one owner, lifted to be that asset's own graph.</summary>
    private static AnimationGraph Extract(AnimationGraphDomain domain, string owner, string name)
    {
        AnimationGraph graph = domain.Graph;
        var extracted = new AnimationGraph { Name = name };
        HashSet<string> scopes = domain.ScopesInside([owner]);

        foreach (GraphNodeRecord record in graph.Nodes)
        {
            if (!scopes.Contains(record.Owner)) continue;
            GraphNodeRecord copy = record.Clone();
            if (copy.Owner == owner) copy.Owner = string.Empty;
            extracted.Nodes.Add(copy);
        }
        foreach (GraphGroupRecord group in graph.Groups)
            if (scopes.Contains(group.Owner))
            {
                GraphGroupRecord copy = group.Clone();
                if (copy.Owner == owner) copy.Owner = string.Empty;
                extracted.Groups.Add(copy);
            }
        foreach (GraphNoteRecord note in graph.Notes)
            if (scopes.Contains(note.Owner))
            {
                GraphNoteRecord copy = note.Clone();
                if (copy.Owner == owner) copy.Owner = string.Empty;
                extracted.Notes.Add(copy);
            }

        extracted.Parameters.AddRange(graph.Parameters.ConvertAll(parameter => parameter.Clone()));

        // A sub graph's output becomes the root. A state's output stays, since the state still reads its Enter and Exit.
        GraphNodeRecord? output = extracted.OwnedNode(string.Empty, AnimationNodeIds.GraphOutput);
        if (output != null)
        {
            extracted.RootNode = output.Inputs.Count > 0 ? output.Inputs[0].Node : string.Empty;
            extracted.Nodes.Remove(output);
        }
        return extracted;
    }

    // The inspector

    private void DrawNodeSection(Paper paper, GraphNodeRecord node)
    {
        AnimationGraphNode? type = AnimationNodeRegistry.Get(node.Type);
        if (type == null)
        {
            EditorGUI.Note(paper, "ag_unknown", $"'{node.Type}' is not a node type this build knows about.");
            return;
        }

        AnimationNodeEditor editor = AnimationNodeEditor.For(type);
        editor.DrawInspector(new NodeInspector<GraphNodeRecord, AnimationGraphEditing>(paper, node, editor, _view.Editing), node);
    }

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
            NodeValue value = AnimationGraphDomain.ValueOf(record, list);
            NodeValue? paired = list.PairedWith is { } other ? AnimationGraphDomain.ValueOf(record, other) : null;

            ListEditor(paper, id, list.Label, list.Bone, value.Text,
                v => Set("Set " + list.Label, () => value.Text = v),
                paired?.Text, paired == null ? null : v => Set("Set " + list.PairedWith!.Label, () => paired.Text = v));
            return;
        }

        switch (property.Kind)
        {
            case NodeValueKind.Flag:
                NodeValue flag = AnimationGraphDomain.ValueOf(record, property);
                EditorGUI.SettingsToggle(paper, id, property.Label, flag.Flag,
                    v => Set("Set " + property.Label, () => flag.Flag = v, false), separator: false);
                break;

            case NodeValueKind.Clip or NodeValueKind.Mask or NodeValueKind.Avatar or NodeValueKind.Graph:
                AnimationNodeFields.AssetField(_view.Editing, paper, $"{id}_v", property.Label, record, property);
                break;

            default:
                EditorGUI.Row(paper, id, property.Label, () =>
                    AnimationNodeFields.ValueField(_view.Editing, paper, $"{id}_v", record, property, curveHeight: 28f));
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
                () => Edit($"Unexpose {property.Label}", () => AnimationNodeFields.Expose(record, property, false)))
                .Subtle().Width(72).Show();
        });
    }

    private void DrawStateSection(Paper paper, Scribe.FontFile font, OrigamiMetrics m, GraphStateRecord state)
    {
        if (state.IsAny)
        {
            EditorGUI.Note(paper, "ag_anyHow", "Transitions from here can leave any other state. Each fires as soon as the state it leads to wants in, and they are checked before a state's own.");
            return;
        }

        EditorGUI.Row(paper, "ag_stateName", "Name", () =>
            Origami.TextField(paper, "ag_stateName_v", state.Name, v =>
            {
                // A state's node is named by the state, so the renamed node is selected again.
                Set("Rename State", () => _stateView.Rename(state, v.Trim()));
                Controller.SelectNodes([AnimationGraphStateView.StateId(state.Name)]);
            }).CommitOnEnter().Show());

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
            AnimationNodeFields.AssetField<AnimationGraph>(paper, "ag_stateAsset", "Asset", state.Graph, v =>
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

    private void DrawTransitionSection(Paper paper, Scribe.FontFile font, OrigamiMetrics m, GraphStateRecord from, GraphTransitionRecord transition)
    {
        EditorGUI.Row(paper, "ag_trDuration", "Duration (s)", () =>
            Origami.NumericField<float>(paper, "ag_trDuration_v", transition.Duration,
                v => Set("Set Transition Duration", () => transition.Duration = MathF.Max(0f, v), false)).Min(0f).Show());

        string[] easings = Enum.GetNames<TransitionEasing>();
        EditorGUI.Row(paper, "ag_trEasing", "Easing", () =>
            Origami.Dropdown(paper, "ag_trEasing_v", (int)transition.Easing,
                v => Set("Set Transition Easing", () => transition.Easing = (TransitionEasing)v), easings).Show());

        string[] syncs = ["Start Fresh", "Match Time", "Synchronized"];
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

    // The side panel: the graph's parameters

    protected override void DrawPanelSection(Paper paper, Scribe.FontFile font, OrigamiMetrics m, float width, float height)
    {
        using (paper.Row("ag_paramHeader").Width(UnitValue.Stretch()).Height(30)
            .Padding(m.Padding, m.Padding, 0, 0).AlignItems(LayoutAlignment.Center).Enter())
        {
            paper.Box("ag_paramTitle").Width(UnitValue.Stretch()).Height(24).IsNotInteractable()
                .Text("Parameters", EditorTheme.FontSemiBold ?? font).TextColor(EditorTheme.Ink600)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

            Origami.IconButton(paper, "ag_paramAdd", EditorIcons.Plus_I, () => OpenParameterPopup(paper.PointerPos))
                .Small().Width(22).Height(22).Tooltip("Add a parameter").Show();
        }

        EditorGUI.Divider(paper, "ag_paramSep");

        Origami.ScrollView(paper, "ag_paramScroll", width - 2f, height - 31f).Body(() =>
        {
            if (Graph!.Parameters.Count == 0)
                EditorGUI.Note(paper, "ag_noParams", "Parameters are the values the game sets on the Animator. Add one, then drag it onto the graph to read it.");

            for (int i = 0; i < Graph.Parameters.Count; i++)
                DrawParameterRow(paper, font, m, Graph.Parameters[i], i);

            List<string> named = AnimationGraphDomain.VirtualParameters(Graph);
            if (named.Count > 0)
            {
                EditorGUI.SectionHeader(paper, "ag_virtualHeader", "Virtual", compact: true);
                for (int i = 0; i < named.Count; i++)
                    DrawVirtualParameterRow(paper, font, m, named[i], i);
            }
        });
    }

    /// <summary>One parameter row. Clicking selects it, dragging it onto the graph makes a node that reads it.</summary>
    private void DrawParameterRow(Paper paper, Scribe.FontFile font, OrigamiMetrics m, GraphParameterRecord parameter, int index)
    {
        bool selected = IsSelected(parameter);
        string live = _probe.ReadParameter(parameter);

        ElementBuilder row = ParameterRow(paper, m, "ag_param", index, parameter.Name, selected)
            .OnClick(0, (_, _) => Publish([parameter]));

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
    private static void DrawVirtualParameterRow(Paper paper, Scribe.FontFile font, OrigamiMetrics m, string name, int index)
    {
        ElementBuilder row = ParameterRow(paper, m, "ag_virtual", index, name, selected: false)
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
        paper.Box("kind").Width(8).Height(8).Rounded(4).BackgroundColor(AnimationGraphDomain.KindColor(kind)).IsNotInteractable();
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

            _view.Delete(readers, []);
            Graph!.Parameters.Remove(parameter);
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

    protected override void DrawOverlays(Paper paper, Scribe.FontFile font, OrigamiMetrics m)
    {
        if (_parameterPopup) DrawParameterPopup(paper, font, m);
    }

    /// <summary>The popup that asks for a new parameter's name and type.</summary>
    private void DrawParameterPopup(Paper paper, Scribe.FontFile font, OrigamiMetrics m)
    {
        const float w = 240f;
        Rect root = RootRect;
        float x = Math.Clamp(_parameterPopupAt.X - (float)root.Min.X - w, 4f, MathF.Max(4f, (float)root.Size.X - w - 4f));
        float y = Math.Clamp(_parameterPopupAt.Y - (float)root.Min.Y + 14f, 4f, MathF.Max(4f, (float)root.Size.Y - 160f));

        if (paper.IsKeyPressed(PaperKey.Escape)) { _parameterPopup = false; return; }

        paper.Box("ag_ppBackdrop").PositionType(PositionType.SelfDirected).Left(0).Top(0)
            .Width(UnitValue.Percentage(100)).Height(UnitValue.Percentage(100)).Layer(Layer.Overlay)
            .OnClick(0, (_, _) => _parameterPopup = false);

        string name = _newParameterName.Trim();
        bool taken = Graph!.Parameters.Exists(p => p.Name == name);
        bool valid = name.Length > 0 && !taken;

        using (paper.Column("ag_pp").PositionType(PositionType.SelfDirected).Left(x).Top(y)
            .Width(w).Height(UnitValue.Auto).Layer(Layer.Overlay + 10)
            .BackgroundColor(EditorTheme.Neutral200).Rounded(m.ContainerRounding)
            .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .Padding(m.PaddingLarge, m.PaddingLarge, m.PaddingLarge, m.PaddingLarge).Gap(m.Spacing)
            .DropShadow(0, 8, 24, 0, EditorTheme.Shadow).Enter())
        {
            paper.Box("ag_ppTitle").Width(UnitValue.Stretch()).Height(20).IsNotInteractable()
                .Text("New Parameter", EditorTheme.FontSemiBold ?? font).TextColor(EditorTheme.Ink600)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

            Origami.TextField(paper, "ag_ppName", _newParameterName, v => _newParameterName = v)
                .Placeholder("Name").Width(UnitValue.Stretch()).AutoFocus().SelectAllOnFocus().Show();

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

        Edit("Add Parameter", () => Graph!.Parameters.Add(parameter));
        _parameterPopup = false;
        Publish([parameter]);
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
    [
        NodeValueKind.Number, NodeValueKind.Flag, NodeValueKind.Integer,
        NodeValueKind.Vector, NodeValueKind.Id, NodeValueKind.Target, NodeValueKind.Flag,
    ];

    private static readonly string[] ParameterKindNames = ["Number", "Flag", "Int", "Vector", "Name", "Target", "Trigger"];

    /// <summary>The popup entry that makes a trigger, a flag that turns itself off once used.</summary>
    private const int TriggerChoice = 6;

    /// <summary>What a parameter type is called, in the add popup and the inspector.</summary>
    private static string KindName(NodeValueKind kind)
    {
        int index = Array.IndexOf(ParameterKinds, kind);
        return index < 0 ? kind.ToString() : ParameterKindNames[index];
    }

    internal static string KindName(GraphParameterRecord parameter)
        => parameter.Kind == NodeValueKind.Flag && parameter.Trigger ? ParameterKindNames[TriggerChoice] : KindName(parameter.Kind);

    private static string Join(IReadOnlyList<string> entries) => string.Join(", ", entries);

    private static string JoinWeight(string bone, float weight)
        => Math.Abs(weight - 1f) < 0.0001f ? bone : $"{bone}:{weight.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>A list of names, a row each, with a paired list's number beside each name.</summary>
    private static void ListEditor(Paper paper, string id, string label, bool bones,
        string text, Action<string> setter, string? pairedText = null, Action<string>? pairedSetter = null)
    {
        var entries = new List<string>(GraphCompileContext.Split(text));
        var paired = new List<string>(pairedText != null ? GraphCompileContext.Split(pairedText) : []);

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
                DrawEntry(paper, $"{id}_e{index}", bones, entries, index, Write);

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
            entries.Add(bones ? AnimationNodeFields.DefaultBone : "Name");
            Write();
        }).Subtle().Width(UnitValue.Stretch()).Show();
    }

    private static void DrawEntry(Paper paper, string id, bool bones,
        List<string> entries, int index, Action write)
    {
        if (!bones)
        {
            Origami.TextField(paper, id, entries[index], v => { entries[index] = v; write(); })
                .Width(UnitValue.Stretch()).Show();
            return;
        }

        (string bone, float weight) = GraphCompileContext.SplitWeight(entries[index]);
        AnimationNodeFields.BoneField(paper, id, bone, v => { entries[index] = JoinWeight(v, weight); write(); });

        Origami.NumericField<float>(paper, $"{id}_w", weight, v =>
        {
            entries[index] = JoinWeight(bone, v);
            write();
        }).Width(60).Show();
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
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        var graph = asset as AnimationGraph;
        OrigamiMetrics m = Origami.Current.Metrics;

        using (paper.Row($"{id}_open").Width(UnitValue.Stretch()).Height(34)
            .Margin(m.PaddingLarge, m.PaddingLarge, m.Spacing, m.SpacingMedium).Enter())
        {
            paper.Box($"{id}_openBtn").Width(UnitValue.Stretch()).Height(30)
                .Rounded(m.SmallRounding).BackgroundColor(EditorTheme.Accent)
                .Hovered.BackgroundColor(EditorTheme.AccentBright).End()
                .Text("Open Graph", font).TextColor(Color.White)
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
