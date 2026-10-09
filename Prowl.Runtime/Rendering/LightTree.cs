// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Vector;
using Prowl.Vector.Geometry;

// Box math runs on every light at every level of the build, and the system vector type is the one the JIT turns
// into SIMD min and max
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

namespace Prowl.Runtime.Rendering;

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

        var rootBounds = LightBounds.Empty;
        var rootCentroids = LightBounds.Empty;
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

            var right = LightBounds.Empty;
            var rightCentroid = LightBounds.Empty;
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

            var left = LightBounds.Empty;
            var leftCentroid = LightBounds.Empty;
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
        var counts = new int[Bins];
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

        var leftCounts = new int[workers];
        Parallel.For(0, workers, w =>
        {
            int start = first + w * chunk;
            int end = Math.Min(start + chunk, first + count);
            int left = 0;
            for (int i = start; i < end; i++)
                if (BinOf(_centroids[_indices[i]], axis, min, scale) <= bin) left++;
            leftCounts[w] = left;
        });

        var leftOffset = new int[workers];
        var rightOffset = new int[workers];
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
            var leaf = LightBounds.Empty;
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

        var bounds = leftBounds;
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

/// <summary>
/// The point and spot lights forward shading reads, kept as two trees. Static lights live in a tree built once and
/// rebuilt only when one is added, removed or moved, with their records in tree order so each leaf is a run. Moving
/// lights are culled to the view and built into a fresh tree every frame, written to the next of a ring of slots.
/// <para/>
/// Both trees share one light table and one node table, so the shader walks either from its root index alone.
/// </summary>
internal sealed class ForwardLightTrees : IDisposable
{
    /// <summary>Texels per light record. Must match <c>LightTree.glsl</c>.</summary>
    public const int TexelsPerLight = 5;

    private const int RingSlots = 3;
    private const int StaticLeafSize = 8;

    private sealed class Entry
    {
        public IRenderableLight Light = null!;
        public ForwardLightData Data;
        public bool Static;
        public int ShadowSlot = -1;
        public int ListIndex;
        public int TableIndex = -1;
    }

    private readonly Dictionary<IRenderableLight, Entry> _entries = new(ReferenceEqualityComparer.Instance);
    private readonly List<Entry> _static = new();
    private readonly List<Entry> _dynamic = new();
    private readonly List<Entry> _culled = new();
    private readonly List<Entry> _gone = new();
    private readonly LightTreeBuilder _staticTree = new();
    private readonly LightTreeBuilder _dynamicTree = new();
    private readonly Float4[] _record = new Float4[TexelsPerLight];
    private Vector4[] _spheres = [];

    private bool _staticDirty;
    private bool _dynamicPending;
    private Frustum? _view;
    private int _staticNodes;
    private int _dynamicCapacity;
    private int _dynamicNodeCapacity;
    private int _ring;

    internal ShaderDataTable LightTable { get; } = new("ProwlLightData", "_LightDataTex", GraphicsFeature.FragmentStorageBuffers);
    internal ShaderDataTable NodeTable { get; } = new("ProwlLightNodes", "_LightNodeTex", GraphicsFeature.FragmentStorageBuffers);

    /// <summary>The node the static tree starts at, or -1 when it is empty.</summary>
    public int StaticRoot { get; private set; } = -1;

    /// <summary>The node this frame's dynamic tree starts at, or -1 when no moving light is in view.</summary>
    public int DynamicRoot { get; private set; } = -1;

    public int StaticCount => _static.Count;
    public int DynamicCount => _dynamic.Count;

    /// <summary>Moving lights the last build kept after culling to the view.</summary>
    public int DynamicVisible { get; private set; }

    /// <summary>Adds a light or refreshes one, moving it between the trees when its static flag changed.</summary>
    public void Track(IRenderableLight light, in ForwardLightData data, bool isStatic)
    {
        if (!_entries.TryGetValue(light, out Entry? entry))
        {
            entry = new Entry { Light = light, Data = data, Static = isStatic };
            _entries[light] = entry;
            Insert(entry);
            return;
        }

        if (entry.Static != isStatic)
        {
            RemoveFromList(entry);
            entry.Static = isStatic;
            entry.Data = data;
            Insert(entry);
            return;
        }

        if (!entry.Static)
        {
            entry.Data = data;
            return;
        }

        if (SameRecord(entry.Data, data)) return;
        bool moved = entry.Data.Position != data.Position || entry.Data.Range != data.Range;
        entry.Data = data;
        if (moved) _staticDirty = true;
        else WriteStaticRecord(entry);
    }

    /// <summary>Drops every light <paramref name="seen"/> does not hold.</summary>
    public void RemoveUnseen(HashSet<IRenderableLight> seen)
    {
        if (_entries.Count == seen.Count) return;
        _gone.Clear();
        foreach (Entry entry in _entries.Values)
            if (!seen.Contains(entry.Light)) _gone.Add(entry);
        foreach (Entry entry in _gone)
        {
            _entries.Remove(entry.Light);
            RemoveFromList(entry);
        }
        _gone.Clear();
    }

    /// <summary>Marks the moving lights for a rebuild at the next <see cref="Prepare"/>, culled to <paramref name="view"/>, or none when null.</summary>
    public void BeginFrame(Frustum? view)
    {
        _view = view;
        _dynamicPending = true;
    }

    public void SetShadowSlot(IRenderableLight light, int slot)
    {
        if (!_entries.TryGetValue(light, out Entry? entry) || entry.ShadowSlot == slot) return;
        entry.ShadowSlot = slot;
        if (entry.Static) WriteStaticRecord(entry);
    }

    public bool Contains(IRenderableLight light) => _entries.ContainsKey(light);

    public bool IsStatic(IRenderableLight light) => _entries.TryGetValue(light, out Entry? entry) && entry.Static;

    public int ShadowSlotOf(IRenderableLight light) => _entries.TryGetValue(light, out Entry? entry) ? entry.ShadowSlot : -1;

    public ForwardLightData DataOf(IRenderableLight light) => _entries.TryGetValue(light, out Entry? entry) ? entry.Data : default;

    private void Insert(Entry entry)
    {
        List<Entry> list = entry.Static ? _static : _dynamic;
        entry.ListIndex = list.Count;
        entry.TableIndex = -1;
        list.Add(entry);
        if (entry.Static) _staticDirty = true;
    }

    private void RemoveFromList(Entry entry)
    {
        List<Entry> list = entry.Static ? _static : _dynamic;
        int last = list.Count - 1;
        list[entry.ListIndex] = list[last];
        list[entry.ListIndex].ListIndex = entry.ListIndex;
        list.RemoveAt(last);
        if (entry.Static) _staticDirty = true;
    }

    private static bool SameRecord(in ForwardLightData a, in ForwardLightData b) =>
        a.Type == b.Type && a.Position == b.Position && a.Direction == b.Direction && a.Color == b.Color
        && a.Intensity == b.Intensity && a.Range == b.Range && a.SpotAngle == b.SpotAngle && a.InnerSpotAngle == b.InnerSpotAngle
        && a.ShadowEnabled == b.ShadowEnabled && a.ShadowDepthBias == b.ShadowDepthBias && a.ShadowNormalBias == b.ShadowNormalBias
        && a.ShadowStrength == b.ShadowStrength && a.ShadowQuality == b.ShadowQuality;

    /// <summary>Binds both tables and roots for every shader, after <see cref="Prepare"/> has filled them.</summary>
    public void Upload(CommandBuffer cmd)
    {
        Prepare();
        if (LightTable.Capacity > 0)
        {
            LightTable.Bind(cmd);
            NodeTable.Bind(cmd);
        }
        cmd.SetGlobalInt("_StaticLightRoot", StaticRoot);
        cmd.SetGlobalInt("_DynamicLightRoot", DynamicRoot);
    }

    /// <summary>Rebuilds the static tree when it changed and the moving lights once per <see cref="BeginFrame"/>.</summary>
    internal void Prepare()
    {
        if (_staticDirty) BuildStatic();
        if (_dynamicPending) BuildDynamic();
    }

    private void BuildStatic()
    {
        _staticDirty = false;
        int count = _static.Count;
        _staticTree.Build(Spheres(_static, count), StaticLeafSize, morton: false);
        _staticNodes = _staticTree.NodeCount;
        StaticRoot = _staticNodes > 0 ? 0 : -1;
        WarnIfTooDeep(_staticTree);

        // The ring sits after the static tree, so it moves along and the moving lights are written again
        ReserveTables();
        _dynamicPending = true;

        ReadOnlySpan<int> order = _staticTree.Order;
        for (int i = 0; i < count; i++)
        {
            Entry entry = _static[order[i]];
            entry.TableIndex = i;
            WriteRecord(entry, i);
        }
        WriteNodes(_staticTree.Nodes, 0, 0);
    }

    private void BuildDynamic()
    {
        _dynamicPending = false;
        _culled.Clear();
        foreach (Entry entry in _dynamic)
            if (_view is not { } view || view.Intersects(new Sphere(entry.Data.Position, entry.Data.Range)))
                _culled.Add(entry);

        int count = _culled.Count;
        DynamicVisible = count;
        if (count == 0)
        {
            DynamicRoot = -1;
            return;
        }

        // Leaves of about a thousandth of the lights, where the cost of building meets the cost of walking
        int leaf = Math.Clamp(count / 1024, 4, 128);
        _dynamicTree.Build(Spheres(_culled, count), leaf, morton: true);
        WarnIfTooDeep(_dynamicTree);

        if (count > _dynamicCapacity || _dynamicTree.NodeCount > _dynamicNodeCapacity)
        {
            _dynamicCapacity = Math.Max(64, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)count));
            _dynamicNodeCapacity = Math.Max(_dynamicCapacity / 2 + 64, _dynamicTree.NodeCount);
            ReserveTables();
        }

        int slot = _ring;
        _ring = (_ring + 1) % RingSlots;
        int lightBase = _static.Count + slot * _dynamicCapacity;
        int nodeBase = _staticNodes + slot * _dynamicNodeCapacity;

        ReadOnlySpan<int> order = _dynamicTree.Order;
        for (int i = 0; i < count; i++)
            WriteRecord(_culled[order[i]], lightBase + i);
        WriteNodes(_dynamicTree.Nodes, nodeBase, lightBase);
        DynamicRoot = nodeBase;
    }

    private static void WarnIfTooDeep(LightTreeBuilder tree)
    {
        if (tree.Depth * 3 > LightTreeBuilder.ShaderStack)
            Debug.LogWarningOnce("LightTree.Depth", $"A light tree is {tree.Depth} levels deep, more than the shader's traversal stack holds, so some lights may be skipped.");
    }

    private void ReserveTables()
    {
        LightTable.EnsureCapacity((_static.Count + RingSlots * _dynamicCapacity) * TexelsPerLight);
        NodeTable.EnsureCapacity((_staticNodes + RingSlots * _dynamicNodeCapacity) * LightTreeNode.Vec4Count);
    }

    private ReadOnlySpan<Vector4> Spheres(List<Entry> entries, int count)
    {
        if (_spheres.Length < count) _spheres = new Vector4[Math.Max(count, _spheres.Length * 2)];
        for (int i = 0; i < count; i++)
        {
            ForwardLightData d = entries[i].Data;
            _spheres[i] = new Vector4(d.Position.X, d.Position.Y, d.Position.Z, Math.Max(d.Range, 1e-4f));
        }
        return _spheres.AsSpan(0, count);
    }

    private void WriteNodes(ReadOnlySpan<LightTreeNode> nodes, int nodeBase, int lightBase) => WriteNodes(NodeTable, nodes, 0, nodeBase, lightBase);

    /// <summary>
    /// Writes a tree's nodes into <paramref name="table"/> from texel <paramref name="texelBase"/>. A tree stores its
    /// children relative to itself, so they are shifted to where its nodes and lights sit in the tables.
    /// </summary>
    internal static void WriteNodes(ShaderDataTable table, ReadOnlySpan<LightTreeNode> nodes, int texelBase, int nodeBase, int lightBase)
    {
        table.EnsureCapacity(texelBase + (nodeBase + nodes.Length) * LightTreeNode.Vec4Count);
        for (int n = 0; n < nodes.Length; n++)
        {
            LightTreeNode node = nodes[n];
            int t = texelBase + (nodeBase + n) * LightTreeNode.Vec4Count;
            table[t + 0] = ToFloat4(node.MinX);
            table[t + 1] = ToFloat4(node.MinY);
            table[t + 2] = ToFloat4(node.MinZ);
            table[t + 3] = ToFloat4(node.MaxX);
            table[t + 4] = ToFloat4(node.MaxY);
            table[t + 5] = ToFloat4(node.MaxZ);
            table[t + 6] = new Float4(Rebase(node.Child0, nodeBase, lightBase), Rebase(node.Child1, nodeBase, lightBase),
                                      Rebase(node.Child2, nodeBase, lightBase), Rebase(node.Child3, nodeBase, lightBase));
            table[t + 7] = new Float4(Bits(node.Count0), Bits(node.Count1), Bits(node.Count2), Bits(node.Count3));
        }
    }

    private static float Rebase(uint child, int nodeBase, int lightBase)
    {
        if (child != LightTreeBuilder.Invalid)
            child = (child & LightTreeBuilder.LeafBit) != 0 ? child + (uint)lightBase : child + (uint)nodeBase;
        return Bits(child);
    }

    private static Float4 ToFloat4(Vector4 v) => new(v.X, v.Y, v.Z, v.W);

    private static float Bits(uint value) => BitConverter.UInt32BitsToSingle(value);

    private static float Bits(int value) => BitConverter.Int32BitsToSingle(value);

    private void WriteStaticRecord(Entry entry)
    {
        if (!_staticDirty && entry.TableIndex >= 0) WriteRecord(entry, entry.TableIndex);
    }

    //   +0 Position.xyz, Range
    //   +1 Color.rgb, Intensity
    //   +2 Direction.xyz, type in the low 2 bits and shadow enabled in bit 2, as int bits
    //   +3 cos of the outer and inner spot angles, shadow depth and normal bias
    //   +4 shadow strength, shadow quality, shadow slot as int bits
    private void WriteRecord(Entry entry, int index)
    {
        ForwardLightData d = entry.Data;
        int type = d.Type switch { LightType.Directional => 0, LightType.Spot => 2, _ => 1 };
        int flags = type | ((d.ShadowEnabled ? 1 : 0) << 2);
        float spotCos = d.Type == LightType.Spot ? MathF.Cos(d.SpotAngle * MathF.PI / 180f) : -1f;
        float innerCos = d.Type == LightType.Spot ? MathF.Cos(d.InnerSpotAngle * MathF.PI / 180f) : 1f;

        _record[0] = new Float4(d.Position.X, d.Position.Y, d.Position.Z, d.Range);
        _record[1] = new Float4(d.Color.X, d.Color.Y, d.Color.Z, d.Intensity);
        _record[2] = new Float4(d.Direction.X, d.Direction.Y, d.Direction.Z, Bits(flags));
        _record[3] = new Float4(spotCos, innerCos, d.ShadowDepthBias, d.ShadowNormalBias);
        _record[4] = new Float4(d.ShadowStrength, d.ShadowQuality, Bits(entry.ShadowSlot), 0f);

        LightTable.EnsureCapacity((index + 1) * TexelsPerLight);
        LightTable.Write(index * TexelsPerLight, _record);
    }

    public void Dispose()
    {
        LightTable.Dispose();
        NodeTable.Dispose();
        _entries.Clear();
        _static.Clear();
        _dynamic.Clear();
        _culled.Clear();
        StaticRoot = DynamicRoot = -1;
    }
}
