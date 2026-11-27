using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>The IK solvers: two bone, chain and rig.</summary>
public class IK_Tests
{
    private static readonly Float3 XAxis = new(1f, 0f, 0f);

    // Hip at (0,1,0), knee and ankle each 0.5 below their parent. The knee is bent forward (+Z).
    private static Pose MakeBentLeg()
    {
        var ids = new[] { new StringID("Hip"), new StringID("Knee"), new StringID("Ankle") };
        var parents = new[] { Skeleton.InvalidIndex, 0, 1 };
        var bind = new[]
        {
            new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(0f, -0.5f, 0f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(0f, -0.5f, 0f), Quaternion.Identity, Float3.One),
        };
        var pose = new Pose(new Skeleton(ids, parents, bind));
        pose.SetToReferencePose();
        pose.SetTransform(0, new Transform3D(bind[0].position, Quaternion.AxisAngle(XAxis, -0.4f), Float3.One));
        pose.SetTransform(1, new Transform3D(bind[1].position, Quaternion.AxisAngle(XAxis, 0.8f), Float3.One));
        pose.CalculateModelSpaceTransforms();
        return pose;
    }

    private static Float3 BendNormal(Pose pose, int a, int b, int c)
    {
        Float3 pa = pose.GetModelSpaceTransform(a).position;
        Float3 pb = pose.GetModelSpaceTransform(b).position;
        Float3 pc = pose.GetModelSpaceTransform(c).position;
        return Float3.Cross(pb - pa, pc - pb);
    }

    // Root(0,0,0) -> Hip(0,1,0) -> Knee(0,2,0): two unit segments, total reach 2.
    private static Pose MakeChainPose()
    {
        var pose = new Pose(TestSkeletons.MakeChain());
        pose.SetToReferencePose();
        return pose;
    }

    // The solve as it was when every step recomputed the whole model space.
    private static void ReferenceSolve(Pose pose, IReadOnlyList<int> chain, Float3 target, int iterations, float tolerance, float weight)
    {
        Skeleton skeleton = pose.Skeleton;
        int jointCount = chain.Count - 1;
        var original = new Quaternion[jointCount];
        for (int j = 0; j < jointCount; j++)
            original[j] = pose.GetTransform(chain[j]).rotation;

        int endBone = chain[chain.Count - 1];
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            if (Float3.Distance(pose.GetModelSpaceTransform(endBone).position, target) <= tolerance)
                break;

            for (int j = jointCount - 1; j >= 0; j--)
            {
                int joint = chain[j];
                Transform3D jointWorld = pose.GetModelSpaceTransform(joint);
                Float3 toEnd = pose.GetModelSpaceTransform(endBone).position - jointWorld.position;
                Float3 toTarget = target - jointWorld.position;
                if (Float3.Length(toEnd) < 1e-5f || Float3.Length(toTarget) < 1e-5f)
                    continue;

                Quaternion newWorld = FromTo(toEnd, toTarget) * jointWorld.rotation;
                int parent = skeleton.GetParentBoneIndex(joint);
                Quaternion parentWorld = parent == Skeleton.InvalidIndex ? Quaternion.Identity : pose.GetModelSpaceTransform(parent).rotation;
                Transform3D local = pose.GetTransform(joint);
                pose.SetTransform(joint, new Transform3D(local.position, Quaternion.Inverse(parentWorld) * newWorld, local.scale));
            }
        }

        if (weight >= 1f)
            return;
        for (int j = 0; j < jointCount; j++)
        {
            Transform3D local = pose.GetTransform(chain[j]);
            pose.SetTransform(chain[j], new Transform3D(local.position, Quaternion.Slerp(original[j], local.rotation, weight), local.scale));
        }
    }

    private static Quaternion FromTo(Float3 from, Float3 to)
    {
        Float3 f = Unit(from);
        Float3 g = Unit(to);
        float d = Float3.Dot(f, g);
        if (d >= 1f - 1e-6f)
            return Quaternion.Identity;
        if (d <= -1f + 1e-6f)
            return Quaternion.AxisAngle(Float3.Normalize(Float3.Cross(f, MathF.Abs(f.X) < 0.9f ? new Float3(1f, 0f, 0f) : new Float3(0f, 1f, 0f))), MathF.PI);
        return Quaternion.AxisAngle(Float3.Normalize(Float3.Cross(f, g)), MathF.Acos(Math.Clamp(d, -1f, 1f)));
    }

    private static Float3 Unit(Float3 v)
    {
        float length = Float3.Length(v);
        return new Float3(v.X / length, v.Y / length, v.Z / length);
    }

    // A spine of depth bones with a side branch on each, some rotated and non uniformly scaled.
    private static Skeleton BranchySkeleton(int depth)
    {
        var ids = new List<StringID>();
        var parents = new List<int>();
        var local = new List<Transform3D>();
        var rng = new Random(3);
        int previous = Skeleton.InvalidIndex;
        for (int i = 0; i < depth; i++)
        {
            int spine = ids.Count;
            ids.Add(new StringID("Spine" + i));
            parents.Add(previous);
            Quaternion rotation = Quaternion.AxisAngle(Float3.Normalize(new Float3((float)rng.NextDouble(), 1f, (float)rng.NextDouble())), (float)rng.NextDouble() * 0.5f);
            Float3 scale = i % 5 == 3 ? new Float3(1.2f, 0.9f, 1.1f) : Float3.One;
            local.Add(new Transform3D(new Float3(0.05f, 0.3f, 0.02f), rotation, scale));

            ids.Add(new StringID("Branch" + i));
            parents.Add(spine);
            local.Add(new Transform3D(new Float3(0.2f, 0f, 0f), Quaternion.Identity, Float3.One));
            previous = spine;
        }
        return new Skeleton(ids, parents, local);
    }

    [Fact]
    public void Rig_SolvesEffectorToTarget()
    {
        var pose = new Pose(TestSkeletons.MakeChain());
        pose.SetToReferencePose();

        var rig = new IKRig();
        IKEffector arm = rig.AddEffector("arm", new[] { 0, 1, 2 });
        arm.Target = new Float3(1f, 1f, 0f);

        rig.Solve(pose);

        pose.CalculateModelSpaceTransforms();
        Float3 end = pose.GetModelSpaceTransform(2).position;
        Assert.True(Float3.Distance(end, arm.Target) < 1e-3f);
    }

    [Fact]
    public void Find_ReturnsAddedEffector()
    {
        var rig = new IKRig();
        rig.AddEffector("leg", new[] { 0, 1, 2 });
        Assert.NotNull(rig.Find("leg"));
        Assert.Null(rig.Find("missing"));
    }

    [Fact]
    public void TwoBoneIK_KneeKeepsItsBendSideAsTheTargetSweepsPastIt()
    {
        Assert.True(BendNormal(MakeBentLeg(), 0, 1, 2).X > 0f);

        for (float z = -0.3f; z <= 0.6f; z += 0.05f)
        {
            Pose pose = MakeBentLeg();
            var target = new Float3(0f, 0.3f, z);
            TwoBoneIK.Solve(pose, 0, 1, 2, target);

            Assert.True(BendNormal(pose, 0, 1, 2).X > 0f, $"knee flipped for target z={z}");
            Assert.True(Float3.Distance(pose.GetModelSpaceTransform(2).position, target) < 1e-3f, $"missed target z={z}");
        }
    }

    [Fact]
    public void TwoBoneIK_StretchIsBlendedByWeight()
    {
        var pose = new Pose(TestSkeletons.MakeChain());
        pose.SetToReferencePose();

        // Full stretch would be 1.4x. Half weight gives 1.2x.
        TwoBoneIK.Solve(pose, 0, 1, 2, new Float3(2.8f, 0f, 0f), weight: 0.5f, stretch: 0.5f);

        Float3 a = pose.GetModelSpaceTransform(0).position;
        Float3 b = pose.GetModelSpaceTransform(1).position;
        Float3 c = pose.GetModelSpaceTransform(2).position;
        Assert.Equal(1.2, (double)Float3.Distance(a, b), 3);
        Assert.Equal(1.2, (double)Float3.Distance(b, c), 3);
    }

    [Fact]
    public void TwoBoneIK_InvalidInputsLeaveThePoseUntouched()
    {
        var pose = new Pose(TestSkeletons.MakeChain());
        pose.SetToReferencePose();
        var before = new Transform3D[3];
        for (int i = 0; i < 3; i++)
            before[i] = pose.GetTransform(i);

        var target = new Float3(1f, 1f, 0f);
        TwoBoneIK.Solve(pose, 2, 1, 0, target);
        TwoBoneIK.Solve(pose, 0, 0, 2, target);
        TwoBoneIK.Solve(pose, 0, 1, 7, target);
        TwoBoneIK.Solve(pose, -1, 1, 2, target);
        TwoBoneIK.Solve(pose, 0, 1, 2, new Float3(float.NaN, 0f, 0f));

        for (int i = 0; i < 3; i++)
            Assert.Equal(before[i], pose.GetTransform(i));
    }

    [Fact]
    public void TwoBoneIK_ZeroLengthBoneIsSkipped()
    {
        var ids = new[] { new StringID("A"), new StringID("B"), new StringID("C") };
        var parents = new[] { Skeleton.InvalidIndex, 0, 1 };
        var bind = new[]
        {
            Transform3D.Identity,
            Transform3D.Identity,
            new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One),
        };
        var pose = new Pose(new Skeleton(ids, parents, bind));
        pose.SetToReferencePose();

        TwoBoneIK.Solve(pose, 0, 1, 2, new Float3(0.5f, 0.5f, 0f));

        for (int i = 0; i < 3; i++)
            Assert.Equal(bind[i], pose.GetTransform(i));
    }

    [Fact]
    public void ChainIK_WeightBlendsTheFinalSolveOnce()
    {
        var target = new Float3(1f, 1f, 0.5f);

        var full = new Pose(TestSkeletons.MakeChain());
        full.SetToReferencePose();
        ChainIK.Solve(full, new[] { 0, 1, 2 }, target, iterations: 20);

        var half = new Pose(TestSkeletons.MakeChain());
        half.SetToReferencePose();
        ChainIK.Solve(half, new[] { 0, 1, 2 }, target, iterations: 20, weight: 0.5f);

        var fk = new Pose(TestSkeletons.MakeChain());
        fk.SetToReferencePose();

        for (int joint = 0; joint < 2; joint++)
        {
            float fullAngle = Quaternion.Angle(fk.GetTransform(joint).rotation, full.GetTransform(joint).rotation);
            float halfAngle = Quaternion.Angle(fk.GetTransform(joint).rotation, half.GetTransform(joint).rotation);
            Assert.Equal(fullAngle * 0.5f, halfAngle, 3);
        }
    }

    [Fact]
    public void IKRig_ThreeBoneEffectorWithATwistBoneStillReachesTheTarget()
    {
        // Upper -> Twist -> Mid -> End, registered as {Upper, Mid, End}.
        var ids = new[] { new StringID("Upper"), new StringID("Twist"), new StringID("Mid"), new StringID("End") };
        var parents = new[] { Skeleton.InvalidIndex, 0, 1, 2 };
        var bind = new[]
        {
            new Transform3D(new Float3(0f, 1f, 0f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(0f, -0.25f, 0f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(0f, -0.25f, 0f), Quaternion.Identity, Float3.One),
            new Transform3D(new Float3(0f, -0.5f, 0f), Quaternion.Identity, Float3.One),
        };
        var pose = new Pose(new Skeleton(ids, parents, bind));
        pose.SetToReferencePose();
        pose.SetTransform(2, new Transform3D(bind[2].position, Quaternion.AxisAngle(XAxis, 0.6f), Float3.One));

        var rig = new IKRig();
        IKEffector leg = rig.AddEffector("leg", new[] { 0, 2, 3 });
        leg.Target = new Float3(0f, 0.4f, 0.3f);
        rig.Solve(pose);

        Assert.True(Float3.Distance(pose.GetModelSpaceTransform(3).position, leg.Target) < 1e-2f);
    }

    [Fact]
    public void Solve_ReachesReachableTarget()
    {
        var pose = MakeChainPose();
        var target = new Float3(1f, 1f, 0f); // distance ~1.414 from root, within reach

        TwoBoneIK.Solve(pose, upper: 0, mid: 1, end: 2, target);

        pose.CalculateModelSpaceTransforms();
        Float3 end = pose.GetModelSpaceTransform(2).position;
        Assert.True(Float3.Distance(end, target) < 1e-3f, $"end {end} did not reach {target}");
    }

    [Fact]
    public void Solve_UnreachableTarget_StraightensTowardIt()
    {
        var pose = MakeChainPose();
        var target = new Float3(0f, 10f, 0f); // far beyond reach of 2

        TwoBoneIK.Solve(pose, 0, 1, 2, target);

        pose.CalculateModelSpaceTransforms();
        Float3 end = pose.GetModelSpaceTransform(2).position;
        // Fully extended toward the target => end is ~2 units up the Y axis.
        Assert.Equal(2.0, (double)end.Y, 2);
        Assert.True(Float3.Distance(new Float3(0f, 0f, 0f), end) > 1.99f);
    }

    [Fact]
    public void Solve_PreservesBoneLengths()
    {
        var pose = MakeChainPose();
        TwoBoneIK.Solve(pose, 0, 1, 2, new Float3(1.2f, 0.8f, 0.3f));

        pose.CalculateModelSpaceTransforms();
        Float3 a = pose.GetModelSpaceTransform(0).position;
        Float3 b = pose.GetModelSpaceTransform(1).position;
        Float3 c = pose.GetModelSpaceTransform(2).position;
        Assert.Equal(1.0, (double)Float3.Distance(a, b), 3);
        Assert.Equal(1.0, (double)Float3.Distance(b, c), 3);
    }

    [Fact]
    public void Solve_ReachesTarget()
    {
        var pose = new Pose(TestSkeletons.MakeChain());
        pose.SetToReferencePose();

        var target = new Float3(1f, 1f, 0.5f);
        ChainIK.Solve(pose, new[] { 0, 1, 2 }, target, iterations: 20);

        Float3 end = pose.GetModelSpaceTransform(2).position;
        Assert.True(Float3.Distance(end, target) < 1e-2f, $"end {end} did not reach {target}");
    }

    [Theory]
    [InlineData(8, new[] { 2, 6, 10, 14 }, 1f)]
    [InlineData(8, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }, 1f)]
    [InlineData(8, new[] { 4, 8, 9 }, 0.5f)]
    [InlineData(80, new[] { 20, 100, 158 }, 1f)]
    [InlineData(80, new[] { 140, 146, 150, 154, 158 }, 0.7f)]
    public void Solve_GivesTheSameResultAsTheFullRecompute(int depth, int[] chain, float weight)
    {
        Skeleton skeleton = BranchySkeleton(depth);
        var rng = new Random(depth + chain.Length);

        for (int trial = 0; trial < 20; trial++)
        {
            var target = new Float3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() * depth * 0.3f, (float)rng.NextDouble() - 0.5f);
            var solved = new Pose(skeleton);
            solved.SetToReferencePose();
            var reference = new Pose(skeleton);
            reference.SetToReferencePose();

            ChainIK.Solve(solved, chain, target, iterations: 12, tolerance: 1e-4f, weight: weight);
            ReferenceSolve(reference, chain, target, iterations: 12, tolerance: 1e-4f, weight: weight);

            for (int i = 0; i < skeleton.BoneCount; i++)
            {
                Transform3D a = solved.GetTransform(i);
                Transform3D b = reference.GetTransform(i);
                Assert.True(MathF.Abs(MathF.Abs(Quaternion.Dot(a.rotation, b.rotation)) - 1f) < 1e-5f, $"trial {trial}: bone {i} rotation differs");
                Assert.Equal(b.position, a.position);
            }
        }
    }
}
