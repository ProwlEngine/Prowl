using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// Muscle space operations on <see cref="HumanPose"/>s. Muscles blend linearly, the body and goal rotations by slerp, and every channel (muscles,
/// body, goals and look at) is treated the same way, so <c>AddLayer(b, Subtract(a, b), 1)</c> gives back
/// <c>a</c>. Masks weight muscles by their bone, goals by goal, the body by the root weight and the look at
/// by the head weight.
/// </summary>
public static class HumanPoseBlender
{
    /// <summary>result = a blended toward b by weight, on every channel.</summary>
    public static void Blend(HumanPose result, HumanPose a, HumanPose b, float weight)
    {
        Validate(result, a, b);
        BlendChannels(result, a, b, weight, null);
    }

    /// <summary>Masked blend: the effective weight of each channel is weight times its mask weight.</summary>
    public static void Blend(HumanPose result, HumanPose a, HumanPose b, float weight, HumanPoseMask mask)
    {
        Validate(result, a, b);
        ArgumentNullException.ThrowIfNull(mask);
        BlendChannels(result, a, b, weight, mask);
    }

    /// <summary>result = a relative to b on every channel, for use as an additive layer.</summary>
    public static void Subtract(HumanPose result, HumanPose a, HumanPose b)
    {
        Validate(result, a, b);

        for (int i = 0; i < HumanTrait.MuscleCount; i++)
            result.SetMuscle(i, a.GetMuscle(i) - b.GetMuscle(i));

        result.BodyPosition = a.BodyPosition - b.BodyPosition;
        result.BodyRotation = Quaternion.Normalize(Quaternion.Inverse(b.BodyRotation) * a.BodyRotation);

        for (int g = 0; g < HumanPose.GoalCount; g++)
        {
            HumanGoalState ga = a.GetGoal((HumanGoal)g);
            HumanGoalState gb = b.GetGoal((HumanGoal)g);
            result.SetGoal((HumanGoal)g, new HumanGoalState
            {
                Transform = new Transform3D(ga.Transform.position - gb.Transform.position, Quaternion.Normalize(Quaternion.Inverse(gb.Transform.rotation) * ga.Transform.rotation), Float3.One),
                PositionWeight = ga.PositionWeight - gb.PositionWeight,
                RotationWeight = ga.RotationWeight - gb.RotationWeight,
                Pole = ga.Pole - gb.Pole,
                HasPole = ga.HasPole,
            });
        }

        result.LookAtPosition = a.LookAtPosition - b.LookAtPosition;
        result.LookAtClampWeight = a.LookAtClampWeight - b.LookAtClampWeight;
        result.LookAtBodyWeight = a.LookAtBodyWeight - b.LookAtBodyWeight;
        result.LookAtHeadWeight = a.LookAtHeadWeight - b.LookAtHeadWeight;
        result.LookAtEyesWeight = a.LookAtEyesWeight - b.LookAtEyesWeight;
    }

    /// <summary>Adds an additive layer (a <see cref="Subtract"/> result) onto a base pose, weighted and masked.</summary>
    public static void AddLayer(HumanPose basePose, HumanPose additive, float weight, HumanPoseMask? mask = null)
    {
        ArgumentNullException.ThrowIfNull(basePose);
        ArgumentNullException.ThrowIfNull(additive);

        for (int i = 0; i < HumanTrait.MuscleCount; i++)
            basePose.SetMuscle(i, basePose.GetMuscle(i) + additive.GetMuscle(i) * weight * MuscleWeight(mask, i));

        float root = weight * (mask?.RootWeight ?? 1f);
        basePose.BodyPosition += additive.BodyPosition * root;
        basePose.BodyRotation = Quaternion.Normalize(basePose.BodyRotation * Quaternion.Slerp(Quaternion.Identity, additive.BodyRotation, root));

        for (int g = 0; g < HumanPose.GoalCount; g++)
        {
            float w = weight * (mask?.GetGoalWeight((HumanGoal)g) ?? 1f);
            HumanGoalState gb = basePose.GetGoal((HumanGoal)g);
            HumanGoalState ga = additive.GetGoal((HumanGoal)g);
            basePose.SetGoal((HumanGoal)g, new HumanGoalState
            {
                Transform = new Transform3D(gb.Transform.position + ga.Transform.position * w, Quaternion.Normalize(gb.Transform.rotation * Quaternion.Slerp(Quaternion.Identity, ga.Transform.rotation, w)), Float3.One),
                PositionWeight = gb.PositionWeight + ga.PositionWeight * w,
                RotationWeight = gb.RotationWeight + ga.RotationWeight * w,
                Pole = gb.Pole + ga.Pole * w,
                HasPole = gb.HasPole || ga.HasPole,
            });
        }

        float look = weight * LookWeight(mask);
        basePose.LookAtPosition += additive.LookAtPosition * look;
        basePose.LookAtClampWeight += additive.LookAtClampWeight * look;
        basePose.LookAtBodyWeight += additive.LookAtBodyWeight * look;
        basePose.LookAtHeadWeight += additive.LookAtHeadWeight * look;
        basePose.LookAtEyesWeight += additive.LookAtEyesWeight * look;
    }

    private static void BlendChannels(HumanPose result, HumanPose a, HumanPose b, float weight, HumanPoseMask? mask)
    {
        for (int i = 0; i < HumanTrait.MuscleCount; i++)
            result.SetMuscle(i, Maths.Lerp(a.GetMuscle(i), b.GetMuscle(i), weight * MuscleWeight(mask, i)));

        float root = weight * (mask?.RootWeight ?? 1f);
        result.BodyPosition = Maths.Lerp(a.BodyPosition, b.BodyPosition, root);
        result.BodyRotation = Quaternion.Slerp(a.BodyRotation, b.BodyRotation, root);

        for (int g = 0; g < HumanPose.GoalCount; g++)
        {
            float w = weight * (mask?.GetGoalWeight((HumanGoal)g) ?? 1f);
            result.SetGoal((HumanGoal)g, LerpGoal(a.GetGoal((HumanGoal)g), b.GetGoal((HumanGoal)g), w));
        }

        float look = weight * LookWeight(mask);
        result.LookAtPosition = Maths.Lerp(a.LookAtPosition, b.LookAtPosition, look);
        result.LookAtClampWeight = Maths.Lerp(a.LookAtClampWeight, b.LookAtClampWeight, look);
        result.LookAtBodyWeight = Maths.Lerp(a.LookAtBodyWeight, b.LookAtBodyWeight, look);
        result.LookAtHeadWeight = Maths.Lerp(a.LookAtHeadWeight, b.LookAtHeadWeight, look);
        result.LookAtEyesWeight = Maths.Lerp(a.LookAtEyesWeight, b.LookAtEyesWeight, look);
    }

    private static float MuscleWeight(HumanPoseMask? mask, int muscle)
        => mask?.GetBoneWeight(HumanTrait.GetMuscleBone(muscle)) ?? 1f;

    private static float LookWeight(HumanPoseMask? mask) => mask?.GetBoneWeight(HumanBodyBone.Head) ?? 1f;

    // A pole only one side has is kept as is rather than blended toward an unset zero.
    private static HumanGoalState LerpGoal(HumanGoalState a, HumanGoalState b, float t) => new()
    {
        Transform = new Transform3D(Maths.Lerp(a.Transform.position, b.Transform.position, t), Quaternion.Slerp(a.Transform.rotation, b.Transform.rotation, t), Float3.One),
        PositionWeight = Maths.Lerp(a.PositionWeight, b.PositionWeight, t),
        RotationWeight = Maths.Lerp(a.RotationWeight, b.RotationWeight, t),
        Pole = a.HasPole && b.HasPole ? Maths.Lerp(a.Pole, b.Pole, t) : a.HasPole ? a.Pole : b.Pole,
        HasPole = a.HasPole || b.HasPole,
    };

    private static void Validate(HumanPose result, HumanPose a, HumanPose b)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
    }
}
