// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.CompilerServices;

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

        // Output buffers (we'll swap them in at the end).
        var newPos = new List<Float3>(oldCount);
        var newNormal = mesh.Normals is null ? null : new List<Float3>(oldCount);
        var newTangent = mesh.Tangents is null ? null : new List<Float4>(oldCount);
        var newColor = mesh.Colors0 is null ? null : new List<Color>(oldCount);
        var newUVs = new List<Float2>?[Mesh.MaxUVChannels];
        for (int i = 0; i < Mesh.MaxUVChannels; i++)
            newUVs[i] = mesh.UVs[i] is null ? null : new List<Float2>(oldCount);

        // Old-index -> new-index map; -1 means "not yet assigned a new slot".
        var oldToNew = new int[oldCount];
        Array.Fill(oldToNew, -1);

        // Hash chains over the kept vertices, stored in flat arrays.
        int tableSize = 1;
        while (tableSize < oldCount * 2) tableSize <<= 1;
        int mask = tableSize - 1;
        var heads = new int[tableSize];
        Array.Fill(heads, -1);
        var next = new int[oldCount];
        var hashes = new int[oldCount];

        for (int i = 0; i < oldCount; i++)
        {
            int hash = HashVertex(mesh, i);
            hashes[i] = hash;
            int slot = hash & mask;

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
                matchedNew = newPos.Count;
                newPos.Add(mesh.Positions[i]);
                if (newNormal is not null) newNormal.Add(mesh.Normals![i]);
                if (newTangent is not null) newTangent.Add(mesh.Tangents![i]);
                if (newColor is not null) newColor.Add(mesh.Colors0![i]);
                for (int uv = 0; uv < Mesh.MaxUVChannels; uv++)
                {
                    if (newUVs[uv] is { } list)
                        list.Add(mesh.UVs[uv]![i]);
                }
                next[i] = heads[slot];
                heads[slot] = i;
            }

            oldToNew[i] = matchedNew;
        }

        // Remap per-vertex joint/weight arrays into the new vertex order.
        if (mesh.VertexJoints is { } srcJoints && mesh.VertexWeights is { } srcWeights)
        {
            int influences = mesh.MaxInfluencesPerVertex;
            int newVertexCount = newPos.Count;
            int[] dstJoints = new int[newVertexCount * influences];
            float[] dstWeights = new float[newVertexCount * influences];
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
                var newDelta = new Float3[newPos.Count];
                Float3[]? newNorm = frame.DeltaNormals is null ? null : new Float3[newPos.Count];
                Float3[]? newTan = frame.DeltaTangents is null ? null : new Float3[newPos.Count];
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

        // Rewrite faces with the new indices, dropping any the weld collapsed. A thin triangle whose
        // two ends merge becomes a face naming one vertex twice, which is not a face at all, and the
        // degenerate pass runs before this step rather than after, so nothing else would catch it.
        var faces = mesh.Faces;
        foreach (ref int index in faces.AllIndices)
            index = oldToNew[index];
        var collapsed = new bool[faces.Count];
        for (int f = 0; f < collapsed.Length; f++)
            collapsed[f] = HasRepeatedIndex(faces.IndicesOf(f));
        faces.RemoveAll(collapsed);

        mesh.Positions = newPos;
        if (newNormal is not null) mesh.Normals = newNormal;
        if (newTangent is not null) mesh.Tangents = newTangent;
        if (newColor is not null) mesh.Colors0 = newColor;
        for (int uv = 0; uv < Mesh.MaxUVChannels; uv++)
        {
            if (newUVs[uv] is { } list)
                mesh.UVs[uv] = list;
        }
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

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    private static int HashVertex(IntermediateMesh m, int i)
    {
        var hc = new HashCode();
        hc.Add(m.Positions[i]);
        if (m.Normals is not null) hc.Add(m.Normals[i]);
        if (m.Tangents is not null) hc.Add(m.Tangents[i]);
        if (m.Colors0 is not null) hc.Add(m.Colors0[i]);
        for (int uv = 0; uv < Mesh.MaxUVChannels; uv++)
            if (m.UVs[uv] is { } u) hc.Add(u[i]);
        if (m.VertexJoints is { } vj && m.VertexWeights is { } vw)
        {
            int influences = m.MaxInfluencesPerVertex;
            for (int k = 0; k < influences; k++)
            {
                hc.Add(vj[i * influences + k]);
                hc.Add(vw[i * influences + k]);
            }
        }
        return hc.ToHashCode();
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
