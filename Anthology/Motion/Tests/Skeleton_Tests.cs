using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>Skeletons: hierarchy queries, reference poses, levels of detail and invalid layouts.</summary>
public class Skeleton_Tests
{
    [Fact]
    public void Skeleton_ChildrenBeforeParents_DeepChainDoesNotOverflow()
    {
        const int count = 20000;
        var ids = new StringID[count];
        var parents = new int[count];
        var local = new Transform3D[count];
        for (int i = 0; i < count; i++)
        {
            ids[i] = new StringID("b" + i);
            parents[i] = i == count - 1 ? Skeleton.InvalidIndex : i + 1;
            local[i] = new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One);
        }

        var skeleton = new Skeleton(ids, parents, local);
        var pose = new Pose(skeleton);
        pose.SetToReferencePose();

        Assert.Equal(count, (double)pose.GetModelSpaceTransform(0).position.Y, 1);
    }

    [Fact]
    public void Skeleton_ParentCycle_IsInvalidAndStillEvaluates()
    {
        var ids = new[] { new StringID("a"), new StringID("b") };
        var local = new[] { Transform3D.Identity, Transform3D.Identity };
        var skeleton = new Skeleton(ids, new[] { 1, 0 }, local);

        Assert.False(skeleton.IsValid);
        Assert.Equal(1.0, (double)skeleton.GetBoneModelSpaceTransform(0).scale.X, 5);
        Assert.Equal(1.0, (double)skeleton.GetBoneModelSpaceTransform(1).scale.X, 5);
    }

    [Theory]
    [InlineData("a", "a")]
    [InlineData("Bone_94922", "Bone_633800")]
    public void Skeleton_DuplicateOrCollidingIds_AreInvalid(string first, string second)
    {
        var ids = new[] { new StringID(first), new StringID(second) };
        var skeleton = new Skeleton(ids, new[] { Skeleton.InvalidIndex, 0 }, new[] { Transform3D.Identity, Transform3D.Identity });

        Assert.True(skeleton.HasDuplicateBoneIds);
        Assert.False(skeleton.IsValid);
    }

    [Fact]
    public void LowLod_ComputesLeadingBones()
    {
        var ids = new[] { new StringID("Root"), new StringID("Hip"), new StringID("Knee") };
        var parents = new[] { Skeleton.InvalidIndex, 0, 1 };
        var local = new[]
        {
            new Transform3D(new Float3(0f, 0f, 0f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One),
        };
        var skeleton = new Skeleton(ids, parents, local, numBonesToSampleAtLowLOD: 2);
        Assert.Equal(2, skeleton.GetBoneCount(SkeletonLOD.Low));

        var pose = new Pose(skeleton);
        pose.SetToReferencePose(false);
        pose.CalculateModelSpaceTransforms(SkeletonLOD.Low);

        Assert.Equal(1.0, (double)pose.GetModelSpaceTransform(1).position.Y, 4); // hip model Y
    }

    [Fact]
    public void GetParentBoneIndex_ReturnsDirectParent()
    {
        var s = TestSkeletons.MakeChain();
        Assert.Equal(0, s.GetParentBoneIndex(1));
        Assert.Equal(Skeleton.InvalidIndex, s.GetParentBoneIndex(0));
    }

    [Fact]
    public void IsChildBoneOf_WalksFullHierarchy()
    {
        var s = TestSkeletons.MakeChain();
        Assert.True(s.IsChildBoneOf(0, 2));   // Knee is a descendant of Root
        Assert.False(s.IsChildBoneOf(2, 0));
    }

    [Fact]
    public void GetBoneIndex_FindsByIdOrReturnsInvalid()
    {
        var s = TestSkeletons.MakeChain();
        Assert.Equal(2, s.GetBoneIndex(new StringID("Knee")));
        Assert.Equal(Skeleton.InvalidIndex, s.GetBoneIndex(new StringID("DoesNotExist")));
    }

    [Fact]
    public void IsLeafBone_TrueForTipOnly()
    {
        var s = TestSkeletons.MakeChain();
        Assert.True(s.IsLeafBone(2));
        Assert.False(s.IsLeafBone(0));
    }

    [Fact]
    public void ModelSpaceReferencePose_AccumulatesDownChain()
    {
        var s = TestSkeletons.MakeChain();
        // Root(0) + Hip(+1) + Knee(+1) along Y => Knee at Y = 2 in model space.
        Assert.Equal(2.0, (double)s.GetBoneModelSpaceTransform(2).position.Y, 4);
    }
}
