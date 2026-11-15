// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Clay.Importer;
using Prowl.Vector;

namespace Prowl.Clay.Tests;

/// <summary>
/// Tests for the BVH importer.
/// </summary>
public sealed class BvhAnimationTests
{
    [Fact]
    public void BvhFormat_LoadsSingleHierarchy()
    {
        Model model = ModelImporter.Load(TestModels.Bvh("one_root.bvh"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(model.Log.HasAnyWarnings);

        // Check the nodes are as expected.
        Assert.Collection(model.Nodes,
            root =>
            {
                Assert.Equal(0, root.Index);
                Assert.Equal("<RootNode>", root.Name);
                Assert.Null(root.Parent);
            },
            hips =>
            {
                Assert.Equal(1, hips.Index);
                Assert.Equal("Hips", hips.Name);
                Assert.Equal(model.Nodes[0], hips.Parent);
                Assert.Equal(new Float3(0, 0, 0), hips.LocalPosition);
                Assert.Equal(Quaternion.Identity, hips.LocalRotation);
            },
            spine =>
            {
                Assert.Equal(2, spine.Index);
                Assert.Equal("Upper Spine", spine.Name);
                Assert.Same(model.Nodes[1], spine.Parent);
                Assert.Equal(new Float3(0, 20, 0), spine.LocalPosition);
                Assert.Empty(spine.Children);
                Assert.Equal(Quaternion.Identity, spine.LocalRotation);
                Assert.Equal(new Float3(0, 15, 0), spine.Metadata["EndSite"]);
            });

        // Check the animation clip is as expected.
        AnimationClip animation = Assert.Single(model.AnimationClips);
        Assert.Equal("Animation", animation.Name);
        Assert.Equal(0, animation.StartTime);
        Assert.Equal(0.033333f, animation.EndTime);
        Assert.Equal(0.033333f, animation.Duration);

        // Animate hips position
        var hipsPosition = Assert.Single(animation.Bindings, b => b.NodeIndex == 1 && b.Property == AnimatedProperty.Position);
        Assert.Equal(1, hipsPosition.NodeIndex);
        Assert.Equal(AnimatedProperty.Position, hipsPosition.Property);
        Assert.Equal(0, hipsPosition.SubIndex);
        Assert.Equal(3, hipsPosition.Curve.Dimension);
        Assert.Equal(2, hipsPosition.Curve.Count);
        Assert.Collection(hipsPosition.Curve,
            keyFrame =>
            {
                Assert.Equal(0, keyFrame.Time);
                Assert.Equal(ConvertTranslation(new Float3(0, 90, 0)), keyFrame.Value3);
            },
            keyFrame =>
            {
                Assert.Equal(0.033333f, keyFrame.Time);
                Assert.Equal(ConvertTranslation(new Float3(0.5f, 90.5f, 0)), keyFrame.Value3);
            });



        // Animate hips rotation
        var hipsRotation = Assert.Single(animation.Bindings, b => b.NodeIndex == 1 && b.Property == AnimatedProperty.Rotation);
        Assert.Equal(1, hipsRotation.NodeIndex);
        Assert.Equal(AnimatedProperty.Rotation, hipsRotation.Property);
        Assert.Equal(0, hipsRotation.SubIndex);
        Assert.Equal(4, hipsRotation.Curve.Dimension);
        Assert.Equal(2, hipsRotation.Curve.Count);
        Assert.Collection(hipsRotation.Curve,
            keyFrame => Assert.Equal(Quaternion.Identity, keyFrame.Value4),
            keyFrame => Assert.Equal(ConvertRotation(
                Quaternion.AxisAngle(Float3.UnitZ, 5 * Maths.Deg2Rad) *
                Quaternion.AxisAngle(Float3.UnitX, 0 * Maths.Deg2Rad) *
                Quaternion.AxisAngle(Float3.UnitY, 0 * Maths.Deg2Rad)), keyFrame.Value4));

        // Animate spine rotation
        var spineRotation = Assert.Single(animation.Bindings, b => b.NodeIndex == 2 && b.Property == AnimatedProperty.Rotation);
        Assert.Equal(2, spineRotation.NodeIndex);
        Assert.Equal(AnimatedProperty.Rotation, spineRotation.Property);
        Assert.Equal(0, spineRotation.SubIndex);
        Assert.Equal(4, spineRotation.Curve.Dimension);
        Assert.Equal(2, spineRotation.Curve.Count);
        Assert.Collection(spineRotation.Curve,
            keyFrame => Assert.Equal(ConvertRotation(
                Quaternion.AxisAngle(Float3.UnitZ, 12 * Maths.Deg2Rad) *
                Quaternion.AxisAngle(Float3.UnitX, 5 * Maths.Deg2Rad) *
                Quaternion.AxisAngle(Float3.UnitY, -2 * Maths.Deg2Rad)), keyFrame.Value4),
            keyFrame => Assert.Equal(ConvertRotation(
                Quaternion.AxisAngle(Float3.UnitZ, 14 * Maths.Deg2Rad) *
                Quaternion.AxisAngle(Float3.UnitX, 6 * Maths.Deg2Rad) *
                Quaternion.AxisAngle(Float3.UnitY, -1 * Maths.Deg2Rad)), keyFrame.Value4));
    }

    [Fact]
    public void BvhFormat_LoadsMultipleHierarchies()
    {
        Model model = ModelImporter.Load(TestModels.Bvh("two_roots.bvh"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(model.Log.HasAnyWarnings);

        // Check the nodes are as expected.
        Assert.Collection(model.Nodes,
            root =>
            {
                Assert.Equal(0, root.Index);
                Assert.Equal("<RootNode>", root.Name);
                Assert.Null(root.Parent);
            },
            hips =>
            {
                Assert.Equal(1, hips.Index);
                Assert.Equal("CharacterA_Hips", hips.Name);
                Assert.Equal(model.Nodes[0], hips.Parent);
                Assert.Equal(ConvertTranslation(new Float3(0, 0, 0)), hips.LocalPosition);
                Assert.Equal(Quaternion.Identity, hips.LocalRotation);
            },
            spine =>
            {
                Assert.Equal(2, spine.Index);
                Assert.Equal("Spine", spine.Name);
                Assert.Same(model.Nodes[1], spine.Parent);
                Assert.Equal(ConvertTranslation(new Float3(0, 20, 0)), spine.LocalPosition);
                Assert.Empty(spine.Children);
                Assert.Equal(Quaternion.Identity, spine.LocalRotation);
            },
            ball =>
            {
                Assert.Equal(3, ball.Index);
                Assert.Equal("Prop_Ball", ball.Name);
                Assert.Equal(model.Nodes[0], ball.Parent);
                Assert.Equal(ConvertTranslation(new Float3(50, 0, 0)), ball.LocalPosition);
                Assert.Equal(Quaternion.Identity, ball.LocalRotation);
            });
    }

    [Fact]
    public void FormatDetector_CanDetectBvhFromPath()
    {
        // Act
        string? format = FormatDetector.FromPath(TestModels.Bvh("one_root.bvh"));

        // Assert
        Assert.Equal("bvh", format);
    }

    [Fact]
    public void FormatDetector_CanDetectBvhFromStream()
    {
        // Act
        using Stream stream = File.OpenRead(TestModels.Bvh("one_root.bvh"));
        bool success = FormatDetector.TryDetectFromStream(stream, out string format);

        // Assert
        Assert.True(success);
        Assert.Equal("bvh", format);
    }

    // Helpers to convert from right-handed (BVH) to left-handed (Prowl) coordinate system.
    private static Float3 ConvertTranslation(Float3 v) => new(-v.X, v.Y, v.Z);
    private static Quaternion ConvertRotation(Quaternion q) => new(q.X, -q.Y, -q.Z, q.W);
}
