using System;

namespace Prowl.Scaffold;

public sealed partial class LayoutTree
{
    private static void CheckMeasured(Size content)
    {
        if (!Ok(content.Width) || !Ok(content.Height))
        {
            throw new InvalidOperationException(
                $"A measure callback returned {content.Width} x {content.Height}. Content sizes must be finite and nonnegative.");
        }

        static bool Ok(float value) => value >= 0 && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static float Clamp(float value, float min, float max) => Math.Min(max, Math.Max(min, value));
    private static float Positive(float value) => Math.Max(0, value);
    private static float Main(Size size, bool row) => row ? size.Width : size.Height;
    private static float Cross(Size size, bool row) => row ? size.Height : size.Width;
    private static float MainMargin(in Resolved style, bool row) => row ? style.Margin.Horizontal : style.Margin.Vertical;
    private static float CrossMargin(in Resolved style, bool row) => row ? style.Margin.Vertical : style.Margin.Horizontal;

    /// <summary>A style with its percentage-bearing lengths collapsed against a known available
    /// size. Everything else passes straight through to the declared style.</summary>
    private readonly struct Resolved
    {
        public readonly Style Declared;
        public readonly float MinWidth, MinHeight, MaxWidth, MaxHeight;
        public readonly Edges Padding, Margin, MarginGrow;
        public Resolved(in Style style, Size available)
        {
            Declared = style;
            MinWidth = Positive(style.MinWidth.Resolve(available.Width, 0));
            MinHeight = Positive(style.MinHeight.Resolve(available.Height, 0));
            MaxWidth = Math.Max(MinWidth, style.MaxWidth.Resolve(available.Width, 0));
            MaxHeight = Math.Max(MinHeight, style.MaxHeight.Resolve(available.Height, 0));
            Padding = new Edges(
                Positive(style.Padding.Left.Resolve(available.Width, 0)),
                Positive(style.Padding.Top.Resolve(available.Height, 0)),
                Positive(style.Padding.Right.Resolve(available.Width, 0)),
                Positive(style.Padding.Bottom.Resolve(available.Height, 0)));
            Margin = new Edges(
                style.Margin.Left.Resolve(available.Width, 0),
                style.Margin.Top.Resolve(available.Height, 0),
                style.Margin.Right.Resolve(available.Width, 0),
                style.Margin.Bottom.Resolve(available.Height, 0));
            MarginGrow = new Edges(style.Margin.Left.Grow, style.Margin.Top.Grow, style.Margin.Right.Grow, style.Margin.Bottom.Grow);
        }

        public Length Width => Declared.Width;
        public Length Height => Declared.Height;
        public LayoutMode Layout => Declared.Layout;
        public Align AlignItems => Declared.AlignItems;
        public Align? AlignSelf => Declared.AlignSelf;
        public Justify Justify => Declared.Justify;
        public float Gap => Declared.Gap;
        public float LineGap => Declared.LineGap;
        public bool Wrap => Declared.Wrap;
        public bool Hidden => Declared.Hidden;
        public float? Left => Declared.Left;
        public float? Top => Declared.Top;
        public float? Right => Declared.Right;
        public float? Bottom => Declared.Bottom;
        public float AspectRatio => Declared.AspectRatio;
        public int GridColumns => Declared.GridColumns;
    }

    private static Resolved ResolveStyle(in Style style, Size available) => new(style, available);

    private static float MarginGrowMain(in Style style, bool row) =>
        row ? style.Margin.Left.Grow + style.Margin.Right.Grow : style.Margin.Top.Grow + style.Margin.Bottom.Grow;

    private static bool DependsOnParent(in LengthEdges edges) =>
        edges.Left.Pct != 0 || edges.Top.Pct != 0 || edges.Right.Pct != 0 || edges.Bottom.Pct != 0;

    private void InitializeMargins(int nodeIndex, Size inner, bool row)
    {
        var style = ResolveStyle(_nodes[nodeIndex].Style, inner);
        _nodes[nodeIndex].Before = row ? style.Margin.Left : style.Margin.Top;
        _nodes[nodeIndex].After = row ? style.Margin.Right : style.Margin.Bottom;
    }

    private int First(int parentIndex) => _nodes[parentIndex].Style.Reverse ? _nodes[parentIndex].Last : _nodes[parentIndex].First;
    private int Next(int parentIndex, int childIndex) => _nodes[parentIndex].Style.Reverse ? _nodes[childIndex].Previous : _nodes[childIndex].Next;
    private bool InFlow(int nodeIndex) => !_nodes[nodeIndex].Style.Hidden && _nodes[nodeIndex].Style.Position == Position.Flow;
    private int Flow(int parentIndex, int childIndex)
    {
        while (childIndex >= 0 && !InFlow(childIndex))
        {
            childIndex = Next(parentIndex, childIndex);
        }

        return childIndex;
    }

    private Size Measure(int nodeIndex, Size available, float forcedWidth = -1, float forcedHeight = -1)
    {
        ref Node node = ref _nodes[nodeIndex];
        if (!node.MeasureDirty && node.Measurements.TryGet(available, forcedWidth, forcedHeight, out Size cached))
        {
            _measureHits++;
            return node.Desired = cached;
        }

        _measured++;
        var style = ResolveStyle(node.Style, available);
        Size result = default;
        if (!style.Hidden)
        {
            float width = forcedWidth >= 0
                ? forcedWidth
                : style.Width.AutoFactor == 0
                ? Clamp(style.Width.Resolve(available.Width, 0), style.MinWidth, style.MaxWidth)
                : Clamp(available.Width, style.MinWidth, style.MaxWidth);
            float height = forcedHeight >= 0
                ? forcedHeight
                : style.Height.AutoFactor == 0
                ? Clamp(style.Height.Resolve(available.Height, 0), style.MinHeight, style.MaxHeight)
                : Clamp(available.Height, style.MinHeight, style.MaxHeight);
            Size inner = new(Positive(width - style.Padding.Horizontal), Positive(height - style.Padding.Vertical));
            bool needsContent = (forcedWidth < 0 && style.Width.AutoFactor > 0)
                || (forcedHeight < 0 && style.Height.AutoFactor > 0);
            Size content = needsContent ? node.Measure?.Invoke(Handle(nodeIndex), inner, node.Context) ?? default : default;
            CheckMeasured(content);
            bool definiteWidth = forcedWidth >= 0 || style.Width.AutoFactor == 0;
            bool definiteHeight = forcedHeight >= 0 || style.Height.AutoFactor == 0;
            Size children = needsContent ? MeasureChildren(nodeIndex, inner, definiteWidth, definiteHeight) : default;
            content = new Size(
                Math.Max(content.Width, children.Width) + style.Padding.Horizontal,
                Math.Max(content.Height, children.Height) + style.Padding.Vertical);
            width = forcedWidth >= 0
                ? forcedWidth
                : Clamp(style.Width.Resolve(available.Width, content.Width), style.MinWidth, style.MaxWidth);
            height = forcedHeight >= 0
                ? forcedHeight
                : Clamp(style.Height.Resolve(available.Height, content.Height), style.MinHeight, style.MaxHeight);
            if (style.AspectRatio > 0)
            {
                if (style.Height.AutoFactor > 0 && forcedHeight < 0
                    && (forcedWidth >= 0 || style.Width.AutoFactor == 0))
                {
                    height = Clamp(width / style.AspectRatio, style.MinHeight, style.MaxHeight);
                }
                else if (style.Width.AutoFactor > 0 && forcedWidth < 0
                    && (forcedHeight >= 0 || style.Height.AutoFactor == 0))
                {
                    width = Clamp(height * style.AspectRatio, style.MinWidth, style.MaxWidth);
                }
            }

            result = new Size(width, height);
        }

        node.Measurements.Add(available, forcedWidth, forcedHeight, result);
        node.Desired = result;
        node.MeasureDirty = false;
        return result;
    }

    private Size MeasureChildren(int parentIndex, Size inner, bool definiteWidth, bool definiteHeight)
    {
        Style style = _nodes[parentIndex].Style;
        if (style.Layout == LayoutMode.Row || style.Layout == LayoutMode.Column)
        {
            bool row = style.Layout == LayoutMode.Row;
            return MeasureFlowChildren(
                parentIndex,
                inner,
                row ? definiteWidth : definiteHeight,
                row ? definiteHeight : definiteWidth);
        }

        float lineMain = 0, lineCross = 0, totalCross = 0, maxMain = 0;
        int count = 0, lines = 0;
        float cell = style.Layout == LayoutMode.Grid
            ? Positive((inner.Width - style.Gap * (style.GridColumns - 1)) / style.GridColumns)
            : 0;
        for (int childIndex = Flow(parentIndex, First(parentIndex)); childIndex >= 0; childIndex = Flow(parentIndex, Next(parentIndex, childIndex)))
        {
            var childStyle = ResolveStyle(_nodes[childIndex].Style, inner);
            Size size = style.Layout == LayoutMode.Grid
                ? Measure(
                childIndex,
                inner,
                Clamp(Positive(cell - childStyle.Margin.Horizontal), childStyle.MinWidth, childStyle.MaxWidth))
                : MeasureOverlay(parentIndex, childIndex, inner, definiteWidth, definiteHeight);
            if (style.Layout == LayoutMode.Overlay)
            {
                maxMain = Math.Max(maxMain, size.Width + childStyle.Margin.Horizontal);
                totalCross = Math.Max(totalCross, size.Height + childStyle.Margin.Vertical);
                continue;
            }

            float cross = size.Height + childStyle.Margin.Vertical;
            bool newLine = count == style.GridColumns;
            if (newLine)
            {
                maxMain = Math.Max(maxMain, lineMain);
                totalCross += lineCross + (lines++ > 0 ? style.LineGap : 0);
                count = 0;
                lineMain = lineCross = 0;
            }

            lineMain += (count++ > 0 ? style.Gap : 0) + cell;
            lineCross = Math.Max(lineCross, cross);
        }

        // Overlay leaves the line accumulators at zero, so the grid tail below is a no-op for it.
        maxMain = Math.Max(maxMain, lineMain);
        totalCross += lineCross + (lines > 0 ? style.LineGap : 0);
        return new(maxMain, totalCross);
    }

    private Size MeasureFlowChildren(int parentIndex, Size inner, bool definiteMain, bool definiteCross)
    {
        Style style = _nodes[parentIndex].Style;
        bool row = style.Layout == LayoutMode.Row;
        float limit = Main(inner, row), maxMain = 0, totalCross = 0;
        int start = Flow(parentIndex, First(parentIndex)), lines = 0;
        while (start >= 0)
        {
            int end = start, count = 0;
            float used = 0, cross = 0;
            while (end >= 0)
            {
                Size desired = MeasureFlow(parentIndex, end, inner, row, -1, definiteCross);
                float main = Main(desired, row), outer = main + MainMargin(ResolveStyle(_nodes[end].Style, inner), row);
                if (style.Wrap && count > 0 && used + style.Gap + outer > limit)
                {
                    break;
                }

                _nodes[end].BaseMain = _nodes[end].FlexMain = main;
                _nodes[end].Frozen = false;
                InitializeMargins(end, inner, row);
                used += (count++ > 0 ? style.Gap : 0) + outer;
                end = Flow(parentIndex, Next(parentIndex, end));
            }

            if (definiteMain)
            {
                Distribute(parentIndex, start, end, limit - used, row, inner);
            }

            used = Math.Max(0, count - 1) * style.Gap;
            for (int childIndex = start; childIndex != end; childIndex = Flow(parentIndex, Next(parentIndex, childIndex)))
            {
                float main = _nodes[childIndex].FlexMain;
                Size desired = main == _nodes[childIndex].BaseMain
                    ? _nodes[childIndex].Desired
                    : MeasureFlow(parentIndex, childIndex, inner, row, main, definiteCross);
                used += main + _nodes[childIndex].Before + _nodes[childIndex].After;
                cross = Math.Max(cross, Cross(desired, row) + CrossMargin(ResolveStyle(_nodes[childIndex].Style, inner), row));
            }

            maxMain = Math.Max(maxMain, used);
            totalCross += cross + (lines++ > 0 ? style.LineGap : 0);
            start = end;
        }

        return row ? new(maxMain, totalCross) : new(totalCross, maxMain);
    }

    private void Arrange(int nodeIndex, Rect rect)
    {
        ref Node node = ref _nodes[nodeIndex];
        // Origin changes do not require laying out descendants: their coordinates are local.
        node.Rect = node.Style.Hidden ? default : rect;
        Size size = new(rect.Width, rect.Height);
        Size parentSize = node.Parent < 0 ? size : _nodes[node.Parent].ContentSize;
        if (!node.ArrangeDirty && node.HasArrange && node.ArrangedSize == size
            && (!DependsOnParent(node.Style.Padding) || node.ArrangedParentSize == parentSize))
        {
            _arrangeHits++;
            return;
        }

        _arranged++;
        // A root's percentage padding resolves against this call's viewport, not last frame's size.
        var style = ResolveStyle(node.Style, parentSize);
        if (style.Hidden)
        {
            ClearHidden(nodeIndex);
            node.ArrangedSize = size;
            node.HasArrange = true;
            node.ArrangeDirty = false;
            return;
        }

        Size inner = new(Positive(rect.Width - style.Padding.Horizontal), Positive(rect.Height - style.Padding.Vertical));
        node.ContentSize = inner;
        node.ArrangedParentSize = parentSize;
        if (style.Layout == LayoutMode.Grid)
        {
            ArrangeGrid(nodeIndex, style, inner);
        }
        else if (style.Layout == LayoutMode.Overlay)
        {
            ArrangeOverlay(nodeIndex, style, inner);
        }
        else
        {
            ArrangeFlow(nodeIndex, style, inner);
        }

        for (int childIndex = node.First; childIndex >= 0; childIndex = _nodes[childIndex].Next)
        {
            if (_nodes[childIndex].Style.Hidden)
            {
                ClearHidden(childIndex);
            }
            else if (_nodes[childIndex].Style.Position == Position.Absolute)
            {
                ArrangeAbsolute(childIndex, inner, style.Padding);
            }
        }

        // Stamped only after a successful pass: a throwing measure callback must not leave a
        // half-arranged node looking cached.
        node.ArrangedSize = size;
        node.HasArrange = true;
        node.ArrangeDirty = false;
    }

    private void ClearHidden(int nodeIndex)
    {
        // An unrelated edit must not walk an already-cleared hidden subtree again.
        // Structural/content edits propagate ArrangeDirty through its ancestors.
        if (!_nodes[nodeIndex].HasArrange && !_nodes[nodeIndex].ArrangeDirty)
        {
            return;
        }

        // Invalidate cached arrangement when an ancestor hides. Showing it must restore descendants.
        _nodes[nodeIndex].Rect = default;
        _nodes[nodeIndex].HasArrange = false;
        for (int childIndex = _nodes[nodeIndex].First; childIndex >= 0; childIndex = _nodes[childIndex].Next)
        {
            ClearHidden(childIndex);
        }

        _nodes[nodeIndex].ArrangeDirty = false;
    }

    private void ArrangeFlow(int parentIndex, in Resolved style, Size inner)
    {
        bool row = style.Layout == LayoutMode.Row;
        float limit = Main(inner, row), crossOffset = 0;
        int start = Flow(parentIndex, First(parentIndex));
        while (start >= 0)
        {
            int end = start, count = 0;
            float used = 0;
            while (end >= 0)
            {
                Size desired = MeasureFlow(parentIndex, end, inner, row);
                float main = Main(desired, row);
                float outer = main + MainMargin(ResolveStyle(_nodes[end].Style, inner), row);
                if (style.Wrap && count > 0 && used + style.Gap + outer > limit)
                {
                    break;
                }

                _nodes[end].BaseMain = _nodes[end].FlexMain = main;
                _nodes[end].Frozen = false;
                InitializeMargins(end, inner, row);
                used += (count++ > 0 ? style.Gap : 0) + outer;
                end = Flow(parentIndex, Next(parentIndex, end));
            }

            Distribute(parentIndex, start, end, limit - used, row, inner);
            // Non-wrapping alignment uses the container's actual cross space, even with overflow.
            // Only wrapped lines size themselves to their tallest item.
            float crossSize = style.Wrap ? 0 : Cross(inner, row);
            used = Math.Max(0, count - 1) * style.Gap;
            for (int childIndex = start; childIndex != end; childIndex = Flow(parentIndex, Next(parentIndex, childIndex)))
            {
                float main = _nodes[childIndex].FlexMain;
                if (main != _nodes[childIndex].BaseMain)
                {
                    MeasureFlow(parentIndex, childIndex, inner, row, main);
                }

                if (style.Wrap)
                {
                    crossSize = Math.Max(
                        crossSize,
                        Cross(_nodes[childIndex].Desired, row) + CrossMargin(ResolveStyle(_nodes[childIndex].Style, inner), row));
                }

                used += main + _nodes[childIndex].Before + _nodes[childIndex].After;
            }

            Spacing(style.Justify, Positive(limit - used), count, out float offset, out float extraGap);
            for (int childIndex = start; childIndex != end; childIndex = Flow(parentIndex, Next(parentIndex, childIndex)))
            {
                var childStyle = ResolveStyle(_nodes[childIndex].Style, inner);
                Size desired = _nodes[childIndex].Desired;
                float main = _nodes[childIndex].FlexMain;
                Align align = childStyle.AlignSelf ?? style.AlignItems;
                float cross = Cross(desired, row);
                var crossFlex = FlexCross(childStyle, row, crossSize, cross, align, !style.Wrap);
                cross = crossFlex.Size;
                float crossBefore = row ? childStyle.Margin.Top : childStyle.Margin.Left;
                float mainBefore = _nodes[childIndex].Before;
                float crossPos = crossOffset + crossBefore + crossFlex.Before + Aligned(align, crossFlex.Remaining);
                float mainPos = offset + mainBefore;
                Arrange(
                    childIndex,
                    row
                    ? new Rect(style.Padding.Left + mainPos, style.Padding.Top + crossPos, main, cross)
                    : new Rect(style.Padding.Left + crossPos, style.Padding.Top + mainPos, cross, main));
                offset += main + _nodes[childIndex].Before + _nodes[childIndex].After + style.Gap + extraGap;
            }

            crossOffset += crossSize + style.LineGap;
            start = end;
        }
    }

    private void Distribute(int parentIndex, int start, int end, float remaining, bool row, Size inner)
    {
        bool grow = remaining > 0;
        // Freeze bound violations, then redistribute their unused share. Worst case O(k squared) per line.
        // ordinary unconstrained rows take one pass. No temporary lists or per-layout buffers.
        while (Math.Abs(remaining) > .0001f)
        {
            double weight = 0;
            for (int childIndex = start; childIndex != end; childIndex = Flow(parentIndex, Next(parentIndex, childIndex)))
            {
                Length length = row ? _nodes[childIndex].Style.Width : _nodes[childIndex].Style.Height;
                if (!_nodes[childIndex].Frozen)
                {
                    weight += grow ? length.Grow : (double)length.Shrink * _nodes[childIndex].BaseMain;
                }

                if (grow)
                {
                    weight += MarginGrowMain(_nodes[childIndex].Style, row);
                }
            }

            if (weight <= 0)
            {
                break;
            }

            float consumed = 0;
            bool frozen = false;
            for (int childIndex = start; childIndex != end; childIndex = Flow(parentIndex, Next(parentIndex, childIndex)))
            {
                ref Node node = ref _nodes[childIndex];
                if (grow)
                {
                    float before = (float)(remaining * (row ? node.Style.Margin.Left.Grow : node.Style.Margin.Top.Grow) / weight);
                    float after = (float)(remaining * (row ? node.Style.Margin.Right.Grow : node.Style.Margin.Bottom.Grow) / weight);
                    node.Before += before;
                    node.After += after;
                    consumed += before + after;
                }

                if (node.Frozen)
                {
                    continue;
                }

                Length length = row ? node.Style.Width : node.Style.Height;
                double part = grow ? length.Grow : (double)length.Shrink * node.BaseMain;
                float proposed = node.FlexMain + (float)(remaining * part / weight);
                var bounds = ResolveStyle(node.Style, inner);
                float clamped = Clamp(proposed, row ? bounds.MinWidth : bounds.MinHeight, row ? bounds.MaxWidth : bounds.MaxHeight);
                if (clamped != proposed)
                {
                    node.Frozen = true;
                    frozen = true;
                }

                consumed += clamped - node.FlexMain;
                node.FlexMain = clamped;
            }

            remaining -= consumed;
            if (!frozen)
            {
                break;
            }
        }
    }

    /// <summary>Measure one flow child. While the parent's cross axis is still intrinsic, a stretching
    /// child resolves to its own natural size: stretching against the provisional available space
    /// would let an empty spacer inflate an auto-sized container to the whole viewport.</summary>
    private Size MeasureFlow(int parentIndex, int childIndex, Size inner, bool row, float main = -1, bool definiteCross = true)
    {
        var style = ResolveStyle(_nodes[childIndex].Style, inner);
        Style parent = _nodes[parentIndex].Style;
        Length crossLength = row ? style.Height : style.Width;
        float cross = -1;
        if (definiteCross && !parent.Wrap
            && ((style.AlignSelf ?? parent.AlignItems) == Align.Stretch || crossLength.Grow > 0))
        {
            float basis = crossLength.AutoFactor > 0
                ? Cross(Measure(childIndex, inner, row ? main : -1, row ? -1 : main), row)
                : Clamp(
                crossLength.Resolve(Cross(inner, row), 0),
                row ? style.MinHeight : style.MinWidth,
                row ? style.MaxHeight : style.MaxWidth);
            cross = FlexCross(style, row, Cross(inner, row), basis, style.AlignSelf ?? parent.AlignItems).Size;
        }

        return Measure(childIndex, inner, row ? main : cross, row ? cross : main);
    }

    private static (float Size, float Before, float Remaining) FlexCross(in Resolved style, bool row, float available, float basis, Align align, bool resolved = false)
    {
        float beforeGrow = row ? style.MarginGrow.Top : style.MarginGrow.Left;
        float afterGrow = row ? style.MarginGrow.Bottom : style.MarginGrow.Right;
        float grow = row ? style.Height.Grow : style.Width.Grow;
        float room = Positive(available - CrossMargin(style, row) - basis);
        float total = beforeGrow + afterGrow + grow;
        float size = resolved
            ? basis
            : align == Align.Stretch
            ? available - CrossMargin(style, row)
            : basis + (total > 0 ? room * grow / total : 0);
        size = Clamp(Positive(size), row ? style.MinHeight : style.MinWidth, row ? style.MaxHeight : style.MaxWidth);
        float remaining = available - CrossMargin(style, row) - size;
        float before = beforeGrow + afterGrow > 0 ? Positive(remaining) * beforeGrow / (beforeGrow + afterGrow) : 0;
        return (size, before, beforeGrow + afterGrow > 0 ? Math.Min(0, remaining) : remaining);
    }

    private void ArrangeGrid(int parentIndex, in Resolved style, Size inner)
    {
        float cell = Positive((inner.Width - (style.GridColumns - 1) * style.Gap) / style.GridColumns), y = 0;
        int start = Flow(parentIndex, First(parentIndex));
        while (start >= 0)
        {
            int end = start, count = 0;
            float lineHeight = 0;
            while (end >= 0 && count++ < style.GridColumns)
            {
                var childStyle = ResolveStyle(_nodes[end].Style, inner);
                Size desired = Measure(
                    end,
                    inner,
                    Clamp(Positive(cell - childStyle.Margin.Horizontal), childStyle.MinWidth, childStyle.MaxWidth));
                lineHeight = Math.Max(lineHeight, desired.Height + childStyle.Margin.Vertical);
                end = Flow(parentIndex, Next(parentIndex, end));
            }

            int column = 0;
            for (int childIndex = start; childIndex != end; childIndex = Flow(parentIndex, Next(parentIndex, childIndex)))
            {
                var childStyle = ResolveStyle(_nodes[childIndex].Style, inner);
                Align align = childStyle.AlignSelf ?? style.AlignItems;
                Size desired = _nodes[childIndex].Desired;
                float height = align == Align.Stretch || childStyle.Height.Grow > 0
                    ? Clamp(Positive(lineHeight - childStyle.Margin.Vertical), childStyle.MinHeight, childStyle.MaxHeight)
                    : desired.Height;
                Arrange(
                    childIndex,
                    new Rect(
                    style.Padding.Left + column++ * (cell + style.Gap) + childStyle.Margin.Left,
                    style.Padding.Top + y + childStyle.Margin.Top + Aligned(align, lineHeight - height - childStyle.Margin.Vertical),
                    desired.Width,
                    height));
            }

            y += lineHeight + style.LineGap;
            start = end;
        }
    }

    private void ArrangeOverlay(int parentIndex, in Resolved style, Size inner)
    {
        for (int childIndex = Flow(parentIndex, First(parentIndex)); childIndex >= 0; childIndex = Flow(parentIndex, Next(parentIndex, childIndex)))
        {
            var childStyle = ResolveStyle(_nodes[childIndex].Style, inner);
            Align align = childStyle.AlignSelf ?? style.AlignItems;
            Size size = MeasureOverlay(parentIndex, childIndex, inner);
            Arrange(
                childIndex,
                new Rect(
                style.Padding.Left + childStyle.Margin.Left + Aligned(align, inner.Width - size.Width - childStyle.Margin.Horizontal),
                style.Padding.Top + childStyle.Margin.Top + Aligned(align, inner.Height - size.Height - childStyle.Margin.Vertical),
                size.Width,
                size.Height));
        }
    }

    private Size MeasureOverlay(int parentIndex, int childIndex, Size inner, bool definiteWidth = true, bool definiteHeight = true)
    {
        var childStyle = ResolveStyle(_nodes[childIndex].Style, inner);
        Align align = childStyle.AlignSelf ?? _nodes[parentIndex].Style.AlignItems;
        float width = definiteWidth && (align == Align.Stretch || childStyle.Width.Grow > 0)
            ? Clamp(Positive(inner.Width - childStyle.Margin.Horizontal), childStyle.MinWidth, childStyle.MaxWidth)
            : -1;
        float height = definiteHeight && (align == Align.Stretch || childStyle.Height.Grow > 0)
            ? Clamp(Positive(inner.Height - childStyle.Margin.Vertical), childStyle.MinHeight, childStyle.MaxHeight)
            : -1;
        return Measure(childIndex, inner, width, height);
    }

    private void ArrangeAbsolute(int childIndex, Size inner, Edges padding)
    {
        var style = ResolveStyle(_nodes[childIndex].Style, inner);
        float width = style.Left.HasValue && style.Right.HasValue
            ? Clamp(
            Positive(inner.Width - style.Left.Value - style.Right.Value - style.Margin.Horizontal),
            style.MinWidth,
            style.MaxWidth)
            : -1;
        float height = style.Top.HasValue && style.Bottom.HasValue
            ? Clamp(
            Positive(inner.Height - style.Top.Value - style.Bottom.Value - style.Margin.Vertical),
            style.MinHeight,
            style.MaxHeight)
            : -1;
        if (width < 0 && style.Width.Grow > 0)
        {
            width = FlexCross(
                style,
                false,
                inner.Width,
                Clamp(style.Width.Resolve(inner.Width, 0), style.MinWidth, style.MaxWidth),
                Align.Start).Size;
        }

        if (height < 0 && style.Height.Grow > 0)
        {
            height = FlexCross(
                style,
                true,
                inner.Height,
                Clamp(style.Height.Resolve(inner.Height, 0), style.MinHeight, style.MaxHeight),
                Align.Start).Size;
        }

        Size size = Measure(childIndex, inner, width, height);
        float x = style.Left.HasValue
            ? style.Left.Value + style.Margin.Left
            : style.Right.HasValue
            ? inner.Width - style.Right.Value - size.Width - style.Margin.Right
            : style.Margin.Left + FlexCross(style, false, inner.Width, size.Width, Align.Start, true).Before;
        float y = style.Top.HasValue
            ? style.Top.Value + style.Margin.Top
            : style.Bottom.HasValue
            ? inner.Height - style.Bottom.Value - size.Height - style.Margin.Bottom
            : style.Margin.Top + FlexCross(style, true, inner.Height, size.Height, Align.Start, true).Before;
        Arrange(childIndex, new Rect(padding.Left + x, padding.Top + y, size.Width, size.Height));
    }

    private static float Aligned(Align align, float space) => align == Align.Center ? space * .5f : align == Align.End ? space : 0;
    private static void Spacing(Justify justify, float space, int count, out float offset, out float gap)
    {
        offset = gap = 0;
        switch (justify)
        {
            case Justify.Center:
                offset = space * .5f;
                break;
            case Justify.End:
                offset = space;
                break;
            case Justify.SpaceBetween:
                gap = count > 1 ? space / (count - 1) : 0;
                break;
            case Justify.SpaceAround:
                gap = count > 0 ? space / count : 0;
                offset = gap * .5f;
                break;
            case Justify.SpaceEvenly:
                gap = space / (count + 1);
                offset = gap;
                break;
        }
    }
}
