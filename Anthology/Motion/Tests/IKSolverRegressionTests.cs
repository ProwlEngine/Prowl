using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class IKSolverRegressionTests
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
}
