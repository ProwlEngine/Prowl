using System.Linq;
using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion;

/// <summary>
/// The rig dependent data the muscle codec works in, measured on the rig's T pose. Each mapped bone gets
/// an axis frame whose X axis runs down the bone, stored as <c>Post</c> (bone rotation to axis frame) and
/// <c>Pre</c> (parent rotation to the axis frame all zero muscles give). A bone's local rotation is then
/// <c>Pre * muscles * inverse(Post)</c>.
/// </summary>
internal sealed class HumanoidFrames
{
    public readonly Quaternion[] Pre = new Quaternion[HumanTrait.BoneCount];
    public readonly Quaternion[] Post = new Quaternion[HumanTrait.BoneCount];
    public readonly int[] FrameParent = new int[HumanTrait.BoneCount];
    public readonly float[] SegmentMass = new float[HumanoidFrameBuilder.SegmentCount];
    public Quaternion[] TPoseLocal = Array.Empty<Quaternion>();
    public Float3 Floor;
    public Quaternion BodyBind;
    public Quaternion Facing;
    public float Scale;
    public float LegLength;
}

/// <summary>Reads the model space position of the mapped humanoid bones.</summary>
internal interface IBonePositions
{
    bool Has(HumanBodyBone bone);
    Float3 Get(HumanBodyBone bone);
}

internal readonly struct DescriptionPositions : IBonePositions
{
    private readonly HumanDescription _description;
    private readonly Float3[] _position;

    public DescriptionPositions(HumanDescription description, Float3[] position)
    {
        _description = description;
        _position = position;
    }

    public bool Has(HumanBodyBone bone) => _description.HasBone(bone);
    public Float3 Get(HumanBodyBone bone) => _position[_description.GetSkeletonBoneIndex(bone)];
}

internal readonly struct RigPositions : IBonePositions
{
    private readonly HumanoidRig _rig;
    private readonly Float3[] _position;

    public RigPositions(HumanoidRig rig, Float3[] position)
    {
        _rig = rig;
        _position = position;
    }

    public bool Has(HumanBodyBone bone) => _rig.HasBone(bone);
    public Float3 Get(HumanBodyBone bone) => _position[_rig.GetSkeletonBoneIndex(bone)];
}

internal readonly struct PosePositions : IBonePositions
{
    private readonly HumanoidRig _rig;
    private readonly Pose _pose;

    public PosePositions(HumanoidRig rig, Pose pose)
    {
        _rig = rig;
        _pose = pose;
    }

    public bool Has(HumanBodyBone bone) => _rig.HasBone(bone);
    public Float3 Get(HumanBodyBone bone) => _pose.GetModelSpaceTransform(_rig.GetSkeletonBoneIndex(bone)).position;
}

/// <summary>Builds <see cref="HumanoidFrames"/> from a skeleton's reference pose and its humanoid mapping.</summary>
internal static class HumanoidFrameBuilder
{
    // Arms and fingers within this many degrees of straight are taken as they are.
    private const float Tolerance = 5f;

    private static readonly Float3 s_up = new(0f, 1f, 0f);

    public const int SegmentCount = 22;

    // Segment masses, indexed by body bone, summing to one.
    private static readonly float[] s_mass =
    {
        0.145454556f, 0.121212132f, 0.121212132f, 0.0484848544f, 0.0484848544f, 0.009696971f, 0.009696971f,
        0.0303030331f, 0.145454556f, 0.145454556f, 0.0121212136f, 0.0484848544f, 0.006060607f, 0.006060607f,
        0.0242424272f, 0.0242424272f, 0.01818182f, 0.01818182f, 0.006060607f, 0.006060607f, 0.00242424267f, 0.00242424267f,
    };

    public static HumanoidFrames Build(Skeleton skeleton, HumanDescription description)
    {
        var frames = new HumanoidFrames { TPoseLocal = TPose(skeleton, description) };
        (Quaternion[] world, Float3[] position) = ModelSpace(skeleton, frames.TPoseLocal);
        var positions = new DescriptionPositions(description, position);

        Quaternion facing = Facing(positions);
        Array.Fill(frames.FrameParent, Skeleton.InvalidIndex);
        var axisFrames = new Quaternion[HumanTrait.BoneCount];
        foreach (HumanBodyBone bone in HumanTrait.AllBones)
        {
            if (!description.HasBone(bone))
                continue;

            HumanTrait.BoneSpec spec = HumanTrait.GetBoneSpec(bone);
            int index = description.GetSkeletonBoneIndex(bone);
            HumanBodyBone? parentBone = HumanTrait.GetParentBone(bone);
            while (parentBone is not null && !description.HasBone(parentBone.Value))
                parentBone = HumanTrait.GetParentBone(parentBone.Value);
            Float3? fallback = parentBone is { } pb ? axisFrames[(int)pb] * new Float3(0f, 1f, 0f) : null;
            Quaternion axes = axisFrames[(int)bone] = AxisFrame(Aim(description, bone, spec, facing, positions), facing * spec.Reference, fallback);
            int parent = FrameParent(skeleton, description, bone, index);
            frames.FrameParent[(int)bone] = parent;
            Quaternion parentWorld = parent == Skeleton.InvalidIndex ? Quaternion.Identity : world[parent];

            frames.Post[(int)bone] = Quaternion.Normalize(Quaternion.Inverse(world[index]) * axes);
            Quaternion zero = facing * spec.Zero * Quaternion.Inverse(facing);
            if (IdealAim(bone) is { } ideal)
            {
                // The zero turn is given for a limb along its ideal direction, so carry it into this bone's own frame.
                Quaternion ideally = AxisFrame(facing * ideal, facing * spec.Reference, null);
                frames.Pre[(int)bone] = Quaternion.Normalize(Quaternion.Inverse(parentWorld) * axes * Quaternion.Inverse(ideally) * zero * ideally);
            }
            else
            {
                frames.Pre[(int)bone] = Quaternion.Normalize(Quaternion.Inverse(parentWorld) * zero * axes);
            }
        }

        frames.Facing = facing;
        frames.BodyBind = BodyFrame(positions) ?? facing;
        SegmentMasses(description, frames.SegmentMass);
        float leg = LegLength(positions, HumanBodyBone.LeftUpperLeg, HumanBodyBone.LeftLowerLeg, HumanBodyBone.LeftFoot);
        frames.Floor = new Float3(0f, Floor(positions, leg), 0f);
        float height = CenterOfMass(positions, frames.SegmentMass).Y - frames.Floor.Y;
        frames.Scale = height > 1e-4f ? height : leg > 1e-4f ? leg : 1f;
        frames.LegLength = leg > 1e-4f ? leg : frames.Scale;
        return frames;
    }

    // The limb direction the zero turn is given for.
    private static Float3? IdealAim(HumanBodyBone bone) => bone switch
    {
        HumanBodyBone.LeftUpperLeg or HumanBodyBone.RightUpperLeg or HumanBodyBone.LeftLowerLeg or HumanBodyBone.RightLowerLeg => new Float3(0f, -1f, 0f),
        HumanBodyBone.LeftUpperArm or HumanBodyBone.LeftLowerArm => new Float3(-1f, 0f, 0f),
        HumanBodyBone.RightUpperArm or HumanBodyBone.RightLowerArm => new Float3(1f, 0f, 0f),
        _ => null,
    };

    // The bone a mapped bone is measured against: its skeleton parent, or its humanoid parent when the
    // skeleton hangs it off something else (a neck under the root rather than the chest).
    private static int FrameParent(Skeleton skeleton, HumanDescription description, HumanBodyBone bone, int index)
    {
        int parent = skeleton.SanitizedParentIndices[index];
        if (bone == HumanBodyBone.Hips)
            return parent;

        HumanBodyBone? human = HumanTrait.GetParentBone(bone);
        while (human is not null && !description.HasBone(human.Value))
            human = HumanTrait.GetParentBone(human.Value);
        int humanIndex = description.GetSkeletonBoneIndex(human ?? HumanBodyBone.Hips);

        int mapped = parent;
        while (mapped != Skeleton.InvalidIndex && !IsMapped(description, mapped))
            mapped = skeleton.SanitizedParentIndices[mapped];
        return mapped == humanIndex ? parent : humanIndex;
    }

    private static bool IsMapped(HumanDescription description, int index)
    {
        foreach (HumanBodyBone bone in description.MappedBones)
            if (description.GetSkeletonBoneIndex(bone) == index)
                return true;
        return false;
    }

    // Each segment's share of the body mass. A missing segment's mass goes to the nearest present one above it,
    // which now spans it, so the same body has the same centre of mass whichever optional bones it maps.
    private static void SegmentMasses(HumanDescription description, float[] mass)
    {
        Array.Clear(mass);
        for (int b = 0; b < s_mass.Length; b++)
        {
            HumanBodyBone? bone = (HumanBodyBone)b;
            while (bone is not null && !description.HasBone(bone.Value))
                bone = HumanTrait.GetParentBone(bone.Value);
            mass[(int)(bone ?? HumanBodyBone.Hips)] += s_mass[b];
        }
    }

    // The floor height. The origin when the feet stand on or just above it, else the lowest foot joint.
    private static float Floor(DescriptionPositions positions, float legLength)
    {
        float lowest = float.MaxValue;
        foreach (HumanBodyBone bone in new[] { HumanBodyBone.LeftFoot, HumanBodyBone.RightFoot, HumanBodyBone.LeftToes, HumanBodyBone.RightToes })
            if (positions.Has(bone))
                lowest = MathF.Min(lowest, positions.Get(bone).Y);
        return lowest < 0f || lowest > 0.25f * legLength ? lowest : 0f;
    }

    private static float LegLength(DescriptionPositions positions, HumanBodyBone upper, HumanBodyBone lower, HumanBodyBone foot)
        => Float3.Distance(positions.Get(upper), positions.Get(lower)) + Float3.Distance(positions.Get(lower), positions.Get(foot));

    /// <summary>
    /// The body frame of a pose: X right, Y up, Z forward. Right is the hip line plus the shoulder line, up
    /// runs from between the hips to between the upper arms. Null when the rig is degenerate.
    /// </summary>
    public static Quaternion? BodyFrame<T>(in T pos) where T : struct, IBonePositions
    {
        Float3 leftLeg = pos.Get(HumanBodyBone.LeftUpperLeg), rightLeg = pos.Get(HumanBodyBone.RightUpperLeg);
        Float3 leftArm = pos.Get(HumanBodyBone.LeftUpperArm), rightArm = pos.Get(HumanBodyBone.RightUpperArm);
        Float3 right = (rightLeg - leftLeg) + (rightArm - leftArm);
        Float3 up = (leftArm + rightArm - leftLeg - rightLeg) * 0.5f;
        if (Float3.Length(up) < 1e-6f)
            return null;
        Float3 y = Float3.Normalize(up);
        Float3 forward = Float3.Cross(right, y);
        if (Float3.Length(forward) < 1e-6f)
            return null;
        Float3 z = Float3.Normalize(forward);
        return Quaternion.Normalize(Quaternion.FromMatrix(new Float3x3(Float3.Cross(y, z), y, z)));
    }

    /// <summary>The mass weighted centre of the body segments. Each segment's mass sits midway down it.</summary>
    public static Float3 CenterOfMass<T>(in T pos, float[] mass) where T : struct, IBonePositions
    {
        Float3 sum = Float3.Zero;
        float total = 0f;
        for (int b = 0; b < mass.Length; b++)
        {
            var bone = (HumanBodyBone)b;
            if (mass[b] <= 0f || !pos.Has(bone))
                continue;
            sum += SegmentCenter(pos, bone) * mass[b];
            total += mass[b];
        }
        return total > 0f ? sum / total : Float3.Zero;
    }

    private static Float3 SegmentCenter<T>(in T pos, HumanBodyBone bone) where T : struct, IBonePositions
    {
        switch (bone)
        {
            case HumanBodyBone.Hips:
                return (pos.Get(HumanBodyBone.LeftUpperLeg) + pos.Get(HumanBodyBone.RightUpperLeg) + pos.Get(HumanBodyBone.Spine)) / 3f;
            case HumanBodyBone.UpperChest:
            {
                Float3 sum = pos.Get(bone) + pos.Get(pos.Has(HumanBodyBone.Neck) ? HumanBodyBone.Neck : HumanBodyBone.Head);
                int count = 2;
                if (pos.Has(HumanBodyBone.LeftShoulder))
                {
                    sum += pos.Get(HumanBodyBone.LeftShoulder);
                    count++;
                }
                if (pos.Has(HumanBodyBone.RightShoulder))
                {
                    sum += pos.Get(HumanBodyBone.RightShoulder);
                    count++;
                }
                return sum / count;
            }
        }

        HumanBodyBone? end = bone switch
        {
            HumanBodyBone.Spine => NextPresent(pos, HumanBodyBone.Chest, HumanBodyBone.UpperChest, HumanBodyBone.Neck, HumanBodyBone.Head),
            HumanBodyBone.Chest => NextPresent(pos, HumanBodyBone.UpperChest, HumanBodyBone.Neck, HumanBodyBone.Head),
            HumanBodyBone.Neck => HumanBodyBone.Head,
            HumanBodyBone.LeftShoulder => HumanBodyBone.LeftUpperArm,
            HumanBodyBone.RightShoulder => HumanBodyBone.RightUpperArm,
            HumanBodyBone.LeftUpperArm or HumanBodyBone.RightUpperArm or HumanBodyBone.LeftLowerArm or HumanBodyBone.RightLowerArm
                or HumanBodyBone.LeftUpperLeg or HumanBodyBone.RightUpperLeg or HumanBodyBone.LeftLowerLeg or HumanBodyBone.RightLowerLeg => NextInLimb(bone),
            _ => null,
        };
        return end is { } e ? (pos.Get(bone) + pos.Get(e)) * 0.5f : pos.Get(bone);
    }

    private static HumanBodyBone? NextPresent<T>(in T pos, HumanBodyBone a, HumanBodyBone b, HumanBodyBone c, HumanBodyBone d = HumanBodyBone.Head) where T : struct, IBonePositions
    {
        if (pos.Has(a)) return a;
        if (pos.Has(b)) return b;
        if (pos.Has(c)) return c;
        return pos.Has(d) ? d : null;
    }

    private static HumanBodyBone NextInLimb(HumanBodyBone bone) => bone switch
    {
        HumanBodyBone.LeftUpperArm => HumanBodyBone.LeftLowerArm,
        HumanBodyBone.RightUpperArm => HumanBodyBone.RightLowerArm,
        HumanBodyBone.LeftLowerArm => HumanBodyBone.LeftHand,
        HumanBodyBone.RightLowerArm => HumanBodyBone.RightHand,
        HumanBodyBone.LeftUpperLeg => HumanBodyBone.LeftLowerLeg,
        HumanBodyBone.RightUpperLeg => HumanBodyBone.RightLowerLeg,
        HumanBodyBone.LeftLowerLeg => HumanBodyBone.LeftFoot,
        _ => HumanBodyBone.RightFoot,
    };

    private static HumanBodyBone? NextPresent(HumanDescription description, params HumanBodyBone[] chain)
    {
        foreach (HumanBodyBone bone in chain)
            if (description.HasBone(bone))
                return bone;
        return null;
    }

    // The next joint down the chain a bone aims at.
    private static HumanBodyBone? ChildJoint(HumanDescription description, HumanBodyBone bone) => bone switch
    {
        HumanBodyBone.Hips => HumanBodyBone.Spine,
        HumanBodyBone.Spine => NextPresent(description, HumanBodyBone.Chest, HumanBodyBone.UpperChest, HumanBodyBone.Neck, HumanBodyBone.Head),
        HumanBodyBone.Chest => NextPresent(description, HumanBodyBone.UpperChest, HumanBodyBone.Neck, HumanBodyBone.Head),
        HumanBodyBone.UpperChest => NextPresent(description, HumanBodyBone.Neck, HumanBodyBone.Head),
        HumanBodyBone.Neck => HumanBodyBone.Head,
        HumanBodyBone.LeftShoulder => HumanBodyBone.LeftUpperArm,
        HumanBodyBone.RightShoulder => HumanBodyBone.RightUpperArm,
        HumanBodyBone.LeftUpperArm or HumanBodyBone.RightUpperArm or HumanBodyBone.LeftLowerArm or HumanBodyBone.RightLowerArm
            or HumanBodyBone.LeftUpperLeg or HumanBodyBone.RightUpperLeg or HumanBodyBone.LeftLowerLeg or HumanBodyBone.RightLowerLeg => NextInLimb(bone),
        _ => FingerChild(description, bone),
    };

    private static HumanBodyBone? FingerChild(HumanDescription description, HumanBodyBone bone)
    {
        string name = bone.ToString();
        if (name.EndsWith("Proximal", StringComparison.Ordinal))
            return NextPresent(description, Enum.Parse<HumanBodyBone>(name[..^8] + "Intermediate"), Enum.Parse<HumanBodyBone>(name[..^8] + "Distal"));
        if (name.EndsWith("Intermediate", StringComparison.Ordinal))
            return NextPresent(description, Enum.Parse<HumanBodyBone>(name[..^12] + "Distal"));
        return null;
    }

    private static Float3 Aim(HumanDescription description, HumanBodyBone bone, HumanTrait.BoneSpec spec, Quaternion facing, DescriptionPositions positions)
    {
        if (spec.Aim == HumanTrait.AimKind.Fixed)
            return facing * spec.FixedAim;

        Float3 Pos(HumanBodyBone b) => positions.Get(b);
        if (spec.Aim == HumanTrait.AimKind.Child && ChildJoint(description, bone) is { } child && Float3.Length(Pos(child) - Pos(bone)) > 1e-6f)
            return Float3.Normalize(Pos(child) - Pos(bone));

        HumanBodyBone? parent = HumanTrait.GetParentBone(bone);
        while (parent is not null && !description.HasBone(parent.Value))
            parent = HumanTrait.GetParentBone(parent.Value);
        if (parent is { } p && Float3.Length(Pos(bone) - Pos(p)) > 1e-6f)
            return Float3.Normalize(Pos(bone) - Pos(p));
        return facing * new Float3(0f, 0f, 1f);
    }

    // X along the aim, Y is the reference crossed with the aim. When the aim runs within 30 degrees of the
    // reference, Y is taken from the parent's Y axis instead, so the frame still turns with the limb.
    private static Quaternion AxisFrame(Float3 aim, Float3 reference, Float3? fallback)
    {
        Float3 y = Float3.Cross(reference, aim);
        if (Float3.Length(y) < 0.5f && fallback is { } parentY && Float3.Length(parentY - aim * Float3.Dot(parentY, aim)) > 1e-3f)
            y = parentY - aim * Float3.Dot(parentY, aim);
        if (Float3.Length(y) < 1e-6f)
            y = Float3.Cross(TransformOps.AnyPerpendicular(aim), aim);
        y = Float3.Normalize(y);
        return Quaternion.Normalize(Quaternion.FromMatrix(new Float3x3(aim, y, Float3.Cross(aim, y))));
    }

    // The yaw that turns +Z onto the rig's forward.
    private static Quaternion Facing(DescriptionPositions positions)
    {
        if (BodyFrame(positions) is not { } body)
            return Quaternion.Identity;
        Float3 forward = body * new Float3(0f, 0f, 1f);
        float yaw = MathF.Atan2(forward.X, forward.Z);
        return Quaternion.AxisAngle(s_up, yaw);
    }

    private static (Quaternion[] World, Float3[] Position) ModelSpace(Skeleton skeleton, Quaternion[] local)
    {
        var world = new Transform3D[skeleton.BoneCount];
        int[] parents = skeleton.SanitizedParentIndices;
        foreach (int index in skeleton.EvaluationOrder)
        {
            Transform3D bind = skeleton.GetBoneParentSpaceTransform(index);
            var transform = new Transform3D(bind.position, local[index], bind.scale);
            int parent = parents[index];
            world[index] = parent == Skeleton.InvalidIndex ? transform : TransformOps.Combine(world[parent], transform);
        }
        return (world.Select(t => Quaternion.Normalize(t.rotation)).ToArray(), world.Select(t => t.position).ToArray());
    }

    // The reference pose with arms that hang down or bend straightened out sideways and curled fingers
    // straightened, so every rig is measured in a T pose.
    private static Quaternion[] TPose(Skeleton skeleton, HumanDescription description)
    {
        var local = new Quaternion[skeleton.BoneCount];
        for (int i = 0; i < local.Length; i++)
            local[i] = skeleton.GetBoneParentSpaceTransform(i).rotation;

        (Quaternion[] world, Float3[] position) = ModelSpace(skeleton, local);
        Quaternion facing = Facing(new DescriptionPositions(description, position));

        foreach ((HumanBodyBone upper, HumanBodyBone lower, HumanBodyBone hand, float side) in new[]
        {
            (HumanBodyBone.LeftUpperArm, HumanBodyBone.LeftLowerArm, HumanBodyBone.LeftHand, -1f),
            (HumanBodyBone.RightUpperArm, HumanBodyBone.RightLowerArm, HumanBodyBone.RightHand, 1f),
        })
        {
            Float3 lateral = facing * new Float3(side, 0f, 0f);
            foreach ((HumanBodyBone from, HumanBodyBone to) in new[] { (upper, lower), (lower, hand) })
            {
                (world, position) = ModelSpace(skeleton, local);
                int index = description.GetSkeletonBoneIndex(from);
                Float3 aim = position[description.GetSkeletonBoneIndex(to)] - position[index];
                if (Float3.Length(aim) < 1e-6f)
                    continue;
                aim = Float3.Normalize(aim);
                if (MathF.Acos(Math.Clamp(Float3.Dot(aim, lateral), -1f, 1f)) * 180f / MathF.PI <= Tolerance)
                    continue;

                Turn(skeleton, local, world, index, aim, lateral);
            }
        }

        // A curled phalanx is turned in line with the bone before it: the proximal one with a metacarpal
        // that sits between it and the hand, the intermediate one with the proximal one.
        foreach (HumanBodyBone middle in HumanTrait.AllBones)
        {
            string name = middle.ToString();
            if (!name.EndsWith("Intermediate", StringComparison.Ordinal))
                continue;
            HumanBodyBone first = Enum.Parse<HumanBodyBone>(name[..^12] + "Proximal");
            HumanBodyBone last = Enum.Parse<HumanBodyBone>(name[..^12] + "Distal");
            HumanBodyBone hand = name.StartsWith("Left", StringComparison.Ordinal) ? HumanBodyBone.LeftHand : HumanBodyBone.RightHand;
            if (!description.HasBone(first) || !description.HasBone(middle))
                continue;

            int firstIndex = description.GetSkeletonBoneIndex(first);
            int metacarpal = skeleton.SanitizedParentIndices[firstIndex];
            if (metacarpal != Skeleton.InvalidIndex && metacarpal != description.GetSkeletonBoneIndex(hand))
                Straighten(skeleton, local, metacarpal, firstIndex, description.GetSkeletonBoneIndex(middle));
            if (description.HasBone(last))
                Straighten(skeleton, local, firstIndex, description.GetSkeletonBoneIndex(middle), description.GetSkeletonBoneIndex(last));
        }
        return local;
    }

    // Turns the bone at joint so the segment it starts points the way the segment ending at it does.
    private static void Straighten(Skeleton skeleton, Quaternion[] local, int from, int joint, int to)
    {
        (Quaternion[] world, Float3[] position) = ModelSpace(skeleton, local);
        Float3 along = position[joint] - position[from];
        Float3 aim = position[to] - position[joint];
        if (Float3.Length(along) < 1e-6f || Float3.Length(aim) < 1e-6f)
            return;
        along = Float3.Normalize(along);
        aim = Float3.Normalize(aim);
        if (MathF.Acos(Math.Clamp(Float3.Dot(aim, along), -1f, 1f)) * 180f / MathF.PI > Tolerance)
            Turn(skeleton, local, world, joint, aim, along);
    }

    private static void Turn(Skeleton skeleton, Quaternion[] local, Quaternion[] world, int index, Float3 from, Float3 to)
    {
        int parent = skeleton.SanitizedParentIndices[index];
        Quaternion parentWorld = parent == Skeleton.InvalidIndex ? Quaternion.Identity : world[parent];
        Quaternion turned = TransformOps.FromToRotation(from, to) * world[index];
        local[index] = Quaternion.Normalize(Quaternion.Inverse(parentWorld) * turned);
    }
}
