using Prowl.Clay;
using Prowl.Clay.Importer;
using Xunit;

namespace Prowl.Clay.Tests;

/// <summary>
/// End-to-end tests for glTF animation, skinning, and morph targets against the Khronos sample
/// models. Pure-API <see cref="AnimationCurve"/> tests live in <see cref="AnimationCurveTests"/>.
/// </summary>
public sealed class GltfAnimationTests
{
    [Fact]
    public void BoxAnimated_HasClipWithPositionAndRotationBindings()
    {
        var model = ModelImporter.Load(TestModels.Gltf("2.0/BoxAnimated/glTF-Binary/BoxAnimated.glb"));

        Assert.NotEmpty(model.AnimationClips);
        var clip = model.AnimationClips[0];
        Assert.True(clip.Duration > 0f);
        Assert.NotEmpty(clip.Bindings);
        Assert.Contains(clip.Bindings, b => b.Property == AnimatedProperty.Position);
        Assert.Contains(clip.Bindings, b => b.Property == AnimatedProperty.Rotation);
    }

    [Fact]
    public void BoxAnimated_RotationCurve_StaysUnitLengthAcrossEvaluation()
    {
        var model = ModelImporter.Load(TestModels.Gltf("2.0/BoxAnimated/glTF-Binary/BoxAnimated.glb"));
        var clip = model.AnimationClips[0];
        var rot = clip.Bindings.First(b => b.Property == AnimatedProperty.Rotation);

        foreach (float t in new[] { 0f, clip.Duration * 0.5f, clip.Duration })
        {
            var q = rot.Curve.EvaluateQuaternion(t);
            float len = MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W);
            Assert.InRange(len, 0.999f, 1.001f);
        }
    }

    [Fact]
    public void CesiumMan_Skin_HasValidJointReferences_AndNormalizedWeights()
    {
        var model = ModelImporter.Load(TestModels.Gltf("2.0/CesiumMan/glTF-Binary/CesiumMan.glb"));

        Assert.NotEmpty(model.Skins);
        Assert.NotEmpty(model.AnimationClips);

        var skin = model.Skins[0];
        Assert.NotEmpty(skin.BoneNodeIndices);
        Assert.Equal(skin.BoneNodeIndices.Length, skin.InverseBindPoses.Length);
        foreach (var idx in skin.BoneNodeIndices)
            Assert.InRange(idx, 0, model.Nodes.Count - 1);

        // The skinned mesh should mirror the skin's bone count in BindPoses and carry normalized
        // BoneWeights for every vertex (post LimitBoneWeights).
        var skinnedMesh = model.Meshes.First(m => m.BoneWeights is not null);
        Assert.NotNull(skinnedMesh.BindPoses);
        Assert.Equal(skin.InverseBindPoses.Length, skinnedMesh.BindPoses!.Length);

        // Every vertex - not just the first 50 - must have weights summing to ~1.
        foreach (var bw in skinnedMesh.BoneWeights!)
        {
            float sum = bw.Weight0 + bw.Weight1 + bw.Weight2 + bw.Weight3;
            Assert.InRange(sum, 0.99f, 1.01f);
        }
    }

    [Fact]
    public void RiggedFigure_PopulateSkeletons_LeavesValidRootIndex()
    {
        var model = ModelImporter.Load(TestModels.Gltf("2.0/RiggedFigure/glTF-Binary/RiggedFigure.glb"));

        Assert.NotEmpty(model.Skins);
        Assert.InRange(model.Skins[0].RootNodeIndex, 0, model.Nodes.Count - 1);
    }

    [Fact]
    public void AnimatedMorphCube_HasBlendShapes_AndWeightAnimation()
    {
        var model = ModelImporter.Load(TestModels.Gltf("2.0/AnimatedMorphCube/glTF-Binary/AnimatedMorphCube.glb"));

        Assert.NotEmpty(model.Meshes);
        var mesh = model.Meshes[0];
        Assert.NotEmpty(mesh.BlendShapes);
        foreach (var bs in mesh.BlendShapes)
        {
            Assert.NotEmpty(bs.Frames);
            Assert.Equal(mesh.VertexCount, bs.Frames[0].DeltaVertices.Length);
        }

        Assert.NotEmpty(model.AnimationClips);
        Assert.Contains(
            model.AnimationClips[0].Bindings,
            b => b.Property == AnimatedProperty.BlendShapeWeight);
    }

    [Fact]
    public void InterpolationTest_SurfacesStepLinearAndCubicSpline()
    {
        var model = ModelImporter.Load(TestModels.Gltf("2.0/InterpolationTest/glTF/InterpolationTest.gltf"));

        var seen = new HashSet<AnimationInterpolation>();
        foreach (var clip in model.AnimationClips)
            foreach (var b in clip.Bindings)
                seen.Add(b.Curve.Interpolation);

        Assert.Contains(AnimationInterpolation.Step,        seen);
        Assert.Contains(AnimationInterpolation.Linear,      seen);
        Assert.Contains(AnimationInterpolation.CubicSpline, seen);
    }
}
