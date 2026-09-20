using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

/// <summary>
/// A source rig with proportionally long arms must not fling a normal-armed target's arms up into the
/// air. The muscle codec already reproduces the arm direction in a proportion-independent way; only the
/// feet are pinned with IK. If hand IK were auto-applied, the long-armed source's (unreachable)
/// body-relative hand goal would force the target arm to fully extend up-and-out to reach it.
/// </summary>
public class LongArmedSourceRetargetTests
{
    // T-pose humanoid whose arm segments are scaled by armReach (1.0 = normal).
    private static Skeleton TPoseHumanoid(float armReach)
    {
        float upper = 0.18f * armReach;
        float fore = 0.28f * armReach;
        float hand = 0.25f * armReach;
        var defs = new (HumanBodyBone Bone, HumanBodyBone? Parent, Float3 Local)[]
        {
            (HumanBodyBone.Hips, null, new Float3(0f, 1f, 0f)),
            (HumanBodyBone.Spine, HumanBodyBone.Hips, new Float3(0f, 0.25f, 0f)),
            (HumanBodyBone.Head, HumanBodyBone.Spine, new Float3(0f, 0.45f, 0f)),
            (HumanBodyBone.LeftUpperArm, HumanBodyBone.Spine, new Float3(-upper, 0.15f, 0f)),
            (HumanBodyBone.LeftLowerArm, HumanBodyBone.LeftUpperArm, new Float3(-fore, 0f, 0f)),
            (HumanBodyBone.LeftHand, HumanBodyBone.LeftLowerArm, new Float3(-hand, 0f, 0f)),
            (HumanBodyBone.RightUpperArm, HumanBodyBone.Spine, new Float3(upper, 0.15f, 0f)),
            (HumanBodyBone.RightLowerArm, HumanBodyBone.RightUpperArm, new Float3(fore, 0f, 0f)),
            (HumanBodyBone.RightHand, HumanBodyBone.RightLowerArm, new Float3(hand, 0f, 0f)),
            (HumanBodyBone.LeftUpperLeg, HumanBodyBone.Hips, new Float3(-0.1f, 0f, 0f)),
            (HumanBodyBone.LeftLowerLeg, HumanBodyBone.LeftUpperLeg, new Float3(0f, -0.5f, 0f)),
            (HumanBodyBone.LeftFoot, HumanBodyBone.LeftLowerLeg, new Float3(0f, -0.5f, 0f)),
            (HumanBodyBone.RightUpperLeg, HumanBodyBone.Hips, new Float3(0.1f, 0f, 0f)),
            (HumanBodyBone.RightLowerLeg, HumanBodyBone.RightUpperLeg, new Float3(0f, -0.5f, 0f)),
            (HumanBodyBone.RightFoot, HumanBodyBone.RightLowerLeg, new Float3(0f, -0.5f, 0f)),
        };

        var indexOf = new Dictionary<HumanBodyBone, int>();
        for (int i = 0; i < defs.Length; i++) indexOf[defs[i].Bone] = i;
        var ids = new StringID[defs.Length];
        var parents = new int[defs.Length];
        var pose = new Transform3D[defs.Length];
        for (int i = 0; i < defs.Length; i++)
        {
            ids[i] = new StringID(defs[i].Bone.ToString());
            parents[i] = defs[i].Parent is { } p ? indexOf[p] : Skeleton.InvalidIndex;
            pose[i] = new Transform3D(defs[i].Local, Quaternion.Identity, Float3.One);
        }
        return new Skeleton(ids, parents, pose);
    }

    [Fact]
    public void LongArmedSourceRest_KeepsTargetArmsHorizontal()
    {
        // Source arms are twice as long as the target's; both rest in a T-pose (arms horizontal).
        Avatar source = AvatarBuilder.BuildAutomatic(TPoseHumanoid(2f));
        Avatar target = AvatarBuilder.BuildAutomatic(TPoseHumanoid(1f));
        Assert.True(source.IsHuman && target.IsHuman);

        var rest = new Pose(source.Skeleton);
        rest.SetToReferencePose();
        rest.CalculateModelSpaceTransforms();

        var human = new HumanPose();
        Retargeter.RetargetFrom(source, rest, human);
        var result = new Pose(target.Skeleton);
        Retargeter.RetargetTo(target, human, result);
        result.CalculateModelSpaceTransforms();

        int upper = target.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperArm);
        int lower = target.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.LeftLowerArm);
        Float3 u = result.GetModelSpaceTransform(upper).position;
        Float3 l = result.GetModelSpaceTransform(lower).position;
        Float3 arm = l - u;
        float len = Float3.Length(arm);
        float upFraction = len < 1e-5f ? 0f : arm.Y / len;

        // The target arm must stay roughly horizontal - it must not be flung up toward an unreachable
        // hand goal. (Before the fix it rose to ~+0.27 of its length.)
        Assert.True(upFraction < 0.2f, $"target arm rose upward (Y fraction {upFraction}); it should stay horizontal");
    }
}
