// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Motion;
using Prowl.Vector;
using Prowl.Vector.Spatial;

using MotionAvatar = Prowl.Motion.Avatar;
using MotionGraph = Prowl.Motion.AnimationGraph;
using MotionSkeleton = Prowl.Motion.Skeleton;

namespace Prowl.Runtime;

/// <summary>Plays clips with cross fades, for an animator with no graph.</summary>
internal sealed class ProwlClipAnimator : SimpleAnimator
{
    private readonly Animator _owner;
    private readonly AnimatorBinding _binding;

    public ProwlClipAnimator(Animator owner, AnimatorBinding binding, MotionSkeleton skeleton, MotionAvatar? avatar)
        : base(skeleton, avatar)
    {
        _owner = owner;
        _binding = binding;
    }

    protected override void ApplyBoneTransform(int boneIndex, in Transform3D localTransform) => _binding.ApplyBone(boneIndex, localTransform);

    protected override void ApplyFloatChannel(int channelIndex, float value) => _binding.ApplyChannel(channelIndex, value);

    protected override Transform3D RootWorldTransform => _owner.RootWorld;

    protected override void ApplyRootMotion(in Transform3D delta) => _owner.ApplyRootMotionDelta(delta);
}

/// <summary>Plays an animation graph.</summary>
internal sealed class ProwlGraphAnimator : GraphAnimator, IGroundProbe
{
    private readonly Animator _owner;
    private readonly AnimatorBinding _binding;

    public ProwlGraphAnimator(Animator owner, AnimatorBinding binding, MotionGraph graph, MotionSkeleton skeleton, MotionAvatar? avatar)
        : base(graph, skeleton, avatar)
    {
        _owner = owner;
        _binding = binding;
        ProbeGroundWith(this);
        SetHost(owner);
    }

    bool IGroundProbe.Raycast(Float3 origin, Float3 direction, float maxDistance, out Float3 point, out Float3 normal)
        => _owner.RaycastGround(origin, direction, maxDistance, out point, out normal);

    protected override void ApplyBoneTransform(int boneIndex, in Transform3D localTransform) => _binding.ApplyBone(boneIndex, localTransform);

    protected override void ApplyFloatChannel(int channelIndex, float value) => _binding.ApplyChannel(channelIndex, value);

    protected override Transform3D RootWorldTransform => _owner.RootWorld;

    protected override void ApplyRootMotion(in Transform3D delta) => _owner.ApplyRootMotionDelta(delta);
}
