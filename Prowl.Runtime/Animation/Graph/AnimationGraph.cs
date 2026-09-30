// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Motion;

using MotionAvatar = Prowl.Motion.Avatar;
using MotionGraph = Prowl.Motion.AnimationGraph;
using MotionSkeleton = Prowl.Motion.Skeleton;

using Prowl.Runtime.AnimationNodes;

namespace Prowl.Runtime;

/// <summary>A graph of animation nodes, stored as records and compiled into a Motion graph.</summary>
[CreateAssetMenu("Animation Graph", Extension = ".animgraph", Order = 1150)]
public sealed class AnimationGraph : Asset
{
    public List<GraphNodeRecord> Nodes = new();
    public List<GraphParameterRecord> Parameters = new();

    public string RootNode = string.Empty;

    /// <summary>Boxes and notes for keeping a large graph readable. The compiler ignores both.</summary>
    public List<GraphGroupRecord> Groups = new();
    public List<GraphNoteRecord> Notes = new();

    /// <summary>One rig's compile: the Motion graph, where each record landed in it, and what it was built from.</summary>
    private sealed class RigCompile
    {
        public required MotionGraph Graph;
        public required Dictionary<string, int> Nodes;
        public required Dictionary<string, int[]> States;
        public required int DeepVersion;
    }

    // One compile per rig, so characters on different skeletons can share the graph.
    [NonSerialized] private Dictionary<MotionSkeleton, RigCompile>? _compiles;
    [NonSerialized] private int _version;

    /// <summary>Goes up with every edit to any graph, so a check for edits can skip the walk when nothing changed.</summary>
    internal static int Edits { get; private set; }

    // Goes up when the node types reload, which every compile is built from.
    private static int s_nodeTypes;

    /// <summary>Marks every graph out of date after the node types reload, so each compiles again with the new ones.</summary>
    internal static void NodeTypesChanged()
    {
        s_nodeTypes++;
        Edits++;
    }

    [NonSerialized] private long _changedAt;

    /// <summary>Goes up with every edit, so anything running this graph can tell it is out of date.</summary>
    public int Version => _version;

    /// <summary>This graph's version combined with every sub graph it runs.</summary>
    public int DeepVersion => unchecked(DeepVersionOf(new HashSet<AnimationGraph>()) * 31 + s_nodeTypes);

    /// <summary>Milliseconds since the last edit to this graph or any sub graph it runs, for letting a drag settle.</summary>
    public long SinceChanged => Environment.TickCount64 - LatestChange(new HashSet<AnimationGraph>());

    public AnimationGraph() : base("Animation Graph") { }

    /// <summary>The node and state maps of the compile that produced a running graph.</summary>
    public bool TryGetCompiledMaps(MotionGraph running, out IReadOnlyDictionary<string, int> nodes, out IReadOnlyDictionary<string, int[]> states)
    {
        if (_compiles != null)
            foreach (RigCompile compile in _compiles.Values)
                if (ReferenceEquals(compile.Graph, running))
                {
                    nodes = compile.Nodes;
                    states = compile.States;
                    return true;
                }

        nodes = null!;
        states = null!;
        return false;
    }

    /// <summary>Loads every asset the graph reads, its clips, masks, avatars and sub graphs, blocking until each is in.</summary>
    public void LoadDependencies() => LoadDependencies(new HashSet<AnimationGraph>());

    private void LoadDependencies(HashSet<AnimationGraph> visiting)
    {
        if (!visiting.Add(this)) return;
        Load();

        foreach (GraphNodeRecord record in Nodes)
        {
            foreach (NodeValue value in record.Properties.Values)
            {
                switch (value.Kind)
                {
                    case NodeValueKind.Clip when value.Clip is { } clip: clip.Load(); break;
                    case NodeValueKind.Mask when value.Mask is { } mask: mask.Load(); break;
                    case NodeValueKind.Avatar when value.Avatar is { } avatar: avatar.Load(); break;
                    case NodeValueKind.Graph when value.Graph is { } graph: graph.LoadDependencies(visiting); break;
                }
            }

            foreach (GraphStateRecord state in record.States)
                if (state.Graph is { } played) played.LoadDependencies(visiting);
        }
    }

    // Takes in the content version too, so a refill that brings back an older edit count still reads as a change.
    private int DeepVersionOf(HashSet<AnimationGraph> visiting)
    {
        if (!visiting.Add(this)) return 0;

        int version = unchecked(_version * 31 + ContentVersion);
        foreach (AnimationGraph inner in SubGraphs())
            version = unchecked(version * 31 + inner.DeepVersionOf(visiting));
        return version;
    }

    private long LatestChange(HashSet<AnimationGraph> visiting)
    {
        if (!visiting.Add(this)) return long.MinValue;

        long latest = _changedAt;
        foreach (AnimationGraph inner in SubGraphs())
            latest = Math.Max(latest, inner.LatestChange(visiting));
        return latest;
    }

    /// <summary>Whether this graph runs another anywhere inside it, through sub graphs and state assets.</summary>
    public bool Runs(AnimationGraph other) => Runs(other, new HashSet<AnimationGraph>());

    private bool Runs(AnimationGraph other, HashSet<AnimationGraph> visiting)
    {
        if (!visiting.Add(this)) return false;

        foreach (AnimationGraph inner in SubGraphs())
            if (ReferenceEquals(inner, other) || inner.Runs(other, visiting)) return true;
        return false;
    }

    private IEnumerable<AnimationGraph> SubGraphs()
    {
        foreach (GraphNodeRecord record in Nodes)
        {
            if (!record.Get(SubGraphNode.Embedded))
                foreach (NodeValue value in record.Properties.Values)
                    if (value.Kind == NodeValueKind.Graph && value.Graph is { } inner)
                        yield return inner;

            foreach (GraphStateRecord state in record.States)
                if (state.UsesAsset && state.Graph is { } played)
                    yield return played;
        }
    }

    /// <summary>Builds the Motion graph for one rig, cached until it or a sub graph changes. Null without a root.</summary>
    public MotionGraph? Compile(MotionSkeleton skeleton, MotionAvatar? avatar = null)
        => Compile(skeleton, avatar, new HashSet<AnimationGraph>());

    /// <summary>Compiles with the graphs already being compiled, so a cycle of sub graphs is caught.</summary>
    internal MotionGraph? Compile(MotionSkeleton skeleton, MotionAvatar? avatar, HashSet<AnimationGraph> visiting)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        EnsureLoaded();

        int deepVersion = DeepVersion;
        _compiles ??= new Dictionary<MotionSkeleton, RigCompile>();
        if (_compiles.TryGetValue(skeleton, out RigCompile? cached) && cached.DeepVersion == deepVersion)
            return cached.Graph;

        var graph = new MotionGraph();
        visiting.Add(this);
        try
        {
            var context = new GraphCompileContext(this, graph, skeleton, avatar, visiting);

            context.DeclareParameters(Parameters);

            int root = context.Node(RootNode);
            if (root < 0)
            {
                Debug.LogWarning($"[AnimationGraph] '{Name}' has no root node, so it produces nothing.");
                return null;
            }

            graph.SetRoot(root);
            var compile = new RigCompile
            {
                Graph = graph,
                Nodes = new Dictionary<string, int>(context.Compiled),
                States = new Dictionary<string, int[]>(context.States),
                DeepVersion = deepVersion,
            };
            _compiles[skeleton] = compile;
            return graph;
        }
        finally
        {
            visiting.Remove(this);
        }
    }

    /// <summary>Drops the compiled graphs so the next use rebuilds them. Call after editing the records.</summary>
    public void Invalidate()
    {
        EnsureLoaded();
        _compiles = null;
        _version++;
        Edits++;
        _changedAt = Environment.TickCount64;
    }

    public GraphNodeRecord AddNode(string type, string? id = null)
    {
        var record = new GraphNodeRecord { Id = id ?? Guid.NewGuid().ToString("N")[..8], Type = type };
        Nodes.Add(record);
        Invalidate();
        return record;
    }

    /// <summary>The one node of a type inside an owner's graph, such as a state's output.</summary>
    public GraphNodeRecord? OwnedNode(string owner, string type)
    {
        foreach (GraphNodeRecord record in Nodes)
            if (record.Owner == owner && record.Type == type) return record;
        return null;
    }

    public GraphNodeRecord? Find(string id)
    {
        foreach (GraphNodeRecord node in Nodes)
            if (node.Id == id) return node;
        return null;
    }

    protected override void OnUnload() => _compiles = null;

    // The edit count keeps moving forward across a refill, so an animator running the old content rebinds.
    protected override void TakeContent(Asset staging)
    {
        int version = _version;
        base.TakeContent(staging);
        _version = Math.Max(version, _version) + 1;
        Edits++;
    }
}
