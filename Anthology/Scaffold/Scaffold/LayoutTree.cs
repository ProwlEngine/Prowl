using System;
using System.Threading;

namespace Prowl.Scaffold;
/// <summary>A single-threaded persistent layout forest. Storage grows only on creation/Reserve.
/// Geometry is parent-local. GetWorldRect composes origins without walking unchanged descendants.</summary>
public sealed partial class LayoutTree
{
    private struct Measurement
    {
        public bool Valid;
        public Size Available, Result;
        public float Width, Height;
        public readonly bool Matches(Size available, float width, float height) => Valid && Available == available && Width == width && Height == height;
    }

    /// <summary>The four most recent measurements of one node. A node is commonly measured under a
    /// handful of constraints per pass (natural, then forced main and/or cross), so keeping the last
    /// few avoids re-running content callbacks within a single layout.</summary>
    private struct MeasureCache
    {
        private Measurement _a, _b, _c, _d;
        public bool TryGet(Size available, float width, float height, out Size result)
        {
            if (_a.Matches(available, width, height))
            {
                result = _a.Result;
                return true;
            }

            if (_b.Matches(available, width, height))
            {
                result = _b.Result;
                return true;
            }

            if (_c.Matches(available, width, height))
            {
                result = _c.Result;
                return true;
            }

            if (_d.Matches(available, width, height))
            {
                result = _d.Result;
                return true;
            }

            result = default;
            return false;
        }

        public void Add(Size available, float width, float height, Size result)
        {
            _d = _c;
            _c = _b;
            _b = _a;
            _a = new Measurement
            {
                Valid = true,
                Available = available,
                Width = width,
                Height = height,
                Result = result
            };
        }

        public void Clear() => _a.Valid = _b.Valid = _c.Valid = _d.Valid = false;
    }

    private struct Node
    {
        public uint Generation;
        public bool Alive, MeasureDirty, ArrangeDirty, HasArrange;
        public int Parent, First, Last, Previous, Next;
        public Style Style;
        public MeasureContent? Measure;
        public object? Context;
        public Size Desired, ArrangedSize;
        public Size ContentSize;
        public Size ArrangedParentSize;
        public MeasureCache Measurements;
        public Rect Rect;
        public float BaseMain, FlexMain;
        public float Before, After;
        public bool Frozen;
    }

    private static long s_owner;
    private readonly long _owner = Interlocked.Increment(ref s_owner);
    private Node[] _nodes;
    private int _used, _free = -1;
    private bool _layingOut;
    private int _measured, _arranged, _measureHits, _arrangeHits;
    public int Count { get; private set; }
    public int Capacity => _nodes.Length;
    public LayoutStatistics Statistics => new(_measured, _arranged, _measureHits, _arrangeHits);

    public LayoutTree(int capacity = 128)
    {
        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _nodes = new Node[Math.Max(1, capacity)];
    }

    public void Reserve(int capacity)
    {
        CheckMutation();
        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        if (capacity > Capacity)
        {
            Array.Resize(ref _nodes, capacity);
        }
    }

    public NodeId Create(in Style style, NodeId parent = default)
    {
        CheckMutation();
        Validate(style);
        int parentIndex = parent == default ? -1 : Index(parent);
        CheckDepth(parentIndex, 1);
        int nodeIndex;
        if (_free >= 0)
        {
            nodeIndex = _free;
            _free = _nodes[nodeIndex].Next;
        }
        else
        {
            if (_used == Capacity)
            {
                Reserve(checked(Capacity * 2));
            }

            nodeIndex = _used++;
        }

        uint generation = _nodes[nodeIndex].Generation;
        _nodes[nodeIndex] = new Node
        {
            Alive = true,
            Generation = generation == 0 ? 1 : generation,
            Parent = -1,
            First = -1,
            Last = -1,
            Previous = -1,
            Next = -1,
            Style = style,
            MeasureDirty = true,
            ArrangeDirty = true
        };
        Count++;
        if (parentIndex >= 0)
        {
            Attach(nodeIndex, parentIndex, _nodes[parentIndex].Last);
        }

        return Handle(nodeIndex);
    }

    public bool IsAlive(NodeId node) => node.Owner == _owner && (uint)node.Index < (uint)_used && _nodes[node.Index].Alive
        && _nodes[node.Index].Generation == node.Generation;
    private int Index(NodeId node) => IsAlive(node)
        ? node.Index
        : throw new ArgumentException("Stale, invalid, or foreign layout node.", nameof(node));
    private NodeId Handle(int nodeIndex) => nodeIndex < 0 ? default : new(_owner, nodeIndex, _nodes[nodeIndex].Generation);
    public Style GetStyle(NodeId node) => _nodes[Index(node)].Style;
    public NodeId GetParent(NodeId node) => Handle(_nodes[Index(node)].Parent);
    public NodeId GetFirstChild(NodeId node) => Handle(_nodes[Index(node)].First);
    public NodeId GetNextSibling(NodeId node) => Handle(_nodes[Index(node)].Next);
    public Rect GetRect(NodeId node) => _nodes[Index(node)].Rect;
    public Rect GetWorldRect(NodeId node)
    {
        int nodeIndex = Index(node);
        if (_nodes[nodeIndex].Style.Hidden)
        {
            return default;
        }

        Rect rect = _nodes[nodeIndex].Rect;
        for (int parentIndex = _nodes[nodeIndex].Parent; parentIndex >= 0; parentIndex = _nodes[parentIndex].Parent)
        {
            if (_nodes[parentIndex].Style.Hidden)
            {
                return default;
            }

            rect = rect with
            {
                X = rect.X + _nodes[parentIndex].Rect.X,
                Y = rect.Y + _nodes[parentIndex].Rect.Y
            };
        }

        return rect;
    }

    /// <summary>Exact value comparison: declaring an identical style does not invalidate layout.</summary>
    public bool SetStyle(NodeId node, in Style style)
    {
        CheckMutation();
        int nodeIndex = Index(node);
        if (_nodes[nodeIndex].Style == style)
        {
            return false;
        }

        Validate(style);
        _nodes[nodeIndex].Style = style;
        Invalidate(nodeIndex);
        return true;
    }

    /// <summary>Register the intrinsic measurement for a node. Always invalidates: the context is
    /// opaque, so re-declaring is the caller saying the content may have moved. Register once and
    /// use <see cref="MarkDirty"/> for updates if you want to keep a node's measurement cached.</summary>
    public void SetMeasure(NodeId node, MeasureContent? measure, object? context = null)
    {
        CheckMutation();
        int nodeIndex = Index(node);
        _nodes[nodeIndex].Measure = measure;
        _nodes[nodeIndex].Context = context;
        Invalidate(nodeIndex);
    }

    public void MarkDirty(NodeId node)
    {
        CheckMutation();
        Invalidate(Index(node));
    }

    /// <summary>Turn a subtree into a root without destroying its nodes or cached geometry.</summary>
    public void Detach(NodeId node)
    {
        CheckMutation();
        int nodeIndex = Index(node);
        if (_nodes[nodeIndex].Parent < 0)
        {
            return;
        }

        Detach(nodeIndex);
        Invalidate(nodeIndex);
    }

    public bool IsDirty(NodeId node) => _nodes[Index(node)].ArrangeDirty;
    private void Invalidate(int nodeIndex)
    {
        // Always process the initial node: a newly attached node starts dirty, but its
        // new ancestors may be clean. Stop only when reaching an already-dirty ancestor.
        DirtyNode(nodeIndex);
        for (nodeIndex = _nodes[nodeIndex].Parent; nodeIndex >= 0; nodeIndex = _nodes[nodeIndex].Parent)
        {
            if (_nodes[nodeIndex].MeasureDirty && _nodes[nodeIndex].ArrangeDirty)
            {
                break;
            }

            DirtyNode(nodeIndex);
        }
    }

    private void DirtyNode(int nodeIndex)
    {
        _nodes[nodeIndex].MeasureDirty = true;
        _nodes[nodeIndex].ArrangeDirty = true;
        _nodes[nodeIndex].Measurements.Clear();
    }

    /// <summary>Invalidate every measurement in a subtree, e.g. after a global font/DPI change.
    /// Visits the subtree once, then propagates through its ancestors once.</summary>
    public void MarkSubtreeDirty(NodeId node)
    {
        CheckMutation();
        int nodeIndex = Index(node);
        DirtySubtree(nodeIndex);
        if (_nodes[nodeIndex].Parent >= 0)
        {
            Invalidate(_nodes[nodeIndex].Parent);
        }
    }

    private void DirtySubtree(int nodeIndex)
    {
        DirtyNode(nodeIndex);
        for (int childIndex = _nodes[nodeIndex].First; childIndex >= 0; childIndex = _nodes[childIndex].Next)
        {
            DirtySubtree(childIndex);
        }
    }

    /// <summary>Move a subtree, or reorder it immediately after a sibling. Default after means first.</summary>
    public void PlaceAfter(NodeId node, NodeId parent, NodeId after = default)
    {
        CheckMutation();
        int nodeIndex = Index(node), parentIndex = Index(parent), afterIndex = after == default ? -1 : Index(after);
        if (afterIndex == nodeIndex || (afterIndex >= 0 && _nodes[afterIndex].Parent != parentIndex))
        {
            throw new ArgumentException("after must be a different child of parent.");
        }

        if (_nodes[nodeIndex].Parent == parentIndex)
        {
            if (_nodes[nodeIndex].Previous == afterIndex)
            {
                return;
            }

            // Sibling reorders cannot create a cycle or change subtree depth.
            Detach(nodeIndex);
            Attach(nodeIndex, parentIndex, afterIndex);
            return;
        }

        for (int ancestorIndex = parentIndex; ancestorIndex >= 0; ancestorIndex = _nodes[ancestorIndex].Parent)
        {
            if (ancestorIndex == nodeIndex)
            {
                throw new ArgumentException("A node cannot be parented under itself.");
            }
        }

        CheckDepth(parentIndex, SubtreeDepth(nodeIndex));
        Detach(nodeIndex);
        Attach(nodeIndex, parentIndex, afterIndex);
    }

    private int SubtreeDepth(int nodeIndex)
    {
        int depth = 1;
        for (int childIndex = _nodes[nodeIndex].First; childIndex >= 0; childIndex = _nodes[childIndex].Next)
        {
            depth = Math.Max(depth, 1 + SubtreeDepth(childIndex));
        }

        return depth;
    }

    private void CheckDepth(int parent, int depth)
    {
        for (; parent >= 0; parent = _nodes[parent].Parent)
        {
            depth++;
        }

        if (depth > 256)
        {
            throw new ArgumentException("Layout trees support at most 256 levels.");
        }
    }

    private void Attach(int nodeIndex, int parentIndex, int after)
    {
        int next = after < 0 ? _nodes[parentIndex].First : _nodes[after].Next;
        _nodes[nodeIndex].Parent = parentIndex;
        _nodes[nodeIndex].Previous = after;
        _nodes[nodeIndex].Next = next;
        if (after < 0)
        {
            _nodes[parentIndex].First = nodeIndex;
        }
        else
        {
            _nodes[after].Next = nodeIndex;
        }

        if (next < 0)
        {
            _nodes[parentIndex].Last = nodeIndex;
        }
        else
        {
            _nodes[next].Previous = nodeIndex;
        }

        Invalidate(nodeIndex);
    }

    private void Detach(int nodeIndex)
    {
        ref Node nodeData = ref _nodes[nodeIndex];
        if (nodeData.Parent < 0)
        {
            return;
        }

        if (nodeData.Previous < 0)
        {
            _nodes[nodeData.Parent].First = nodeData.Next;
        }
        else
        {
            _nodes[nodeData.Previous].Next = nodeData.Next;
        }

        if (nodeData.Next < 0)
        {
            _nodes[nodeData.Parent].Last = nodeData.Previous;
        }
        else
        {
            _nodes[nodeData.Next].Previous = nodeData.Previous;
        }

        Invalidate(nodeData.Parent);
        nodeData.Parent = nodeData.Previous = nodeData.Next = -1;
    }

    /// <summary>Remove a subtree and invalidate every handle into it.</summary>
    public void Remove(NodeId node)
    {
        CheckMutation();
        int nodeIndex = Index(node);
        Detach(nodeIndex);
        Release(nodeIndex);
    }

    private void Release(int nodeIndex)
    {
        for (int childIndex = _nodes[nodeIndex].First; childIndex >= 0;)
        {
            int next = _nodes[childIndex].Next;
            Release(childIndex);
            childIndex = next;
        }

        uint generation = _nodes[nodeIndex].Generation;
        _nodes[nodeIndex] = default;
        // Retire exhausted slots, so a generation can never alias an old handle.
        if (generation != uint.MaxValue)
        {
            _nodes[nodeIndex].Generation = generation + 1;
            _nodes[nodeIndex].Next = _free;
            _free = nodeIndex;
        }

        Count--;
    }

    /// <summary>Lay out a root into an exact viewport. Clean repeated calls are O(1).
    /// Root width/height are overridden by the viewport. Padding and child layout still apply.</summary>
    public LayoutStatistics Layout(NodeId root, Size viewport)
    {
        CheckMutation();
        int nodeIndex = Index(root);
        if (_nodes[nodeIndex].Parent >= 0)
        {
            throw new ArgumentException("Layout requires a root.", nameof(root));
        }

        Nonnegative(viewport.Width, nameof(viewport));
        Nonnegative(viewport.Height, nameof(viewport));
        _measured = _arranged = _measureHits = _arrangeHits = 0;
        _layingOut = true;
        try
        {
            Arrange(nodeIndex, new Rect(0, 0, viewport.Width, viewport.Height));
        }
        finally
        {
            _layingOut = false;
        }

        return Statistics;
    }

    private void CheckMutation()
    {
        if (_layingOut)
        {
            throw new InvalidOperationException("Layout callbacks must not mutate or recursively lay out this tree.");
        }
    }

    private static void Finite(float value, string property)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(property, value, $"{property} must be finite.");
        }
    }

    private static void Nonnegative(float value, string property)
    {
        Finite(value, property);
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(property, value, $"{property} must not be negative.");
        }
    }

    private static void ValidateLength(Length length, string property)
    {
        Finite(length.Px, property);
        Finite(length.Pct, property);
        Nonnegative(length.Grow, property);
        Nonnegative(length.AutoFactor, property);
        Nonnegative(length.Shrink, property);
    }

    /// <summary>Size bounds take pixels and percentages. Intrinsic content and flex weights are
    /// properties of a size, not of a limit on one.</summary>
    private static void ValidateBound(Length length, string property)
    {
        Nonnegative(length.Px, property);
        Nonnegative(length.Pct, property);
        if (length.Grow != 0 || length.AutoFactor != 0 || length.Shrink != 0)
        {
            throw new ArgumentException($"{property} takes pixels and percentages only.", property);
        }
    }

    private static void ValidateSpacing(in LengthEdges edges, string property, bool allowGrow, bool allowNegative)
    {
        Edge(edges.Left, "Left");
        Edge(edges.Top, "Top");
        Edge(edges.Right, "Right");
        Edge(edges.Bottom, "Bottom");
        void Edge(Length length, string side)
        {
            string name = $"{property}.{side}";
            if (allowNegative)
            {
                Finite(length.Px, name);
                Finite(length.Pct, name);
            }
            else
            {
                Nonnegative(length.Px, name);
                Nonnegative(length.Pct, name);
            }

            Nonnegative(length.Grow, name);
            if ((!allowGrow && length.Grow != 0) || length.AutoFactor != 0 || length.Shrink != 0)
            {
                throw new ArgumentException(
                    allowGrow
                        ? $"{name} takes pixels, percentages and a grow weight."
                        : $"{name} takes pixels and percentages only.",
                    name);
            }
        }
    }

    private static void Validate(Style style)
    {
        ValidateLength(style.Width, "Width");
        ValidateLength(style.Height, "Height");
        ValidateBound(style.MinWidth, "MinWidth");
        ValidateBound(style.MinHeight, "MinHeight");
        ValidateBound(style.MaxWidth, "MaxWidth");
        ValidateBound(style.MaxHeight, "MaxHeight");
        // Bounds can only be compared once both are resolved, so only the pure-pixel case is
        // checkable here. A percentage bound is clamped against its minimum during layout.
        if ((style.MinWidth.Pct == 0 && style.MaxWidth.Pct == 0 && style.MinWidth.Px > style.MaxWidth.Px)
            || (style.MinHeight.Pct == 0 && style.MaxHeight.Pct == 0 && style.MinHeight.Px > style.MaxHeight.Px))
        {
            throw new ArgumentException("Minimum size exceeds maximum.", nameof(style));
        }

        ValidateSpacing(style.Padding, "Padding", allowGrow: false, allowNegative: false);
        ValidateSpacing(style.Margin, "Margin", allowGrow: true, allowNegative: true);
        Nonnegative(style.Gap, "Gap");
        Nonnegative(style.LineGap, "LineGap");
        Nonnegative(style.AspectRatio, "AspectRatio");
        if (style.Left.HasValue)
        {
            Finite(style.Left.Value, "Left.Value");
        }

        if (style.Top.HasValue)
        {
            Finite(style.Top.Value, "Top.Value");
        }

        if (style.Right.HasValue)
        {
            Finite(style.Right.Value, "Right.Value");
        }

        if (style.Bottom.HasValue)
        {
            Finite(style.Bottom.Value, "Bottom.Value");
        }

        if ((uint)style.Layout > 3 || (uint)style.AlignItems > 3
            || (style.AlignSelf.HasValue && (uint)style.AlignSelf.Value > 3)
            || (uint)style.Justify > 5
            || (uint)style.Position > 1)
        {
            throw new ArgumentException("Invalid layout enum.", nameof(style));
        }

        if (style.Layout == LayoutMode.Grid && style.GridColumns < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(style.GridColumns));
        }
    }
}
