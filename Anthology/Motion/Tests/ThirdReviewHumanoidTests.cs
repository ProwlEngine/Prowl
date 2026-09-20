using Prowl.Vector;
using Prowl.Vector.Spatial;
using static Prowl.Motion.Tests.HumanoidTestRig;

namespace Prowl.Motion.Tests;

public class ThirdReviewHumanoidTests
{
    private static readonly Float3 X = new(1f, 0f, 0f);
    private const float Deg = MathF.PI / 180f;

    private static Pose Mirrored(Avatar avatar, Pose source)
    {
        var result = new Pose(avatar.Skeleton);
        PoseMirror.Apply(avatar, source, result);
        result.CalculateModelSpaceTransforms();
        return result;
    }

    private static Pose Decoded(Avatar avatar, HumanPose human)
    {
        var pose = new Pose(avatar.Skeleton);
        Retargeter.RetargetTo(avatar, human, pose);
        pose.CalculateModelSpaceTransforms();
        return pose;
    }

    private static HumanPose Encoded(Avatar avatar, Pose pose)
    {
        var human = new HumanPose();
        Retargeter.RetargetFrom(avatar, pose, human);
        return human;
    }

    private static HumanPose RandomHuman(Random rng, float amplitude, bool body)
    {
        var human = new HumanPose();
        for (int m = 0; m < HumanTrait.MuscleCount; m++)
            human.SetMuscle(m, (float)(rng.NextDouble() * 2.0 - 1.0) * amplitude);
        if (body)
        {
            human.BodyRotation = Quaternion.Normalize(new Quaternion((float)rng.NextDouble() * 0.3f, (float)rng.NextDouble() * 0.3f, (float)rng.NextDouble() * 0.3f, 1f));
            human.BodyPosition = new Float3((float)rng.NextDouble() * 0.2f, (float)rng.NextDouble() * 0.2f, (float)rng.NextDouble() * 0.2f);
        }
        return human;
    }

    private static (float Rotation, float Position) WorstDifference(Pose a, Pose b)
    {
        a.CalculateModelSpaceTransforms();
        b.CalculateModelSpaceTransforms();
        float rotation = 0f, position = 0f;
        for (int i = 0; i < a.BoneCount; i++)
        {
            rotation = MathF.Max(rotation, AngleDeg(a.GetModelSpaceTransform(i).rotation, b.GetModelSpaceTransform(i).rotation));
            position = MathF.Max(position, Float3.Distance(a.GetModelSpaceTransform(i).position, b.GetModelSpaceTransform(i).position));
        }
        return (rotation, position);
    }

    // Appends an unmapped bone between a mapped bone and one of its children, at the given model space position.
    private static Skeleton InsertBone(Skeleton skeleton, string name, int parent, int child, Float3 model)
    {
        int n = skeleton.BoneCount;
        var ids = new StringID[n + 1];
        var parents = new int[n + 1];
        var local = new Transform3D[n + 1];
        for (int i = 0; i < n; i++)
        {
            ids[i] = skeleton.GetBoneID(i);
            parents[i] = skeleton.GetParentBoneIndex(i);
            local[i] = skeleton.GetBoneParentSpaceTransform(i);
        }

        Float3 parentModel = skeleton.GetBoneModelSpaceTransform(parent).position;
        ids[n] = new StringID(name);
        parents[n] = parent;
        local[n] = new Transform3D(model - parentModel, Quaternion.Identity, Float3.One);
        parents[child] = n;
        local[child] = new Transform3D(skeleton.GetBoneModelSpaceTransform(child).position - model, local[child].rotation, local[child].scale);
        return new Skeleton(ids, parents, local);
    }

    // The test rig with an unmapped twist bone halfway down each forearm, between the forearm and the hand.
    private static Avatar TwistBoneAvatar()
    {
        (Skeleton skeleton, HumanDescription description) = new HumanoidTestRig().Build();
        foreach (string side in new[] { "Left", "Right" })
        {
            int lower = description.GetSkeletonBoneIndex(Enum.Parse<HumanBodyBone>(side + "LowerArm"));
            int hand = description.GetSkeletonBoneIndex(Enum.Parse<HumanBodyBone>(side + "Hand"));
            Float3 middle = (skeleton.GetBoneModelSpaceTransform(lower).position + skeleton.GetBoneModelSpaceTransform(hand).position) * 0.5f;
            skeleton = InsertBone(skeleton, side + "ForeArmTwist", lower, hand, middle);
        }
        return AvatarBuilder.BuildHumanoid(skeleton, description);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Mirror_AnimatedNeck_MovesTheHeadOnceWhetherOrNotTheNeckIsMapped(bool mapNeck)
    {
        (Skeleton skeleton, HumanDescription description) = new HumanoidTestRig().Build();
        if (!mapNeck)
            description.SetSkeletonBoneIndex(HumanBodyBone.Neck, Skeleton.InvalidIndex);
        Avatar avatar = AvatarBuilder.BuildHumanoid(skeleton, description);
        int neck = skeleton.GetBoneIndex(new StringID("Neck"));
        int head = skeleton.GetBoneIndex(new StringID("Head"));

        Pose source = BindPose(avatar);
        Transform3D n = source.GetTransform(neck);
        source.SetTransform(neck, new Transform3D(n.position, Quaternion.AxisAngle(X, 30f * Deg), n.scale));
        source.CalculateModelSpaceTransforms();

        Pose mirrored = Mirrored(avatar, source);

        Assert.True(AngleDeg(source.GetModelSpaceTransform(head).rotation, mirrored.GetModelSpaceTransform(head).rotation) < 0.5f);
        Assert.True(Float3.Distance(source.GetModelSpaceTransform(head).position, mirrored.GetModelSpaceTransform(head).position) < 1e-3f);
    }

    [Fact]
    public void Mirror_RootMovedForward_KeepsTheHipsWhereTheSourceHasThem()
    {
        Avatar avatar = new HumanoidTestRig().BuildAvatar();
        Pose source = BindPose(avatar);
        source.SetTransform(0, new Transform3D(new Float3(0f, 0f, 1f), Quaternion.Identity, Float3.One));

        Pose mirrored = Mirrored(avatar, source);

        Assert.True(Float3.Distance(ModelPos(avatar, source, HumanBodyBone.Hips), ModelPos(avatar, mirrored, HumanBodyBone.Hips)) < 1e-3f);
        Assert.True(Float3.Distance(mirrored.GetModelSpaceTransform(0).position, new Float3(0f, 0f, 1f)) < 1e-4f);
    }

    [Fact]
    public void Mirror_RootPitched_KeepsTheHipsRotation()
    {
        Avatar avatar = new HumanoidTestRig().BuildAvatar();
        Pose source = BindPose(avatar);
        source.SetTransform(0, new Transform3D(Float3.Zero, Quaternion.AxisAngle(X, 0.5f), Float3.One));

        Pose mirrored = Mirrored(avatar, source);

        Assert.True(AngleDeg(ModelRot(avatar, source, HumanBodyBone.Hips), ModelRot(avatar, mirrored, HumanBodyBone.Hips)) < 0.5f);
        Assert.True(WorstDifference(source, mirrored).Rotation < 0.5f);
    }

    [Fact]
    public void Mirror_RootYawed_ReflectsTheRootAndTheBody()
    {
        Avatar avatar = new HumanoidTestRig().BuildAvatar();
        Pose source = BindPose(avatar);
        source.SetTransform(0, new Transform3D(new Float3(0.4f, 0f, 0f), Quaternion.AxisAngle(new Float3(0f, 1f, 0f), 0.6f), Float3.One));

        Pose mirrored = Mirrored(avatar, source);

        Transform3D root = mirrored.GetTransform(0);
        Assert.True(Float3.Distance(root.position, new Float3(-0.4f, 0f, 0f)) < 1e-4f);
        Assert.True(AngleDeg(root.rotation, Quaternion.AxisAngle(new Float3(0f, 1f, 0f), -0.6f)) < 0.1f);
        Float3 hips = ModelPos(avatar, source, HumanBodyBone.Hips);
        Assert.True(Float3.Distance(new Float3(-hips.X, hips.Y, hips.Z), ModelPos(avatar, mirrored, HumanBodyBone.Hips)) < 1e-3f);
    }

    [Fact]
    public void Mirror_UnmappedTwistBone_MovesToTheOtherSide()
    {
        Avatar avatar = TwistBoneAvatar();
        Skeleton skeleton = avatar.Skeleton;
        int leftTwist = skeleton.GetBoneIndex(new StringID("LeftForeArmTwist"));
        int rightTwist = skeleton.GetBoneIndex(new StringID("RightForeArmTwist"));

        Pose source = BindPose(avatar);
        Transform3D t = source.GetTransform(leftTwist);
        Quaternion swing = Quaternion.AxisAngle(new Float3(0f, 0f, 1f), 25f * Deg);
        source.SetTransform(leftTwist, new Transform3D(t.position, swing, t.scale));

        Pose mirrored = Mirrored(avatar, source);

        Assert.True(AngleDeg(mirrored.GetTransform(leftTwist).rotation, Quaternion.Identity) < 0.1f);
        Assert.True(AngleDeg(mirrored.GetTransform(rightTwist).rotation, Quaternion.AxisAngle(new Float3(0f, 0f, 1f), -25f * Deg)) < 0.1f);

        Float3 hand = ModelPos(avatar, source, HumanBodyBone.LeftHand);
        Float3 mirroredHand = ModelPos(avatar, mirrored, HumanBodyBone.RightHand);
        Assert.True(Float3.Distance(new Float3(-hand.X, hand.Y, hand.Z), mirroredHand) < 1e-3f);
        Float3 right = ModelPos(avatar, source, HumanBodyBone.RightHand);
        Assert.True(Float3.Distance(new Float3(-right.X, right.Y, right.Z), ModelPos(avatar, mirrored, HumanBodyBone.LeftHand)) < 1e-3f);
    }

    [Fact]
    public void Mirror_Twice_ReturnsThePose_WithAnimatedUnmappedBones()
    {
        Avatar avatar = TwistBoneAvatar();
        Skeleton skeleton = avatar.Skeleton;
        int leftTwist = skeleton.GetBoneIndex(new StringID("LeftForeArmTwist"));
        int rightTwist = skeleton.GetBoneIndex(new StringID("RightForeArmTwist"));
        var rng = new Random(11);

        for (int trial = 0; trial < 40; trial++)
        {
            Pose source = Decoded(avatar, RandomHuman(rng, 0.6f, body: true));
            source.SetTransform(0, new Transform3D(new Float3(0.3f, 0.1f, -0.2f), Quaternion.AxisAngle(Float3.Normalize(new Float3(0.2f, 1f, 0.3f)), 0.7f), Float3.One));
            foreach (int twist in new[] { leftTwist, rightTwist })
            {
                Transform3D t = source.GetTransform(twist);
                source.SetTransform(twist, new Transform3D(t.position, Quaternion.AxisAngle(X, (float)(rng.NextDouble() - 0.5)), t.scale));
            }

            Pose once = Mirrored(avatar, source);
            Pose back = Mirrored(avatar, Mirrored(avatar, once));
            (float rotation, float position) = WorstDifference(once, back);
            Assert.True(rotation < 0.5f, $"trial {trial}: a bone came back {rotation:N2} degrees off");
            Assert.True(position < 2e-3f, $"trial {trial}: a bone came back {position:N4} off");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Mirror_Twice_ReturnsRandomPoses(int config)
    {
        HumanoidTestRig rigDef = config switch
        {
            0 => new HumanoidTestRig(),
            1 => new HumanoidTestRig { Chest = false, UpperChest = false, Neck = false, Shoulders = false, Toes = false },
            2 => new HumanoidTestRig { ArmatureRotation = Quaternion.AxisAngle(new Float3(0f, 1f, 0f), MathF.PI) },
            3 => new HumanoidTestRig { Fingers = true, ArmDownDegrees = 40f, NeckLeanDegrees = 20f },
            _ => new HumanoidTestRig { ArmatureRotation = Quaternion.AxisAngle(X, -MathF.PI / 2f) },
        };
        Avatar avatar = rigDef.BuildAvatar();
        var rng = new Random(7);
        // A missing chest is carried by the spine in even shares, which clamp on the narrower ranges when poses get large.
        float amplitude = config == 1 ? 0.4f : 0.7f;

        for (int trial = 0; trial < 60; trial++)
        {
            Pose source = Decoded(avatar, RandomHuman(rng, amplitude, body: true));
            Pose back = Mirrored(avatar, Mirrored(avatar, source));
            float rotation = WorstDifference(source, back).Rotation;
            Assert.True(rotation < 0.5f, $"trial {trial}: a bone came back {rotation:N2} degrees off");
        }
    }

    [Fact]
    public void RoundTrip_ForearmTwist_DoesNotDrift()
    {
        Avatar avatar = new HumanoidTestRig().BuildAvatar();
        int twist = HumanTrait.GetMuscleIndex(HumanBodyBone.LeftLowerArm, MuscleAxis.X);
        int upperTwist = HumanTrait.GetMuscleIndex(HumanBodyBone.LeftUpperArm, MuscleAxis.X);
        var human = new HumanPose();
        human.SetMuscle(twist, 0.5f);
        Pose first = Decoded(avatar, human);

        for (int cycle = 0; cycle < 4; cycle++)
        {
            human = Encoded(avatar, Decoded(avatar, human));
            Assert.Equal(0.5, human.GetMuscle(twist), 3);
            Assert.Equal(0.0, human.GetMuscle(upperTwist), 3);
        }
        Assert.True(WorstDifference(first, Decoded(avatar, human)).Rotation < 0.1f);
    }

    [Theory]
    [InlineData(0.5f, 0.5f, 0.5f, 0.5f)]
    [InlineData(0.2f, 0.9f, 0.7f, 0.1f)]
    [InlineData(0f, 1f, 1f, 0f)]
    [InlineData(0.9f, 0.9f, 0.3f, 0.6f)]
    public void EncodeOfDecode_ReturnsTheMuscles(float upperArm, float lowerArm, float upperLeg, float lowerLeg)
    {
        (Skeleton skeleton, HumanDescription description) = new HumanoidTestRig { Fingers = true }.Build();
        description.UpperArmTwist = upperArm;
        description.LowerArmTwist = lowerArm;
        description.UpperLegTwist = upperLeg;
        description.LowerLegTwist = lowerLeg;
        Avatar avatar = AvatarBuilder.BuildHumanoid(skeleton, description);
        HumanoidRig rig = avatar.Humanoid!;
        var rng = new Random(5);

        for (int trial = 0; trial < 100; trial++)
        {
            HumanPose human = RandomHuman(rng, 0.7f, body: false);
            HumanPose back = Encoded(avatar, Decoded(avatar, human));
            for (int m = 0; m < HumanTrait.MuscleCount; m++)
            {
                if (!rig.HasBone(HumanTrait.GetMuscleBone(m)))
                    continue;
                float error = MathF.Abs(human.GetMuscle(m) - back.GetMuscle(m));
                Assert.True(error < 5e-3f, $"trial {trial}: {HumanTrait.GetMuscleName(m)} went from {human.GetMuscle(m):N4} to {back.GetMuscle(m):N4}");
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedRoundTrips_KeepThePose(bool minimal)
    {
        HumanoidTestRig rigDef = minimal
            ? new HumanoidTestRig { Chest = false, UpperChest = false, Neck = false, Shoulders = false, Toes = false }
            : new HumanoidTestRig { ArmDownDegrees = 30f };
        Avatar avatar = rigDef.BuildAvatar();
        var rng = new Random(9);
        float amplitude = minimal ? 0.4f : 0.7f;

        for (int trial = 0; trial < 60; trial++)
        {
            Pose first = Decoded(avatar, Encoded(avatar, Decoded(avatar, RandomHuman(rng, amplitude, body: true))));
            Pose pose = first;
            for (int cycle = 0; cycle < 3; cycle++)
                pose = Decoded(avatar, Encoded(avatar, pose));
            float rotation = WorstDifference(first, pose).Rotation;
            Assert.True(rotation < 0.5f, $"trial {trial}: a bone drifted {rotation:N2} degrees");
        }
    }

    private static Skeleton CyclicSkeleton(out HumanDescription description)
    {
        (Skeleton skeleton, HumanDescription desc) = new HumanoidTestRig().Build();
        int n = skeleton.BoneCount;
        var ids = new StringID[n + 2];
        var parents = new int[n + 2];
        var local = new Transform3D[n + 2];
        for (int i = 0; i < n; i++)
        {
            ids[i] = skeleton.GetBoneID(i);
            parents[i] = skeleton.GetParentBoneIndex(i);
            local[i] = skeleton.GetBoneParentSpaceTransform(i);
        }
        ids[n] = new StringID("CycleA");
        parents[n] = n + 1;
        local[n] = Transform3D.Identity;
        ids[n + 1] = new StringID("CycleB");
        parents[n + 1] = n;
        local[n + 1] = Transform3D.Identity;
        parents[desc.GetSkeletonBoneIndex(HumanBodyBone.Spine)] = n;
        description = desc;
        return new Skeleton(ids, parents, local);
    }

    [Fact]
    public void ParentCycleAmongUnmappedBones_RetargetsAndMirrorsWithoutRecursing()
    {
        Skeleton skeleton = CyclicSkeleton(out HumanDescription description);
        Assert.False(skeleton.IsValid);
        Avatar avatar = AvatarBuilder.BuildHumanoid(skeleton, description);

        var human = new HumanPose();
        human.SetMuscle(HumanTrait.GetMuscleIndex(HumanBodyBone.Spine, MuscleAxis.Z), 0.4f);
        Pose pose = Decoded(avatar, human);
        HumanPose back = Encoded(avatar, pose);
        Pose mirrored = Mirrored(avatar, pose);

        Assert.Equal(0.4, back.GetMuscle(HumanTrait.GetMuscleIndex(HumanBodyBone.Spine, MuscleAxis.Z)), 2);
        for (int i = 0; i < skeleton.BoneCount; i++)
            Assert.True(float.IsFinite(mirrored.GetModelSpaceTransform(i).position.X));

        Avatar automatic = AvatarBuilder.BuildAutomatic(skeleton);
        Assert.NotNull(automatic);
    }
}
