using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>A per vertex stream a mesh can supply. Every stream has one canonical GPU format.</summary>
public enum VertexSemantic : byte
{
    Position, Normal, Tangent, Color, UV0, UV1, UV2, UV3, BoneIndices, BoneWeights, Custom0, Custom1
}

/// <summary>One stream of source vertex data, in any supported source format.</summary>
public readonly struct VertexStream
{
    public VertexSemantic Semantic { get; init; }

    /// <summary>Format of <see cref="Data"/>, converted to the stream's canonical format at upload.</summary>
    public VertexElementFormat Format { get; init; }

    /// <summary>Tightly packed source data, one element per vertex.</summary>
    public ReadOnlyMemory<byte> Data { get; init; }
}

/// <summary>A range of a mesh's indices drawn with one material.</summary>
public readonly struct SubMesh
{
    public uint IndexStart { get; init; }
    public uint IndexCount { get; init; }
    public PrimitiveTopology Topology { get; init; }
}

/// <summary>CPU description of a mesh. Graphite keeps no copy of it after upload.</summary>
public sealed class MeshData
{
    public uint VertexCount;
    public VertexStream[] Streams = [];

    /// <summary>Indices into the vertices, 32 bit. Sources with 16 bit indices widen before.</summary>
    public ReadOnlyMemory<uint> Indices;

    public SubMesh[] SubMeshes = [];
}

/// <summary>A part of a submesh drawn and culled as a unit, about <see cref="MeshLayout.ChunkTriangles"/> triangles.</summary>
internal readonly record struct MeshChunk(int SubMesh, uint IndexStart, uint IndexCount, AABB Bounds);

/// <summary>
/// A mesh converted to the canonical streams and split into chunks, ready to copy into a geometry page. Each stream
/// is a run of 32 bit words; positions are their own stream so depth only passes read 12 bytes a vertex.
/// </summary>
internal sealed class MeshLayout
{
    /// <summary>Triangles per chunk. Big enough that per chunk culling stays cheap, small enough to cull big meshes finely.</summary>
    public const int ChunkTriangles = 2048;

    /// <summary>Number of canonical streams.</summary>
    public const int StreamCount = 12;

    private static readonly uint[] s_wordsPerVertex = [3, 3, 4, 1, 2, 2, 2, 2, 2, 2, 4, 4];

    /// <summary>Words of one vertex in a stream's canonical format.</summary>
    public static uint WordsPerVertex(VertexSemantic semantic) => s_wordsPerVertex[(int)semantic];

    public uint VertexCount { get; }

    /// <summary>Canonical data per stream, null for streams the mesh does not supply.</summary>
    public uint[]?[] Streams { get; } = new uint[StreamCount][];

    /// <summary>Bit per <see cref="VertexSemantic"/> the mesh supplies.</summary>
    public uint StreamsPresent { get; }

    public uint[] Indices { get; }
    public SubMesh[] SubMeshes { get; }
    public MeshChunk[] Chunks { get; }
    public AABB Bounds { get; }

    /// <summary>Total words of vertex data across the present streams.</summary>
    public uint VertexWords { get; }

    public MeshLayout(MeshData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        VertexCount = data.VertexCount;

        foreach (VertexStream stream in data.Streams)
        {
            if (Streams[(int)stream.Semantic] != null)
                throw new ArgumentException($"The mesh supplies the {stream.Semantic} stream twice.");

            uint[] canonical = Convert(stream, data.VertexCount);
            Streams[(int)stream.Semantic] = canonical;
            StreamsPresent |= 1u << (int)stream.Semantic;
            VertexWords += (uint)canonical.Length;
        }

        uint[]? positions = Streams[(int)VertexSemantic.Position]
            ?? throw new ArgumentException("A mesh needs a Position stream.");

        Indices = data.Indices.ToArray();
        foreach (uint index in Indices)
        {
            if (index >= data.VertexCount)
                throw new ArgumentException($"Index {index} is past the {data.VertexCount} vertices.");
        }

        SubMeshes = data.SubMeshes.Length > 0
            ? (SubMesh[])data.SubMeshes.Clone()
            : [new SubMesh { IndexStart = 0, IndexCount = (uint)Indices.Length, Topology = PrimitiveTopology.TriangleList }];

        List<MeshChunk> chunks = new();
        AABB bounds = Empty;
        for (int s = 0; s < SubMeshes.Length; s++)
        {
            SubMesh sub = SubMeshes[s];
            if ((ulong)sub.IndexStart + sub.IndexCount > (ulong)Indices.Length)
                throw new ArgumentException($"Submesh {s} reaches past the {Indices.Length} indices.");

            uint chunkIndices = ChunkIndexCount(sub.Topology);
            for (uint start = 0; start < sub.IndexCount; start += chunkIndices)
            {
                uint count = Math.Min(chunkIndices, sub.IndexCount - start);
                AABB chunkBounds = BoundsOf(positions, Indices.AsSpan((int)(sub.IndexStart + start), (int)count));
                chunks.Add(new MeshChunk(s, sub.IndexStart + start, count, chunkBounds));
                bounds.Encapsulate(chunkBounds);
            }
        }

        Chunks = [.. chunks];
        Bounds = chunks.Count > 0 ? bounds : new AABB(Float3.Zero, Float3.Zero);
    }

    private static AABB Empty => new() { Min = new Float3(float.MaxValue), Max = new Float3(float.MinValue) };

    // Strips continue across chunk edges, so they are not split.
    private static uint ChunkIndexCount(PrimitiveTopology topology) => topology switch
    {
        PrimitiveTopology.TriangleList => ChunkTriangles * 3,
        PrimitiveTopology.LineList => ChunkTriangles * 2,
        PrimitiveTopology.PointList => ChunkTriangles,
        _ => uint.MaxValue,
    };

    private static AABB BoundsOf(uint[] positions, ReadOnlySpan<uint> indices)
    {
        AABB bounds = Empty;
        foreach (uint index in indices)
        {
            int at = (int)index * 3;
            bounds.Encapsulate(new Float3(
                BitConverter.UInt32BitsToSingle(positions[at]),
                BitConverter.UInt32BitsToSingle(positions[at + 1]),
                BitConverter.UInt32BitsToSingle(positions[at + 2])));
        }
        return bounds;
    }

    private static uint[] Convert(in VertexStream stream, uint vertexCount)
    {
        uint words = WordsPerVertex(stream.Semantic);
        uint sourceSize = stream.Format.GetSizeInBytes();
        if ((ulong)stream.Data.Length < (ulong)sourceSize * vertexCount)
            throw new ArgumentException($"The {stream.Semantic} stream holds {stream.Data.Length} bytes but {vertexCount} vertices of {stream.Format} need {sourceSize * vertexCount}.");

        uint[] output = new uint[words * vertexCount];
        ReadOnlySpan<byte> source = stream.Data.Span;
        Kind kind = KindOf(stream.Semantic);

        if (kind == Kind.Float && IsFloat32(stream.Format) && ComponentsOf(stream.Format) == words)
        {
            MemoryMarshal.Cast<byte, uint>(source[..(int)(words * 4 * vertexCount)]).CopyTo(output);
            return output;
        }

        Span<float> components = stackalloc float[4];
        for (uint v = 0; v < vertexCount; v++)
        {
            ReadOnlySpan<byte> element = source.Slice((int)(v * sourceSize), (int)sourceSize);
            int count = Read(stream.Format, element, components);
            int at = (int)(v * words);
            switch (kind)
            {
                case Kind.Float:
                    for (int c = 0; c < words; c++)
                        output[at + c] = BitConverter.SingleToUInt32Bits(c < count ? components[c] : DefaultComponent(stream.Semantic, c));
                    break;
                case Kind.Unorm8x4:
                    output[at] = PackUnorm8(components, count);
                    break;
                case Kind.Uint16x4:
                    output[at] = (uint)Clamp16(components[0]) | ((uint)Clamp16(count > 1 ? components[1] : 0) << 16);
                    output[at + 1] = (uint)Clamp16(count > 2 ? components[2] : 0) | ((uint)Clamp16(count > 3 ? components[3] : 0) << 16);
                    break;
                case Kind.Unorm16x4:
                    output[at] = PackUnorm16(components[0]) | (PackUnorm16(count > 1 ? components[1] : 0) << 16);
                    output[at + 1] = PackUnorm16(count > 2 ? components[2] : 0) | (PackUnorm16(count > 3 ? components[3] : 0) << 16);
                    break;
            }
        }

        return output;
    }

    private enum Kind { Float, Unorm8x4, Uint16x4, Unorm16x4 }

    private static Kind KindOf(VertexSemantic semantic) => semantic switch
    {
        VertexSemantic.Color => Kind.Unorm8x4,
        VertexSemantic.BoneIndices => Kind.Uint16x4,
        VertexSemantic.BoneWeights => Kind.Unorm16x4,
        _ => Kind.Float,
    };

    // A tangent without a stored handedness is right handed.
    private static float DefaultComponent(VertexSemantic semantic, int component) => semantic == VertexSemantic.Tangent && component == 3 ? 1f : 0f;

    private static bool IsFloat32(VertexElementFormat format)
        => format is VertexElementFormat.Float1 or VertexElementFormat.Float2 or VertexElementFormat.Float3 or VertexElementFormat.Float4;

    private static int ComponentsOf(VertexElementFormat format) => format switch
    {
        VertexElementFormat.Float1 or VertexElementFormat.UInt1 or VertexElementFormat.Int1 or VertexElementFormat.Half1 => 1,
        VertexElementFormat.Float2 or VertexElementFormat.UInt2 or VertexElementFormat.Int2 or VertexElementFormat.Half2
            or VertexElementFormat.Byte2_Norm or VertexElementFormat.Byte2 or VertexElementFormat.SByte2_Norm or VertexElementFormat.SByte2
            or VertexElementFormat.UShort2_Norm or VertexElementFormat.UShort2 or VertexElementFormat.Short2_Norm or VertexElementFormat.Short2 => 2,
        VertexElementFormat.Float3 or VertexElementFormat.UInt3 or VertexElementFormat.Int3 => 3,
        _ => 4,
    };

    // Reads one source element into floats, normalised formats to their unit range and integers to their value.
    private static int Read(VertexElementFormat format, ReadOnlySpan<byte> e, Span<float> c)
    {
        int count = ComponentsOf(format);
        for (int i = 0; i < count; i++)
        {
            c[i] = format switch
            {
                VertexElementFormat.Float1 or VertexElementFormat.Float2 or VertexElementFormat.Float3 or VertexElementFormat.Float4
                    => BinaryPrimitives.ReadSingleLittleEndian(e[(i * 4)..]),
                VertexElementFormat.Half1 or VertexElementFormat.Half2 or VertexElementFormat.Half4
                    => (float)BinaryPrimitives.ReadHalfLittleEndian(e[(i * 2)..]),
                VertexElementFormat.Byte2_Norm or VertexElementFormat.Byte4_Norm => e[i] / 255f,
                VertexElementFormat.Byte2 or VertexElementFormat.Byte4 => e[i],
                VertexElementFormat.SByte2_Norm or VertexElementFormat.SByte4_Norm => Math.Max((sbyte)e[i] / 127f, -1f),
                VertexElementFormat.SByte2 or VertexElementFormat.SByte4 => (sbyte)e[i],
                VertexElementFormat.UShort2_Norm or VertexElementFormat.UShort4_Norm => BinaryPrimitives.ReadUInt16LittleEndian(e[(i * 2)..]) / 65535f,
                VertexElementFormat.UShort2 or VertexElementFormat.UShort4 => BinaryPrimitives.ReadUInt16LittleEndian(e[(i * 2)..]),
                VertexElementFormat.Short2_Norm or VertexElementFormat.Short4_Norm => Math.Max(BinaryPrimitives.ReadInt16LittleEndian(e[(i * 2)..]) / 32767f, -1f),
                VertexElementFormat.Short2 or VertexElementFormat.Short4 => BinaryPrimitives.ReadInt16LittleEndian(e[(i * 2)..]),
                VertexElementFormat.UInt1 or VertexElementFormat.UInt2 or VertexElementFormat.UInt3 or VertexElementFormat.UInt4
                    => BinaryPrimitives.ReadUInt32LittleEndian(e[(i * 4)..]),
                VertexElementFormat.Int1 or VertexElementFormat.Int2 or VertexElementFormat.Int3 or VertexElementFormat.Int4
                    => BinaryPrimitives.ReadInt32LittleEndian(e[(i * 4)..]),
                _ => throw new ArgumentException($"Vertex format {format} is not supported."),
            };
        }
        return count;
    }

    private static uint PackUnorm8(ReadOnlySpan<float> c, int count)
    {
        uint packed = 0;
        for (int i = 0; i < 4; i++)
        {
            float value = i < count ? c[i] : 1f;
            packed |= (uint)MathF.Round(Math.Clamp(value, 0f, 1f) * 255f) << (i * 8);
        }
        return packed;
    }

    private static uint PackUnorm16(float value) => (uint)MathF.Round(Math.Clamp(value, 0f, 1f) * 65535f);

    private static ushort Clamp16(float value) => (ushort)Math.Clamp(value, 0f, 65535f);
}
