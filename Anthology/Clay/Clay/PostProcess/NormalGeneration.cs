// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Shared implementation behind <see cref="GenerateNormalsStep"/> and
/// <see cref="GenerateSmoothNormalsStep"/>. Both are the same algorithm at different smoothing
/// angles: flat shading is smoothing with a threshold of zero, where only exactly coplanar faces
/// end up in the same smoothing group.
/// </summary>
/// <remarks>
/// Per vertex, the adjacent faces are partitioned into smoothing groups by whether their normals
/// lie within the threshold of each other, and each group contributes one angle-weighted normal.
/// The first group keeps the original vertex; every further group gets a duplicate, so a hard edge
/// splits only the vertices that actually sit on it rather than unindexing the whole mesh.
/// <para>
/// Grouping is transitive, so a chain of gradually turning faces smooths across the whole chain.
/// That is what smoothing groups do in every DCC tool, and it is why a curved surface tessellated
/// finely enough never develops a seam.
/// </para>
/// </remarks>
internal static class NormalGeneration
{
    /// <summary>
    /// Writes <see cref="IntermediateMesh.Normals"/>, splitting vertices where the smoothing angle
    /// says the surface is not continuous. Returns the number of vertices added by that splitting.
    /// </summary>
    public static int Generate(IntermediateMesh mesh, float smoothingAngleDeg)
    {
        int vertexCount = mesh.Positions.Count;
        if (vertexCount == 0 || mesh.Faces.Count == 0)
            return 0;

        float cosThreshold = MathF.Cos(Math.Clamp(smoothingAngleDeg, 0f, 180f) * (MathF.PI / 180f));

        // Face normals. A degenerate face has no meaningful normal and takes no part in smoothing.
        var faceNormals = new Float3[mesh.Faces.Count];
        var faceValid = new bool[mesh.Faces.Count];
        for (int f = 0; f < mesh.Faces.Count; f++)
        {
            int[] idx = mesh.Faces[f].Indices;
            if (idx.Length != 3) continue;

            Float3 a = mesh.Positions[idx[0]];
            Float3 cross = Float3.Cross(mesh.Positions[idx[1]] - a, mesh.Positions[idx[2]] - a);
            float len = Float3.Length(cross);
            if (len < 1e-20f) continue;

            faceNormals[f] = cross / len;
            faceValid[f] = true;
        }

        var adjacency = BuildAdjacency(mesh, vertexCount, faceValid);

        var normals = new Float3[vertexCount];
        var assigned = new bool[vertexCount];

        // Corner -> vertex index it ends up on. Only entries that had to be split are written.
        var cornerRemap = new Dictionary<(int Face, int Corner), int>();
        var extraPositions = new List<Float3>();
        var extraNormals = new List<Float3>();
        var extraSourceVertex = new List<int>();

        var groupOf = new Dictionary<int, int>();
        var groupNormals = new List<Float3>();

        for (int v = 0; v < vertexCount; v++)
        {
            var faces = adjacency[v];
            if (faces is null || faces.Count == 0)
            {
                // Referenced only by lines or points, or by nothing at all. Nothing to derive a
                // normal from, so it gets an arbitrary but finite one.
                normals[v] = new Float3(0f, 1f, 0f);
                assigned[v] = true;
                continue;
            }

            PartitionIntoSmoothingGroups(faces, faceNormals, cosThreshold, groupOf, out int groupCount);

            // One angle-weighted normal per group. Weighting by the interior angle at this corner is
            // what keeps a vertex shared by one large and several small faces from being dragged
            // toward the small ones.
            groupNormals.Clear();
            for (int g = 0; g < groupCount; g++)
                groupNormals.Add(Float3.Zero);

            foreach (int f in faces)
            {
                int group = groupOf[f];
                groupNormals[group] += faceNormals[f] * CornerAngle(mesh, f, v);
            }

            for (int g = 0; g < groupCount; g++)
                groupNormals[g] = SafeNormalize(groupNormals[g], faceNormals[faces[0]]);

            // The first group keeps the original vertex; the rest become duplicates.
            normals[v] = groupNormals[0];
            assigned[v] = true;

            if (groupCount > 1)
            {
                var groupVertex = new int[groupCount];
                groupVertex[0] = v;
                for (int g = 1; g < groupCount; g++)
                {
                    groupVertex[g] = vertexCount + extraPositions.Count;
                    extraPositions.Add(mesh.Positions[v]);
                    extraNormals.Add(groupNormals[g]);
                    extraSourceVertex.Add(v);
                }

                foreach (int f in faces)
                {
                    int group = groupOf[f];
                    if (group == 0) continue;

                    int[] idx = mesh.Faces[f].Indices;
                    for (int c = 0; c < idx.Length; c++)
                        if (idx[c] == v)
                            cornerRemap[(f, c)] = groupVertex[group];
                }
            }
        }

        for (int v = 0; v < vertexCount; v++)
            if (!assigned[v])
                normals[v] = new Float3(0f, 1f, 0f);

        ApplySplit(mesh, normals, extraPositions, extraNormals, extraSourceVertex, cornerRemap);
        return extraPositions.Count;
    }

    private static List<int>?[] BuildAdjacency(IntermediateMesh mesh, int vertexCount, bool[] faceValid)
    {
        var adjacency = new List<int>?[vertexCount];
        for (int f = 0; f < mesh.Faces.Count; f++)
        {
            if (!faceValid[f]) continue;
            foreach (int v in mesh.Faces[f].Indices)
            {
                if ((uint)v >= (uint)vertexCount) continue;
                (adjacency[v] ??= new List<int>(4)).Add(f);
            }
        }
        return adjacency;
    }

    /// <summary>
    /// Groups a vertex's adjacent faces so that any two faces within the threshold of each other
    /// share a group. Small union-find over what is normally a handful of faces.
    /// </summary>
    private static void PartitionIntoSmoothingGroups(
        List<int> faces, Float3[] faceNormals, float cosThreshold,
        Dictionary<int, int> groupOf, out int groupCount)
    {
        int n = faces.Count;
        Span<int> parent = n <= 32 ? stackalloc int[n] : new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;

        int Find(Span<int> p, int i)
        {
            while (p[i] != i) { p[i] = p[p[i]]; i = p[i]; }
            return i;
        }

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                if (Float3.Dot(faceNormals[faces[i]], faceNormals[faces[j]]) < cosThreshold) continue;
                int ri = Find(parent, i), rj = Find(parent, j);
                if (ri != rj) parent[ri] = rj;
            }
        }

        // Number the roots in first-seen order so group 0 is the one the original vertex keeps.
        groupOf.Clear();
        Span<int> rootToGroup = n <= 32 ? stackalloc int[n] : new int[n];
        rootToGroup.Fill(-1);
        groupCount = 0;
        for (int i = 0; i < n; i++)
        {
            int root = Find(parent, i);
            if (rootToGroup[root] < 0) rootToGroup[root] = groupCount++;
            groupOf[faces[i]] = rootToGroup[root];
        }
    }

    /// <summary>Interior angle of face <paramref name="face"/> at vertex <paramref name="vertex"/>.</summary>
    private static float CornerAngle(IntermediateMesh mesh, int face, int vertex)
    {
        int[] idx = mesh.Faces[face].Indices;
        int corner = -1;
        for (int c = 0; c < idx.Length; c++)
            if (idx[c] == vertex) { corner = c; break; }
        if (corner < 0) return 0f;

        Float3 p = mesh.Positions[vertex];
        Float3 e0 = mesh.Positions[idx[(corner + 1) % idx.Length]] - p;
        Float3 e1 = mesh.Positions[idx[(corner + 2) % idx.Length]] - p;

        float l0 = Float3.Length(e0), l1 = Float3.Length(e1);
        if (l0 < 1e-20f || l1 < 1e-20f) return 0f;

        return MathF.Acos(Math.Clamp(Float3.Dot(e0 / l0, e1 / l1), -1f, 1f));
    }

    private static Float3 SafeNormalize(Float3 v, Float3 fallback)
    {
        float len = Float3.Length(v);
        return len < 1e-20f ? fallback : v / len;
    }

    /// <summary>
    /// Appends the duplicated vertices, copies every per-vertex stream onto them, and rewrites the
    /// face indices that were remapped.
    /// </summary>
    private static void ApplySplit(
        IntermediateMesh mesh, Float3[] normals,
        List<Float3> extraPositions, List<Float3> extraNormals, List<int> extraSourceVertex,
        Dictionary<(int Face, int Corner), int> cornerRemap)
    {
        int oldCount = mesh.Positions.Count;
        int extra = extraPositions.Count;

        var newNormals = new List<Float3>(oldCount + extra);
        newNormals.AddRange(normals);
        newNormals.AddRange(extraNormals);
        mesh.Normals = newNormals;

        if (extra == 0)
            return;

        mesh.Positions.AddRange(extraPositions);

        if (mesh.Tangents is { } tangents)
            for (int i = 0; i < extra; i++) tangents.Add(tangents[extraSourceVertex[i]]);

        if (mesh.Colors0 is { } colors)
            for (int i = 0; i < extra; i++) colors.Add(colors[extraSourceVertex[i]]);

        for (int uv = 0; uv < Mesh.MaxUVChannels; uv++)
            if (mesh.UVs[uv] is { } list)
                for (int i = 0; i < extra; i++) list.Add(list[extraSourceVertex[i]]);

        if (mesh.VertexJoints is { } joints && mesh.VertexWeights is { } weights)
        {
            int influences = mesh.MaxInfluencesPerVertex;
            var newJoints = new int[(oldCount + extra) * influences];
            var newWeights = new float[(oldCount + extra) * influences];
            Array.Copy(joints, newJoints, oldCount * influences);
            Array.Copy(weights, newWeights, oldCount * influences);

            for (int i = 0; i < extra; i++)
            {
                int src = extraSourceVertex[i] * influences;
                int dst = (oldCount + i) * influences;
                for (int k = 0; k < influences; k++)
                {
                    newJoints[dst + k] = joints[src + k];
                    newWeights[dst + k] = weights[src + k];
                }
            }

            mesh.VertexJoints = newJoints;
            mesh.VertexWeights = newWeights;
        }

        foreach (var shape in mesh.BlendShapes)
        {
            for (int f = 0; f < shape.Frames.Count; f++)
            {
                var frame = shape.Frames[f];
                shape.Frames[f] = new IntermediateBlendShapeFrame
                {
                    Weight = frame.Weight,
                    DeltaPositions = GrowDeltas(frame.DeltaPositions, oldCount, extraSourceVertex)!,
                    DeltaNormals = GrowDeltas(frame.DeltaNormals, oldCount, extraSourceVertex),
                    DeltaTangents = GrowDeltas(frame.DeltaTangents, oldCount, extraSourceVertex),
                };
            }
        }

        foreach (var kvp in cornerRemap)
            mesh.Faces[kvp.Key.Face].Indices[kvp.Key.Corner] = kvp.Value;
    }

    private static Float3[]? GrowDeltas(Float3[]? source, int oldCount, List<int> extraSourceVertex)
    {
        if (source is null) return null;

        var grown = new Float3[oldCount + extraSourceVertex.Count];
        Array.Copy(source, grown, Math.Min(source.Length, oldCount));
        for (int i = 0; i < extraSourceVertex.Count; i++)
        {
            int src = extraSourceVertex[i];
            grown[oldCount + i] = src < source.Length ? source[src] : Float3.Zero;
        }
        return grown;
    }
}
