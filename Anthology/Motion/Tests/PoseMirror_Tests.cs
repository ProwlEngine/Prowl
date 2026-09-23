using Prowl.Vector.Spatial;
using Prowl.Vector;
using static Prowl.Motion.Tests.HumanoidTestRig;

namespace Prowl.Motion.Tests;

/// <summary>Mirroring humanoid poses and root motion.</summary>
public class PoseMirror_Tests
{
    private const float Deg = MathF.PI / 180f;

    private static int Muscle(HumanBodyBone bone, MuscleAxis axis) => HumanTrait.GetMuscleIndex(bone, axis);

    private static readonly Float3 Left = new(1f, 0f, 0f);

    private static HumanPose Encode(Avatar avatar, Pose pose)
    {
        var human = new HumanPose();
        Retargeter.RetargetFrom(avatar, pose, human);
        return human;
    }

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

    private static readonly Float3 X = new(1f, 0f, 0f);

    private static Pose Mirrored(Avatar avatar, Pose source)
    {
        var result = new Pose(avatar.Skeleton);
        PoseMirror.Apply(avatar, source, result);
        result.CalculateModelSpaceTransforms();
        return result;
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

    private static Pose Decoded(Avatar avatar, HumanPose human)
    {
        var pose = new Pose(avatar.Skeleton);
        Retargeter.RetargetTo(avatar, human, pose);
        pose.CalculateModelSpaceTransforms();
        return pose;
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

    [Theory]
    [InlineData(10f)]
    [InlineData(30f)]
    public void Mirror_OnARigTurnedOffAxis_IsAReflection(float yawDegrees)
    {
        Quaternion yaw = Quaternion.AxisAngle(new Float3(0f, 1f, 0f), yawDegrees * Deg);
        Avatar avatar = new HumanoidTestRig { ArmatureRotation = yaw }.BuildAvatar();
        Float3 normal = yaw * new Float3(1f, 0f, 0f);
        Pose pose = BindPose(avatar);
        RotateLocal(avatar, pose, HumanBodyBone.Spine, Quaternion.AxisAngle(yaw * new Float3(0f, 0f, 1f), 0.5f));
        RotateLocal(avatar, pose, HumanBodyBone.LeftUpperArm, Quaternion.AxisAngle(yaw * new Float3(0f, 0f, 1f), -0.6f));

        var mirrored = new Pose(avatar.Skeleton);
        PoseMirror.Apply(avatar, pose, mirrored);
        mirrored.CalculateModelSpaceTransforms();

        Float3 Reflect(Float3 v) => v - normal * (2f * Float3.Dot(v, normal));
        foreach (HumanBodyBone bone in Enum.GetValues<HumanBodyBone>())
        {
            HumanBodyBone other = HumanTrait.GetMirrorBone(bone);
            if (!avatar.Humanoid!.HasBone(bone) || !avatar.Humanoid.HasBone(other))
                continue;
            float miss = Float3.Distance(Reflect(ModelPos(avatar, pose, other)), ModelPos(avatar, mirrored, bone));
            Assert.True(miss < 0.01f, $"{bone} {miss:N4}");
        }
    }

    [Fact]
    public void MirrorInPlace_ReflectsAMovedRootOnce()
    {
        Avatar avatar = new HumanoidTestRig().BuildAvatar();
        Pose pose = BindPose(avatar);
        pose.SetTransform(0, new Transform3D(new Float3(0.3f, 0f, 0.2f), Quaternion.Identity, Float3.One));
        pose.CalculateModelSpaceTransforms();
        Float3 hips = ModelPos(avatar, pose, HumanBodyBone.Hips);

        var copy = new Pose(avatar.Skeleton);
        PoseMirror.Apply(avatar, pose, copy);
        PoseMirror.Apply(avatar, pose, pose);
        pose.CalculateModelSpaceTransforms();
        copy.CalculateModelSpaceTransforms();

        Assert.True(Float3.Distance(ModelPos(avatar, copy, HumanBodyBone.Hips), ModelPos(avatar, pose, HumanBodyBone.Hips)) < 1e-4f);
        Assert.True(Float3.Distance(new Float3(-hips.X, hips.Y, hips.Z), ModelPos(avatar, copy, HumanBodyBone.Hips)) < 1e-3f);
    }

    [Fact]
    public void MirrorHumanPose_SwapsSidesAndFlipsCentreLateralMuscles()
    {
        var pose = new HumanPose();
        pose.SetMuscle(Muscle(HumanBodyBone.LeftUpperArm, MuscleAxis.Z), 0.5f);
        pose.SetMuscle(Muscle(HumanBodyBone.LeftUpperArm, MuscleAxis.X), 0.3f);
        pose.SetMuscle(Muscle(HumanBodyBone.Spine, MuscleAxis.X), 0.2f);
        pose.SetMuscle(Muscle(HumanBodyBone.Spine, MuscleAxis.Z), 0.4f);
        pose.BodyPosition = new Float3(0.3f, 0.1f, 0.2f);

        var mirrored = new HumanPose();
        PoseMirror.Mirror(pose, mirrored);

        Assert.Equal(0.5f, mirrored.GetMuscle(HumanBodyBone.RightUpperArm, MuscleAxis.Z));
        Assert.Equal(0.3f, mirrored.GetMuscle(HumanBodyBone.RightUpperArm, MuscleAxis.X));
        Assert.Equal(0f, mirrored.GetMuscle(HumanBodyBone.LeftUpperArm, MuscleAxis.Z));
        Assert.Equal(-0.2f, mirrored.GetMuscle(HumanBodyBone.Spine, MuscleAxis.X));
        Assert.Equal(0.4f, mirrored.GetMuscle(HumanBodyBone.Spine, MuscleAxis.Z));
        Assert.Equal(-0.3f, mirrored.BodyPosition.X);
    }

    [Fact]
    public void MirroredGoalRotation_MatchesTheMirroredFoot()
    {
        Avatar avatar = new HumanoidTestRig().BuildAvatar();
        Pose pose = BindPose(avatar);
        RotateLocal(avatar, pose, HumanBodyBone.LeftFoot, Quaternion.AxisAngle(new Float3(0f, 1f, 0f), 0.5f) * Quaternion.AxisAngle(Left, 0.3f));

        var mirrored = new HumanPose();
        PoseMirror.Mirror(Encode(avatar, pose), mirrored);

        var mirroredPose = new Pose(avatar.Skeleton);
        PoseMirror.Apply(avatar, pose, mirroredPose);
        HumanPose reencoded = Encode(avatar, mirroredPose);

        Quaternion a = mirrored.GetGoal(HumanGoal.RightFoot).Transform.rotation;
        Quaternion b = reencoded.GetGoal(HumanGoal.RightFoot).Transform.rotation;
        Assert.True(AngleDeg(a, b) < 1f, $"mirrored goal rotation is {AngleDeg(a, b):N1} degrees off");
    }

    [Fact]
    public void Mirror_ReflectsHipsTranslation()
    {
        Avatar avatar = new HumanoidTestRig().BuildAvatar();
        Pose source = BindPose(avatar);
        int hips = Index(avatar, HumanBodyBone.Hips);
        Transform3D t = source.GetTransform(hips);
        source.SetTransform(hips, new Transform3D(t.position + new Float3(0.3f, 0f, 0f), t.rotation, t.scale));

        var mirrored = new Pose(avatar.Skeleton);
        PoseMirror.Apply(avatar, source, mirrored);
        mirrored.CalculateModelSpaceTransforms();

        Assert.Equal(-0.3, (double)ModelPos(avatar, mirrored, HumanBodyBone.Hips).X, 3);
    }

    [Fact]
    public void Mirror_OfTwistedSpine_IsATrueReflection()
    {
        Avatar avatar = new HumanoidTestRig().BuildAvatar();
        Pose source = BindPose(avatar);
        RotateLocal(avatar, source, HumanBodyBone.Spine, Quaternion.AxisAngle(Up, 34f * MathF.PI / 180f));
        RotateLocal(avatar, source, HumanBodyBone.LeftUpperArm, Quaternion.AxisAngle(new Float3(0f, 0f, 1f), 0.7f));

        var mirrored = new Pose(avatar.Skeleton);
        PoseMirror.Apply(avatar, source, mirrored);
        mirrored.CalculateModelSpaceTransforms();

        Float3 left = ModelPos(avatar, source, HumanBodyBone.LeftLowerArm) - ModelPos(avatar, source, HumanBodyBone.LeftUpperArm);
        Float3 right = ModelPos(avatar, mirrored, HumanBodyBone.RightLowerArm) - ModelPos(avatar, mirrored, HumanBodyBone.RightUpperArm);
        float error = AngleDeg(new Float3(-left.X, left.Y, left.Z), right);
        Assert.True(error < 2f, $"mirrored right arm is {error:N1} degrees off the reflection");

        Float3 hand = ModelPos(avatar, source, HumanBodyBone.LeftHand);
        Float3 mirroredHand = ModelPos(avatar, mirrored, HumanBodyBone.RightHand);
        Assert.True(Float3.Distance(new Float3(-hand.X, hand.Y, hand.Z), mirroredHand) < 0.01f);
    }

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

    [Fact]
    public void Mirror_SwapsLeftPoseOntoTheRightSide()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid();
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        var rig = avatar.Humanoid!;

        // Raise the LEFT arm by rotating the left upper arm.
        var source = new Pose(skeleton);
        source.SetToReferencePose();
        int leftUpper = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftUpperArm);
        Transform3D bind = skeleton.GetBoneParentSpaceTransform(leftUpper);
        source.SetTransform(leftUpper, new Transform3D(bind.position, bind.rotation * Quaternion.AxisAngle(new Float3(0f, 0f, 1f), 0.6f), bind.scale));
        source.CalculateModelSpaceTransforms();

        var mirrored = new Pose(skeleton);
        PoseMirror.Apply(avatar, source, mirrored);
        mirrored.CalculateModelSpaceTransforms();

        int leftHand = rig.GetSkeletonBoneIndex(HumanBodyBone.LeftHand);
        int rightHand = rig.GetSkeletonBoneIndex(HumanBodyBone.RightHand);

        Float3 sourceLeftHand = source.GetModelSpaceTransform(leftHand).position;
        Float3 mirroredRightHand = mirrored.GetModelSpaceTransform(rightHand).position;

        // The mirrored right hand should sit at the source left hand's position with X negated.
        Assert.Equal((double)(-sourceLeftHand.X), (double)mirroredRightHand.X, 2);
        Assert.Equal((double)sourceLeftHand.Y, (double)mirroredRightHand.Y, 2);
        Assert.Equal((double)sourceLeftHand.Z, (double)mirroredRightHand.Z, 2);
    }

    [Fact]
    public void Mirror_OfRestPose_IsStillRest()
    {
        Skeleton skeleton = TestSkeletons.MakeProportionedHumanoid();
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);

        var source = new Pose(skeleton);
        source.SetToReferencePose();
        source.CalculateModelSpaceTransforms();

        var mirrored = new Pose(skeleton);
        PoseMirror.Apply(avatar, source, mirrored);
        mirrored.CalculateModelSpaceTransforms();

        int rightHand = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.RightHand);
        Float3 a = source.GetModelSpaceTransform(rightHand).position;
        Float3 b = mirrored.GetModelSpaceTransform(rightHand).position;
        Assert.True(Float3.Distance(a, b) < 1e-2f, $"rest mirror moved the right hand: {a} -> {b}");
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
}
