// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.AnimationNodes;
using Prowl.Vector;

using AnimationValueType = Prowl.Motion.AnimationValueType;
using Color = System.Drawing.Color;
using GraphNodeInstance = Prowl.Motion.GraphNodeInstance;
using IBlendWeights = Prowl.Motion.IBlendWeights;
using IStateMachineState = Prowl.Motion.IStateMachineState;
using MotionGraphInstance = Prowl.Motion.AnimationGraphInstance;
using ParameterValue = Prowl.Motion.ParameterValue;
using PoseNodeInstance = Prowl.Motion.PoseNodeInstance;
using Scene = Prowl.Runtime.Resources.Scene;

namespace Prowl.Editor.Inspector;

/// <summary>What is wrong with one node, for the badge on its card and the list in the panel.</summary>
internal readonly struct GraphProblem
{
    public GraphProblem(string nodeId, string message, bool blocking)
    {
        NodeId = nodeId; Message = message; Blocking = blocking;
    }

    public string NodeId { get; }
    public string Message { get; }

    /// <summary>True when the graph cannot run like this, as opposed to merely being unfinished.</summary>
    public bool Blocking { get; }
}

/// <summary>Turns a graph asset's records into the widget's nodes and wires, and the widget's reports back into edits.</summary>
internal sealed class AnimationGraphView
{
    /// <summary>The id the widget uses for a node's single output.</summary>
    public const string OutputPort = "out";

    private readonly List<GraphNode> _nodes = new();
    private readonly List<GraphConnection> _wires = new();
    private readonly Dictionary<string, GraphNodeRecord> _records = new();
    private readonly List<GraphProblem> _problems = new();
    private readonly List<GraphGroup> _groups = new();
    private readonly Dictionary<GraphNode, GraphBadge> _liveBadges = new();

    /// <summary>A view per graph a sub graph node runs, kept only to draw its thumbnail.</summary>
    private readonly Dictionary<string, AnimationGraphView> _inner = new();

    public AnimationGraphView() => Editing = new AnimationGraphEditing(this);

    public AnimationGraphEditing Editing { get; }

    /// <summary>Whether cards show their settings. Without them a card shows its ports and a summary.</summary>
    public bool ShowsCards { get; set; }

    /// <summary>Whether this view keeps node names in step with their settings. Off for thumbnails.</summary>
    public bool WritesNames { get; set; } = true;
    private readonly List<GraphSticky> _notes = new();

    private AnimationGraph? _graph;
    private bool _stale = true;
    private bool _live;

    /// <summary>The running graph to read, or null when nothing is running.</summary>
    public AnimationGraphProbe? Probe { get; set; }

    /// <summary>Whether cards make room for a live readout. Changing it rebuilds.</summary>
    public bool ShowLive
    {
        get => _live;
        set { if (_live != value) { _live = value; _stale = true; } }
    }

    public IReadOnlyList<GraphNode> Nodes => _nodes;
    public IReadOnlyList<GraphConnection> Wires => _wires;
    public IReadOnlyList<GraphProblem> Problems => _problems;
    public IReadOnlyList<GraphGroup> Groups => _groups;
    public IReadOnlyList<GraphSticky> Notes => _notes;

    public static GraphGroupRecord? GroupOf(GraphGroup group) => group.UserData as GraphGroupRecord;

    public static GraphNoteRecord? NoteOf(GraphSticky note) => note.UserData as GraphNoteRecord;

    /// <summary>The widget's box for a group record, shared by this view and the state machine view.</summary>
    public static GraphGroup ToWidget(GraphGroupRecord group)
        => new() { Id = group.Id, Title = group.Title, Position = group.Position, Size = group.Size, UserData = group };

    public static GraphSticky ToWidget(GraphNoteRecord note)
        => new() { Id = note.Id, Text = note.Text, Position = note.Position, Size = note.Size, UserData = note };

    public AnimationGraph? Graph
    {
        get => _graph;
        set { _graph = value; _stale = true; }
    }

    private string _scope = string.Empty;

    /// <summary>Which graph inside the asset is shown: empty for the root, or a state or embedded sub graph id.</summary>
    public string Scope
    {
        get => _scope;
        set { if (_scope != value) { _scope = value; _stale = true; } }
    }

    /// <summary>True for the nodes a graph places itself and always has one of, a state's or sub graph's output.</summary>
    public static bool IsOutputNode(GraphNodeRecord record)
        => record.Type is AnimationNodeIds.StateOutput or AnimationNodeIds.GraphOutput;

    public void Invalidate() => _stale = true;

    public GraphNodeRecord? RecordOf(string nodeId) => _records.TryGetValue(nodeId, out GraphNodeRecord? r) ? r : null;

    public void Sync()
    {
        if (!_stale) return;
        _stale = false;

        _nodes.Clear();
        _wires.Clear();
        _records.Clear();
        _groups.Clear();
        _notes.Clear();
        _liveBadges.Clear();
        if (_graph.IsNotValid()) return;

        foreach (GraphGroupRecord group in _graph!.Groups)
            if (group.Owner == _scope) _groups.Add(ToWidget(group));

        foreach (GraphNoteRecord note in _graph.Notes)
            if (note.Owner == _scope) _notes.Add(ToWidget(note));

        foreach (GraphNodeRecord record in _graph!.Nodes)
        {
            if (record.Owner != _scope) continue;
            _records[record.Id] = record;
            if (WritesNames) record.Name = AnimationNodeRegistry.Get(record.Type) is { } named ? AnimationNodeEditor.For(named).AutoName(record) : "";
        }

        Validate();

        foreach (GraphNodeRecord record in _records.Values)
            _nodes.Add(BuildNode(record));

        foreach (GraphNodeRecord record in _records.Values)
        {
            AnimationGraphNode? type = AnimationNodeRegistry.Get(record.Type);
            for (int pin = 0; pin < record.Inputs.Count; pin++)
            {
                string from = record.Inputs[pin].Node;
                if (from.Length == 0 || !_records.ContainsKey(from)) continue;
                if (type != null && !type.ShowsPin(record, pin)) continue;
                _wires.Add(new GraphConnection(from, OutputPort, record.Id, PinId(pin))
                {
                    Color = KindColor(PinKindAt(record, pin)),
                });
            }
        }

        foreach (GraphNodeRecord record in _records.Values)
        {
            if (AnimationNodeRegistry.Get(record.Type) is not { } type) continue;
            foreach (InputPin port in NamedPorts(record, type))
            {
                if (!record.PropertyInputs.TryGetValue(port.Name, out string? from) || !_records.ContainsKey(from)) continue;
                _wires.Add(new GraphConnection(from, OutputPort, record.Id, PropertyPort(port.Name))
                {
                    Color = KindColor(port.Kind),
                });
            }
        }
    }

    /// <summary>The ports a node has by name: exposed settings, and its type's named inputs.</summary>
    public static List<InputPin> NamedPorts(GraphNodeRecord record, AnimationGraphNode type)
    {
        var ports = new List<InputPin>();
        foreach (NodeSetting property in type.Properties)
            if (property.Drivable && record.PropertyInputs.ContainsKey(property.Key) && type.ShowsProperty(record, property.Key))
                ports.Add(new InputPin(property.Key, NodePinKind.Number));
        ports.AddRange(type.NamedInputs(record));
        return ports;
    }

    private static NodePinKind? NamedPortKind(GraphNodeRecord record, AnimationGraphNode type, string name)
    {
        foreach (InputPin port in NamedPorts(record, type))
            if (port.Name == name) return port.Kind;
        return null;
    }

    /// <summary>Finds what is wrong with the graph as it stands, such as unwired required pins.</summary>
    private void Validate()
    {
        _problems.Clear();
        if (_graph.IsNotValid()) return;

        if (_scope.Length == 0)
        {
            if (_graph!.RootNode.Length == 0)
                _problems.Add(new GraphProblem("", "The graph has no output node, so it produces nothing.", true));
            else if (!_records.ContainsKey(_graph.RootNode))
                _problems.Add(new GraphProblem("", "The output node is missing.", true));
        }

        foreach (GraphNodeRecord record in _records.Values)
        {
            AnimationGraphNode? type = AnimationNodeRegistry.Get(record.Type);
            if (type == null)
            {
                _problems.Add(new GraphProblem(record.Id, $"'{record.Type}' is not a node type this build knows about.", true));
                continue;
            }

            for (int pin = 0; pin < type.Inputs.Count; pin++)
            {
                InputPin described = type.Inputs[pin];
                if (described.Optional || described.Variadic || !type.ShowsPin(record, pin)) continue;

                bool wired = pin < record.Inputs.Count && record.Inputs[pin].Node.Length > 0;
                if (!wired)
                    _problems.Add(new GraphProblem(record.Id, $"'{described.Name}' has nothing wired into it.", false));
            }

            if (IsOutputNode(record) && (record.Inputs.Count == 0 || record.Inputs[0].Node.Length == 0))
                _problems.Add(new GraphProblem(record.Id, "Nothing is wired into the pose, so this plays the reference pose.", false));

            CheckWireKinds(record, type);
            CheckStates(record);
            AnimationNodeEditor.For(type).Validate(_graph!, record, new NodeProblems(_problems, record.Id));
        }

        FindCycles();
    }

    /// <summary>Wires whose ends no longer agree on a kind, which stop the graph binding.</summary>
    private void CheckWireKinds(GraphNodeRecord record, AnimationGraphNode type)
    {
        for (int pin = 0; pin < record.Inputs.Count; pin++)
        {
            if (!type.ShowsPin(record, pin)) continue;
            if (RecordOf(record.Inputs[pin].Node) is not { } source || OutputKind(source) is not { } output) continue;

            if (type.PinAt(pin) is { } described && described.Kind != output)
                _problems.Add(new GraphProblem(record.Id,
                    $"'{described.Name}' takes {KindLabel(described.Kind)} but is wired to {KindLabel(output)}.", true));
        }
    }

    /// <summary>States that play a missing asset, and states no transition could ever enter.</summary>
    private void CheckStates(GraphNodeRecord machine)
    {
        foreach (GraphStateRecord state in machine.States)
        {
            if (state.IsAny) continue;
            if (state.UsesAsset)
            {
                if (state.Graph.Res.IsNotValid())
                    _problems.Add(new GraphProblem(machine.Id, $"The graph the state '{state.Name}' plays is missing.", true));
                else if (LeadsBack(_graph!, state.Graph.Res!))
                    _problems.Add(new GraphProblem(machine.Id,
                        $"The state '{state.Name}' plays '{state.Graph.Res!.Name}', which leads back to this graph, so it would never end.", true));
                continue;
            }

            if (state.IsDefault || !machine.States.Exists(s => s.Transitions.Exists(t => t.To == state.Name))) continue;

            GraphNodeRecord? output = _graph!.OwnedNode(state.Id, AnimationNodeIds.StateOutput);
            bool canEnter = output != null && output.Inputs.Count > 1 && (output.Inputs[1].Node.Length > 0 || output.Inputs[1].Flag);
            if (!canEnter)
                _problems.Add(new GraphProblem(machine.Id,
                    $"Nothing can move into '{state.Name}': its Enter is off and nothing is wired to it.", false));
        }
    }

    /// <summary>Whether playing a graph from inside another would come back round to the other.</summary>
    public static bool LeadsBack(AnimationGraph graph, AnimationGraph played)
        => ReferenceEquals(played, graph) || played.Runs(graph);

    private void FindCycles()
    {
        var state = new Dictionary<string, int>(_records.Count); // 0 unseen, 1 on the stack, 2 done
        foreach (string id in _records.Keys) state[id] = 0;

        foreach (string id in _records.Keys)
            Walk(id);

        void Walk(string id)
        {
            if (state[id] != 0) return;
            state[id] = 1;

            foreach (GraphInputRecord input in _records[id].Inputs)
            {
                if (input.Node.Length == 0 || !state.ContainsKey(input.Node)) continue;
                if (state[input.Node] == 1)
                {
                    _problems.Add(new GraphProblem(id, "This node feeds itself, directly or through others.", true));
                    continue;
                }
                Walk(input.Node);
            }

            state[id] = 2;
        }
    }

    private List<GraphProblem> ProblemsFor(string nodeId)
    {
        var found = new List<GraphProblem>();
        foreach (GraphProblem problem in _problems)
            if (problem.NodeId == nodeId) found.Add(problem);

        // What stops the graph reads first.
        found.Sort((a, b) => b.Blocking.CompareTo(a.Blocking));
        return found;
    }

    /// <summary>An icon beside the output chip, with every problem the node has in its tooltip, blocking ones first.</summary>
    private static GraphBadge ProblemBadge(List<GraphProblem> problems)
    {
        bool blocking = problems.Exists(p => p.Blocking);
        string title = problems.Count > 1 ? $"{problems.Count} problems"
            : blocking ? "Stops the graph" : "Not finished";

        return new GraphBadge
        {
            Icon = blocking ? EditorIcons.CircleExclamation_I : EditorIcons.TriangleExclamation_I,
            Color = blocking ? EditorTheme.Red400 : EditorTheme.Amber400,
            Content = new TooltipContent
            {
                Title = title,
                MinWidth = 240f,
                MaxWidth = 300f,
                CustomDraw = paper => DrawProblems(paper, problems),
            },
        };
    }

    private static void DrawProblems(Paper paper, List<GraphProblem> problems)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        for (int i = 0; i < problems.Count; i++)
        {
            GraphProblem problem = problems[i];
            Color colour = problem.Blocking ? EditorTheme.Red400 : EditorTheme.Amber400;

            using (paper.Row($"agp_{i}").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                .Margin(0, 0, i == 0 ? 6 : 4, 0).Gap(8).Enter())
            {
                paper.Box($"agp_dot_{i}").Width(7).Height(7).Margin(0, 0, 5, 0).Rounded(4)
                    .BackgroundColor(colour).IsNotInteractable();
                paper.Box($"agp_text_{i}").Width(UnitValue.Stretch()).Height(UnitValue.Auto).IsNotInteractable()
                    .Text(problem.Message, font).TextColor(EditorTheme.Ink600)
                    .FontSize(EditorTheme.FontSizeSmall).Wrap(Scribe.TextWrapMode.Wrap);
            }
        }
    }

    private GraphNode BuildNode(GraphNodeRecord record)
    {
        AnimationGraphNode? type = AnimationNodeRegistry.Get(record.Type);
        AnimationNodeEditor? editor = type == null ? null : AnimationNodeEditor.For(type);
        bool isRoot = _scope.Length == 0 && _graph.IsValid() && _graph!.RootNode == record.Id;

        var node = new GraphNode
        {
            Id = record.Id,
            Title = type?.DisplayName ?? record.Type,
            Position = record.EditorPosition,
            Width = 182f,
            Accent = CategoryColor(type?.Category),
            Icon = editor?.Icon ?? EditorIcons.Cube_I,
            Collapsible = true,
            Collapsed = record.Collapsed,
            Tooltip = editor?.Tooltip,
            UserData = record,
        };

        if (isRoot) node.Badge = new GraphBadge("output", EditorTheme.Accent, "The pose this graph produces");

        List<GraphProblem> problems = ProblemsFor(record.Id);
        if (problems.Count > 0) node.Badges.Add(ProblemBadge(problems));

        if (type == null || editor == null) return node;

        BuildInputs(node, record, type);

        // An output feeds its owner rather than another node, so it has no output port of its own.
        if (!IsOutputNode(record))
        {
            NodePinKind output = type.OutputOf(_graph, record);
            node.Outputs.Add(new GraphPort(OutputPort, KindLabel(output))
            {
                Color = KindColor(output),
                Tooltip = $"Output ({KindLabel(output)})",
            });
        }

        var card = new AnimationNodeCard(node, record, editor, Editing);
        editor.BuildCard(card, record);
        if (node.HeaderOnly || ShowsCards && card.Apply()) return node;

        string summary = editor.Summary(record);
        if (summary.Length > 0 && summary != record.Name)
        {
            node.BodyHeight = 20f;
            node.Body = ctx => DrawBody(ctx, summary);
        }
        return node;
    }

    /// <summary>One port per pin, plus an open socket on a node that takes a list.</summary>
    private void BuildInputs(GraphNode node, GraphNodeRecord record, AnimationGraphNode type)
    {
        int stride = type.VariadicStride;

        // Round a variadic node up to whole groups, so a half filled group still shows all its pins.
        int pins = type.WholeGroups(Math.Max(type.Inputs.Count, record.Inputs.Count));

        for (int pin = 0; pin < pins; pin++)
        {
            if (!type.ShowsPin(record, pin) || type.PinAt(pin) is not { } described) continue;
            string label = stride > 0 && pin >= type.VariadicStart ? InputLabel(record, type, pin) : described.Name;

            node.Inputs.Add(new GraphPort(PinId(pin), label)
            {
                Color = KindColor(described.Kind),
                Tooltip = $"{label} ({KindLabel(described.Kind)})",
            });
        }

        foreach (InputPin port in NamedPorts(record, type))
            node.Inputs.Add(new GraphPort(PropertyPort(port.Name), port.Name)
            {
                Color = KindColor(port.Kind),
                Tooltip = $"{port.Name} ({KindLabel(port.Kind)})",
            });

        if (stride > 0)
            node.Inputs.Add(new GraphPort("add", "") { IsPlaceholder = true, Color = EditorTheme.Ink300, Tooltip = "Drop a wire here to take another input" });
    }

    /// <summary>The line on a card that has no settings of its own to show.</summary>
    private static void DrawBody(NodeBodyContext ctx, string summary)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        ctx.Paper.Box(ctx.Id("summary")).Width(ctx.Paper.Percent(100)).Height(ctx.Paper.Percent(100))
            .Margin(9f, 9f, 0, 0).IsNotInteractable()
            .Text(summary, font).FontSize(11f)
            .TextColor(EditorTheme.Ink400).Alignment(TextAlignment.MiddleLeft);
    }

    /// <summary>A fresh view of a graph a sub graph node runs, for its card's thumbnail.</summary>
    public AnimationGraphView InnerView(AnimationGraph inner, string scope = "")
    {
        string key = inner.AssetID + "/" + scope;
        if (!_inner.TryGetValue(key, out AnimationGraphView? view))
        {
            view = new AnimationGraphView { WritesNames = false };
            _inner[key] = view;
        }

        view.Graph = inner;
        view.Scope = scope;
        view.Invalidate();
        view.Sync();
        return view;
    }

    /// <summary>What a repeating input is called: the node feeding it, or the pin and its place in the list.</summary>
    public string InputLabel(GraphNodeRecord record, AnimationGraphNode type, int pin)
    {
        if (pin < record.Inputs.Count && RecordOf(record.Inputs[pin].Node) is { } source)
            return source.Title;

        int stride = Math.Max(1, type.VariadicStride);
        return $"{type.PinAt(pin)?.Name ?? "In"} {(pin - type.VariadicStart) / stride + 1}";
    }

    /// <summary>The stored value for a setting, created from its default on first touch.</summary>
    public static NodeValue ValueOf(GraphNodeRecord record, NodeSetting setting)
    {
        if (record.Properties.TryGetValue(setting.Key, out NodeValue? value)) return value;

        value = setting.CreateValue();
        record.Properties[setting.Key] = value;
        return value;
    }

    /// <summary>The names the graph's Virtual Parameter nodes give their values, which read like parameters.</summary>
    public static List<string> VirtualParameters(AnimationGraph graph)
    {
        var names = new List<string>();
        foreach (GraphNodeRecord record in graph.Nodes)
            if (record.Type == AnimationNodeIds.VirtualParameter && record.Get(ParameterNode.NameSetting) is { Length: > 0 } name
                && !names.Contains(name))
                names.Add(name);
        return names;
    }

    /// <summary>Shows what the running graph is doing: running nodes outlined, the rest faded, values live.</summary>
    public void Tint()
    {
        bool live = Probe != null && _live;
        var running = new HashSet<string>();

        foreach (GraphNode node in _nodes)
        {
            if (node.UserData is not GraphNodeRecord record) continue;

            AnimationGraphProbe.NodeState? state = live ? Probe!.Read(_graph, record) : null;
            bool isRunning = state is { Live: true };
            if (isRunning) running.Add(record.Id);

            node.Outline = isRunning ? EditorTheme.Green400 : null;
            node.Dimmed = live && !isRunning;
            SetLiveBadge(node, live ? LiveText(record, state) : "");
        }

        foreach (GraphConnection wire in _wires)
        {
            GraphNodeRecord? target = RecordOf(wire.ToNode);
            if (target == null) continue;

            Color colour = KindColor(PinKindAt(target, PinIndex(wire.ToPort)));
            bool flowing = live && running.Contains(wire.FromNode) && running.Contains(wire.ToNode);

            // A wire into a blend is as thick as its share of the blend.
            float? weight = flowing && RecordOf(wire.FromNode) is { } source ? Probe!.WeightInto(_graph, target, source) : null;
            if (weight is <= 0.001f) flowing = false;

            wire.Flow = flowing;
            wire.Color = flowing ? EditorTheme.Green400 : live ? Color.FromArgb(60, colour.R, colour.G, colour.B) : colour;
            wire.Thickness = !flowing ? 1f : weight is { } share ? 1f + 3.4f * share : 2.4f;
        }
    }

    private string LiveText(GraphNodeRecord record, AnimationGraphProbe.NodeState? state)
    {
        if (state is not { Live: true } running || AnimationNodeRegistry.Get(record.Type) is not { } type) return "";
        return AnimationNodeEditor.For(type).LiveText(Editing, record, running.Value);
    }

    private void SetLiveBadge(GraphNode node, string text)
    {
        _liveBadges.TryGetValue(node, out GraphBadge? badge);
        if (text.Length == 0)
        {
            if (badge != null) node.Badges.Remove(badge);
            _liveBadges.Remove(node);
            return;
        }

        if (badge == null)
        {
            badge = new GraphBadge { Color = EditorTheme.Green400 };
            node.Badges.Insert(0, badge);
            _liveBadges[node] = badge;
        }
        badge.Text = text;
    }

    public void Connect(ConnectionRequest request)
    {
        GraphNodeRecord? target = RecordOf(request.ToNode);
        if (target == null) return;

        if (PropertyOf(request.ToPort) is { } driven)
        {
            target.PropertyInputs[driven] = request.FromNode;
            return;
        }

        int pin = request.ToPlaceholder ? NextVariadicPin(target, KindOf(request.FromNode)) : PinIndex(request.ToPort);
        if (pin < 0) return;

        while (target.Inputs.Count <= pin) target.Inputs.Add(new GraphInputRecord());
        target.Inputs[pin].Node = request.FromNode;
        PadToWholeGroup(target);
    }

    /// <summary>Pads a variadic node's inputs out to whole groups.</summary>
    private static void PadToWholeGroup(GraphNodeRecord record)
    {
        if (AnimationNodeRegistry.Get(record.Type) is not { } type) return;

        int whole = type.WholeGroups(record.Inputs.Count);
        while (record.Inputs.Count < whole) record.Inputs.Add(new GraphInputRecord());
    }

    private NodePinKind? KindOf(string nodeId)
    {
        GraphNodeRecord? record = RecordOf(nodeId);
        return record == null ? null : OutputKind(record);
    }

    /// <summary>The first free pin of a kind, for a wire dropped on the open socket.</summary>
    private static int NextVariadicPin(GraphNodeRecord record, NodePinKind? kind)
    {
        AnimationGraphNode? type = AnimationNodeRegistry.Get(record.Type);
        int stride = type?.VariadicStride ?? 0;
        if (type == null || stride <= 0) return record.Inputs.Count;

        for (int pin = type.VariadicStart; pin < record.Inputs.Count; pin++)
            if (record.Inputs[pin].Node.Length == 0 && (kind == null || type.PinAt(pin)?.Kind == kind)) return pin;

        int start = Math.Max(type.VariadicStart, type.WholeGroups(record.Inputs.Count));
        for (int pin = start; pin < start + stride; pin++)
            if (kind == null || type.PinAt(pin)?.Kind == kind) return pin;
        return start;
    }

    public void Disconnect(GraphConnection wire)
    {
        GraphNodeRecord? target = RecordOf(wire.ToNode);
        if (target == null) return;

        // The setting stays exposed with nothing wired to it, which reads the value on the node again.
        if (PropertyOf(wire.ToPort) is { } driven)
        {
            if (target.PropertyInputs.ContainsKey(driven)) target.PropertyInputs[driven] = string.Empty;
            return;
        }

        int pin = PinIndex(wire.ToPort);
        if (pin < 0 || pin >= target.Inputs.Count) return;

        target.Inputs[pin].Node = string.Empty;
        TrimVariadic(target);
    }

    private static void RemapDriven(GraphNodeRecord record, Func<string, string> remap)
    {
        foreach (string driven in new List<string>(record.PropertyInputs.Keys))
            record.PropertyInputs[driven] = remap(record.PropertyInputs[driven]);
    }

    /// <summary>Drops trailing groups nothing is wired into, so an emptied stack does not keep its pins.</summary>
    public static void TrimVariadic(GraphNodeRecord record)
    {
        AnimationGraphNode? type = AnimationNodeRegistry.Get(record.Type);
        int stride = type?.VariadicStride ?? 0;
        if (type == null || stride <= 0) return;

        while (record.Inputs.Count >= type.VariadicStart + stride)
        {
            int start = record.Inputs.Count - stride;
            for (int pin = start; pin < record.Inputs.Count; pin++)
                if (record.Inputs[pin].Node.Length > 0) return;

            record.Inputs.RemoveRange(start, stride);
        }
    }

    public void Move(IReadOnlyList<GraphNode> moved, Float2 delta)
    {
        foreach (GraphNode node in moved)
        {
            node.Position += delta;
            if (RecordOf(node.Id) is { } record) record.EditorPosition = node.Position;
        }
    }

    public void Delete(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphConnection> wires)
    {
        if (_graph.IsNotValid()) return;

        foreach (GraphConnection wire in wires) Disconnect(wire);

        var inside = new HashSet<string>();
        foreach (GraphNode node in nodes)
        {
            if (RecordOf(node.Id) is not { } deleted || IsOutputNode(deleted)) continue;

            inside.Add(deleted.Id);
            foreach (GraphStateRecord state in deleted.States) inside.Add(state.Id);
            _graph!.Nodes.Remove(deleted);

            foreach (GraphNodeRecord record in _graph.Nodes)
            {
                foreach (GraphInputRecord input in record.Inputs)
                    if (input.Node == node.Id) input.Node = string.Empty;

                foreach (string driven in new List<string>(record.PropertyInputs.Keys))
                    if (record.PropertyInputs[driven] == node.Id) record.PropertyInputs[driven] = string.Empty;

                TrimVariadic(record);
            }

            if (_graph.RootNode == node.Id) _graph.RootNode = string.Empty;
        }

        // Whatever lived inside the deleted nodes, a state machine's states or an embedded graph, goes with them.
        RemoveInside(_graph!, inside);
    }

    /// <summary>The given owners and every graph nested inside them.</summary>
    public static HashSet<string> Inside(AnimationGraph graph, IEnumerable<string> owners)
    {
        var scopes = new HashSet<string>(owners);
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (GraphNodeRecord record in graph.Nodes)
            {
                if (!scopes.Contains(record.Owner) || !scopes.Add(record.Id)) continue;
                foreach (GraphStateRecord state in record.States) scopes.Add(state.Id);
                grew = true;
            }
        }
        return scopes;
    }

    /// <summary>Removes everything inside the given owners: their nodes, groups and notes, and whatever those own.</summary>
    public static void RemoveInside(AnimationGraph graph, IEnumerable<string> owners)
    {
        HashSet<string> scopes = Inside(graph, owners);
        if (scopes.Count == 0) return;

        graph.Nodes.RemoveAll(r => r.Owner.Length > 0 && scopes.Contains(r.Owner));
        graph.Groups.RemoveAll(g => g.Owner.Length > 0 && scopes.Contains(g.Owner));
        graph.Notes.RemoveAll(n => n.Owner.Length > 0 && scopes.Contains(n.Owner));
    }

    /// <summary>The output node of an owner's graph, made if missing. A state's starts closed to Enter and open to Exit.</summary>
    public static GraphNodeRecord EnsureOutput(AnimationGraph graph, string owner, string type)
    {
        if (graph.OwnedNode(owner, type) is { } existing) return existing;

        GraphNodeRecord output = graph.AddNode(type);
        output.Owner = owner;
        output.EditorPosition = new Float2(360f, 0f);
        if (AnimationNodeRegistry.Get(type) is { } described)
            for (int i = 0; i < described.Inputs.Count; i++) output.Inputs.Add(new GraphInputRecord());

        if (type == AnimationNodeIds.StateOutput) output.Inputs[2].Flag = true;
        return output;
    }

    public GraphNodeRecord? Add(string typeId, Float2 at)
    {
        if (_graph.IsNotValid()) return null;

        GraphNodeRecord record = _graph!.AddNode(typeId);
        record.EditorPosition = at;
        record.Owner = _scope;

        AnimationGraphNode? type = AnimationNodeRegistry.Get(typeId);
        if (type != null)
            for (int i = 0; i < type.Inputs.Count; i++) record.Inputs.Add(new GraphInputRecord());

        if (type != null) AnimationNodeEditor.For(type).OnCreated(_graph, record);

        if (_scope.Length == 0 && _graph.RootNode.Length == 0 && type is { IsPose: true }) _graph.RootNode = record.Id;
        return record;
    }

    /// <summary>Copies a node, its properties and its states, but not what is wired into it.</summary>
    public GraphNodeRecord? Duplicate(GraphNodeRecord source)
    {
        List<GraphNodeRecord> copies = Copy(new[] { source }, new Float2(28f, 28f));
        return copies.Count > 0 ? copies[0] : null;
    }

    /// <summary>A detached copy of some nodes with everything inside them, for the clipboard.</summary>
    public sealed class Fragment
    {
        public readonly List<GraphNodeRecord> Nodes = new();
        public readonly List<GraphNodeRecord> Inside = new();
        public readonly List<GraphGroupRecord> Groups = new();
        public readonly List<GraphNoteRecord> Notes = new();
    }

    public Fragment Capture(IReadOnlyList<GraphNodeRecord> sources)
    {
        var fragment = new Fragment();
        if (_graph.IsNotValid()) return fragment;

        var owners = new HashSet<string>();
        foreach (GraphNodeRecord source in sources)
        {
            if (IsOutputNode(source)) continue;
            fragment.Nodes.Add(source.Clone());
            owners.Add(source.Id);
            foreach (GraphStateRecord state in source.States) owners.Add(state.Id);
        }

        HashSet<string> scopes = Inside(_graph!, owners);
        foreach (GraphNodeRecord record in _graph!.Nodes)
            if (record.Owner.Length > 0 && scopes.Contains(record.Owner)) fragment.Inside.Add(record.Clone());
        foreach (GraphGroupRecord group in _graph.Groups)
            if (group.Owner.Length > 0 && scopes.Contains(group.Owner)) fragment.Groups.Add(group.Clone());
        foreach (GraphNoteRecord note in _graph.Notes)
            if (note.Owner.Length > 0 && scopes.Contains(note.Owner)) fragment.Notes.Add(note.Clone());
        return fragment;
    }

    /// <summary>Adds copies of a set of nodes and everything inside them, offset from where they were.</summary>
    public List<GraphNodeRecord> Copy(IReadOnlyList<GraphNodeRecord> sources, Float2 offset) => Paste(Capture(sources), offset);

    /// <summary>Adds a fragment's nodes with fresh ids. Only wires between copied nodes are kept.</summary>
    public List<GraphNodeRecord> Paste(Fragment fragment, Float2 offset)
    {
        var copies = new List<GraphNodeRecord>();
        if (_graph.IsNotValid()) return copies;

        var ids = new Dictionary<string, string>();
        var inside = new List<GraphNodeRecord>();

        GraphNodeRecord Fresh(GraphNodeRecord source)
        {
            GraphNodeRecord copy = source.Clone();
            copy.Id = NewId();
            ids[source.Id] = copy.Id;
            foreach (GraphStateRecord state in copy.States)
            {
                string id = NewStateId();
                ids[state.Id] = id;
                state.Id = id;
            }
            return copy;
        }

        foreach (GraphNodeRecord source in fragment.Nodes)
        {
            GraphNodeRecord copy = Fresh(source);
            copy.EditorPosition = source.EditorPosition + offset;
            copy.Owner = _scope;
            copies.Add(copy);
        }
        foreach (GraphNodeRecord source in fragment.Inside) inside.Add(Fresh(source));

        string Remap(string id) => id.Length > 0 && ids.TryGetValue(id, out string? mapped) ? mapped : string.Empty;

        foreach (GraphNodeRecord copy in copies)
        {
            foreach (GraphInputRecord input in copy.Inputs) input.Node = Remap(input.Node);
            RemapDriven(copy, Remap);
            _graph!.Nodes.Add(copy);
        }
        foreach (GraphNodeRecord copy in inside)
        {
            copy.Owner = Remap(copy.Owner);
            foreach (GraphInputRecord input in copy.Inputs) input.Node = Remap(input.Node);
            RemapDriven(copy, Remap);
            _graph!.Nodes.Add(copy);
        }

        foreach (GraphGroupRecord group in fragment.Groups)
        {
            GraphGroupRecord copy = group.Clone();
            copy.Id = NewId();
            copy.Owner = Remap(group.Owner);
            _graph!.Groups.Add(copy);
        }
        foreach (GraphNoteRecord note in fragment.Notes)
        {
            GraphNoteRecord copy = note.Clone();
            copy.Id = NewId();
            copy.Owner = Remap(note.Owner);
            _graph!.Notes.Add(copy);
        }

        _graph!.Invalidate();
        return copies;
    }

    public static string NewStateId() => Guid.NewGuid().ToString("N");

    /// <summary>A group around the given nodes, or an empty one at a point when there are none.</summary>
    public GraphGroupRecord? AddGroup(IReadOnlyList<GraphNode> around, Float2 at, string owner = "")
    {
        if (_graph.IsNotValid()) return null;

        var group = new GraphGroupRecord { Id = NewId(), Position = at, Owner = owner };
        if (around.Count > 0)
        {
            const float margin = 24f, header = 32f;
            var metrics = Origami.Current.Metrics;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;

            foreach (GraphNode node in around)
            {
                minX = MathF.Min(minX, node.Position.X);
                minY = MathF.Min(minY, node.Position.Y);
                maxX = MathF.Max(maxX, node.Position.X + NodeGraphPreview.MeasureWidth(node, metrics));
                maxY = MathF.Max(maxY, node.Position.Y + NodeGraphPreview.MeasureHeight(node, metrics));
            }

            group.Position = new Float2(minX - margin, minY - margin - header);
            group.Size = new Float2(maxX - minX + margin * 2f, maxY - minY + margin * 2f + header);
        }

        _graph!.Groups.Add(group);
        return group;
    }

    public GraphNoteRecord? AddNote(Float2 at, string owner = "")
    {
        if (_graph.IsNotValid()) return null;

        var note = new GraphNoteRecord { Id = NewId(), Position = at, Text = "Note", Owner = owner };
        _graph!.Notes.Add(note);
        return note;
    }

    private string NewId()
    {
        while (true)
        {
            string id = Guid.NewGuid().ToString("N")[..8];
            if (_graph.IsNotValid() || _graph!.Find(id) == null) return id;
        }
    }

    public void SetRoot(string nodeId)
    {
        if (_graph.IsValid()) _graph!.RootNode = nodeId;
    }

    public void ToggleCollapsed(GraphNode node)
    {
        node.Collapsed = !node.Collapsed;
        if (RecordOf(node.Id) is { } record) record.Collapsed = node.Collapsed;
    }

    /// <summary>Whether a wire is allowed: the kinds must match and it must not close a loop.</summary>
    public bool Validate(ConnectionRequest request)
    {
        GraphNodeRecord? source = RecordOf(request.FromNode);
        GraphNodeRecord? target = RecordOf(request.ToNode);
        if (source == null || target == null) return false;

        AnimationGraphNode? sourceType = AnimationNodeRegistry.Get(source.Type);
        AnimationGraphNode? targetType = AnimationNodeRegistry.Get(target.Type);
        if (sourceType == null || targetType == null) return false;

        NodePinKind output = sourceType.OutputOf(_graph, source);
        if (PropertyOf(request.ToPort) is { } named)
            return NamedPortKind(target, targetType, named) == output && !DependsOn(source, target.Id);

        int pin = request.ToPlaceholder ? NextVariadicPin(target, output) : PinIndex(request.ToPort);
        if (pin < 0) return false;

        return targetType.PinAt(pin)?.Kind == output && !DependsOn(source, target.Id);
    }

    /// <summary>Whether a node reads, however indirectly, from another.</summary>
    private bool DependsOn(GraphNodeRecord record, string upstream)
    {
        if (record.Id == upstream) return true;

        var seen = new HashSet<string>();
        var stack = new Stack<GraphNodeRecord>();
        stack.Push(record);

        while (stack.Count > 0)
        {
            GraphNodeRecord current = stack.Pop();
            if (!seen.Add(current.Id)) continue;

            foreach (GraphInputRecord input in current.Inputs)
            {
                if (input.Node == upstream) return true;
                if (RecordOf(input.Node) is { } next) stack.Push(next);
            }

            foreach (string driver in current.PropertyInputs.Values)
            {
                if (driver == upstream) return true;
                if (RecordOf(driver) is { } next) stack.Push(next);
            }
        }
        return false;
    }

    /// <summary>What a node's output carries, which for a parameter node is its parameter's kind.</summary>
    public NodePinKind? OutputKind(GraphNodeRecord record)
        => AnimationNodeRegistry.Get(record.Type)?.OutputOf(_graph, record);

    public static string PinId(int index) => "in" + index;

    /// <summary>The port a setting exposed as an input sits on, which is named rather than numbered.</summary>
    public static string PropertyPort(string property) => PropertyPrefix + property;

    /// <summary>The setting a port drives, or null when the port is one of the node's own pins.</summary>
    public static string? PropertyOf(string portId)
        => portId.StartsWith(PropertyPrefix, StringComparison.Ordinal) ? portId[PropertyPrefix.Length..] : null;

    private const string PropertyPrefix = "prop:";

    public static int PinIndex(string portId)
        => portId.StartsWith("in", StringComparison.Ordinal) && int.TryParse(portId[2..], out int index) ? index : -1;

    private static NodePinKind PinKindAt(GraphNodeRecord record, int pin)
        => AnimationNodeRegistry.Get(record.Type)?.PinAt(pin)?.Kind ?? NodePinKind.Pose;

    public static string KindLabel(NodePinKind kind) => kind switch
    {
        NodePinKind.Pose => "Pose",
        NodePinKind.Flag => "Flag",
        NodePinKind.Integer => "Int",
        NodePinKind.Vector => "Vector",
        NodePinKind.Target => "Target",
        NodePinKind.Mask => "Mask",
        NodePinKind.Id => "Name",
        _ => "Number",
    };

    /// <summary>One colour per kind, so what fits where is readable without reading anything.</summary>
    public static Color KindColor(NodePinKind kind) => kind switch
    {
        NodePinKind.Pose => EditorTheme.Purple400,
        NodePinKind.Flag => EditorTheme.Red400,
        NodePinKind.Integer => EditorTheme.Blue400,
        NodePinKind.Vector => EditorTheme.Green400,
        NodePinKind.Target => EditorTheme.Green400,
        NodePinKind.Mask => EditorTheme.Ink300,
        _ => EditorTheme.Amber400,
    };

    public static Color CategoryColor(string? category) => category switch
    {
        "Sources" => EditorTheme.Purple400,
        "Modify" => EditorTheme.Purple400,
        "Blending" => EditorTheme.Blue400,
        "Logic" => EditorTheme.Blue400,
        "Locomotion" => EditorTheme.Green400,
        "Rig" => EditorTheme.Red400,
        "Channels" => EditorTheme.Amber400,
        "Masks" => EditorTheme.Ink400,
        "Events" => EditorTheme.Amber400,
        _ => EditorTheme.Accent,
    };
}

/// <summary>
/// The inside of a state machine: a card per state, an arrow per transition. A transition is drawn by
/// right dragging from one state to another.
/// </summary>
internal sealed class AnimationGraphStateView
{
    private readonly List<GraphNode> _nodes = new();
    private readonly List<GraphConnection> _wires = new();
    private readonly List<GraphGroup> _groups = new();
    private readonly List<GraphSticky> _notes = new();
    private readonly Dictionary<string, GraphStateRecord> _states = new();

    private GraphNodeRecord? _machine;
    private AnimationGraph? _graph;
    private bool _stale = true;

    /// <summary>The running graph to read, or null when nothing is running.</summary>
    public AnimationGraphProbe? Probe { get; set; }

    public IReadOnlyList<GraphNode> Nodes => _nodes;
    public IReadOnlyList<GraphConnection> Wires => _wires;
    public IReadOnlyList<GraphGroup> Groups => _groups;
    public IReadOnlyList<GraphSticky> Notes => _notes;

    /// <summary>The state machine node being shown, or null when the window is not inside one.</summary>
    public GraphNodeRecord? Machine
    {
        get => _machine;
        set { _machine = value; _stale = true; }
    }

    public void Invalidate() => _stale = true;

    public GraphStateRecord? StateOf(string nodeId) => _states.TryGetValue(nodeId, out GraphStateRecord? s) ? s : null;

    /// <summary>The transition a wire stands for, found by the pair of states it joins.</summary>
    public GraphTransitionRecord? TransitionOf(GraphConnection wire)
    {
        GraphStateRecord? from = StateOf(wire.FromNode);
        GraphStateRecord? to = StateOf(wire.ToNode);
        if (from == null || to == null) return null;

        foreach (GraphTransitionRecord transition in from.Transitions)
            if (transition.To == to.Name) return transition;
        return null;
    }

    /// <summary>Marks the state the machine is in while the game runs.</summary>
    public void Tint(AnimationGraph? graph)
    {
        if (_machine == null) return;

        int active = Probe?.ActiveState(graph, _machine) ?? -1;
        for (int i = 0; i < _machine.States.Count; i++)
        {
            if (!_states.TryGetValue(StateId(_machine.States[i].Name), out GraphStateRecord? state)) continue;

            GraphNode? node = _nodes.Find(n => ReferenceEquals(n.UserData, state));
            if (node == null || state.IsAny) continue;

            bool running = i == active;
            node.Accent = running ? EditorTheme.Green400
                : state.IsDefault ? EditorTheme.Accent
                : EditorTheme.Purple400;
        }

        foreach (GraphConnection wire in _wires)
        {
            GraphStateRecord? from = StateOf(wire.FromNode);
            bool live = active >= 0 && from != null && ReferenceEquals(from, _machine.States[active]);
            wire.Thickness = live ? 2.2f : 1.2f;
        }
    }

    public void Sync(AnimationGraph? graph)
    {
        if (!_stale) return;
        _stale = false;
        _graph = graph;

        _nodes.Clear();
        _wires.Clear();
        _states.Clear();
        _groups.Clear();
        _notes.Clear();
        if (_machine == null) return;

        if (graph.IsValid())
        {
            foreach (GraphGroupRecord group in graph!.Groups)
                if (group.Owner == _machine.Id) _groups.Add(AnimationGraphView.ToWidget(group));
            foreach (GraphNoteRecord note in graph.Notes)
                if (note.Owner == _machine.Id) _notes.Add(AnimationGraphView.ToWidget(note));
        }

        foreach (GraphStateRecord state in _machine.States)
        {
            string id = StateId(state.Name);
            if (!_states.TryAdd(id, state)) continue;
            _nodes.Add(BuildState(state));
        }

        foreach (GraphStateRecord state in _machine.States)
            foreach (GraphTransitionRecord transition in state.Transitions)
            {
                string to = StateId(transition.To);
                if (!_states.ContainsKey(to)) continue;

                _wires.Add(new GraphConnection(StateId(state.Name), "", to, "")
                {
                    // One transition per pair in a direction, so the pair is a good enough id and the
                    // widget can tell two transitions between the same states apart by direction.
                    Id = $"{state.Name}->{transition.To}",
                    Color = EditorTheme.Purple400,
                    Thickness = 1.2f,
                });
            }
    }

    private GraphNode BuildState(GraphStateRecord state)
    {
        if (state.IsAny) return BuildAnyState(state);

        string source = !state.UsesAsset ? "Own graph"
            : state.Graph.Res is { } asset && asset.IsValid() ? "Plays " + asset.Name
            : "Missing graph";

        var node = new GraphNode
        {
            Id = StateId(state.Name),
            Title = state.Name.Length > 0 ? state.Name : "Unnamed",
            Icon = EditorIcons.CircleNodes_I,
            Position = state.EditorPosition,
            Width = 170f,
            Accent = state.IsDefault ? EditorTheme.Accent : EditorTheme.Purple400,
            Tooltip = "Double click to open this state's graph. Drag with the right button onto another state to add a transition.",
            UserData = state,
            BodyHeight = 20f,
        };

        if (state.IsDefault) node.Badge = new GraphBadge("default", EditorTheme.Accent, "The state the machine starts in");

        if (_graph.IsValid() && state.UsesAsset && state.Graph.Res is { } played && played.IsValid()
            && AnimationGraphView.LeadsBack(_graph!, played))
            node.Badges.Add(new GraphBadge
            {
                Icon = EditorIcons.TriangleExclamation_I,
                Color = EditorTheme.Red400,
                Tooltip = $"'{played.Name}' leads back to this graph, so this state would never end.",
            });

        node.Body = ctx => DrawPoseName(ctx, source);
        return node;
    }

    /// <summary>The Any State: somewhere transitions leave from while the machine is in any other state.</summary>
    private static GraphNode BuildAnyState(GraphStateRecord state) => new()
    {
        Id = StateId(state.Name),
        Title = state.Name,
        Icon = EditorIcons.CircleNodes_I,
        Position = state.EditorPosition,
        Width = 170f,
        Accent = EditorTheme.Amber400,
        Tooltip = "Drag with the right button onto a state to add a transition that can leave any other state.",
        UserData = state,
        BodyHeight = 20f,
        Body = ctx => DrawPoseName(ctx, "Leaves from every state"),
    };

    private static void DrawPoseName(NodeBodyContext ctx, string text)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        ctx.Paper.Box(ctx.Id("pose")).Width(ctx.Paper.Percent(100)).Height(ctx.Paper.Percent(100))
            .Margin(ctx.S(9f), ctx.S(9f), 0, 0).IsNotInteractable()
            .Text(text, font).FontSize(ctx.S(11f))
            .TextColor(EditorTheme.Ink400).Alignment(TextAlignment.MiddleLeft);
    }

    public GraphStateRecord? Add(Float2 at)
    {
        if (_machine == null) return null;

        var state = new GraphStateRecord
        {
            Id = AnimationGraphView.NewStateId(),
            Name = UniqueName("State"),
            EditorPosition = at,
            IsDefault = !_machine.States.Exists(s => !s.IsAny),
        };
        _machine.States.Add(state);
        if (_graph.IsValid()) AnimationGraphView.EnsureOutput(_graph!, state.Id, AnimationNodeIds.StateOutput);
        return state;
    }

    /// <summary>The machine's Any State, made here the first time one is asked for. A machine has one at most.</summary>
    public GraphStateRecord? AddAny(Float2 at)
    {
        if (_machine == null) return null;
        if (_machine.States.Find(s => s.IsAny) is { } existing) return existing;

        var any = new GraphStateRecord { Id = AnimationGraphView.NewStateId(), Name = UniqueName("Any State"), EditorPosition = at, IsAny = true };
        _machine.States.Add(any);
        return any;
    }

    public bool HasAny => _machine != null && _machine.States.Exists(s => s.IsAny);

    /// <summary>Whether a transition may be drawn: between two different states, and not twice the same way round.</summary>
    public bool CanConnect(ConnectionRequest request)
    {
        GraphStateRecord? from = StateOf(request.FromNode);
        GraphStateRecord? to = StateOf(request.ToNode);
        return from != null && to != null && !to.IsAny && !ReferenceEquals(from, to) && !from.Transitions.Exists(t => t.To == to.Name);
    }

    /// <summary>A transition from one state to another, unless there already is one that way round.</summary>
    public void Connect(ConnectionRequest request)
    {
        if (CanConnect(request))
            StateOf(request.FromNode)!.Transitions.Add(new GraphTransitionRecord { To = StateOf(request.ToNode)!.Name, Duration = 0.2f });
    }

    public void Disconnect(GraphConnection wire)
    {
        GraphStateRecord? from = StateOf(wire.FromNode);
        GraphStateRecord? to = StateOf(wire.ToNode);
        if (from == null || to == null) return;

        from.Transitions.RemoveAll(t => t.To == to.Name);
    }

    public void Move(IReadOnlyList<GraphNode> moved, Float2 delta)
    {
        foreach (GraphNode node in moved)
        {
            node.Position += delta;
            if (StateOf(node.Id) is { } state) state.EditorPosition = node.Position;
        }
    }

    public void Delete(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphConnection> wires)
    {
        if (_machine == null) return;

        foreach (GraphConnection wire in wires) Disconnect(wire);

        foreach (GraphNode node in nodes)
        {
            GraphStateRecord? state = StateOf(node.Id);
            if (state == null) continue;

            _machine.States.Remove(state);
            if (_graph.IsValid()) AnimationGraphView.RemoveInside(_graph!, new[] { state.Id });

            // A transition to a state that is gone would fire into nothing, so it goes with it.
            foreach (GraphStateRecord other in _machine.States)
                other.Transitions.RemoveAll(t => t.To == state.Name);
        }

        if (!_machine.States.Exists(s => s.IsDefault) && _machine.States.Find(s => !s.IsAny) is { } first)
            first.IsDefault = true;
    }

    /// <summary>Renames a state, and the transitions leading to it.</summary>
    public void Rename(GraphStateRecord state, string name)
    {
        if (_machine == null || name.Length == 0 || name == state.Name) return;

        string unique = UniqueName(name, state);
        foreach (GraphStateRecord other in _machine.States)
            foreach (GraphTransitionRecord transition in other.Transitions)
                if (transition.To == state.Name) transition.To = unique;

        state.Name = unique;
    }

    public void SetDefault(GraphStateRecord state)
    {
        if (_machine == null || state.IsAny) return;
        foreach (GraphStateRecord other in _machine.States) other.IsDefault = ReferenceEquals(other, state);
    }

    private string UniqueName(string wanted, GraphStateRecord? ignore = null)
    {
        if (_machine == null) return wanted;

        bool Taken(string name)
        {
            foreach (GraphStateRecord state in _machine.States)
                if (!ReferenceEquals(state, ignore) && state.Name == name) return true;
            return false;
        }

        if (!Taken(wanted)) return wanted;
        for (int i = 2; ; i++)
            if (!Taken($"{wanted} {i}")) return $"{wanted} {i}";
    }

    /// <summary>The widget node id of a state.</summary>
    public static string StateId(string stateName) => "state:" + stateName;
}

/// <summary>Reads what a running graph is doing for the editor, without changing any of it.</summary>
internal sealed class AnimationGraphProbe
{
    /// <summary>What one node is doing this frame.</summary>
    internal readonly struct NodeState
    {
        public NodeState(bool live, string value, float progress)
        {
            Live = live; Value = value; Progress = progress;
        }

        /// <summary>True when the node is in the running graph rather than a branch nothing reaches.</summary>
        public bool Live { get; }

        /// <summary>The node's current value, short enough for a card, or empty when it has none.</summary>
        public string Value { get; }

        /// <summary>Where it is through its content in [0,1], or -1 for a node with no time.</summary>
        public float Progress { get; }
    }

    private readonly List<Animator> _candidates = new();
    private Animator? _animator;

    /// <summary>The animators in the scene running this graph, for when there is more than one.</summary>
    public IReadOnlyList<Animator> Candidates => _candidates;

    /// <summary>The one being watched, or null when nothing in the scene is running this graph.</summary>
    public Animator? Animator => _animator.IsValid() ? _animator : null;

    public bool Live => Application.IsPlaying && Instance != null;

    private MotionGraphInstance? Instance
    {
        get
        {
            Animator? animator = Animator;
            return animator == null ? null : animator.GraphInstance;
        }
    }

    public void Watch(Animator? animator) => _animator = animator;

    /// <summary>Finds the animators running this graph. Called every frame.</summary>
    public void Refresh(Guid assetGuid)
    {
        _candidates.Clear();
        if (!Application.IsPlaying || assetGuid == Guid.Empty)
        {
            _animator = null;
            return;
        }

        Scene? scene = Scene.Current;
        if (scene.IsNotValid()) return;

        foreach (Animator animator in scene!.Animation.Animators)
            if (animator.Graph.AssetID == assetGuid && animator.GraphInstance != null) _candidates.Add(animator);

        if (_animator.IsValid() && _candidates.Contains(_animator!)) return;
        _animator = _candidates.Count > 0 ? _candidates[0] : null;
    }

    /// <summary>What a record's node is doing, or null when it is not running.</summary>
    public NodeState? Read(AnimationGraph? asset, GraphNodeRecord record)
    {
        if (!TryMaps(asset, out MotionGraphInstance instance, out var nodes, out _)) return null;
        if (!nodes.TryGetValue(record.Id, out int index) || index < 0) return null;

        GraphNodeInstance? node = instance.TryGetNodeInstance(index);
        if (node == null) return null;

        if (node is PoseNodeInstance pose)
            return new NodeState(pose.IsInitialized, "", pose.Duration > 0f ? pose.NormalizedTime : -1f);

        return instance.TryReadValueNode(index, out ParameterValue value)
            ? new NodeState(true, Format(value), -1f)
            : new NodeState(node.IsInitialized, "", -1f);
    }

    /// <summary>A value node's current number, or null when it is not running or is not a number.</summary>
    public float? ReadNumber(AnimationGraph? asset, GraphNodeRecord record)
    {
        if (!TryMaps(asset, out MotionGraphInstance instance, out var nodes, out _)) return null;
        if (!nodes.TryGetValue(record.Id, out int index) || index < 0) return null;

        return instance.TryReadValueNode(index, out ParameterValue value) ? value.AsFloat() : null;
    }

    /// <summary>The share of a blend an input carries right now, or null when there is none to report.</summary>
    public float? WeightInto(AnimationGraph? asset, GraphNodeRecord target, GraphNodeRecord source)
    {
        if (!TryMaps(asset, out MotionGraphInstance instance, out var nodes, out _)) return null;
        if (!nodes.TryGetValue(target.Id, out int into) || !nodes.TryGetValue(source.Id, out int from)) return null;

        return instance.TryGetNodeInstance(into) is IBlendWeights blend ? blend.WeightOf(from) : null;
    }

    /// <summary>The state a machine is in, as an index into the record's own states, or -1.</summary>
    public int ActiveState(AnimationGraph? asset, GraphNodeRecord machine)
    {
        if (!TryMaps(asset, out MotionGraphInstance instance, out var nodes, out var machines)) return -1;
        if (!nodes.TryGetValue(machine.Id, out int index) || index < 0) return -1;
        if (!machines.TryGetValue(machine.Id, out int[]? states)) return -1;

        if (instance.TryGetNodeInstance(index) is not IStateMachineState running) return -1;

        int active = running.CurrentStateIndex;
        if (active < 0) return -1;
        for (int i = 0; i < states.Length; i++)
            if (states[i] == active) return i;
        return -1;
    }

    private bool TryMaps(AnimationGraph? asset, out MotionGraphInstance instance,
        out IReadOnlyDictionary<string, int> nodes, out IReadOnlyDictionary<string, int[]> machines)
    {
        instance = Instance!;
        nodes = null!;
        machines = null!;
        return instance != null && asset.IsValid() && asset!.TryGetCompiledMaps(instance.Graph, out nodes, out machines);
    }

    /// <summary>A parameter's live value, or empty when nothing is running.</summary>
    public string ReadParameter(GraphParameterRecord parameter)
    {
        MotionGraphInstance? instance = Instance;
        if (instance == null || parameter.Name.Length == 0) return "";
        if (instance.GetParameterIndex(parameter.Name) < 0) return "";

        return parameter.Kind switch
        {
            NodeValueKind.Flag => instance.GetBool(parameter.Name) ? "true" : "false",
            NodeValueKind.Integer => instance.GetInt(parameter.Name).ToString(),
            NodeValueKind.Vector => Format(ParameterValue.FromVector(instance.GetVector(parameter.Name))),
            NodeValueKind.Id => instance.GetId(parameter.Name).ToString(),
            NodeValueKind.Target => "target",
            _ => instance.GetFloat(parameter.Name).ToString("0.###"),
        };
    }

    private static string Format(ParameterValue value) => value.Type switch
    {
        AnimationValueType.Bool => value.Bool ? "true" : "false",
        AnimationValueType.Int => value.Int.ToString(),
        AnimationValueType.Vector => $"{value.Vector.X:0.##}, {value.Vector.Y:0.##}, {value.Vector.Z:0.##}",
        AnimationValueType.Id => value.Id.ToString(),
        AnimationValueType.Target => "target",
        _ => value.Float.ToString("0.###"),
    };
}
