using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The Look At node: turning the spine, head and eyes toward a target.</summary>
public class N_LookAt_Tests
{
    private static readonly Float3 Forward = new(0f, 0f, 1f);

    internal static Avatar MakeAvatar()
        => new HumanoidTestRig { Chest = false, UpperChest = false, Neck = false, Shoulders = false }.BuildAvatar();

    private static float YawDegrees(Float3 v) => MathF.Atan2(v.X, v.Z) * 180f / MathF.PI;

    [Fact]
    public void LookAtNode_ResolvesATargetTypedInput()
    {
        Avatar avatar = LookAt_Tests.MakeAvatar();
        int head = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Head);

        var g = new AnimationGraph();
        int target = g.AddTargetParameter("T");
        g.SetRoot(g.AddLookAt(g.AddReferencePose(), target, clamp: 0f, body: 0f, head: 1f, eyes: 0f));

        AnimationGraphInstance instance = g.CreateInstance(avatar);
        instance.SetTarget("T", Target.FromWorld(new Transform3D(new Float3(10f, 1.7f, 0f), Quaternion.Identity, Float3.One)));
        instance.Update(0.016f);

        Float3 forward = instance.Pose.GetModelSpaceTransform(head).rotation * new Float3(0f, 0f, 1f);
        Assert.True(forward.X > 0.99f, $"head forward {forward} does not face the target");
    }

    [Fact]
    public void LookAtNode_UnsetTargetLeavesTheHeadAlone()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid();
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        int head = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Head);

        var g = new AnimationGraph();
        int target = g.AddTargetParameter("T");
        g.SetRoot(g.AddLookAt(g.AddReferencePose(), target, clamp: 0f, body: 0f, head: 1f, eyes: 0f));

        AnimationGraphInstance instance = g.CreateInstance(avatar);
        instance.Update(0.016f);

        Assert.Equal(skeleton.GetBoneParentSpaceTransform(head).rotation, instance.Pose.GetTransform(head).rotation);
    }

    [Fact]
    public void LookAtNode_UsesTheOverallWeight()
    {
        Avatar avatar = MakeAvatar();
        int head = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Head);

        var g = new AnimationGraph();
        int target = g.AddVectorParameter("T", new Float3(10f, 1.7f, 0f));
        g.SetRoot(g.AddNode(new LookAtDefinition(g.AddReferencePose(), target, weight: 0.5f, clamp: 0f, body: 0f, head: 1f, eyes: 0f)));

        AnimationGraphInstance instance = g.CreateInstance(avatar);
        instance.Update(0.016f);

        Assert.Equal(45.0, (double)YawDegrees(instance.Pose.GetModelSpaceTransform(head).rotation * Forward), 1);
    }
}
