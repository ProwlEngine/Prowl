using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Foot Grounding node: moving the feet and hips by the ground under them.</summary>
public class N_FootGrounding_Tests
{
    private const float Dt = 1f / 60f;

    // Ground whose height and normal depend on where the ray is cast, so each foot can stand on its own.
    private sealed class Ground : IGroundProbe
    {
        public Func<Float3, float> Height = _ => 0f;
        public Float3 Normal = new(0f, 1f, 0f);

        public bool Raycast(Float3 worldOrigin, Float3 worldDirection, float maxDistance, out Float3 worldPoint, out Float3 worldNormal)
        {
            float height = Height(worldOrigin);
            worldPoint = new Float3(worldOrigin.X, height, worldOrigin.Z);
            worldNormal = Normal;
            return worldOrigin.Y - height <= maxDistance;
        }
    }

    private static (Avatar Avatar, HumanoidRig Rig) MakeRig()
    {
        Avatar avatar = AvatarBuilder.BuildAutomatic(TestSkeletons.MakeProportionedHumanoid(0.5f, 0.5f));
        return (avatar, avatar.Humanoid!);
    }

    // The left knee bent so the left foot swings clear of the floor.
    private static Pose LeftFootLifted(Avatar avatar)
    {
        HumanoidRig rig = avatar.Humanoid!;
        Skeleton skeleton = avatar.Skeleton;
        Pose pose = HumanoidTestRig.BindPose(avatar);
        int upper = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperLeg);
        int lower = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftLowerLeg);
        Transform3D ub = skeleton.GetBoneParentSpaceTransform(upper);
        pose.SetTransform(upper, new Transform3D(ub.position, ub.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.8f), ub.scale));
        Transform3D lb = skeleton.GetBoneParentSpaceTransform(lower);
        pose.SetTransform(lower, new Transform3D(lb.position, lb.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), -1.4f), lb.scale));
        return pose;
    }

    private static AnimationGraphInstance Grounded(Avatar avatar, Pose pose, Ground ground, Action<FootGroundingDefinition>? configure = null)
    {
        var graph = new AnimationGraph();
        var node = new FootGroundingDefinition(graph.AddClip(new AnimationClip(avatar.Skeleton, new[] { pose, pose }, 1f))) { ProbeGround = true };
        configure?.Invoke(node);
        graph.SetRoot(graph.AddNode(node));

        AnimationGraphInstance instance = graph.CreateInstance(avatar);
        instance.Ground = ground;
        return instance;
    }

    [Fact]
    public void GroundAtTheFloor_LeavesThePoseAlone()
    {
        (Avatar avatar, HumanoidRig rig) = MakeRig();
        Pose pose = LeftFootLifted(avatar);
        AnimationGraphInstance instance = Grounded(avatar, pose, new Ground());
        instance.Update(Dt);

        Assert.Equal(HumanoidTestRig.ModelPos(avatar, pose, HumanBodyBone.LeftFoot).Y, HumanoidTestRig.ModelPos(avatar, instance.Pose, HumanBodyBone.LeftFoot).Y, 3);
        Assert.Equal(HumanoidTestRig.ModelPos(avatar, pose, HumanBodyBone.Hips).Y, HumanoidTestRig.ModelPos(avatar, instance.Pose, HumanBodyBone.Hips).Y, 3);
    }

    [Fact]
    public void ALiftedFootStaysLiftedAboveRaisedGround()
    {
        (Avatar avatar, HumanoidRig rig) = MakeRig();
        Pose pose = LeftFootLifted(avatar);
        AnimationGraphInstance instance = Grounded(avatar, pose, new Ground { Height = _ => 0.2f });
        instance.Update(Dt);

        Assert.Equal(HumanoidTestRig.ModelPos(avatar, pose, HumanBodyBone.LeftFoot).Y + 0.2f, HumanoidTestRig.ModelPos(avatar, instance.Pose, HumanBodyBone.LeftFoot).Y, 3);
        Assert.Equal(0.2f, HumanoidTestRig.ModelPos(avatar, instance.Pose, HumanBodyBone.RightFoot).Y, 3);
    }

    [Fact]
    public void LowerGroundUnderOneFoot_DropsTheHipsSoTheLegReaches()
    {
        (Avatar avatar, HumanoidRig rig) = MakeRig();
        AnimationGraphInstance instance = Grounded(avatar, HumanoidTestRig.BindPose(avatar), new Ground { Height = origin => origin.X < 0f ? -0.2f : 0f });
        instance.Update(Dt);

        Assert.Equal(-0.2f, HumanoidTestRig.ModelPos(avatar, instance.Pose, HumanBodyBone.LeftFoot).Y, 3);
        Assert.Equal(0f, HumanoidTestRig.ModelPos(avatar, instance.Pose, HumanBodyBone.RightFoot).Y, 3);
        Assert.Equal(0.8f, HumanoidTestRig.ModelPos(avatar, instance.Pose, HumanBodyBone.Hips).Y, 3);
    }

    [Fact]
    public void WithoutHipsAdjustment_TheHipsStay()
    {
        (Avatar avatar, HumanoidRig rig) = MakeRig();
        AnimationGraphInstance instance = Grounded(avatar, HumanoidTestRig.BindPose(avatar), new Ground { Height = origin => origin.X < 0f ? -0.2f : 0f },
            node => node.AdjustHips = false);
        instance.Update(Dt);

        Assert.Equal(1f, HumanoidTestRig.ModelPos(avatar, instance.Pose, HumanBodyBone.Hips).Y, 3);
    }

    [Fact]
    public void GroundPastTheStepLimit_IsIgnored()
    {
        (Avatar avatar, HumanoidRig rig) = MakeRig();
        AnimationGraphInstance instance = Grounded(avatar, HumanoidTestRig.BindPose(avatar), new Ground { Height = _ => -0.8f }, node => node.MaxStepDown = 0.5f);
        instance.Update(Dt);

        Assert.Equal(0f, HumanoidTestRig.ModelPos(avatar, instance.Pose, HumanBodyBone.LeftFoot).Y, 3);
    }

    [Fact]
    public void APlantedFootTiltsOntoASlope_ASwingingFootDoesNot()
    {
        (Avatar avatar, HumanoidRig rig) = MakeRig();
        Pose pose = LeftFootLifted(avatar);
        Float3 slope = Quaternion.AxisAngle(new Float3(0f, 0f, 1f), 20f * Maths.Deg2Rad) * new Float3(0f, 1f, 0f);
        AnimationGraphInstance instance = Grounded(avatar, pose, new Ground { Normal = slope });
        instance.Update(Dt);

        Float3 plantedUp = HumanoidTestRig.ModelRot(avatar, instance.Pose, HumanBodyBone.RightFoot) * new Float3(0f, 1f, 0f);
        Assert.True(Float3.Dot(plantedUp, slope) > 0.999f);

        pose.CalculateModelSpaceTransforms();
        Quaternion animated = pose.GetModelSpaceTransform(rig.GetSkeletonBoneIndex(HumanBodyBone.LeftFoot)).rotation;
        Quaternion swinging = HumanoidTestRig.ModelRot(avatar, instance.Pose, HumanBodyBone.LeftFoot);
        Assert.True(MathF.Abs(Quaternion.Dot(animated, swinging)) > 0.9999f);
    }

    [Fact]
    public void ASteepSlope_TiltsNoFurtherThanTheLimit()
    {
        (Avatar avatar, HumanoidRig rig) = MakeRig();
        Float3 slope = Quaternion.AxisAngle(new Float3(0f, 0f, 1f), 60f * Maths.Deg2Rad) * new Float3(0f, 1f, 0f);
        AnimationGraphInstance instance = Grounded(avatar, HumanoidTestRig.BindPose(avatar), new Ground { Normal = slope }, node => node.MaxFootAngle = 30f);
        instance.Update(Dt);

        Float3 up = HumanoidTestRig.ModelRot(avatar, instance.Pose, HumanBodyBone.RightFoot) * new Float3(0f, 1f, 0f);
        Assert.Equal(30f, MathF.Acos(Maths.Clamp(up.Y, -1f, 1f)) * Maths.Rad2Deg, 1);
    }

    [Fact]
    public void AChangeInGround_EasesInRatherThanSnapping()
    {
        (Avatar avatar, HumanoidRig rig) = MakeRig();
        var ground = new Ground();
        AnimationGraphInstance instance = Grounded(avatar, HumanoidTestRig.BindPose(avatar), ground, node => node.FootSmoothing = node.HipsSmoothing = 0.1f);
        instance.Update(Dt);

        ground.Height = _ => 0.2f;
        instance.Update(Dt);

        float y = HumanoidTestRig.ModelPos(avatar, instance.Pose, HumanBodyBone.LeftFoot).Y;
        Assert.InRange(y, 0.001f, 0.199f);
    }

    [Fact]
    public void WiredHeights_AreWorldSpace()
    {
        (Avatar avatar, HumanoidRig rig) = MakeRig();
        Pose pose = LeftFootLifted(avatar);

        var graph = new AnimationGraph();
        int groundY = graph.AddFloatParameter("GroundY", 0.55f);
        graph.SetRoot(graph.AddFootGrounding(graph.AddClip(new AnimationClip(avatar.Skeleton, new[] { pose, pose }, 1f)), groundY, groundY));
        AnimationGraphInstance instance = graph.CreateInstance(avatar);

        // The character stands 0.5 up in the world, so world height 0.55 is 0.05 in character space.
        var world = new Transform3D(new Float3(3f, 0.5f, -2f), Quaternion.AxisAngle(new Float3(0f, 1f, 0f), 1f), Float3.One);
        instance.Update(Dt, world);

        Assert.Equal(HumanoidTestRig.ModelPos(avatar, pose, HumanBodyBone.LeftFoot).Y + 0.05f, HumanoidTestRig.ModelPos(avatar, instance.Pose, HumanBodyBone.LeftFoot).Y, 3);
    }
}
