using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class HumanPoseTests
{
    private static readonly int SpineTwist = HumanTrait.GetMuscleIndex(HumanBodyBone.Spine, MuscleAxis.X);

    [Fact]
    public void SetThenGetMuscle_RoundTrips()
    {
        var pose = new HumanPose();
        pose.SetMuscle(SpineTwist, 0.5f);
        Assert.Equal(0.5f, pose.GetMuscle(HumanBodyBone.Spine, MuscleAxis.X));
    }

    [Fact]
    public void NewPose_IsTheNeutralPose()
    {
        var pose = new HumanPose();
        Assert.All(pose.Muscles.ToArray(), m => Assert.Equal(0f, m));
        Assert.True(MathF.Abs(Quaternion.Dot(Quaternion.Identity, pose.BodyRotation)) > 0.999f);
    }

    [Fact]
    public void Reset_ClearsMusclesAndStandsTheBodyUp()
    {
        var pose = new HumanPose();
        pose.SetMuscle(SpineTwist, 0.9f);
        pose.RootTransform = new Transform3D(new Float3(1f, 2f, 3f), Quaternion.Identity, Float3.One);
        pose.Reset();
        Assert.Equal(0f, pose.GetMuscle(SpineTwist));
        Assert.Equal(1.0, (double)pose.RootTransform.position.Y, 5);
        Assert.Equal(0.0, (double)pose.RootTransform.position.Z, 5);
    }
}

public class RetargeterTests
{
    private static Avatar Humanoid() => AvatarBuilder.BuildAutomatic(ClayModelLoader.LoadHumanoidSkeleton());

    [ModelFact]
    public void RetargetFrom_EncodesReferencePoseAsNeutralSpine()
    {
        var avatar = Humanoid();
        var sourcePose = new Pose(avatar.Skeleton);
        sourcePose.SetToReferencePose();

        var human = new HumanPose();
        Retargeter.RetargetFrom(avatar, sourcePose, human);

        foreach (MuscleAxis axis in Enum.GetValues<MuscleAxis>())
            Assert.Equal(0.0, (double)human.GetMuscle(HumanBodyBone.Spine, axis), 3);
        Assert.All(human.Muscles.ToArray(), m => Assert.InRange(m, -1f, 1f));
    }

    [ModelFact]
    public void RetargetTo_ReconstructsASkeletonPose()
    {
        var avatar = Humanoid();
        var human = new HumanPose();
        var result = new Pose(avatar.Skeleton);

        Retargeter.RetargetTo(avatar, human, result);

        Assert.True(result.IsValid);
    }

    [ModelFact]
    public void RoundTrip_ReproducesBoneRotation()
    {
        // Muscle space is intentionally lossy on locked/limited axes (e.g. the elbow only bends one
        // way), so we test the upper arm, whose muscle ranges are wide and free, with a moderate
        // in-range rotation; it should round-trip closely through encode/decode.
        var avatar = Humanoid();
        var rig = avatar.Humanoid!;
        int armIndex = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperArm);

        var source = new Pose(avatar.Skeleton);
        source.SetToReferencePose();
        Transform3D bind = avatar.Skeleton.GetBoneParentSpaceTransform(armIndex);
        var rotated = new Transform3D(
            bind.position,
            bind.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.4f),
            bind.scale);
        source.SetTransform(armIndex, rotated);

        var human = new HumanPose();
        Retargeter.RetargetFrom(avatar, source, human);
        var result = new Pose(avatar.Skeleton);
        Retargeter.RetargetTo(avatar, human, result);

        Quaternion sourceRot = source.GetTransform(armIndex).rotation;
        Quaternion resultRot = result.GetTransform(armIndex).rotation;
        Assert.True(MathF.Abs(Quaternion.Dot(sourceRot, resultRot)) > 0.99f);
    }

    [ModelFact]
    public void RoundTrip_PreservesRootTranslation()
    {
        var avatar = Humanoid();
        var rig = avatar.Humanoid!;
        int hipsIndex = rig.GetSkeletonBoneIndex(HumanBodyBone.Hips);

        var source = new Pose(avatar.Skeleton);
        source.SetToReferencePose();
        Transform3D bind = avatar.Skeleton.GetBoneParentSpaceTransform(hipsIndex);
        source.SetTransform(hipsIndex, new Transform3D(new Float3(1f, 2f, 3f), bind.rotation, bind.scale));

        var human = new HumanPose();
        Retargeter.RetargetFrom(avatar, source, human);
        var result = new Pose(avatar.Skeleton);
        Retargeter.RetargetTo(avatar, human, result);

        Float3 p = result.GetTransform(hipsIndex).position;
        Assert.Equal(1.0, (double)p.X, 3);
        Assert.Equal(2.0, (double)p.Y, 3);
        Assert.Equal(3.0, (double)p.Z, 3);
    }

    [Fact]
    public void GoalIK_PinsFeetToSameBodyRelativePosition_AcrossProportions()
    {
        // Short legs vs long legs, same naming so both auto-map to humanoid.
        var source = AvatarBuilder.BuildAutomatic(TestSkeletons.MakeProportionedHumanoid(0.5f, 0.5f));
        var target = AvatarBuilder.BuildAutomatic(TestSkeletons.MakeProportionedHumanoid(0.85f, 0.85f));

        int sUpper = source.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperLeg);
        int sLower = source.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.LeftLowerLeg);

        // Bend the left knee so the foot is raised into a non-trivial, bent configuration.
        var sourcePose = new Pose(source.Skeleton);
        sourcePose.SetToReferencePose();
        Transform3D ub = source.Skeleton.GetBoneParentSpaceTransform(sUpper);
        sourcePose.SetTransform(sUpper, new Transform3D(ub.position, ub.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.7f), ub.scale));
        Transform3D lb = source.Skeleton.GetBoneParentSpaceTransform(sLower);
        sourcePose.SetTransform(sLower, new Transform3D(lb.position, lb.rotation * Quaternion.AxisAngle(new Float3(1f, 0f, 0f), -1.2f), lb.scale));
        sourcePose.CalculateModelSpaceTransforms();

        var human = new HumanPose();
        Retargeter.RetargetFrom(source, sourcePose, human);

        var targetPose = new Pose(target.Skeleton);
        Retargeter.RetargetTo(target, human, targetPose);
        targetPose.CalculateModelSpaceTransforms();

        Float3 sourceRel = BodyRelativeFoot(source, sourcePose);
        Float3 targetRel = BodyRelativeFoot(target, targetPose);

        // The foot's scale-normalized, body-relative position should match across the two rigs.
        Assert.Equal((double)sourceRel.X, (double)targetRel.X, 2);
        Assert.Equal((double)sourceRel.Y, (double)targetRel.Y, 2);
        Assert.Equal((double)sourceRel.Z, (double)targetRel.Z, 2);
    }

    private static Float3 BodyRelativeFoot(Avatar avatar, Pose pose)
    {
        int hips = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Hips);
        int foot = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.LeftFoot);
        Transform3D hipsModel = pose.GetModelSpaceTransform(hips);
        Float3 footModel = pose.GetModelSpaceTransform(foot).position;
        Float3 local = hipsModel.InverseTransformPoint(footModel);
        Float3 Bind(HumanBodyBone bone) => avatar.Skeleton.GetBoneModelSpaceTransform(avatar.Humanoid!.GetSkeletonBoneIndex(bone)).position;
        float scale = Float3.Distance(Bind(HumanBodyBone.LeftUpperLeg), Bind(HumanBodyBone.LeftLowerLeg)) + Float3.Distance(Bind(HumanBodyBone.LeftLowerLeg), Bind(HumanBodyBone.LeftFoot));
        return new Float3(local.X / scale, local.Y / scale, local.Z / scale);
    }

    [Fact]
    public void RetargetFrom_OnGenericAvatar_Throws()
    {
        var generic = AvatarBuilder.BuildGeneric(TestSkeletons.MakeChain());
        var pose = new Pose(generic.Skeleton);
        pose.SetToReferencePose();
        Assert.Throws<InvalidOperationException>(() => Retargeter.RetargetFrom(generic, pose, new HumanPose()));
    }
}
