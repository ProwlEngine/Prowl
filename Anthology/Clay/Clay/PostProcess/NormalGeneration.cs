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
            var idx = mesh.Faces.IndicesOf(f);
            if (idx.Length != 3) continue;

            Float3 a = mesh.Positions[idx[0]];
            Float3 cross = Float3.Cross(mesh.Positions[idx[1]] - a, mesh.Positions[idx[2]] - a);
            float len = Float3.Length(cross);
            if (len < 1e-20f) continue;

            faceNormals[f] = cross / len;
            faceValid[f] = true;
        }

        // Vertices split only by their UVs or colours still sit on one continuous surface, so faces
        // are gathered per position rather than per vertex index.
        int[] positionOf = WeldPositions(mesh, out int positionCount);
        var adjacency = BuildAdjacency(mesh, positionOf, positionCount, faceValid);
        var smoothing = new PositionSmoothing?[positionCount];

        var normals = new Float3[vertexCount];

        // Corner -> vertex index it ends up on. Only entries that had to be split are written.
        var cornerRemap = new Dictionary<(int Face, int Corner), int>();
        var extraPositions = new List<Float3>();
        var extraNormals = new List<Float3>();
        var extraSourceVertex = new List<int>();
        var vertexOfGroup = new Dictionary<int, int>();

        for (int v = 0; v < vertexCount; v++)
        {
            int p = positionOf[v];
            var faces = adjacency[p];
            if (faces is null || faces.Count == 0)
            {
                // Referenced only by lines or points, or by nothing at all. Nothing to derive a
                // normal from, so it gets an arbitrary but finite one.
                normals[v] = new Float3(0f, 1f, 0f);
                continue;
            }

            PositionSmoothing groups = smoothing[p] ??= Smooth(mesh, faces, p, positionOf, faceNormals, cosThreshold);

            // The first group among this vertex's own faces keeps the vertex, the rest become duplicates.
            vertexOfGroup.Clear();
            bool used = false;
            foreach (int f in faces)
            {
                var idx = mesh.Faces.IndicesOf(f);
                for (int c = 0; c < idx.Length; c++)
                {
                    if (idx[c] != v) continue;

                    int group = groups.GroupOf[f];
                    if (!vertexOfGroup.TryGetValue(group, out int target))
                    {
                        if (!used)
                        {
                            target = v;
                            normals[v] = groups.Normals[group];
                            used = true;
                        }
                        else
                        {
                            target = vertexCount + extraPositions.Count;
                            extraPositions.Add(mesh.Positions[v]);
                            extraNormals.Add(groups.Normals[group]);
                            extraSourceVertex.Add(v);
                        }
                        vertexOfGroup[group] = target;
                    }

                    if (target != v)
                        cornerRemap[(f, c)] = target;
                }
            }

            if (!used)
                normals[v] = new Float3(0f, 1f, 0f);
        }

        ApplySplit(mesh, normals, extraPositions, extraNormals, extraSourceVertex, cornerRemap);
        return extraPositions.Count;
    }

    private sealed record PositionSmoothing(Dictionary<int, int> GroupOf, Float3[] Normals);

    /// <summary>
    /// Partitions every face around one position into smoothing groups, each with one angle weighted
    /// normal. Weighting by the interior angle keeps a position shared by one large and several small
    /// faces from being dragged toward the small ones.
    /// </summary>
    private static PositionSmoothing Smooth(IntermediateMesh mesh, List<int> faces, int position, int[] positionOf, Float3[] faceNormals, float cosThreshold)
    {
        var groupOf = new Dictionary<int, int>();
        PartitionIntoSmoothingGroups(mesh, faces, faceNormals, cosThreshold, groupOf, out int groupCount);

        var groupNormals = new Float3[groupCount];
        foreach (int f in faces)
            groupNormals[groupOf[f]] += faceNormals[f] * CornerAngle(mesh, f, position, positionOf);

        for (int g = 0; g < groupCount; g++)
            groupNormals[g] = SafeNormalize(groupNormals[g], faceNormals[faces[0]]);

        return new PositionSmoothing(groupOf, groupNormals);
    }

    private static int[] WeldPositions(IntermediateMesh mesh, out int positionCount)
    {
        var positionOf = new int[mesh.Positions.Count];
        var byPosition = new Dictionary<(float, float, float), int>();
        for (int v = 0; v < positionOf.Length; v++)
        {
            Float3 p = mesh.Positions[v];
            var key = (p.X, p.Y, p.Z);
            if (!byPosition.TryGetValue(key, out int id))
            {
                id = byPosition.Count;
                byPosition[key] = id;
            }
            positionOf[v] = id;
        }
        positionCount = byPosition.Count;
        return positionOf;
    }

    private static List<int>?[] BuildAdjacency(IntermediateMesh mesh, int[] positionOf, int positionCount, bool[] faceValid)
    {
        var adjacency = new List<int>?[positionCount];
        for (int f = 0; f < mesh.Faces.Count; f++)
        {
            if (!faceValid[f]) continue;
            foreach (int v in mesh.Faces.IndicesOf(f))
            {
                if ((uint)v >= (uint)positionOf.Length) continue;
                var faces = adjacency[positionOf[v]] ??= new List<int>(4);
                if (faces.Count == 0 || faces[^1] != f)
                    faces.Add(f);
            }
        }
        return adjacency;
    }

    /// <summary>
    /// Groups a vertex's adjacent faces so that any two faces within the threshold of each other
    /// share a group. Small union-find over what is normally a handful of faces.
    /// </summary>
    /// <summary>
    /// Decides whether two adjacent faces shade as one surface.
    /// </summary>
    /// <remarks>
    /// A source that stated smoothing groups is taken at its word, because that statement is the
    /// author's shading intent and the angle threshold is only ever a guess at it. Faces in the same
    /// non-zero group smooth however sharp the edge between them, faces in different groups never
    /// do, and group 0 means this face smooths with nothing. Only when at least one side said
    /// nothing does the angle decide.
    /// </remarks>
    private static bool Smooths(IntermediateMesh mesh, int faceA, int faceB, Float3[] faceNormals, float cosThreshold)
    {
        int a = mesh.Faces[faceA].SmoothingGroup;
        int b = mesh.Faces[faceB].SmoothingGroup;

        if (a != IntermediateFace.NoSmoothingGroup && b != IntermediateFace.NoSmoothingGroup)
            return a == b && a != 0;

        return Float3.Dot(faceNormals[faceA], faceNormals[faceB]) >= cosThreshold;
    }

    private static void PartitionIntoSmoothingGroups(
        IntermediateMesh mesh, List<int> faces, Float3[] faceNormals, float cosThreshold,
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
                if (!Smooths(mesh, faces[i], faces[j], faceNormals, cosThreshold)) continue;
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

    /// <summary>Interior angle of face <paramref name="face"/> at its corner on <paramref name="position"/>.</summary>
    private static float CornerAngle(IntermediateMesh mesh, int face, int position, int[] positionOf)
    {
        var idx = mesh.Faces.IndicesOf(face);
        int corner = -1;
        for (int c = 0; c < idx.Length; c++)
            if (positionOf[idx[c]] == position) { corner = c; break; }
        if (corner < 0) return 0f;

        Float3 p = mesh.Positions[idx[corner]];
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
            mesh.Faces.IndicesOf(kvp.Key.Face)[kvp.Key.Corner] = kvp.Value;
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
