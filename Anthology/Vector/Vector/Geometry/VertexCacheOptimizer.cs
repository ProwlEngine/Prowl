// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Vector.Geometry;

/// <summary>
/// Orders triangle lists for the GPU without changing what they draw. Triangles go in an order that keeps their
/// vertices in the post transform cache, after Tom Forsyth's linear speed vertex cache optimisation, and vertices
/// can then be renumbered in the order the triangles first use them so their fetches stay close. Works on the index
/// buffer a renderer draws, as the cache only sees the vertices shared there.
/// </summary>
public static class VertexCacheOptimizer
{
    /// <summary>The FIFO cache size the order is scored for and <see cref="MissRatio"/> simulates.</summary>
    public const int CacheSize = 32;

    private const float DecayPower = 1.5f, LastTriangleScore = 0.75f, ValenceScale = 2f, ValencePower = 0.5f;

    /// <summary>Reorders a triangle list's triangles in place for the cache.</summary>
    public static void Optimize(Span<uint> indices, uint vertexCount)
    {
        int[] order = TriangleOrder(indices, vertexCount);
        uint[] source = indices.ToArray();
        for (int t = 0; t < order.Length; t++)
            source.AsSpan(order[t] * 3, 3).CopyTo(indices.Slice(t * 3, 3));
    }

    /// <summary>The order to draw a triangle list's triangles in for the cache, each entry a source triangle, for callers that carry data per triangle.</summary>
    public static int[] TriangleOrder(ReadOnlySpan<uint> indices, uint vertexCount)
    {
        int triangles = indices.Length / 3;
        int[] order = new int[triangles];
        if (triangles == 0)
            return order;

        // Each vertex's triangles, those not yet drawn first
        int[] live = new int[vertexCount], start = new int[vertexCount + 1];
        for (int i = 0; i < triangles * 3; i++)
            live[indices[i]]++;
        for (int v = 0; v < vertexCount; v++)
            start[v + 1] = start[v] + live[v];
        int[] adjacent = new int[triangles * 3], filled = new int[vertexCount];
        for (int t = 0; t < triangles; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                uint v = indices[t * 3 + k];
                adjacent[start[v] + filled[v]++] = t;
            }
        }

        float[] vertexScore = new float[vertexCount];
        for (int v = 0; v < vertexCount; v++)
            vertexScore[v] = Score(-1, live[v]);
        bool[] drawn = new bool[triangles];
        List<uint> cache = new(CacheSize + 3), next = new(CacheSize + 3);
        int best = -1, cursor = 0;
        for (int written = 0; written < triangles; written++)
        {
            if (best < 0)
            {
                while (drawn[cursor])
                    cursor++;
                best = cursor;
            }

            drawn[best] = true;
            order[written] = best;
            next.Clear();
            for (int k = 0; k < 3; k++)
            {
                uint v = indices[best * 3 + k];
                if (!next.Contains(v))
                    next.Add(v);
                int at = start[v], end = start[v] + live[v];
                while (adjacent[at] != best)
                    at++;
                adjacent[at] = adjacent[end - 1];
                live[v]--;
            }
            foreach (uint v in cache)
            {
                if (!next.Contains(v))
                    next.Add(v);
            }
            (cache, next) = (next, cache);
            for (int i = 0; i < cache.Count; i++)
                vertexScore[cache[i]] = Score(i < CacheSize ? i : -1, live[cache[i]]);

            // The best triangle touching the cache goes next, else the first one left
            best = -1;
            float bestScore = -1;
            foreach (uint v in cache)
            {
                for (int a = start[v]; a < start[v] + live[v]; a++)
                {
                    int t = adjacent[a];
                    float score = vertexScore[indices[t * 3]] + vertexScore[indices[t * 3 + 1]] + vertexScore[indices[t * 3 + 2]];
                    if (score > bestScore)
                        (best, bestScore) = (t, score);
                }
            }
            if (cache.Count > CacheSize)
                cache.RemoveRange(CacheSize, cache.Count - CacheSize);
        }
        return order;
    }

    /// <summary>
    /// Each vertex's new number, in the order the indices first use them, unused vertices after in their own order.
    /// Indices and every vertex stream are then remapped through it.
    /// </summary>
    public static uint[] FirstUseOrder(ReadOnlySpan<uint> indices, uint vertexCount)
    {
        uint[] remap = new uint[vertexCount];
        Array.Fill(remap, uint.MaxValue);
        uint placed = 0;
        foreach (uint index in indices)
        {
            if (remap[index] == uint.MaxValue)
                remap[index] = placed++;
        }
        for (uint v = 0; v < vertexCount; v++)
        {
            if (remap[v] == uint.MaxValue)
                remap[v] = placed++;
        }
        return remap;
    }

    /// <summary>Vertices a <see cref="CacheSize"/> entry FIFO cache transforms per triangle, 0.5 at best on a large grid and 3 at worst.</summary>
    public static double MissRatio(ReadOnlySpan<uint> indices)
    {
        Queue<uint> fifo = new();
        HashSet<uint> cached = new();
        int misses = 0;
        foreach (uint index in indices)
        {
            if (!cached.Add(index))
                continue;
            misses++;
            fifo.Enqueue(index);
            if (fifo.Count > CacheSize)
                cached.Remove(fifo.Dequeue());
        }
        return indices.Length < 3 ? 0 : misses / (indices.Length / 3.0);
    }

    private static float Score(int cachePosition, int liveTriangles)
    {
        if (liveTriangles == 0)
            return -1;
        float score = 0;
        if (cachePosition >= 3)
            score = MathF.Pow(1 - (cachePosition - 3) / (float)(CacheSize - 3), DecayPower);
        else if (cachePosition >= 0)
            score = LastTriangleScore;
        return score + ValenceScale * MathF.Pow(liveTriangles, -ValencePower);
    }
}
