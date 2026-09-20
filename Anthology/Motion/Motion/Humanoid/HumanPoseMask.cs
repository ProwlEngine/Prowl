using Prowl.Vector;

namespace Prowl.Motion;

/// <summary>Coarse body regions for building a <see cref="HumanPoseMask"/>.</summary>
public enum HumanBodyPart : byte
{
    UpperBody,
    LowerBody,
    Arms,
    Legs,
    Head
}

/// <summary>
/// A humanoid body part mask in muscle space: a [0,1] weight per humanoid bone (applied to that bone's
/// muscles), per IK goal, and for the root. The head weight also masks the look at. Used to restrict
/// muscle space blends/layers to part of the body.
/// </summary>
public sealed class HumanPoseMask
{
    private readonly float[] _bones = new float[HumanTrait.BoneCount];
    private readonly float[] _goals = new float[HumanPose.GoalCount];

    public float RootWeight { get; set; }

    public float GetBoneWeight(HumanBodyBone bone) => _bones[(int)bone];
    public void SetBoneWeight(HumanBodyBone bone, float weight) => _bones[(int)bone] = Maths.Clamp(weight, 0f, 1f);

    public float GetGoalWeight(HumanGoal goal) => _goals[(int)goal];
    public void SetGoalWeight(HumanGoal goal, float weight) => _goals[(int)goal] = Maths.Clamp(weight, 0f, 1f);

    public void SetAll(float weight)
    {
        float w = Maths.Clamp(weight, 0f, 1f);
        Array.Fill(_bones, w);
        Array.Fill(_goals, w);
        RootWeight = w;
    }

    /// <summary>A mask that affects everything (weight 1).</summary>
    public static HumanPoseMask Full()
    {
        var mask = new HumanPoseMask();
        mask.SetAll(1f);
        return mask;
    }

    /// <summary>A mask covering the given body part (plus its fingers and the matching goals).</summary>
    public static HumanPoseMask ForBodyPart(HumanBodyPart part)
    {
        var mask = new HumanPoseMask();
        foreach (HumanBodyBone bone in HumanTrait.AllBones)
            if (Belongs(bone, part))
                mask.SetBoneWeight(bone, 1f);

        switch (part)
        {
            case HumanBodyPart.LowerBody or HumanBodyPart.Legs:
                mask.SetGoalWeight(HumanGoal.LeftFoot, 1f);
                mask.SetGoalWeight(HumanGoal.RightFoot, 1f);
                mask.RootWeight = part == HumanBodyPart.LowerBody ? 1f : 0f;
                break;
            case HumanBodyPart.UpperBody or HumanBodyPart.Arms:
                mask.SetGoalWeight(HumanGoal.LeftHand, 1f);
                mask.SetGoalWeight(HumanGoal.RightHand, 1f);
                break;
        }
        return mask;
    }

    internal float GetBoneWeightByIndex(int boneIndex) => _bones[boneIndex];

    private static bool Belongs(HumanBodyBone bone, HumanBodyPart part)
    {
        bool isLeg = IsLeg(bone);
        bool isArm = IsArm(bone);
        bool isHead = IsHead(bone);
        bool isHips = bone == HumanBodyBone.Hips;
        return part switch
        {
            HumanBodyPart.Legs => isLeg,
            HumanBodyPart.Arms => isArm,
            HumanBodyPart.Head => isHead,
            HumanBodyPart.LowerBody => isLeg || isHips,
            HumanBodyPart.UpperBody => !isLeg && !isHips,
            _ => false
        };
    }

    private static bool IsLeg(HumanBodyBone bone) => bone is
        HumanBodyBone.LeftUpperLeg or HumanBodyBone.RightUpperLeg or
        HumanBodyBone.LeftLowerLeg or HumanBodyBone.RightLowerLeg or
        HumanBodyBone.LeftFoot or HumanBodyBone.RightFoot or
        HumanBodyBone.LeftToes or HumanBodyBone.RightToes;

    private static bool IsArm(HumanBodyBone bone)
    {
        string n = bone.ToString();
        if (n.Contains("Shoulder") || n.Contains("UpperArm") || n.Contains("LowerArm") || n.Contains("Hand"))
            return true;
        // Fingers (Thumb/Index/Middle/Ring/Little phalanges).
        return n.Contains("Thumb") || n.Contains("Index") || n.Contains("Middle") || n.Contains("Ring") || n.Contains("Little");
    }

    private static bool IsHead(HumanBodyBone bone) => bone is
        HumanBodyBone.Neck or HumanBodyBone.Head or HumanBodyBone.LeftEye or HumanBodyBone.RightEye or HumanBodyBone.Jaw;
}
