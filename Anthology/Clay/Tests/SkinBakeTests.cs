// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;

using Prowl.Clay.Importer;

using Xunit;

namespace Prowl.Clay.Tests;

/// <summary>
/// The baked <see cref="BoneWeight"/> holds four influences, so any vertex with more has to be
/// reduced. Taking the first four in file order and leaving the weights short makes them sum to
/// less than one, and under linear blend skinning that collapses the vertex toward the origin, so
/// the mesh visibly deflates.
/// </summary>
public sealed class SkinBakeTests
{
    /// <summary>Packs segments into one buffer, 4-byte aligned, and reports where each landed.</summary>
    private sealed class BufferBuilder
    {
        private readonly List<byte> _bytes = new();
        public List<(int Offset, int Length)> Views { get; } = new();

        public int Add(byte[] data)
        {
            while (_bytes.Count % 4 != 0) _bytes.Add(0);
            Views.Add((_bytes.Count, data.Length));
            _bytes.AddRange(data);
            return Views.Count - 1;
        }

        public string Base64 => Convert.ToBase64String(_bytes.ToArray());
        public int Length => _bytes.Count;
    }

    private static byte[] Floats(params float[] v)
    {
        var b = new byte[v.Length * 4];
        Buffer.BlockCopy(v, 0, b, 0, b.Length);
        return b;
    }

    private static byte[] UShorts(params int[] v)
    {
        var b = new byte[v.Length * 2];
        for (int i = 0; i < v.Length; i++)
            BitConverter.GetBytes((ushort)v[i]).CopyTo(b, i * 2);
        return b;
    }

    /// <summary>
    /// A three-vertex skinned triangle where every vertex carries the same influences. Joints and
    /// weights are supplied in sets of four, so eight values become JOINTS_0/1 and WEIGHTS_0/1.
    /// </summary>
    private static Model LoadSkinned(int[] joints, float[] weights, ModelImporterSettings settings)
    {
        const int vertexCount = 3;
        int sets = joints.Length / 4;

        var buf = new BufferBuilder();
        int posView = buf.Add(Floats(0, 0, 0, 1, 0, 0, 0, 1, 0));
        int idxView = buf.Add(UShorts(0, 1, 2));

        var jointViews = new int[sets];
        var weightViews = new int[sets];
        for (int s = 0; s < sets; s++)
        {
            var j = new List<int>();
            var w = new List<float>();
            for (int v = 0; v < vertexCount; v++)
                for (int k = 0; k < 4; k++)
                {
                    j.Add(joints[s * 4 + k]);
                    w.Add(weights[s * 4 + k]);
                }
            jointViews[s] = buf.Add(UShorts(j.ToArray()));
            weightViews[s] = buf.Add(Floats(w.ToArray()));
        }

        var attributes = new List<string> { "\"POSITION\": 0" };
        var accessors = new List<string>
        {
            $$"""{ "bufferView": {{posView}}, "componentType": 5126, "count": 3, "type": "VEC3" }""",
            $$"""{ "bufferView": {{idxView}}, "componentType": 5123, "count": 3, "type": "SCALAR" }""",
        };
        for (int s = 0; s < sets; s++)
        {
            attributes.Add($"\"JOINTS_{s}\": {accessors.Count}");
            accessors.Add($$"""{ "bufferView": {{jointViews[s]}}, "componentType": 5123, "count": 3, "type": "VEC4" }""");
            attributes.Add($"\"WEIGHTS_{s}\": {accessors.Count}");
            accessors.Add($$"""{ "bufferView": {{weightViews[s]}}, "componentType": 5126, "count": 3, "type": "VEC4" }""");
        }

        var views = buf.Views.Select(v => $$"""{ "buffer": 0, "byteOffset": {{v.Offset}}, "byteLength": {{v.Length}} }""");
        int jointCount = joints.Length;
        var jointNodes = Enumerable.Range(0, jointCount).Select(i => $$"""{ "name": "Bone{{i}}" }""");
        var jointIndices = string.Join(", ", Enumerable.Range(2, jointCount));

        string json = $$"""
        {
          "asset": { "version": "2.0" },
          "scene": 0,
          "scenes": [ { "nodes": [ 0 ] } ],
          "nodes": [
            { "name": "Root", "children": [ 1, {{jointIndices}} ] },
            { "name": "Skinned", "mesh": 0, "skin": 0 },
            {{string.Join(", ", jointNodes)}}
          ],
          "skins": [ { "joints": [ {{jointIndices}} ] } ],
          "meshes": [ { "name": "Body", "primitives": [ { "attributes": { {{string.Join(", ", attributes)}} }, "indices": 1 } ] } ],
          "accessors": [ {{string.Join(", ", accessors)}} ],
          "bufferViews": [ {{string.Join(", ", views)}} ],
          "buffers": [ { "byteLength": {{buf.Length}}, "uri": "data:application/octet-stream;base64,{{buf.Base64}}" } ]
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return ModelImporter.Load(stream, "gltf", settings);
    }

    private static BoneWeight FirstWeight(Model model) => model.Meshes[0].BoneWeights![0];

    private static float Sum(BoneWeight w) => w.Weight0 + w.Weight1 + w.Weight2 + w.Weight3;


    // Eight influences, none tied, so which four survive is unambiguous.
    private static readonly int[] EightJoints = [0, 1, 2, 3, 4, 5, 6, 7];
    private static readonly float[] EightWeights = [0.02f, 0.03f, 0.05f, 0.08f, 0.30f, 0.22f, 0.18f, 0.12f];

    [Fact]
    public void EightInfluences_KeepTheStrongestFour()
    {
        var w = FirstWeight(LoadSkinned(EightJoints, EightWeights, ModelImporterSettings.Raw));

        Assert.Equal([4, 5, 6, 7], new[] { w.Index0, w.Index1, w.Index2, w.Index3 });
    }

    // The regression itself: the four kept weights summed to 0.82, and a vertex whose weights sum to
    // less than one is dragged toward the origin.
    [Fact]
    public void TruncatedInfluences_AreRenormalisedSoTheVertexDoesNotCollapse()
    {
        var w = FirstWeight(LoadSkinned(EightJoints, EightWeights, ModelImporterSettings.Raw));

        Assert.Equal(1f, Sum(w), 4);
        Assert.Equal(0.30f / 0.82f, w.Weight0, 4);
    }

    // GameFast used to omit LimitBoneWeights, which left the bake to truncate.
    [Fact]
    public void GameFast_ProducesUsableSkinning()
    {
        var w = FirstWeight(LoadSkinned(EightJoints, EightWeights, ModelImporterSettings.GameFast));

        Assert.Equal(1f, Sum(w), 4);
        Assert.Equal(4, w.Index0);
    }

    [Fact]
    public void GameQuality_ProducesUsableSkinning()
    {
        var w = FirstWeight(LoadSkinned(EightJoints, EightWeights, ModelImporterSettings.GameQuality));

        Assert.Equal(1f, Sum(w), 4);
        Assert.Equal(4, w.Index0);
    }

    // BoneWeight.Index0 is documented as the strongest influence. The four-influence case takes the
    // step's early-out, which normalised but did not sort.
    [Fact]
    public void FourInfluences_ComeBackStrongestFirst()
    {
        var w = FirstWeight(LoadSkinned([0, 1, 2, 3], [0.1f, 0.5f, 0.2f, 0.2f], ModelImporterSettings.GameQuality));

        Assert.Equal(1, w.Index0);
        Assert.Equal(0.5f, w.Weight0, 4);
    }

    // Nothing is dropped, so the bake leaves the authored weights alone rather than quietly
    // rescaling them. Raw means raw.
    [Fact]
    public void FourInfluences_AreNotRescaledByTheBake()
    {
        var w = FirstWeight(LoadSkinned([0, 1, 2, 3], [0.4f, 0.2f, 0.1f, 0.1f], ModelImporterSettings.Raw));

        Assert.Equal(0.8f, Sum(w), 4);
    }


    /// <summary>
    /// The bake indexes the vertex array by whatever the file supplied. Only the format read used to
    /// be wrapped, so a bad index escaped as a bare IndexOutOfRangeException with no path, no format
    /// and nothing naming the mesh, and callers catching ImportException did not catch it at all.
    /// </summary>
    [Fact]
    public void IndexPastTheEndOfTheVertexArray_IsAnImportException()
    {
        var buf = new List<byte>();
        buf.AddRange(Floats(0, 0, 0, 1, 0, 0, 0, 1, 0));
        buf.AddRange(UShorts(0, 1, 99)); // 99 is well past the end of a three-vertex mesh
        buf.AddRange(new byte[2]);

        string json = $$"""
        {
          "asset": { "version": "2.0" },
          "scene": 0,
          "scenes": [ { "nodes": [ 0 ] } ],
          "nodes": [ { "name": "N", "mesh": 0 } ],
          "meshes": [ { "name": "Body", "primitives": [ { "attributes": { "POSITION": 0 }, "indices": 1 } ] } ],
          "accessors": [
            { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3" },
            { "bufferView": 1, "componentType": 5123, "count": 3, "type": "SCALAR" }
          ],
          "bufferViews": [
            { "buffer": 0, "byteOffset": 0, "byteLength": 36 },
            { "buffer": 0, "byteOffset": 36, "byteLength": 6 }
          ],
          "buffers": [ { "byteLength": {{buf.Count}}, "uri": "data:application/octet-stream;base64,{{Convert.ToBase64String(buf.ToArray())}}" } ]
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var ex = Assert.Throws<ImportException>(() => ModelImporter.Load(stream, "gltf", ModelImporterSettings.Raw));

        Assert.Contains("Body", ex.Message);
        Assert.Contains("99", ex.Message);
    }

    // A failure raised deep in the reader knows what is wrong but not which file it came from, so
    // the entry point attaches that on the way out.
    [Fact]
    public void FailureRaisedWithoutContext_PicksUpTheFormat()
    {
        string json = """
        {
          "asset": { "version": "2.0" },
          "scene": 0,
          "scenes": [ { "nodes": [ 0 ] } ],
          "nodes": [ { "name": "A", "children": [ 0 ] } ]
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var ex = Assert.Throws<ImportException>(() => ModelImporter.Load(stream, "gltf", ModelImporterSettings.Raw));

        Assert.Equal("gltf", ex.Format);
    }
}
