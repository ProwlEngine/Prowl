using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>How an <see cref="ExternalPoseDefinition"/> brings a pose onto the graph's own skeleton.</summary>
public enum PoseTransferMode : byte
{
    /// <summary>Copy when the skeletons match, retarget when both avatars are humanoid, otherwise match by name.</summary>
    Auto,
    /// <summary>Straight copy, for a pose already on this skeleton.</summary>
    Copy,
    /// <summary>Match bones by name.</summary>
    ByName,
    /// <summary>Go through muscle space, so proportions and bind poses may differ.</summary>
    Humanoid,
}

// ---- Pose from outside the graph ---------------------------------------------------------------

/// <summary>
/// A pose the host writes in each frame rather than one the graph plays: a ragdoll settling, a hit
/// reaction solved by physics, a pose arriving over the network, or a performer's tracked pose. The
/// pose may belong to another rig, in which case it is matched by bone name or retargeted through
/// muscle space on the way in.
/// </summary>
/// <remarks>
/// Get the instance with <see cref="AnimationGraphInstance.GetNodeInstance(int)"/>, write into its
/// <see cref="ExternalPoseInstance.Source"/> pose and set <see cref="ExternalPoseInstance.HasPose"/>.
/// Until then the node outputs the reference pose.
/// </remarks>
public sealed class ExternalPoseDefinition : PoseNodeDefinition
{
    public ExternalPoseDefinition(Skeleton sourceSkeleton)
    {
        ArgumentNullException.ThrowIfNull(sourceSkeleton);
        SourceSkeleton = sourceSkeleton;
    }

    public ExternalPoseDefinition(Avatar sourceAvatar)
    {
        ArgumentNullException.ThrowIfNull(sourceAvatar);
        SourceAvatar = sourceAvatar;
        SourceSkeleton = sourceAvatar.Skeleton;
    }

    public Skeleton SourceSkeleton { get; }

    /// <summary>The avatar the incoming pose belongs to, needed to retarget it.</summary>
    public Avatar? SourceAvatar { get; }

    public PoseTransferMode Mode { get; set; } = PoseTransferMode.Auto;

    public override GraphNodeInstance CreateInstance() => new ExternalPoseInstance(this);
}

/// <summary>The runtime side of an <see cref="ExternalPoseDefinition"/>, which the host writes into.</summary>
public sealed class ExternalPoseInstance : PoseNodeInstance
{
    private readonly ExternalPoseDefinition _def;
    private readonly HumanPose _human = new();
    private SkeletonMapping? _mapping;
    private Avatar? _targetAvatar;
    private PoseTransferMode _mode;

    internal ExternalPoseInstance(ExternalPoseDefinition def)
    {
        _def = def;
        Source = new Pose(def.SourceSkeleton);
        Source.SetToReferencePose();
    }

    /// <summary>The pose the host writes each frame, on the source skeleton.</summary>
    public Pose Source { get; }

    /// <summary>Set once the host has written a pose. While false the node outputs the reference pose.</summary>
    public bool HasPose { get; set; }

    /// <summary>How the source pose is being brought onto the graph's skeleton.</summary>
    public PoseTransferMode ResolvedMode => _mode;

    public override void Bind(GraphBindContext context)
    {
        Pose = new Pose(context.Skeleton);
        _targetAvatar = context.Avatar;
        _mode = Resolve(context.Skeleton, context.Avatar);

        // Muscle space carries no channels, so they are matched by name whichever way the bones travel.
        if (_mode is PoseTransferMode.ByName or PoseTransferMode.Humanoid)
            _mapping = SkeletonMapping.Create(_def.SourceSkeleton, context.Skeleton);

        if (_mode == PoseTransferMode.Copy && _def.SourceSkeleton.BoneCount != context.Skeleton.BoneCount)
            throw context.Error("is set to copy a pose from a skeleton with a different bone count.");

        if (_mode == PoseTransferMode.Humanoid && (_def.SourceAvatar is not { IsHuman: true } || context.Avatar is not { IsHuman: true }))
            throw context.Error("is set to retarget, which needs a humanoid avatar on both sides.");
    }

    protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
    {
        Duration = 0f;
        Pose.SetToReferencePose();
    }

    protected override void OnUpdate(GraphContext context)
    {
        RootMotionDelta = Transform3D.Identity;
        if (!HasPose)
        {
            Pose.SetToReferencePose();
            return;
        }

        switch (_mode)
        {
            case PoseTransferMode.Humanoid:
                Retargeter.RetargetFrom(_def.SourceAvatar!, Source, _human);
                Retargeter.RetargetTo(_targetAvatar!, _human, Pose);
                CopyFloatsByName();
                break;
            case PoseTransferMode.ByName:
                CopyByName();
                break;
            default:
                Pose.CopyFrom(Source);
                break;
        }
    }

    private void CopyByName()
    {
        SkeletonMapping mapping = _mapping!;
        for (int b = 0; b < Pose.BoneCount; b++)
        {
            int source = mapping.GetSourceBone(b);
            Pose.SetTransform(b, source == Skeleton.InvalidIndex ? Pose.Skeleton.GetBoneParentSpaceTransform(b) : Source.GetTransform(source));
        }

        CopyFloatsByName();
    }

    private void CopyFloatsByName()
    {
        SkeletonMapping mapping = _mapping!;
        for (int c = 0; c < Pose.FloatChannelCount; c++)
        {
            int source = mapping.GetSourceFloatChannel(c);
            Pose.SetFloat(c, source == Skeleton.InvalidIndex ? 0f : Source.GetFloat(source));
        }
    }

    private PoseTransferMode Resolve(Skeleton skeleton, Avatar? avatar)
    {
        if (_def.Mode != PoseTransferMode.Auto)
            return _def.Mode;
        if (ReferenceEquals(_def.SourceSkeleton, skeleton))
            return PoseTransferMode.Copy;
        if (_def.SourceAvatar is { IsHuman: true } && avatar is { IsHuman: true })
            return PoseTransferMode.Humanoid;
        return PoseTransferMode.ByName;
    }
}

// ---- Muscle space layering ---------------------------------------------------------------------

/// <summary>
/// Blends two poses in muscle space rather than bone by bone, optionally through a humanoid mask. Body
/// parts keep their meaning, so an upper body layer stays anatomically sensible even when the two poses
/// disagree strongly, where a bone blend would pass through poses the body cannot hold.
/// </summary>
public sealed class MuscleLayerDefinition : PoseNodeDefinition
{
    public MuscleLayerDefinition(int basePose, int layerPose, int weightNodeIndex = -1)
    {
        BasePose = basePose;
        LayerPose = layerPose;
        WeightNodeIndex = weightNodeIndex;
    }

    public int BasePose { get; }

    public int LayerPose { get; }

    /// <summary>Float value node giving the layer's weight, or -1 for <see cref="Weight"/>.</summary>
    public int WeightNodeIndex { get; }

    public float Weight { get; set; } = 1f;

    /// <summary>Which parts of the body the layer may drive. Null means all of it.</summary>
    public HumanPoseMask? Mask { get; set; }

    /// <summary>
    /// When true the layer is added on top of the base rather than blended with it. What gets added is
    /// how far the layer sits from <see cref="ReferenceNodeIndex"/>, or from the rig's reference pose.
    /// </summary>
    public bool Additive { get; set; }

    /// <summary>The pose an additive layer is measured against, or -1 for the rig's reference pose.</summary>
    public int ReferenceNodeIndex { get; set; } = -1;

    public override GraphNodeInstance CreateInstance() => new Instance(this);

    private sealed class Instance : PoseNodeInstance
    {
        private readonly MuscleLayerDefinition _def;
        private readonly HumanPose _basePose = new();
        private readonly HumanPose _layerPose = new();
        private readonly HumanPose _referencePose = new();
        private PoseNodeInstance _base = null!;
        private PoseNodeInstance _layer = null!;
        private PoseNodeInstance? _reference;
        private ValueNodeInstance? _weight;
        private Avatar _avatar = null!;

        public Instance(MuscleLayerDefinition def) => _def = def;

        public override SyncTrack SyncTrack => _base.SyncTrack;

        public override void Bind(GraphBindContext context)
        {
            if (context.Avatar is not { IsHuman: true } avatar)
                throw context.Error("blends in muscle space, which needs a humanoid avatar.");

            _avatar = avatar;
            Pose = new Pose(context.Skeleton);
            _base = context.PoseNode(_def.BasePose);
            _layer = context.PoseNode(_def.LayerPose);
            _weight = context.OptionalValueNode(_def.WeightNodeIndex, ValueInputKind.Number);

            // Both are resolved up front, so turning the layer additive later still measures against
            // something real.
            if (_def.ReferenceNodeIndex >= 0)
                _reference = context.PoseNode(_def.ReferenceNodeIndex);
            else
                EncodeReferencePose(context.Skeleton, avatar);
        }

        /// <summary>The rig's own reference pose in muscle space, which additive layers measure against.</summary>
        private void EncodeReferencePose(Skeleton skeleton, Avatar avatar)
        {
            var bind = new Pose(skeleton);
            bind.SetToReferencePose();
            Retargeter.RetargetFrom(avatar, bind, _referencePose);
        }

        protected override void OnInitialize(GraphContext context, SyncTrackTime? initialTime)
        {
            _base.Initialize(context, initialTime);
            _layer.Initialize(context, initialTime);
            _reference?.Initialize(context, initialTime);
            CopyTimingFrom(_base);
        }

        protected override void OnShutdown(GraphContext context)
        {
            _base.Shutdown(context);
            _layer.Shutdown(context);
            _reference?.Shutdown(context);
        }

        protected override void OnUpdate(GraphContext context)
        {
            _base.Update(context);
            _layer.Update(context);
            _reference?.Update(context);
            CopyTimingFrom(_base);
            RootMotionDelta = _base.RootMotionDelta;

            float weight = Math.Clamp(_weight?.GetValue(context).AsFloat() ?? _def.Weight, 0f, 1f);
            if (!(weight > 0f))
            {
                Pose.CopyFrom(_base.Pose);
                return;
            }

            Retargeter.RetargetFrom(_avatar, _base.Pose, _basePose);
            Retargeter.RetargetFrom(_avatar, _layer.Pose, _layerPose);

            if (_def.Additive)
            {
                if (_reference is not null)
                    Retargeter.RetargetFrom(_avatar, _reference.Pose, _referencePose);

                HumanPoseBlender.Subtract(_layerPose, _layerPose, _referencePose);
                HumanPoseBlender.AddLayer(_basePose, _layerPose, weight, _def.Mask);
            }
            else if (_def.Mask is null)
                HumanPoseBlender.Blend(_basePose, _basePose, _layerPose, weight);
            else
                HumanPoseBlender.Blend(_basePose, _basePose, _layerPose, weight, _def.Mask);

            Retargeter.RetargetTo(_avatar, _basePose, Pose);
            BlendFloats(weight);
        }

        /// <summary>
        /// Muscle space holds bones only, so the channels are carried across the same way the poses are:
        /// mixed toward the layer, or added on top of the base when the layer is additive.
        /// </summary>
        private void BlendFloats(float weight)
        {
            Pose basePose = _base.Pose, layerPose = _layer.Pose;
            // The rig's own reference pose holds no channel values, so an unreferenced layer adds itself whole.
            Pose? referencePose = _reference?.Pose;

            for (int c = 0; c < Pose.FloatChannelCount; c++)
            {
                float from = basePose.GetFloat(c);
                float to = layerPose.GetFloat(c);
                if (_def.Additive)
                    to -= referencePose?.GetFloat(c) ?? 0f;
                else
                    to -= from;
                Pose.SetFloat(c, from + to * weight);
            }
        }
    }
}
