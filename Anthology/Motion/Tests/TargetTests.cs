using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class TargetTests
{
    [Fact]
    public void World_IsSetAndNotABoneTarget()
    {
        var t = Target.FromWorld(Transform3D.Identity);
        Assert.True(t.IsSet);
        Assert.False(t.IsBoneTarget);
    }

    [Fact]
    public void Bone_IsABoneTarget()
    {
        var t = Target.FromBone(2);
        Assert.True(t.IsBoneTarget);
        Assert.Equal(2, t.BoneIndex);
    }

    [Fact]
    public void World_ResolvesToItsOwnTransform()
    {
        var world = new Transform3D(new Float3(1f, 2f, 3f), Quaternion.Identity, Float3.One);
        var t = Target.FromWorld(world);
        Assert.True(t.TryGetTransform(new Pose(TestSkeletons.MakeChain()), out var x));
        Assert.Equal(2.0, (double)x.position.Y, 4);
    }

    [Fact]
    public void Bone_ResolvesAgainstPoseModelSpace()
    {
        var s = TestSkeletons.MakeChain();
        var pose = new Pose(s);
        pose.SetToReferencePose();
        pose.CalculateModelSpaceTransforms();

        var t = Target.FromBone(2);
        Assert.True(t.TryGetTransform(pose, out var x));
        Assert.Equal(2.0, (double)x.position.Y, 4);   // Knee model-space Y
    }
}
