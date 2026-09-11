using System;

namespace Prowl.Scaffold;
/// <summary>A node identity and its world rectangle, produced by CopyWorldRects.</summary>
public readonly record struct LayoutResult(NodeId Node, Rect WorldRect);
public sealed partial class LayoutTree
{
    /// <summary>Count this node and all its descendants in O(subtree size).</summary>
    public int GetSubtreeCount(NodeId node) => SubtreeCount(Index(node));
    private int SubtreeCount(int nodeIndex)
    {
        int count = 1;
        for (int childIndex = _nodes[nodeIndex].First; childIndex >= 0; childIndex = _nodes[childIndex].Next)
        {
            count += SubtreeCount(childIndex);
        }

        return count;
    }

    /// <summary>Copy a subtree in parent-first hierarchy order to caller-owned storage.
    /// Returns entries written, including hidden nodes (zero rectangles). O(subtree size + depth),
    /// zero allocations. An undersized destination throws before writing anything.
    /// Call after successful Layout. Sibling order follows the tree, unaffected by Style.Reverse.</summary>
    public int CopyWorldRects(NodeId node, Span<LayoutResult> destination)
    {
        int nodeIndex = Index(node);
        if (destination.Length < SubtreeCount(nodeIndex))
        {
            throw new ArgumentException("Destination is smaller than the subtree.", nameof(destination));
        }

        float x = 0, y = 0;
        bool hidden = false;
        for (int parentIndex = _nodes[nodeIndex].Parent; parentIndex >= 0; parentIndex = _nodes[parentIndex].Parent)
        {
            x += _nodes[parentIndex].Rect.X;
            y += _nodes[parentIndex].Rect.Y;
            hidden |= _nodes[parentIndex].Style.Hidden;
        }

        int written = 0;
        CopyWorldRects(nodeIndex, destination, ref written, x, y, hidden);
        return written;
    }

    private void CopyWorldRects(int nodeIndex, Span<LayoutResult> destination, ref int written, float x, float y, bool hidden)
    {
        ref Node nodeData = ref _nodes[nodeIndex];
        hidden |= nodeData.Style.Hidden;
        Rect world = hidden
            ? default
            : nodeData.Rect with
            {
                X = x + nodeData.Rect.X,
                Y = y + nodeData.Rect.Y
            };
        destination[written++] = new LayoutResult(Handle(nodeIndex), world);
        for (int childIndex = nodeData.First; childIndex >= 0; childIndex = _nodes[childIndex].Next)
        {
            CopyWorldRects(childIndex, destination, ref written, world.X, world.Y, hidden);
        }
    }
}
