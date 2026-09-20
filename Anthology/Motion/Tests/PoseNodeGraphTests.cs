using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class PoseNodeGraphTests
{
    private static AnimationClip ConstClip(Skeleton skeleton, int bone, float z)
    {
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        pose.SetTransform(bone, new Transform3D(new Float3(0f, 0f, z), Quaternion.Identity, Float3.One));
        return new AnimationClip(skeleton, new[] { pose, pose }, 1f);
    }

    [Fact]
    public void Selector_PicksChildByInt()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int which = graph.AddIntParameter("Which");
        int a = graph.AddClip(ConstClip(skeleton, 0, 1f));
        int b = graph.AddClip(ConstClip(skeleton, 0, 2f));
        int c = graph.AddClip(ConstClip(skeleton, 0, 3f));
        int selector = graph.AddSelector(which, new[] { a, b, c });
        graph.SetRoot(selector);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.SetInt("Which", 2);
        instance.Update(0.016f);
        Assert.Equal(3.0, (double)instance.Pose.GetTransform(0).position.Z, 3);
    }

    [Fact]
    public void OverrideLayer_BlendsByWeight()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int w = graph.AddFloatParameter("W");
        int basePose = graph.AddClip(ConstClip(skeleton, 0, 0f));
        int layer = graph.AddClip(ConstClip(skeleton, 0, 8f));
        int layered = graph.AddOverrideLayer(basePose, layer, w);
        graph.SetRoot(layered);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.SetFloat("W", 0.25f);
        instance.Update(0.016f);
        Assert.Equal(2.0, (double)instance.Pose.GetTransform(0).position.Z, 3); // lerp(0,8,0.25)
    }

    [Fact]
    public void Mirror_SwapsHumanoidSides()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid();
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        var rig = avatar.Humanoid!;

        // A clip that raises the LEFT arm.
        var raised = new Pose(skeleton);
        raised.SetToReferencePose();
        int leftUpper = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperArm);
        Transform3D bind = skeleton.GetBoneParentSpaceTransform(leftUpper);
        raised.SetTransform(leftUpper, new Transform3D(bind.position, bind.rotation * Quaternion.AxisAngle(new Float3(0f, 0f, 1f), 0.6f), bind.scale));
        var clip = new AnimationClip(skeleton, new[] { raised, raised }, 1f);

        var graph = new AnimationGraph();
        int clipNode = graph.AddClip(clip);
        int mirror = graph.AddMirror(clipNode);
        graph.SetRoot(mirror);

        AnimationGraphInstance instance = graph.CreateInstance(avatar);
        instance.Update(0.016f);
        instance.Pose.CalculateModelSpaceTransforms();

        int leftHand = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftHand);
        int rightHand = rig.GetSkeletonBoneIndex(HumanBodyBone.RightHand);

        // After mirroring, the RIGHT hand should be raised (matches where the left hand was in the clip).
        var direct = new Pose(skeleton);
        clip.GetPose(0f, direct);
        direct.CalculateModelSpaceTransforms();
        float clipLeftHandY = direct.GetModelSpaceTransform(leftHand).position.Y;
        float mirroredRightHandY = instance.Pose.GetModelSpaceTransform(rightHand).position.Y;
        Assert.Equal((double)clipLeftHandY, (double)mirroredRightHandY, 2);
    }

    [Fact]
    public void TwoBoneIKNode_DrivesEndToVectorTarget()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int target = graph.AddControlParameter("Target", AnimationValueType.Vector, ParameterValue.FromVector(new Float3(1f, 1f, 0f)));
        int refPose = graph.AddReferencePose();
        int ik = graph.AddTwoBoneIK(refPose, target, upper: 0, mid: 1, end: 2);
        graph.SetRoot(ik);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);
        instance.Update(0.016f);
        instance.Pose.CalculateModelSpaceTransforms();

        Float3 end = instance.Pose.GetModelSpaceTransform(2).position;
        Assert.True(Float3.Distance(end, new Float3(1f, 1f, 0f)) < 1e-2f, $"end {end}");
    }
}
