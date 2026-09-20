using Prowl.Clay;
using Prowl.Clay.Importer;

namespace Prowl.Motion.Tests;

/// <summary>
/// Sample/integration tests: load the real humanoid FBX models with Prowl.Clay and build a
/// Prowl.Motion <see cref="Skeleton"/> from them. Clay does the loading; the assertions exercise
/// the (currently stubbed) Prowl.Motion API, so these stay red until the API is implemented.
/// </summary>
public class ModelLoadingTests
{
    public static IEnumerable<object[]> HumanoidModels()
    {
        yield return new object[] { TestAssets.RumbaDancing };
        yield return new object[] { TestAssets.HeadSpinning };
    }

    [ModelTheory]
    [MemberData(nameof(HumanoidModels))]
    public void BuildsSkeletonFromModelNodes(string path)
    {
        Assert.True(File.Exists(path), $"Test model not found: {path}");

        Model model = ModelImporter.Load(path);
        Skeleton skeleton = ClayModelLoader.BuildSkeleton(model);

        Assert.True(skeleton.BoneCount > 0);
    }

    [ModelTheory]
    [MemberData(nameof(HumanoidModels))]
    public void SkeletonBoneCount_MatchesNodeCount(string path)
    {
        Model model = ModelImporter.Load(path);
        Skeleton skeleton = ClayModelLoader.BuildSkeleton(model);

        Assert.Equal(model.Nodes.Count, skeleton.BoneCount);
    }

    [ModelFact]
    public void Skeleton_ContainsHipsBone()
    {
        Model model = ModelImporter.Load(TestAssets.HumanoidModel);
        Skeleton skeleton = ClayModelLoader.BuildSkeleton(model);

        // Mixamo rigs name the root of the skeleton "mixamorig:Hips".
        Assert.NotEqual(Skeleton.InvalidIndex, skeleton.GetBoneIndex(new StringID("mixamorig:Hips")));
    }
}
