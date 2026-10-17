// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Collapses identical attribute tuples into shared vertex indices.
/// </summary>
/// <remarks>
/// Compares positions, normals, tangents, color channel 0, and UV channels 0-7. The order of
/// vertices in the output is determined by the order in which each unique attribute combination
/// is first encountered while walking the face list, which keeps things cache-friendly.
/// </remarks>
internal sealed class JoinIdenticalVerticesStep : IPostProcess
{
    public PostProcessFlags Flag => PostProcessFlags.JoinIdenticalVertices;
    public string Name => "JoinIdenticalVertices";

    public void Execute(IntermediateScene scene, ImportContext context)
    {
        foreach (var mesh in scene.Meshes)
            JoinOne(mesh);
        _ = context;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void JoinOne(IntermediateMesh mesh)
    {
        int oldCount = mesh.Positions.Count;
        if (oldCount == 0)
            return;

        // Two vertices that differ only in their morph deltas have to stay distinct, so the deltas
        // are part of the merge predicate. They are deliberately not part of the hash: hashing them
        // reads every shape and frame for every vertex, which on a face mesh with fifty-odd ARKit
        // shapes is the dominant cost of the whole step. Leaving them out only makes the hash
        // coarser, and the delta comparison then runs solely for the candidates that already match
        // on everything else, which is the handful of seam duplicates rather than all of them.
        bool hasMorph = mesh.BlendShapes.Count > 0;

        // Old-index -> new-index map, and the old index each new vertex is copied from.
        var oldToNew = new int[oldCount];
        var kept = new int[oldCount];
        int newCount = 0;

        // Hash chains over the kept vertices, stored in flat arrays.
        int tableSize = 1;
        while (tableSize < oldCount * 2) tableSize <<= 1;
        int mask = tableSize - 1;
        var heads = new int[tableSize];
        Array.Fill(heads, -1);
        var next = new int[oldCount];
        var hashes = HashVertices(mesh);

        for (int i = 0; i < oldCount; i++)
        {
            uint hash = hashes[i];
            int slot = (int)(hash & (uint)mask);

            int matchedNew = -1;
            for (int candidate = heads[slot]; candidate >= 0; candidate = next[candidate])
            {
                if (hashes[candidate] == hash && VerticesEqual(mesh, candidate, i, hasMorph))
                {
                    matchedNew = oldToNew[candidate];
                    break;
                }
            }

            if (matchedNew < 0)
            {
                matchedNew = newCount;
                kept[newCount++] = i;
                next[i] = heads[slot];
                heads[slot] = i;
            }

            oldToNew[i] = matchedNew;
        }

        if (newCount < oldCount)
            Compact(mesh, oldToNew, kept, newCount);

        // Drop faces the weld collapsed. A thin triangle whose two ends merge becomes a face naming
        // one vertex twice, which is not a face at all, and the degenerate pass runs before this step
        // rather than after, so nothing else would catch it.
        var faces = mesh.Faces;
        var collapsed = new bool[faces.Count];
        bool anyCollapsed = false;
        for (int f = 0; f < collapsed.Length; f++)
            anyCollapsed |= collapsed[f] = HasRepeatedIndex(faces.IndicesOf(f));
        if (anyCollapsed)
            faces.RemoveAll(collapsed);
    }

    /// <summary>Rewrites every per-vertex stream and the face indices to the welded vertex order.</summary>
    private static void Compact(IntermediateMesh mesh, int[] oldToNew, int[] kept, int newCount)
    {
        int oldCount = oldToNew.Length;
        var newKept = kept.AsSpan(0, newCount);
        mesh.Positions = Gather(mesh.Positions, newKept);
        if (mesh.Normals is { } normals) mesh.Normals = Gather(normals, newKept);
        if (mesh.Tangents is { } tangents) mesh.Tangents = Gather(tangents, newKept);
        if (mesh.Colors0 is { } colors) mesh.Colors0 = Gather(colors, newKept);
        for (int uv = 0; uv < Mesh.MaxUVChannels; uv++)
            if (mesh.UVs[uv] is { } list)
                mesh.UVs[uv] = Gather(list, newKept);

        // Remap per-vertex joint/weight arrays into the new vertex order.
        if (mesh.VertexJoints is { } srcJoints && mesh.VertexWeights is { } srcWeights)
        {
            int influences = mesh.MaxInfluencesPerVertex;
            int[] dstJoints = new int[newCount * influences];
            float[] dstWeights = new float[newCount * influences];
            for (int i = 0; i < oldCount; i++)
            {
                int dst = oldToNew[i];
                for (int k = 0; k < influences; k++)
                {
                    dstJoints[dst * influences + k] = srcJoints[i * influences + k];
                    dstWeights[dst * influences + k] = srcWeights[i * influences + k];
                }
            }
            mesh.VertexJoints = dstJoints;
            mesh.VertexWeights = dstWeights;
        }

        // Remap morph-target deltas into the new vertex order.
        foreach (var bs in mesh.BlendShapes)
        {
            for (int fi = 0; fi < bs.Frames.Count; fi++)
            {
                var frame = bs.Frames[fi];
                var newDelta = new Float3[newCount];
                Float3[]? newNorm = frame.DeltaNormals is null ? null : new Float3[newCount];
                Float3[]? newTan = frame.DeltaTangents is null ? null : new Float3[newCount];
                for (int i = 0; i < oldCount; i++)
                {
                    int dst = oldToNew[i];
                    newDelta[dst] = frame.DeltaPositions[i];
                    if (newNorm is not null) newNorm[dst] = frame.DeltaNormals![i];
                    if (newTan is not null) newTan[dst] = frame.DeltaTangents![i];
                }
                bs.Frames[fi] = new IntermediateBlendShapeFrame
                {
                    Weight = frame.Weight,
                    DeltaPositions = newDelta,
                    DeltaNormals = newNorm,
                    DeltaTangents = newTan,
                };
            }
        }

        foreach (ref int index in mesh.Faces.AllIndices)
            index = oldToNew[index];
    }

    private static List<T> Gather<T>(List<T> source, ReadOnlySpan<int> kept)
    {
        var src = CollectionsMarshal.AsSpan(source);
        var result = new List<T>(kept.Length);
        CollectionsMarshal.SetCount(result, kept.Length);
        var dst = CollectionsMarshal.AsSpan(result);
        for (int i = 0; i < kept.Length; i++)
            dst[i] = src[kept[i]];
        return result;
    }

    /// <summary>True when a face names the same vertex twice, which only a point can survive.</summary>
    private static bool HasRepeatedIndex(ReadOnlySpan<int> indices)
    {
        for (int i = 0; i < indices.Length; i++)
            for (int j = i + 1; j < indices.Length; j++)
                if (indices[i] == indices[j])
                    return true;
        return false;
    }

    /// <summary>Hashes every vertex over the same attributes <see cref="VerticesEqual"/> compares, morph deltas aside.</summary>
    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static uint[] HashVertices(IntermediateMesh m)
    {
        var hashes = new uint[m.Positions.Count];
        Mix(hashes, MemoryMarshal.Cast<Float3, float>(CollectionsMarshal.AsSpan(m.Positions)), 3);
        if (m.Normals is { } n) Mix(hashes, MemoryMarshal.Cast<Float3, float>(CollectionsMarshal.AsSpan(n)), 3);
        if (m.Tangents is { } t) Mix(hashes, MemoryMarshal.Cast<Float4, float>(CollectionsMarshal.AsSpan(t)), 4);
        if (m.Colors0 is { } c) Mix(hashes, MemoryMarshal.Cast<Color, float>(CollectionsMarshal.AsSpan(c)), 4);
        for (int uv = 0; uv < Mesh.MaxUVChannels; uv++)
            if (m.UVs[uv] is { } u) Mix(hashes, MemoryMarshal.Cast<Float2, float>(CollectionsMarshal.AsSpan(u)), 2);
        if (m.VertexWeights is { } vw && m.VertexJoints is not null)
            Mix(hashes, vw, m.MaxInfluencesPerVertex);

        for (int i = 0; i < hashes.Length; i++)
            hashes[i] = Avalanche(hashes[i]);
        return hashes;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static void Mix(uint[] hashes, ReadOnlySpan<float> values, int perVertex)
    {
        values = values[..(hashes.Length * perVertex)];
        for (int i = 0, v = 0; i < hashes.Length; i++)
        {
            uint h = hashes[i];
            for (int k = 0; k < perVertex; k++, v++)
                h = (BitOperations.RotateLeft(h, 5) ^ Bits(values[v])) * 0x9E3779B1u;
            hashes[i] = h;
        }
    }

    // Equal floats give equal bits: both zeros map to 0 and every NaN to one pattern.
    private static uint Bits(float f) =>
        f == 0f ? 0u : float.IsNaN(f) ? 0x7FC00000u : BitConverter.SingleToUInt32Bits(f);

    private static uint Avalanche(uint h)
    {
        h ^= h >> 16;
        h *= 0x85EBCA6Bu;
        h ^= h >> 13;
        h *= 0xC2B2AE35u;
        h ^= h >> 16;
        return h;
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static bool VerticesEqual(IntermediateMesh m, int a, int b, bool includeMorph)
    {
        if (!m.Positions[a].Equals(m.Positions[b])) return false;
        if (m.Normals is not null && !m.Normals[a].Equals(m.Normals[b])) return false;
        if (m.Tangents is not null && !m.Tangents[a].Equals(m.Tangents[b])) return false;
        if (m.Colors0 is not null && !m.Colors0[a].Equals(m.Colors0[b])) return false;
        for (int uv = 0; uv < Mesh.MaxUVChannels; uv++)
            if (m.UVs[uv] is { } u && !u[a].Equals(u[b])) return false;

        if (m.VertexJoints is { } vj && m.VertexWeights is { } vw)
        {
            int influences = m.MaxInfluencesPerVertex;
            for (int k = 0; k < influences; k++)
            {
                if (vj[a * influences + k] != vj[b * influences + k]) return false;
                if (vw[a * influences + k] != vw[b * influences + k]) return false;
            }
        }

        if (includeMorph)
        {
            for (int bs = 0; bs < m.BlendShapes.Count; bs++)
            {
                var shape = m.BlendShapes[bs];
                for (int f = 0; f < shape.Frames.Count; f++)
                {
                    var frame = shape.Frames[f];
                    if (!frame.DeltaPositions[a].Equals(frame.DeltaPositions[b])) return false;
                    if (frame.DeltaNormals is { } dn && !dn[a].Equals(dn[b])) return false;
                    if (frame.DeltaTangents is { } dt && !dt[a].Equals(dt[b])) return false;
                }
            }
        }

        return true;
    }
}
