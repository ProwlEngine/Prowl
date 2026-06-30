using System.Collections.Generic;

namespace Prowl.Unwrapper;

/// <summary>
/// "Local cutting" non-manifold fixer. Detects edges shared by &gt;2 triangles, edges with
/// inconsistent winding, and double-sided triangles, then splits the vertices whose stars
/// fall into multiple manifold components — duplicating positions in the process.
/// </summary>
/// <remarks>
/// Algorithm follows Gueziec, Taubin, Lazarus and Horn -- "Cutting and Stitching: Converting
/// Sets of Polygons to Manifold Surfaces" (cutting half only; we don't try to stitch).
/// Storage layout is CSC-style flat int arrays so the per-edge and per-vertex List allocations
/// stay out of the hot path on large meshes.
/// </remarks>
internal static class NonManifoldFixer
{
    /// <summary>
    /// Run the fixer in place on <paramref name="triangles"/>. Position duplicates are
    /// appended via the returned <see cref="FixResult.ExtraVertexSourceIndices"/> list;
    /// callers expand their own position buffer accordingly.
    /// </summary>
    public static FixResult Fix(int positionCount, int[] triangles, System.Action<string>? progress = null)
    {
        int triangleCount = triangles.Length / 3;
        int sideCount = 3 * triangleCount;
        var phaseSw = System.Diagnostics.Stopwatch.StartNew();

        // ---- Pass 1: hash each side ONCE, remember the slot, count entries per slot ----
        // Caching the slot in triSideSlot[] means pass 2 never re-hashes — significant on
        // large meshes where the hash dominates.
        var edgeIndex = new LongIntMap(sideCount);
        int[] triSideSlot = new int[sideCount];
        int[] edgeTriCount = new int[sideCount];  // upper bound; trimmed below
        int uniqueEdgeCount = 0;

        for (int triI = 0; triI < triangleCount; ++triI)
        {
            int prevApex = 2;
            for (int apex = 0; apex < 3; ++apex)
            {
                int v0 = triangles[3 * triI + prevApex];
                int v1 = triangles[3 * triI + apex];
                long key = EdgeKey(v0, v1);
                int slot;
                if (!edgeIndex.TryGetOrAdd(key, uniqueEdgeCount, out slot))
                {
                    slot = uniqueEdgeCount++;
                }
                triSideSlot[3 * triI + prevApex] = slot;
                ++edgeTriCount[slot];
                prevApex = apex;
            }
        }

        progress?.Invoke($"[fix-detail] edge slot+count {phaseSw.ElapsedMilliseconds} ms");
        phaseSw.Restart();

        // ---- Build offsets from counts ----
        int[] edgeTriStart = new int[uniqueEdgeCount + 1];
        for (int e = 0; e < uniqueEdgeCount; ++e)
            edgeTriStart[e + 1] = edgeTriStart[e] + edgeTriCount[e];
        int totalEdgeEntries = edgeTriStart[uniqueEdgeCount];

        int[] edgeEntryTri = new int[totalEdgeEntries];
        byte[] edgeEntryApex = new byte[totalEdgeEntries];

        // ---- Pass 2: fill the flat per-edge triangle entries using cached slots ----
        int[] fillCursor = new int[uniqueEdgeCount];
        for (int triI = 0; triI < triangleCount; ++triI)
        {
            for (int side = 0; side < 3; ++side)
            {
                int slot = triSideSlot[3 * triI + side];
                int dst = edgeTriStart[slot] + fillCursor[slot]++;
                edgeEntryTri[dst] = triI;
                edgeEntryApex[dst] = (byte)side;
            }
        }

        progress?.Invoke($"[fix-detail] flat fill {phaseSw.ElapsedMilliseconds} ms");
        phaseSw.Restart();

        // ---- Singular-edge detection ----
        // bool[] keyed on edge slot — far cheaper to check than a HashSet for the BFS pass.
        bool[] isSingularEdge = new bool[uniqueEdgeCount];
        var singularVertices = new HashSet<int>();
        int sgOver = 0, sgWinding = 0, sgDoubleSided = 0;

        var edgeIt = edgeIndex.GetEnumerator();
        while (edgeIt.MoveNext())
        {
            int slot = edgeIt.Current.Value;
            int start = edgeTriStart[slot];
            int count = edgeTriStart[slot + 1] - start;
            int reason = WhySingular(count, start, edgeEntryTri, edgeEntryApex, triangles);
            if (reason == 0) continue;
            if (reason == 1) ++sgOver;
            else if (reason == 2) ++sgWinding;
            else ++sgDoubleSided;

            isSingularEdge[slot] = true;
            singularVertices.Add(DecodeEdgeKeyLow(edgeIt.Current.Key));
            singularVertices.Add(DecodeEdgeKeyHigh(edgeIt.Current.Key));
        }
        progress?.Invoke($"[fix] singular edges: {sgOver + sgWinding + sgDoubleSided} (>2 tris: {sgOver}, winding: {sgWinding}, double-sided: {sgDoubleSided})");
        progress?.Invoke($"[fix-detail] singular detect {phaseSw.ElapsedMilliseconds} ms");
        phaseSw.Restart();

        // ---- Per-vertex incident triangles, flat-array CSC layout ----
        int[] vertexTriCount = new int[positionCount];
        for (int t = 0; t < sideCount; ++t) ++vertexTriCount[triangles[t]];

        int[] vertexTriStart = new int[positionCount + 1];
        for (int v = 0; v < positionCount; ++v)
            vertexTriStart[v + 1] = vertexTriStart[v] + vertexTriCount[v];
        int totalVertexEntries = vertexTriStart[positionCount];

        int[] vertexTriEntry = new int[totalVertexEntries];
        int[] vertexFillCursor = new int[positionCount];
        for (int triI = 0; triI < triangleCount; ++triI)
        {
            int a = triangles[3 * triI + 0];
            int b = triangles[3 * triI + 1];
            int c = triangles[3 * triI + 2];
            vertexTriEntry[vertexTriStart[a] + vertexFillCursor[a]++] = triI;
            vertexTriEntry[vertexTriStart[b] + vertexFillCursor[b]++] = triI;
            vertexTriEntry[vertexTriStart[c] + vertexFillCursor[c]++] = triI;
        }

        progress?.Invoke($"[fix-detail] vertex CSC {phaseSw.ElapsedMilliseconds} ms");
        phaseSw.Restart();

        // ---- BFS to label sub-fans, only for singular vertices ----
        // Generation counter avoids initialising/resetting the triangle→star-slot lookup table
        // between calls: each MarkNeighborhood call gets a unique token, and entries from
        // previous calls compare unequal automatically.
        int[]?[] vertexComponentLabels = new int[]?[positionCount];
        int[] labelScratch = System.Array.Empty<int>();
        var bfsQueue = new Queue<int>();
        int[] triGenToken = new int[triangleCount];
        int[] triToStarSlot = new int[triangleCount];
        int genCursor = 0;

        foreach (int vi in singularVertices)
        {
            int starStart = vertexTriStart[vi];
            int starCount = vertexTriStart[vi + 1] - starStart;
            if (starCount == 0) continue;
            if (starCount > labelScratch.Length) labelScratch = new int[starCount];

            int componentCount = MarkNeighborhood(
                triangles,
                vertexTriEntry, starStart, starCount,
                edgeIndex, edgeTriStart, edgeEntryTri,
                isSingularEdge,
                labelScratch, bfsQueue,
                triGenToken, triToStarSlot, ++genCursor);

            if (componentCount > 1)
            {
                var copy = new int[starCount];
                System.Array.Copy(labelScratch, copy, starCount);
                vertexComponentLabels[vi] = copy;
            }
        }

        progress?.Invoke($"[fix-detail] BFS labelling {phaseSw.ElapsedMilliseconds} ms");
        phaseSw.Restart();

        // ---- Cut: each multi-component vertex gains K-1 duplicates ----
        var extraVertexSource = new List<int>();
        foreach (int curV in singularVertices)
        {
            int[]? labels = vertexComponentLabels[curV];
            if (labels is null) continue;

            int componentCount = 0;
            for (int i = 0; i < labels.Length; ++i)
                if (labels[i] >= 0 && labels[i] + 1 > componentCount) componentCount = labels[i] + 1;
            if (componentCount <= 1) continue;

            int duplicateBase = positionCount + extraVertexSource.Count;
            for (int i = 1; i < componentCount; ++i) extraVertexSource.Add(curV);

            int starStart = vertexTriStart[curV];
            int starEnd = vertexTriStart[curV + 1];
            int starI = 0;
            for (int e = starStart; e < starEnd; ++e, ++starI)
            {
                int label = labels[starI];
                if (label <= 0) continue;

                int srcTri = vertexTriEntry[e];
                for (int corner = 0; corner < 3; ++corner)
                {
                    if (triangles[3 * srcTri + corner] == curV)
                        triangles[3 * srcTri + corner] = duplicateBase + label - 1;
                }
            }
        }

        progress?.Invoke($"[fix-detail] cut {phaseSw.ElapsedMilliseconds} ms");
        return new FixResult(extraVertexSource);
    }

    /// <summary>Vertex-position duplicates added by the fixer; each entry is the original index they cloned.</summary>
    public readonly struct FixResult
    {
        public readonly List<int> ExtraVertexSourceIndices;
        public FixResult(List<int> extraSrc) { ExtraVertexSourceIndices = extraSrc; }
    }

    private static long EdgeKey(int v0, int v1)
    {
        int a = System.Math.Min(v0, v1);
        int b = System.Math.Max(v0, v1);
        return ((long)b << 32) | (uint)a;
    }

    private static int DecodeEdgeKeyLow(long key) => (int)(key & 0xFFFFFFFFL);
    private static int DecodeEdgeKeyHigh(long key) => (int)(key >> 32);

    /// <summary>Returns 0 for manifold, 1 for &gt;2 triangles, 2 for inconsistent winding, 3 for double-sided.</summary>
    private static int WhySingular(int count, int start, int[] entryTri, byte[] entryApex, int[] triangles)
    {
        if (count > 2) return 1;
        if (count != 2) return 0;

        int triA = entryTri[start + 0];
        int triB = entryTri[start + 1];
        int apexA = entryApex[start + 0];
        int apexB = entryApex[start + 1];

        int sA = triangles[3 * triA + apexA];
        int sB = triangles[3 * triB + apexB];
        if (sA == sB) return 2;

        int tA = triangles[3 * triA + (apexA + 2) % 3];
        int tB = triangles[3 * triB + (apexB + 2) % 3];
        if (tA == tB) return 3;

        return 0;
    }

    /// <summary>
    /// BFS over the triangles touching one vertex (described as a span of <paramref name="vertexTriEntry"/>
    /// from <paramref name="starStart"/> for <paramref name="starCount"/> entries), treating singular
    /// edges as walls. Each connected sub-fan gets its own label 0..K-1. Returns K.
    /// </summary>
    private static int MarkNeighborhood(
        int[] triangles,
        int[] vertexTriEntry, int starStart, int starCount,
        LongIntMap edgeIndex, int[] edgeTriStart, int[] edgeEntryTri,
        bool[] isSingularEdge,
        int[] labels, Queue<int> queue,
        int[] triGenToken, int[] triToStarSlot, int token)
    {
        // Stamp this call's token into the lookup table for each star triangle; later BFS
        // membership checks become O(1) by comparing token == triGenToken[neighbour].
        for (int i = 0; i < starCount; ++i)
        {
            int tri = vertexTriEntry[starStart + i];
            triGenToken[tri] = token;
            triToStarSlot[tri] = i;
            labels[i] = -1;
        }

        int markedCount = 0;
        int currentMark = 0;
        queue.Clear();

        while (markedCount < starCount)
        {
            int seed = -1;
            for (int i = 0; i < starCount; ++i) { if (labels[i] == -1) { seed = i; break; } }
            if (seed < 0) break;

            queue.Clear();
            queue.Enqueue(seed);
            labels[seed] = currentMark;

            while (queue.Count > 0)
            {
                int starI = queue.Dequeue();
                ++markedCount;

                int triI = vertexTriEntry[starStart + starI];
                int prevAdded = -1;

                for (int edgeI = 0; edgeI < 3; ++edgeI)
                {
                    int e0 = triangles[3 * triI + edgeI];
                    int e1 = triangles[3 * triI + (edgeI + 1) % 3];
                    if (!edgeIndex.TryGet(EdgeKey(e0, e1), out int slot)) continue;
                    if (isSingularEdge[slot]) continue;
                    int entryStart = edgeTriStart[slot];
                    int entryCount = edgeTriStart[slot + 1] - entryStart;
                    if (entryCount < 2) continue;  // border edge

                    int neighbour = edgeEntryTri[entryStart + 0] == triI
                        ? edgeEntryTri[entryStart + 1]
                        : edgeEntryTri[entryStart + 0];
                    if (neighbour == prevAdded) continue;
                    if (triGenToken[neighbour] != token) continue;  // not in this vertex's star

                    int neighbourStar = triToStarSlot[neighbour];
                    if (labels[neighbourStar] != -1) continue;

                    labels[neighbourStar] = currentMark;
                    queue.Enqueue(neighbourStar);
                    prevAdded = neighbour;
                }
            }

            ++currentMark;
        }

        return currentMark;
    }
}
