using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The External Graph Slot node: a slot a running graph is plugged into at runtime.</summary>
public class N_ExternalGraphSlot_Tests
{
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
    public void ExternalGraph_WithAnotherSkeleton_IsRejected()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var host = new AnimationGraph();
        host.SetRoot(host.AddExternalGraphSlot("slot"));
        AnimationGraphInstance instance = host.CreateInstance(skeleton);

        var other = new AnimationGraph();
        other.SetRoot(other.AddReferencePose());
        AnimationGraphInstance otherInstance = other.CreateInstance(TestSkeletons.MakeMinimalHumanoid());

        Assert.Throws<ArgumentException>(() => instance.SetExternalGraph("slot", otherInstance));
    }

    [Fact]
    public void AGraphInASlot_KeepsTheGroundProbeItWasGiven()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid(0.5f, 0.5f);
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        Pose pose = new(skeleton);
        pose.SetToReferencePose();

        var plugged = new AnimationGraph();
        plugged.SetRoot(plugged.AddNode(new FootGroundingDefinition(plugged.AddClip(new AnimationClip(skeleton, new[] { pose, pose }, 1f)))
        {
            ProbeGround = true,
            ProbeDistance = 2f,
            ProbeRise = 1f,
        }));
        AnimationGraphInstance pluggedInstance = plugged.CreateInstance(avatar);
        pluggedInstance.Ground = new FlatGround(0.2f);

        var host = new AnimationGraph();
        host.SetRoot(host.AddExternalGraphSlot("slot"));
        AnimationGraphInstance instance = host.CreateInstance(avatar);
        instance.SetExternalGraph("slot", pluggedInstance);
        instance.Update(1f / 60f);

        instance.Pose.CalculateModelSpaceTransforms();
        int foot = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.LeftFoot);
        Assert.Equal(0.2, (double)instance.Pose.GetModelSpaceTransform(foot).position.Y, 2);
    }

    [Fact]
    public void ExternalGraphSlot_UsesFallbackUntilGraphPluggedIn()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();

        var graph = new AnimationGraph();
        int fallback = graph.AddClip(TestClips.Const(skeleton, 2f));
        int slot = graph.AddExternalGraphSlot("Action", fallback);
        graph.SetRoot(slot);

        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        instance.Update(0.016f);
        Assert.Equal(2.0, (double)instance.Pose.GetTransform(0).position.Z, 3); // fallback

        // Plug a runtime graph into the slot.
        var external = new AnimationGraph();
        int eClip = external.AddClip(TestClips.Const(skeleton, 9f));
        external.SetRoot(eClip);
        AnimationGraphInstance externalInstance = external.CreateInstance(skeleton);

        instance.SetExternalGraph("Action", externalInstance);
        instance.Update(0.016f);
        Assert.Equal(9.0, (double)instance.Pose.GetTransform(0).position.Z, 3); // external

        // Clear it again -> back to fallback.
        instance.SetExternalGraph("Action", null);
        instance.Update(0.016f);
        Assert.Equal(2.0, (double)instance.Pose.GetTransform(0).position.Z, 3);
    }

    [Fact]
    public void SetExternalGraph_UnknownSlot_Throws()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var graph = new AnimationGraph();
        int refPose = graph.AddReferencePose();
        graph.SetRoot(refPose);
        AnimationGraphInstance instance = graph.CreateInstance(skeleton);

        Assert.Throws<ArgumentException>(() => instance.SetExternalGraph("Nope", null));
    }

    [Fact]
    public void ExternalGraph_PreviewedStandalone_StartsFreshInASlot()
    {
        Skeleton skeleton = TestSkeletons.MakeChain();
        var external = new AnimationGraph();
        external.SetRoot(external.AddClip(TestClips.Ramp(skeleton)));
        AnimationGraphInstance preview = external.CreateInstance(skeleton);

        var host = new AnimationGraph();
        host.SetRoot(host.AddExternalGraphSlot("slot"));
        AnimationGraphInstance instance = host.CreateInstance(skeleton);

        for (int i = 0; i < 5; i++)
            preview.Update(0.1f);
        instance.SetExternalGraph("slot", preview);
        instance.Update(0.1f);
        Assert.Equal(1.0, (double)TestClips.RootZ(instance), 3);

        instance.SetExternalGraph("slot", null);
        instance.Update(0.1f);
        instance.SetExternalGraph("slot", preview);
        instance.Update(0.1f);
        Assert.Equal(1.0, (double)TestClips.RootZ(instance), 3);
    }
}
