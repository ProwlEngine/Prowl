using Prowl.Vector;

namespace Prowl.Clay;

/// <summary>
/// Immutable mesh data: a single shared vertex buffer with parallel attribute streams plus one
/// or more <see cref="SubMesh"/>es, each with its own index range and material.
/// </summary>
public sealed class Mesh
{
    /// <summary>Mesh name, taken from the source file when available.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Number of vertices in <see cref="Vertices"/> and every parallel attribute stream.</summary>
    public int VertexCount => Vertices.Length;

    /// <summary>Vertex positions in local mesh space.</summary>
    public required Float3[] Vertices { get; init; }

    /// <summary>Vertex normals, or <c>null</c> if the source did not provide them and none were generated.</summary>
    public Float3[]? Normals { get; init; }

    /// <summary>
    /// Vertex tangents in the MikkTSpace convention: xyz is the tangent direction and w is the
    /// bitangent sign (+1 or -1). Bitangent is reconstructed in the shader as <c>cross(N, T) * T.w</c>.
    /// </summary>
    public Float4[]? Tangents { get; init; }

    /// <summary>Vertex colors for color channel 0, or <c>null</c> when absent.</summary>
    public Color[]? Colors { get; init; }

    /// <summary>
    /// Texture coordinate channels (UV0..UV7). Unused slots are <c>null</c>. Slot 0 is the
    /// primary UV; subsequent slots typically hold lightmap, decal, or detail UVs.
    /// </summary>
    public Float2[]?[] UVs { get; init; } = new Float2[]?[MaxUVChannels];

    /// <summary>Maximum number of UV channels supported per mesh.</summary>
    public const int MaxUVChannels = 8;

    /// <summary>Per-vertex bone weights, or <c>null</c> if this mesh is not skinned.</summary>
    public BoneWeight[]? BoneWeights { get; init; }

    /// <summary>
    /// Inverse-bind matrices, one per joint in the associated <see cref="Skin"/>.
    /// Maps a vertex from mesh-local space to the joint's local space at bind time.
    /// </summary>
    public Float4x4[]? BindPoses { get; init; }

    /// <summary>Sub-meshes (index ranges + material assignments).</summary>
    public required SubMesh[] SubMeshes { get; init; }

    /// <summary>Morph targets (blend shapes); empty when none were authored.</summary>
    public BlendShape[] BlendShapes { get; init; } = Array.Empty<BlendShape>();

    /// <summary>Local-space axis-aligned bounding box of all vertex positions.</summary>
    public Bounds Bounds { get; init; }

    /// <summary>
    /// True when at least one <see cref="SubMesh"/> requires 32-bit indices to address its vertex range.
    /// Engines can use this to pick the index format when uploading.
    /// </summary>
    public bool Has32BitIndices { get; init; }

    /// <summary>
    /// Backing index buffer shared across all sub-meshes. Each <see cref="SubMesh"/> reads
    /// <c>IndexCount</c> entries starting at <c>IndexStart</c>.
    /// </summary>
    public required uint[] Indices { get; init; }

    /// <summary>
    /// Returns a copy of the indices for a specific sub-mesh as 32-bit unsigned integers.
    /// </summary>
    public uint[] GetIndices32(int subMeshIndex)
    {
        var sub = SubMeshes[subMeshIndex];
        uint[] result = new uint[sub.IndexCount];
        Array.Copy(Indices, sub.IndexStart, result, 0, sub.IndexCount);
        return result;
    }

    /// <summary>
    /// Returns a copy of the indices for a specific sub-mesh as 16-bit unsigned integers.
    /// Throws when any referenced index does not fit in 16 bits.
    /// </summary>
    public ushort[] GetIndices16(int subMeshIndex)
    {
        var sub = SubMeshes[subMeshIndex];
        ushort[] result = new ushort[sub.IndexCount];
        for (int i = 0; i < sub.IndexCount; i++)
        {
            uint v = Indices[sub.IndexStart + i];
            if (v > ushort.MaxValue)
                throw new InvalidOperationException(
                    $"Mesh '{Name}' sub-mesh {subMeshIndex} contains index {v} which does not fit in 16 bits.");
            result[i] = (ushort)v;
        }
        return result;
    }
}
