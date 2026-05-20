using Prowl.Vector;

namespace Prowl.Photonic.Raytracing;

/// <summary>
/// Top-level acceleration structure. One leaf per <see cref="BakeInstance"/>; each leaf holds an
/// inverse world transform and a pointer into the per-mesh <see cref="Blas"/>. Built once at the
/// start of a bake.
/// </summary>
internal sealed class Tlas
{
    public struct Node
    {
        public AABB Bounds;
        public int LeftFirst;
        public int PrimCount;
    }

    /// <summary>Per-instance entry. Index lines up with <see cref="Instances"/> in <see cref="Job"/>.</summary>
    public struct InstanceRef
    {
        public int InstanceIndex;       // index into Job.Instances
        public int BlasIndex;           // index into Job.Blas
        public Float4x4 WorldFromLocal;
        public Float4x4 LocalFromWorld;
        /// <summary>True if both transforms are identity, allowing the traversal to skip the matrix multiplies.</summary>
        public bool IsIdentity;
    }

    public Node[] Nodes { get; private set; } = System.Array.Empty<Node>();
    public InstanceRef[] Instances { get; private set; } = System.Array.Empty<InstanceRef>();
    private Blas[] _blas = System.Array.Empty<Blas>();

    public void Build(InstanceRef[] instanceRefs, Blas[] blas)
    {
        Instances = instanceRefs;
        _blas = blas;
        int n = instanceRefs.Length;
        var aabbs = new AABB[n];
        var centroids = new Float3[n];
        for (int i = 0; i < n; i++)
        {
            var ir = instanceRefs[i];
            var localBox = blas[ir.BlasIndex].Mesh.Bounds;
            var worldBox = localBox.TransformBy(ir.WorldFromLocal);
            aabbs[i] = worldBox;
            centroids[i] = worldBox.Center;
        }
        var perm = new int[n];
        for (int i = 0; i < n; i++) perm[i] = i;

        var nodes = new System.Collections.Generic.List<Node>(System.Math.Max(1, 2 * n));
        nodes.Add(default);
        Build(nodes, 0, perm, 0, n, aabbs, centroids);

        var reordered = new InstanceRef[n];
        for (int i = 0; i < n; i++) reordered[i] = instanceRefs[perm[i]];
        Instances = reordered;
        Nodes = nodes.ToArray();
    }

    private const int LeafThreshold = 2;

    private static void Build(System.Collections.Generic.List<Node> nodes, int nodeIndex,
                              int[] perm, int first, int count, AABB[] aabbs, Float3[] centroids)
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

        // simple median-of-longest-axis split: instance counts are small, SAH overkill.
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
        if (cext.Z > (axis == 0 ? cext.X : cext.Y)) axis = 2;

        // partial-sort: pick a pivot, partition into smaller/larger halves
        int mid = first + count / 2;
        System.Array.Sort(perm, first, count, System.Collections.Generic.Comparer<int>.Create((a, b) =>
        {
            float ca = axis == 0 ? centroids[a].X : axis == 1 ? centroids[a].Y : centroids[a].Z;
            float cb = axis == 0 ? centroids[b].X : axis == 1 ? centroids[b].Y : centroids[b].Z;
            return ca.CompareTo(cb);
        }));

        int leftIndex = nodes.Count;
        nodes.Add(default);
        nodes.Add(default);
        node.LeftFirst = leftIndex;
        node.PrimCount = 0;
        nodes[nodeIndex] = node;

        Build(nodes, leftIndex,     perm, first, mid - first,           aabbs, centroids);
        Build(nodes, leftIndex + 1, perm, mid,   first + count - mid,    aabbs, centroids);
    }

    public bool ClosestHit(Float3 ro, Float3 rd, float tMin, float maxT, out HitInfo hit)
    {
        hit = default;
        hit.Distance = maxT;
        if (Nodes.Length == 0) return false;
        var invD = new Float3(1f / SafeNonZero(rd.X), 1f / SafeNonZero(rd.Y), 1f / SafeNonZero(rd.Z));
        System.Span<int> stack = stackalloc int[64];
        int sp = 0;
        stack[sp++] = 0;
        bool got = false;
        while (sp > 0)
        {
            int ni = stack[--sp];
            ref var n = ref Nodes[ni];
            if (!RayAabb(ro, invD, n.Bounds, tMin, hit.Distance)) continue;
            if (n.PrimCount > 0)
            {
                int end = n.LeftFirst + n.PrimCount;
                for (int i = n.LeftFirst; i < end; i++)
                {
                    ref var ir = ref Instances[i];
                    Float3 roL, rdL;
                    if (ir.IsIdentity) { roL = ro; rdL = rd; }
                    else { roL = Transform(ir.LocalFromWorld, ro, 1f); rdL = Transform(ir.LocalFromWorld, rd, 0f); }
                    if (_blas[ir.BlasIndex].ClosestHit(roL, rdL, tMin, hit.Distance,
                                                       out float tt, out float uu, out float vv, out int triIdx))
                    {
                        // Under linear transforms t is preserved between local and world space, so we
                        // can take it directly (skipping the world-distance reconstruction).
                        if (tt > tMin && tt < hit.Distance)
                        {
                            hit.Hit = true;
                            hit.Distance = tt;
                            hit.U = uu; hit.V = vv;
                            hit.InstanceIndex = ir.InstanceIndex;
                            hit.TriangleIndex = triIdx;
                            got = true;
                        }
                    }
                }
            }
            else
            {
                if (sp + 2 > stack.Length) continue;
                stack[sp++] = n.LeftFirst;
                stack[sp++] = n.LeftFirst + 1;
            }
        }
        return got;
    }

    public bool AnyHit(Float3 ro, Float3 rd, float tMin, float maxT)
    {
        if (Nodes.Length == 0) return false;
        var invD = new Float3(1f / SafeNonZero(rd.X), 1f / SafeNonZero(rd.Y), 1f / SafeNonZero(rd.Z));
        System.Span<int> stack = stackalloc int[64];
        int sp = 0;
        stack[sp++] = 0;
        while (sp > 0)
        {
            int ni = stack[--sp];
            ref var n = ref Nodes[ni];
            if (!RayAabb(ro, invD, n.Bounds, tMin, maxT)) continue;
            if (n.PrimCount > 0)
            {
                int end = n.LeftFirst + n.PrimCount;
                for (int i = n.LeftFirst; i < end; i++)
                {
                    ref var ir = ref Instances[i];
                    Float3 roL, rdL;
                    if (ir.IsIdentity) { roL = ro; rdL = rd; }
                    else { roL = Transform(ir.LocalFromWorld, ro, 1f); rdL = Transform(ir.LocalFromWorld, rd, 0f); }
                    if (_blas[ir.BlasIndex].AnyHit(roL, rdL, tMin, maxT))
                        return true;
                }
            }
            else
            {
                if (sp + 2 > stack.Length) continue;
                stack[sp++] = n.LeftFirst;
                stack[sp++] = n.LeftFirst + 1;
            }
        }
        return false;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static float SafeNonZero(float v) => System.Math.Abs(v) < 1e-30f ? (v < 0 ? -1e-30f : 1e-30f) : v;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static bool RayAabb(Float3 ro, Float3 invD, AABB box, float tMin, float tMax)
    {
        float t1x = (box.Min.X - ro.X) * invD.X;
        float t2x = (box.Max.X - ro.X) * invD.X;
        float t1y = (box.Min.Y - ro.Y) * invD.Y;
        float t2y = (box.Max.Y - ro.Y) * invD.Y;
        float t1z = (box.Min.Z - ro.Z) * invD.Z;
        float t2z = (box.Max.Z - ro.Z) * invD.Z;
        float tmin = System.Math.Max(System.Math.Max(System.Math.Min(t1x, t2x), System.Math.Min(t1y, t2y)), System.Math.Min(t1z, t2z));
        float tmax = System.Math.Min(System.Math.Min(System.Math.Max(t1x, t2x), System.Math.Max(t1y, t2y)), System.Math.Max(t1z, t2z));
        return tmax >= System.Math.Max(tmin, tMin) && tmin <= tMax;
    }

    /// <summary>Transform a vector with implicit w (1 for points, 0 for directions). Column-major matrix.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static Float3 Transform(Float4x4 m, Float3 v, float w)
    {
        float x = m.c0.X * v.X + m.c1.X * v.Y + m.c2.X * v.Z + m.c3.X * w;
        float y = m.c0.Y * v.X + m.c1.Y * v.Y + m.c2.Y * v.Z + m.c3.Y * w;
        float z = m.c0.Z * v.X + m.c1.Z * v.Y + m.c2.Z * v.Z + m.c3.Z * w;
        return new Float3(x, y, z);
    }
}
