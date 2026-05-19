using Prowl.Clay;
using Prowl.Clay.Importer;
using Xunit;

namespace Prowl.Clay.Tests;

/// <summary>
/// Tests for individual post-process steps. Each step is exercised in isolation against a fixed
/// fixture so we can compare Raw vs flag-on output deterministically.
/// </summary>
public sealed class PostProcessTests
{
    [Fact]
    public void FlipUVs_FlipsVCoordinate_AndLeavesUAlone()
    {
        string path = TestModels.Gltf("2.0/BoxTextured/glTF-Binary/BoxTextured.glb");

        var raw     = ModelImporter.Load(path, ModelImporterSettings.Raw);
        var flipped = ModelImporter.Load(path, ModelImporterSettings.Raw with { PostProcess = PostProcessFlags.FlipUVs });

        var rawMesh  = raw.Meshes[0];
        var flipMesh = flipped.Meshes[0];
        Assert.NotNull(rawMesh.UVs[0]);
        Assert.NotNull(flipMesh.UVs[0]);

        for (int i = 0; i < rawMesh.UVs[0]!.Length; i++)
        {
            Assert.Equal(     rawMesh.UVs[0]![i].X, flipMesh.UVs[0]![i].X, precision: 5);
            Assert.Equal(1f - rawMesh.UVs[0]![i].Y, flipMesh.UVs[0]![i].Y, precision: 5);
        }
    }

    [Fact]
    public void FlipWindingOrder_SwapsIndex1And2_OfEveryTriangle()
    {
        string path = TestModels.Gltf("2.0/Box/glTF-Binary/Box.glb");

        var raw     = ModelImporter.Load(path, ModelImporterSettings.Raw);
        var flipped = ModelImporter.Load(path, ModelImporterSettings.Raw with { PostProcess = PostProcessFlags.FlipWindingOrder });

        var rawIdx  = raw.Meshes[0].GetIndices32(0);
        var flipIdx = flipped.Meshes[0].GetIndices32(0);
        Assert.Equal(rawIdx.Length, flipIdx.Length);
        Assert.True(rawIdx.Length % 3 == 0);

        for (int t = 0; t < rawIdx.Length; t += 3)
        {
            Assert.Equal(rawIdx[t + 0], flipIdx[t + 0]);
            Assert.Equal(rawIdx[t + 2], flipIdx[t + 1]);
            Assert.Equal(rawIdx[t + 1], flipIdx[t + 2]);
        }
    }

    [Fact]
    public void GlobalScale_ScalesPositions_ButOneIsIdentity()
    {
        string path = TestModels.Gltf("2.0/Box/glTF-Binary/Box.glb");

        var raw    = ModelImporter.Load(path, ModelImporterSettings.Raw);
        var scaled = ModelImporter.Load(path, ModelImporterSettings.Raw with
        {
            PostProcess = PostProcessFlags.GlobalScale,
            GlobalScale = 10f,
        });

        for (int i = 0; i < raw.Meshes[0].Vertices.Length; i++)
        {
            Assert.Equal(raw.Meshes[0].Vertices[i].X * 10f, scaled.Meshes[0].Vertices[i].X, precision: 4);
            Assert.Equal(raw.Meshes[0].Vertices[i].Y * 10f, scaled.Meshes[0].Vertices[i].Y, precision: 4);
            Assert.Equal(raw.Meshes[0].Vertices[i].Z * 10f, scaled.Meshes[0].Vertices[i].Z, precision: 4);
        }

        // Scale = 1 must be a fast-path no-op regardless of the flag being on.
        var noop = ModelImporter.Load(path, ModelImporterSettings.Raw with
        {
            PostProcess = PostProcessFlags.GlobalScale,
            GlobalScale = 1f,
        });
        for (int i = 0; i < raw.Meshes[0].Vertices.Length; i++)
            Assert.Equal(raw.Meshes[0].Vertices[i].X, noop.Meshes[0].Vertices[i].X, precision: 5);
    }

    [Fact]
    public void LimitBoneWeights_NormalizesWeightsToSumOne_OnEveryVertex()
    {
        // The step normalizes weights to sum to 1 across the four BoneWeight slots. Note: it
        // only re-sorts weights when actually truncating from > limit; on the early-path case
        // (oldInfluences <= limit) it normalizes in place without reordering, so we don't assert
        // slot order here.
        var model = ModelImporter.Load(TestModels.Gltf("2.0/CesiumMan/glTF-Binary/CesiumMan.glb"));
        var mesh = model.Meshes.First(m => m.BoneWeights is not null);

        foreach (var bw in mesh.BoneWeights!)
        {
            float sum = bw.Weight0 + bw.Weight1 + bw.Weight2 + bw.Weight3;
            Assert.InRange(sum, 0.99f, 1.01f);
        }
    }

    [Fact]
    public void RemoveDegenerates_DropsCoincidentIndexTriangles()
    {
        // Synthetic quad with two triangles, one degenerate (0,1,1). After RemoveDegenerates
        // only one triangle survives.
        string gltf = SyntheticGltf.QuadWithDegenerateTri();
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(gltf));
        var settings = ModelImporterSettings.Raw with { PostProcess = PostProcessFlags.RemoveDegenerates };
        var model = ModelImporter.Load(ms, "gltf", settings, new BufferOnlyResolver(SyntheticGltf.QuadBufferBytes));

        Assert.Single(model.Meshes);
        Assert.Equal(3, model.Meshes[0].SubMeshes[0].IndexCount);
    }

    [Fact]
    public void ValidateDataStructure_LogsWarning_OnInvalidMaterialIndex()
    {
        string gltf = SyntheticGltf.SingleTriangleWithBadMaterialIndex();
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(gltf));
        var settings = ModelImporterSettings.Raw with { PostProcess = PostProcessFlags.ValidateDataStructure };
        var model = ModelImporter.Load(ms, "gltf", settings, new BufferOnlyResolver(SyntheticGltf.SingleTriangleBufferBytes));

        Assert.Contains(model.Log.Entries, e => e.Source == "ValidateDataStructure");
    }

    [Fact]
    public void ValidateDataStructure_StrictMode_ThrowsOnInvalidMaterialIndex()
    {
        string gltf = SyntheticGltf.SingleTriangleWithBadMaterialIndex();
        using var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(gltf));
        var settings = ModelImporterSettings.Raw with
        {
            PostProcess = PostProcessFlags.ValidateDataStructure,
            StrictValidation = true,
        };

        Assert.Throws<ImportException>(() => ModelImporter.Load(ms, "gltf", settings, new BufferOnlyResolver(SyntheticGltf.SingleTriangleBufferBytes)));
    }

    [Fact]
    public void ImproveCacheLocality_ReordersIndices_WithoutChangingSetOrCount()
    {
        string path = TestModels.Gltf("2.0/DamagedHelmet/glTF-Binary/DamagedHelmet.glb");

        var baseline  = ModelImporter.Load(path, ModelImporterSettings.Raw);
        var optimized = ModelImporter.Load(path, ModelImporterSettings.Raw with { PostProcess = PostProcessFlags.ImproveCacheLocality });

        Assert.Equal(baseline.Meshes[0].VertexCount,                   optimized.Meshes[0].VertexCount);
        Assert.Equal(baseline.Meshes[0].SubMeshes[0].IndexCount, optimized.Meshes[0].SubMeshes[0].IndexCount);

        // Same set of indices, just reordered - sort-and-compare catches "reorder lost an index".
        var baseSorted = baseline.Meshes[0].GetIndices32(0).OrderBy(x => x).ToArray();
        var optSorted  = optimized.Meshes[0].GetIndices32(0).OrderBy(x => x).ToArray();
        Assert.Equal(baseSorted, optSorted);

        // ...but the actual order must have changed for a non-trivially-sized mesh, otherwise
        // the step did nothing.
        var baseRaw = baseline.Meshes[0].GetIndices32(0);
        var optRaw  = optimized.Meshes[0].GetIndices32(0);
        Assert.False(baseRaw.SequenceEqual(optRaw),
            "ImproveCacheLocality produced an identical index order - step appears to be a no-op.");
    }

    [Fact]
    public void OptimizeGraph_PreservesBoneCount_AndAnimationTargetNodes()
    {
        string path = TestModels.Gltf("2.0/CesiumMan/glTF-Binary/CesiumMan.glb");

        var raw       = ModelImporter.Load(path, ModelImporterSettings.Raw);
        var optimized = ModelImporter.Load(path, ModelImporterSettings.Raw with { PostProcess = PostProcessFlags.OptimizeGraph });

        // Joints must not collapse: bone count is structural and skinning relies on it.
        Assert.Equal(raw.Skins[0].BoneNodeIndices.Length, optimized.Skins[0].BoneNodeIndices.Length);

        // Every animation binding must still target a valid (uncollapsed) node.
        foreach (var clip in optimized.AnimationClips)
            foreach (var b in clip.Bindings)
                Assert.InRange(b.NodeIndex, 0, optimized.Nodes.Count - 1);
    }
}

/// <summary>
/// Minimal synthetic glTF documents used by the post-process tests so the step's effect can be
/// inspected without sample-model noise.
/// </summary>
internal static class SyntheticGltf
{
    public static readonly byte[] QuadBufferBytes          = BuildQuadBuffer();
    public static readonly byte[] SingleTriangleBufferBytes = BuildSingleTriangleBuffer();

    private static byte[] BuildQuadBuffer()
    {
        // 4 positions (3 floats each) followed by 6 ushort indices = 48 + 12 = 60 bytes.
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        foreach (var c in new float[]  { 0, 0, 0,  1, 0, 0,  0, 1, 0,  1, 1, 0 }) bw.Write(c);
        foreach (var i in new ushort[] { 0, 1, 2,  0, 1, 1 })                     bw.Write(i); // second tri is degenerate
        return ms.ToArray();
    }

    public static string QuadWithDegenerateTri()
    {
        string b64 = Convert.ToBase64String(QuadBufferBytes);
        return $$"""
        {
          "asset": { "version": "2.0" },
          "buffers": [{ "uri": "data:application/octet-stream;base64,{{b64}}", "byteLength": 60 }],
          "bufferViews": [
            { "buffer": 0, "byteOffset": 0,  "byteLength": 48, "target": 34962 },
            { "buffer": 0, "byteOffset": 48, "byteLength": 12, "target": 34963 }
          ],
          "accessors": [
            { "bufferView": 0, "componentType": 5126, "count": 4, "type": "VEC3" },
            { "bufferView": 1, "componentType": 5123, "count": 6, "type": "SCALAR" }
          ],
          "meshes": [{ "primitives": [{ "attributes": { "POSITION": 0 }, "indices": 1, "mode": 4 }] }],
          "nodes": [{ "mesh": 0 }],
          "scenes": [{ "nodes": [0] }],
          "scene": 0
        }
        """;
    }

    private static byte[] BuildSingleTriangleBuffer()
    {
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        foreach (var c in new float[] { 0, 0, 0,  1, 0, 0,  0, 1, 0 }) bw.Write(c);
        return ms.ToArray();
    }

    public static string SingleTriangleWithBadMaterialIndex()
    {
        string b64 = Convert.ToBase64String(SingleTriangleBufferBytes);
        return $$"""
        {
          "asset": { "version": "2.0" },
          "buffers": [{ "uri": "data:application/octet-stream;base64,{{b64}}", "byteLength": 36 }],
          "bufferViews": [{ "buffer": 0, "byteOffset": 0, "byteLength": 36, "target": 34962 }],
          "accessors": [{ "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3" }],
          "meshes": [{ "primitives": [{ "attributes": { "POSITION": 0 }, "material": 7 }] }],
          "nodes": [{ "mesh": 0 }],
          "scenes": [{ "nodes": [0] }],
          "scene": 0
        }
        """;
    }
}

/// <summary>Returns the same synthetic byte buffer for any path lookup.</summary>
internal sealed class BufferOnlyResolver : IFileResolver
{
    private readonly byte[] _data;
    public BufferOnlyResolver(byte[] data) => _data = data;
    public string? Resolve(string modelPath, string relativePath) => relativePath;
    public Stream OpenRead(string absolutePath) => new MemoryStream(_data);
    public byte[] ReadAllBytes(string absolutePath) => _data;
    public bool Exists(string absolutePath) => true;
}
