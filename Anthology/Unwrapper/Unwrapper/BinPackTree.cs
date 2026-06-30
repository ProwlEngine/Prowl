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
internal sealed class BinPackTree
{
    private struct Node
    {
        public Double2 Origin;
        public Double2 Extent;
        public int LeftChild;
        public int RightChild;
        public bool IsLeaf;
        public bool IsOccupied;

        public const int NoChild = -1;
    }

    private readonly List<Node> _nodes;

    public BinPackTree(int initialCapacity) => _nodes = new List<Node>(initialCapacity);

    public void StartPack(Double2 borderInset)
    {
        _nodes.Clear();
        _nodes.Add(new Node
        {
            Origin = borderInset,
            Extent = new Double2(1, 1) - borderInset,
            LeftChild = Node.NoChild,
            RightChild = Node.NoChild,
            IsLeaf = true,
            IsOccupied = false,
        });
    }

    /// <summary>Attempt to place <paramref name="rect"/>. On success, its origin is filled in.</summary>
    public bool TryInsert(ref BinRect rect, double border) => TryInsert(0, ref rect, border);

    private bool TryInsert(int nodeIdx, ref BinRect rect, double border)
    {
        var node = _nodes[nodeIdx];

        if (node.IsLeaf)
        {
            if (node.IsOccupied) return false;

            const double eps = 1e-4;
            if (NumericHelpers.ApproxLess(node.Extent.X, rect.Extent.X, eps) ||
                NumericHelpers.ApproxLess(node.Extent.Y, rect.Extent.Y, eps))
                return false;

            double remainingW = node.Extent.X - rect.Extent.X;
            double remainingH = node.Extent.Y - rect.Extent.Y;

            // Tight fit — claim this node.
            if (NumericHelpers.ApproxLessOrEqual(remainingW, border, eps) && NumericHelpers.ApproxLessOrEqual(remainingH, border, eps))
            {
                rect.Origin = node.Origin;
                node.IsOccupied = true;
                _nodes[nodeIdx] = node;
                return true;
            }

            // Otherwise split along whichever direction has more leftover.
            bool widerLeftover = NumericHelpers.ApproxLess(remainingH, remainingW, eps);
            Node innerChild = widerLeftover
                ? new Node { Origin = node.Origin, Extent = new Double2(rect.Extent.X, node.Extent.Y), LeftChild = Node.NoChild, RightChild = Node.NoChild, IsLeaf = true }
                : new Node { Origin = node.Origin, Extent = new Double2(node.Extent.X, rect.Extent.Y), LeftChild = Node.NoChild, RightChild = Node.NoChild, IsLeaf = true };
            Node remainder = widerLeftover
                ? new Node { Origin = node.Origin + new Double2(rect.Extent.X, 0), Extent = new Double2(remainingW, node.Extent.Y), LeftChild = Node.NoChild, RightChild = Node.NoChild, IsLeaf = true }
                : new Node { Origin = node.Origin + new Double2(0, rect.Extent.Y), Extent = new Double2(node.Extent.X, remainingH), LeftChild = Node.NoChild, RightChild = Node.NoChild, IsLeaf = true };

            node.IsLeaf = false;
            node.LeftChild = _nodes.Count;
            node.RightChild = _nodes.Count + 1;
            _nodes[nodeIdx] = node;

            int target = node.LeftChild;
            _nodes.Add(innerChild);
            _nodes.Add(remainder);
            return TryInsert(target, ref rect, border);
        }
        else
        {
            return TryInsert(node.LeftChild, ref rect, border) || TryInsert(node.RightChild, ref rect, border);
        }
    }
}
