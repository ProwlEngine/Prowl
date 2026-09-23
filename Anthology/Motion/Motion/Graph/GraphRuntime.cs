using System.Collections.Generic;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>Whether the nodes being updated feed the final result (active) or the losing side of a transition.</summary>
public enum BranchState : byte
{
    Active,
    Inactive,
}

/// <summary>The value types a node input accepts. Bool, int and float read as one another, so they share one kind.</summary>
[Flags]
public enum ValueInputKind : byte
{
    Any = 0,
    Number = 1,
    Vector = 2,
    Target = 4,
    Id = 8,
}

/// <summary>Per-update state passed to every node while evaluating a graph instance.</summary>
public sealed class GraphContext
{
    public float DeltaTime;
    public Skeleton Skeleton = null!;
    public Avatar? Avatar;
    public ParameterValue[] Parameters = System.Array.Empty<ParameterValue>();
    public SampledEventsBuffer Events = new();
    public uint UpdateId;

    /// <summary>Seconds of graph time elapsed since the instance started (unscaled by speed nodes).</summary>
    public double Time;

    /// <summary>How far <see cref="Time"/> moved this update: the real frame time, whatever a speed node has done to <see cref="DeltaTime"/>.</summary>
    public float FrameTime;

    /// <summary>
    /// The previous frame's output pose (model-space transforms valid), used to resolve bone targets
    /// in poseless value nodes. One frame of latency.
    /// </summary>
    public Pose? PreviousPose;

    /// <summary>
    /// How the graph asks the world where the ground is, when it has one. The host sets it, and a node
    /// that wants the ground under a foot uses it rather than being fed heights from outside.
    /// </summary>
    public IGroundProbe? Ground;

    /// <summary>The character's world transform. World space targets are converted to character space with its inverse.</summary>
    public Transform3D WorldTransform = Transform3D.Identity;

    /// <summary>The inverse of <see cref="WorldTransform"/> (exact for uniform scale, see <see cref="WorldToCharacter(Float3)"/>).</summary>
    public Transform3D WorldTransformInverse = Transform3D.Identity;

    /// <summary>A world space point in character space, exact for any scale.</summary>
    public Float3 WorldToCharacter(Float3 point) => TransformOps.InverseTransformPoint(WorldTransform, point);

    /// <summary>A world space transform in character space: the position exact for any scale, the rotation relative to the character.</summary>
    public Transform3D WorldToCharacter(Transform3D transform)
        => new(WorldToCharacter(transform.position), Quaternion.Inverse(WorldTransform.rotation) * transform.rotation, transform.scale);

    /// <summary>A world space surface normal in character space.</summary>
    public Float3 WorldNormalToCharacter(Float3 normal)
    {
        Float3 local = Quaternion.Inverse(WorldTransform.rotation) * normal;
        Float3 scale = WorldTransform.scale;
        return TransformOps.SafeNormalize(new Float3(local.X * scale.X, local.Y * scale.Y, local.Z * scale.Z));
    }

    /// <summary>Inactive while updating the source side of a transition. Nodes tag their sampled events with it.</summary>
    public BranchState BranchState = BranchState.Active;

    /// <summary>
    /// When set, time based nodes play over this sync range (a parent is driving them phase locked)
    /// instead of advancing their own clock by <see cref="DeltaTime"/>.
    /// </summary>
    public SyncTrackTimeRange? SyncRange;

    /// <summary>True while a parent drives the update with a <see cref="SyncRange"/>.</summary>
    public bool Synchronized => SyncRange.HasValue;

    public bool IsActiveBranch => BranchState == BranchState.Active;
}

/// <summary>
/// Wiring context passed once at instance creation to resolve child references. Every reference is
/// recorded so the instance can reject cycles and pose nodes shared by several parents.
/// </summary>
public sealed class GraphBindContext
{
    private readonly List<(int From, int To, bool IsPose)> _edges = new();

    public GraphBindContext(Skeleton skeleton, GraphNodeInstance[] nodes, Avatar? avatar = null)
    {
        Skeleton = skeleton;
        Nodes = nodes;
        Avatar = avatar;
    }

    public Skeleton Skeleton { get; }
    public Avatar? Avatar { get; }
    public GraphNodeInstance[] Nodes { get; }

    internal int CurrentIndex { get; set; } = -1;
    internal IReadOnlyList<(int From, int To, bool IsPose)> Edges => _edges;
    internal Dictionary<int, string>? NodeNames { get; set; }
    internal IReadOnlyList<GraphNodeDefinition> Definitions { get; set; } = System.Array.Empty<GraphNodeDefinition>();

    /// <summary>Resolves a child pose node. The caller owns its lifecycle (initialize, update, shutdown).</summary>
    public PoseNodeInstance PoseNode(int index) => Resolve<PoseNodeInstance>(index, "a pose node", isPose: true);

    /// <summary>
    /// Resolves a value node input. By default it is initialized and shut down together with the node
    /// being bound, pass <paramref name="dependency"/> false to manage it manually.
    /// </summary>
    public ValueNodeInstance ValueNode(int index, ValueInputKind expected = ValueInputKind.Any, bool dependency = true)
    {
        ValueNodeInstance node = Resolve<ValueNodeInstance>(index, "a value node", isPose: false);
        if (expected != ValueInputKind.Any && index < Definitions.Count && Definitions[index] is ValueNodeDefinition definition && (KindOf(definition.ValueType) & expected) == 0)
            throw Error($"expects a {expected} input at node {index}, but that node produces {definition.ValueType}.");
        if (dependency)
            Current.AddDependency(node);
        return node;
    }

    /// <summary>Resolves an optional value node input (-1 means none).</summary>
    public ValueNodeInstance? OptionalValueNode(int index, ValueInputKind expected = ValueInputKind.Any, bool dependency = true)
        => index < 0 ? null : ValueNode(index, expected, dependency);

    private static ValueInputKind KindOf(AnimationValueType type) => type switch
    {
        AnimationValueType.Vector => ValueInputKind.Vector,
        AnimationValueType.Target => ValueInputKind.Target,
        AnimationValueType.Id => ValueInputKind.Id,
        _ => ValueInputKind.Number,
    };

    /// <summary>
    /// Resolves a pose node to read without driving it: no ownership, no lifecycle and no edge, so the
    /// node stays free to be played by its real parent. What is read is whatever that node last
    /// produced, which is the previous frame's pose when this node is evaluated first.
    /// </summary>
    public PoseNodeInstance ObservePoseNode(int index)
    {
        if (index < 0 || index >= Nodes.Length)
            throw Error($"references node {index}, which does not exist.");
        if (Nodes[index] is not PoseNodeInstance node)
            throw Error($"expects a pose node at index {index}, but found {Nodes[index].GetType().Name}.");
        if (index == CurrentIndex)
            throw Error("references itself.");
        return node;
    }

    /// <summary>Resolves a bone mask node input, initialized together with the node being bound.</summary>
    public BoneMaskNodeInstance BoneMaskNode(int index)
    {
        BoneMaskNodeInstance node = Resolve<BoneMaskNodeInstance>(index, "a bone mask node", isPose: false);
        Current.AddDependency(node);
        return node;
    }

    /// <summary>Throws a validation error for the node being bound.</summary>
    public GraphValidationException Error(string message) => new(CurrentIndex, NameOf(CurrentIndex), message);

    private GraphNodeInstance Current => Nodes[CurrentIndex];

    private T Resolve<T>(int index, string expected, bool isPose) where T : GraphNodeInstance
    {
        if (index < 0 || index >= Nodes.Length)
            throw Error($"references node {index}, which does not exist.");
        if (Nodes[index] is not T node)
            throw Error($"expects {expected} at index {index}, but found {Nodes[index].GetType().Name}.");
        if (index == CurrentIndex)
            throw Error("references itself.");
        _edges.Add((CurrentIndex, index, isPose));
        return node;
    }

    private string? NameOf(int index) => NodeNames is not null && NodeNames.TryGetValue(index, out string? name) ? name : null;
}
/// <summary>
/// How much of each input a blend is using right now, for an editor or a debug view. Reads only; the
/// weights are the ones the blend worked out in its last update.
/// </summary>
public interface IBlendWeights
{
    /// <summary>The weight given to the input fed by the node at this index, or 0 when it is not in the blend.</summary>
    float WeightOf(int childNodeIndex);
}


/// <summary>
/// Base for all runtime node instances. Nodes are initialized when they become part of the active
/// graph and shut down when they leave it, initialization is reference counted so a value node read
/// by several parents is set up once.
/// </summary>
public abstract class GraphNodeInstance
{
    private readonly List<GraphNodeInstance> _dependencies = new();
    private int _initializationCount;
    private uint _lastUpdateId = uint.MaxValue;

    /// <summary>Index of this node in its graph.</summary>
    public int NodeIndex { get; internal set; } = -1;

    public bool IsInitialized => _initializationCount > 0;

    /// <summary>True if the node already ran during the update identified by the context.</summary>
    public bool WasUpdated(GraphContext context) => _lastUpdateId == context.UpdateId;

    /// <summary>Resolves child references. Called once when the instance is created.</summary>
    public virtual void Bind(GraphBindContext context) { }

    /// <summary>Brings the node (and its value inputs) into the active graph.</summary>
    public void Initialize(GraphContext context)
    {
        if (_initializationCount++ > 0)
            return;
        for (int i = 0; i < _dependencies.Count; i++)
            _dependencies[i].Initialize(context);
        OnInitialize(context);
    }

    /// <summary>Removes the node from the active graph once every parent that initialized it has shut it down.</summary>
    public void Shutdown(GraphContext context)
    {
        if (_initializationCount == 0 || --_initializationCount > 0)
            return;
        OnShutdown(context);
        for (int i = 0; i < _dependencies.Count; i++)
            _dependencies[i].Shutdown(context);
        _lastUpdateId = uint.MaxValue;
    }

    protected virtual void OnInitialize(GraphContext context) { }

    protected virtual void OnShutdown(GraphContext context) { }

    protected void MarkNodeActive(GraphContext context) => _lastUpdateId = context.UpdateId;

    /// <summary>The value and mask nodes this node reads, as bound.</summary>
    internal IReadOnlyList<GraphNodeInstance> Dependencies => _dependencies;

    internal void AddDependency(GraphNodeInstance node)
    {
        if (!_dependencies.Contains(node))
            _dependencies.Add(node);
    }
}

/// <summary>
/// A runtime pose node. After <see cref="Update"/>, <see cref="Pose"/> holds the produced pose,
/// <see cref="RootMotionDelta"/> the root motion for the step and <see cref="SampledEventRange"/>
/// the events its subtree sampled. Each instance owns its pose buffer (direct evaluation, no task system).
/// </summary>
public abstract class PoseNodeInstance : GraphNodeInstance
{
    private SyncTrackTime? _initialTime;

    public Pose Pose { get; protected set; } = null!;
    public Transform3D RootMotionDelta { get; protected set; } = Transform3D.Identity;

    /// <summary>Normalized time in [0,1] reached this update (clips, blends).</summary>
    public float NormalizedTime { get; protected set; }

    /// <summary>Normalized time at the start of this update.</summary>
    public float PreviousTime { get; protected set; }

    /// <summary>True when the last update played backward.</summary>
    public bool PlayingBackward { get; protected set; }

    /// <summary>Number of times the node's content looped since it was initialized.</summary>
    public int LoopCount { get; protected set; }

    /// <summary>Duration in seconds of the currently driving content (0 if not time based).</summary>
    public float Duration { get; protected set; }

    /// <summary>The events sampled by this node's subtree during its last update.</summary>
    public SampledEventRange SampledEventRange { get; private set; }

    /// <summary>The node's sync track, used to phase-lock synchronized blends (default: single event).</summary>
    public virtual SyncTrack SyncTrack => Prowl.Motion.SyncTrack.Default;

    /// <summary>
    /// Initializes the node to start playing at <paramref name="initialTime"/> on its sync track, or
    /// at the very start when null.
    /// </summary>
    public void Initialize(GraphContext context, SyncTrackTime? initialTime)
    {
        _initialTime = initialTime;
        Initialize(context);
    }

    /// <summary>
    /// Produces this update's pose, root motion and events. A node not yet initialized is initialized
    /// first. A second update in the same frame is ignored and keeps the first result.
    /// </summary>
    public void Update(GraphContext context)
    {
        if (!IsInitialized)
            Initialize(context, null);
        if (WasUpdated(context))
            return;

        MarkNodeActive(context);
        int firstEvent = context.Events.Count;
        OnUpdate(context);
        SampledEventRange = context.Events.RangeFrom(firstEvent);
    }

    protected abstract void OnUpdate(GraphContext context);

    protected sealed override void OnInitialize(GraphContext context)
    {
        PreviousTime = NormalizedTime = 0f;
        LoopCount = 0;
        PlayingBackward = false;
        RootMotionDelta = Transform3D.Identity;
        SampledEventRange = default;
        OnInitialize(context, _initialTime);
    }

    /// <summary>Resets playback state and initializes the children needed to start at <paramref name="initialTime"/>.</summary>
    protected virtual void OnInitialize(GraphContext context, SyncTrackTime? initialTime) { }

    /// <summary>Copies duration and time from another node (typically the child being forwarded).</summary>
    protected void CopyTimingFrom(PoseNodeInstance other)
    {
        Duration = other.Duration;
        PreviousTime = other.PreviousTime;
        NormalizedTime = other.NormalizedTime;
        LoopCount = other.LoopCount;
        PlayingBackward = other.PlayingBackward;
    }

    /// <summary>Copies pose, root motion and timing from another node.</summary>
    protected void CopyResultFrom(PoseNodeInstance other)
    {
        Pose.CopyFrom(other.Pose);
        RootMotionDelta = other.RootMotionDelta;
        CopyTimingFrom(other);
    }
}

/// <summary>
/// A pose node wrapping a single child: forwards initialization, shutdown, timing and the sync track,
/// so modifiers (speed scale, mirror, IK, root motion override) stay transparent to synchronized blends.
/// </summary>
public abstract class PassthroughPoseNodeInstance : PoseNodeInstance
{
    protected PoseNodeInstance Child { get; private set; } = null!;

    public override SyncTrack SyncTrack => Child.SyncTrack;

    /// <summary>Resolves the child and allocates the pose buffer. Call from <see cref="GraphNodeInstance.Bind"/>.</summary>
    protected void BindChild(GraphBindContext context, int childIndex)
    {
        Pose = new Pose(context.Skeleton);
        Child = context.PoseNode(childIndex);
    }

    protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
    {
        Child.Initialize(context, initialTime);
        CopyTimingFrom(Child);
    }

    protected override void OnShutdown(GraphContext context) => Child.Shutdown(context);

    protected override void OnUpdate(GraphContext context)
    {
        Child.Update(context);
        CopyResultFrom(Child);
    }
}

/// <summary>
/// A runtime value node. The result is cached per update id so re-reads are free, except that a node
/// whose answer rests on the sampled events is read again once the events have changed: read before the
/// clip that fires an event has played, or before a blend weighs it, it would otherwise hold a stale answer all frame.
/// </summary>
public abstract class ValueNodeInstance : GraphNodeInstance
{
    private uint _lastUpdate = uint.MaxValue;
    private ParameterValue _cached;
    private int _eventsSeen;
    private bool? _readsEvents;

    public ParameterValue GetValue(GraphContext context)
    {
        if (_lastUpdate != context.UpdateId || (ReadsEventsHere() && context.Events.Version != _eventsSeen))
        {
            _lastUpdate = context.UpdateId;
            _eventsSeen = context.Events.Version;
            MarkNodeActive(context);
            _cached = Compute(context);
        }
        return _cached;
    }

    /// <summary>
    /// The node's answer as the frame ended, for inspection. A node reading events may have answered
    /// before the events it looks for arrived, so it works its answer out again, but only when nothing
    /// that would run again keeps state, since stepping state outside a tick would change the graph.
    /// Otherwise it gives its last answer as it stands.
    /// </summary>
    internal ParameterValue Inspect(GraphContext context)
        => ReadsEventsHere() && SafeToRunAgain() ? GetValue(context) : _cached;

    /// <summary>Whether working the answer out again leaves no state behind. Nodes with none say so.</summary>
    protected virtual bool KeepsNoState => false;

    private bool? _safeToRunAgain;

    // Only what reads events runs again, so only those inputs need to be free of state.
    private bool SafeToRunAgain()
    {
        if (_safeToRunAgain is { } known) return known;

        bool safe = KeepsNoState;
        foreach (GraphNodeInstance input in Dependencies)
            if (input is ValueNodeInstance value && value.ReadsEventsHere())
                safe &= value.SafeToRunAgain();
        _safeToRunAgain = safe;
        return safe;
    }

    protected abstract ParameterValue Compute(GraphContext context);

    /// <summary>Whether the node reads the sampled events buffer itself.</summary>
    protected virtual bool ReadsEvents => false;

    /// <summary>Whether the node's answer rests on the sampled events, itself or through anything it reads.</summary>
    private bool ReadsEventsHere()
    {
        if (_readsEvents is { } known) return known;

        bool reads = ReadsEvents;
        foreach (GraphNodeInstance input in Dependencies)
            reads |= input is ValueNodeInstance value && value.ReadsEventsHere();
        _readsEvents = reads;
        return reads;
    }

    protected override void OnShutdown(GraphContext context) => _lastUpdate = uint.MaxValue;
}

/// <summary>A runtime node that produces a <see cref="BoneMask"/>, recomputed once per update id.</summary>
public abstract class BoneMaskNodeInstance : GraphNodeInstance
{
    private uint _lastUpdate = uint.MaxValue;
    private BoneMask _cached = null!;

    public BoneMask GetMask(GraphContext context)
    {
        if (_lastUpdate != context.UpdateId || _cached is null)
        {
            _lastUpdate = context.UpdateId;
            _cached = Compute(context);
        }
        return _cached;
    }

    protected abstract BoneMask Compute(GraphContext context);
}
