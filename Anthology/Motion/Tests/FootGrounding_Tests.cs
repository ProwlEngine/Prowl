using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The foot grounding solver: putting an ankle on the ground at its bind height.</summary>
public class FootGrounding_Tests
{
    private const float AnkleHeight = 0.1f;

    // A standing humanoid whose ankles sit AnkleHeight above the model origin in the bind pose.
    private static (HumanoidRig Rig, Pose Pose, int Foot) MakeLiftedFootRig()
    {
        var defs = new (HumanBodyBone Bone, HumanBodyBone? Parent, Float3 LocalPos)[]
        {
            (HumanBodyBone.Hips, null, new Float3(0f, 1f + AnkleHeight, 0f)),
            (HumanBodyBone.Spine, HumanBodyBone.Hips, new Float3(0f, 0.25f, 0f)),
            (HumanBodyBone.Head, HumanBodyBone.Spine, new Float3(0f, 0.45f, 0f)),
            (HumanBodyBone.LeftUpperArm, HumanBodyBone.Spine, new Float3(-0.18f, 0.15f, 0f)),
            (HumanBodyBone.LeftLowerArm, HumanBodyBone.LeftUpperArm, new Float3(-0.28f, 0f, 0f)),
            (HumanBodyBone.LeftHand, HumanBodyBone.LeftLowerArm, new Float3(-0.25f, 0f, 0f)),
            (HumanBodyBone.RightUpperArm, HumanBodyBone.Spine, new Float3(0.18f, 0.15f, 0f)),
            (HumanBodyBone.RightLowerArm, HumanBodyBone.RightUpperArm, new Float3(0.28f, 0f, 0f)),
            (HumanBodyBone.RightHand, HumanBodyBone.RightLowerArm, new Float3(0.25f, 0f, 0f)),
            (HumanBodyBone.LeftUpperLeg, HumanBodyBone.Hips, new Float3(-0.1f, 0f, 0f)),
            (HumanBodyBone.LeftLowerLeg, HumanBodyBone.LeftUpperLeg, new Float3(0f, -0.5f, 0f)),
            (HumanBodyBone.LeftFoot, HumanBodyBone.LeftLowerLeg, new Float3(0f, -0.5f, 0f)),
            (HumanBodyBone.RightUpperLeg, HumanBodyBone.Hips, new Float3(0.1f, 0f, 0f)),
            (HumanBodyBone.RightLowerLeg, HumanBodyBone.RightUpperLeg, new Float3(0f, -0.5f, 0f)),
            (HumanBodyBone.RightFoot, HumanBodyBone.RightLowerLeg, new Float3(0f, -0.5f, 0f)),
        };

        var ids = new StringID[defs.Length];
        var parents = new int[defs.Length];
        var bind = new Transform3D[defs.Length];
        for (int i = 0; i < defs.Length; i++)
        {
            ids[i] = new StringID(defs[i].Bone.ToString());
            parents[i] = defs[i].Parent is { } p ? Array.FindIndex(defs, d => d.Bone == p) : Skeleton.InvalidIndex;
            bind[i] = new Transform3D(defs[i].LocalPos, Quaternion.Identity, Float3.One);
        }

        var skeleton = new Skeleton(ids, parents, bind);
        HumanoidRig rig = AvatarBuilder.BuildAutomatic(skeleton).Humanoid!;

        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        int upper = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperLeg);
        int lower = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftLowerLeg);
        pose.SetTransform(upper, new Transform3D(bind[upper].position, Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.8f), Float3.One));
        pose.SetTransform(lower, new Transform3D(bind[lower].position, Quaternion.AxisAngle(new Float3(1f, 0f, 0f), -1.4f), Float3.One));
        return (rig, pose, rig.GetSkeletonBoneIndex(HumanBodyBone.LeftFoot));
    }

    [Fact]
    public void Ground_KeepsTheAnkleItsBindHeightAboveTheGround()
    {
        var (rig, pose, foot) = MakeLiftedFootRig();
        Assert.True(pose.GetModelSpaceTransform(foot).position.Y > 0.2f);

        FootGrounding.Ground(pose, rig, HumanGoal.LeftFoot, groundY: 0.05f, weight: 1f);

        Assert.Equal(0.05f + AnkleHeight, pose.GetModelSpaceTransform(foot).position.Y, 3);
    }

    [Fact]
    public void Ground_KeepsTheAnimatedFootOrientationOnFlatGround()
    {
        var (rig, pose, foot) = MakeLiftedFootRig();
        Quaternion animated = pose.GetModelSpaceTransform(foot).rotation;

        FootGrounding.Ground(pose, rig, HumanGoal.LeftFoot, groundY: 0.05f, weight: 1f);

        Assert.True(Quaternion.Angle(animated, pose.GetModelSpaceTransform(foot).rotation) < 1e-3f);
    }

    [Fact]
    public void Ground_OnASlopeTiltsTheFootOntoTheNormal()
    {
        var (rig, pose, foot) = MakeLiftedFootRig();
        Quaternion animated = pose.GetModelSpaceTransform(foot).rotation;
        Float3 footPos = pose.GetModelSpaceTransform(foot).position;
        Quaternion tilt = Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.3f);
        Float3 normal = tilt * new Float3(0f, 1f, 0f);
        var point = new Float3(footPos.X, 0.05f, footPos.Z);

        FootGrounding.Ground(pose, rig, HumanGoal.LeftFoot, point, normal, 1f);

        Transform3D grounded = pose.GetModelSpaceTransform(foot);
        Assert.True(Quaternion.Angle(tilt * animated, grounded.rotation) < 1e-3f);
        Assert.True(Float3.Distance(point + normal * AnkleHeight, grounded.position) < 1e-3f, $"ankle at {grounded.position}");
    }

    [Fact]
    public void Ground_PullsLiftedFootToTheGround()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid(0.5f, 0.5f);
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        var rig = avatar.Humanoid!;

        // Bend the left knee so the foot lifts off the ground (still reachable back down).
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        int upper = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperLeg);
        int lower = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftLowerLeg);
        Transform3D ub = skeleton.GetBoneParentSpaceTransform(upper);
        pose.SetTransform(upper, new Transform3D(ub.position, ub.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.8f), ub.scale));
        Transform3D lb = skeleton.GetBoneParentSpaceTransform(lower);
        pose.SetTransform(lower, new Transform3D(lb.position, lb.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), -1.4f), lb.scale));
        pose.CalculateModelSpaceTransforms();

        int leftFoot = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftFoot);
        float liftedY = pose.GetModelSpaceTransform(leftFoot).position.Y;
        Assert.True(liftedY > 0.1f); // it actually lifted

        FootGrounding.Ground(pose, rig, HumanGoal.LeftFoot, groundY: 0f, weight: 1f);
        pose.CalculateModelSpaceTransforms();

        Assert.Equal(0.0, (double)pose.GetModelSpaceTransform(leftFoot).position.Y, 2);
    }
}
