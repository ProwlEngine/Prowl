using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Vector;

// Box math runs on every light at every level of the build, and the system vector type is the one the JIT turns
// into SIMD min and max
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

namespace Prowl.Graphite.Rendering;

internal struct LightBounds
{
    public Vector3 Min;
    public Vector3 Max;

    public static LightBounds Empty => new() { Min = new Vector3(float.MaxValue), Max = new Vector3(float.MinValue) };

    public void Add(Vector3 point)
    {
        Min = Vector3.Min(Min, point);
        Max = Vector3.Max(Max, point);
    }

    public void Add(in LightBounds other)
    {
        Min = Vector3.Min(Min, other.Min);
        Max = Vector3.Max(Max, other.Max);
    }

    // A point query enters a node with probability proportional to its volume, so splits are ranked by volume
    // rather than the surface area a ray tracer wants
    public readonly float Volume
    {
        get
        {
            Vector3 e = Max - Min;
            if (e.X < 0f) return 0f;
            return (e.X + 1e-4f) * (e.Y + 1e-4f) * (e.Z + 1e-4f);
        }
    }

    public readonly bool Contains(Vector3 p) =>
        p.X >= Min.X && p.Y >= Min.Y && p.Z >= Min.Z && p.X <= Max.X && p.Y <= Max.Y && p.Z <= Max.Z;
}

/// <summary>
/// A four wide BVH node as the shaders read it, eight vec4s. The six bound planes are stored one component per child,
/// so a fragment tests all four boxes at once with vector math. A child is a node index, or a contiguous run of
/// lights flagged with <see cref="LightTreeBuilder.LeafBit"/> whose length is in the matching count.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct LightTreeNode
{
    public Vector4 MinX;
    public Vector4 MinY;
    public Vector4 MinZ;
    public Vector4 MaxX;
    public Vector4 MaxY;
    public Vector4 MaxZ;
    public uint Child0, Child1, Child2, Child3;
    public uint Count0, Count1, Count2, Count3;

    public const int Vec4Count = 8;

    public readonly uint Child(int i) => i switch { 0 => Child0, 1 => Child1, 2 => Child2, _ => Child3 };
    public readonly uint Count(int i) => i switch { 0 => Count0, 1 => Count1, 2 => Count2, _ => Count3 };

    public readonly LightBounds Box(int i) => new()
    {
        Min = new Vector3(MinX[i], MinY[i], MinZ[i]),
        Max = new Vector3(MaxX[i], MaxY[i], MaxZ[i]),
    };
}

/// <summary>
/// Builds the four wide light trees of H-Forward shading: a binary tree collapsed four wide, with the lights reordered
/// so every leaf is a contiguous run. Static lights use a binned volume heuristic search over every axis, built once.
/// Moving lights are rebuilt every frame, so they sort by Morton code once and then find each split by binary search,
/// which needs no walk over the lights at all. Scratch storage is kept, so a rebuild allocates nothing.
/// </summary>
internal sealed class LightTreeBuilder
{
    public const uint LeafBit = 0x80000000u;
    public const uint Invalid = 0xFFFFFFFFu;

    /// <summary>Entries in the shader's traversal stack. Four wide, so each level defers at most three siblings.</summary>
    public const int ShaderStack = 48;

    private const int Bins = 16;
    private const int ParallelThreshold = 2_048;
    private const int WideNodeThreshold = 131_072;
    private const int BfsThreshold = 131_072;
    private const int MaxBinaryDepth = 30;

    private struct BinaryNode
    {
        public LightBounds Bounds;
        public int LeftFirst;
        public int Count;
    }

    private struct Split
    {
        public int Axis;
        public int Bin;
        public float Min;
        public float Scale;
        public LightBounds Left;
        public LightBounds Right;
        public LightBounds LeftCentroids;
        public LightBounds RightCentroids;
        public float Cost;
    }

    private BinaryNode[] _nodes = [];
    private int[] _indices = [];
    private int[] _scratch = [];
    private Vector3[] _centroids = [];
    private LightBounds[] _primBounds = [];
    private LightTreeNode[] _wide = [];
    private uint[] _codes = [];
    private uint[] _codesScratch = [];

    private int _nodeCount;
    private int _wideCount;
    private int _maxLeafSize;
    private int _depth;
    private int _parallelDepth;

    public ReadOnlySpan<LightTreeNode> Nodes => _wide.AsSpan(0, _wideCount);
    public int NodeCount => _wideCount;

    /// <summary>Light indices in tree order. A leaf covering [first, first + count) holds Order[first..].</summary>
    public ReadOnlySpan<int> Order => _indices.AsSpan(0, PrimitiveCount);

    public int PrimitiveCount { get; private set; }

    /// <summary>Depth of the four wide tree, which sets the shader stack it needs.</summary>
    public int Depth { get; private set; }

    /// <summary>
    /// Builds over lights given as sphere center and radius. <paramref name="morton"/> picks the per frame Morton
    /// build, otherwise the binned search over every axis that a tree built once can afford.
    /// </summary>
    public void Build(ReadOnlySpan<Vector4> spheres, int maxLeafSize, bool morton)
    {
        int count = spheres.Length;
        PrimitiveCount = count;
        _maxLeafSize = Math.Max(1, maxLeafSize);
        _wideCount = 0;
        _depth = 0;
        Depth = 0;
        _parallelDepth = (int)Math.Ceiling(Math.Log2(Math.Max(Environment.ProcessorCount, 2))) + 2;
        if (count == 0) return;

        EnsureCapacity(count);

        LightBounds rootBounds = LightBounds.Empty;
        LightBounds rootCentroids = LightBounds.Empty;
        for (int i = 0; i < count; i++)
        {
            Vector4 sphere = spheres[i];
            var center = new Vector3(sphere.X, sphere.Y, sphere.Z);
            var radius = new Vector3(Math.Max(sphere.W, 1e-4f));

            _indices[i] = i;
            _centroids[i] = center;
            _primBounds[i] = new LightBounds { Min = center - radius, Max = center + radius };
            rootBounds.Add(_primBounds[i]);
            rootCentroids.Add(center);
        }

        _nodes[0] = new BinaryNode { Bounds = rootBounds, LeftFirst = 0, Count = count };
        _nodeCount = 1;

        if (morton)
        {
            SortByMorton(count, rootCentroids);
            _nodes[0].Bounds = BuildMorton(0, 0, count, 0);
        }
        else
        {
            BuildHierarchy(rootCentroids);
        }

        BuildWide(0, 1);
        Depth = _depth;
    }

    private void EnsureCapacity(int count)
    {
        if (_indices.Length < count)
        {
            _indices = new int[count];
            _scratch = new int[count];
            _centroids = new Vector3[count];
            _primBounds = new LightBounds[count];
        }

        int maxNodes = 2 * count + 1;
        if (_nodes.Length < maxNodes) _nodes = new BinaryNode[maxNodes];

        int expectedWide = count / 4 + 16;
        if (_wide.Length < expectedWide) _wide = new LightTreeNode[expectedWide];
    }

    // ---------------------------------------------------------------- binned build

    // The top of a big tree is built one level at a time so nothing blocks a pool thread waiting on a child.
    // Recursive fan out leaves every ancestor parked in a wait, and the pool only recovers by injecting threads slowly.
    private void BuildHierarchy(in LightBounds rootCentroids)
    {
        if (_nodes[0].Count < BfsThreshold)
        {
            SubdivideRecursive(0, 0, rootCentroids);
            return;
        }

        var current = new List<(int Node, LightBounds Centroids)> { (0, rootCentroids) };
        var next = new List<(int Node, LightBounds Centroids)>();
        int workers = Environment.ProcessorCount;

        for (int depth = 0; depth < _parallelDepth && current.Count > 0; depth++)
        {
            next.Clear();
            if (current.Count * 4 < workers)
            {
                foreach ((int node, LightBounds centroids) in current)
                    SplitOne(node, centroids, depth, true, next);
            }
            else
            {
                object gate = new();
                int levelDepth = depth;
                Parallel.ForEach(current, item =>
                {
                    var local = new List<(int, LightBounds)>(2);
                    SplitOne(item.Node, item.Centroids, levelDepth, false, local);
                    if (local.Count > 0)
                        lock (gate) next.AddRange(local);
                });
            }
            (current, next) = (next, current);
        }

        Parallel.ForEach(current, item => Subdivide(item.Node, _parallelDepth, item.Centroids));
    }

    private void SplitOne(int nodeIndex, in LightBounds centroidBounds, int depth, bool wide, List<(int, LightBounds)> children)
    {
        if (!TrySplit(nodeIndex, centroidBounds, depth, wide, out int left, out Split split)) return;
        children.Add((left, split.LeftCentroids));
        children.Add((left + 1, split.RightCentroids));
    }

    private void Subdivide(int nodeIndex, int depth, in LightBounds centroidBounds)
    {
        if (!TrySplit(nodeIndex, centroidBounds, depth, false, out int left, out Split split)) return;
        Subdivide(left, depth + 1, split.LeftCentroids);
        Subdivide(left + 1, depth + 1, split.RightCentroids);
    }

    private void SubdivideRecursive(int nodeIndex, int depth, in LightBounds centroidBounds)
    {
        int count = _nodes[nodeIndex].Count;
        if (!TrySplit(nodeIndex, centroidBounds, depth, false, out int left, out Split split)) return;

        if (count > ParallelThreshold)
        {
            LightBounds leftCopy = split.LeftCentroids;
            LightBounds rightCopy = split.RightCentroids;
            Parallel.Invoke(
                () => SubdivideRecursive(left, depth + 1, leftCopy),
                () => SubdivideRecursive(left + 1, depth + 1, rightCopy));
        }
        else
        {
            SubdivideRecursive(left, depth + 1, split.LeftCentroids);
            SubdivideRecursive(left + 1, depth + 1, split.RightCentroids);
        }
    }

    private bool TrySplit(int nodeIndex, in LightBounds centroidBounds, int depth, bool wide, out int left, out Split split)
    {
        left = 0;
        split = default;

        int count = _nodes[nodeIndex].Count;
        if (count <= _maxLeafSize || depth >= MaxBinaryDepth) return false;

        wide = wide && count >= WideNodeThreshold;
        int first = _nodes[nodeIndex].LeftFirst;

        if (!FindSplit(first, count, centroidBounds, wide, out split)) return false;
        if (split.Cost >= _nodes[nodeIndex].Bounds.Volume * count) return false;

        int mid = wide ? PartitionWide(first, count, split) : Partition(first, count, split);
        if (mid == first || mid == first + count) return false;

        left = Interlocked.Add(ref _nodeCount, 2) - 2;
        _nodes[left] = new BinaryNode { Bounds = split.Left, LeftFirst = first, Count = mid - first };
        _nodes[left + 1] = new BinaryNode { Bounds = split.Right, LeftFirst = mid, Count = first + count - mid };
        _nodes[nodeIndex].LeftFirst = left;
        _nodes[nodeIndex].Count = 0;
        return true;
    }

    private bool FindSplit(int first, int count, in LightBounds centroidBounds, bool wide, out Split best)
    {
        best = default;
        best.Axis = -1;
        best.Cost = float.MaxValue;

        Span<LightBounds> binBounds = stackalloc LightBounds[Bins];
        Span<int> binCounts = stackalloc int[Bins];
        Span<LightBounds> binCentroids = stackalloc LightBounds[Bins];
        Span<LightBounds> suffixBounds = stackalloc LightBounds[Bins];
        Span<LightBounds> suffixCentroids = stackalloc LightBounds[Bins];
        Span<int> suffixCount = stackalloc int[Bins];

        Vector3 extent = centroidBounds.Max - centroidBounds.Min;
        for (int axis = 0; axis <= 2; axis++)
        {
            float min = Component(centroidBounds.Min, axis);
            float span = Component(extent, axis);
            if (span < 1e-6f) continue;

            float scale = Bins / span;
            Bin(first, count, axis, min, scale, wide, binBounds, binCounts, binCentroids);

            LightBounds right = LightBounds.Empty;
            LightBounds rightCentroid = LightBounds.Empty;
            int rightCount = 0;
            for (int b = Bins - 1; b > 0; b--)
            {
                right.Add(binBounds[b]);
                rightCentroid.Add(binCentroids[b]);
                rightCount += binCounts[b];
                suffixBounds[b] = right;
                suffixCentroids[b] = rightCentroid;
                suffixCount[b] = rightCount;
            }

            LightBounds left = LightBounds.Empty;
            LightBounds leftCentroid = LightBounds.Empty;
            int leftCount = 0;
            for (int b = 0; b < Bins - 1; b++)
            {
                left.Add(binBounds[b]);
                leftCentroid.Add(binCentroids[b]);
                leftCount += binCounts[b];
                if (leftCount == 0 || suffixCount[b + 1] == 0) continue;

                float cost = left.Volume * leftCount + suffixBounds[b + 1].Volume * suffixCount[b + 1];
                if (cost >= best.Cost) continue;

                best.Cost = cost;
                best.Axis = axis;
                best.Bin = b;
                best.Min = min;
                best.Scale = scale;
                best.Left = left;
                best.Right = suffixBounds[b + 1];
                best.LeftCentroids = leftCentroid;
                best.RightCentroids = suffixCentroids[b + 1];
            }
        }

        return best.Axis >= 0;
    }

    // Binning by centroid means the child centroid bounds are the union of the bins on each side, so the partition
    // never has to accumulate them
    private void Bin(int first, int count, int axis, float min, float scale, bool wide,
        Span<LightBounds> binBounds, Span<int> binCounts, Span<LightBounds> binCentroids)
    {
        for (int b = 0; b < Bins; b++)
        {
            binBounds[b] = LightBounds.Empty;
            binCentroids[b] = LightBounds.Empty;
            binCounts[b] = 0;
        }

        if (!wide)
        {
            for (int i = first; i < first + count; i++)
            {
                int prim = _indices[i];
                Vector3 centroid = _centroids[prim];
                int bin = BinOf(centroid, axis, min, scale);
                binBounds[bin].Add(_primBounds[prim]);
                binCentroids[bin].Add(centroid);
                binCounts[bin]++;
            }
            return;
        }

        int workers = Environment.ProcessorCount;
        int chunk = (count + workers - 1) / workers;
        var bounds = new LightBounds[Bins];
        var centroids = new LightBounds[Bins];
        int[] counts = new int[Bins];
        for (int b = 0; b < Bins; b++)
        {
            bounds[b] = LightBounds.Empty;
            centroids[b] = LightBounds.Empty;
        }

        object gate = new();
        Parallel.For(0, workers, NewBins, (w, _, local) =>
        {
            int start = first + w * chunk;
            int end = Math.Min(start + chunk, first + count);
            for (int i = start; i < end; i++)
            {
                int prim = _indices[i];
                Vector3 centroid = _centroids[prim];
                int bin = BinOf(centroid, axis, min, scale);
                local.Bounds[bin].Add(_primBounds[prim]);
                local.Centroids[bin].Add(centroid);
                local.Counts[bin]++;
            }
            return local;
        },
        local =>
        {
            lock (gate)
            {
                for (int b = 0; b < Bins; b++)
                {
                    bounds[b].Add(local.Bounds[b]);
                    centroids[b].Add(local.Centroids[b]);
                    counts[b] += local.Counts[b];
                }
            }
        });

        for (int b = 0; b < Bins; b++)
        {
            binBounds[b] = bounds[b];
            binCentroids[b] = centroids[b];
            binCounts[b] = counts[b];
        }
    }

    private static (LightBounds[] Bounds, LightBounds[] Centroids, int[] Counts) NewBins()
    {
        var bounds = new LightBounds[Bins];
        var centroids = new LightBounds[Bins];
        for (int b = 0; b < Bins; b++)
        {
            bounds[b] = LightBounds.Empty;
            centroids[b] = LightBounds.Empty;
        }
        return (bounds, centroids, new int[Bins]);
    }

    // Counting pass, prefix sum, then a scatter, all across the pool
    private int PartitionWide(int first, int count, in Split split)
    {
        int workers = Environment.ProcessorCount;
        int chunk = (count + workers - 1) / workers;
        int axis = split.Axis, bin = split.Bin;
        float min = split.Min, scale = split.Scale;

        int[] leftCounts = new int[workers];
        Parallel.For(0, workers, w =>
        {
            int start = first + w * chunk;
            int end = Math.Min(start + chunk, first + count);
            int left = 0;
            for (int i = start; i < end; i++)
                if (BinOf(_centroids[_indices[i]], axis, min, scale) <= bin) left++;
            leftCounts[w] = left;
        });

        int[] leftOffset = new int[workers];
        int[] rightOffset = new int[workers];
        int total = 0;
        for (int w = 0; w < workers; w++)
        {
            leftOffset[w] = total;
            total += leftCounts[w];
        }

        int running = total;
        for (int w = 0; w < workers; w++)
        {
            int start = first + w * chunk;
            int end = Math.Min(start + chunk, first + count);
            rightOffset[w] = running;
            running += Math.Max(0, end - start - leftCounts[w]);
        }

        Parallel.For(0, workers, w =>
        {
            int start = first + w * chunk;
            int end = Math.Min(start + chunk, first + count);
            int lo = first + leftOffset[w];
            int hi = first + rightOffset[w];
            for (int i = start; i < end; i++)
            {
                int prim = _indices[i];
                if (BinOf(_centroids[prim], axis, min, scale) <= bin) _scratch[lo++] = prim;
                else _scratch[hi++] = prim;
            }
        });

        Array.Copy(_scratch, first, _indices, first, count);
        return first + total;
    }

    // Partitioning by bin index rather than a world position keeps the split consistent with the bins, so the child
    // bounds the search already accumulated stay valid and never need a second pass
    private int Partition(int first, int count, in Split split)
    {
        int low = first;
        int high = first + count - 1;
        while (low <= high)
        {
            if (BinOf(_centroids[_indices[low]], split.Axis, split.Min, split.Scale) <= split.Bin)
            {
                low++;
            }
            else
            {
                (_indices[low], _indices[high]) = (_indices[high], _indices[low]);
                high--;
            }
        }
        return low;
    }

    private static int BinOf(Vector3 centroid, int axis, float min, float scale) =>
        Math.Clamp((int)((Component(centroid, axis) - min) * scale), 0, Bins - 1);

    // ---------------------------------------------------------------- Morton build

    // One scale for all three axes, not one each. Normalising per axis would stretch a thin axis to full range and
    // spend a third of every code's bits splitting it, so on a flat scene a third of the tree would separate nothing.
    private void SortByMorton(int count, in LightBounds centroidBounds)
    {
        if (_codes.Length < count)
        {
            _codes = new uint[count];
            _codesScratch = new uint[count];
        }

        Vector3 extent = centroidBounds.Max - centroidBounds.Min;
        float longest = Math.Max(extent.X, Math.Max(extent.Y, extent.Z));
        var scale = new Vector3(longest > 1e-9f ? 1f / longest : 0f);
        Vector3 origin = centroidBounds.Min;

        for (int i = 0; i < count; i++)
            _codes[i] = Morton3((_centroids[_indices[i]] - origin) * scale);

        RadixSort(count);
    }

    private static uint Morton3(Vector3 unit) => (Spread(Quantise(unit.X)) << 2) | (Spread(Quantise(unit.Y)) << 1) | Spread(Quantise(unit.Z));

    private static uint Quantise(float v) => (uint)Math.Clamp((int)(v * 1024f), 0, 1023);

    // Spreads ten bits out so every third bit is occupied
    private static uint Spread(uint v)
    {
        v = (v * 0x00010001u) & 0xFF0000FFu;
        v = (v * 0x00000101u) & 0x0F00F00Fu;
        v = (v * 0x00000011u) & 0xC30C30C3u;
        v = (v * 0x00000005u) & 0x49249249u;
        return v;
    }

    private void RadixSort(int count)
    {
        const int Bits = 10;
        const int Buckets = 1 << Bits;
        const int Mask = Buckets - 1;

        Span<int> histogram = stackalloc int[Buckets];
        uint[] keys = _codes;
        uint[] keysOut = _codesScratch;
        int[] values = _indices;
        int[] valuesOut = _scratch;

        for (int shift = 0; shift < 30; shift += Bits)
        {
            histogram.Clear();
            for (int i = 0; i < count; i++)
                histogram[(int)((keys[i] >> shift) & Mask)]++;

            int running = 0;
            for (int b = 0; b < Buckets; b++)
            {
                int here = histogram[b];
                histogram[b] = running;
                running += here;
            }

            for (int i = 0; i < count; i++)
            {
                int slot = histogram[(int)((keys[i] >> shift) & Mask)]++;
                keysOut[slot] = keys[i];
                valuesOut[slot] = values[i];
            }

            (keys, keysOut) = (keysOut, keys);
            (values, valuesOut) = (valuesOut, values);
        }

        // Three passes is an odd number of swaps, so the sorted data ended up in the scratch pair
        if (!ReferenceEquals(values, _indices))
        {
            Array.Copy(values, _indices, count);
            Array.Copy(keys, _codes, count);
        }
    }

    // The split is the highest bit at which the range's Morton codes diverge, found by binary search with no walk over
    // the lights. Bounds come back up the recursion so nothing is scanned twice.
    private LightBounds BuildMorton(int nodeIndex, int first, int count, int depth)
    {
        if (count <= _maxLeafSize || depth >= MaxBinaryDepth)
        {
            LightBounds leaf = LightBounds.Empty;
            for (int i = first; i < first + count; i++)
                leaf.Add(_primBounds[_indices[i]]);
            _nodes[nodeIndex] = new BinaryNode { Bounds = leaf, LeftFirst = first, Count = count };
            return leaf;
        }

        int last = first + count - 1;
        int split = FindMortonSplit(first, last);
        int leftCount = split - first + 1;
        int left = Interlocked.Add(ref _nodeCount, 2) - 2;

        LightBounds leftBounds = default;
        LightBounds rightBounds = default;
        if (count > ParallelThreshold && depth < _parallelDepth)
        {
            Parallel.Invoke(
                () => leftBounds = BuildMorton(left, first, leftCount, depth + 1),
                () => rightBounds = BuildMorton(left + 1, split + 1, count - leftCount, depth + 1));
        }
        else
        {
            leftBounds = BuildMorton(left, first, leftCount, depth + 1);
            rightBounds = BuildMorton(left + 1, split + 1, count - leftCount, depth + 1);
        }

        LightBounds bounds = leftBounds;
        bounds.Add(rightBounds);
        _nodes[nodeIndex] = new BinaryNode { Bounds = bounds, LeftFirst = left, Count = 0 };
        return bounds;
    }

    private int FindMortonSplit(int first, int last)
    {
        uint firstCode = _codes[first];
        uint lastCode = _codes[last];

        // A run of identical codes has no bit to split on, so it is halved to keep the tree balanced
        if (firstCode == lastCode) return (first + last) >> 1;

        int common = System.Numerics.BitOperations.LeadingZeroCount(firstCode ^ lastCode);
        int split = first;
        int step = last - first;
        do
        {
            step = (step + 1) >> 1;
            int candidate = split + step;
            if (candidate < last && System.Numerics.BitOperations.LeadingZeroCount(firstCode ^ _codes[candidate]) > common)
                split = candidate;
        }
        while (step > 1);

        return split;
    }

    // ---------------------------------------------------------------- four wide collapse

    // Opens the largest internal child until there are four, so the volume a point is likely to fall in is split finest
    private int BuildWide(int binaryIndex, int depth)
    {
        if (depth > _depth) _depth = depth;

        Span<int> children = stackalloc int[4];
        int used = 0;
        if (_nodes[binaryIndex].Count > 0)
        {
            children[used++] = binaryIndex;
        }
        else
        {
            children[used++] = _nodes[binaryIndex].LeftFirst;
            children[used++] = _nodes[binaryIndex].LeftFirst + 1;
        }

        while (used < 4)
        {
            int best = -1;
            float bestVolume = -1f;
            for (int i = 0; i < used; i++)
            {
                if (_nodes[children[i]].Count != 0) continue;
                float volume = _nodes[children[i]].Bounds.Volume;
                if (volume > bestVolume)
                {
                    bestVolume = volume;
                    best = i;
                }
            }
            if (best < 0) break;

            int expand = _nodes[children[best]].LeftFirst;
            children[best] = expand;
            children[used++] = expand + 1;
        }

        int self = _wideCount++;
        if (self >= _wide.Length) Array.Resize(ref _wide, _wide.Length * 2);

        Span<float> minX = stackalloc float[4], minY = stackalloc float[4], minZ = stackalloc float[4];
        Span<float> maxX = stackalloc float[4], maxY = stackalloc float[4], maxZ = stackalloc float[4];
        Span<uint> child = stackalloc uint[4];
        Span<uint> counts = stackalloc uint[4];

        for (int i = 0; i < 4; i++)
        {
            if (i >= used)
            {
                minX[i] = minY[i] = minZ[i] = 1e30f;
                maxX[i] = maxY[i] = maxZ[i] = -1e30f;
                child[i] = Invalid;
                counts[i] = 0;
                continue;
            }

            BinaryNode source = _nodes[children[i]];
            minX[i] = source.Bounds.Min.X;
            minY[i] = source.Bounds.Min.Y;
            minZ[i] = source.Bounds.Min.Z;
            maxX[i] = source.Bounds.Max.X;
            maxY[i] = source.Bounds.Max.Y;
            maxZ[i] = source.Bounds.Max.Z;

            if (source.Count > 0)
            {
                child[i] = LeafBit | (uint)source.LeftFirst;
                counts[i] = (uint)source.Count;
            }
            else
            {
                child[i] = (uint)BuildWide(children[i], depth + 1);
                counts[i] = 0;
            }
        }

        _wide[self] = new LightTreeNode
        {
            MinX = new Vector4(minX[0], minX[1], minX[2], minX[3]),
            MinY = new Vector4(minY[0], minY[1], minY[2], minY[3]),
            MinZ = new Vector4(minZ[0], minZ[1], minZ[2], minZ[3]),
            MaxX = new Vector4(maxX[0], maxX[1], maxX[2], maxX[3]),
            MaxY = new Vector4(maxY[0], maxY[1], maxY[2], maxY[3]),
            MaxZ = new Vector4(maxZ[0], maxZ[1], maxZ[2], maxZ[3]),
            Child0 = child[0], Child1 = child[1], Child2 = child[2], Child3 = child[3],
            Count0 = counts[0], Count1 = counts[1], Count2 = counts[2], Count3 = counts[3],
        };
        return self;
    }

    private static float Component(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
}
