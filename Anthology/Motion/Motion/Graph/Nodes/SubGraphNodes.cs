using System.Collections.Generic;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

// ---------------------------------------------------------------------------------------------
// Referenced graph (compiled-in sub-graph)
// ---------------------------------------------------------------------------------------------

/// <summary>
/// Embeds another <see cref="AnimationGraph"/> as a child, forwarding parent value nodes into the
/// child's named control parameters. Output is the child's root pose. The child shares the parent's
/// event buffer and sync range. Lets a graph asset reference other graph assets (e.g. a shared
/// locomotion graph reused inside several character graphs).
/// </summary>
public sealed class ReferencedGraphDefinition : PoseNodeDefinition
{
    public ReferencedGraphDefinition(AnimationGraph graph) => Graph = graph;

    public AnimationGraph Graph { get; }

    /// <summary>Links from a parent value node to a child control parameter by name.</summary>
    public List<(int ParentValueNode, string ChildParameter)> ParameterLinks { get; } = new();

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PoseNodeInstance
    {
        private readonly ReferencedGraphDefinition _def;
        private AnimationGraphInstance _child = null!;
        private (ValueNodeInstance Node, int Parameter)[] _links = null!;

        public Instance(ReferencedGraphDefinition def) => _def = def;

        public override SyncTrack SyncTrack => _child.SyncTrack;

        public override void Bind(GraphBindContext context)
        {
            _child = _def.Graph.CreateInstance(context.Skeleton, context.Avatar);
            Pose = new Pose(context.Skeleton);
            _links = new (ValueNodeInstance, int)[_def.ParameterLinks.Count];
            for (int i = 0; i < _links.Length; i++)
            {
                (int parentNode, string childParameter) = _def.ParameterLinks[i];
                int parameter = _child.GetParameterIndex(childParameter);
                if (parameter < 0)
                    throw context.Error($"links to a sub graph parameter named '{childParameter}', which does not exist.");

                ValueNodeInstance node = context.ValueNode(parentNode);
                AnimationValueType parentType = ((ValueNodeDefinition)context.Definitions[parentNode]).ValueType;
                AnimationValueType childType = _child.GetParameterType(parameter);
                if (parentType != childType && !(childType == AnimationValueType.Float && parentType == AnimationValueType.Int))
                    throw context.Error($"links a {parentType} value to the sub graph parameter '{childParameter}', which is {childType}.");
                _links[i] = (node, parameter);
            }
        }

        protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
        {
            ReflectParameters(context);
            _child.InitializeAsChild(context, initialTime);
            CopyTimingFrom(_child.Root);
        }

        protected override void OnShutdown(GraphContext context) => _child.ShutdownAsChild();

        protected override void OnUpdate(GraphContext context)
        {
            ReflectParameters(context);
            _child.UpdateAsChild(context);
            CopyResultFrom(_child.Root);
        }

        private void ReflectParameters(GraphContext context)
        {
            for (int i = 0; i < _links.Length; i++)
                _child.SetParameterValue(_links[i].Parameter, _links[i].Node.GetValue(context));
        }
    }
}

// ---------------------------------------------------------------------------------------------
// External graph slot (runtime-swappable sub-graph)
// ---------------------------------------------------------------------------------------------

/// <summary>
/// A pose slot whose source graph is plugged in at runtime via
/// <see cref="AnimationGraphInstance.SetExternalGraph"/>. While empty it outputs a fallback content
/// node (or the reference pose). Used for gameplay-injected graphs (interactions, mounted poses).
/// </summary>
public sealed class ExternalGraphSlotDefinition : PoseNodeDefinition
{
    public ExternalGraphSlotDefinition(string slotName, int fallbackPoseNodeIndex = -1)
    { SlotName = slotName; FallbackPoseNodeIndex = fallbackPoseNodeIndex; }

    public string SlotName { get; }
    public int FallbackPoseNodeIndex { get; }

    public override GraphNodeInstance CreateInstance() => new ExternalGraphSlotInstance(this);
}

internal sealed class ExternalGraphSlotInstance : PoseNodeInstance
{
    private readonly ExternalGraphSlotDefinition _def;
    private PoseNodeInstance? _fallback;
    private AnimationGraphInstance? _external;
    private bool _externalStarted;

    public ExternalGraphSlotInstance(ExternalGraphSlotDefinition def) => _def = def;

    public string SlotName => _def.SlotName;
    public override SyncTrack SyncTrack => _external?.SyncTrack ?? _fallback?.SyncTrack ?? SyncTrack.Default;

    /// <summary>Plugs in (or clears, when null) the runtime graph driving this slot.</summary>
    public void SetExternal(AnimationGraphInstance? graph)
    {
        if (ReferenceEquals(graph, _external))
            return;

        StopExternal();
        if (_external is not null)
            _external.HostSlot = null;
        _external = graph;
        if (_external is not null)
            _external.HostSlot = this;
    }

    public override void Bind(GraphBindContext context)
    {
        Pose = new Pose(context.Skeleton);
        _fallback = _def.FallbackPoseNodeIndex >= 0 ? context.PoseNode(_def.FallbackPoseNodeIndex) : null;
    }

    protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
    {
        _fallback?.Initialize(context, initialTime);
        if (_fallback is not null)
            CopyTimingFrom(_fallback);
    }

    protected override void OnShutdown(GraphContext context)
    {
        StopExternal();
        _fallback?.Shutdown(context);
    }

    protected override void OnUpdate(GraphContext context)
    {
        if (_external is not null)
        {
            if (!_externalStarted)
            {
                _external.InitializeAsChild(context, null);
                _externalStarted = true;
            }
            _external.UpdateAsChild(context);
            CopyResultFrom(_external.Root);
            return;
        }

        if (_fallback is not null)
        {
            _fallback.Update(context);
            CopyResultFrom(_fallback);
            return;
        }

        Pose.SetToReferencePose();
        RootMotionDelta = Transform3D.Identity;
    }

    private void StopExternal()
    {
        if (_external is not null && _externalStarted)
            _external.ShutdownAsChild();
        _externalStarted = false;
    }
}
