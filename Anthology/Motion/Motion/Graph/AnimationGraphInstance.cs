using System.Collections.Generic;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// A runtime instance of an <see cref="AnimationGraph"/> bound to a skeleton: per-node state plus a
/// control parameter store. Set parameters by name or index, call <see cref="Update(float)"/>, then read
/// <see cref="Pose"/> / <see cref="RootMotionDelta"/> / <see cref="Events"/>.
/// </summary>
public sealed class AnimationGraphInstance
{
    [ThreadStatic] private static List<AnimationGraph>? _instantiating;

    private readonly AnimationGraph _graph;
    private readonly Skeleton _skeleton;
    private readonly Avatar? _avatar;
    private readonly GraphNodeInstance[] _nodes;
    private readonly ParameterValue[] _parameters;
    private readonly AnimationValueType[] _parameterTypes;
    private readonly SampledEventsBuffer _events = new();
    private readonly PoseNodeInstance _root;
    private readonly Pose _previousPose;
    private readonly GraphContext _context;
    private uint _updateId;
    private bool _parametersChanged;

    internal AnimationGraphInstance(AnimationGraph graph, Skeleton skeleton, Avatar? avatar = null)
    {
        _instantiating ??= new List<AnimationGraph>();
        if (_instantiating.Contains(graph))
            throw new GraphValidationException(-1, null, "the graph references itself as a sub graph (directly or through other sub graphs).");

        _instantiating.Add(graph);
        try
        {
            _graph = graph;
            _skeleton = skeleton;
            _avatar = avatar;

            _parameters = new ParameterValue[graph.Parameters.Count];
            _parameterTypes = new AnimationValueType[graph.Parameters.Count];
            foreach (ControlParameterDefinition p in graph.Parameters)
            {
                _parameters[p.ParameterIndex] = p.DefaultValue;
                _parameterTypes[p.ParameterIndex] = p.ValueType;
            }

            _nodes = new GraphNodeInstance[graph.NodeCount];
            for (int i = 0; i < _nodes.Length; i++)
            {
                _nodes[i] = graph.Nodes[i].CreateInstance();
                _nodes[i].NodeIndex = i;
            }

            var bindContext = new GraphBindContext(skeleton, _nodes, avatar) { NodeNames = NodeNames(graph), Definitions = graph.Nodes };
            for (int i = 0; i < _nodes.Length; i++)
            {
                bindContext.CurrentIndex = i;
                try
                {
                    _nodes[i].Bind(bindContext);
                }
                catch (Exception ex) when (ex is InvalidCastException or IndexOutOfRangeException or ArgumentException)
                {
                    throw new GraphValidationException(i, graph.Nodes[i].Name, "failed to bind: " + ex.Message, ex);
                }
            }

            ValidateEdges(graph, bindContext.Edges);
            _root = (PoseNodeInstance)_nodes[graph.RootIndex];
        }
        finally
        {
            _instantiating.Remove(graph);
        }

        _previousPose = new Pose(skeleton);
        _previousPose.SetToReferencePose();
        _previousPose.CalculateModelSpaceTransforms();

        _context = new GraphContext
        {
            Skeleton = _skeleton,
            Avatar = _avatar,
            Parameters = _parameters,
            Events = _events,
            PreviousPose = _previousPose,
        };
    }

    public Skeleton Skeleton => _skeleton;

    /// <summary>The graph's output pose (valid after <see cref="Update(float)"/>).</summary>
    public Pose Pose => _root.Pose;

    /// <summary>The root motion produced this update.</summary>
    public Transform3D RootMotionDelta => _root.RootMotionDelta;

    /// <summary>Normalized time [0,1] of the graph's root output (for sub-graph hosting).</summary>
    public float NormalizedTime => _root.NormalizedTime;

    /// <summary>Duration in seconds of the graph's root output content.</summary>
    public float Duration => _root.Duration;

    /// <summary>The root output's sync track (lets a host phase-lock this graph).</summary>
    public SyncTrack SyncTrack => _root.SyncTrack;

    /// <summary>Events sampled this update.</summary>
    public SampledEventsBuffer Events => _events;

    /// <summary>How nodes in this graph ask the world where the ground is, or null when nothing can be asked.</summary>
    public IGroundProbe? Ground
    {
        get => _ground;
        set => _context.Ground = _ground = value;
    }

    // The probe this instance was given. Played inside another graph, it keeps its own and only borrows
    // the host's when it has none, so a graph plugged into a slot keeps the probe it came with.
    private IGroundProbe? _ground;

    /// <summary>The root pose node (used by hosts to forward its full timing).</summary>
    internal PoseNodeInstance Root => _root;

    /// <summary>The external graph slot currently hosting this instance, if any.</summary>
    internal ExternalGraphSlotInstance? HostSlot { get; set; }

    // ---- Parameters ----------------------------------------------------------------------------

    /// <summary>The index of a control parameter for the index based setters, or -1.</summary>
    public int GetParameterIndex(string name) => _graph.GetParameterIndex(name);

    /// <summary>The value type of a control parameter.</summary>
    public AnimationValueType GetParameterType(int index) => _parameterTypes[index];

    public void SetFloat(string name, float value) => SetParameter(IndexOf(name), ParameterValue.FromFloat(value));
    public void SetBool(string name, bool value) => SetParameter(IndexOf(name), ParameterValue.FromBool(value));
    public void SetInt(string name, int value) => SetParameter(IndexOf(name), ParameterValue.FromInt(value));
    public void SetVector(string name, Float3 value) => SetParameter(IndexOf(name), ParameterValue.FromVector(value));
    public void SetTarget(string name, Target value) => SetParameter(IndexOf(name), ParameterValue.FromTarget(value));
    public void SetId(string name, StringID value) => SetParameter(IndexOf(name), ParameterValue.FromId(value));

    public void SetFloat(int index, float value) => SetParameter(index, ParameterValue.FromFloat(value));
    public void SetBool(int index, bool value) => SetParameter(index, ParameterValue.FromBool(value));
    public void SetInt(int index, int value) => SetParameter(index, ParameterValue.FromInt(value));
    public void SetVector(int index, Float3 value) => SetParameter(index, ParameterValue.FromVector(value));
    public void SetTarget(int index, Target value) => SetParameter(index, ParameterValue.FromTarget(value));
    public void SetId(int index, StringID value) => SetParameter(index, ParameterValue.FromId(value));

    public float GetFloat(string name) => GetParameter(IndexOf(name)).AsFloat();
    public bool GetBool(string name) => GetParameter(IndexOf(name)).AsBool();
    public int GetInt(string name) => GetParameter(IndexOf(name)).AsInt();
    public Float3 GetVector(string name) => GetParameter(IndexOf(name)).Vector;
    public Target GetTarget(string name) => GetParameter(IndexOf(name)).Target;
    public StringID GetId(string name) => GetParameter(IndexOf(name)).AsId();

    /// <summary>Sets a control parameter from a raw tagged value (used for sub-graph parameter forwarding).</summary>
    public void SetParameterValue(string name, ParameterValue value) => SetParameter(IndexOf(name), value);

    /// <summary>Sets a control parameter by index from a raw tagged value.</summary>
    public void SetParameterValue(int index, ParameterValue value) => SetParameter(index, value);

    private int IndexOf(string name)
    {
        int index = _graph.GetParameterIndex(name);
        if (index < 0)
            throw new ArgumentException($"No control parameter named '{name}'.", nameof(name));
        return index;
    }

    private void SetParameter(int index, ParameterValue value)
    {
        if ((uint)index >= (uint)_parameters.Length)
            throw new ArgumentOutOfRangeException(nameof(index), $"No control parameter with index {index}.");

        AnimationValueType expected = _parameterTypes[index];
        if (value.Type != expected)
        {
            if (expected == AnimationValueType.Float && value.Type == AnimationValueType.Int)
                value = ParameterValue.FromFloat(value.Int);
            else
                throw new ArgumentException($"Control parameter '{_graph.Parameters[index].Name}' is {expected}, not {value.Type}.");
        }

        _parameters[index] = value;
        _parametersChanged = true;
    }

    internal ParameterValue GetParameter(int index) => _parameters[index];

    // ---- Evaluation ----------------------------------------------------------------------------

    /// <summary>Advances and evaluates the graph by <paramref name="deltaTime"/> seconds with the character at the origin.</summary>
    public void Update(float deltaTime) => Update(deltaTime, Transform3D.Identity);

    /// <summary>
    /// Advances and evaluates the graph by <paramref name="deltaTime"/> seconds. The character's world
    /// transform lets nodes convert world space targets into character space.
    /// </summary>
    public void Update(float deltaTime, Transform3D worldTransform)
    {
        _events.Clear();
        BeginTick(deltaTime, worldTransform);
        _context.SyncRange = null;
        _context.BranchState = BranchState.Active;

        _root.Update(_context);
        FinishTick();
    }

    /// <summary>
    /// Evaluates this instance as a sub graph inside a parent update: shares the parent's event
    /// buffer, branch state and sync range.
    /// </summary>
    internal void UpdateAsChild(GraphContext parent)
    {
        _context.Events = parent.Events;
        _context.Ground = _ground ?? parent.Ground;
        BeginTick(parent.DeltaTime, parent.WorldTransform);
        _context.SyncRange = parent.SyncRange;
        _context.BranchState = parent.BranchState;

        _root.Update(_context);
        FinishTick();
        _context.Events = _events;
    }

    /// <summary>Initializes the root to start at a sync time (used when this instance is hosted as a sub graph).</summary>
    internal void InitializeAsChild(GraphContext parent, SyncTrackTime? initialTime)
    {
        if (_root.IsInitialized)
            ResetGraphState();
        _context.Ground = _ground ?? parent.Ground;
        BeginTick(0f, parent.WorldTransform);
        _root.Initialize(_context, initialTime);
    }

    /// <summary>Shuts the root down (used when a host stops playing this instance).</summary>
    internal void ShutdownAsChild() => _root.Shutdown(_context);

    /// <summary>
    /// Resets every node to its initial state (state machines back to their default state, clips to
    /// the start). Control parameter values are kept.
    /// </summary>
    public void ResetGraphState()
    {
        if (_root.IsInitialized)
            _root.Shutdown(_context);
        _previousPose.SetToReferencePose();
        _previousPose.CalculateModelSpaceTransforms();
    }

    private void BeginTick(float deltaTime, Transform3D worldTransform)
    {
        float dt = float.IsFinite(deltaTime) ? deltaTime : 0f;
        _updateId++;
        _parametersChanged = false;
        _context.DeltaTime = dt;
        _context.FrameTime = MathF.Abs(dt);
        _context.Time += _context.FrameTime;
        _context.UpdateId = _updateId;
        _context.WorldTransform = worldTransform;
        _context.WorldTransformInverse = TransformOps.Inverse(worldTransform);
    }

    private void FinishTick()
    {
        _previousPose.CopyFrom(_root.Pose);
        _previousPose.CalculateModelSpaceTransforms();
    }

    /// <summary>The graph these instances were built from.</summary>
    public AnimationGraph Graph => _graph;

    /// <summary>Access to a node instance by index (for advanced/editor inspection).</summary>
    public GraphNodeInstance GetNodeInstance(int index) => _nodes[index];

    /// <summary>The node at an index, or null when the index belongs to some other graph.</summary>
    public GraphNodeInstance? TryGetNodeInstance(int index)
        => (uint)index < (uint)_nodes.Length ? _nodes[index] : null;

    /// <summary>
    /// Reads a value node only if it already produced a value this update. A debug view has to leave
    /// the graph exactly as it found it, and <see cref="EvaluateValueNode"/> will tick a node that has
    /// not run, which advances edge detectors and cached values as a real tick would. An event reader
    /// that keeps no state gives its answer as the frame ended rather than as it first read.
    /// </summary>
    public bool TryReadValueNode(int index, out ParameterValue value)
    {
        value = default;
        if ((uint)index >= (uint)_nodes.Length) return false;
        if (_nodes[index] is not ValueNodeInstance node) return false;
        if (_parametersChanged || !node.WasUpdated(_context)) return false;

        value = node.Inspect(_context);
        return true;
    }

    /// <summary>Finds a node instance by its authored name (or null if unnamed/absent).</summary>
    public GraphNodeInstance? GetNodeInstance(string name)
    {
        int index = _graph.GetNodeIndex(name);
        return index >= 0 ? _nodes[index] : null;
    }

    /// <summary>
    /// Reads a value node for scripting or editor inspection. Without parameter changes since the last
    /// update it returns the value from that update. After a parameter change it evaluates a zero
    /// time tick, which advances stateful nodes (edge detectors, cached values) as a real tick would.
    /// </summary>
    public ParameterValue EvaluateValueNode(int index)
    {
        if (_nodes[index] is not ValueNodeInstance value)
            throw new ArgumentException($"Node {index} is not a value node.", nameof(index));

        // A node that already ran this frame answers as the frame ended, and looking never steps its state.
        if (!_parametersChanged && value.WasUpdated(_context))
            return value.Inspect(_context);

        BeginInspectionTick();
        return value.GetValue(_context);
    }

    /// <summary>Evaluates a bone-mask node and returns its mask (for scripting/editor inspection).</summary>
    public BoneMask EvaluateBoneMaskNode(int index)
    {
        if (_nodes[index] is not BoneMaskNodeInstance mask)
            throw new ArgumentException($"Node {index} is not a bone-mask node.", nameof(index));

        if (_parametersChanged)
            BeginInspectionTick();
        return mask.GetMask(_context);
    }

    private void BeginInspectionTick() => BeginTick(0f, _context.WorldTransform);

    /// <summary>
    /// Plugs a runtime graph into a named external slot (or clears it with null). The supplied graph
    /// must be bound to the same skeleton and must not already be plugged into another slot.
    /// </summary>
    public void SetExternalGraph(string slotName, AnimationGraphInstance? graph)
    {
        if (GetNodeInstance(slotName) is not ExternalGraphSlotInstance slot)
            throw new ArgumentException($"No external graph slot named '{slotName}'.", nameof(slotName));
        if (graph is not null && !ReferenceEquals(graph._skeleton, _skeleton))
            throw new ArgumentException("An external graph must be bound to the same skeleton as its host.", nameof(graph));
        if (graph is not null && graph.HostSlot is not null && !ReferenceEquals(graph.HostSlot, slot))
            throw new ArgumentException("That graph instance is already plugged into another external slot.", nameof(graph));
        if (ReferenceEquals(graph, this))
            throw new ArgumentException("A graph cannot host itself.", nameof(graph));
        slot.SetExternal(graph);
    }

    private static Dictionary<int, string> NodeNames(AnimationGraph graph)
    {
        var names = new Dictionary<int, string>();
        for (int i = 0; i < graph.NodeCount; i++)
            if (!string.IsNullOrEmpty(graph.Nodes[i].Name))
                names[i] = graph.Nodes[i].Name!;
        return names;
    }

    // Rejects pose nodes with several parents and any dependency cycle.
    private static void ValidateEdges(AnimationGraph graph, IReadOnlyList<(int From, int To, bool IsPose)> edges)
    {
        int count = graph.NodeCount;
        var poseParent = new int[count];
        Array.Fill(poseParent, -1);
        var adjacency = new List<int>[count];

        foreach ((int from, int to, bool isPose) in edges)
        {
            (adjacency[from] ??= new List<int>()).Add(to);
            if (!isPose)
                continue;
            if (poseParent[to] == from)
                throw new GraphValidationException(to, graph.Nodes[to].Name,
                    $"is listed twice by node {from}. A pose node keeps playback state and would be played twice a frame, so give each entry its own node.");
            if (poseParent[to] >= 0)
                throw new GraphValidationException(to, graph.Nodes[to].Name,
                    $"is used by more than one parent (nodes {poseParent[to]} and {from}). A pose node keeps playback state, so give each parent its own node.");
            poseParent[to] = from;
        }

        var state = new byte[count]; // 0 unvisited, 1 visiting, 2 done
        var stack = new Stack<(int Node, int Next)>();
        for (int start = 0; start < count; start++)
        {
            if (state[start] != 0)
                continue;
            stack.Push((start, 0));
            state[start] = 1;
            while (stack.Count > 0)
            {
                (int node, int next) = stack.Pop();
                List<int>? children = adjacency[node];
                if (children is null || next >= children.Count)
                {
                    state[node] = 2;
                    continue;
                }

                stack.Push((node, next + 1));
                int child = children[next];
                if (state[child] == 1)
                    throw new GraphValidationException(child, graph.Nodes[child].Name, $"is part of a dependency cycle through node {node}.");
                if (state[child] == 0)
                {
                    state[child] = 1;
                    stack.Push((child, 0));
                }
            }
        }
    }
}
