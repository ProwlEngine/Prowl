using ClayClip = Prowl.Clay.AnimationClip;
using Prowl.Clay.Importer;
using Prowl.Clay;
using Prowl.Vector.Spatial;
using Prowl.Vector;

namespace Prowl.Motion.Tests;

/// <summary>Loading real models through Clay: skeletons, clips, skinning and retargeting them.</summary>
public class ContentPipeline_Tests
{
    private static Pose SampleClayClip(Skeleton skeleton, ClayClip clip, float time)
    {
        var pose = new Pose(skeleton);
        pose.SetToReferencePose(calculateModelSpace: false);
        foreach (AnimationBinding binding in clip.Bindings)
        {
            int bone = binding.NodeIndex;
            if (!skeleton.IsValidBoneIndex(bone))
                continue;

            Transform3D local = pose.GetTransform(bone);
            switch (binding.Property)
            {
                case AnimatedProperty.Position:
                    local = new Transform3D(binding.Curve.EvaluateFloat3(time), local.rotation, local.scale);
                    break;
                case AnimatedProperty.Rotation:
                    local = new Transform3D(local.position, binding.Curve.EvaluateQuaternion(time), local.scale);
                    break;
                case AnimatedProperty.Scale:
                    local = new Transform3D(local.position, local.rotation, binding.Curve.EvaluateFloat3(time));
                    break;
                default:
                    continue;
            }
            pose.SetTransform(bone, local);
        }
        return pose;
    }

    private static float Accumulate(ref Float3 sum, Float3 vertex, Float4x4[] matrices, int index, float weight)
    {
        if (weight <= 0f || index < 0 || index >= matrices.Length)
            return 0f;
        Float3 p = Float4x4.TransformPoint(vertex, matrices[index]);
        sum = new Float3(sum.X + p.X * weight, sum.Y + p.Y * weight, sum.Z + p.Z * weight);
        return weight;
    }

    public static IEnumerable<object[]> HumanoidModels()
    {
        yield return new object[] { TestAssets.RumbaDancing };
        yield return new object[] { TestAssets.HeadSpinning };
    }

    [ModelFact]
    public void RumbaModel_LoadsClipsAndIsHumanoid()
    {
        Model model = ModelImporter.Load(TestAssets.RumbaDancing);
        Skeleton skeleton = ClayModelLoader.BuildSkeleton(model);
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);

        Assert.True(avatar.IsHuman);
        Assert.NotEmpty(model.AnimationClips);
        Assert.True(model.AnimationClips[0].Duration > 0f);
        Assert.NotEmpty(model.AnimationClips[0].Bindings);
    }

    [ModelFact]
    public void AnimationActuallyMovesBones()
    {
        Model model = ModelImporter.Load(TestAssets.RumbaDancing);
        Skeleton skeleton = ClayModelLoader.BuildSkeleton(model);
        ClayClip clip = model.AnimationClips[0];

        Pose rest = SampleClayClip(skeleton, clip, 0f);
        Pose mid = SampleClayClip(skeleton, clip, clip.Duration * 0.5f);

        bool anyMoved = false;
        for (int b = 0; b < skeleton.BoneCount; b++)
        {
            float dot = MathF.Abs(Quaternion.Dot(rest.GetTransform(b).rotation, mid.GetTransform(b).rotation));
            if (dot < 0.999f)
            {
                anyMoved = true;
                break;
            }
        }
        Assert.True(anyMoved, "Expected the dancing animation to rotate at least one bone.");
    }

    [ModelFact]
    public void CpuSkinning_ProducesFiniteDeformedVertices()
    {
        Model model = ModelImporter.Load(TestAssets.RumbaDancing);
        Skeleton skeleton = ClayModelLoader.BuildSkeleton(model);
        ClayClip clip = model.AnimationClips[0];

        // Find a skinned mesh node.
        ModelNode? meshNode = null;
        foreach (ModelNode n in model.Nodes)
        {
            if (n.MeshIndex >= 0 && n.SkinIndex >= 0)
            {
                meshNode = n;
                break;
            }
        }
        Assert.NotNull(meshNode);

        Mesh mesh = model.Meshes[meshNode!.MeshIndex];
        Skin skin = model.Skins[meshNode.SkinIndex];
        Assert.NotNull(mesh.BoneWeights);

        Pose pose = SampleClayClip(skeleton, clip, clip.Duration * 0.5f);
        pose.CalculateModelSpaceTransforms();

        var skinMatrices = new Float4x4[skin.BoneNodeIndices.Length];
        for (int j = 0; j < skinMatrices.Length; j++)
            skinMatrices[j] = pose.GetModelSpaceTransform(skin.BoneNodeIndices[j]).ToMatrix() * skin.InverseBindPoses[j];

        int sampleCount = Math.Min(200, mesh.Vertices.Length);
        bool anyMoved = false;
        for (int i = 0; i < sampleCount; i++)
        {
            BoneWeight w = mesh.BoneWeights![i];
            Float3 sum = default;
            float total = 0f;
            total += Accumulate(ref sum, mesh.Vertices[i], skinMatrices, w.Index0, w.Weight0);
            total += Accumulate(ref sum, mesh.Vertices[i], skinMatrices, w.Index1, w.Weight1);
            total += Accumulate(ref sum, mesh.Vertices[i], skinMatrices, w.Index2, w.Weight2);
            total += Accumulate(ref sum, mesh.Vertices[i], skinMatrices, w.Index3, w.Weight3);
            Float3 skinned = total > 1e-5f ? new Float3(sum.X / total, sum.Y / total, sum.Z / total) : mesh.Vertices[i];

            Assert.False(float.IsNaN(skinned.X) || float.IsNaN(skinned.Y) || float.IsNaN(skinned.Z));
            if (Float3.Distance(skinned, mesh.Vertices[i]) > 1e-3f)
                anyMoved = true;
        }
        Assert.True(anyMoved, "Expected skinning at a mid-animation frame to displace some vertices.");
    }

    [ModelFact]
    public void RetargetRoundTrip_OnRealClip_ReproducesPose()
    {
        Model model = ModelImporter.Load(TestAssets.RumbaDancing);
        Skeleton skeleton = ClayModelLoader.BuildSkeleton(model);
        Avatar avatar = AvatarBuilder.BuildAutomatic(skeleton);
        ClayClip clip = model.AnimationClips[0];

        Pose source = SampleClayClip(skeleton, clip, clip.Duration * 0.33f);
        source.CalculateModelSpaceTransforms();

        var human = new HumanPose();
        Retargeter.RetargetFrom(avatar, source, human);
        var result = new Pose(skeleton);
        Retargeter.RetargetTo(avatar, human, result);

        int spine = avatar.Humanoid!.GetSkeletonBoneIndex(HumanBodyBone.Spine);
        float dot = MathF.Abs(Quaternion.Dot(source.GetTransform(spine).rotation, result.GetTransform(spine).rotation));
        Assert.True(dot > 0.99f);
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
