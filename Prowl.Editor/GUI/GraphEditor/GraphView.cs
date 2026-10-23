// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GraphEditor;

/// <summary>What is wrong with one node, or with the graph when <see cref="NodeId"/> is empty.</summary>
public readonly struct GraphProblem
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

/// <summary>What is wrong with one node, as a domain or a node editor reports it.</summary>
public sealed class NodeProblems
{
    private readonly List<GraphProblem> _found;
    private readonly string _nodeId;

    internal NodeProblems(List<GraphProblem> found, string nodeId)
    {
        _found = found;
        _nodeId = nodeId;
    }

    /// <summary>Something that stops the graph running.</summary>
    public void Blocking(string message) => _found.Add(new GraphProblem(_nodeId, message, true));

    /// <summary>Something the graph runs with, but that is likely not finished.</summary>
    public void Unfinished(string message) => _found.Add(new GraphProblem(_nodeId, message, false));
}

public enum IdKind { Node, Scope }

/// <summary>What a port carries, with the label and colour every socket and wire of it is drawn with.</summary>
public sealed class PortKind(string name, string label, Color color)
{
    public string Name { get; } = name;
    public string Label { get; } = label;
    public Color Color { get; } = color;
}

/// <summary>One input or output of a node, as the shared layer sees it.</summary>
public sealed class PortInfo
{
    public required string Id { get; init; }

    /// <summary>The name the node type declares, which problems quote.</summary>
    public required string Name { get; init; }

    /// <summary>What the socket shows, which can differ from the name, such as the node feeding a repeating input.</summary>
    public required string Label { get; init; }

    public required PortKind Kind { get; init; }

    /// <summary>True when the node works with nothing wired here.</summary>
    public bool Optional { get; init; }

    /// <summary>Where a positional input sits on its node, or -1 for one taken by name.</summary>
    public int Index { get; init; } = -1;

    /// <summary>Part of a repeating group of inputs.</summary>
    public bool Repeating { get; init; }
}

/// <summary>The output end of a wire.</summary>
public readonly record struct WireEnd(string Node, string Port);

/// <summary>What the create picker knows about a node type before any node of it exists.</summary>
public sealed class NodeTypeInfo
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Category { get; init; }
    public string Description { get; init; } = "";
    public bool Hidden { get; init; }
    public IReadOnlyList<PortKind> InputKinds { get; init; } = [];
    public IReadOnlyList<PortKind> OutputKinds { get; init; } = [];

    /// <summary>The output depends on how a node is set, so the type is offered for any dropped wire.</summary>
    public bool DynamicOutput { get; init; }
}

/// <summary>A detached copy of some nodes with everything inside them, for the clipboard.</summary>
public sealed class GraphFragment<TRecord> where TRecord : class
{
    public readonly List<TRecord> Nodes = [];
    public readonly List<TRecord> Inside = [];
    public readonly List<GraphGroupRecord> Groups = [];
    public readonly List<GraphNoteRecord> Notes = [];
}

/// <summary>What one node of a running graph is doing this frame.</summary>
public readonly record struct GraphNodeState(bool Live, string Value, float Progress);

/// <summary>Reads what a running graph is doing for the editor, without changing any of it.</summary>
public interface IGraphProbe
{
    /// <summary>True while something is running the graph.</summary>
    bool Live { get; }

    /// <summary>The names of what runs the graph, for when there is more than one.</summary>
    IReadOnlyList<string> Candidates { get; }

    /// <summary>Which of <see cref="Candidates"/> the readouts come from.</summary>
    int Watched { get; set; }

    /// <summary>Finds what runs the asset. Called every frame.</summary>
    void Refresh(Asset? asset);

    GraphNodeState? Read(string nodeId);

    /// <summary>The share of a blend an input carries right now, or null when there is none to report.</summary>
    float? WeightInto(string targetId, string sourceId);
}

/// <summary>
/// One graph asset as the shared editor works on it, with no window needed, so the CLI and an undo with no
/// window open use it the same way the window does. The asset's records stay its own.
/// </summary>
public abstract class GraphDomain
{
    protected GraphDomain(Asset asset)
    {
        if (asset is null) throw new ArgumentNullException(nameof(asset));
        Asset = asset;
    }

    public Asset Asset { get; }

    public abstract List<GraphGroupRecord> Groups { get; }
    public abstract List<GraphNoteRecord> Notes { get; }

    /// <summary>The asset's records as a snapshot, for undo and for saving.</summary>
    public abstract EchoObject Snapshot();

    /// <summary>Writes a snapshot's records onto the live asset, leaving the asset itself in place.</summary>
    public abstract bool Restore(EchoObject snapshot, bool compiles);

    /// <summary>Drops what was compiled from the records, so whatever runs the graph picks the change up.</summary>
    public abstract void Invalidate();

    /// <summary>An id unique in the asset.</summary>
    public abstract string NewId(IdKind kind);

    public GraphGroupRecord AddGroup(Float2 position, Float2 size, string owner)
    {
        var group = new GraphGroupRecord { Id = NewId(IdKind.Node), Position = position, Size = size, Owner = owner };
        Groups.Add(group);
        return group;
    }

    public GraphNoteRecord AddNote(Float2 at, string owner)
    {
        var note = new GraphNoteRecord { Id = NewId(IdKind.Node), Position = at, Text = "Note", Owner = owner };
        Notes.Add(note);
        return note;
    }
}

/// <summary>
/// The domain adapter a graph tool writes: storage primitives over its own records. Every edit the shared
/// layer makes is built on them.
/// </summary>
public abstract class GraphDomain<TRecord> : GraphDomain where TRecord : class
{
    protected GraphDomain(Asset asset) : base(asset) { }

    public abstract List<TRecord> Nodes { get; }

    public abstract string IdOf(TRecord record);
    public abstract string TypeOf(TRecord record);
    public abstract string TitleOf(TRecord record);
    public abstract string OwnerOf(TRecord record);
    public abstract void SetOwner(TRecord record, string owner);
    public abstract Float2 PositionOf(TRecord record);
    public abstract void SetPosition(TRecord record, Float2 position);
    public abstract bool CollapsedOf(TRecord record);
    public abstract void SetCollapsed(TRecord record, bool collapsed);
    public abstract TRecord Clone(TRecord record);

    /// <summary>
    /// Rewrites everything in a record that names an id: its own, the scopes it owns, its owner and every wire
    /// it stores, hidden or not. The map answers empty for an id it does not know.
    /// </summary>
    public abstract void RemapIds(TRecord record, Func<string, string> remap);

    /// <summary>Scopes a record owns besides its own id, such as a state machine's states.</summary>
    public virtual IEnumerable<string> OwnedScopes(TRecord record) => [];

    public virtual bool IsKnownType(TRecord record) => true;

    /// <summary>The visible inputs, in the order the card draws them: positional first, then by name.</summary>
    public abstract IReadOnlyList<PortInfo> Inputs(TRecord record);
    public abstract IReadOnlyList<PortInfo> Outputs(TRecord record);

    /// <summary>Any input by id, including a positional one past what is stored or one hidden for now.</summary>
    public abstract PortInfo? InputPort(TRecord record, string portId);

    /// <summary>The positional input at an index. In a repeating group it is the group's template for that slot.</summary>
    public abstract PortInfo? PortAt(TRecord record, int index);

    /// <summary>Every wire a record stores, by the input it arrives at: hidden pins and exposed settings included.</summary>
    public abstract IEnumerable<(string Port, WireEnd Source)> AllSources(TRecord record);

    public abstract WireEnd? Source(TRecord record, string inputPort);

    /// <summary>Wires an input, or clears it with null. A positional input past what is stored is made first.</summary>
    public abstract void SetSource(TRecord record, string inputPort, WireEnd? source);

    /// <summary>How many positional inputs are stored.</summary>
    public virtual int InputCount(TRecord record) => 0;
    public virtual void SetInputCount(TRecord record, int count) { }

    /// <summary>Where a record's repeating group of positional inputs starts and how many inputs make one repetition.</summary>
    public virtual (int Start, int Size)? Repeating(TRecord record) => null;

    public virtual bool Accepts(PortKind input, PortKind output) => ReferenceEquals(input, output);
    public virtual bool AllowsCycles => false;

    public abstract IReadOnlyList<NodeTypeInfo> Catalog { get; }

    /// <summary>A new node, with whatever setup its type needs.</summary>
    public abstract TRecord? Create(string typeId, Float2 at, string owner);

    /// <summary>Runs after a node is removed by a delete.</summary>
    public virtual void OnRemoved(TRecord record) { }

    public virtual bool IsRemovable(TRecord record) => true;
    public virtual bool IsCopyable(TRecord record) => true;

    /// <summary>Problems with one node beyond what the shared checks find.</summary>
    public virtual void Validate(TRecord record, NodeProblems problems) { }

    /// <summary>Problems with a scope as a whole, such as having no output.</summary>
    public virtual void ValidateGraph(string scope, List<GraphProblem> problems) { }

    public TRecord? Find(string id)
    {
        if (id.Length == 0) return null;
        foreach (TRecord record in Nodes)
            if (IdOf(record) == id) return record;
        return null;
    }

    public PortKind? OutputKind(TRecord record, string port)
    {
        foreach (PortInfo output in Outputs(record))
            if (output.Id == port) return output.Kind;
        return null;
    }

    public PortKind? OutputKind(WireEnd end) => Find(end.Node) is { } record ? OutputKind(record, end.Port) : null;

    // Repeating inputs

    /// <summary>An input count rounded up to whole repetitions of the repeating group.</summary>
    public int WholeGroups(TRecord record, int count)
    {
        if (Repeating(record) is not { Size: > 0 } group || count <= group.Start) return count;
        return group.Start + (count - group.Start + group.Size - 1) / group.Size * group.Size;
    }

    /// <summary>Pads the positional inputs out to whole groups.</summary>
    public void PadRepeating(TRecord record)
    {
        int whole = WholeGroups(record, InputCount(record));
        if (whole > InputCount(record)) SetInputCount(record, whole);
    }

    /// <summary>Drops trailing groups nothing is wired into, so an emptied stack does not keep its pins.</summary>
    public void TrimRepeating(TRecord record)
    {
        if (Repeating(record) is not { Size: > 0 } group) return;

        while (InputCount(record) >= group.Start + group.Size)
        {
            int count = InputCount(record);
            for (int pin = count - group.Size; pin < count; pin++)
                if (PortAt(record, pin) is { } port && Source(record, port.Id) != null) return;

            SetInputCount(record, count - group.Size);
        }
    }

    /// <summary>The input a wire dropped on the open socket lands on: the first free one of its kind.</summary>
    public string? PlaceholderTarget(TRecord record, PortKind? kind)
    {
        int count = InputCount(record);
        if (Repeating(record) is not { Size: > 0 } group) return PortAt(record, count)?.Id;

        for (int pin = group.Start; pin < count; pin++)
            if (PortAt(record, pin) is { } port && Source(record, port.Id) == null && Fits(port, kind)) return port.Id;

        int start = Math.Max(group.Start, WholeGroups(record, count));
        for (int pin = start; pin < start + group.Size; pin++)
            if (PortAt(record, pin) is { } port && Fits(port, kind)) return port.Id;
        return PortAt(record, start)?.Id;

        bool Fits(PortInfo port, PortKind? wanted) => wanted == null || Accepts(port.Kind, wanted);
    }

    // Edits

    public void Connect(ConnectionRequest request)
    {
        if (Find(request.ToNode) is not { } target) return;

        string? port = request.ToPlaceholder
            ? PlaceholderTarget(target, OutputKind(new WireEnd(request.FromNode, request.FromPort)))
            : request.ToPort;
        if (port == null) return;

        SetSource(target, port, new WireEnd(request.FromNode, request.FromPort));
        PadRepeating(target);
    }

    public void Disconnect(string toNode, string toPort)
    {
        if (Find(toNode) is not { } target) return;

        SetSource(target, toPort, null);
        TrimRepeating(target);
    }

    /// <summary>Whether a wire is allowed: the kinds must agree and, unless cycles are allowed, it must not close a loop.</summary>
    public bool CanConnect(ConnectionRequest request)
    {
        if (Find(request.FromNode) is not { } source || Find(request.ToNode) is not { } target) return false;
        if (OutputKind(source, request.FromPort) is not { } output) return false;

        string? port = request.ToPlaceholder ? PlaceholderTarget(target, output) : request.ToPort;
        if (port == null || InputPort(target, port) is not { } input) return false;

        return Accepts(input.Kind, output) && (AllowsCycles || !DependsOn(source, IdOf(target)));
    }

    /// <summary>Whether a node reads, however indirectly, from another.</summary>
    public bool DependsOn(TRecord record, string upstream)
    {
        if (IdOf(record) == upstream) return true;

        var seen = new HashSet<string>();
        var stack = new Stack<TRecord>();
        stack.Push(record);

        while (stack.Count > 0)
        {
            TRecord current = stack.Pop();
            if (!seen.Add(IdOf(current))) continue;

            foreach ((string _, WireEnd source) in AllSources(current))
            {
                if (source.Node == upstream) return true;
                if (Find(source.Node) is { } next) stack.Push(next);
            }
        }
        return false;
    }

    /// <summary>
    /// Removes nodes, clearing every wire into them, and everything inside them. Wires are given by the input
    /// they arrive at. Nodes that are not removable stay.
    /// </summary>
    public void Delete(IEnumerable<TRecord> records, IEnumerable<(string Node, string Port)> wires)
    {
        foreach ((string node, string port) in wires) Disconnect(node, port);

        var owners = new HashSet<string>();
        foreach (TRecord deleted in records)
        {
            if (!IsRemovable(deleted) || !Nodes.Contains(deleted)) continue;

            string id = IdOf(deleted);
            owners.Add(id);
            foreach (string scope in OwnedScopes(deleted)) owners.Add(scope);
            Nodes.Remove(deleted);

            foreach (TRecord record in Nodes)
            {
                foreach ((string port, WireEnd source) in new List<(string, WireEnd)>(AllSources(record)))
                    if (source.Node == id) SetSource(record, port, null);
                TrimRepeating(record);
            }

            OnRemoved(deleted);
        }

        RemoveInside(owners);
    }

    /// <summary>The given owners and every scope nested inside them.</summary>
    public HashSet<string> ScopesInside(IEnumerable<string> owners)
    {
        var scopes = new HashSet<string>(owners);
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (TRecord record in Nodes)
            {
                if (!scopes.Contains(OwnerOf(record)) || !scopes.Add(IdOf(record))) continue;
                foreach (string scope in OwnedScopes(record)) scopes.Add(scope);
                grew = true;
            }
        }
        return scopes;
    }

    /// <summary>Removes everything inside the given owners: their nodes, groups and notes, and whatever those own.</summary>
    public void RemoveInside(IEnumerable<string> owners)
    {
        HashSet<string> scopes = ScopesInside(owners);
        if (scopes.Count == 0) return;

        Nodes.RemoveAll(r => OwnerOf(r).Length > 0 && scopes.Contains(OwnerOf(r)));
        Groups.RemoveAll(g => g.Owner.Length > 0 && scopes.Contains(g.Owner));
        Notes.RemoveAll(n => n.Owner.Length > 0 && scopes.Contains(n.Owner));
    }

    public GraphFragment<TRecord> Capture(IEnumerable<TRecord> sources)
    {
        var fragment = new GraphFragment<TRecord>();

        var owners = new HashSet<string>();
        foreach (TRecord source in sources)
        {
            if (!IsCopyable(source)) continue;
            fragment.Nodes.Add(Clone(source));
            owners.Add(IdOf(source));
            foreach (string scope in OwnedScopes(source)) owners.Add(scope);
        }
        if (owners.Count == 0) return fragment;

        HashSet<string> scopes = ScopesInside(owners);
        foreach (TRecord record in Nodes)
            if (OwnerOf(record).Length > 0 && scopes.Contains(OwnerOf(record))) fragment.Inside.Add(Clone(record));
        foreach (GraphGroupRecord group in Groups)
            if (group.Owner.Length > 0 && scopes.Contains(group.Owner)) fragment.Groups.Add(group.Clone());
        foreach (GraphNoteRecord note in Notes)
            if (note.Owner.Length > 0 && scopes.Contains(note.Owner)) fragment.Notes.Add(note.Clone());
        return fragment;
    }

    /// <summary>Adds a fragment's nodes with fresh ids into a scope. Only wires between copied nodes are kept.</summary>
    public List<TRecord> Paste(GraphFragment<TRecord> fragment, Float2 offset, string owner)
    {
        var ids = new Dictionary<string, string>();
        foreach (TRecord source in fragment.Nodes) Fresh(source);
        foreach (TRecord source in fragment.Inside) Fresh(source);

        string Remap(string id) => id.Length > 0 && ids.TryGetValue(id, out string? mapped) ? mapped : string.Empty;

        var copies = new List<TRecord>();
        foreach (TRecord source in fragment.Nodes)
        {
            TRecord copy = Clone(source);
            RemapIds(copy, Remap);
            SetOwner(copy, owner);
            SetPosition(copy, PositionOf(source) + offset);
            copies.Add(copy);
            Nodes.Add(copy);
        }
        foreach (TRecord source in fragment.Inside)
        {
            TRecord copy = Clone(source);
            RemapIds(copy, Remap);
            Nodes.Add(copy);
        }

        foreach (GraphGroupRecord group in fragment.Groups)
        {
            GraphGroupRecord copy = group.Clone();
            copy.Id = NewId(IdKind.Node);
            copy.Owner = Remap(group.Owner);
            Groups.Add(copy);
        }
        foreach (GraphNoteRecord note in fragment.Notes)
        {
            GraphNoteRecord copy = note.Clone();
            copy.Id = NewId(IdKind.Node);
            copy.Owner = Remap(note.Owner);
            Notes.Add(copy);
        }

        Invalidate();
        return copies;

        void Fresh(TRecord source)
        {
            ids[IdOf(source)] = NewId(IdKind.Node);
            foreach (string scope in OwnedScopes(source)) ids[scope] = NewId(IdKind.Scope);
        }
    }

    /// <summary>
    /// Everything wrong with the nodes of one scope: unknown types, required inputs left unwired, wires whose
    /// kinds disagree, and loops, then the domain's own checks and <paramref name="extra"/>'s.
    /// </summary>
    public List<GraphProblem> FindProblems(string scope, Action<TRecord, NodeProblems>? extra = null)
    {
        var problems = new List<GraphProblem>();
        ValidateGraph(scope, problems);

        var inScope = new List<TRecord>();
        foreach (TRecord record in Nodes)
            if (OwnerOf(record) == scope) inScope.Add(record);

        foreach (TRecord record in inScope)
        {
            string id = IdOf(record);
            if (!IsKnownType(record))
            {
                problems.Add(new GraphProblem(id, $"'{TypeOf(record)}' is not a node type this build knows about.", true));
                continue;
            }

            foreach (PortInfo port in Inputs(record))
            {
                WireEnd? source = Source(record, port.Id);
                if (source == null)
                {
                    if (port.Index >= 0 && !port.Optional && !port.Repeating)
                        problems.Add(new GraphProblem(id, $"'{port.Name}' has nothing wired into it.", false));
                    continue;
                }

                if (OutputKind(source.Value) is { } output && !Accepts(port.Kind, output))
                    problems.Add(new GraphProblem(id, $"'{port.Name}' takes {port.Kind.Label} but is wired to {output.Label}.", true));
            }

            var nodeProblems = new NodeProblems(problems, id);
            Validate(record, nodeProblems);
            extra?.Invoke(record, nodeProblems);
        }

        if (!AllowsCycles) FindCycles(inScope, problems);
        return problems;
    }

    private void FindCycles(List<TRecord> records, List<GraphProblem> problems)
    {
        var state = new Dictionary<string, int>(records.Count); // 0 unseen, 1 on the stack, 2 done
        var byId = new Dictionary<string, TRecord>(records.Count);
        foreach (TRecord record in records)
        {
            state[IdOf(record)] = 0;
            byId[IdOf(record)] = record;
        }

        foreach (TRecord record in records)
            Walk(IdOf(record));

        void Walk(string id)
        {
            if (state[id] != 0) return;
            state[id] = 1;

            foreach ((string _, WireEnd source) in AllSources(byId[id]))
            {
                if (!state.TryGetValue(source.Node, out int seen)) continue;
                if (seen == 1)
                {
                    problems.Add(new GraphProblem(id, "This node feeds itself, directly or through others.", true));
                    continue;
                }
                Walk(source.Node);
            }

            state[id] = 2;
        }
    }
}

/// <summary>
/// Anything a graph window draws with the node graph widget: its node, wire, group and note lists, and the
/// edits the widget's reports turn into. Groups and notes are handled here for every canvas.
/// </summary>
public abstract class GraphCanvas
{
    protected readonly List<GraphNode> NodeList = [];
    protected readonly List<GraphConnection> WireList = [];
    private readonly List<GraphGroup> _groups = [];
    private readonly List<GraphSticky> _notes = [];
    private bool _stale = true;

    public IReadOnlyList<GraphNode> Nodes => NodeList;
    public IReadOnlyList<GraphConnection> Wires => WireList;
    public IReadOnlyList<GraphGroup> Groups => _groups;
    public IReadOnlyList<GraphSticky> Notes => _notes;

    /// <summary>The asset being shown, or null when nothing is.</summary>
    public abstract GraphDomain? Domain { get; }

    /// <summary>The scope this canvas's groups and notes belong to.</summary>
    public abstract string GroupOwner { get; }

    /// <summary>Joins nodes with arrows instead of wiring ports, for a state machine.</summary>
    public virtual bool Arrows => false;

    /// <summary>Whether copy, paste and duplicate apply here.</summary>
    public virtual bool Copyable => true;

    /// <summary>The undo names of the edits the widget reports.</summary>
    public virtual string MoveText => "Move Nodes";
    public virtual string ConnectText => "Connect Node";
    public virtual string DisconnectText => "Disconnect Node";

    public void Invalidate() => _stale = true;

    public void Sync()
    {
        if (!_stale) return;
        _stale = false;

        NodeList.Clear();
        WireList.Clear();
        _groups.Clear();
        _notes.Clear();
        if (Domain == null) return;

        string owner = GroupOwner;
        foreach (GraphGroupRecord group in Domain.Groups)
            if (group.Owner == owner) _groups.Add(ToWidget(group));
        foreach (GraphNoteRecord note in Domain.Notes)
            if (note.Owner == owner) _notes.Add(ToWidget(note));

        Rebuild();
    }

    /// <summary>Fills <see cref="NodeList"/> and <see cref="WireList"/> from the records.</summary>
    protected abstract void Rebuild();

    /// <summary>Marks what is running. Called every frame after <see cref="Sync"/>.</summary>
    public virtual void Tint() { }

    public abstract void Move(IReadOnlyList<GraphNode> moved, Float2 delta);
    public abstract bool CanConnect(ConnectionRequest request);
    public abstract void Connect(ConnectionRequest request);
    public abstract void Disconnect(GraphConnection wire);
    public abstract void Delete(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphConnection> wires);
    public virtual void ToggleCollapsed(GraphNode node) => node.Collapsed = !node.Collapsed;

    /// <summary>Everything the widget reports as selected goes: nodes, wires, groups and notes.</summary>
    public void DeleteSelection(GraphSelection selection)
    {
        Delete(selection.Nodes, selection.Edges);
        if (Domain == null) return;

        foreach (GraphGroup group in selection.Groups)
            if (GroupOf(group) is { } record) Domain.Groups.Remove(record);
        foreach (GraphSticky note in selection.Stickies)
            if (NoteOf(note) is { } record) Domain.Notes.Remove(record);
    }

    /// <summary>A group around the given nodes, or an empty one at a point when there are none.</summary>
    public GraphGroupRecord? AddGroup(IReadOnlyList<GraphNode> around, Float2 at)
    {
        if (Domain == null) return null;

        const float margin = 24f, header = 32f;
        if (!NodeGraphPreview.TryMeasure(around, Origami.Current.Metrics, out Float2 min, out Float2 size))
            return Domain.AddGroup(at, new GraphGroupRecord().Size, GroupOwner);

        return Domain.AddGroup(new Float2(min.X - margin, min.Y - margin - header),
            new Float2(size.X + margin * 2f, size.Y + margin * 2f + header), GroupOwner);
    }

    public GraphNoteRecord? AddNote(Float2 at) => Domain?.AddNote(at, GroupOwner);

    public static GraphGroupRecord? GroupOf(GraphGroup group) => group.UserData as GraphGroupRecord;

    public static GraphNoteRecord? NoteOf(GraphSticky note) => note.UserData as GraphNoteRecord;

    public static GraphGroup ToWidget(GraphGroupRecord group)
        => new() { Id = group.Id, Title = group.Title, Position = group.Position, Size = group.Size, UserData = group };

    public static GraphSticky ToWidget(GraphNoteRecord note)
        => new() { Id = note.Id, Text = note.Text, Position = note.Position, Size = note.Size, UserData = note };
}

/// <summary>
/// The dataflow canvas: one scope of a graph asset as cards and wires. A tool derives it to say what a card
/// looks like, and gets syncing, problems, connection rules and the live tint from here.
/// </summary>
public abstract class GraphView<TRecord> : GraphCanvas where TRecord : class
{
    private readonly Dictionary<string, TRecord> _records = [];
    private readonly Dictionary<GraphNode, GraphBadge> _liveBadges = [];
    private List<GraphProblem> _problems = [];
    private string _scope = string.Empty;
    private bool _live;

    private GraphDomain<TRecord>? _domain;

    /// <summary>The asset's records, or null when nothing is open. Changing it rebuilds.</summary>
    public GraphDomain<TRecord>? Records
    {
        get => _domain;
        set { _domain = value; Invalidate(); }
    }

    public override GraphDomain? Domain => Records;

    /// <summary>Which graph inside the asset is shown: empty for the root, or an owner's id.</summary>
    public string Scope
    {
        get => _scope;
        set { if (_scope != value) { _scope = value; Invalidate(); } }
    }

    public override string GroupOwner => _scope;

    /// <summary>The running graph to read, or null when nothing is running.</summary>
    public IGraphProbe? Probe { get; set; }

    /// <summary>Whether cards make room for a live readout. Changing it rebuilds.</summary>
    public bool ShowLive
    {
        get => _live;
        set { if (_live != value) { _live = value; Invalidate(); } }
    }

    public IReadOnlyList<GraphProblem> Problems => _problems;

    public TRecord? RecordOf(string nodeId) => _records.TryGetValue(nodeId, out TRecord? record) ? record : null;

    /// <summary>The header of a card: title, width, accent, icon, tooltip and any fixed badge.</summary>
    protected abstract GraphNode CreateNode(TRecord record);

    /// <summary>The body of a card, its settings or a summary, once its ports are in place.</summary>
    protected virtual void DecorateNode(GraphNode node, TRecord record) { }

    /// <summary>Runs on each record in scope before the cards are built.</summary>
    protected virtual void Prepare(TRecord record) { }

    /// <summary>Problems a tool's node editors report for a node.</summary>
    protected virtual void ValidateNode(TRecord record, NodeProblems problems) { }

    /// <summary>What a running node's live badge says.</summary>
    protected virtual string LiveText(TRecord record, GraphNodeState state) => state.Value;

    protected override void Rebuild()
    {
        _records.Clear();
        _liveBadges.Clear();
        _problems = [];
        GraphDomain<TRecord>? domain = Records;
        if (domain == null) return;

        foreach (TRecord record in domain.Nodes)
        {
            if (domain.OwnerOf(record) != _scope) continue;
            _records[domain.IdOf(record)] = record;
            Prepare(record);
        }

        _problems = domain.FindProblems(_scope, ValidateNode);

        foreach (TRecord record in _records.Values)
            NodeList.Add(BuildNode(domain, record));

        foreach (TRecord record in _records.Values)
            foreach (PortInfo port in domain.Inputs(record))
                if (domain.Source(record, port.Id) is { } source && _records.ContainsKey(source.Node))
                    WireList.Add(new GraphConnection(source.Node, source.Port, domain.IdOf(record), port.Id) { Color = port.Kind.Color });
    }

    private GraphNode BuildNode(GraphDomain<TRecord> domain, TRecord record)
    {
        GraphNode node = CreateNode(record);
        node.Id = domain.IdOf(record);
        node.Position = domain.PositionOf(record);
        node.Collapsed = domain.CollapsedOf(record);
        node.Removable = domain.IsRemovable(record);
        node.UserData = record;

        List<GraphProblem> problems = ProblemsFor(node.Id);
        if (problems.Count > 0) node.Badges.Add(ProblemBadge(problems));

        if (!domain.IsKnownType(record)) return node;

        foreach (PortInfo port in domain.Inputs(record))
            node.Inputs.Add(new GraphPort(port.Id, port.Label)
            {
                Color = port.Kind.Color,
                Tooltip = $"{port.Label} ({port.Kind.Label})",
            });

        if (domain.Repeating(record) is { Size: > 0 })
            node.Inputs.Add(new GraphPort(PlaceholderPort, "")
            {
                IsPlaceholder = true,
                Color = EditorTheme.Ink300,
                Tooltip = "Drop a wire here to take another input",
            });

        foreach (PortInfo port in domain.Outputs(record))
            node.Outputs.Add(new GraphPort(port.Id, port.Label)
            {
                Color = port.Kind.Color,
                Tooltip = $"Output ({port.Label})",
            });

        DecorateNode(node, record);
        return node;
    }

    /// <summary>The id of the open socket on a node that takes a list.</summary>
    public const string PlaceholderPort = "add";

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
        Scribe.FontFile? font = EditorTheme.DefaultFont;
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

    /// <summary>Shows what the running graph is doing: running nodes outlined, the rest faded, values live.</summary>
    public override void Tint()
    {
        GraphDomain<TRecord>? domain = Records;
        if (domain == null) return;

        bool live = Probe != null && _live;
        var running = new HashSet<string>();

        foreach (GraphNode node in NodeList)
        {
            if (node.UserData is not TRecord record) continue;

            GraphNodeState? state = live ? Probe!.Read(node.Id) : null;
            bool isRunning = state is { Live: true };
            if (isRunning) running.Add(node.Id);

            node.Outline = isRunning ? EditorTheme.Green400 : null;
            node.Dimmed = live && !isRunning;
            SetLiveBadge(node, isRunning && live ? LiveText(record, state!.Value) : "");
        }

        foreach (GraphConnection wire in WireList)
        {
            TRecord? target = RecordOf(wire.ToNode);
            if (target == null || domain.InputPort(target, wire.ToPort) is not { } port) continue;

            Color colour = port.Kind.Color;
            bool flowing = live && running.Contains(wire.FromNode) && running.Contains(wire.ToNode);

            // A wire into a blend is as thick as its share of the blend.
            float? weight = flowing ? Probe!.WeightInto(wire.ToNode, wire.FromNode) : null;
            if (weight is <= 0.001f) flowing = false;

            wire.Flow = flowing;
            wire.Color = flowing ? EditorTheme.Green400 : live ? Color.FromArgb(60, colour.R, colour.G, colour.B) : colour;
            wire.Thickness = !flowing ? 1f : weight is { } share ? 1f + 3.4f * share : 2.4f;
        }
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

    // The widget's reports, as edits on the records.

    public TRecord? Add(string typeId, Float2 at) => Records?.Create(typeId, at, _scope);

    public override void Move(IReadOnlyList<GraphNode> moved, Float2 delta)
    {
        foreach (GraphNode node in moved)
        {
            node.Position += delta;
            if (RecordOf(node.Id) is { } record) Records!.SetPosition(record, node.Position);
        }
    }

    public override bool CanConnect(ConnectionRequest request) => Records?.CanConnect(request) ?? false;

    public override void Connect(ConnectionRequest request) => Records?.Connect(request);

    public override void Disconnect(GraphConnection wire) => Records?.Disconnect(wire.ToNode, wire.ToPort);

    public override void Delete(IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphConnection> wires)
    {
        if (Records == null) return;

        var records = new List<TRecord>();
        foreach (GraphNode node in nodes)
            if (RecordOf(node.Id) is { } record) records.Add(record);

        var ends = new List<(string, string)>();
        foreach (GraphConnection wire in wires) ends.Add((wire.ToNode, wire.ToPort));

        Records.Delete(records, ends);
    }

    public override void ToggleCollapsed(GraphNode node)
    {
        node.Collapsed = !node.Collapsed;
        if (RecordOf(node.Id) is { } record) Records!.SetCollapsed(record, node.Collapsed);
    }

    public List<TRecord> RecordsOf(IReadOnlyList<GraphNode> nodes)
    {
        var records = new List<TRecord>();
        foreach (GraphNode node in nodes)
            if (RecordOf(node.Id) is { } record) records.Add(record);
        return records;
    }
}
