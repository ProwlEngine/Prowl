using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Mirror node: a humanoid pose swapped left for right in a graph.</summary>
public class N_Mirror_Tests
{
    [Fact]
    public void Mirror_PassesThroughWhileSwitchedOff()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid(0.5f, 0.5f);
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        HumanoidRig rig = avatar.Humanoid!;

        var pose = new Pose(skeleton);
        pose.SetToReferencePose();
        int arm = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperArm);
        Transform3D bind = skeleton.GetBoneParentSpaceTransform(arm);
        pose.SetTransform(arm, new Transform3D(bind.position, bind.rotation * Quaternion.AxisAngle(new Float3(0f, 0f, 1f), 0.9f), bind.scale));
        var clip = new AnimationClip(skeleton, new[] { pose, pose }, 1f);

        Quaternion Run(bool enabled)
        {
            var graph = new AnimationGraph();
            graph.SetRoot(graph.AddMirror(graph.AddClip(clip), graph.AddConstBool(enabled)));
            AnimationGraphInstance instance = graph.CreateInstance(avatar);
            instance.Update(0.016f);
            return instance.Pose.GetTransform(arm).rotation;
        }

        Quaternion off = Run(false);
        Quaternion on = Run(true);
        Quaternion authored = pose.GetTransform(arm).rotation;

        Assert.True(MathF.Abs(Quaternion.Dot(off, authored)) > 0.9999f);
        Assert.True(MathF.Abs(Quaternion.Dot(on, authored)) < 0.9999f);
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
    public void Mirror_SwapsFootEventsAndMirrorsRootMotion()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid();
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        var frame = new Pose(skeleton);
        frame.SetToReferencePose();
        var root = new[] { Transform3D.Identity, new Transform3D(new Float3(1f, 0f, 0f), Quaternion.AxisAngle(new Float3(0f, 1f, 0f), 0.5f), Float3.One) };
        var clip = new AnimationClip(skeleton, new[] { frame, frame }, 1f, rootMotion: new RootMotion(root, 1f), events: new AnimationEvent[] { new FootEvent(FootPhase.LeftFootDown, 0.05f) });
        var g = new AnimationGraph();
        g.SetRoot(g.AddMirror(g.AddClip(clip)));
        AnimationGraphInstance instance = g.CreateInstance(avatar);

        instance.Update(0.1f);

        Assert.True(instance.RootMotionDelta.position.X < 0f);
        Assert.True(instance.RootMotionDelta.rotation.Y < 0f);
        FootEvent foot = Assert.Single(instance.Events.FootEvents());
        Assert.Equal(FootPhase.RightFootDown, foot.Phase);
        Assert.Same(clip.Events[0], foot.Mirrored);
    }
}
