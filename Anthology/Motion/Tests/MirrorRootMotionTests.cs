using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class MirrorRootMotionTests
{
    private static readonly Float3 Up = new(0f, 1f, 0f);
    private static readonly Float3 Forward = new(0f, 0f, 1f);

    private static (Avatar Avatar, Float3 Left, Float3 Forward) MakeCharacter(float facingDegrees)
    {
        Quaternion facing = Quaternion.AxisAngle(Up, facingDegrees * MathF.PI / 180f);
        Avatar avatar = new HumanoidTestRig { ArmatureRotation = facing }.BuildAvatar();
        Skeleton skeleton = avatar.Skeleton;
        Float3 leftLeg = skeleton.GetBoneModelSpaceTransform(HumanoidTestRig.Index(avatar, HumanBodyBone.LeftUpperLeg)).position;
        Float3 rightLeg = skeleton.GetBoneModelSpaceTransform(HumanoidTestRig.Index(avatar, HumanBodyBone.RightUpperLeg)).position;
        return (avatar, Float3.Normalize(leftLeg - rightLeg), facing * Forward);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(90f)]
    [InlineData(-150f)]
    [InlineData(40f)]
    public void SidestepAndTurnLeft_BecomesSidestepAndTurnRight(float facingDegrees)
    {
        var (avatar, left, forward) = MakeCharacter(facingDegrees);
        Float3 turnAxis = Float3.Normalize(Float3.Cross(forward, left));
        Quaternion leftTurn = Quaternion.AxisAngle(turnAxis, 30f * MathF.PI / 180f);
        Assert.True(Float3.Dot(leftTurn * forward, left) > 0.49f);

        var delta = new Transform3D(left * 0.5f + forward * 0.2f + Up * 0.05f, leftTurn, new Float3(1f, 2f, 3f));
        Transform3D mirrored = PoseMirror.MirrorRootMotion(avatar, delta);

        Assert.Equal(-0.5, (double)Float3.Dot(mirrored.position, left), 4);
        Assert.Equal(0.2, (double)Float3.Dot(mirrored.position, forward), 4);
        Assert.Equal(0.05, (double)Float3.Dot(mirrored.position, Up), 4);

        Float3 turned = mirrored.rotation * forward;
        Assert.Equal(-0.5, (double)Float3.Dot(turned, left), 4);
        Assert.Equal((double)MathF.Cos(30f * MathF.PI / 180f), (double)Float3.Dot(turned, forward), 4);
        Assert.Equal(1.0, (double)Float3.Dot(mirrored.rotation * Up, Up), 4);

        Assert.Equal(delta.scale, mirrored.scale);
    }

    [Fact]
    public void MirroringTwice_RestoresTheDelta()
    {
        var (avatar, _, _) = MakeCharacter(25f);
        var delta = new Transform3D(new Float3(0.3f, -0.1f, 0.7f), Quaternion.Normalize(new Quaternion(0.1f, 0.4f, -0.2f, 0.9f)), Float3.One);

        Transform3D twice = PoseMirror.MirrorRootMotion(avatar, PoseMirror.MirrorRootMotion(avatar, delta));

        Assert.True(Float3.Distance(delta.position, twice.position) < 1e-5f);
        Assert.True(MathF.Abs(Quaternion.Dot(delta.rotation, twice.rotation)) > 0.99999f);
    }

    [Fact]
    public void MirroredPose_AndMirroredRootMotion_AgreeOnTheTurn()
    {
        var (avatar, left, forward) = MakeCharacter(facingDegrees: 0f);
        Quaternion leftTurn = Quaternion.AxisAngle(Float3.Normalize(Float3.Cross(forward, left)), 0.5f);
        int hips = HumanoidTestRig.Index(avatar, HumanBodyBone.Hips);

        var source = new Pose(avatar.Skeleton);
        source.SetToReferencePose();
        Transform3D hipsLocal = source.GetTransform(hips);
        source.SetTransform(hips, new Transform3D(hipsLocal.position, leftTurn * hipsLocal.rotation, hipsLocal.scale));
        var mirrored = new Pose(avatar.Skeleton);
        PoseMirror.Apply(avatar, source, mirrored);

        Quaternion poseTurn = mirrored.GetModelSpaceTransform(hips).rotation * Quaternion.Inverse(avatar.Skeleton.GetBoneModelSpaceTransform(hips).rotation);
        Quaternion motionTurn = PoseMirror.MirrorRootMotion(avatar, new Transform3D(Float3.Zero, leftTurn, Float3.One)).rotation;
        Assert.True(HumanoidTestRig.AngleDeg(poseTurn, motionTurn) < 0.5f, $"pose turn {poseTurn}, root motion turn {motionTurn}");
    }
}
