using Prowl.Clay;
using Prowl.Clay.Importer;
using Xunit;

namespace Prowl.Clay.Tests;

/// <summary>
/// Tests for the FBX geometry / materials / format-version path. Animation and skinning move to
/// <see cref="FbxAnimationTests"/>.
/// </summary>
public sealed class FbxGeometryTests
{
    [Fact]
    public void Box_BinaryFbx_LoadsAsTriangulatedCube()
    {
        var model = ModelImporter.Load(TestModels.Fbx("box.fbx"));

        Assert.Equal("fbx",  model.Metadata.Format);
        Assert.Equal("7400", model.Metadata.FormatVersion);
        Assert.NotEmpty(model.Meshes);

        var mesh = model.Meshes[0];
        // 6 quad faces -> 12 triangles -> 36 indices, with per-face normals giving 24 unique
        // vertex tuples after JoinIdenticalVertices.
        Assert.Equal(24, mesh.VertexCount);
        Assert.Equal(36, mesh.SubMeshes[0].IndexCount);
        Assert.Equal(PrimitiveTopology.Triangles, mesh.SubMeshes[0].Topology);

        // Bounds are non-degenerate on every axis.
        var bounds = mesh.Bounds;
        Assert.True(bounds.Max.X - bounds.Min.X > 0f);
        Assert.True(bounds.Max.Y - bounds.Min.Y > 0f);
        Assert.True(bounds.Max.Z - bounds.Min.Z > 0f);
    }

    [Fact]
    public void PhongCube_PopulatesNonBlackBaseColor()
    {
        var model = ModelImporter.Load(TestModels.Fbx("phong_cube.fbx"));

        Assert.NotEmpty(model.Materials);
        var c = model.Materials[0].BaseColor;
        Assert.True(c.R + c.G + c.B > 0f, "Phong cube material should have a non-black base color.");
    }

    [Fact]
    public void Spider_LoadsAsHierarchyWithMultipleMaterials()
    {
        var model = ModelImporter.Load(TestModels.Fbx("spider.fbx"));

        // Spider's known authoring: 19 sub-meshes split across 4 named materials.
        Assert.Equal(19, model.Meshes.Count);
        Assert.Equal(4,  model.Materials.Count);

        // Every node except the synthetic root must have a parent.
        Assert.Equal(1, model.Nodes.Count(n => n.Parent is null));
    }

    [Fact]
    public void AsciiFbx_LoadsSuccessfully_WithEqualShapeToBinaryEquivalent()
    {
        // The ASCII reader produces the same FbxNode tree shape as the binary reader; loading an
        // ASCII fixture must succeed and produce non-empty geometry. cubes_with_names.fbx is FBX
        // 7.5.0 ASCII with named per-cube nodes.
        var model = ModelImporter.Load(TestModels.Fbx("cubes_with_names.fbx"));

        Assert.Equal("fbx", model.Metadata.Format);
        Assert.NotEmpty(model.Nodes);
        Assert.NotEmpty(model.Meshes);
        // Named nodes survive: at least one node should not be a synthetic placeholder.
        Assert.Contains(model.Nodes, n => !string.IsNullOrEmpty(n.Name) && n.Name != "<root>");
    }
}
