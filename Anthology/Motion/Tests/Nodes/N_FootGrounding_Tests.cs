using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Foot Grounding node: planting the feet on ground the host reports or the node finds.</summary>
public class N_FootGrounding_Tests
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

    private sealed class FlatGround : IGroundProbe
    {
        public FlatGround(float height) => Height = height;

        public float Height { get; }

        public bool Raycast(Float3 worldOrigin, Float3 worldDirection, float maxDistance, out Float3 worldPoint, out Float3 worldNormal)
        {
            worldPoint = new Float3(worldOrigin.X, Height, worldOrigin.Z);
            worldNormal = new Float3(0f, 1f, 0f);
            return worldOrigin.Y - Height <= maxDistance;
        }
    }

    [Fact]
    public void FootGroundingNode_PullsLiftedFootToGround()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid(0.5f, 0.5f);
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        HumanoidRig rig = avatar.Humanoid!;

        // Author a pose with the left knee bent so the foot lifts off the ground.
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        int upper = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperLeg);
        int lower = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftLowerLeg);
        Transform3D ub = skeleton.GetBoneParentSpaceTransform(upper);
        pose.SetTransform(upper, new Transform3D(ub.position, ub.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.8f), ub.scale));
        Transform3D lb = skeleton.GetBoneParentSpaceTransform(lower);
        pose.SetTransform(lower, new Transform3D(lb.position, lb.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), -1.4f), lb.scale));

        var clip = new AnimationClip(skeleton, new[] { pose, pose }, 1f);

        var graph = new AnimationGraph();
        int clipNode = graph.AddClip(clip);
        int grounded = graph.AddFootGrounding(clipNode); // ground heights default to 0, weight 1
        graph.SetRoot(grounded);

        AnimationGraphInstance instance = graph.CreateInstance(avatar);
        instance.Update(0.016f);

        instance.Pose.CalculateModelSpaceTransforms();
        int leftFoot = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftFoot);
        Assert.Equal(0.0, (double)instance.Pose.GetModelSpaceTransform(leftFoot).position.Y, 2);
    }

    [Fact]
    public void FootGroundingNode_TakesWorldSpaceGroundHeights()
    {
        var (rig, pose, foot) = MakeLiftedFootRig();
        Avatar avatar = AvatarBuilder.BuildAutomatic(rig.Skeleton);
        var clip = new AnimationClip(rig.Skeleton, new[] { pose, pose }, 1f);

        var g = new AnimationGraph();
        int groundY = g.AddFloatParameter("GroundY", 0.55f);
        int root = g.AddFootGrounding(g.AddClip(clip), groundY, groundY);
        g.SetRoot(root);
        AnimationGraphInstance instance = g.CreateInstance(avatar);

        // The character stands 0.5 up in the world, so world height 0.55 is 0.05 in model space.
        var world = new Transform3D(new Float3(3f, 0.5f, -2f), Quaternion.AxisAngle(new Float3(0f, 1f, 0f), 1f), Float3.One);
        var context = new GraphContext
        {
            DeltaTime = 0.016f,
            Skeleton = rig.Skeleton,
            Avatar = avatar,
            UpdateId = 1,
            WorldTransform = world,
            WorldTransformInverse = new Transform3D(Quaternion.Inverse(world.rotation) * -world.position, Quaternion.Inverse(world.rotation), Float3.One),
            Parameters = new[] { ParameterValue.FromFloat(0.55f) },
        };
        var node = (PoseNodeInstance)instance.GetNodeInstance(root);
        node.Update(context);

        Assert.Equal(0.05f + AnkleHeight, node.Pose.GetModelSpaceTransform(foot).position.Y, 3);
    }

    [Fact]
    public void FootGrounding_FindsTheGroundItself()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid(0.5f, 0.5f);
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        HumanoidRig rig = avatar.Humanoid!;

        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        var clip = new AnimationClip(skeleton, new[] { pose, pose }, 1f);

        var graph = new AnimationGraph();
        int clipNode = graph.AddClip(clip);
        graph.SetRoot(graph.AddNode(new FootGroundingDefinition(clipNode) { ProbeGround = true, ProbeDistance = 2f, ProbeRise = 1f }));

        AnimationGraphInstance instance = graph.CreateInstance(avatar);
        instance.Ground = new FlatGround(0.2f);
        instance.Update(0.016f);

        instance.Pose.CalculateModelSpaceTransforms();
        int leftFoot = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftFoot);
        Assert.Equal(0.2, (double)instance.Pose.GetModelSpaceTransform(leftFoot).position.Y, 2);
    }
}
