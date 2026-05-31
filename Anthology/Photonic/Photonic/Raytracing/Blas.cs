using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using Prowl.Vector;

namespace Prowl.Photonic.Raytracing;

/// <summary>
/// Per-mesh acceleration structure. We build a binary SAH BVH, collapse it into a wider BVH4
/// where each node stores 4 children's AABBs in SoA Vector128 lanes, and pad every leaf to
/// exactly 4 triangles in SoA storage. Per node visit we do one SIMD ray-vs-4-AABB; per leaf
/// visit we do one SIMD ray-vs-4-triangle test (Möller-Trumbore). Padding triangles are
/// degenerate (E1 = E2 = 0) so the SIMD test rejects them automatically.
/// </summary>
internal sealed class Blas
{
    public struct Node
    {
        public AABB Bounds;
        public int LeftFirst;
        public int PrimCount;
    }

    /// <summary>
    /// BVH4 node: SoA AABB packs (lane <c>i</c> = child <c>i</c>) plus child / prim arrays.
    /// Inner children store a node index in <c>C{i}</c>; leaves store the 4-aligned first
    /// triangle index, with <c>P{i}</c> &gt; 0 marking it as a leaf.
    /// </summary>
    public struct Node4
    {
        public Vector128<float> MinX, MinY, MinZ;
        public Vector128<float> MaxX, MaxY, MaxZ;
        public int C0, C1, C2, C3;
        public int P0, P1, P2, P3;
        public int Valid;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetChild(int i) => i switch { 0 => C0, 1 => C1, 2 => C2, _ => C3 };
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetPrim(int i)  => i switch { 0 => P0, 1 => P1, 2 => P2, _ => P3 };
    }

    /// <summary>Triangle vertex indices + material group. Looked up at hit time for material info.</summary>
    public struct TriRef
    {
        public int I0, I1, I2;
        public int MaterialGroupIndex;
    }

    public BakeMesh Mesh { get; }
    public Node4[] Nodes4 { get; private set; } = System.Array.Empty<Node4>();

    /// <summary>
    /// Triangle reference array (parallel to the SoA edge arrays). Indices are padded so leaves
    /// occupy exactly 4 contiguous slots; padded slots have <c>I0=I1=I2=0</c>.
    /// </summary>
    public TriRef[] Triangles { get; private set; } = System.Array.Empty<TriRef>();

    // SoA edge arrays. Length is the padded triangle count (multiple of 4). Each element is a
    // single float: `Vector128.LoadUnsafe(ref V0X[i])` loads 4 consecutive triangles' x-components.
    public float[] V0X = System.Array.Empty<float>(), V0Y = System.Array.Empty<float>(), V0Z = System.Array.Empty<float>();
    public float[] E1X = System.Array.Empty<float>(), E1Y = System.Array.Empty<float>(), E1Z = System.Array.Empty<float>();
    public float[] E2X = System.Array.Empty<float>(), E2Y = System.Array.Empty<float>(), E2Z = System.Array.Empty<float>();

    public Blas(BakeMesh mesh) { Mesh = mesh; }

    private const int LeafThreshold = 8;
    private const int SahBins = 16;
    private const int SimdWidth = 8;  // Vector256: 8 triangles per leaf SIMD test (AVX2)

    public void Build()
    {
        int triCount = 0;
        for (int g = 0; g < Mesh.MaterialGroups.Count; g++)
            triCount += Mesh.MaterialGroups[g].Indices.Length / 3;

        var tris = new TriRef[triCount];
        var centroids = new Float3[triCount];
        var aabbs = new AABB[triCount];
        var positions = Mesh.Positions;

        int t = 0;
        for (int g = 0; g < Mesh.MaterialGroups.Count; g++)
        {
            var idx = Mesh.MaterialGroups[g].Indices;
            for (int i = 0; i < idx.Length; i += 3)
            {
                int i0 = idx[i], i1 = idx[i + 1], i2 = idx[i + 2];
                tris[t] = new TriRef { I0 = i0, I1 = i1, I2 = i2, MaterialGroupIndex = g };
                var p0 = positions[i0]; var p1 = positions[i1]; var p2 = positions[i2];
                var min = new Float3(System.Math.Min(p0.X, System.Math.Min(p1.X, p2.X)),
                                     System.Math.Min(p0.Y, System.Math.Min(p1.Y, p2.Y)),
                                     System.Math.Min(p0.Z, System.Math.Min(p1.Z, p2.Z)));
                var max = new Float3(System.Math.Max(p0.X, System.Math.Max(p1.X, p2.X)),
                                     System.Math.Max(p0.Y, System.Math.Max(p1.Y, p2.Y)),
                                     System.Math.Max(p0.Z, System.Math.Max(p1.Z, p2.Z)));
                aabbs[t] = new AABB(min, max);
                centroids[t] = (p0 + p1 + p2) * (1f / 3f);
                t++;
            }
        }

        var perm = new int[triCount];
        for (int i = 0; i < triCount; i++) perm[i] = i;

        // ---- 1) Build binary SAH BVH into a temp list. ----------------------------------------
        var bvh2 = new System.Collections.Generic.List<Node>(2 * System.Math.Max(1, triCount));
        bvh2.Add(default);
        BuildRecursive(bvh2, 0, perm, 0, triCount, aabbs, centroids);

        // ---- 2) Rebuild triangle storage as 4-aligned padded SoA so each leaf is exactly one
        //         4-pack. Leaves with fewer than 4 triangles get degenerate padding (E1 = E2 = 0)
        //         which the SIMD ray-triangle test rejects via |det| ~ 0.
        int paddedCapacity = triCount + bvh2.Count * SimdWidth; // upper bound
        var padTris = new System.Collections.Generic.List<TriRef>(paddedCapacity);
        var padV0X = new System.Collections.Generic.List<float>(paddedCapacity);
        var padV0Y = new System.Collections.Generic.List<float>(paddedCapacity);
        var padV0Z = new System.Collections.Generic.List<float>(paddedCapacity);
        var padE1X = new System.Collections.Generic.List<float>(paddedCapacity);
        var padE1Y = new System.Collections.Generic.List<float>(paddedCapacity);
        var padE1Z = new System.Collections.Generic.List<float>(paddedCapacity);
        var padE2X = new System.Collections.Generic.List<float>(paddedCapacity);
        var padE2Y = new System.Collections.Generic.List<float>(paddedCapacity);
        var padE2Z = new System.Collections.Generic.List<float>(paddedCapacity);
        for (int ni = 0; ni < bvh2.Count; ni++)
        {
            var n = bvh2[ni];
            if (n.PrimCount == 0) continue; // inner
            int firstNew = padTris.Count;
            for (int i = 0; i < n.PrimCount; i++)
            {
                var tr = tris[perm[n.LeftFirst + i]];
                var v0 = positions[tr.I0];
                var e1 = positions[tr.I1] - v0;
                var e2 = positions[tr.I2] - v0;
                padTris.Add(tr);
                padV0X.Add(v0.X); padV0Y.Add(v0.Y); padV0Z.Add(v0.Z);
                padE1X.Add(e1.X); padE1Y.Add(e1.Y); padE1Z.Add(e1.Z);
                padE2X.Add(e2.X); padE2Y.Add(e2.Y); padE2Z.Add(e2.Z);
            }
            // pad up to a 4-pack
            while ((padTris.Count - firstNew) % SimdWidth != 0)
            {
                padTris.Add(default);
                padV0X.Add(0); padV0Y.Add(0); padV0Z.Add(0);
                padE1X.Add(0); padE1Y.Add(0); padE1Z.Add(0);
                padE2X.Add(0); padE2Y.Add(0); padE2Z.Add(0);
            }
            n.LeftFirst = firstNew;
            n.PrimCount = padTris.Count - firstNew; // multiple of 4
            bvh2[ni] = n;
        }
        Triangles = padTris.ToArray();
        V0X = padV0X.ToArray(); V0Y = padV0Y.ToArray(); V0Z = padV0Z.ToArray();
        E1X = padE1X.ToArray(); E1Y = padE1Y.ToArray(); E1Z = padE1Z.ToArray();
        E2X = padE2X.ToArray(); E2Y = padE2Y.ToArray(); E2Z = padE2Z.ToArray();

        // ---- 3) Collapse BVH2 to BVH4. --------------------------------------------------------
        var bvh4 = new System.Collections.Generic.List<Node4>(System.Math.Max(1, bvh2.Count / 2));
        if (bvh2.Count > 0) BuildNode4(bvh2, 0, bvh4);
        Nodes4 = bvh4.ToArray();
    }

    private static void BuildRecursive(System.Collections.Generic.List<Node> nodes, int nodeIndex,
                                       int[] perm, int first, int count,
                                       AABB[] aabbs, Float3[] centroids)
    {
        var bounds = aabbs[perm[first]];
        for (int i = 1; i < count; i++) bounds.Encapsulate(aabbs[perm[first + i]]);
        var node = nodes[nodeIndex];
        node.Bounds = bounds;

        if (count <= LeafThreshold)
        {
            node.LeftFirst = first;
            node.PrimCount = count;
            nodes[nodeIndex] = node;
            return;
        }

        var cmin = centroids[perm[first]]; var cmax = cmin;
        for (int i = 1; i < count; i++)
        {
            var c = centroids[perm[first + i]];
            cmin = new Float3(System.Math.Min(cmin.X, c.X), System.Math.Min(cmin.Y, c.Y), System.Math.Min(cmin.Z, c.Z));
            cmax = new Float3(System.Math.Max(cmax.X, c.X), System.Math.Max(cmax.Y, c.Y), System.Math.Max(cmax.Z, c.Z));
        }
        var cext = cmax - cmin;
        int axis = 0;
        if (cext.Y > cext.X) axis = 1;
        if (cext.Z > Component(cext, axis)) axis = 2;

        if (Component(cext, axis) <= 0f)
        {
            int mid = first + count / 2;
            EmitInner(nodes, nodeIndex, ref node, perm, first, count, aabbs, centroids, mid);
            return;
        }

        var binCounts = new int[SahBins];
        var binBounds = new AABB[SahBins];
        bool[] binSet = new bool[SahBins];
        float scale = SahBins / Component(cext, axis);
        float originAxis = Component(cmin, axis);

        for (int i = 0; i < count; i++)
        {
            int p = perm[first + i];
            int b = System.Math.Min(SahBins - 1, (int)((Component(centroids[p], axis) - originAxis) * scale));
            if (b < 0) b = 0;
            binCounts[b]++;
            if (!binSet[b]) { binBounds[b] = aabbs[p]; binSet[b] = true; }
            else binBounds[b].Encapsulate(aabbs[p]);
        }

        var leftBounds = new AABB[SahBins - 1];
        var rightBounds = new AABB[SahBins - 1];
        var leftCount = new int[SahBins - 1];
        var rightCount = new int[SahBins - 1];

        AABB lAcc = default; bool lSet = false; int lCnt = 0;
        for (int b = 0; b < SahBins - 1; b++)
        {
            if (binCounts[b] > 0)
            {
                if (!lSet) { lAcc = binBounds[b]; lSet = true; }
                else lAcc.Encapsulate(binBounds[b]);
                lCnt += binCounts[b];
            }
            leftBounds[b] = lAcc;
            leftCount[b] = lCnt;
        }
        AABB rAcc = default; bool rSet = false; int rCnt = 0;
        for (int b = SahBins - 1; b >= 1; b--)
        {
            if (binCounts[b] > 0)
            {
                if (!rSet) { rAcc = binBounds[b]; rSet = true; }
                else rAcc.Encapsulate(binBounds[b]);
                rCnt += binCounts[b];
            }
            rightBounds[b - 1] = rAcc;
            rightCount[b - 1] = rCnt;
        }

        float bestCost = float.PositiveInfinity;
        int bestSplit = -1;
        float parentArea = bounds.SurfaceArea;
        for (int b = 0; b < SahBins - 1; b++)
        {
            if (leftCount[b] == 0 || rightCount[b] == 0) continue;
            float cost = leftCount[b] * leftBounds[b].SurfaceArea + rightCount[b] * rightBounds[b].SurfaceArea;
            if (cost < bestCost) { bestCost = cost; bestSplit = b; }
        }

        float leafCost = count * parentArea;
        if (bestSplit < 0 || bestCost >= leafCost)
        {
            node.LeftFirst = first;
            node.PrimCount = count;
            nodes[nodeIndex] = node;
            return;
        }

        float splitCoord = originAxis + (bestSplit + 1) * Component(cext, axis) / SahBins;
        int p_ = Partition(perm, first, count, centroids, axis, splitCoord);

        if (p_ == first || p_ == first + count) p_ = first + count / 2;

        EmitInner(nodes, nodeIndex, ref node, perm, first, count, aabbs, centroids, p_);
    }

    private static void EmitInner(System.Collections.Generic.List<Node> nodes, int nodeIndex, ref Node node,
                                  int[] perm, int first, int count, AABB[] aabbs, Float3[] centroids, int mid)
    {
        int leftIndex = nodes.Count;
        nodes.Add(default);
        nodes.Add(default);
        node.LeftFirst = leftIndex;
        node.PrimCount = 0;
        nodes[nodeIndex] = node;

        BuildRecursive(nodes, leftIndex,     perm, first, mid - first,                 aabbs, centroids);
        BuildRecursive(nodes, leftIndex + 1, perm, mid,   first + count - mid,          aabbs, centroids);
    }

    private static int Partition(int[] perm, int first, int count, Float3[] centroids, int axis, float splitCoord)
    {
        int lo = first, hi = first + count - 1;
        while (lo <= hi)
        {
            while (lo <= hi && Component(centroids[perm[lo]], axis) <  splitCoord) lo++;
            while (lo <= hi && Component(centroids[perm[hi]], axis) >= splitCoord) hi--;
            if (lo < hi)
            {
                (perm[lo], perm[hi]) = (perm[hi], perm[lo]);
                lo++; hi--;
            }
        }
        return lo;
    }

    private static float Component(Float3 v, int axis) => axis == 0 ? v.X : (axis == 1 ? v.Y : v.Z);

    // ---- BVH2 -> BVH4 collapse ---------------------------------------------------------------

    private static int BuildNode4(System.Collections.Generic.List<Node> bvh2, int bvh2Idx,
                                  System.Collections.Generic.List<Node4> result)
    {
        int myIdx = result.Count;
        result.Add(default);

        System.Span<int> slots = stackalloc int[4];
        System.Span<bool> isLeaf = stackalloc bool[4];
        int slotCount = 0;
        var root = bvh2[bvh2Idx];
        if (root.PrimCount > 0)
        {
            slots[0] = bvh2Idx; isLeaf[0] = true; slotCount = 1;
        }
        else
        {
            for (int c = 0; c < 2; c++)
            {
                int ci = root.LeftFirst + c;
                var child = bvh2[ci];
                if (child.PrimCount > 0)
                {
                    slots[slotCount] = ci; isLeaf[slotCount] = true; slotCount++;
                }
                else
                {
                    int gA = child.LeftFirst, gB = child.LeftFirst + 1;
                    slots[slotCount] = gA; isLeaf[slotCount] = bvh2[gA].PrimCount > 0; slotCount++;
                    slots[slotCount] = gB; isLeaf[slotCount] = bvh2[gB].PrimCount > 0; slotCount++;
                }
            }
        }

        System.Span<float> minX = stackalloc float[4];
        System.Span<float> minY = stackalloc float[4];
        System.Span<float> minZ = stackalloc float[4];
        System.Span<float> maxX = stackalloc float[4];
        System.Span<float> maxY = stackalloc float[4];
        System.Span<float> maxZ = stackalloc float[4];
        System.Span<int> childIdx = stackalloc int[4];
        System.Span<int> primCnt = stackalloc int[4];

        for (int i = 0; i < 4; i++)
        {
            if (i < slotCount)
            {
                int srcIdx = slots[i];
                var bbox = bvh2[srcIdx].Bounds;
                minX[i] = bbox.Min.X; minY[i] = bbox.Min.Y; minZ[i] = bbox.Min.Z;
                maxX[i] = bbox.Max.X; maxY[i] = bbox.Max.Y; maxZ[i] = bbox.Max.Z;
                if (isLeaf[i])
                {
                    childIdx[i] = bvh2[srcIdx].LeftFirst;
                    primCnt[i]  = bvh2[srcIdx].PrimCount;
                }
                else
                {
                    childIdx[i] = BuildNode4(bvh2, srcIdx, result);
                    primCnt[i] = 0;
                }
            }
            else
            {
                minX[i] = float.PositiveInfinity; minY[i] = float.PositiveInfinity; minZ[i] = float.PositiveInfinity;
                maxX[i] = float.NegativeInfinity; maxY[i] = float.NegativeInfinity; maxZ[i] = float.NegativeInfinity;
                childIdx[i] = 0; primCnt[i] = 0;
            }
        }

        var n = new Node4
        {
            MinX = Vector128.Create(minX[0], minX[1], minX[2], minX[3]),
            MinY = Vector128.Create(minY[0], minY[1], minY[2], minY[3]),
            MinZ = Vector128.Create(minZ[0], minZ[1], minZ[2], minZ[3]),
            MaxX = Vector128.Create(maxX[0], maxX[1], maxX[2], maxX[3]),
            MaxY = Vector128.Create(maxY[0], maxY[1], maxY[2], maxY[3]),
            MaxZ = Vector128.Create(maxZ[0], maxZ[1], maxZ[2], maxZ[3]),
            C0 = childIdx[0], C1 = childIdx[1], C2 = childIdx[2], C3 = childIdx[3],
            P0 = primCnt[0],  P1 = primCnt[1],  P2 = primCnt[2],  P3 = primCnt[3],
            Valid = slotCount,
        };
        result[myIdx] = n;
        return myIdx;
    }

    // ---- traversal -----------------------------------------------------------------------------

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SafeNonZero(float v) => System.Math.Abs(v) < 1e-30f ? (v < 0 ? -1e-30f : 1e-30f) : v;

    /// <summary>
    /// SIMD Möller-Trumbore against <see cref="SimdWidth"/> (= 8 with AVX2) triangles starting at
    /// <paramref name="firstTri"/>. Returns per-lane (t, u, v) and a bitmask of which lanes hit
    /// (within <c>tMin..maxT</c>). Padding triangles have <c>E1 = E2 = 0</c>, so <c>|det|</c> is
    /// zero and they're naturally rejected.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RayTri8(int firstTri,
                         Vector256<float> roX, Vector256<float> roY, Vector256<float> roZ,
                         Vector256<float> rdX, Vector256<float> rdY, Vector256<float> rdZ,
                         Vector256<float> tMinV, Vector256<float> tMaxV,
                         bool cullEnabled, bool keepPositive,
                         out Vector256<float> tOut, out Vector256<float> uOut, out Vector256<float> vOut,
                         out uint hitMask)
    {
        var v0x = Vector256.LoadUnsafe(ref V0X[firstTri]);
        var v0y = Vector256.LoadUnsafe(ref V0Y[firstTri]);
        var v0z = Vector256.LoadUnsafe(ref V0Z[firstTri]);
        var e1x = Vector256.LoadUnsafe(ref E1X[firstTri]);
        var e1y = Vector256.LoadUnsafe(ref E1Y[firstTri]);
        var e1z = Vector256.LoadUnsafe(ref E1Z[firstTri]);
        var e2x = Vector256.LoadUnsafe(ref E2X[firstTri]);
        var e2y = Vector256.LoadUnsafe(ref E2Y[firstTri]);
        var e2z = Vector256.LoadUnsafe(ref E2Z[firstTri]);

        // p = cross(rd, e2)
        var px = rdY * e2z - rdZ * e2y;
        var py = rdZ * e2x - rdX * e2z;
        var pz = rdX * e2y - rdY * e2x;

        // det = dot(e1, p). Its sign is the triangle's facing relative to the ray; backface culling
        // keeps only one side. (For a CCW-wound triangle, det > 0 = front-facing.)
        var det = e1x * px + e1y * py + e1z * pz;
        var eps = Vector256.Create(1e-12f);
        Vector256<float> validDet;
        if (!cullEnabled) validDet = Vector256.GreaterThan(Vector256.Abs(det), eps);
        else if (keepPositive) validDet = Vector256.GreaterThan(det, eps);
        else validDet = Vector256.LessThan(det, -Vector256.Create(1e-12f));

        var invDet = Vector256.Create(1f) / det;

        // tv = ro - v0
        var tvx = roX - v0x;
        var tvy = roY - v0y;
        var tvz = roZ - v0z;

        // u = dot(tv, p) * invDet, must be in [0, 1]
        var u = (tvx * px + tvy * py + tvz * pz) * invDet;
        var zero = Vector256<float>.Zero;
        var one  = Vector256.Create(1f);
        var validU = Vector256.GreaterThanOrEqual(u, zero) & Vector256.LessThanOrEqual(u, one);

        // q = cross(tv, e1)
        var qx = tvy * e1z - tvz * e1y;
        var qy = tvz * e1x - tvx * e1z;
        var qz = tvx * e1y - tvy * e1x;

        // v = dot(rd, q) * invDet, must be >= 0 and u+v <= 1
        var v = (rdX * qx + rdY * qy + rdZ * qz) * invDet;
        var validV = Vector256.GreaterThanOrEqual(v, zero) & Vector256.LessThanOrEqual(u + v, one);

        // t = dot(e2, q) * invDet, must be in (tMin, maxT)
        var tt = (e2x * qx + e2y * qy + e2z * qz) * invDet;
        var validT = Vector256.GreaterThan(tt, tMinV) & Vector256.LessThan(tt, tMaxV);

        var hit = validDet & validU & validV & validT;
        hitMask = Vector256.ExtractMostSignificantBits(hit);
        tOut = tt; uOut = u; vOut = v;
    }

    /// <summary>
    /// Closest-hit query: SIMD ray-vs-4-AABB at each node, sort hit children by entry distance,
    /// push far-to-near. Leaves run one SIMD ray-vs-4-triangle per 4-pack.
    /// </summary>
    public bool ClosestHit(Float3 ro, Float3 rd, float tMin, float maxT,
                           out float t, out float u, out float v, out int triIndex,
                           bool cullEnabled = false, bool keepPositive = false)
    {
        t = maxT; u = v = 0f; triIndex = -1;
        if (Nodes4.Length == 0) return false;

        // Vector128 broadcasts for the BVH4 inner-node AABB test (4 children per node).
        var roX  = Vector128.Create(ro.X);
        var roY  = Vector128.Create(ro.Y);
        var roZ  = Vector128.Create(ro.Z);
        var invX = Vector128.Create(1f / SafeNonZero(rd.X));
        var invY = Vector128.Create(1f / SafeNonZero(rd.Y));
        var invZ = Vector128.Create(1f / SafeNonZero(rd.Z));
        var tMinV = Vector128.Create(tMin);

        // Vector256 broadcasts for the 8-wide leaf ray-triangle test.
        var roX8  = Vector256.Create(ro.X);
        var roY8  = Vector256.Create(ro.Y);
        var roZ8  = Vector256.Create(ro.Z);
        var rdX8  = Vector256.Create(rd.X);
        var rdY8  = Vector256.Create(rd.Y);
        var rdZ8  = Vector256.Create(rd.Z);
        var tMinV8 = Vector256.Create(tMin);

        // Stack stores (nodeIdx, entryDistance). On pop we skip any node whose recorded entry
        // is already past the current best hit: a closer hit in another subtree pruned it.
        System.Span<(int idx, float tNear)> stack = stackalloc (int, float)[64];
        System.Span<float> enterArr = stackalloc float[4];
        System.Span<int> order = stackalloc int[4];
        System.Span<float> tLanes = stackalloc float[SimdWidth];
        System.Span<float> uLanes = stackalloc float[SimdWidth];
        System.Span<float> vLanes = stackalloc float[SimdWidth];
        int sp = 0;
        stack[sp++] = (0, tMin);
        bool hit = false;

        while (sp > 0)
        {
            var top = stack[--sp];
            if (top.tNear >= t) continue;   // pruned by a closer hit
            int ni = top.idx;
            ref var n = ref Nodes4[ni];

            // SIMD ray-vs-4-AABB
            var t1x = (n.MinX - roX) * invX;
            var t2x = (n.MaxX - roX) * invX;
            var t1y = (n.MinY - roY) * invY;
            var t2y = (n.MaxY - roY) * invY;
            var t1z = (n.MinZ - roZ) * invZ;
            var t2z = (n.MaxZ - roZ) * invZ;
            var enter = Vector128.Max(Vector128.Max(Vector128.Min(t1x, t2x), Vector128.Min(t1y, t2y)),
                                       Vector128.Max(Vector128.Min(t1z, t2z), tMinV));
            var exit  = Vector128.Min(Vector128.Min(Vector128.Max(t1x, t2x), Vector128.Max(t1y, t2y)),
                                       Vector128.Min(Vector128.Max(t1z, t2z), Vector128.Create(t)));
            var hitV  = Vector128.LessThanOrEqual(enter, exit);
            uint mask = Vector128.ExtractMostSignificantBits(hitV);
            if (mask == 0) continue;

            enter.CopyTo(enterArr);
            int hitCount = 0;
            for (int i = 0; i < n.Valid; i++)
            {
                if ((mask & (1u << i)) == 0) continue;
                int j = hitCount;
                while (j > 0 && enterArr[order[j - 1]] > enterArr[i]) { order[j] = order[j - 1]; j--; }
                order[j] = i;
                hitCount++;
            }

            // Visit children in stack order (push far -> near so near pops first).
            for (int k = hitCount - 1; k >= 0; k--)
            {
                int slot = order[k];
                int cIdx = n.GetChild(slot);
                int pCnt = n.GetPrim(slot);
                if (pCnt > 0)
                {
                    // Leaf: walk each SIMD-wide pack with one ray-triangle test.
                    var tMaxV8 = Vector256.Create(t);
                    for (int packStart = cIdx; packStart < cIdx + pCnt; packStart += SimdWidth)
                    {
                        RayTri8(packStart, roX8, roY8, roZ8, rdX8, rdY8, rdZ8, tMinV8, tMaxV8,
                                cullEnabled, keepPositive,
                                out var tt, out var uu, out var vv, out uint tHitMask);
                        if (tHitMask == 0) continue;
                        tt.CopyTo(tLanes);
                        uu.CopyTo(uLanes);
                        vv.CopyTo(vLanes);
                        for (int lane = 0; lane < SimdWidth; lane++)
                        {
                            if ((tHitMask & (1u << lane)) == 0) continue;
                            if (tLanes[lane] < t)
                            {
                                t = tLanes[lane];
                                u = uLanes[lane];
                                v = vLanes[lane];
                                triIndex = packStart + lane;
                                hit = true;
                                tMaxV8 = Vector256.Create(t);
                            }
                        }
                    }
                }
                else
                {
                    if (sp < stack.Length) stack[sp++] = (cIdx, enterArr[slot]);
                }
            }
        }
        return hit;
    }

    /// <summary>Any-hit: return on the first triangle hit closer than <paramref name="maxT"/>.</summary>
    public bool AnyHit(Float3 ro, Float3 rd, float tMin, float maxT,
                       bool cullEnabled = false, bool keepPositive = false)
    {
        if (Nodes4.Length == 0) return false;
        // Vector128 broadcasts for inner-node BVH4 AABB tests.
        var roX  = Vector128.Create(ro.X);
        var roY  = Vector128.Create(ro.Y);
        var roZ  = Vector128.Create(ro.Z);
        var invX = Vector128.Create(1f / SafeNonZero(rd.X));
        var invY = Vector128.Create(1f / SafeNonZero(rd.Y));
        var invZ = Vector128.Create(1f / SafeNonZero(rd.Z));
        var tMinV = Vector128.Create(tMin);
        var tMaxV = Vector128.Create(maxT);
        // Vector256 broadcasts for the 8-wide leaf test.
        var roX8 = Vector256.Create(ro.X);
        var roY8 = Vector256.Create(ro.Y);
        var roZ8 = Vector256.Create(ro.Z);
        var rdX8 = Vector256.Create(rd.X);
        var rdY8 = Vector256.Create(rd.Y);
        var rdZ8 = Vector256.Create(rd.Z);
        var tMinV8 = Vector256.Create(tMin);
        var tMaxV8 = Vector256.Create(maxT);

        System.Span<int> stack = stackalloc int[64];
        int sp = 0;
        stack[sp++] = 0;

        while (sp > 0)
        {
            int ni = stack[--sp];
            ref var n = ref Nodes4[ni];

            var t1x = (n.MinX - roX) * invX;
            var t2x = (n.MaxX - roX) * invX;
            var t1y = (n.MinY - roY) * invY;
            var t2y = (n.MaxY - roY) * invY;
            var t1z = (n.MinZ - roZ) * invZ;
            var t2z = (n.MaxZ - roZ) * invZ;
            var enter = Vector128.Max(Vector128.Max(Vector128.Min(t1x, t2x), Vector128.Min(t1y, t2y)),
                                       Vector128.Max(Vector128.Min(t1z, t2z), tMinV));
            var exit  = Vector128.Min(Vector128.Min(Vector128.Max(t1x, t2x), Vector128.Max(t1y, t2y)),
                                       Vector128.Min(Vector128.Max(t1z, t2z), tMaxV));
            var hitV  = Vector128.LessThanOrEqual(enter, exit);
            uint mask = Vector128.ExtractMostSignificantBits(hitV);
            if (mask == 0) continue;

            for (int i = 0; i < n.Valid; i++)
            {
                if ((mask & (1u << i)) == 0) continue;
                int cIdx = n.GetChild(i);
                int pCnt = n.GetPrim(i);
                if (pCnt > 0)
                {
                    for (int packStart = cIdx; packStart < cIdx + pCnt; packStart += SimdWidth)
                    {
                        RayTri8(packStart, roX8, roY8, roZ8, rdX8, rdY8, rdZ8, tMinV8, tMaxV8,
                                cullEnabled, keepPositive,
                                out _, out _, out _, out uint tHitMask);
                        if (tHitMask != 0) return true;
                    }
                }
                else
                {
                    if (sp < stack.Length) stack[sp++] = cIdx;
                }
            }
        }
        return false;
    }
}
