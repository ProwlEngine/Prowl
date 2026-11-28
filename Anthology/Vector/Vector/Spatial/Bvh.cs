// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector.Geometry;

namespace Prowl.Vector.Spatial;

/// <summary>How a <see cref="Bvh{T}"/> trades build speed against query quality.</summary>
public enum BvhBuildQuality
{
    /// <summary>Median split on the largest axis. Fastest to build, lowest query quality.</summary>
    Fast,
    /// <summary>Binned surface-area heuristic with a modest bin count. A good default.</summary>
    Balanced,
    /// <summary>Binned surface-area heuristic with more bins. Slowest build, best queries.</summary>
    High
}

/// <summary>Tuning for <see cref="Bvh{T}"/> construction.</summary>
public struct BvhBuildSettings
{
    /// <summary>Split strategy / build vs query trade-off.</summary>
    public BvhBuildQuality Quality;
    /// <summary>Maximum primitives stored in a leaf node.</summary>
    public int MaxLeafSize;
    /// <summary>Number of bins used by the surface-area heuristic (ignored for <see cref="BvhBuildQuality.Fast"/>).</summary>
    public int SahBinCount;

    /// <summary>The default settings: <see cref="BvhBuildQuality.Balanced"/>, leaf size 4, 12 bins.</summary>
    public static BvhBuildSettings Default => new BvhBuildSettings { Quality = BvhBuildQuality.Balanced, MaxLeafSize = 4, SahBinCount = 12 };

    // Fills in sensible values for a zero-initialized struct (e.g. when callers pass `default`).
    internal BvhBuildSettings Resolve()
    {
        if (MaxLeafSize <= 0)
            return Default;
        BvhBuildSettings s = this;
        if (s.SahBinCount < 2) s.SahBinCount = 12;
        return s;
    }
}

/// <summary>
/// A generic bounding-volume hierarchy over items with axis-aligned bounds. Useful as a broadphase
/// for overlap queries and as an acceleration structure for ray casts. The tree is built once from a
/// snapshot of the items; rebuild it when the set or their bounds change.
/// </summary>
/// <typeparam name="T">The item type stored in the hierarchy.</typeparam>
public sealed class Bvh<T>
{
    private struct Node
    {
        public AABB Box;
        public int Start;  // leaf: first index into _order
        public int Count;  // >0 leaf; 0 internal
        public int Left;   // internal: child node indices
        public int Right;
    }

    private readonly T[] _items;
    private readonly AABB[] _bounds;
    private readonly int[] _order;
    private readonly Node[] _nodes;
    private int _nodeCount;
    private readonly BvhBuildSettings _settings;

    /// <summary>Number of items in the hierarchy.</summary>
    public int Count => _items.Length;

    /// <summary>Bounds of the whole hierarchy (empty if it has no items).</summary>
    public AABB Bounds => _nodeCount > 0 ? _nodes[0].Box : new AABB();

    /// <summary>Builds a hierarchy over <paramref name="items"/>, taking each item's bounds via <paramref name="bounds"/>.</summary>
    public Bvh(IReadOnlyList<T> items, Func<T, AABB> bounds, BvhBuildSettings settings = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(bounds);

        _settings = settings.Resolve();

        int n = items.Count;
        _items = new T[n];
        _bounds = new AABB[n];
        _order = new int[n];
        _centers = new Float3[n];
        for (int i = 0; i < n; i++)
        {
            _items[i] = items[i];
            _bounds[i] = bounds(items[i]);
            _centers[i] = _bounds[i].Center;
            _order[i] = i;
        }

        _nodes = new Node[Math.Max(1, 2 * n)];
        _nodeCount = 0;
        if (n > 0)
            Build(0, n);

        // Only the build reads these
        _centers = Array.Empty<Float3>();
        _binCount = null;
        _binBox = null;
        _binUsed = null;
        _leftCount = null;
        _leftArea = null;
        _rightArea = null;
    }

    #region Build

    // Centres of the item bounds, and the bins of the surface area split, kept across the build's nodes so none of it
    // is worked out or allocated again for every node
    private Float3[] _centers;
    private int[]? _binCount, _leftCount;
    private AABB[]? _binBox;
    private bool[]? _binUsed;
    private float[]? _leftArea, _rightArea;

    private int Build(int start, int count)
    {
        int nodeIndex = _nodeCount++;
        Node node = default;
        node.Box = RangeBounds(start, count);

        int leafSize = _settings.MaxLeafSize;
        if (count <= leafSize)
        {
            node.Count = count;
            node.Start = start;
            _nodes[nodeIndex] = node;
            return nodeIndex;
        }

        int mid = _settings.Quality == BvhBuildQuality.Fast
            ? MedianSplit(start, count)
            : SahSplit(start, count);

        if (mid <= start || mid >= start + count)
            mid = start + count / 2; // degenerate split: fall back to an even median by index

        node.Count = 0;
        int left = Build(start, mid - start);
        int right = Build(mid, start + count - mid);
        node.Left = left;
        node.Right = right;
        _nodes[nodeIndex] = node;
        return nodeIndex;
    }

    private AABB RangeBounds(int start, int count)
    {
        AABB box = _bounds[_order[start]];
        for (int k = 1; k < count; k++)
            box = box.Encapsulating(_bounds[_order[start + k]]);
        return box;
    }

    private int LargestCentroidAxis(int start, int count, out float min, out float extent)
    {
        Float3 cmin = _centers[_order[start]];
        Float3 cmax = cmin;
        for (int k = 1; k < count; k++)
        {
            Float3 c = _centers[_order[start + k]];
            cmin = Maths.Min(cmin, c);
            cmax = Maths.Max(cmax, c);
        }
        Float3 size = cmax - cmin;
        int axis = size.X >= size.Y ? (size.X >= size.Z ? 0 : 2) : (size.Y >= size.Z ? 1 : 2);
        min = cmin[axis];
        extent = size[axis];
        return axis;
    }

    private int MedianSplit(int start, int count)
    {
        int axis = LargestCentroidAxis(start, count, out _, out float extent);
        if (extent < 1e-12f) return start + count / 2;
        SelectMedian(start, count, axis);
        return start + count / 2;
    }

    private int SahSplit(int start, int count)
    {
        int axis = LargestCentroidAxis(start, count, out float min, out float extent);
        if (extent < 1e-12f) return start + count / 2;

        int bins = _settings.Quality == BvhBuildQuality.High ? Math.Max(_settings.SahBinCount, 16) : _settings.SahBinCount;
        if (_binCount == null || _binCount.Length != bins)
        {
            _binCount = new int[bins];
            _binBox = new AABB[bins];
            _binUsed = new bool[bins];
            _leftCount = new int[bins];
            _leftArea = new float[bins];
            _rightArea = new float[bins];
        }
        int[] binCount = _binCount, leftCount = _leftCount!;
        AABB[] binBox = _binBox!;
        bool[] binUsed = _binUsed!;
        float[] leftArea = _leftArea!, rightArea = _rightArea!;
        Array.Clear(binCount, 0, bins);
        Array.Clear(binUsed, 0, bins);

        float scale = bins / extent;
        for (int k = 0; k < count; k++)
        {
            int item = _order[start + k];
            AABB b = _bounds[item];
            int bi = (int)((_centers[item][axis] - min) * scale);
            if (bi < 0) bi = 0; else if (bi >= bins) bi = bins - 1;

            binBox[bi] = binUsed[bi] ? binBox[bi].Encapsulating(b) : b;
            binUsed[bi] = true;
            binCount[bi]++;
        }

        // Sweep from the left and right to score every split plane between bins.
        AABB acc = default; bool accUsed = false; int accCount = 0;
        for (int i = 0; i < bins; i++)
        {
            if (binUsed[i]) { acc = accUsed ? acc.Encapsulating(binBox[i]) : binBox[i]; accUsed = true; }
            accCount += binCount[i];
            leftCount[i] = accCount;
            leftArea[i] = accUsed ? acc.SurfaceArea : 0f;
        }

        accUsed = false;
        for (int i = bins - 1; i >= 0; i--)
        {
            if (binUsed[i]) { acc = accUsed ? acc.Encapsulating(binBox[i]) : binBox[i]; accUsed = true; }
            rightArea[i] = accUsed ? acc.SurfaceArea : 0f;
        }

        float bestCost = float.PositiveInfinity;
        int bestBin = -1;
        for (int i = 0; i < bins - 1; i++)
        {
            int lc = leftCount[i];
            int rc = count - lc;
            if (lc == 0 || rc == 0) continue;
            float cost = lc * leftArea[i] + rc * rightArea[i + 1];
            if (cost < bestCost) { bestCost = cost; bestBin = i; }
        }

        if (bestBin < 0)
        {
            SelectMedian(start, count, axis);
            return start + count / 2;
        }

        // Partition in place: bins <= bestBin go left.
        int lo = start, hi = start + count - 1;
        while (lo <= hi)
        {
            int bi = (int)((_centers[_order[lo]][axis] - min) * scale);
            if (bi < 0) bi = 0; else if (bi >= bins) bi = bins - 1;
            if (bi <= bestBin) lo++;
            else { (_order[lo], _order[hi]) = (_order[hi], _order[lo]); hi--; }
        }
        return lo;
    }

    // Puts the items of the lower half of the range, by centre along the axis, before those of the upper half.
    // Neither half is sorted, which is all a split needs and takes time in step with the range, not more.
    private void SelectMedian(int start, int count, int axis)
    {
        int lo = start, hi = start + count - 1, target = start + count / 2;
        while (lo < hi)
        {
            float pivot = _centers[_order[lo + (hi - lo) / 2]][axis];
            int i = lo, j = hi;
            while (i <= j)
            {
                while (_centers[_order[i]][axis] < pivot) i++;
                while (_centers[_order[j]][axis] > pivot) j--;
                if (i > j) break;
                (_order[i], _order[j]) = (_order[j], _order[i]);
                i++;
                j--;
            }
            if (target <= j) hi = j;
            else if (target >= i) lo = i;
            else break;
        }
    }

    #endregion

    #region Overlap queries

    /// <summary>Appends every item whose bounds overlap <paramref name="box"/> to <paramref name="results"/>.</summary>
    public void QueryAABB(AABB box, List<T> results)
    {
        QueryAABB(box, results.Add);
    }

    /// <summary>Invokes <paramref name="visit"/> for every item whose bounds overlap <paramref name="box"/>.</summary>
    public void QueryAABB(AABB box, Action<T> visit)
    {
        if (_nodeCount == 0) return;

        Span<int> stack = stackalloc int[64];
        int sp = 0;
        stack[sp++] = 0;
        while (sp > 0)
        {
            Node node = _nodes[stack[--sp]];
            if (!node.Box.Intersects(box)) continue;

            if (node.Count > 0)
            {
                for (int k = 0; k < node.Count; k++)
                {
                    int item = _order[node.Start + k];
                    if (box.Intersects(_bounds[item]))
                        visit(_items[item]);
                }
            }
            else if (sp + 2 > stack.Length) // extremely deep tree guard
            {
                QueryAABBRecursive(node.Left, box, visit);
                QueryAABBRecursive(node.Right, box, visit);
            }
            else
            {
                stack[sp++] = node.Left;
                stack[sp++] = node.Right;
            }
        }
    }

    private void QueryAABBRecursive(int nodeIndex, AABB box, Action<T> visit)
    {
        Node node = _nodes[nodeIndex];
        if (!node.Box.Intersects(box)) return;
        if (node.Count > 0)
        {
            for (int k = 0; k < node.Count; k++)
            {
                int item = _order[node.Start + k];
                if (box.Intersects(_bounds[item]))
                    visit(_items[item]);
            }
            return;
        }
        QueryAABBRecursive(node.Left, box, visit);
        QueryAABBRecursive(node.Right, box, visit);
    }

    /// <summary>Appends every item whose bounds contain <paramref name="point"/> to <paramref name="results"/>.</summary>
    public void QueryPoint(Float3 point, List<T> results)
    {
        QueryAABB(new AABB(point, point), results);
    }

    #endregion

    #region Ray queries

    /// <summary>
    /// Finds the closest item hit by a ray. The hierarchy narrows the search to candidate items;
    /// <paramref name="intersect"/> performs the exact item test and returns the hit distance (or a
    /// negative value for a miss).
    /// </summary>
    /// <returns>True if any item was hit within <paramref name="maxDistance"/>.</returns>
    public bool Raycast(Ray ray, Func<T, Ray, float> intersect, out T hit, out float distance, float maxDistance = float.PositiveInfinity)
    {
        hit = default!;
        distance = maxDistance;
        bool found = false;
        if (_nodeCount == 0) return false;

        Span<int> stack = stackalloc int[64];
        int sp = 0;
        stack[sp++] = 0;
        while (sp > 0)
        {
            Node node = _nodes[stack[--sp]];
            if (!Intersection.RayAABB(ray.Origin, ray.Direction, node.Box.Min, node.Box.Max, out float tMin, out float tMax))
                continue;
            if (tMax < 0 || tMin > distance)
                continue;

            if (node.Count > 0)
            {
                for (int k = 0; k < node.Count; k++)
                {
                    T item = _items[_order[node.Start + k]];
                    float d = intersect(item, ray);
                    if (d >= 0 && d < distance)
                    {
                        distance = d;
                        hit = item;
                        found = true;
                    }
                }
            }
            else if (sp + 2 <= stack.Length)
            {
                stack[sp++] = node.Left;
                stack[sp++] = node.Right;
            }
        }

        if (!found) distance = 0;
        return found;
    }

    /// <summary>Invokes <paramref name="visit"/> for every item whose bounds the ray enters within <paramref name="maxDistance"/>.</summary>
    public void RaycastCandidates(Ray ray, Action<T> visit, float maxDistance = float.PositiveInfinity)
    {
        if (_nodeCount == 0) return;

        Span<int> stack = stackalloc int[64];
        int sp = 0;
        stack[sp++] = 0;
        while (sp > 0)
        {
            Node node = _nodes[stack[--sp]];
            if (!Intersection.RayAABB(ray.Origin, ray.Direction, node.Box.Min, node.Box.Max, out float tMin, out float tMax))
                continue;
            if (tMax < 0 || tMin > maxDistance)
                continue;

            if (node.Count > 0)
            {
                for (int k = 0; k < node.Count; k++)
                {
                    int item = _order[node.Start + k];
                    if (Intersection.RayAABB(ray.Origin, ray.Direction, _bounds[item].Min, _bounds[item].Max, out float it0, out float it1) && it1 >= 0 && it0 <= maxDistance)
                        visit(_items[item]);
                }
            }
            else if (sp + 2 <= stack.Length)
            {
                stack[sp++] = node.Left;
                stack[sp++] = node.Right;
            }
        }
    }

    #endregion
}
