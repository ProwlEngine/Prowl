using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Graphite.Rendering.Tools;

/// <summary>How <see cref="LodBuilder"/> simplifies a mesh into levels.</summary>
public sealed class LodBuildOptions
{
    /// <summary>The share of the source's triangles each level keeps, falling, one level each.</summary>
    public float[] Ratios = [0.5f, 0.25f, 0.125f];

    /// <summary>How far in pixels a level's error may show before a finer one takes over, at <see cref="ReferenceHeight"/>.</summary>
    public float PixelError = 1;

    /// <summary>The view height in pixels the screen sizes are worked out for, as <see cref="LodSettings.ReferenceHeight"/>.</summary>
    public float ReferenceHeight = 1080;

    /// <summary>Keeps open borders where they are, for shells and alpha cards.</summary>
    public bool LockBorders;

    /// <summary>How much changing each stream costs a collapse, against moving the surface. Streams left out cost nothing, but their seams are kept.</summary>
    public Dictionary<VertexSemantic, float> AttributeWeights = new() { [VertexSemantic.Normal] = 1, [VertexSemantic.UV0] = 1 };
}

/// <summary>The coarser levels of a mesh, levels 1 on, each with its error in the mesh's units and its screen size.</summary>
public sealed class LodChain
{
    public IReadOnlyList<MeshData> Levels { get; init; } = [];
    public IReadOnlyList<float> Errors { get; init; } = [];
    public IReadOnlyList<float> ScreenSizes { get; init; } = [];

    /// <summary>A LOD set of the levels, each made into a mesh by create.</summary>
    public LodSet CreateSet(Func<MeshData, GpuMesh> create, float cullScreenSize = 0)
    {
        LodLevel[] levels = new LodLevel[Levels.Count];
        for (int i = 0; i < levels.Length; i++)
            levels[i] = new LodLevel(create(Levels[i]), ScreenSizes[i]);
        return new LodSet(levels, cullScreenSize);
    }
}

/// <summary>
/// Builds the coarser levels of a mesh with Vector's quadric simplification, each from the one before, so every corner
/// of a level is one the source had. Seams in any stream and the outlines between submeshes are kept. Each level is
/// reordered by <see cref="MeshOptimizer"/>. For tools and import, not for frames.
/// </summary>
public static class LodBuilder
{
    private const string SubMeshAttribute = "SubMesh";

    // A level's screen size is at least this share under the one before, so the sizes always fall
    private const float SizeStep = 0.9f;

    private static readonly VertexSemantic[] s_carried =
    [
        VertexSemantic.Normal, VertexSemantic.Tangent, VertexSemantic.Color,
        VertexSemantic.UV0, VertexSemantic.UV1, VertexSemantic.UV2, VertexSemantic.UV3,
        VertexSemantic.Custom0, VertexSemantic.Custom1,
    ];

    public static LodChain Build(MeshData source, LodBuildOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new LodBuildOptions();
        MeshLayout layout = new(source);
        if (layout.Streams[(int)VertexSemantic.Position] == null)
            throw new ArgumentException("A mesh whose positions the GPU writes cannot be simplified.", nameof(source));
        if (layout.Streams[(int)VertexSemantic.BoneIndices] != null || source.BlendShapes.Length > 0)
            throw new ArgumentException("Skinned and blend shape meshes draw their own mesh whatever their LODs, so they have none.", nameof(source));
        foreach (SubMesh sub in layout.SubMeshes)
        {
            if (sub.Topology != PrimitiveTopology.TriangleList)
                throw new ArgumentException("Only triangle lists can be simplified.", nameof(source));
        }

        List<VertexSemantic> carried = [.. Array.FindAll(s_carried, s => layout.Streams[(int)s] != null)];
        GeometryData mesh = ToGeometry(layout, carried);
        int sourceTriangles = layout.Indices.Length / 3;
        float radius = Radius(layout.Bounds);

        SimplifyOptions simplify = new() { LockBorders = options.LockBorders, RegionAttributes = [SubMeshAttribute] };
        foreach ((VertexSemantic semantic, float weight) in options.AttributeWeights)
        {
            if (carried.Contains(semantic))
                simplify.AttributeWeights[semantic.ToString()] = weight;
        }

        List<MeshData> levels = new();
        List<float> errors = new(), sizes = new();
        float error = 0, size = 1 / SizeStep;
        foreach (float ratio in options.Ratios)
        {
            simplify.TargetTriangleCount = Math.Max(1, (int)(sourceTriangles * ratio));
            error += GeometryOperators.Simplify(mesh, simplify).Error;
            levels.Add(MeshOptimizer.Optimize(ToMeshData(mesh, carried, layout.SubMeshes.Length)));
            errors.Add(error);

            // The size at which the error projects to PixelError pixels, as the screen metric measures size
            float projected = error > 0 ? 2 * options.PixelError * radius / (error * options.ReferenceHeight) : float.PositiveInfinity;
            size = MathF.Min(projected, size * SizeStep);
            sizes.Add(size);
        }
        return new LodChain { Levels = levels, Errors = errors, ScreenSizes = sizes };
    }

    private static float Radius(AABB bounds)
    {
        Float3 half = (bounds.Max - bounds.Min) * 0.5f;
        return MathF.Max(MathF.Sqrt(half.X * half.X + half.Y * half.Y + half.Z * half.Z), 1e-6f);
    }

    private static int Dimensions(VertexSemantic semantic) => semantic switch
    {
        VertexSemantic.Normal => 3,
        VertexSemantic.UV0 or VertexSemantic.UV1 or VertexSemantic.UV2 or VertexSemantic.UV3 => 2,
        _ => 4,
    };

    // One vertex per position, every other stream on the corners, the submesh on the faces
    private static GeometryData ToGeometry(MeshLayout layout, List<VertexSemantic> carried)
    {
        GeometryData mesh = new();
        foreach (VertexSemantic semantic in carried)
            mesh.AddLoopAttribute(semantic.ToString(), GeometryData.AttributeBaseType.Float, Dimensions(semantic));
        mesh.AddFaceAttribute(SubMeshAttribute, GeometryData.AttributeBaseType.Int, 1);

        uint[] positions = layout.Streams[(int)VertexSemantic.Position]!;
        Dictionary<Float3, GeometryData.Vertex> welded = new();
        GeometryData.Vertex[] vertices = new GeometryData.Vertex[layout.VertexCount];
        for (int v = 0; v < vertices.Length; v++)
        {
            Float3 point = new(BitConverter.UInt32BitsToSingle(positions[v * 3]), BitConverter.UInt32BitsToSingle(positions[v * 3 + 1]), BitConverter.UInt32BitsToSingle(positions[v * 3 + 2]));
            if (!welded.TryGetValue(point, out GeometryData.Vertex? vertex))
                welded[point] = vertex = mesh.AddVertex(point);
            vertices[v] = vertex;
        }

        for (int s = 0; s < layout.SubMeshes.Length; s++)
        {
            SubMesh sub = layout.SubMeshes[s];
            for (uint i = sub.IndexStart; i + 2 < sub.IndexStart + sub.IndexCount; i += 3)
            {
                uint a = layout.Indices[i], b = layout.Indices[i + 1], c = layout.Indices[i + 2];
                if (vertices[a] == vertices[b] || vertices[b] == vertices[c] || vertices[a] == vertices[c])
                    continue;
                GeometryData.Face? face = mesh.AddFace(vertices[a], vertices[b], vertices[c]);
                if (face == null)
                    continue;
                face.Attributes[SubMeshAttribute] = new GeometryData.IntAttributeValue(s);
                GeometryData.Loop loop = face.Loop!;
                foreach (uint corner in (ReadOnlySpan<uint>)[a, b, c])
                {
                    foreach (VertexSemantic semantic in carried)
                        loop.Attributes[semantic.ToString()] = new GeometryData.FloatAttributeValue(Read(layout, semantic, corner));
                    loop = loop.Next!;
                }
            }
        }
        return mesh;
    }

    private static float[] Read(MeshLayout layout, VertexSemantic semantic, uint vertex)
    {
        uint[] words = layout.Streams[(int)semantic]!;
        if (semantic == VertexSemantic.Color)
        {
            uint packed = words[vertex];
            return [(packed & 0xFF) / 255f, ((packed >> 8) & 0xFF) / 255f, ((packed >> 16) & 0xFF) / 255f, (packed >> 24) / 255f];
        }
        int dimensions = Dimensions(semantic);
        float[] values = new float[dimensions];
        for (int d = 0; d < dimensions; d++)
            values[d] = BitConverter.UInt32BitsToSingle(words[vertex * (uint)dimensions + (uint)d]);
        return values;
    }

    // Triangles by submesh, corners with the same vertex and values sharing one vertex
    private static MeshData ToMeshData(GeometryData mesh, List<VertexSemantic> carried, int subMeshes)
    {
        List<Float3> positions = new();
        List<float>[] streams = new List<float>[carried.Count];
        for (int c = 0; c < carried.Count; c++)
            streams[c] = new List<float>();
        Dictionary<Corner, uint> corners = new(new CornerComparer());
        List<uint>[] indices = new List<uint>[subMeshes];
        for (int s = 0; s < subMeshes; s++)
            indices[s] = new List<uint>();

        foreach (GeometryData.Face face in mesh.Faces)
        {
            int sub = face.Attributes[SubMeshAttribute].AsInt()!.Data[0];
            GeometryData.Loop loop = face.Loop!;
            for (int k = 0; k < face.VertCount; k++, loop = loop.Next!)
            {
                float[][] values = new float[carried.Count][];
                for (int c = 0; c < carried.Count; c++)
                    values[c] = loop.Attributes[carried[c].ToString()].AsFloat()!.Data;
                Corner corner = new(loop.Vert, values);
                if (!corners.TryGetValue(corner, out uint index))
                {
                    index = (uint)positions.Count;
                    corners[corner] = index;
                    positions.Add(loop.Vert.Point);
                    for (int c = 0; c < carried.Count; c++)
                        streams[c].AddRange(values[c]);
                }
                indices[sub].Add(index);
            }
        }

        List<VertexStream> output = [Stream(VertexSemantic.Position, VertexElementFormat.Float3, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(positions)).ToArray())];
        for (int c = 0; c < carried.Count; c++)
            output.Add(StreamOf(carried[c], streams[c]));

        List<uint> all = new();
        SubMesh[] subs = new SubMesh[subMeshes];
        for (int s = 0; s < subMeshes; s++)
        {
            subs[s] = new SubMesh { IndexStart = (uint)all.Count, IndexCount = (uint)indices[s].Count };
            all.AddRange(indices[s]);
        }
        return new MeshData { VertexCount = (uint)positions.Count, Streams = [.. output], Indices = all.ToArray(), SubMeshes = subs };
    }

    private static VertexStream StreamOf(VertexSemantic semantic, List<float> values)
    {
        if (semantic == VertexSemantic.Color)
        {
            byte[] bytes = new byte[values.Count];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = (byte)MathF.Round(Math.Clamp(values[i], 0, 1) * 255);
            return Stream(semantic, VertexElementFormat.Byte4_Norm, bytes);
        }
        VertexElementFormat format = Dimensions(semantic) switch { 2 => VertexElementFormat.Float2, 3 => VertexElementFormat.Float3, _ => VertexElementFormat.Float4 };
        return Stream(semantic, format, MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(values)).ToArray());
    }

    private static VertexStream Stream(VertexSemantic semantic, VertexElementFormat format, byte[] data) => new() { Semantic = semantic, Format = format, Data = data };

    private readonly record struct Corner(GeometryData.Vertex Vertex, float[][] Values);

    private sealed class CornerComparer : IEqualityComparer<Corner>
    {
        public bool Equals(Corner a, Corner b)
        {
            if (a.Vertex != b.Vertex)
                return false;
            for (int c = 0; c < a.Values.Length; c++)
            {
                if (!a.Values[c].AsSpan().SequenceEqual(b.Values[c]))
                    return false;
            }
            return true;
        }

        public int GetHashCode(Corner corner)
        {
            HashCode hash = new();
            hash.Add(corner.Vertex);
            foreach (float[] values in corner.Values)
            {
                foreach (float value in values)
                    hash.Add(value);
            }
            return hash.ToHashCode();
        }
    }
}
