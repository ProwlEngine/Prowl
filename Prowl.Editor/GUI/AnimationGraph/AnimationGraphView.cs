// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Editor.GraphEditor;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
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

/// <summary>
/// An animation graph asset as the graph editor works on it. Positional pins are ports "in0", "in1" and on,
/// settings exposed as ports and a sub graph's parameters are "prop:" ports, and every node has one output.
/// </summary>
public sealed class AnimationGraphDomain : GraphDomain<GraphNodeRecord>
{
    /// <summary>The id of a node's single output.</summary>
    public const string OutputPort = "out";

    private const string PropertyPrefix = "prop:";

    private static readonly Dictionary<NodePinKind, PortKind> s_kinds = [];

    public AnimationGraphDomain(AnimationGraph graph) : base(graph) => Graph = graph;

    public AnimationGraph Graph { get; }

    public override List<GraphNodeRecord> Nodes => Graph.Nodes;
    public override List<GraphGroupRecord> Groups => Graph.Groups;
    public override List<GraphNoteRecord> Notes => Graph.Notes;

    public override EchoObject Snapshot() => Serializer.Serialize(typeof(object), Graph);

    public override bool Restore(EchoObject snapshot, bool compiles)
    {
        AnimationGraph? restored = Serializer.Deserialize<AnimationGraph>(snapshot.Clone());
        if (restored == null) return false;

        Graph.Nodes = restored.Nodes;
        Graph.Parameters = restored.Parameters;
        Graph.RootNode = restored.RootNode;
        Graph.Groups = restored.Groups;
        Graph.Notes = restored.Notes;
        if (compiles) Graph.Invalidate();
        return true;
    }

    public override void Invalidate() => Graph.Invalidate();

    public override string NewId(IdKind kind)
    {
        if (kind == IdKind.Scope) return Guid.NewGuid().ToString("N");

        while (true)
        {
            string id = Guid.NewGuid().ToString("N")[..8];
            if (Graph.Find(id) == null) return id;
        }
    }

    public override string IdOf(GraphNodeRecord record) => record.Id;
    public override string TypeOf(GraphNodeRecord record) => record.Type;
    public override string TitleOf(GraphNodeRecord record) => record.Title;
    public override string OwnerOf(GraphNodeRecord record) => record.Owner;
    public override void SetOwner(GraphNodeRecord record, string owner) => record.Owner = owner;
    public override Float2 PositionOf(GraphNodeRecord record) => record.EditorPosition;
    public override void SetPosition(GraphNodeRecord record, Float2 position) => record.EditorPosition = position;
    public override bool CollapsedOf(GraphNodeRecord record) => record.Collapsed;
    public override void SetCollapsed(GraphNodeRecord record, bool collapsed) => record.Collapsed = collapsed;
    public override GraphNodeRecord Clone(GraphNodeRecord record) => record.Clone();

    public override void RemapIds(GraphNodeRecord record, Func<string, string> remap)
    {
        record.Id = remap(record.Id);
        record.Owner = remap(record.Owner);
        foreach (GraphStateRecord state in record.States) state.Id = remap(state.Id);
        foreach (GraphInputRecord input in record.Inputs) input.Node = remap(input.Node);
        foreach (string driven in new List<string>(record.PropertyInputs.Keys))
            record.PropertyInputs[driven] = remap(record.PropertyInputs[driven]);
    }

    public override IEnumerable<string> OwnedScopes(GraphNodeRecord record)
    {
        foreach (GraphStateRecord state in record.States) yield return state.Id;
    }

    public override bool IsKnownType(GraphNodeRecord record) => AnimationNodeRegistry.Get(record.Type) != null;

    public override IReadOnlyList<PortInfo> Inputs(GraphNodeRecord record)
    {
        if (AnimationNodeRegistry.Get(record.Type) is not { } type) return [];

        var ports = new List<PortInfo>();
        int stride = type.VariadicStride;

        // Round a variadic node up to whole groups, so a half filled group still shows all its pins.
        int pins = type.WholeGroups(Math.Max(type.Inputs.Count, record.Inputs.Count));
        for (int pin = 0; pin < pins; pin++)
        {
            if (!type.ShowsPin(record, pin) || type.PinAt(pin) is not { } described) continue;

            bool repeating = stride > 0 && pin >= type.VariadicStart;
            ports.Add(new PortInfo
            {
                Id = PinId(pin),
                Name = described.Name,
                Label = repeating ? InputLabel(record, type, pin) : described.Name,
                Kind = KindOf(described.Kind),
                Optional = described.Optional,
                Index = pin,
                Repeating = repeating,
            });
        }

        foreach (InputPin port in NamedPorts(record, type))
            ports.Add(new PortInfo { Id = PropertyPort(port.Name), Name = port.Name, Label = port.Name, Kind = KindOf(port.Kind), Optional = true });
        return ports;
    }

    public override IReadOnlyList<PortInfo> Outputs(GraphNodeRecord record)
    {
        // An output feeds its owner rather than another node, so it has no output port of its own.
        if (IsOutputNode(record) || AnimationNodeRegistry.Get(record.Type) is not { } type) return [];

        NodePinKind output = type.OutputOf(Graph, record);
        return [new PortInfo { Id = OutputPort, Name = KindLabel(output), Label = KindLabel(output), Kind = KindOf(output) }];
    }

    public override PortInfo? InputPort(GraphNodeRecord record, string portId)
    {
        if (AnimationNodeRegistry.Get(record.Type) is not { } type) return null;

        if (PropertyOf(portId) is { } named)
        {
            foreach (InputPin port in NamedPorts(record, type))
                if (port.Name == named)
                    return new PortInfo { Id = portId, Name = port.Name, Label = port.Name, Kind = KindOf(port.Kind), Optional = true };
            return null;
        }

        int pin = PinIndex(portId);
        return pin < 0 ? null : PortAt(record, pin);
    }

    public override PortInfo? PortAt(GraphNodeRecord record, int index)
    {
        if (AnimationNodeRegistry.Get(record.Type) is not { } type || type.PinAt(index) is not { } described) return null;

        return new PortInfo
        {
            Id = PinId(index),
            Name = described.Name,
            Label = described.Name,
            Kind = KindOf(described.Kind),
            Optional = described.Optional,
            Index = index,
            Repeating = type.VariadicStride > 0 && index >= type.VariadicStart,
        };
    }

    public override IEnumerable<(string Port, WireEnd Source)> AllSources(GraphNodeRecord record)
    {
        for (int pin = 0; pin < record.Inputs.Count; pin++)
            if (record.Inputs[pin].Node.Length > 0) yield return (PinId(pin), new WireEnd(record.Inputs[pin].Node, OutputPort));

        foreach ((string driven, string from) in record.PropertyInputs)
            if (from.Length > 0) yield return (PropertyPort(driven), new WireEnd(from, OutputPort));
    }

    public override WireEnd? Source(GraphNodeRecord record, string inputPort)
    {
        if (PropertyOf(inputPort) is { } driven)
            return record.PropertyInputs.TryGetValue(driven, out string? from) && from.Length > 0 ? new WireEnd(from, OutputPort) : null;

        int pin = PinIndex(inputPort);
        return pin >= 0 && pin < record.Inputs.Count && record.Inputs[pin].Node.Length > 0
            ? new WireEnd(record.Inputs[pin].Node, OutputPort) : null;
    }

    public override void SetSource(GraphNodeRecord record, string inputPort, WireEnd? source)
    {
        if (PropertyOf(inputPort) is { } driven)
        {
            // The setting stays exposed with nothing wired to it, which reads the value on the node again.
            if (source is { } wired) record.PropertyInputs[driven] = wired.Node;
            else if (record.PropertyInputs.ContainsKey(driven)) record.PropertyInputs[driven] = string.Empty;
            return;
        }

        int pin = PinIndex(inputPort);
        if (pin < 0) return;

        if (source is not { } end)
        {
            if (pin < record.Inputs.Count) record.Inputs[pin].Node = string.Empty;
            return;
        }

        while (record.Inputs.Count <= pin) record.Inputs.Add(new GraphInputRecord());
        record.Inputs[pin].Node = end.Node;
    }

    public override int InputCount(GraphNodeRecord record) => record.Inputs.Count;

    public override void SetInputCount(GraphNodeRecord record, int count)
    {
        while (record.Inputs.Count < count) record.Inputs.Add(new GraphInputRecord());
        if (record.Inputs.Count > count) record.Inputs.RemoveRange(count, record.Inputs.Count - count);
    }

    public override (int Start, int Size)? Repeating(GraphNodeRecord record)
        => AnimationNodeRegistry.Get(record.Type) is { VariadicStride: > 0 } type ? (type.VariadicStart, type.VariadicStride) : null;

    public override IReadOnlyList<NodeTypeInfo> Catalog
    {
        get
        {
            var catalog = new List<NodeTypeInfo>();
            foreach (AnimationGraphNode type in AnimationNodeRegistry.All)
            {
                var inputs = new List<PortKind>(type.Inputs.Count);
                foreach (InputPin pin in type.Inputs) inputs.Add(KindOf(pin.Kind));

                catalog.Add(new NodeTypeInfo
                {
                    Id = type.Id,
                    DisplayName = type.DisplayName,
                    Category = type.Category,
                    Description = type.Description,
                    Hidden = type.Hidden,
                    InputKinds = inputs,
                    OutputKinds = [KindOf(type.Output)],
                    DynamicOutput = type.GetType().GetMethod(nameof(AnimationGraphNode.OutputOf), [typeof(AnimationGraph), typeof(GraphNodeRecord)])?.DeclaringType != typeof(AnimationGraphNode),
                });
            }
            return catalog;
        }
    }

    public override GraphNodeRecord? Create(string typeId, Float2 at, string owner)
    {
        GraphNodeRecord record = Graph.AddNode(typeId);
        record.EditorPosition = at;
        record.Owner = owner;

        AnimationGraphNode? type = AnimationNodeRegistry.Get(typeId);
        if (type != null)
        {
            for (int i = 0; i < type.Inputs.Count; i++) record.Inputs.Add(new GraphInputRecord());
            AnimationNodeEditor.For(type).OnCreated(this, record);
        }

        if (owner.Length == 0 && Graph.RootNode.Length == 0 && type is { IsPose: true }) Graph.RootNode = record.Id;
        return record;
    }

    public override void OnRemoved(GraphNodeRecord record)
    {
        if (Graph.RootNode == record.Id) Graph.RootNode = string.Empty;
    }

    public override bool IsRemovable(GraphNodeRecord record) => !IsOutputNode(record);
    public override bool IsCopyable(GraphNodeRecord record) => !IsOutputNode(record);

    public override void ValidateGraph(string scope, List<GraphProblem> problems)
    {
        if (scope.Length > 0) return;

        if (Graph.RootNode.Length == 0)
            problems.Add(new GraphProblem("", "The graph has no output node, so it produces nothing.", true));
        else if (Find(Graph.RootNode) is not { Owner.Length: 0 })
            problems.Add(new GraphProblem("", "The output node is missing.", true));
    }

    public override void Validate(GraphNodeRecord record, NodeProblems problems)
    {
        if (IsOutputNode(record) && (record.Inputs.Count == 0 || record.Inputs[0].Node.Length == 0))
            problems.Unfinished("Nothing is wired into the pose, so this plays the reference pose.");

        CheckStates(record, problems);
    }

    /// <summary>States that play a missing asset, and states no transition could ever enter.</summary>
    private void CheckStates(GraphNodeRecord machine, NodeProblems problems)
    {
        foreach (GraphStateRecord state in machine.States)
        {
            if (state.IsAny) continue;
            if (state.UsesAsset)
            {
                if (state.Graph.IsNotValid())
                    problems.Blocking($"The graph the state '{state.Name}' plays is missing.");
                else if (LeadsBack(Graph, state.Graph!))
                    problems.Blocking($"The state '{state.Name}' plays '{state.Graph!.Name}', which leads back to this graph, so it would never end.");
                continue;
            }

            if (state.IsDefault || !machine.States.Exists(s => s.Transitions.Exists(t => t.To == state.Name))) continue;

            GraphNodeRecord? output = Graph.OwnedNode(state.Id, AnimationNodeIds.StateOutput);
            bool canEnter = output != null && output.Inputs.Count > 1 && (output.Inputs[1].Node.Length > 0 || output.Inputs[1].Flag);
            if (!canEnter)
                problems.Unfinished($"Nothing can move into '{state.Name}': its Enter is off and nothing is wired to it.");
        }
    }

    /// <summary>What a repeating input is called: the node feeding it, or the pin and its place in the list.</summary>
    public string InputLabel(GraphNodeRecord record, AnimationGraphNode type, int pin)
    {
        if (pin < record.Inputs.Count && Find(record.Inputs[pin].Node) is { } source)
            return source.Title;

        int stride = Math.Max(1, type.VariadicStride);
        return $"{type.PinAt(pin)?.Name ?? "In"} {(pin - type.VariadicStart) / stride + 1}";
    }

    /// <summary>The output node of an owner's graph, made if missing. A state's starts closed to Enter and open to Exit.</summary>
    public GraphNodeRecord EnsureOutput(string owner, string type)
    {
        if (Graph.OwnedNode(owner, type) is { } existing) return existing;

        GraphNodeRecord output = Graph.AddNode(type);
        output.Owner = owner;
        output.EditorPosition = new Float2(360f, 0f);
        if (AnimationNodeRegistry.Get(type) is { } described)
            for (int i = 0; i < described.Inputs.Count; i++) output.Inputs.Add(new GraphInputRecord());

        if (type == AnimationNodeIds.StateOutput) output.Inputs[2].Flag = true;
        return output;
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

    /// <summary>True for the nodes a graph places itself and always has one of, a state's or sub graph's output.</summary>
    public static bool IsOutputNode(GraphNodeRecord record)
        => record.Type is AnimationNodeIds.StateOutput or AnimationNodeIds.GraphOutput;

    /// <summary>Whether playing a graph from inside another would come back round to the other.</summary>
    public static bool LeadsBack(AnimationGraph graph, AnimationGraph played)
        => ReferenceEquals(played, graph) || played.Runs(graph);

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

    public static string PinId(int index) => "in" + index;

    /// <summary>The port a setting exposed as an input sits on, which is named rather than numbered.</summary>
    public static string PropertyPort(string property) => PropertyPrefix + property;

    /// <summary>The setting a port drives, or null when the port is one of the node's own pins.</summary>
    public static string? PropertyOf(string portId)
        => portId.StartsWith(PropertyPrefix, StringComparison.Ordinal) ? portId[PropertyPrefix.Length..] : null;

    public static int PinIndex(string portId)
        => portId.StartsWith("in", StringComparison.Ordinal) && int.TryParse(portId[2..], out int index) ? index : -1;

    /// <summary>The port kind a pin kind is drawn and checked as. One instance per kind, so kinds compare by reference.</summary>
    public static PortKind KindOf(NodePinKind kind)
    {
        lock (s_kinds)
        {
            if (!s_kinds.TryGetValue(kind, out PortKind? port))
                s_kinds[kind] = port = new PortKind(kind.ToString(), KindLabel(kind), KindColor(kind));
            return port;
        }
    }

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

/// <summary>One scope of an animation graph as cards: each node's editor fills its card and checks it.</summary>
internal sealed class AnimationGraphView : GraphView<GraphNodeRecord>
{
    /// <summary>A view per graph a sub graph node runs, kept only to draw its thumbnail.</summary>
    private readonly Dictionary<string, AnimationGraphView> _inner = [];

    public AnimationGraphView() => Editing = new AnimationGraphEditing(this);

    public AnimationGraphEditing Editing { get; }

    /// <summary>Whether cards show their settings. Without them a card shows its ports and a summary.</summary>
    public bool ShowsCards { get; set; }

    /// <summary>Whether this view keeps node names in step with their settings. Off for thumbnails.</summary>
    public bool WritesNames { get; set; } = true;

    public AnimationGraphDomain? Animation => Records as AnimationGraphDomain;

    public AnimationGraph? Graph => Animation?.Graph;

    protected override void Prepare(GraphNodeRecord record)
    {
        if (WritesNames) record.Name = AnimationNodeRegistry.Get(record.Type) is { } named ? AnimationNodeEditor.For(named).AutoName(record) : "";
    }

    protected override void ValidateNode(GraphNodeRecord record, NodeProblems problems)
    {
        if (AnimationNodeRegistry.Get(record.Type) is { } type) AnimationNodeEditor.For(type).Validate(Editing, record, problems);
    }

    protected override GraphNode CreateNode(GraphNodeRecord record)
    {
        AnimationGraphNode? type = AnimationNodeRegistry.Get(record.Type);
        AnimationNodeEditor? editor = type == null ? null : AnimationNodeEditor.For(type);

        var node = new GraphNode
        {
            Title = type?.DisplayName ?? record.Type,
            Width = 182f,
            Accent = AnimationGraphDomain.CategoryColor(type?.Category),
            Icon = editor?.Icon ?? EditorIcons.Cube_I,
            Collapsible = true,
            Tooltip = editor?.Tooltip,
        };

        if (Scope.Length == 0 && Graph is { } graph && graph.RootNode == record.Id)
            node.Badge = new GraphBadge("output", EditorTheme.Accent, "The pose this graph produces");
        return node;
    }

    protected override void DecorateNode(GraphNode node, GraphNodeRecord record)
    {
        if (AnimationNodeRegistry.Get(record.Type) is not { } type) return;
        AnimationNodeEditor editor = AnimationNodeEditor.For(type);

        var card = new NodeCard<GraphNodeRecord, AnimationGraphEditing>(node, record, editor, Editing);
        editor.BuildCard(card, record);
        if (node.HeaderOnly || ShowsCards && card.Apply()) return;

        string summary = editor.Summary(record);
        if (summary.Length > 0 && summary != record.Name)
        {
            node.BodyHeight = 20f;
            node.Body = ctx => DrawBody(ctx, summary);
        }
    }

    protected override string LiveText(GraphNodeRecord record, GraphNodeState state)
        => AnimationNodeRegistry.Get(record.Type) is { } type ? AnimationNodeEditor.For(type).LiveText(Editing, record, state.Value) : "";

    /// <summary>The line on a card that has no settings of its own to show.</summary>
    private static void DrawBody(NodeBodyContext ctx, string summary)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
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

        if (!ReferenceEquals(view.Graph, inner)) view.Records = new AnimationGraphDomain(inner);
        view.Scope = scope;
        view.Invalidate();
        view.Sync();
        return view;
    }
}

/// <summary>
/// The inside of a state machine: a card per state, an arrow per transition. A transition is drawn by
/// right dragging from one state to another.
/// </summary>
internal sealed class AnimationGraphStateView : GraphCanvas
{
    private readonly Dictionary<string, GraphStateRecord> _states = [];

    private AnimationGraphDomain? _domain;
    private GraphNodeRecord? _machine;

    public override GraphDomain? Domain => _domain;

    public override string GroupOwner => _machine?.Id ?? string.Empty;

    public override bool Arrows => true;
    public override bool Copyable => false;
    public override string MoveText => "Move States";
    public override string ConnectText => "Add Transition";
    public override string DisconnectText => "Remove Transition";

    /// <summary>The running graph to read, or null when nothing is running.</summary>
    public AnimationGraphProbe? Probe { get; set; }

    public AnimationGraphDomain? Animation
    {
        get => _domain;
        set { if (!ReferenceEquals(_domain, value)) { _domain = value; Invalidate(); } }
    }

    /// <summary>The state machine node being shown, or null when the window is not inside one.</summary>
    public GraphNodeRecord? Machine
    {
        get => _machine;
        set { if (!ReferenceEquals(_machine, value)) { _machine = value; Invalidate(); } }
    }

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
    public override void Tint()
    {
        if (_machine == null) return;

        int active = Probe?.ActiveState(_domain?.Graph, _machine) ?? -1;
        for (int i = 0; i < _machine.States.Count; i++)
        {
            if (!_states.TryGetValue(StateId(_machine.States[i].Name), out GraphStateRecord? state)) continue;

            GraphNode? node = NodeList.Find(n => ReferenceEquals(n.UserData, state));
            if (node == null || state.IsAny) continue;

            bool running = i == active;
            node.Accent = running ? EditorTheme.Green400
                : state.IsDefault ? EditorTheme.Accent
                : EditorTheme.Purple400;
        }

        foreach (GraphConnection wire in WireList)
        {
            GraphStateRecord? from = StateOf(wire.FromNode);
            bool live = active >= 0 && from != null && ReferenceEquals(from, _machine.States[active]);
            wire.Thickness = live ? 2.2f : 1.2f;
        }
    }

    protected override void Rebuild()
    {
        _states.Clear();
        if (_machine == null) return;

        foreach (GraphStateRecord state in _machine.States)
        {
            string id = StateId(state.Name);
            if (!_states.TryAdd(id, state)) continue;
            NodeList.Add(BuildState(state));
        }

        foreach (GraphStateRecord state in _machine.States)
            foreach (GraphTransitionRecord transition in state.Transitions)
            {
                string to = StateId(transition.To);
                if (!_states.ContainsKey(to)) continue;

                WireList.Add(new GraphConnection(StateId(state.Name), "", to, "")
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
            : state.Graph is { } asset && asset.IsValid() ? "Plays " + asset.Name
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

        if (_domain != null && state.UsesAsset && state.Graph is { } played && played.IsValid()
            && AnimationGraphDomain.LeadsBack(_domain.Graph, played))
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
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        ctx.Paper.Box(ctx.Id("pose")).Width(ctx.Paper.Percent(100)).Height(ctx.Paper.Percent(100))
            .Margin(ctx.S(9f), ctx.S(9f), 0, 0).IsNotInteractable()
            .Text(text, font).FontSize(ctx.S(11f))
            .TextColor(EditorTheme.Ink400).Alignment(TextAlignment.MiddleLeft);
    }

    public GraphStateRecord? Add(Float2 at)
    {
        if (_machine == null || _domain == null) return null;

        var state = new GraphStateRecord
        {
            Id = _domain.NewId(IdKind.Scope),
            Name = UniqueName("State"),
            EditorPosition = at,
            IsDefault = !_machine.States.Exists(s => !s.IsAny),
        };
        _machine.States.Add(state);
        _domain.EnsureOutput(state.Id, AnimationNodeIds.StateOutput);
        return state;
    }

    /// <summary>The machine's Any State, made here the first time one is asked for. A machine has one at most.</summary>
    public GraphStateRecord? AddAny(Float2 at)
    {
        if (_machine == null || _domain == null) return null;
        if (_machine.States.Find(s => s.IsAny) is { } existing) return existing;

        var any = new GraphStateRecord { Id = _domain.NewId(IdKind.Scope), Name = UniqueName("Any State"), EditorPosition = at, IsAny = true };
        _machine.States.Add(any);
        return any;
    }

    public bool HasAny => _machine != null && _machine.States.Exists(s => s.IsAny);

    /// <summary>Whether a transition may be drawn: between two different states, and not twice the same way round.</summary>
    public override bool CanConnect(ConnectionRequest request)
    {
        GraphStateRecord? from = StateOf(request.FromNode);
        GraphStateRecord? to = StateOf(request.ToNode);
        return from != null && to != null && !to.IsAny && !ReferenceEquals(from, to) && !from.Transitions.Exists(t => t.To == to.Name);
    }

    /// <summary>A transition from one state to another, unless there already is one that way round.</summary>
    public override void Connect(ConnectionRequest request)
    {
        if (CanConnect(request))
            StateOf(request.FromNode)!.Transitions.Add(new GraphTransitionRecord { To = StateOf(request.ToNode)!.Name, Duration = 0.2f });
    }

    public override void Disconnect(GraphConnection wire)
    {
        GraphStateRecord? from = StateOf(wire.FromNode);
        GraphStateRecord? to = StateOf(wire.ToNode);
        if (from == null || to == null) return;

        from.Transitions.RemoveAll(t => t.To == to.Name);
    }

    public override void Move(IReadOnlyList<GraphNode> moved, Float2 delta)
    {
        foreach (GraphNode node in moved)
        {
            node.Position += delta;
            if (StateOf(node.Id) is { } state) state.EditorPosition = node.Position;
        }
    }

    public override void Delete(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphConnection> wires)
    {
        if (_machine == null) return;

        foreach (GraphConnection wire in wires) Disconnect(wire);

        foreach (GraphNode node in nodes)
        {
            GraphStateRecord? state = StateOf(node.Id);
            if (state == null) continue;

            _machine.States.Remove(state);
            _domain?.RemoveInside([state.Id]);

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
internal sealed class AnimationGraphProbe : IGraphProbe
{
    private readonly List<Animator> _candidates = [];
    private readonly List<string> _names = [];
    private Animator? _animator;
    private AnimationGraph? _graph;

    public IReadOnlyList<string> Candidates => _names;

    /// <summary>The one being watched, or null when nothing in the scene is running this graph.</summary>
    public Animator? Animator => _animator.IsValid() ? _animator : null;

    public bool Live => Application.IsPlaying && Instance != null;

    public int Watched
    {
        get => Animator is { } watched ? _candidates.IndexOf(watched) : -1;
        set { if (value >= 0 && value < _candidates.Count) _animator = _candidates[value]; }
    }

    private MotionGraphInstance? Instance
    {
        get
        {
            Animator? animator = Animator;
            return animator == null ? null : animator.GraphInstance;
        }
    }

    /// <summary>Finds the animators running this graph. Called every frame.</summary>
    public void Refresh(Asset? asset)
    {
        _graph = asset as AnimationGraph;
        _candidates.Clear();
        _names.Clear();

        Guid assetGuid = _graph.IsValid() ? _graph!.AssetID : Guid.Empty;
        if (!Application.IsPlaying || assetGuid == Guid.Empty)
        {
            _animator = null;
            return;
        }

        Scene? scene = Scene.Current;
        if (scene.IsNotValid()) return;

        foreach (Animator animator in scene!.Animation.Animators)
            if (animator.Graph.AssetID == assetGuid && animator.GraphInstance != null)
            {
                _candidates.Add(animator);
                _names.Add(animator.GameObject.Name);
            }

        if (_animator.IsValid() && _candidates.Contains(_animator!)) return;
        _animator = _candidates.Count > 0 ? _candidates[0] : null;
    }

    public GraphNodeState? Read(string nodeId)
    {
        if (!TryMaps(_graph, out MotionGraphInstance instance, out IReadOnlyDictionary<string, int>? nodes, out _)) return null;
        if (!nodes.TryGetValue(nodeId, out int index) || index < 0) return null;

        GraphNodeInstance? node = instance.TryGetNodeInstance(index);
        if (node == null) return null;

        if (node is PoseNodeInstance pose)
            return new GraphNodeState(pose.IsInitialized, "", pose.Duration > 0f ? pose.NormalizedTime : -1f);

        return instance.TryReadValueNode(index, out ParameterValue value)
            ? new GraphNodeState(true, Format(value), -1f)
            : new GraphNodeState(node.IsInitialized, "", -1f);
    }

    /// <summary>A value node's current number, or null when it is not running or is not a number.</summary>
    public float? ReadNumber(GraphNodeRecord record)
    {
        if (!TryMaps(_graph, out MotionGraphInstance instance, out IReadOnlyDictionary<string, int>? nodes, out _)) return null;
        if (!nodes.TryGetValue(record.Id, out int index) || index < 0) return null;

        return instance.TryReadValueNode(index, out ParameterValue value) ? value.AsFloat() : null;
    }

    public float? WeightInto(string targetId, string sourceId)
    {
        if (!TryMaps(_graph, out MotionGraphInstance instance, out IReadOnlyDictionary<string, int>? nodes, out _)) return null;
        if (!nodes.TryGetValue(targetId, out int into) || !nodes.TryGetValue(sourceId, out int from)) return null;

        return instance.TryGetNodeInstance(into) is IBlendWeights blend ? blend.WeightOf(from) : null;
    }

    /// <summary>The state a machine is in, as an index into the record's own states, or -1.</summary>
    public int ActiveState(AnimationGraph? asset, GraphNodeRecord machine)
    {
        if (!TryMaps(asset, out MotionGraphInstance instance, out IReadOnlyDictionary<string, int>? nodes, out IReadOnlyDictionary<string, int[]>? machines)) return -1;
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
