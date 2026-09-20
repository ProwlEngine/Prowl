using Prowl.Clay;
using Prowl.Clay.Importer;
using Prowl.Vector;
using Prowl.Vector.Spatial;
using static Prowl.Motion.Tests.HumanoidTestRig;

namespace Prowl.Motion.Tests;

/// <summary>The muscle space model: exact codec round trips, the rest pose straightened into a T pose, and frames that survive odd bone directions.</summary>
public class MuscleSpaceModelTests
{
    private static Avatar Mixamo()
        => AvatarBuilder.BuildAutomatic(ClayModelLoader.BuildSkeleton(ModelImporter.Load(TestAssets.RumbaDancing)));

    [ModelFact]
    public void RandomMuscles_DecodeAndEncodeBackExactly_OnARealRig()
    {
        Avatar avatar = Mixamo();
        var rng = new Random(3);
        var decoded = new Pose(avatar.Skeleton);
        var encoded = new HumanPose();

        for (int trial = 0; trial < 40; trial++)
        {
            var human = new HumanPose();
            for (int m = 0; m < HumanTrait.MuscleCount; m++)
                if (avatar.Humanoid!.HasBone(HumanTrait.GetMuscleBone(m)))
                    human.SetMuscle(m, (float)(rng.NextDouble() * 1.6 - 0.8));
            human.BodyRotation = Quaternion.Normalize(Quaternion.AxisAngle(Float3.Normalize(new Float3(0.3f, 1f, -0.2f)), (float)rng.NextDouble()));
            human.BodyPosition = new Float3(0.1f, 0.9f, -0.2f);

            Retargeter.RetargetTo(avatar, human, decoded);
            Retargeter.RetargetFrom(avatar, decoded, encoded);

            for (int m = 0; m < HumanTrait.MuscleCount; m++)
                Assert.True(MathF.Abs(human.GetMuscle(m) - encoded.GetMuscle(m)) < 2e-3f, $"trial {trial}: {HumanTrait.GetMuscleName(m)} went {human.GetMuscle(m):N4} to {encoded.GetMuscle(m):N4}");
            Assert.True(Float3.Distance(human.BodyPosition, encoded.BodyPosition) < 1e-3f);
            Assert.True(AngleDeg(human.BodyRotation, encoded.BodyRotation) < 0.1f);
        }
    }

    [Fact]
    public void CurledFingerRest_IsStraightenedForTheTPose()
    {
        Avatar straight = new HumanoidTestRig { Fingers = true }.BuildAvatar();
        (Skeleton skeleton, HumanDescription description) = new HumanoidTestRig { Fingers = true }.Build();
        int intermediate = description.GetSkeletonBoneIndex(HumanBodyBone.LeftIndexIntermediate);
        Transform3D bind = skeleton.GetBoneParentSpaceTransform(intermediate);
        var local = new Transform3D[skeleton.BoneCount];
        for (int i = 0; i < local.Length; i++)
            local[i] = skeleton.GetBoneParentSpaceTransform(i);
        local[intermediate] = new Transform3D(bind.position, Quaternion.AxisAngle(new Float3(0f, 0f, 1f), 0.6f), bind.scale);
        var ids = Enumerable.Range(0, skeleton.BoneCount).Select(skeleton.GetBoneID).ToArray();
        var parents = Enumerable.Range(0, skeleton.BoneCount).Select(skeleton.GetParentBoneIndex).ToArray();
        Avatar curled = AvatarBuilder.BuildHumanoid(new Skeleton(ids, parents, local), description);

        Pose result = Retarget(straight, BindPose(straight), curled);

        Float3 Segment(HumanBodyBone from, HumanBodyBone to) => ModelPos(curled, result, to) - ModelPos(curled, result, from);
        float bend = AngleDeg(Segment(HumanBodyBone.LeftIndexProximal, HumanBodyBone.LeftIndexIntermediate), Segment(HumanBodyBone.LeftIndexIntermediate, HumanBodyBone.LeftIndexDistal));
        Assert.True(bend < 0.5f, $"finger still bent {bend:N1} degrees");
    }

    [Fact]
    public void RigFacingBackward_RetargetsTheSameAsOneFacingForward()
    {
        Avatar forward = new HumanoidTestRig().BuildAvatar();
        Avatar backward = new HumanoidTestRig { ArmatureRotation = Quaternion.AxisAngle(new Float3(0f, 1f, 0f), MathF.PI) }.BuildAvatar();

        Pose pose = BindPose(forward);
        RotateLocal(forward, pose, HumanBodyBone.Spine, Quaternion.AxisAngle(new Float3(1f, 0f, 0f), 0.4f));
        RotateLocal(forward, pose, HumanBodyBone.LeftUpperArm, Quaternion.AxisAngle(new Float3(0f, 0f, 1f), 0.5f));
        var human = new HumanPose();
        Retargeter.RetargetFrom(forward, pose, human);
        var twin = new HumanPose();
        Pose turned = Retarget(forward, pose, backward);
        Retargeter.RetargetFrom(backward, turned, twin);

        for (int m = 0; m < HumanTrait.MuscleCount; m++)
            Assert.True(MathF.Abs(human.GetMuscle(m) - twin.GetMuscle(m)) < 1e-3f, $"{HumanTrait.GetMuscleName(m)} {human.GetMuscle(m):N4} vs {twin.GetMuscle(m):N4}");
        Assert.True(Float3.Distance(human.BodyPosition, twin.BodyPosition) < 1e-3f);
        Assert.True(AngleDeg(human.BodyRotation, twin.BodyRotation) < 0.1f);
    }
}
