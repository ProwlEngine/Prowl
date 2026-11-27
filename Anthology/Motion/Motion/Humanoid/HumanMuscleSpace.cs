using Prowl.Vector;

namespace Prowl.Motion;

/// <summary>
/// The muscle codec. A mapped bone's local rotation is <c>Pre * Swing(y, z) * Twist(x) * inverse(Post)</c>,
/// where x, y and z are its muscles turned into angles and the swing is built from the tangents of the
/// half angles. The upper arm, forearm, upper leg and lower leg show only their twist share, and the
/// bones below them rotate with the full twist. Encoding is the inverse. The elbow and knee bend about
/// one axis and the hand and foot do not twist, so for a limb the upper bone's twist is the one that puts
/// the joint below in its bend plane, and the lower bone's twist is the one that leaves the hand or foot
/// untwisted.
/// </summary>
internal static class HumanMuscleSpace
{
    private const float DegToRad = MathF.PI / 180f;
    private static readonly Float3 s_x = new(1f, 0f, 0f);
    private static readonly Float3 s_z = new(0f, 0f, 1f);

    // How far past the end of its range, in muscle units, a bend may go and still be read as slightly past straight.
    private const float Overshoot = 0.25f;

    // Below this bend the upper twist eases toward the bone's own twist.
    private const float StraightDegrees = 10f;
    private static readonly float s_straightSin = MathF.Sin(StraightDegrees * DegToRad);

    private static readonly (HumanBodyBone Upper, HumanBodyBone Lower, HumanBodyBone End)[] s_limbs =
    {
        (HumanBodyBone.LeftUpperArm, HumanBodyBone.LeftLowerArm, HumanBodyBone.LeftHand),
        (HumanBodyBone.RightUpperArm, HumanBodyBone.RightLowerArm, HumanBodyBone.RightHand),
        (HumanBodyBone.LeftUpperLeg, HumanBodyBone.LeftLowerLeg, HumanBodyBone.LeftFoot),
        (HumanBodyBone.RightUpperLeg, HumanBodyBone.RightLowerLeg, HumanBodyBone.RightFoot),
    };

    [ThreadStatic] private static Quaternion[]? s_nominal;

    /// <summary>
    /// Builds the model space rotation of every skeleton bone from the muscles, with the hips at
    /// <paramref name="hips"/>. Bones that are not mapped keep their T pose local rotation.
    /// </summary>
    public static void Decode(HumanoidRig rig, HumanPose pose, Quaternion hips, Quaternion[] world)
    {
        Skeleton skeleton = rig.Skeleton;
        Quaternion[] nominal = Buffer(skeleton.BoneCount);
        int[] parents = skeleton.SanitizedParentIndices;
        int hipsIndex = rig.GetSkeletonBoneIndex(HumanBodyBone.Hips);

        foreach (int index in rig.CodecOrder)
        {
            int parent = parents[index];
            Quaternion parentWorld = parent == Skeleton.InvalidIndex ? Quaternion.Identity : world[parent];
            Quaternion parentNominal = parent == Skeleton.InvalidIndex ? Quaternion.Identity : nominal[parent];

            if (index == hipsIndex)
            {
                world[index] = nominal[index] = hips;
            }
            else if (rig.TryGetHumanBone(index, out HumanBodyBone bone))
            {
                int frameParent = rig.GetFrameParent(bone);
                parentNominal = frameParent == Skeleton.InvalidIndex ? Quaternion.Identity : nominal[frameParent];
                Float3 angles = Angles(rig, pose, bone);
                float share = TwistShare(rig, bone);
                Quaternion post = rig.GetPost(bone);
                Quaternion postInverse = rig.GetPostInverse(bone);
                Quaternion local = rig.GetPre(bone) * Swing(angles.Y, angles.Z) * TwistX(angles.X * share) * postInverse;
                world[index] = Quaternion.Normalize(parentNominal * local);
                nominal[index] = share < 1f
                    ? Quaternion.Normalize(world[index] * post * TwistX(angles.X * (1f - share)) * postInverse)
                    : world[index];
            }
            else
            {
                Quaternion local = rig.GetTPoseLocal(index);
                world[index] = Quaternion.Normalize(parentWorld * local);
                nominal[index] = Quaternion.Normalize(parentNominal * local);
            }
        }
    }

    /// <summary>Reads the muscles from the model space rotation of every skeleton bone.</summary>
    public static void Encode(HumanoidRig rig, Quaternion[] world, HumanPose result)
    {
        Skeleton skeleton = rig.Skeleton;
        Quaternion[] nominal = Buffer(skeleton.BoneCount);
        int[] parents = skeleton.SanitizedParentIndices;
        int hipsIndex = rig.GetSkeletonBoneIndex(HumanBodyBone.Hips);

        foreach (int index in rig.EncodeOrder)
        {
            int parent = parents[index];
            Quaternion parentNominal = parent == Skeleton.InvalidIndex ? Quaternion.Identity : nominal[parent];

            if (index == hipsIndex || !rig.TryGetHumanBone(index, out HumanBodyBone bone))
            {
                Quaternion parentWorld = parent == Skeleton.InvalidIndex ? Quaternion.Identity : world[parent];
                nominal[index] = Quaternion.Normalize(parentNominal * Quaternion.Conjugate(parentWorld) * world[index]);
                continue;
            }

            int frameParent = rig.GetFrameParent(bone);
            parentNominal = frameParent == Skeleton.InvalidIndex ? Quaternion.Identity : nominal[frameParent];
            int limb = LimbOf(bone);
            if (limb < 0)
            {
                Split(rig.GetPreInverse(bone) * Quaternion.Conjugate(parentNominal) * world[index] * rig.GetPost(bone), out Quaternion twist, out Quaternion swing);
                bool keepX = !Locked(bone, MuscleAxis.X), keepY = !Locked(bone, MuscleAxis.Y), keepZ = !Locked(bone, MuscleAxis.Z);
                Store(rig, result, bone, new Float3(
                    keepX ? 2f * MathF.Atan2(twist.X, twist.W) : 0f,
                    keepY ? 2f * MathF.Atan2(swing.Y, swing.W) : 0f,
                    keepZ ? 2f * MathF.Atan2(swing.Z, swing.W) : 0f));

                // Children are measured against what decoding rebuilds, which drops the axes the bone has no muscle for.
                Quaternion keptSwing = Quaternion.Normalize(new Quaternion(0f, keepY ? swing.Y : 0f, keepZ ? swing.Z : 0f, swing.W));
                Quaternion keptTwist = keepX ? twist : Quaternion.Identity;
                nominal[index] = Quaternion.Normalize(parentNominal * rig.GetPre(bone) * keptSwing * keptTwist * rig.GetPostInverse(bone));
            }
            else if (s_limbs[limb].Upper == bone)
            {
                nominal[index] = EncodeLimb(rig, s_limbs[limb], parentNominal, world, result, out Quaternion lowerNominal);
                nominal[rig.GetSkeletonBoneIndex(s_limbs[limb].Lower)] = lowerNominal;
            }
            else if (s_limbs[limb].End == bone)
            {
                nominal[index] = world[index];
            }
        }
    }

    // Solves one limb and returns the upper bone's nominal rotation (with its full twist).
    private static Quaternion EncodeLimb(HumanoidRig rig, (HumanBodyBone Upper, HumanBodyBone Lower, HumanBodyBone End) limb,
        Quaternion parentNominal, Quaternion[] world, HumanPose result, out Quaternion lowerNominal)
    {
        int upper = rig.GetSkeletonBoneIndex(limb.Upper);
        int lower = rig.GetSkeletonBoneIndex(limb.Lower);
        int end = rig.GetSkeletonBoneIndex(limb.End);
        Quaternion preUpper = rig.GetPre(limb.Upper), postUpper = rig.GetPost(limb.Upper);
        Quaternion preLower = rig.GetPre(limb.Lower), postLower = rig.GetPost(limb.Lower);
        Quaternion postEnd = rig.GetPost(limb.End);

        // Bones between the three joints that are not mapped ride along as they are.
        Quaternion between = Quaternion.Conjugate(world[upper]) * world[rig.GetFrameParent(limb.Lower)];
        Quaternion betweenEnd = Quaternion.Conjugate(world[lower]) * world[rig.GetFrameParent(limb.End)];

        Float3 upperAngles = Decompose(rig.GetPreInverse(limb.Upper) * Quaternion.Conjugate(parentNominal) * world[upper] * postUpper);
        Quaternion swung = parentNominal * preUpper * Swing(upperAngles.Y, upperAngles.Z);
        Quaternion toLower = rig.GetPostInverse(limb.Upper) * between * preLower;

        // The lower bone's own aim, so bones between it and the end joint that are not mapped do not bend the limb.
        Float3 wrist = world[lower] * postLower * s_x;
        float share = TwistShare(rig, limb.Upper);
        float guess = share > 1e-4f ? upperAngles.X / share : 0f;
        float twist = HingeTwist(rig, limb.Lower, Quaternion.Inverse(swung) * wrist, toLower, guess);

        Quaternion upperNominal = Quaternion.Normalize(swung * TwistX(twist) * rig.GetPostInverse(limb.Upper));
        Quaternion lowerFrame = upperNominal * between * preLower;
        Float3 v = Quaternion.Inverse(lowerFrame) * wrist;
        float bend = MathF.Atan2(v.Y, v.X);

        // The lower bone's twist makes the end bone's twist about its own axis zero.
        Quaternion bent = lowerFrame * Swing(0f, bend);
        Quaternion lead = rig.GetPreInverse(limb.End) * Quaternion.Inverse(betweenEnd) * postLower;
        Quaternion trail = Quaternion.Inverse(bent) * world[end] * postEnd;
        float a = (lead * trail).X;
        float b = (lead * new Quaternion(-1f, 0f, 0f, 0f) * trail).X;
        float lowerTwist = Wrap(2f * MathF.Atan2(-a, b));

        lowerNominal = Quaternion.Normalize(bent * TwistX(lowerTwist) * rig.GetPostInverse(limb.Lower));
        Float3 endAngles = Decompose(lead * TwistX(-lowerTwist) * trail);

        Store(rig, result, limb.Upper, new Float3(twist, upperAngles.Y, upperAngles.Z));
        Store(rig, result, limb.Lower, new Float3(lowerTwist, 0f, bend));
        Store(rig, result, limb.End, endAngles);
        return upperNominal;
    }

    // The upper bone twist that puts the direction to the end joint (in the upper bone's untwisted axis
    // frame) into the lower bone's bend plane. The two answers bend the joint opposite ways. The one inside
    // the muscle range wins, unless the other is only a little past straight and clearly closer to the guess,
    // which is how a joint slightly past straight reads. A limb within a few degrees of straight has no
    // reliable bend plane, so the answer eases toward the guess as the bend goes to zero.
    private static float HingeTwist(HumanoidRig rig, HumanBodyBone lower, Float3 u, Quaternion toLower, float guess)
    {
        Float3 kz = toLower * s_z;
        float a = kz.Y * u.Y + kz.Z * u.Z;
        float b = kz.Y * u.Z - kz.Z * u.Y;
        float c = kz.X * u.X;
        float r = MathF.Sqrt(a * a + b * b);
        if (r < 1e-6f)
            return guess;
        float weight = Math.Clamp(r / (s_straightSin * Float3.Length(u)), 0f, 1f);
        weight = weight * weight * (3f - 2f * weight);

        float phase = MathF.Atan2(b, a);
        float offset = MathF.Acos(Math.Clamp(-c / r, -1f, 1f));
        float first = Wrap(phase + offset), second = Wrap(phase - offset);
        float firstExcess = Excess(rig, lower, first, u, toLower), secondExcess = Excess(rig, lower, second, u, toLower);
        float firstMiss = MathF.Abs(Wrap(first - guess)), secondMiss = MathF.Abs(Wrap(second - guess));

        bool pickFirst;
        if (firstExcess <= 0f && secondExcess <= 0f)
            pickFirst = firstMiss <= secondMiss;
        else if (firstExcess <= 0f)
            pickFirst = !(secondExcess < Overshoot && secondMiss < firstMiss - MathF.PI * 0.5f);
        else if (secondExcess <= 0f)
            pickFirst = firstExcess < Overshoot && firstMiss < secondMiss - MathF.PI * 0.5f;
        else
            pickFirst = firstExcess < secondExcess;

        float best = pickFirst ? first : second;
        return Wrap(guess + Wrap(best - guess) * weight);
    }

    // How far the bend a candidate twist leaves on the lower bone lies outside its muscle range.
    private static float Excess(HumanoidRig rig, HumanBodyBone lower, float twist, Float3 u, Quaternion toLower)
    {
        Float3 v = Quaternion.Inverse(TwistX(twist) * toLower) * u;
        return MathF.Abs(MuscleValue(rig, lower, MuscleAxis.Z, MathF.Atan2(v.Y, v.X))) - 1f;
    }

    private static bool Locked(HumanBodyBone bone, MuscleAxis axis) => HumanTrait.GetMuscleIndex(bone, axis) < 0;

    private static int LimbOf(HumanBodyBone bone)
    {
        for (int i = 0; i < s_limbs.Length; i++)
            if (s_limbs[i].Upper == bone || s_limbs[i].Lower == bone || s_limbs[i].End == bone)
                return i;
        return -1;
    }

    private static float TwistShare(HumanoidRig rig, HumanBodyBone bone) => bone switch
    {
        HumanBodyBone.LeftUpperArm or HumanBodyBone.RightUpperArm => rig.UpperArmTwist,
        HumanBodyBone.LeftLowerArm or HumanBodyBone.RightLowerArm => rig.LowerArmTwist,
        HumanBodyBone.LeftUpperLeg or HumanBodyBone.RightUpperLeg => rig.UpperLegTwist,
        HumanBodyBone.LeftLowerLeg or HumanBodyBone.RightLowerLeg => rig.LowerLegTwist,
        _ => 1f,
    };

    /// <summary>The swing about Y then Z from the tangents of the half angles.</summary>
    internal static Quaternion Swing(float y, float z)
        => Quaternion.Normalize(new Quaternion(0f, y == 0f ? 0f : MathF.Tan(y * 0.5f), z == 0f ? 0f : MathF.Tan(z * 0.5f), 1f));

    /// <summary>
    /// Splits a rotation into a twist about X and the swing before it, as rotations, with no
    /// trigonometry. The twist and the swing have a positive W, and the swing has no X part.
    /// </summary>
    private static void Split(Quaternion q, out Quaternion twist, out Quaternion swing)
    {
        if (q.W < 0f)
            q = -q;
        float length = MathF.Sqrt(q.X * q.X + q.W * q.W);
        twist = length > 1e-12f ? new Quaternion(q.X / length, 0f, 0f, q.W / length) : Quaternion.Identity;
        swing = q * new Quaternion(-twist.X, 0f, 0f, twist.W);
        if (swing.W < 0f)
            swing = -swing;
        swing = new Quaternion(0f, swing.Y, swing.Z, swing.W);
    }

    /// <summary>A turn about X, with no trigonometry at all for a zero angle.</summary>
    private static Quaternion TwistX(float angle)
    {
        if (angle == 0f)
            return Quaternion.Identity;
        (float sin, float cos) = MathF.SinCos(angle * 0.5f);
        return new Quaternion(sin, 0f, 0f, cos);
    }

    /// <summary>Splits a rotation into a swing (tangent half angle form) followed by a twist about X.</summary>
    internal static Float3 Decompose(Quaternion q)
    {
        if (q.W < 0f)
            q = -q;
        float twist = 2f * MathF.Atan2(q.X, q.W);
        Quaternion swing = q * TwistX(-twist);
        if (swing.W < 0f)
            swing = -swing;
        return new Float3(twist, 2f * MathF.Atan2(swing.Y, swing.W), 2f * MathF.Atan2(swing.Z, swing.W));
    }

    // The bone's three angles in radians along its axis frame, signed per the bone's axis signs.
    private static Float3 Angles(HumanoidRig rig, HumanPose pose, HumanBodyBone bone)
    {
        Float3 signs = HumanTrait.GetBoneSpec(bone).Signs;
        return new Float3(Angle(rig, pose, bone, MuscleAxis.X) * signs.X, Angle(rig, pose, bone, MuscleAxis.Y) * signs.Y, Angle(rig, pose, bone, MuscleAxis.Z) * signs.Z);
    }

    private static float Angle(HumanoidRig rig, HumanPose pose, HumanBodyBone bone, MuscleAxis axis)
    {
        int muscle = HumanTrait.GetMuscleIndex(bone, axis);
        if (muscle < 0)
            return 0f;
        float value = pose.GetMuscle(muscle);
        return (value >= 0f ? value * rig.MuscleMax(muscle) : -value * rig.MuscleMin(muscle)) * DegToRad;
    }

    private static void Store(HumanoidRig rig, HumanPose pose, HumanBodyBone bone, Float3 angles)
    {
        Float3 signs = HumanTrait.GetBoneSpec(bone).Signs;
        StoreAxis(rig, pose, bone, MuscleAxis.X, angles.X * signs.X);
        StoreAxis(rig, pose, bone, MuscleAxis.Y, angles.Y * signs.Y);
        StoreAxis(rig, pose, bone, MuscleAxis.Z, angles.Z * signs.Z);
    }

    private static void StoreAxis(HumanoidRig rig, HumanPose pose, HumanBodyBone bone, MuscleAxis axis, float radians)
    {
        int muscle = HumanTrait.GetMuscleIndex(bone, axis);
        if (muscle >= 0)
            pose.SetMuscle(muscle, ToValue(rig, muscle, radians));
    }

    // The muscle value of an axis angle given in the bone's axis frame.
    private static float MuscleValue(HumanoidRig rig, HumanBodyBone bone, MuscleAxis axis, float radians)
    {
        int muscle = HumanTrait.GetMuscleIndex(bone, axis);
        return muscle < 0 ? 0f : ToValue(rig, muscle, radians * Component(HumanTrait.GetBoneSpec(bone).Signs, axis));
    }

    private static float ToValue(HumanoidRig rig, int muscle, float radians)
    {
        float degrees = radians / DegToRad;
        float limit = degrees >= 0f ? rig.MuscleMax(muscle) : -rig.MuscleMin(muscle);
        return limit > 1e-6f ? degrees / limit : 0f;
    }

    private static float Component(Float3 v, MuscleAxis axis) => axis switch { MuscleAxis.X => v.X, MuscleAxis.Y => v.Y, _ => v.Z };

    private static float Wrap(float angle)
    {
        angle %= 2f * MathF.PI;
        if (angle > MathF.PI) angle -= 2f * MathF.PI;
        if (angle < -MathF.PI) angle += 2f * MathF.PI;
        return angle;
    }

    private static Quaternion[] Buffer(int count)
        => s_nominal is { } buffer && buffer.Length >= count ? buffer : s_nominal = new Quaternion[count];
}
