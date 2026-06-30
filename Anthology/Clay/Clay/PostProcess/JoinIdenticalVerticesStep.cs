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

    private static void JoinOne(IntermediateMesh mesh)
    {
        int oldCount = mesh.Positions.Count;
        if (oldCount == 0)
            return;

        // Meshes that carry morph targets need their delta-position arrays remapped too. Two
        // vertices that differ only in their morph deltas must stay distinct, so we include
        // the deltas in the hash/equality check.
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

        // Bucket by position-hash first to avoid quadratic comparisons.
        var buckets = new Dictionary<int, List<int>>(oldCount);

        for (int i = 0; i < oldCount; i++)
        {
            int hash = HashVertex(mesh, i, hasMorph);
            if (!buckets.TryGetValue(hash, out var bucket))
            {
                bucket = new List<int>(1);
                buckets[hash] = bucket;
            }

            int matchedNew = -1;
            foreach (int candidate in bucket)
            {
                if (VerticesEqual(mesh, candidate, i, hasMorph))
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
                bucket.Add(i);
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

        // Rewrite faces with the new indices.
        for (int fi = 0; fi < mesh.Faces.Count; fi++)
        {
            var face = mesh.Faces[fi];
            for (int k = 0; k < face.Indices.Length; k++)
                face.Indices[k] = oldToNew[face.Indices[k]];
        }

        // Swap buffers in.
        mesh.Positions.Clear();
        mesh.Positions.AddRange(newPos);
        if (newNormal is not null) { mesh.Normals!.Clear(); mesh.Normals.AddRange(newNormal); }
        if (newTangent is not null) { mesh.Tangents!.Clear(); mesh.Tangents.AddRange(newTangent); }
        if (newColor is not null) { mesh.Colors0!.Clear(); mesh.Colors0.AddRange(newColor); }
        for (int uv = 0; uv < Mesh.MaxUVChannels; uv++)
        {
            if (newUVs[uv] is { } list && mesh.UVs[uv] is { } existing)
            {
                existing.Clear();
                existing.AddRange(list);
            }
        }

    }

    private static int HashVertex(IntermediateMesh m, int i, bool includeMorph)
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
        if (includeMorph)
        {
            for (int b = 0; b < m.BlendShapes.Count; b++)
            {
                var bs = m.BlendShapes[b];
                for (int f = 0; f < bs.Frames.Count; f++)
                {
                    hc.Add(bs.Frames[f].DeltaPositions[i]);
                }
            }
        }
        return hc.ToHashCode();
    }

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
                    if (!shape.Frames[f].DeltaPositions[a].Equals(shape.Frames[f].DeltaPositions[b]))
                        return false;
                }
            }
        }

        return true;
    }
}
