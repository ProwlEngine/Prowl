using System.Runtime.CompilerServices;
using Prowl.Vector;
using Prowl.Vector.Spatial;
using Prowl.Clay;
using Prowl.Clay.Importer;

namespace Prowl.Motion.Tests;

/// <summary>
/// Paths to the humanoid test models. They are not redistributable, so they live in a local, git ignored
/// Models folder next to the tests, and the tests that need them skip when it is missing.
/// </summary>
internal static class TestAssets
{
    public static string ModelsDir([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "Models"));

    public static bool ModelsPresent => File.Exists(RumbaDancing);

    public static string RumbaDancing => Path.Combine(ModelsDir(), "Rumba Dancing.fbx");
    public static string HeadSpinning => Path.Combine(ModelsDir(), "Head Spinning.fbx");

    // Unreal Engine skeleton (pelvis / thigh_l / spine_01 ...).
    public static string Daredevil => Path.Combine(ModelsDir(), "Daredevil .fbx");

    // Maya / AdvancedSkeleton rig (cn_/lf_/rt_ prefixes, twist-decomposed limbs).
    public static string Delbin => Path.Combine(ModelsDir(), "Delbin.fbx");

    /// <summary>The model used for the humanoid skeleton/retargeting tests.</summary>
    public static string HumanoidModel => RumbaDancing;
}

/// <summary>
/// Bridges a Prowl.Clay imported model into a Prowl.Motion <see cref="Skeleton"/>.
/// Lives in the test project so the core library stays free of any importer dependency.
/// </summary>
internal static class ClayModelLoader
{
    /// <summary>Builds a skeleton from the full model node hierarchy (works for animation-only FBX).</summary>
    public static Skeleton BuildSkeleton(Model model)
    {
        IReadOnlyList<ModelNode> nodes = model.Nodes;
        int count = nodes.Count;

        var ids = new StringID[count];
        var parents = new int[count];
        var referencePose = new Transform3D[count];

        for (int i = 0; i < count; i++)
        {
            ModelNode node = nodes[i];
            ids[i] = new StringID(node.Name);
            parents[i] = node.Parent?.Index ?? Skeleton.InvalidIndex;
            referencePose[i] = new Transform3D(node.LocalPosition, node.LocalRotation, node.LocalScale);
        }

        return new Skeleton(ids, parents, referencePose);
    }

    /// <summary>Loads the default humanoid test model and builds a skeleton from its node hierarchy.</summary>
    public static Skeleton LoadHumanoidSkeleton()
        => BuildSkeleton(ModelImporter.Load(TestAssets.HumanoidModel));

    /// <summary>Builds a skeleton from an explicit skin (the canonical bone set, when present).</summary>
    public static Skeleton BuildSkeleton(Model model, Skin skin)
    {
        int[] boneNodes = skin.BoneNodeIndices;
        int count = boneNodes.Length;

        var nodeToBone = new Dictionary<int, int>(count);
        for (int i = 0; i < count; i++)
            nodeToBone[boneNodes[i]] = i;

        var ids = new StringID[count];
        var parents = new int[count];
        var referencePose = new Transform3D[count];

        for (int i = 0; i < count; i++)
        {
            ModelNode node = model.Nodes[boneNodes[i]];
            ids[i] = new StringID(node.Name);
            referencePose[i] = new Transform3D(node.LocalPosition, node.LocalRotation, node.LocalScale);

            int parentBone = Skeleton.InvalidIndex;
            ModelNode? p = node.Parent;
            while (p is not null)
            {
                if (nodeToBone.TryGetValue(p.Index, out int bi))
                {
                    parentBone = bi;
                    break;
                }
                p = p.Parent;
            }
            parents[i] = parentBone;
        }

        return new Skeleton(ids, parents, referencePose);
    }
}
