// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Clay;
using Prowl.Clay.Formats.Gltf;
using Prowl.Clay.Importer;
using Prowl.Vector;

using Xunit;

namespace Prowl.Clay.Tests;

/// <summary>Models written as GLB read back through the importer as they were written.</summary>
public sealed class GltfWriterTests
{
    private static readonly ModelImporterSettings s_convertOnly = new() { PostProcess = PostProcessFlags.ConvertCoordinateSystem };

    private static Model RoundTrip(ExportModel model)
    {
        using var stream = new MemoryStream();
        GltfWriter.WriteGlb(model, stream);
        stream.Position = 0;
        return ModelImporter.Load(stream, "glb", s_convertOnly, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static ExportMesh Quad() => new()
    {
        Name = "Quad",
        Positions = [new(0, 0, 0), new(1, 0, 0), new(1, 0, 1), new(0, 0, 1)],
        Normals = [Float3.UnitY, Float3.UnitY, Float3.UnitY, Float3.UnitY],
        UV0 = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)],
        Primitives = { new ExportPrimitive { Indices = [0, 2, 1, 0, 3, 2], Material = 0 } },
    };

    private static float Facing(Float3 a, Float3 b, Float3 c, Float3 normal) => Float3.Dot(Float3.Cross(b - a, c - a), normal);

    [Fact]
    public void AStaticModelReadsBackWithItsTransformGeometryAndMaterial()
    {
        var rotation = Quaternion.FromEuler(new Float3(0.3f, 0.7f, -0.2f));
        var model = new ExportModel { Name = "Crate" };
        model.Nodes.Add(new ExportNode { Name = "Box", Position = new Float3(1, 2, 3), Rotation = rotation, Scale = new Float3(2, 1, 1), Mesh = 0 });
        model.Meshes.Add(Quad());
        model.Textures.Add(new ExportTexture { Name = "Paint", Png = [137, 80, 78, 71, 13, 10, 26, 10] });
        model.Materials.Add(new ExportMaterial
        {
            Name = "Painted",
            BaseColor = new Float4(1f, 0.5f, 0.25f, 1f),
            Metallic = 0.2f,
            Roughness = 0.7f,
            BaseColorTexture = 0,
            TextureOffset = new Float2(0.1f, 0.2f),
            TextureScale = new Float2(2f, 2f),
            Emission = new Float3(1f, 0f, 0f),
            EmissionStrength = 3f,
        });

        Model read = RoundTrip(model);

        ModelNode box = read.FindNode("Box")!;
        Assert.True(Float3.Distance(box.LocalPosition, new Float3(1, 2, 3)) < 1e-5f);
        Assert.True(MathF.Abs(Quaternion.Dot(box.LocalRotation, rotation)) > 0.99999f);
        Assert.True(Float3.Distance(box.LocalScale, new Float3(2, 1, 1)) < 1e-5f);

        Mesh mesh = read.Meshes[box.MeshIndex];
        Assert.Equal(4, mesh.VertexCount);
        Assert.All(model.Meshes[0].Positions, p => Assert.Contains(mesh.Vertices, q => Float3.Distance(p, q) < 1e-5f));
        Assert.All(mesh.Normals!, n => Assert.True(Float3.Distance(n, Float3.UnitY) < 1e-5f));
        uint[] indices = mesh.Indices;
        for (int i = 0; i < indices.Length; i += 3)
            Assert.True(Facing(mesh.Vertices[indices[i]], mesh.Vertices[indices[i + 1]], mesh.Vertices[indices[i + 2]], Float3.UnitY)
                * Facing(new(0, 0, 0), new(1, 0, 1), new(1, 0, 0), Float3.UnitY) > 0f, "a triangle turned over");
        for (int v = 0; v < mesh.VertexCount; v++)
        {
            int source = Array.FindIndex(model.Meshes[0].Positions, p => Float3.Distance(p, mesh.Vertices[v]) < 1e-5f);
            Assert.True(Float2.Distance(mesh.UVs[0]![v], model.Meshes[0].UV0![source]) < 1e-6f);
        }

        Material material = Assert.Single(read.Materials);
        Assert.Equal("Painted", material.Name);
        Assert.Equal(0.5f, material.BaseColor.G, 5);
        Assert.Equal(0.2f, material.Metallic, 5);
        Assert.Equal(0.7f, material.Roughness, 5);
        Assert.Equal(0, material.BaseColorTexture!.TextureIndex);
        Assert.Equal(new Float2(0.1f, 0.2f), material.BaseColorTexture.Offset);
        Assert.Equal(new Float2(2f, 2f), material.BaseColorTexture.Scale);
        Assert.Equal(1f, material.EmissiveFactor.R, 5);
        Assert.Equal(3f, material.EmissiveStrength, 5);
        Assert.Equal([137, 80, 78, 71, 13, 10, 26, 10], read.Textures[0].EncodedBytes);
    }

    [Fact]
    public void ASkinAndMorphTargetsReadBack()
    {
        var model = new ExportModel();
        model.Nodes.Add(new ExportNode { Name = "Body", Mesh = 0, Skin = 0 });
        model.Nodes.Add(new ExportNode { Name = "Hip" });
        model.Nodes.Add(new ExportNode { Name = "Spine", Parent = 1, Position = new Float3(0, 1, 0) });
        ExportMesh quad = Quad();
        quad.Primitives[0].Material = -1;
        quad.Joints = [new(0, 0, 0, 0), new(0, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0)];
        quad.Weights = [new(1, 0, 0, 0), new(1, 0, 0, 0), new(0.5f, 0.5f, 0, 0), new(1, 0, 0, 0)];
        quad.MorphTargets.Add(new ExportMorphTarget { Name = "Smile", DeltaPositions = [new(0.5f, 0, 0), Float3.Zero, Float3.Zero, Float3.Zero], Weight = 0.25f });
        model.Meshes.Add(quad);
        var skin = new ExportSkin { Name = "Rig" };
        skin.Joints.AddRange([1, 2]);
        Float4x4 spineBind = Float4x4.CreateTRS(new Float3(0.3f, -1f, 0.2f), Quaternion.FromEuler(new Float3(0f, 0.4f, 0f)), Float3.One);
        skin.InverseBindMatrices.AddRange([Float4x4.Identity, spineBind]);
        model.Skins.Add(skin);

        Model read = RoundTrip(model);

        Skin rig = Assert.Single(read.Skins);
        Assert.Equal(2, rig.BoneNodeIndices.Length);
        Float4x4 back = rig.InverseBindPoses[1];
        for (int c = 0; c < 4; c++)
            for (int r = 0; r < 4; r++)
                Assert.Equal(spineBind[c][r], back[c][r], 4);

        Mesh mesh = read.Meshes[0];
        int smiling = Array.FindIndex(mesh.Vertices, p => Float3.Distance(p, Float3.Zero) < 1e-5f);
        int mixed = Array.FindIndex(mesh.Vertices, p => Float3.Distance(p, new Float3(1, 0, 1)) < 1e-5f);
        BoneWeight weight = mesh.BoneWeights![mixed];
        float spine = (weight.Index0 == 1 ? weight.Weight0 : 0f) + (weight.Index1 == 1 ? weight.Weight1 : 0f)
                      + (weight.Index2 == 1 ? weight.Weight2 : 0f) + (weight.Index3 == 1 ? weight.Weight3 : 0f);
        Assert.Equal(0.5f, spine, 4);
        BlendShape shape = Assert.Single(mesh.BlendShapes);
        Assert.Equal("Smile", shape.Name);
        Assert.Equal(25f, shape.DefaultWeight, 4);
        Assert.True(Float3.Distance(shape.Frames[0].DeltaVertices[smiling], new Float3(0.5f, 0, 0)) < 1e-5f);
    }
}
