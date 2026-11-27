using System.Collections.Generic;
using Prowl.Vector;

namespace Prowl.Motion;

/// <summary>
/// The component of a bone's rotation a muscle drives, in the bone's axis frame. X is the aim axis
/// (twist), Y and Z are the two swing axes.
/// </summary>
public enum MuscleAxis : byte
{
    X,
    Y,
    Z
}

public static partial class HumanTrait
{
    private readonly record struct MuscleInfo(HumanBodyBone Bone, MuscleAxis Axis, float Min, float Max, string Name);

    // How a bone's axis frame is built in the T pose. The X axis aims at the next joint down the chain, along
    // the bone from its parent, or along a fixed model axis. Y is Reference cross X. Zero is the rotation from
    // the T pose frame to the frame all zero muscles give, as the vector part of an unnormalized quaternion
    // in model axes. Signs flip each axis so a muscle means the same motion on both sides.
    internal enum AimKind : byte { Child, FromParent, Fixed }

    internal readonly record struct BoneSpec(AimKind Aim, Float3 FixedAim, Float3 Reference, Quaternion Zero, Float3 Signs);

    private static MuscleInfo[] s_muscles = Array.Empty<MuscleInfo>();
    private static int[] s_boneMuscles = Array.Empty<int>();
    private static int[] s_mirrorMuscles = Array.Empty<int>();
    private static bool[] s_mirrorFlips = Array.Empty<bool>();
    private static BoneSpec[] s_specs = Array.Empty<BoneSpec>();

    /// <summary>Number of body and finger muscles in a <see cref="HumanPose"/>.</summary>
    public static int MuscleCount => s_muscles.Length;

    /// <summary>Display name of a muscle, e.g. "Left Arm Down-Up".</summary>
    public static string GetMuscleName(int muscle) => s_muscles[muscle].Name;

    /// <summary>The humanoid bone a muscle rotates.</summary>
    public static HumanBodyBone GetMuscleBone(int muscle) => s_muscles[muscle].Bone;

    /// <summary>The axis frame axis a muscle rotates about.</summary>
    public static MuscleAxis GetMuscleAxis(int muscle) => s_muscles[muscle].Axis;

    /// <summary>Default angle in degrees reached at muscle value -1.</summary>
    public static float GetMuscleDefaultMin(int muscle) => s_muscles[muscle].Min;

    /// <summary>Default angle in degrees reached at muscle value +1.</summary>
    public static float GetMuscleDefaultMax(int muscle) => s_muscles[muscle].Max;

    /// <summary>The muscle driving the given axis of a bone, or -1 if that axis is locked.</summary>
    public static int GetMuscleIndex(HumanBodyBone bone, MuscleAxis axis) => s_boneMuscles[(int)bone * 3 + (int)axis];

    /// <summary>The muscle on the opposite side of the body (centre muscles map to themselves).</summary>
    public static int GetMirrorMuscle(int muscle) => s_mirrorMuscles[muscle];

    /// <summary>True if the muscle's value changes sign when a pose is mirrored left to right.</summary>
    public static bool MuscleFlipsWhenMirrored(int muscle) => s_mirrorFlips[muscle];

    /// <summary>The bone on the opposite side of the body (centre bones map to themselves).</summary>
    public static HumanBodyBone GetMirrorBone(HumanBodyBone bone)
    {
        string name = bone.ToString();
        if (name.StartsWith("Left", StringComparison.Ordinal))
            return Enum.Parse<HumanBodyBone>("Right" + name[4..]);
        if (name.StartsWith("Right", StringComparison.Ordinal))
            return Enum.Parse<HumanBodyBone>("Left" + name[5..]);
        return bone;
    }

    internal static BoneSpec GetBoneSpec(HumanBodyBone bone) => s_specs[(int)bone];

    private static string CoreName(HumanBodyBone bone)
    {
        string name = bone.ToString();
        return GetSide(bone) switch
        {
            BoneSide.Left => name[4..],
            BoneSide.Right => name[5..],
            _ => name,
        };
    }

    private static MuscleInfo[] BuildMuscles()
    {
        const MuscleAxis X = MuscleAxis.X, Y = MuscleAxis.Y, Z = MuscleAxis.Z;
        var muscles = new List<MuscleInfo>();
        void Add(HumanBodyBone bone, MuscleAxis axis, float min, float max, string name) => muscles.Add(new MuscleInfo(bone, axis, min, max, name));

        foreach ((HumanBodyBone bone, float range) in new[] { (HumanBodyBone.Spine, 40f), (HumanBodyBone.Chest, 40f), (HumanBodyBone.UpperChest, 20f) })
        {
            Add(bone, Z, -range, range, bone + " Front-Back");
            Add(bone, Y, -range, range, bone + " Left-Right");
            Add(bone, X, -range, range, bone + " Twist Left-Right");
        }
        foreach (HumanBodyBone bone in new[] { HumanBodyBone.Neck, HumanBodyBone.Head })
        {
            Add(bone, Z, -40f, 40f, bone + " Nod Down-Up");
            Add(bone, Y, -40f, 40f, bone + " Tilt Left-Right");
            Add(bone, X, -40f, 40f, bone + " Turn Left-Right");
        }
        foreach ((string side, HumanBodyBone eye) in new[] { ("Left", HumanBodyBone.LeftEye), ("Right", HumanBodyBone.RightEye) })
        {
            Add(eye, Z, -10f, 15f, side + " Eye Down-Up");
            Add(eye, Y, -20f, 20f, side + " Eye In-Out");
        }
        Add(HumanBodyBone.Jaw, Z, -10f, 10f, "Jaw Close");
        Add(HumanBodyBone.Jaw, Y, -10f, 10f, "Jaw Left-Right");

        foreach (string side in new[] { "Left", "Right" })
        {
            HumanBodyBone Bone(string core) => Enum.Parse<HumanBodyBone>(side + core);
            Add(Bone("UpperLeg"), Z, -90f, 50f, side + " Upper Leg Front-Back");
            Add(Bone("UpperLeg"), Y, -60f, 60f, side + " Upper Leg In-Out");
            Add(Bone("UpperLeg"), X, -60f, 60f, side + " Upper Leg Twist In-Out");
            Add(Bone("LowerLeg"), Z, -80f, 80f, side + " Lower Leg Stretch");
            Add(Bone("LowerLeg"), X, -90f, 90f, side + " Lower Leg Twist In-Out");
            Add(Bone("Foot"), Z, -50f, 50f, side + " Foot Up-Down");
            Add(Bone("Foot"), Y, -30f, 30f, side + " Foot Twist In-Out");
            Add(Bone("Toes"), Z, -50f, 50f, side + " Toes Up-Down");
        }
        foreach (string side in new[] { "Left", "Right" })
        {
            HumanBodyBone Bone(string core) => Enum.Parse<HumanBodyBone>(side + core);
            Add(Bone("Shoulder"), Z, -15f, 30f, side + " Shoulder Down-Up");
            Add(Bone("Shoulder"), Y, -15f, 15f, side + " Shoulder Front-Back");
            Add(Bone("UpperArm"), Z, -60f, 100f, side + " Arm Down-Up");
            Add(Bone("UpperArm"), Y, -100f, 100f, side + " Arm Front-Back");
            Add(Bone("UpperArm"), X, -90f, 90f, side + " Arm Twist In-Out");
            Add(Bone("LowerArm"), Z, -80f, 80f, side + " Forearm Stretch");
            Add(Bone("LowerArm"), X, -90f, 90f, side + " Forearm Twist In-Out");
            Add(Bone("Hand"), Z, -80f, 80f, side + " Hand Down-Up");
            Add(Bone("Hand"), Y, -40f, 40f, side + " Hand In-Out");
        }
        foreach (string side in new[] { "Left", "Right" })
        {
            foreach ((string finger, float spread) in new[] { ("Thumb", 25f), ("Index", 20f), ("Middle", 7.5f), ("Ring", 7.5f), ("Little", 20f) })
            {
                HumanBodyBone Bone(string phalanx) => Enum.Parse<HumanBodyBone>(side + finger + phalanx);
                bool thumb = finger == "Thumb";
                Add(Bone("Proximal"), Z, thumb ? -20f : -50f, thumb ? 20f : 50f, $"{side} {finger} 1 Stretched");
                Add(Bone("Proximal"), Y, -spread, spread, $"{side} {finger} Spread");
                Add(Bone("Intermediate"), Z, thumb ? -40f : -45f, thumb ? 35f : 45f, $"{side} {finger} 2 Stretched");
                Add(Bone("Distal"), Z, thumb ? -40f : -45f, thumb ? 35f : 45f, $"{side} {finger} 3 Stretched");
            }
        }

        s_boneMuscles = new int[s_all.Length * 3];
        Array.Fill(s_boneMuscles, -1);
        for (int i = 0; i < muscles.Count; i++)
            s_boneMuscles[(int)muscles[i].Bone * 3 + (int)muscles[i].Axis] = i;

        s_mirrorMuscles = new int[muscles.Count];
        s_mirrorFlips = new bool[muscles.Count];
        for (int i = 0; i < muscles.Count; i++)
        {
            s_mirrorMuscles[i] = s_boneMuscles[(int)GetMirrorBone(muscles[i].Bone) * 3 + (int)muscles[i].Axis];
            s_mirrorFlips[i] = GetSide(muscles[i].Bone) == BoneSide.Center && muscles[i].Axis != MuscleAxis.Z;
        }

        return muscles.ToArray();
    }

    private static BoneSpec[] BuildBoneSpecs()
    {
        var specs = new BoneSpec[s_all.Length];
        foreach (HumanBodyBone bone in s_all)
        {
            BoneSpec left = LeftSpec(CoreName(bone));
            specs[(int)bone] = GetSide(bone) == BoneSide.Right ? Mirror(left) : left;
        }
        return specs;
    }

    // The right side is the left reflected across the sagittal plane. Reflection reverses rotations, so
    // twist flips, and the swing axis that the reflection maps onto itself flips too.
    private static BoneSpec Mirror(BoneSpec left)
    {
        bool lateralReference = MathF.Abs(left.Reference.X) > 0.5f;
        var signs = new Float3(-left.Signs.X, lateralReference ? -left.Signs.Y : left.Signs.Y, lateralReference ? left.Signs.Z : -left.Signs.Z);
        var fixedAim = new Float3(-left.FixedAim.X, left.FixedAim.Y, left.FixedAim.Z);
        var zero = new Quaternion(left.Zero.X, -left.Zero.Y, -left.Zero.Z, left.Zero.W);
        return left with { FixedAim = fixedAim, Zero = zero, Signs = signs };
    }

    // Left side and centre bones. Model axes: X right, Y up, Z forward.
    private static BoneSpec LeftSpec(string core)
    {
        var right = new Float3(1f, 0f, 0f);
        var up = new Float3(0f, 1f, 0f);
        var forward = new Float3(0f, 0f, 1f);

        BoneSpec Spec(AimKind aim, Float3 reference, Float3 zero, Float3 signs, Float3 fixedAim = default)
            => new(aim, fixedAim, reference, Quaternion.Normalize(new Quaternion(zero.X, zero.Y, zero.Z, 1f)), signs);

        var one = new Float3(1f, 1f, 1f);
        var armSigns = new Float3(1f, 1f, -1f);

        if (core.StartsWith("Thumb", StringComparison.Ordinal))
        {
            return core.EndsWith("Proximal", StringComparison.Ordinal)
                ? Spec(AimKind.Child, up, new Float3(0f, 0.125f, 0.125f), new Float3(-1f, -1f, 1f))
                : Spec(core.EndsWith("Distal", StringComparison.Ordinal) ? AimKind.FromParent : AimKind.Child, up, new Float3(0f, -0.2f, 0f), new Float3(-1f, 1f, 1f));
        }
        foreach ((string finger, float spread) in new[] { ("Index", 0.08f), ("Middle", 0.04f), ("Ring", -0.04f), ("Little", -0.08f) })
        {
            if (!core.StartsWith(finger, StringComparison.Ordinal))
                continue;
            if (core.EndsWith("Proximal", StringComparison.Ordinal))
                return Spec(AimKind.Child, forward, new Float3(0f, spread, 0.3f), new Float3(-1f, spread > 0f ? -1f : 1f, -1f));
            return Spec(core.EndsWith("Distal", StringComparison.Ordinal) ? AimKind.FromParent : AimKind.Child, forward, new Float3(0f, 0f, 0.33f), new Float3(-1f, 1f, -1f));
        }

        return core switch
        {
            "Hips" or "Spine" or "Chest" or "UpperChest" or "Neck" => Spec(AimKind.Child, -right, default, one),
            "Head" => Spec(AimKind.Fixed, -right, default, one, up),
            "UpperLeg" => Spec(AimKind.Child, right, new Float3(-0.268f, 0f, 0f), one),
            "LowerLeg" => Spec(AimKind.Child, right, new Float3(0.839f, 0f, 0f), new Float3(1f, 1f, -1f)),
            "Foot" => Spec(AimKind.Fixed, right, default, one, -up),
            "Toes" => Spec(AimKind.Fixed, right, default, one, forward),
            "Shoulder" => Spec(AimKind.Child, forward, default, armSigns),
            "UpperArm" => Spec(AimKind.Child, forward, new Float3(0f, 0.268f, 0.364f), armSigns),
            "LowerArm" => Spec(AimKind.Child, up, new Float3(0f, 0.839f, 0f), armSigns),
            "Hand" => Spec(AimKind.FromParent, forward, default, armSigns),
            "Eye" => Spec(AimKind.Fixed, right, default, armSigns, forward),
            "Jaw" => Spec(AimKind.Fixed, right, new Float3(0.09f, 0f, 0f), armSigns, forward),
            _ => Spec(AimKind.Fixed, right, default, one, forward),
        };
    }
}
