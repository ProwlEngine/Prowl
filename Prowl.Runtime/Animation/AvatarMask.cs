// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Motion;

using MotionSkeleton = Prowl.Motion.Skeleton;

namespace Prowl.Runtime;

/// <summary>
/// Which bones, body parts and float channels a layer is allowed to drive, by name so one mask serves
/// every rig that shares those names.
/// </summary>
[CreateAssetMenu("Avatar Mask", Extension = ".mask", Order = 1200)]
public sealed class AvatarMask : Asset
{
    /// <summary>One bone and how much of the layer reaches it.</summary>
    [Serializable]
    public class BoneWeight
    {
        public string Bone = string.Empty;
        public float Weight = 1f;
        /// <summary>Apply the weight to everything below this bone too.</summary>
        public bool IncludeChildren = true;
    }

    /// <summary>The weight every bone starts at, before the entries below are applied.</summary>
    public float DefaultWeight;

    public List<BoneWeight> Bones = new();

    /// <summary>Per body part weights, used when the layer blends in muscle space.</summary>
    public List<HumanBodyPart> HumanBodyParts = new();

    /// <summary>One float channel, such as a blend shape, and how much of the layer reaches it.</summary>
    [Serializable]
    public class ChannelWeight
    {
        public string Channel = string.Empty;
        public float Weight = 1f;
    }

    /// <summary>The weight every float channel starts at, before the entries below are applied.</summary>
    public float DefaultChannelWeight = 1f;

    public List<ChannelWeight> Channels = new();

    [NonSerialized] private MotionSkeleton? _cachedFor;
    [NonSerialized] private BoneMask? _cached;
    [NonSerialized] private HumanPoseMask? _cachedHuman;

    public AvatarMask() : base("Avatar Mask") { }

    /// <summary>The mask for one skeleton, built on first use. Names the skeleton does not have are skipped.</summary>
    public BoneMask GetBoneMask(MotionSkeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        EnsureLoaded();
        if (_cached != null && ReferenceEquals(_cachedFor, skeleton))
            return _cached;

        var mask = new BoneMask(skeleton, DefaultWeight);
        foreach (BoneWeight entry in Bones)
        {
            int bone = skeleton.GetBoneIndex(new StringID(entry.Bone));
            if (bone == MotionSkeleton.InvalidIndex) continue;

            mask.SetWeight(bone, entry.Weight);
            if (entry.IncludeChildren) WeighDescendants(skeleton, mask, bone, entry.Weight);
        }

        mask.ResetChannelWeights(DefaultChannelWeight);
        foreach (ChannelWeight entry in Channels)
        {
            int channel = skeleton.GetFloatChannelIndex(new StringID(entry.Channel));
            if (channel != MotionSkeleton.InvalidIndex) mask.SetChannelWeight(channel, entry.Weight);
        }

        _cachedFor = skeleton;
        _cached = mask;
        return mask;
    }

    /// <summary>The muscle space mask this describes, for a layer that blends through the human body.</summary>
    public HumanPoseMask GetHumanMask()
    {
        EnsureLoaded();
        if (_cachedHuman != null)
            return _cachedHuman;

        HumanPoseMask mask;
        if (HumanBodyParts.Count == 0)
            mask = HumanPoseMask.Full();
        else
        {
            mask = HumanPoseMask.ForBodyPart(HumanBodyParts[0]);
            for (int i = 1; i < HumanBodyParts.Count; i++)
                Combine(mask, HumanPoseMask.ForBodyPart(HumanBodyParts[i]));
        }

        mask.ChannelWeight = DefaultChannelWeight;
        foreach (ChannelWeight entry in Channels)
            if (entry.Channel.Length > 0) mask.SetChannelWeight(new StringID(entry.Channel), entry.Weight);
        return _cachedHuman = mask;
    }

    /// <summary>Drops the built masks so the next use rebuilds them.</summary>
    public void Invalidate() { EnsureLoaded(); _cached = null; _cachedFor = null; _cachedHuman = null; }

    private static void WeighDescendants(MotionSkeleton skeleton, BoneMask mask, int bone, float weight)
    {
        for (int b = 0; b < skeleton.BoneCount; b++)
        {
            int parent = skeleton.GetParentBoneIndex(b);
            while (parent != MotionSkeleton.InvalidIndex)
            {
                if (parent == bone) { mask.SetWeight(b, weight); break; }
                parent = skeleton.GetParentBoneIndex(parent);
            }
        }
    }

    private static void Combine(HumanPoseMask target, HumanPoseMask other)
    {
        for (int b = 0; b < HumanTrait.BoneCount; b++)
        {
            var bone = (HumanBodyBone)b;
            target.SetBoneWeight(bone, MathF.Max(target.GetBoneWeight(bone), other.GetBoneWeight(bone)));
        }
        for (int g = 0; g < HumanPose.GoalCount; g++)
        {
            var goal = (HumanGoal)g;
            target.SetGoalWeight(goal, MathF.Max(target.GetGoalWeight(goal), other.GetGoalWeight(goal)));
        }
        target.RootWeight = MathF.Max(target.RootWeight, other.RootWeight);
    }
}
