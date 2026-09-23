using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

/// <summary>Synthetic skeletons/poses used across the unit tests.</summary>
internal static class TestSkeletons
{
    /// <summary>A simple 3-bone chain: Root -> Hip -> Knee, each offset +1 on Y in parent space.</summary>
    public static Skeleton MakeChain()
    {
        var ids = new[] { new StringID("Root"), new StringID("Hip"), new StringID("Knee") };
        var parents = new[] { Skeleton.InvalidIndex, 0, 1 };
        var pose = new[]
        {
            new Transform3D(new Float3(0f, 0f, 0f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One),
        };
        return new Skeleton(ids, parents, pose);
    }

    /// <summary>How left/right is written in a synthetic rig's bone names.</summary>
    public enum SideNaming { Prefix, SuffixUnderscore }

    // Minimal valid humanoid: all 15 required bones with a correct hierarchy.
    private static readonly (HumanBodyBone Bone, HumanBodyBone? Parent)[] s_minimalHumanoid =
    {
        (HumanBodyBone.Hips, null),
        (HumanBodyBone.Spine, HumanBodyBone.Hips),
        (HumanBodyBone.Head, HumanBodyBone.Spine),
        (HumanBodyBone.LeftUpperArm, HumanBodyBone.Spine),
        (HumanBodyBone.LeftLowerArm, HumanBodyBone.LeftUpperArm),
        (HumanBodyBone.LeftHand, HumanBodyBone.LeftLowerArm),
        (HumanBodyBone.RightUpperArm, HumanBodyBone.Spine),
        (HumanBodyBone.RightLowerArm, HumanBodyBone.RightUpperArm),
        (HumanBodyBone.RightHand, HumanBodyBone.RightLowerArm),
        (HumanBodyBone.LeftUpperLeg, HumanBodyBone.Hips),
        (HumanBodyBone.LeftLowerLeg, HumanBodyBone.LeftUpperLeg),
        (HumanBodyBone.LeftFoot, HumanBodyBone.LeftLowerLeg),
        (HumanBodyBone.RightUpperLeg, HumanBodyBone.Hips),
        (HumanBodyBone.RightLowerLeg, HumanBodyBone.RightUpperLeg),
        (HumanBodyBone.RightFoot, HumanBodyBone.RightLowerLeg),
    };

    /// <summary>
    /// Builds a minimal but complete humanoid skeleton. <paramref name="naming"/> controls the
    /// left/right naming convention; <paramref name="breakArmHierarchy"/> re-parents the left hand
    /// under the hips to produce a name-complete but topologically invalid rig.
    /// </summary>
    public static Skeleton MakeMinimalHumanoid(SideNaming naming = SideNaming.Prefix, bool breakArmHierarchy = false)
    {
        var defs = s_minimalHumanoid;
        var indexOf = new Dictionary<HumanBodyBone, int>();
        for (int i = 0; i < defs.Length; i++)
            indexOf[defs[i].Bone] = i;

        var ids = new StringID[defs.Length];
        var parents = new int[defs.Length];
        var pose = new Transform3D[defs.Length];

        for (int i = 0; i < defs.Length; i++)
        {
            ids[i] = new StringID(NameFor(defs[i].Bone, naming));
            pose[i] = Transform3D.Identity;

            HumanBodyBone? parent = defs[i].Parent;
            if (defs[i].Bone == HumanBodyBone.LeftHand && breakArmHierarchy)
                parent = HumanBodyBone.Hips;

            parents[i] = parent is { } p ? indexOf[p] : Skeleton.InvalidIndex;
        }

        return new Skeleton(ids, parents, pose);
    }

    /// <summary>
    /// Builds a standing humanoid with real bone offsets and the given leg segment lengths, so IK and
    /// proportion-dependent retargeting can be exercised. Uses prefix naming so the auto-mapper maps it.
    /// </summary>
    public static Skeleton MakeProportionedHumanoid(float thigh = 0.5f, float shin = 0.5f)
    {
        float hipHeight = thigh + shin;
        var defs = new (HumanBodyBone Bone, HumanBodyBone? Parent, Float3 LocalPos)[]
        {
            (HumanBodyBone.Hips, null, new Float3(0f, hipHeight, 0f)),
            (HumanBodyBone.Spine, HumanBodyBone.Hips, new Float3(0f, 0.25f, 0f)),
            (HumanBodyBone.Head, HumanBodyBone.Spine, new Float3(0f, 0.45f, 0f)),
            (HumanBodyBone.LeftUpperArm, HumanBodyBone.Spine, new Float3(-0.18f, 0.15f, 0f)),
            (HumanBodyBone.LeftLowerArm, HumanBodyBone.LeftUpperArm, new Float3(-0.28f, 0f, 0f)),
            (HumanBodyBone.LeftHand, HumanBodyBone.LeftLowerArm, new Float3(-0.25f, 0f, 0f)),
            (HumanBodyBone.RightUpperArm, HumanBodyBone.Spine, new Float3(0.18f, 0.15f, 0f)),
            (HumanBodyBone.RightLowerArm, HumanBodyBone.RightUpperArm, new Float3(0.28f, 0f, 0f)),
            (HumanBodyBone.RightHand, HumanBodyBone.RightLowerArm, new Float3(0.25f, 0f, 0f)),
            (HumanBodyBone.LeftUpperLeg, HumanBodyBone.Hips, new Float3(-0.1f, 0f, 0f)),
            (HumanBodyBone.LeftLowerLeg, HumanBodyBone.LeftUpperLeg, new Float3(0f, -thigh, 0f)),
            (HumanBodyBone.LeftFoot, HumanBodyBone.LeftLowerLeg, new Float3(0f, -shin, 0f)),
            (HumanBodyBone.RightUpperLeg, HumanBodyBone.Hips, new Float3(0.1f, 0f, 0f)),
            (HumanBodyBone.RightLowerLeg, HumanBodyBone.RightUpperLeg, new Float3(0f, -thigh, 0f)),
            (HumanBodyBone.RightFoot, HumanBodyBone.RightLowerLeg, new Float3(0f, -shin, 0f)),
        };

        var indexOf = new Dictionary<HumanBodyBone, int>();
        for (int i = 0; i < defs.Length; i++)
            indexOf[defs[i].Bone] = i;

        var ids = new StringID[defs.Length];
        var parents = new int[defs.Length];
        var pose = new Transform3D[defs.Length];
        for (int i = 0; i < defs.Length; i++)
        {
            ids[i] = new StringID(defs[i].Bone.ToString());
            parents[i] = defs[i].Parent is { } p ? indexOf[p] : Skeleton.InvalidIndex;
            pose[i] = new Transform3D(defs[i].LocalPos, Quaternion.Identity, Float3.One);
        }

        return new Skeleton(ids, parents, pose);
    }

    private static string NameFor(HumanBodyBone bone, SideNaming naming)
    {
        string name = bone.ToString();
        if (naming == SideNaming.Prefix)
            return name;

        if (name.StartsWith("Left", StringComparison.Ordinal))
            return name.Substring(4) + "_L";
        if (name.StartsWith("Right", StringComparison.Ordinal))
            return name.Substring(5) + "_R";
        return name;
    }
}
