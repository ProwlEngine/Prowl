using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class MirrorAllocationTests
{
    [Fact]
    public void Apply_DoesNotAllocateAfterTheFirstCall()
    {
        Avatar avatar = new HumanoidTestRig { Fingers = true }.BuildAvatar();
        var source = new Pose(avatar.Skeleton);
        source.SetToReferencePose();
        int leftUpper = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperArm);
        Transform3D bind = source.GetTransform(leftUpper);
        source.SetTransform(leftUpper, new Transform3D(bind.position, bind.rotation * Quaternion.AxisAngle(new Float3(0f, 0f, 1f), 0.6f), bind.scale));
        var result = new Pose(avatar.Skeleton);

        PoseMirror.Apply(avatar, source, result);

        long lowest = long.MaxValue;
        for (int round = 0; round < 5; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++)
                PoseMirror.Apply(avatar, source, result);
            lowest = Math.Min(lowest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0L, lowest);
    }
}
