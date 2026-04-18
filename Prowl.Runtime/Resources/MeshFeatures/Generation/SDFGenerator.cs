// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.MeshFeatures.Generation;

/// <summary>
/// CPU-side generator for a <see cref="MeshSDF"/>. Computes nearest-triangle distance
/// using a uniform spatial grid (O(local) per voxel) and signs the result via 3-axis
/// ray-parity majority vote (also accelerated by the grid).
/// </summary>
/// <remarks>
/// Sign correctness assumes the input mesh is mostly closed. For meshes with intentional
/// holes (cloth planes, leaves, etc.) the sign of voxels behind the holes may be wrong;
/// the magnitude (unsigned distance) is always correct.
/// </remarks>
public static class SDFGenerator
{
    public struct Options
    {
        /// <summary>Grid resolution along each axis. Default 64.</summary>
        public int Resolution;

        /// <summary>Margin added around the source mesh AABB, as fraction of the longest bounds axis. Default 0.1.</summary>
        public float PaddingFraction;

        /// <summary>
        /// Distance values are clamped to <c>±MaxDistanceFraction × longest_axis</c>.
        /// Default 0.25 — enough for most surface-shading use cases.
        /// </summary>
        public float MaxDistanceFraction;

        public static Options Default => new()
        {
            Resolution = 64,
            PaddingFraction = 0.1f,
            MaxDistanceFraction = 0.25f,
        };
    }

    /// <summary>
    /// Build an <see cref="MeshSDF"/> for the mesh. Returns null if the mesh has no usable
    /// triangles. Blocks the calling thread — offload to a background task for UI responsiveness.
    /// </summary>
    public static MeshSDF? Generate(Mesh mesh, Options options)
    {
        var tris = ExtractTriangles(mesh);
        if (tris.Length == 0) return null;

        int res = Math.Max(4, options.Resolution);

        AABB bounds = ComputeBounds(tris);
        Float3 size = bounds.Max - bounds.Min;
        float longestAxis = MathF.Max(size.X, MathF.Max(size.Y, size.Z));
        if (longestAxis <= 0) return null;

        float padding = longestAxis * MathF.Max(0, options.PaddingFraction);
        bounds.Expand(padding);
        size = bounds.Max - bounds.Min;

        float maxDistance = longestAxis * MathF.Max(0.01f, options.MaxDistanceFraction);

        Float3 cellSize = new(size.X / res, size.Y / res, size.Z / res);

        // Build spatial index. Using ~half the SDF resolution keeps the average tri count
        // per cell low while not exploding memory for large meshes.
        int gridRes = Math.Max(4, res / 2);
        var grid = new TriangleGrid(tris, bounds, gridRes);

        float[] distances = new float[res * res * res];

        // Per-thread scratch for ray-parity tri-already-tested tracking.
        var visitedPool = new ThreadLocal<int[]>(() => new int[tris.Length]);
        var queryIdPool = new ThreadLocal<StrongBox<int>>(() => new StrongBox<int>(0));

        Parallel.For(0, res, z =>
        {
            int[] visited = visitedPool.Value!;
            var qBox = queryIdPool.Value!;

            float pz = bounds.Min.Z + (z + 0.5f) * cellSize.Z;
            for (int y = 0; y < res; y++)
            {
                float py = bounds.Min.Y + (y + 0.5f) * cellSize.Y;
                int rowBase = (z * res + y) * res;
                for (int x = 0; x < res; x++)
                {
                    float px = bounds.Min.X + (x + 0.5f) * cellSize.X;
                    Float3 p = new(px, py, pz);

                    float distSq = grid.ClosestDistanceSquared(p, tris, maxDistance);
                    float dist = MathF.Sqrt(distSq);

                    int insideVotes = 0;
                    qBox.Value++;
                    if (grid.RayCrossings(p, _RayA, tris, visited, qBox.Value) % 2 == 1) insideVotes++;
                    qBox.Value++;
                    if (grid.RayCrossings(p, _RayB, tris, visited, qBox.Value) % 2 == 1) insideVotes++;
                    qBox.Value++;
                    if (grid.RayCrossings(p, _RayC, tris, visited, qBox.Value) % 2 == 1) insideVotes++;

                    float sign = insideVotes >= 2 ? -1f : 1f;
                    float signed = sign * dist;
                    if (signed > maxDistance) signed = maxDistance;
                    else if (signed < -maxDistance) signed = -maxDistance;
                    distances[rowBase + x] = signed;
                }
            }
        });

        var volume = new Texture3D((uint)res, (uint)res, (uint)res, false, TextureImageFormat.Float);
        volume.SetData<float>(distances);

        return new MeshSDF
        {
            Volume = volume,
            Bounds = bounds,
            Resolution = new Int3(res, res, res),
            Padding = padding,
            MaxDistance = maxDistance,
        };
    }

    // Three off-axis rays for parity voting. Off-axis to avoid tri-edge degenerate hits.
    private static readonly Float3 _RayA = Float3.Normalize(new Float3(1.0f, 0.123f, 0.456f));
    private static readonly Float3 _RayB = Float3.Normalize(new Float3(-0.456f, 1.0f, 0.234f));
    private static readonly Float3 _RayC = Float3.Normalize(new Float3(0.234f, -0.345f, 1.0f));

    internal readonly struct Tri
    {
        public readonly Float3 A, B, C;
        public Tri(Float3 a, Float3 b, Float3 c) { A = a; B = b; C = c; }
    }

    private static Tri[] ExtractTriangles(Mesh mesh)
    {
        var verts = mesh.Vertices;
        var indices = mesh.Indices;
        if (verts == null || indices == null || verts.Length == 0 || indices.Length < 3)
            return Array.Empty<Tri>();

        var list = new List<Tri>(indices.Length / 3);
        int subCount = mesh.SubMeshCount;
        for (int s = 0; s < subCount; s++)
        {
            var sub = mesh.GetSubMesh(s);
            if (sub.Topology != Topology.Triangles) continue;

            int end = Math.Min(sub.IndexStart + sub.IndexCount, indices.Length);
            for (int i = sub.IndexStart; i + 2 < end; i += 3)
            {
                uint ia = indices[i], ib = indices[i + 1], ic = indices[i + 2];
                if (ia >= verts.Length || ib >= verts.Length || ic >= verts.Length) continue;
                list.Add(new Tri(verts[ia], verts[ib], verts[ic]));
            }
        }
        return list.ToArray();
    }

    private static AABB ComputeBounds(Tri[] tris)
    {
        Float3 min = new(float.MaxValue);
        Float3 max = new(float.MinValue);
        for (int i = 0; i < tris.Length; i++)
        {
            var t = tris[i];
            Encapsulate(ref min, ref max, t.A);
            Encapsulate(ref min, ref max, t.B);
            Encapsulate(ref min, ref max, t.C);
        }
        return new AABB(min, max);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Encapsulate(ref Float3 min, ref Float3 max, Float3 p)
    {
        if (p.X < min.X) min.X = p.X;
        if (p.Y < min.Y) min.Y = p.Y;
        if (p.Z < min.Z) min.Z = p.Z;
        if (p.X > max.X) max.X = p.X;
        if (p.Y > max.Y) max.Y = p.Y;
        if (p.Z > max.Z) max.Z = p.Z;
    }

    /// <summary>Closest point on triangle (a,b,c) to p — Ericson's Voronoi-region method.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Float3 ClosestPointOnTriangle(Float3 p, Float3 a, Float3 b, Float3 c)
    {
        Float3 ab = b - a, ac = c - a, ap = p - a;
        float d1 = Float3.Dot(ab, ap);
        float d2 = Float3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;

        Float3 bp = p - b;
        float d3 = Float3.Dot(ab, bp);
        float d4 = Float3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;

        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
            return a + ab * (d1 / (d1 - d3));

        Float3 cp = p - c;
        float d5 = Float3.Dot(ab, cp);
        float d6 = Float3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;

        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
            return a + ac * (d2 / (d2 - d6));

        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && (d4 - d3) >= 0 && (d5 - d6) >= 0)
            return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));

        float denom = 1.0f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }

    /// <summary>
    /// Möller–Trumbore ray-triangle intersection. Returns true if the ray hits with t > 0.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool RayTriHit(Float3 ro, Float3 rd, Float3 a, Float3 b, Float3 c)
    {
        const float EPS = 1e-7f;
        Float3 ab = b - a;
        Float3 ac = c - a;
        Float3 pvec = Float3.Cross(rd, ac);
        float det = Float3.Dot(ab, pvec);
        if (det > -EPS && det < EPS) return false;
        float invDet = 1.0f / det;
        Float3 tvec = ro - a;
        float u = Float3.Dot(tvec, pvec) * invDet;
        if (u < 0 || u > 1) return false;
        Float3 qvec = Float3.Cross(tvec, ab);
        float v = Float3.Dot(rd, qvec) * invDet;
        if (v < 0 || u + v > 1) return false;
        float t = Float3.Dot(ac, qvec) * invDet;
        return t > EPS;
    }
}

/// <summary>
/// Uniform spatial grid for triangle queries. Each cell stores the indices of triangles
/// whose AABB intersects the cell. Supports closest-distance queries (ring expansion)
/// and ray-traversal queries (3D-DDA).
/// </summary>
internal sealed class TriangleGrid
{
    public readonly Int3 Res;
    public readonly Float3 Origin;
    public readonly Float3 CellSize;
    public readonly Float3 InvCellSize;
    public readonly float MinCellSize;

    private readonly int[] _cellStart;   // length = (res.x*res.y*res.z) + 1; prefix-sum offset into _triIndices
    private readonly int[] _triIndices;  // flat triangle indices, grouped by cell

    public TriangleGrid(SDFGenerator.Tri[] tris, AABB bounds, int gridRes)
    {
        Res = new Int3(gridRes, gridRes, gridRes);
        Origin = bounds.Min;
        Float3 size = bounds.Max - bounds.Min;
        CellSize = new Float3(size.X / gridRes, size.Y / gridRes, size.Z / gridRes);
        InvCellSize = new Float3(1f / CellSize.X, 1f / CellSize.Y, 1f / CellSize.Z);
        MinCellSize = MathF.Min(CellSize.X, MathF.Min(CellSize.Y, CellSize.Z));

        int totalCells = gridRes * gridRes * gridRes;
        _cellStart = new int[totalCells + 1];

        // Phase 1: count tris per cell.
        for (int i = 0; i < tris.Length; i++)
        {
            ComputeTriCellRange(tris[i], out int x0, out int x1, out int y0, out int y1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        _cellStart[CellIndex(x, y, z) + 1]++;
        }

        // Prefix sum → offsets.
        for (int i = 1; i <= totalCells; i++)
            _cellStart[i] += _cellStart[i - 1];

        int totalEntries = _cellStart[totalCells];
        _triIndices = new int[totalEntries];

        // Phase 2: fill. Use a temp cursor-per-cell.
        int[] cursor = new int[totalCells];
        for (int i = 0; i < tris.Length; i++)
        {
            ComputeTriCellRange(tris[i], out int x0, out int x1, out int y0, out int y1, out int z0, out int z1);
            for (int z = z0; z <= z1; z++)
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int cellId = CellIndex(x, y, z);
                        int idx = _cellStart[cellId] + cursor[cellId]++;
                        _triIndices[idx] = i;
                    }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int CellIndex(int x, int y, int z) => x + Res.X * (y + Res.Y * z);

    private void ComputeTriCellRange(SDFGenerator.Tri t, out int x0, out int x1, out int y0, out int y1, out int z0, out int z1)
    {
        Float3 min = t.A, max = t.A;
        SDFGenerator.Encapsulate(ref min, ref max, t.B);
        SDFGenerator.Encapsulate(ref min, ref max, t.C);

        x0 = Math.Clamp((int)MathF.Floor((min.X - Origin.X) * InvCellSize.X), 0, Res.X - 1);
        y0 = Math.Clamp((int)MathF.Floor((min.Y - Origin.Y) * InvCellSize.Y), 0, Res.Y - 1);
        z0 = Math.Clamp((int)MathF.Floor((min.Z - Origin.Z) * InvCellSize.Z), 0, Res.Z - 1);
        x1 = Math.Clamp((int)MathF.Floor((max.X - Origin.X) * InvCellSize.X), 0, Res.X - 1);
        y1 = Math.Clamp((int)MathF.Floor((max.Y - Origin.Y) * InvCellSize.Y), 0, Res.Y - 1);
        z1 = Math.Clamp((int)MathF.Floor((max.Z - Origin.Z) * InvCellSize.Z), 0, Res.Z - 1);
    }

    /// <summary>
    /// Closest-distance² from p to any triangle, capped at <paramref name="maxDistance"/>.
    /// Expands rings of cells around p's cell until provably no closer triangle can exist.
    /// </summary>
    public float ClosestDistanceSquared(Float3 p, SDFGenerator.Tri[] tris, float maxDistance)
    {
        float minSq = maxDistance * maxDistance;

        int px = Math.Clamp((int)MathF.Floor((p.X - Origin.X) * InvCellSize.X), 0, Res.X - 1);
        int py = Math.Clamp((int)MathF.Floor((p.Y - Origin.Y) * InvCellSize.Y), 0, Res.Y - 1);
        int pz = Math.Clamp((int)MathF.Floor((p.Z - Origin.Z) * InvCellSize.Z), 0, Res.Z - 1);

        int maxRing = Math.Max(Res.X, Math.Max(Res.Y, Res.Z));
        for (int r = 0; r <= maxRing; r++)
        {
            // After ring r-1 was processed, the closest possible tri in ring r is
            // at distance >= (r-1)*minCellSize from p (innermost edge of ring-r cells).
            // If that exceeds our current best, stop.
            if (r >= 1)
            {
                float minDist = (r - 1) * MinCellSize;
                if (minDist * minDist >= minSq) break;
            }

            int xLo = Math.Max(0, px - r), xHi = Math.Min(Res.X - 1, px + r);
            int yLo = Math.Max(0, py - r), yHi = Math.Min(Res.Y - 1, py + r);
            int zLo = Math.Max(0, pz - r), zHi = Math.Min(Res.Z - 1, pz + r);

            for (int z = zLo; z <= zHi; z++)
            {
                for (int y = yLo; y <= yHi; y++)
                {
                    for (int x = xLo; x <= xHi; x++)
                    {
                        // Only cells on the shell at exactly Chebyshev distance r.
                        if (r > 0 && Math.Abs(x - px) != r && Math.Abs(y - py) != r && Math.Abs(z - pz) != r)
                            continue;

                        int cellId = CellIndex(x, y, z);
                        int start = _cellStart[cellId];
                        int end = _cellStart[cellId + 1];
                        for (int i = start; i < end; i++)
                        {
                            int triIdx = _triIndices[i];
                            var t = tris[triIdx];
                            Float3 closest = SDFGenerator.ClosestPointOnTriangle(p, t.A, t.B, t.C);
                            Float3 d = p - closest;
                            float dSq = d.X * d.X + d.Y * d.Y + d.Z * d.Z;
                            if (dSq < minSq) minSq = dSq;
                        }
                    }
                }
            }
        }

        return minSq;
    }

    /// <summary>
    /// Count ray-triangle intersections with t &gt; 0 from origin in <paramref name="dir"/>.
    /// Uses 3D-DDA cell traversal; the visited[] scratch ensures each tri is tested at most
    /// once per query (multi-cell tris won't double-count). Caller increments queryId per call.
    /// </summary>
    public int RayCrossings(Float3 origin, Float3 dir, SDFGenerator.Tri[] tris, int[] visited, int queryId)
    {
        // Start cell.
        int cx = Math.Clamp((int)MathF.Floor((origin.X - Origin.X) * InvCellSize.X), 0, Res.X - 1);
        int cy = Math.Clamp((int)MathF.Floor((origin.Y - Origin.Y) * InvCellSize.Y), 0, Res.Y - 1);
        int cz = Math.Clamp((int)MathF.Floor((origin.Z - Origin.Z) * InvCellSize.Z), 0, Res.Z - 1);

        int stepX = dir.X > 0 ? 1 : (dir.X < 0 ? -1 : 0);
        int stepY = dir.Y > 0 ? 1 : (dir.Y < 0 ? -1 : 0);
        int stepZ = dir.Z > 0 ? 1 : (dir.Z < 0 ? -1 : 0);

        // tMax: distance along ray to next cell boundary in each axis.
        float tMaxX = NextBoundary(origin.X, dir.X, cx, Origin.X, CellSize.X, stepX);
        float tMaxY = NextBoundary(origin.Y, dir.Y, cy, Origin.Y, CellSize.Y, stepY);
        float tMaxZ = NextBoundary(origin.Z, dir.Z, cz, Origin.Z, CellSize.Z, stepZ);

        // tDelta: distance along ray needed to traverse a full cell.
        float tDeltaX = stepX != 0 ? CellSize.X / MathF.Abs(dir.X) : float.MaxValue;
        float tDeltaY = stepY != 0 ? CellSize.Y / MathF.Abs(dir.Y) : float.MaxValue;
        float tDeltaZ = stepZ != 0 ? CellSize.Z / MathF.Abs(dir.Z) : float.MaxValue;

        int crossings = 0;
        int safety = Res.X + Res.Y + Res.Z + 4;

        while (true)
        {
            if (cx >= 0 && cx < Res.X && cy >= 0 && cy < Res.Y && cz >= 0 && cz < Res.Z)
            {
                int cellId = CellIndex(cx, cy, cz);
                int start = _cellStart[cellId];
                int end = _cellStart[cellId + 1];
                for (int i = start; i < end; i++)
                {
                    int triIdx = _triIndices[i];
                    if (visited[triIdx] == queryId) continue;
                    visited[triIdx] = queryId;
                    var t = tris[triIdx];
                    if (SDFGenerator.RayTriHit(origin, dir, t.A, t.B, t.C))
                        crossings++;
                }
            }

            // Step to next cell along smallest tMax.
            if (tMaxX < tMaxY)
            {
                if (tMaxX < tMaxZ) { cx += stepX; tMaxX += tDeltaX; }
                else { cz += stepZ; tMaxZ += tDeltaZ; }
            }
            else
            {
                if (tMaxY < tMaxZ) { cy += stepY; tMaxY += tDeltaY; }
                else { cz += stepZ; tMaxZ += tDeltaZ; }
            }

            // Stop when we've left the grid in any direction the ray is moving.
            if (cx < 0 || cx >= Res.X || cy < 0 || cy >= Res.Y || cz < 0 || cz >= Res.Z)
                break;

            if (--safety <= 0) break;
        }

        return crossings;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float NextBoundary(float pos, float dir, int cell, float origin, float cellSize, int step)
    {
        if (step == 0) return float.MaxValue;
        // World-space coord of the next boundary the ray will hit on this axis.
        float boundary = origin + (cell + (step > 0 ? 1 : 0)) * cellSize;
        return (boundary - pos) / dir;
    }
}
