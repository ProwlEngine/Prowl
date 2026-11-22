// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Importers;
using Prowl.Editor.Modeling;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Editor.Test.Modeling;

/// <summary>A model exported as GLB and imported back as any GLB is keeps its shape, UVs, skin and blend shapes.</summary>
public class ModelExportTests : EditorTestHarness
{
    private static PMeshObject Strip()
    {
        var source = new Mesh
        {
            Vertices = [new(0, 0, 0), new(1, 0, 0), new(1, 1, 0), new(0, 1, 0), new(1, 2, 0), new(0, 2, 0)],
            UV = [new(0, 0), new(1, 0), new(1, 0.5f), new(0, 0.5f), new(1, 1), new(0, 1)],
            Indices = [0, 2, 1, 0, 3, 2, 3, 4, 2, 3, 5, 4],
            BoneIndices = [new(0, 0, 0, 0), new(0, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0)],
            BoneWeights = [new(1, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0), new(1, 0, 0, 0)],
        };
        source.BlendShapes = [new BlendShape { Name = "Bulge", Frames = [new BlendShapeFrame { DeltaVertices = [Float3.Zero, Float3.Zero, new(0.5f, 0, 0), Float3.Zero, Float3.Zero, Float3.Zero] }] }];
        var item = new PMeshObject { Name = "Body", Mesh = MeshGeometry.ToGeometryData(source), Position = new Float3(2, 0, 0) };
        item.Bones.Add(new PMeshBone { Name = "Hip" });
        item.Bones.Add(new PMeshBone { Name = "Spine", Parent = 0, Position = new Float3(0, 1, 0), BindPose = Float4x4.CreateTranslation(new Float3(0, -1, 0)) });
        item.Shapes.Add(new PMeshShape { Name = "Bulge", Weight = 30f });
        return item;
    }

    [Fact]
    public void AnExportedModelImportsBackTheSame()
    {
        PMeshObject item = Strip();
        var document = new PMeshDocument { Objects = { item } };
        using (FileStream file = File.Create(AssetAbsolutePath("Exported.glb")))
            ModelExport.WriteGlb(document, file, _ => null, _ => null);
        Assets.Refresh();

        GameObject root = GameObject.InstantiateDetached(Load<PrefabAsset>(Assets.GetEntry("Exported.glb")!.Guid)!);
        SkinnedMeshRenderer skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>().Single();
        Assert.Equal("Body", skinned.GameObject.Name);
        Assert.True(Float3.Distance(skinned.GameObject.Transform.LocalPosition, new Float3(2, 0, 0)) < 1e-4f);

        Mesh mesh = skinned.SharedMesh!;
        using Mesh expected = PMeshImporter.BuildMesh(item);
        Assert.Equal(expected.Indices.Length, mesh.Indices.Length);
        for (int v = 0; v < expected.Vertices.Length; v++)
        {
            int at = Array.FindIndex(mesh.Vertices, p => Float3.Distance(p, expected.Vertices[v]) < 1e-5f);
            Assert.True(at >= 0, $"point {expected.Vertices[v]} is missing");
            Assert.True(Float2.Distance(mesh.UV[at], expected.UV[v]) < 1e-5f, $"UV at {expected.Vertices[v]}: {mesh.UV[at]} against {expected.UV[v]}");
            Assert.True(Float3.Dot(mesh.Normals[at], expected.Normals[v]) > 0.999f);
        }
        static bool FacesItsNormal(Mesh m, int i)
        {
            Float3 a = m.Vertices[m.Indices[i]], b = m.Vertices[m.Indices[i + 1]], c = m.Vertices[m.Indices[i + 2]];
            return Float3.Dot(Float3.Cross(b - a, c - a), m.Normals[m.Indices[i]]) > 0f;
        }
        for (int i = 0; i < mesh.Indices.Length; i += 3)
            Assert.True(FacesItsNormal(mesh, i) == FacesItsNormal(expected, 0), "a triangle turned over");

        Assert.Equal(new[] { "Hip", "Spine" }, skinned.Bones!.Select(b => b!.GameObject.Name));
        Assert.True(Float3.Distance(skinned.Bones![1]!.LocalPosition, new Float3(0, 1, 0)) < 1e-4f);
        Assert.Equal("Bulge", mesh.GetBlendShapeName(0));
        int bulging = Array.FindIndex(mesh.Vertices, p => Float3.Distance(p, new Float3(1, 1, 0)) < 1e-5f);
        Assert.True(Float3.Distance(mesh.BlendShapes[0].Frames[0].DeltaVertices[bulging], new Float3(0.5f, 0, 0)) < 1e-5f);
    }

    private static byte[] Png(byte r, byte g, byte b)
    {
        var pixels = new byte[2 * 2 * 4];
        for (int i = 0; i < 4; i++) (pixels[i * 4], pixels[i * 4 + 1], pixels[i * 4 + 2], pixels[i * 4 + 3]) = (r, g, b, 255);
        using Aperture.Image image = Aperture.Image.FromPixels(pixels, 2, 2, Aperture.PixelFormat.Rgba8);
        return image.Encode(new Aperture.EncodeOptions { Format = Aperture.ImageFormat.Png });
    }

    [Fact]
    public void SeparateMetalnessAndRoughnessMapsArePackedIntoOneImage()
    {
        var material = new Material(Shader.GetDefault(DefaultShader.Lit)) { Name = "Metal" };
        var metal = new Texture2D(2, 2) { Name = "Metal" };
        var rough = new Texture2D(2, 2) { Name = "Rough" };
        material.SetTexture("base_metalness_map", metal);
        material.SetTexture("specular_roughness_map", rough);
        material.SetTextureChannel("specular_roughness_map", 1);
        var pngs = new Dictionary<Texture2D, byte[]> { [metal] = Png(200, 0, 0), [rough] = Png(0, 60, 0) };
        Guid id = Guid.NewGuid();
        PMeshObject item = Strip();
        item.Materials.Add(id);

        Clay.ExportModel model = ModelExport.Build(new PMeshDocument { Objects = { item } }, g => g == id ? material : null, t => pngs[t]);

        Clay.ExportMaterial exported = Assert.Single(model.Materials);
        Assert.True(exported.MetallicRoughnessTexture >= 0);
        using Aperture.Image packed = Aperture.Image.Load(model.Textures[exported.MetallicRoughnessTexture].Png, new Aperture.DecodeOptions { TargetPixelFormat = Aperture.PixelFormat.Rgba8 });
        Assert.Equal(60, packed.Pixels[1]);
        Assert.Equal(200, packed.Pixels[2]);
    }
}
