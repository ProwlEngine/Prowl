using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Tests;

public class ThirdReviewChainIKTests
{
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
