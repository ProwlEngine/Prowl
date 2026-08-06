// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// The triangle rasterization below is ported from DotRecast (RcRasterizations.cs), which is
// itself a port of Recast by Mikko Mononen — both zlib licensed:
//   Copyright (c) 2009-2010 Mikko Mononen memon@inside.org
//   DotRecast Copyright (c) 2023-2024 Choi Ikpil ikpil@naver.com
// Altered for Prowl: spans are allocated from the heightfield's span pool (the RcSpanPool /
// freelist mechanism that upstream C++ Recast uses but the C# port's public AddSpan bypasses
// with per-span `new`), merged-away spans are returned to the freelist, and the walkable-slope
// test is folded into rasterization so no per-chunk area arrays are allocated.

using System;

using DotRecast.Core.Numerics;
using DotRecast.Recast;

namespace Prowl.Runtime;

/// <summary>
/// Allocation-free (steady-state) triangle rasterization into an <see cref="RcHeightfield"/>.
/// Span objects come from the heightfield's pool pages, which
/// <see cref="NavMeshTileBuilder"/> recycles across tiles per thread — so rebuilding tiles
/// allocates no span objects once the pools have grown to the working-set size.
/// </summary>
internal static class NavMeshRasterizer
{
    /// <summary>
    /// Rasterize triangles with a per-triangle walkable-slope test: up-facing triangles within
    /// the slope limit get <paramref name="walkableArea"/> (a Detour area value), steeper ones
    /// rasterize as null-area (solid, unwalkable — they still occupy space).
    /// </summary>
    /// <param name="heightfield">Target heightfield (spans come from its pool).</param>
    /// <param name="verts">Vertex components (x,y,z per vertex).</param>
    /// <param name="tris">Triangle vertex indices (three per triangle).</param>
    /// <param name="numTris">Number of triangles to rasterize.</param>
    /// <param name="walkableSlopeCos">Cosine of the maximum walkable slope angle.</param>
    /// <param name="walkableArea">Detour area value for walkable triangles.</param>
    /// <param name="flagMergeThreshold">Span merge threshold in voxels (walkable climb).</param>
    public static void RasterizeTriangles(RcHeightfield heightfield, float[] verts, int[] tris, int numTris,
        float walkableSlopeCos, int walkableArea, int flagMergeThreshold)
    {
        float inverseCellSize = 1.0f / heightfield.cs;
        float inverseCellHeight = 1.0f / heightfield.ch;

        for (int t = 0; t < numTris; t++)
        {
            int v0 = tris[t * 3 + 0];
            int v1 = tris[t * 3 + 1];
            int v2 = tris[t * 3 + 2];

            // Walkable-slope test (RcRecast.MarkWalkableTriangles, inlined per triangle).
            int area = TriangleNormalY(verts, v0, v1, v2) > walkableSlopeCos ? walkableArea : 0;

            RasterizeTri(verts, v0, v1, v2, area, heightfield,
                heightfield.bmin, heightfield.bmax, heightfield.cs, inverseCellSize, inverseCellHeight, flagMergeThreshold);
        }
    }

    /// <summary>Y component of the (normalized) face normal — all the slope test needs.</summary>
    private static float TriangleNormalY(float[] verts, int v0, int v1, int v2)
    {
        float ax = verts[v1 * 3 + 0] - verts[v0 * 3 + 0];
        float ay = verts[v1 * 3 + 1] - verts[v0 * 3 + 1];
        float az = verts[v1 * 3 + 2] - verts[v0 * 3 + 2];
        float bx = verts[v2 * 3 + 0] - verts[v0 * 3 + 0];
        float by = verts[v2 * 3 + 1] - verts[v0 * 3 + 1];
        float bz = verts[v2 * 3 + 2] - verts[v0 * 3 + 2];

        float nx = ay * bz - az * by;
        float ny = az * bx - ax * bz;
        float nz = ax * by - ay * bx;
        float lenSq = nx * nx + ny * ny + nz * nz;
        if (lenSq <= 1e-12f) return 0f; // degenerate: not walkable
        return ny / MathF.Sqrt(lenSq);
    }

    #region Span pool (upstream Recast's rcAllocSpan/rcFreeSpan, unused by the C# port's public path)

    /// <summary>Rebuild a freelist chaining every span of every pool page. Used when recycled
    /// pool pages are attached to a fresh heightfield: all their spans are free by definition.</summary>
    public static RcSpan? BuildFreeList(RcSpanPool? pools)
    {
        RcSpan? freelist = null;
        for (RcSpanPool? pool = pools; pool != null; pool = pool.next)
        {
            for (int i = 0; i < pool.items.Length; i++)
            {
                pool.items[i].next = freelist;
                freelist = pool.items[i];
            }
        }
        return freelist;
    }

    private static RcSpan AllocSpan(RcHeightfield heightfield)
    {
        if (heightfield.freelist == null)
        {
            // New pool page; chain its spans onto the freelist.
            var spanPool = new RcSpanPool { next = heightfield.pools };
            heightfield.pools = spanPool;
            RcSpan freeList = heightfield.freelist;
            for (int i = 0; i < spanPool.items.Length; i++)
            {
                spanPool.items[i].next = freeList;
                freeList = spanPool.items[i];
            }
            heightfield.freelist = freeList;
        }

        RcSpan newSpan = heightfield.freelist;
        heightfield.freelist = heightfield.freelist.next;
        return newSpan;
    }

    private static void FreeSpan(RcHeightfield heightfield, RcSpan span)
    {
        span.next = heightfield.freelist;
        heightfield.freelist = span;
    }

    #endregion

    /// <summary>rcAddSpan with pooled spans: allocations go through the pool and spans removed
    /// by merging are returned to it (as upstream C++ does; the C# port drops them).</summary>
    private static void AddSpan(RcHeightfield heightfield, int x, int z, int min, int max, int areaID, int flagMergeThreshold)
    {
        RcSpan newSpan = AllocSpan(heightfield);
        newSpan.smin = min;
        newSpan.smax = max;
        newSpan.area = areaID;
        newSpan.next = null;

        int columnIndex = x + z * heightfield.width;

        if (heightfield.spans[columnIndex] == null)
        {
            heightfield.spans[columnIndex] = newSpan;
            return;
        }

        RcSpan? previousSpan = null;
        RcSpan? currentSpan = heightfield.spans[columnIndex];

        while (currentSpan != null)
        {
            if (currentSpan.smin > newSpan.smax)
            {
                break; // current span is past the new span
            }

            if (currentSpan.smax < newSpan.smin)
            {
                // Entirely before the new span; keep walking.
                previousSpan = currentSpan;
                currentSpan = currentSpan.next;
            }
            else
            {
                // Overlap: merge into newSpan.
                if (currentSpan.smin < newSpan.smin) newSpan.smin = currentSpan.smin;
                if (currentSpan.smax > newSpan.smax) newSpan.smax = currentSpan.smax;

                if (Math.Abs(newSpan.smax - currentSpan.smax) <= flagMergeThreshold)
                    newSpan.area = Math.Max(newSpan.area, currentSpan.area);

                // Unlink the merged span and recycle it.
                RcSpan? next = currentSpan.next;
                if (previousSpan != null) previousSpan.next = next;
                else heightfield.spans[columnIndex] = next;
                FreeSpan(heightfield, currentSpan);
                currentSpan = next;
            }
        }

        if (previousSpan != null)
        {
            newSpan.next = previousSpan.next;
            previousSpan.next = newSpan;
        }
        else
        {
            newSpan.next = heightfield.spans[columnIndex];
            heightfield.spans[columnIndex] = newSpan;
        }
    }

    #region Triangle clipping (verbatim port of RcRasterizations.DividePoly / RasterizeTri)

    private static bool OverlapBounds(RcVec3f aMin, RcVec3f aMax, RcVec3f bMin, RcVec3f bMax)
    {
        return aMin.X <= bMax.X && aMax.X >= bMin.X
            && aMin.Y <= bMax.Y && aMax.Y >= bMin.Y
            && aMin.Z <= bMax.Z && aMax.Z >= bMin.Z;
    }

    private static void CopyVert(Span<float> dst, int dstOffset, ReadOnlySpan<float> src, int srcOffset)
    {
        dst[dstOffset + 0] = src[srcOffset + 0];
        dst[dstOffset + 1] = src[srcOffset + 1];
        dst[dstOffset + 2] = src[srcOffset + 2];
    }

    private static void DividePoly(Span<float> inVerts, int inVertsOffset, int inVertsCount,
        int outVerts1, out int outVerts1Count,
        int outVerts2, out int outVerts2Count,
        float axisOffset, int axis)
    {
        Span<float> inVertAxisDelta = stackalloc float[12];
        for (int inVert = 0; inVert < inVertsCount; ++inVert)
            inVertAxisDelta[inVert] = axisOffset - inVerts[inVertsOffset + inVert * 3 + axis];

        int poly1Vert = 0;
        int poly2Vert = 0;
        for (int inVertA = 0, inVertB = inVertsCount - 1; inVertA < inVertsCount; inVertB = inVertA, ++inVertA)
        {
            bool sameSide = (inVertAxisDelta[inVertA] >= 0) == (inVertAxisDelta[inVertB] >= 0);
            if (!sameSide)
            {
                float s = inVertAxisDelta[inVertB] / (inVertAxisDelta[inVertB] - inVertAxisDelta[inVertA]);
                inVerts[outVerts1 + poly1Vert * 3 + 0] = inVerts[inVertsOffset + inVertB * 3 + 0] + (inVerts[inVertsOffset + inVertA * 3 + 0] - inVerts[inVertsOffset + inVertB * 3 + 0]) * s;
                inVerts[outVerts1 + poly1Vert * 3 + 1] = inVerts[inVertsOffset + inVertB * 3 + 1] + (inVerts[inVertsOffset + inVertA * 3 + 1] - inVerts[inVertsOffset + inVertB * 3 + 1]) * s;
                inVerts[outVerts1 + poly1Vert * 3 + 2] = inVerts[inVertsOffset + inVertB * 3 + 2] + (inVerts[inVertsOffset + inVertA * 3 + 2] - inVerts[inVertsOffset + inVertB * 3 + 2]) * s;
                CopyVert(inVerts, outVerts2 + poly2Vert * 3, inVerts, outVerts1 + poly1Vert * 3);
                poly1Vert++;
                poly2Vert++;

                if (inVertAxisDelta[inVertA] > 0)
                {
                    CopyVert(inVerts, outVerts1 + poly1Vert * 3, inVerts, inVertsOffset + inVertA * 3);
                    poly1Vert++;
                }
                else if (inVertAxisDelta[inVertA] < 0)
                {
                    CopyVert(inVerts, outVerts2 + poly2Vert * 3, inVerts, inVertsOffset + inVertA * 3);
                    poly2Vert++;
                }
            }
            else
            {
                if (inVertAxisDelta[inVertA] >= 0)
                {
                    CopyVert(inVerts, outVerts1 + poly1Vert * 3, inVerts, inVertsOffset + inVertA * 3);
                    poly1Vert++;
                    if (inVertAxisDelta[inVertA] != 0)
                        continue;
                }

                CopyVert(inVerts, outVerts2 + poly2Vert * 3, inVerts, inVertsOffset + inVertA * 3);
                poly2Vert++;
            }
        }

        outVerts1Count = poly1Vert;
        outVerts2Count = poly2Vert;
    }

    private static void RasterizeTri(float[] verts, int v0, int v1, int v2,
        int areaID, RcHeightfield heightfield,
        RcVec3f heightfieldBBMin, RcVec3f heightfieldBBMax,
        float cellSize, float inverseCellSize, float inverseCellHeight,
        int flagMergeThreshold)
    {
        var triBBMin = new RcVec3f(verts[v0 * 3], verts[v0 * 3 + 1], verts[v0 * 3 + 2]);
        var triBBMax = triBBMin;
        for (int i = 0; i < 2; i++)
        {
            int v = i == 0 ? v1 : v2;
            var p = new RcVec3f(verts[v * 3], verts[v * 3 + 1], verts[v * 3 + 2]);
            triBBMin = RcVec3f.Min(triBBMin, p);
            triBBMax = RcVec3f.Max(triBBMax, p);
        }

        if (!OverlapBounds(triBBMin, triBBMax, heightfieldBBMin, heightfieldBBMax))
            return;

        int w = heightfield.width;
        int h = heightfield.height;
        float by = heightfieldBBMax.Y - heightfieldBBMin.Y;

        int z0 = (int)((triBBMin.Z - heightfieldBBMin.Z) * inverseCellSize);
        int z1 = (int)((triBBMax.Z - heightfieldBBMin.Z) * inverseCellSize);

        // -1 rather than 0 so the polygon is cut properly at the start of the tile.
        z0 = Math.Clamp(z0, -1, h - 1);
        z1 = Math.Clamp(z1, 0, h - 1);

        Span<float> buf = stackalloc float[7 * 3 * 4];
        int @in = 0;
        int inRow = 7 * 3;
        int p1 = inRow + 7 * 3;
        int p2 = p1 + 7 * 3;

        CopyVert(buf, 0, verts.AsSpan(v0 * 3, 3), 0);
        CopyVert(buf, 3, verts.AsSpan(v1 * 3, 3), 0);
        CopyVert(buf, 6, verts.AsSpan(v2 * 3, 3), 0);
        int nvRow;
        int nvIn = 3;

        for (int z = z0; z <= z1; ++z)
        {
            float cellZ = heightfieldBBMin.Z + z * cellSize;
            DividePoly(buf, @in, nvIn, inRow, out nvRow, p1, out nvIn, cellZ + cellSize, RcAxis.RC_AXIS_Z);
            (@in, p1) = (p1, @in);

            if (nvRow < 3) continue;
            if (z < 0) continue;

            float minX = buf[inRow];
            float maxX = buf[inRow];
            for (int i = 1; i < nvRow; ++i)
            {
                float v = buf[inRow + i * 3];
                minX = Math.Min(minX, v);
                maxX = Math.Max(maxX, v);
            }

            int x0 = (int)((minX - heightfieldBBMin.X) * inverseCellSize);
            int x1 = (int)((maxX - heightfieldBBMin.X) * inverseCellSize);
            if (x1 < 0 || x0 >= w) continue;

            x0 = Math.Clamp(x0, -1, w - 1);
            x1 = Math.Clamp(x1, 0, w - 1);

            int nv;
            int nv2 = nvRow;
            for (int x = x0; x <= x1; ++x)
            {
                float cx = heightfieldBBMin.X + x * cellSize;
                DividePoly(buf, inRow, nv2, p1, out nv, p2, out nv2, cx + cellSize, RcAxis.RC_AXIS_X);
                (inRow, p2) = (p2, inRow);

                if (nv < 3) continue;
                if (x < 0) continue;

                float spanMin = buf[p1 + 1];
                float spanMax = buf[p1 + 1];
                for (int i = 1; i < nv; ++i)
                {
                    spanMin = Math.Min(spanMin, buf[p1 + i * 3 + 1]);
                    spanMax = Math.Max(spanMax, buf[p1 + i * 3 + 1]);
                }

                spanMin -= heightfieldBBMin.Y;
                spanMax -= heightfieldBBMin.Y;

                if (spanMax < 0.0f) continue;
                if (spanMin > by) continue;

                if (spanMin < 0.0f) spanMin = 0;
                if (spanMax > by) spanMax = by;

                int spanMinCellIndex = Math.Clamp((int)MathF.Floor(spanMin * inverseCellHeight), 0, RcRecast.RC_SPAN_MAX_HEIGHT);
                int spanMaxCellIndex = Math.Clamp((int)MathF.Ceiling(spanMax * inverseCellHeight), spanMinCellIndex + 1, RcRecast.RC_SPAN_MAX_HEIGHT);

                AddSpan(heightfield, x, z, spanMinCellIndex, spanMaxCellIndex, areaID, flagMergeThreshold);
            }
        }
    }

    #endregion
}
