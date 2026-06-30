using System.Text;
using Prowl.Clay;
using Prowl.Clay.Importer;
using Prowl.Vector;
using Xunit;

namespace Prowl.Clay.Tests;

/// <summary>
/// Tests for the OBJ + MTL importer. End-to-end tests load a real .obj fixture; parser-edge-case
/// tests run on small inline OBJ strings (negative indices, v//n, multi-object split, polygon
/// triangulation, vertex-color extension).
/// </summary>
public sealed class ObjTests
{
    [Fact]
    public void Box_Obj_LoadsAsTriangulatedCube()
    {
        var model = ModelImporter.Load(Path.Combine(TestModels.ObjRoot, "box.obj"));

        Assert.NotEmpty(model.Meshes);
        var mesh = model.Meshes[0];
        // 8 unique positions, 6 quad faces -> 12 triangles -> 36 indices after Triangulate.
        Assert.Equal(8, mesh.VertexCount);
        Assert.Equal(36, mesh.SubMeshes[0].IndexCount);
        Assert.Equal(PrimitiveTopology.Triangles, mesh.SubMeshes[0].Topology);
    }

    [Fact]
    public void PositiveIndices_ProduceAOneToOneTriangle()
    {
        const string obj = """
        v 0 0 0
        v 1 0 0
        v 0 1 0
        f 1 2 3
        """;
        var model = LoadInline(obj);
        Assert.Single(model.Meshes);
        var m = model.Meshes[0];
        Assert.Equal(3, m.VertexCount);
        Assert.Equal(new uint[] { 0, 1, 2 }, m.GetIndices32(0));
    }

    [Fact]
    public void NegativeIndices_ResolveRelativeToVertexBufferEnd()
    {
        const string obj = """
        v 0 0 0
        v 1 0 0
        v 0 1 0
        f -3 -2 -1
        """;
        var model = LoadInline(obj);
        Assert.Equal(new uint[] { 0, 1, 2 }, model.Meshes[0].GetIndices32(0));
    }

    [Fact]
    public void FaceWith_v_vn_OnlyFormat_PopulatesNormals_NoUVs()
    {
        const string obj = """
        v 0 0 0
        v 1 0 0
        v 0 1 0
        vn 0 0 1
        vn 0 0 1
        vn 0 0 1
        f 1//1 2//2 3//3
        """;
        var model = LoadInline(obj);
        var m = model.Meshes[0];
        Assert.NotNull(m.Normals);
        Assert.Null(m.UVs[0]);
        for (int i = 0; i < 3; i++)
            Assert.Equal(new Float3(0f, 0f, 1f), m.Normals![i]);
    }

    [Fact]
    public void FaceWith_v_vt_vn_FullFormat_PopulatesUvsAndNormals()
    {
        const string obj = """
        v 0 0 0
        v 1 0 0
        v 0 1 0
        vt 0 0
        vt 1 0
        vt 0 1
        vn 0 0 1
        f 1/1/1 2/2/1 3/3/1
        """;
        var model = LoadInline(obj);
        var m = model.Meshes[0];
        Assert.NotNull(m.UVs[0]);
        Assert.NotNull(m.Normals);
        // FlipUVs is OFF in the LoadInline default, so V stays as authored.
        Assert.Equal(new Float2(0f, 1f), m.UVs[0]![2]);
    }

    [Fact]
    public void Quad_GetsTriangulated_IntoTwoTriangles()
    {
        const string obj = """
        v 0 0 0
        v 1 0 0
        v 1 1 0
        v 0 1 0
        f 1 2 3 4
        """;
        Assert.Equal(6, LoadInline(obj).Meshes[0].SubMeshes[0].IndexCount);
    }

    [Fact]
    public void MultipleObjects_SplitIntoNamedNodes_EachHoldingItsMesh()
    {
        const string obj = """
        v 0 0 0
        v 1 0 0
        v 0 1 0
        v 1 1 0
        v 0 0 1
        v 1 0 1
        o First
        f 1 2 3
        o Second
        f 4 5 6
        """;
        var model = LoadInline(obj);

        var first  = model.FindNode("First");
        var second = model.FindNode("Second");
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.True(first!.MeshIndex  >= 0, "'First' node missing its mesh reference.");
        Assert.True(second!.MeshIndex >= 0, "'Second' node missing its mesh reference.");
    }

    [Fact]
    public void MaterialChangeWithinObject_SplitsIntoSubMeshChildren()
    {
        const string obj = """
        v 0 0 0
        v 1 0 0
        v 0 1 0
        v 1 1 0
        v 2 0 0
        v 2 1 0
        o Object
        usemtl matA
        f 1 2 3
        usemtl matB
        f 4 5 6
        """;
        var model = LoadInline(obj);
        var objectNode = model.FindNode("Object");
        Assert.NotNull(objectNode);
        Assert.Equal(2, objectNode!.Children.Count);
        Assert.True(objectNode.Children.All(c => c.MeshIndex >= 0));
    }

    [Fact]
    public void VertexColorExtension_v_xyz_rgb_PopulatesColors()
    {
        // Blender / MeshLab extension where v lines carry trailing r g b.
        const string obj = """
        v 0 0 0 1 0 0
        v 1 0 0 0 1 0
        v 0 1 0 0 0 1
        f 1 2 3
        """;
        var m = LoadInline(obj).Meshes[0];
        Assert.NotNull(m.Colors);
        Assert.Equal(1f, m.Colors![0].R, precision: 4);
        Assert.Equal(1f, m.Colors[1].G,  precision: 4);
        Assert.Equal(1f, m.Colors[2].B,  precision: 4);
    }

    [Fact]
    public void Mtl_KdNsPmPr_PopulatesBaseColorMetallicRoughness()
    {
        // Write a paired .obj + .mtl into a temp dir so the loader has a real file to walk.
        string tempDir = Path.Combine(Path.GetTempPath(), "Prowl.Clay.MtlTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "test.mtl"), """
            newmtl shiny
            Kd 0.8 0.2 0.1
            Ns 250
            Pm 0.5
            Pr 0.3
            """);
            File.WriteAllText(Path.Combine(tempDir, "test.obj"), """
            mtllib test.mtl
            v 0 0 0
            v 1 0 0
            v 0 1 0
            usemtl shiny
            f 1 2 3
            """);

            var model = ModelImporter.Load(Path.Combine(tempDir, "test.obj"));
            var shiny = model.Materials.First(m => m.Name == "shiny");
            Assert.Equal(0.8f, shiny.BaseColor.R, precision: 4);
            Assert.Equal(0.5f, shiny.Metallic,    precision: 4);
            Assert.Equal(0.3f, shiny.Roughness,   precision: 4);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* tolerate cleanup races */ }
        }
    }

    private static Model LoadInline(string objText)
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(objText));
        // Raw + Triangulate keeps vertex order stable so the assertions can check exact indices.
        var settings = ModelImporterSettings.Raw with { PostProcess = PostProcessFlags.Triangulate };
        return ModelImporter.Load(ms, "obj", settings);
    }
}
