// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Unwrapper;

/// <summary>One rectangle being placed by <see cref="BinPackTree"/>.</summary>
internal struct BinRect
{
    public Double2 Origin;
    public Double2 Extent;
}

/// <summary>
/// Recursive binary bin packer. Each leaf is either free or filled; on insert we split a node
/// into a child that exactly fits the new rectangle and a sibling holding the leftover.
/// </summary>
/// <remarks>
/// Every node carries the largest free width and height anywhere beneath it, so a search can skip
/// a whole subtree instead of walking to its leaves. Without that, placing the n-th rectangle
/// costs a full tree walk and packing an atlas of thousands of charts goes quadratic.
/// </remarks>
internal sealed class BinPackTree
{
    private const double Epsilon = 1e-4;

    private struct Node
    {
        public Double2 Origin;
        public Double2 Extent;
        public int LeftChild;
        public int RightChild;
        public int Parent;
        public double MaxFreeWidth;
        public double MaxFreeHeight;
        public bool IsLeaf;
        public bool IsOccupied;

        public const int NoChild = -1;
    }

    private readonly List<Node> _nodes;

    public BinPackTree(int initialCapacity) => _nodes = new List<Node>(initialCapacity);

    public void StartPack(Double2 borderInset)
    {
        Double2 extent = new Double2(1, 1) - borderInset;
        _nodes.Clear();
        _nodes.Add(new Node
        {
            Origin = borderInset,
            Extent = extent,
            LeftChild = Node.NoChild,
            RightChild = Node.NoChild,
            Parent = Node.NoChild,
            MaxFreeWidth = extent.X,
            MaxFreeHeight = extent.Y,
            IsLeaf = true,
            IsOccupied = false,
        });
    }

    /// <summary>Attempt to place <paramref name="rect"/>. On success, its origin is filled in.</summary>
    public bool TryInsert(ref BinRect rect, double border) => TryInsert(0, ref rect, border);

    private bool TryInsert(int nodeIdx, ref BinRect rect, double border)
    {
        var node = _nodes[nodeIdx];

        if (node.MaxFreeWidth < rect.Extent.X - Epsilon || node.MaxFreeHeight < rect.Extent.Y - Epsilon)
            return false;

        if (!node.IsLeaf)
            return TryInsert(node.LeftChild, ref rect, border) || TryInsert(node.RightChild, ref rect, border);

        if (node.IsOccupied) return false;

        double remainingW = node.Extent.X - rect.Extent.X;
        double remainingH = node.Extent.Y - rect.Extent.Y;

        // Tight fit — claim this node.
        if (NumericHelpers.ApproxLessOrEqual(remainingW, border, Epsilon) && NumericHelpers.ApproxLessOrEqual(remainingH, border, Epsilon))
        {
            rect.Origin = node.Origin;
            node.IsOccupied = true;
            node.MaxFreeWidth = 0.0;
            node.MaxFreeHeight = 0.0;
            _nodes[nodeIdx] = node;
            PropagateFreeExtents(node.Parent);
            return true;
        }

        // Otherwise split along whichever direction has more leftover.
        bool widerLeftover = NumericHelpers.ApproxLess(remainingH, remainingW, Epsilon);
        Double2 innerExtent = widerLeftover
            ? new Double2(rect.Extent.X, node.Extent.Y)
            : new Double2(node.Extent.X, rect.Extent.Y);
        Double2 remainderOrigin = widerLeftover
            ? node.Origin + new Double2(rect.Extent.X, 0)
            : node.Origin + new Double2(0, rect.Extent.Y);
        Double2 remainderExtent = widerLeftover
            ? new Double2(remainingW, node.Extent.Y)
            : new Double2(node.Extent.X, remainingH);

        node.IsLeaf = false;
        node.LeftChild = _nodes.Count;
        node.RightChild = _nodes.Count + 1;
        _nodes[nodeIdx] = node;

        _nodes.Add(NewLeaf(node.Origin, innerExtent, nodeIdx));
        _nodes.Add(NewLeaf(remainderOrigin, remainderExtent, nodeIdx));

        return TryInsert(node.LeftChild, ref rect, border);
    }

    private static Node NewLeaf(Double2 origin, Double2 extent, int parent) => new()
    {
        Origin = origin,
        Extent = extent,
        LeftChild = Node.NoChild,
        RightChild = Node.NoChild,
        Parent = parent,
        MaxFreeWidth = extent.X,
        MaxFreeHeight = extent.Y,
        IsLeaf = true,
        IsOccupied = false,
    };

    /// <summary>Refresh cached free extents up the ancestor chain, stopping once nothing changes.</summary>
    private void PropagateFreeExtents(int nodeIdx)
    {
        while (nodeIdx != Node.NoChild)
        {
            var node = _nodes[nodeIdx];
            var left = _nodes[node.LeftChild];
            var right = _nodes[node.RightChild];

            double width = System.Math.Max(left.MaxFreeWidth, right.MaxFreeWidth);
            double height = System.Math.Max(left.MaxFreeHeight, right.MaxFreeHeight);
            if (width == node.MaxFreeWidth && height == node.MaxFreeHeight) return;

            node.MaxFreeWidth = width;
            node.MaxFreeHeight = height;
            _nodes[nodeIdx] = node;
            nodeIdx = node.Parent;
        }
    }
}
