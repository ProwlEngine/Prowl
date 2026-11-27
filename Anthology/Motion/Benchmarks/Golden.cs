using Prowl.Vector;
using Prowl.Vector.Spatial;

namespace Prowl.Motion.Benchmarks;

/// <summary>
/// Records what the retargeter, mirror, model space and every node case produce, then checks a later
/// build against the recording. It guards optimizations that must not change results: run it with
/// "write" before a change and "check" after.
/// </summary>
internal static class Golden
{
    private const int Samples = 120;

    public static void Run(string mode, string path)
    {
        Dictionary<string, List<float>> sections = Record();
        if (mode == "write")
        {
            using var writer = new BinaryWriter(File.Create(path));
            writer.Write(sections.Count);
            foreach ((string name, List<float> values) in sections)
            {
                writer.Write(name);
                writer.Write(values.Count);
                foreach (float v in values)
                    writer.Write(v);
            }
            Console.WriteLine($"Wrote {sections.Count} sections to {path}.");
            return;
        }

        using var reader = new BinaryReader(File.OpenRead(path));
        int count = reader.ReadInt32();
        float worst = 0f;
        for (int s = 0; s < count; s++)
        {
            string name = reader.ReadString();
            int length = reader.ReadInt32();
            var expected = new float[length];
            for (int i = 0; i < length; i++)
                expected[i] = reader.ReadSingle();

            if (!sections.TryGetValue(name, out List<float>? actual) || actual.Count != length)
            {
                Console.WriteLine($"{name}: MISSING or length changed");
                worst = float.PositiveInfinity;
                continue;
            }

            float max = 0f;
            for (int i = 0; i < length; i++)
            {
                float diff = MathF.Abs(actual[i] - expected[i]);
                if (!(diff <= max))
                    max = float.IsNaN(diff) ? float.PositiveInfinity : diff;
            }
            worst = MathF.Max(worst, max);
            Console.WriteLine($"{name,-46} max difference {max:E2}");
        }
        Console.WriteLine($"Worst difference {worst:E2}");
    }

    private static Dictionary<string, List<float>> Record()
    {
        Rig rig = Rig.Shared;
        var sections = new Dictionary<string, List<float>>();
        List<float> Section(string name) => sections.TryGetValue(name, out List<float>? list) ? list : sections[name] = new List<float>();

        var random = new Random(1234);
        var human = new HumanPose();
        var rebuilt = new Pose(rig.Skeleton);
        var mirrored = new Pose(rig.Skeleton);
        AnimationClip[] clips = { rig.ClipA, rig.ClipB, rig.ClipC };

        for (int i = 0; i < Samples; i++)
        {
            var pose = new Pose(rig.Skeleton);
            clips[i % clips.Length].GetPose(i / (float)Samples, pose);
            if (i % 2 == 1)
                Perturb(pose, random, i % 4 == 3 ? 1.2f : 0.35f);

            pose.CalculateModelSpaceTransforms();
            for (int b = 0; b < pose.BoneCount; b++)
                Add(Section("Model space"), pose.GetModelSpaceTransform(b));

            Retargeter.RetargetFrom(rig.Avatar, pose, human);
            List<float> encoded = Section("Retarget pose to muscle space");
            Add(encoded, human.BodyPosition);
            Add(encoded, human.BodyRotation);
            foreach (float muscle in human.Muscles)
                encoded.Add(muscle);
            for (int g = 0; g < HumanPose.GoalCount; g++)
            {
                HumanGoalState goal = human.GetGoal((HumanGoal)g);
                Add(encoded, goal.Transform);
                Add(encoded, goal.Pole);
            }

            Retargeter.RetargetTo(rig.Avatar, human, rebuilt);
            AddLocals(Section("Retarget muscle space to pose"), rebuilt);

            PoseMirror.Apply(rig.Avatar, pose, mirrored);
            AddLocals(Section("Mirror"), mirrored);
        }

        foreach ((string name, NodeCase node) in NodeCases.All)
        {
            var graph = new AnimationGraph();
            graph.SetRoot(node.Build(graph, rig));
            AnimationGraphInstance instance = graph.CreateInstance(rig.Avatar);
            node.Prepare?.Invoke(instance, rig);
            Transform3D world = Transform3D.Identity;
            List<float> values = Section("Node: " + name);
            for (int frame = 0; frame < 30; frame++)
            {
                world = new Transform3D(world.position + new Float3(0f, 0f, 1.4f * Rig.DeltaTime), world.rotation, Float3.One);
                instance.Update(Rig.DeltaTime, world);
                Add(values, instance.RootMotionDelta);
            }
            AddLocals(values, instance.Pose);
            for (int c = 0; c < instance.Pose.FloatChannelCount; c++)
                values.Add(instance.Pose.GetFloat(c));
        }
        return sections;
    }

    private static void Perturb(Pose pose, Random random, float radians)
    {
        for (int b = 1; b < pose.BoneCount; b++)
        {
            Transform3D local = pose.GetTransform(b);
            var axis = Float3.Normalize(new Float3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f));
            Quaternion turn = Quaternion.AxisAngle(axis, ((float)random.NextDouble() * 2f - 1f) * radians);
            pose.SetTransform(b, new Transform3D(local.position, Quaternion.Normalize(turn * local.rotation), local.scale));
        }
    }

    private static void AddLocals(List<float> values, Pose pose)
    {
        for (int b = 0; b < pose.BoneCount; b++)
            Add(values, pose.GetTransform(b));
    }

    private static void Add(List<float> values, Transform3D t)
    {
        Add(values, t.position);
        Add(values, t.rotation);
        Add(values, t.scale);
    }

    private static void Add(List<float> values, Float3 v)
    {
        values.Add(v.X);
        values.Add(v.Y);
        values.Add(v.Z);
    }

    // Stored with a positive W, so the same rotation written with the other sign compares equal.
    private static void Add(List<float> values, Quaternion q)
    {
        float sign = q.W < 0f ? -1f : 1f;
        values.Add(q.X * sign);
        values.Add(q.Y * sign);
        values.Add(q.Z * sign);
        values.Add(q.W * sign);
    }
}
