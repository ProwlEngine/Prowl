using System;
using System.Collections.Generic;

using Prowl.PaperUI.LayoutEngine;
using Prowl.Scribe;

using LayoutBackend = Prowl.Scaffold;

namespace Prowl.PaperUI;

public partial class Paper
{
    private readonly LayoutBackend.LayoutTree _layoutTree = new(1024);
    private readonly Dictionary<int, LayoutState> _layoutStates = new();
    private readonly List<int> _layoutIds = new();
    private long _layoutEpoch;
    private static long s_layoutEpoch;
    /// <summary>Work counts for the last layout (excludes style resolution, copying and rendering).</summary>
    public LayoutStatistics LayoutStatistics { get; private set; }

    /// <summary>Invalidate the current element's external content, for use inside its Enter scope
    /// alongside <see cref="Draw(Action{Quill.Canvas, Vector.Rect})"/> and the element storage helpers.</summary>
    public void MarkLayoutDirty() => MarkLayoutDirty(CurrentParent.Data.ID);

    /// <summary>Invalidate external content for a persistent element ID. Unknown/removed IDs are ignored.</summary>
    public void MarkLayoutDirty(int elementId)
    {
        if (_layoutStates.TryGetValue(elementId, out var state) && _layoutTree.IsAlive(state.Node))
        {
            _layoutTree.MarkDirty(state.Node);
        }
    }

    /// <summary>Invalidate external content throughout the current element's retained subtree.</summary>
    public void MarkSubtreeLayoutDirty() => MarkSubtreeLayoutDirty(CurrentParent.Data.ID);

    /// <summary>Invalidate external content throughout an element's retained subtree.</summary>
    public void MarkSubtreeLayoutDirty(int elementId)
    {
        if (_layoutStates.TryGetValue(elementId, out var state) && _layoutTree.IsAlive(state.Node))
        {
            _layoutTree.MarkSubtreeDirty(state.Node);
        }
    }

    /// <summary>Invalidate all intrinsic geometry, for example after changing external font metrics.</summary>
    public void MarkAllLayoutDirty() => MarkSubtreeLayoutDirty(RootElement.Data.ID);

    private sealed class LayoutState
    {
        public readonly Paper Owner;
        public readonly LayoutBackend.NodeId Node;
        public int Index;
        public long Seen, Revision;
        public TextMetrics Metrics;
        public bool HasSizer;
        public LayoutBackend.Size? Intrinsic;
        public long StyleRevision;
        public LayoutControls Controls;
        public ElementStyle? StyleSource;
        public LayoutState(Paper owner, LayoutBackend.NodeId node)
        {
            Owner = owner;
            Node = node;
        }
    }

    private readonly record struct LayoutControls(
        LayoutType Layout,
        PositionType Position,
        bool Visible,
        bool Wrap,
        LayoutAlignment? Align,
        LayoutAlignment? Self,
        LayoutJustification? Justify,
        int Columns,
        bool Reverse);
    private readonly record struct TextMetrics(
        string? Text,
        FontFile? Font,
        FontFile? Bold,
        FontFile? Italic,
        FontFile? BoldItalic,
        FontFile? Mono,
        FontStyle FontStyle,
        bool Rich,
        bool Markdown,
        bool Truncate,
        TextWrapMode Wrap,
        TextAlignment Alignment,
        float FontSize,
        float LineHeight,
        float LetterSpacing,
        float WordSpacing,
        int TabSize,
        FontQuality Quality,
        float Scale);
    private TextMetrics Metrics(in ElementData data)
    {
        if (string.IsNullOrEmpty(data.Paragraph))
        {
            return default;
        }

        var style = data._elementStyle;
        return new(
            data.Paragraph,
            data.Font,
            data.FontBold,
            data.FontItalic,
            data.FontBoldItalic,
            data.FontMono,
            data.FontStyle,
            data.IsRichText,
            data.IsMarkdown,
            data.Truncate,
            data.WrapMode,
            data.TextAlignment,
            style.GetFontSize(),
            style.GetLineHeight(),
            style.GetLetterSpacing(),
            style.GetWordSpacing(),
            style.GetTabSize(),
            style.GetTextQuality(),
            Canvas.FramebufferScale);
    }

    private static readonly LayoutBackend.MeasureContent s_measureLayout = (_, available, context) =>
    {
        var state = (LayoutState)context!;
        ref var data = ref state.Owner.GetElementData(state.Index);
        if (data._intrinsicSize.HasValue)
        {
            return data._intrinsicSize.Value;
        }

        var custom = data.ContentSizer?.Invoke(available.Width, available.Height);
        if (custom.HasValue)
        {
            return new LayoutBackend.Size(custom.Value.Item1, custom.Value.Item2);
        }

        var text = data.ProcessText(state.Owner, available.Width);
        return new LayoutBackend.Size(text.X, text.Y);
    };
    // Called after Paper has resolved this frame's animated/inherited style values.
    internal void ComputeLayout()
    {
        long started = _devTools.DeepProfiling ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
        _layoutEpoch = System.Threading.Interlocked.Increment(ref s_layoutEpoch);
        var root = SubmitLayout(RootElement.Index, default, default);
        for (int i = _layoutIds.Count - 1; i >= 0; i--)
        {
            int id = _layoutIds[i];
            var state = _layoutStates[id];
            if (state.Seen == _layoutEpoch)
            {
                continue;
            }

            if (_layoutTree.IsAlive(state.Node))
            {
                _layoutTree.Remove(state.Node);
            }

            _layoutStates.Remove(id);
            int last = _layoutIds.Count - 1;
            _layoutIds[i] = _layoutIds[last];
            _layoutIds.RemoveAt(last);
        }

        var style = RootElement.Data._elementStyle;
        float width = style.GetUnit(GuiProp.Width).Px, height = style.GetUnit(GuiProp.Height).Px;
        var statistics = _layoutTree.Layout(root, new LayoutBackend.Size(width, height));
        LayoutStatistics = new LayoutStatistics(
            statistics.MeasuredNodes,
            statistics.ArrangedNodes,
            statistics.MeasureCacheHits,
            statistics.ArrangeCacheHits);
        CopyLayout(RootElement.Index, 0, 0, false, width, height);
        if (_devTools.DeepProfiling)
        {
            _devTools.RecordLayout(0, -1, System.Diagnostics.Stopwatch.GetTimestamp() - started);
        }
    }

    private LayoutBackend.NodeId SubmitLayout(int index, LayoutBackend.NodeId parent, LayoutBackend.NodeId after)
    {
        ref var data = ref GetElementData(index);
        if (!_layoutStates.TryGetValue(data.ID, out var state))
        {
            state = new LayoutState(this, _layoutTree.Create(LayoutBackend.Style.Default));
            _layoutStates.Add(data.ID, state);
            _layoutIds.Add(data.ID);
            _layoutTree.SetMeasure(state.Node, s_measureLayout, state);
        }

        if (state.Seen == _layoutEpoch)
        {
            throw new InvalidOperationException($"Duplicate Paper layout ID {data.ID}.");
        }

        state.Seen = _layoutEpoch;
        state.Index = index;
        if (parent != default)
        {
            _layoutTree.PlaceAfter(state.Node, parent, after);
        }

        SynchronizeLayoutStyle(ref data, state);
        SynchronizeContentMeasurement(ref data, state);


        LayoutBackend.NodeId tail = default;
        foreach (int child in data.ChildIndices)
        {
            tail = SubmitLayout(child, state.Node, tail);
        }

        return state.Node;
    }

    private void SynchronizeLayoutStyle(ref ElementData data, LayoutState state)
    {
        long styleRevision = data._elementStyle.GetLayoutRevision(_layoutEpoch);
        var controls = new LayoutControls(
            data.LayoutType,
            data.PositionType,
            data.Visible,
            data.ContentWrap,
            data._alignItems,
            data._alignSelf,
            data._justify,
            data._gridColumns,
            data._reverseLayout);
        if (!ReferenceEquals(state.StyleSource, data._elementStyle)
            || state.StyleRevision != styleRevision
            || state.Controls != controls)
        {
            _layoutTree.SetStyle(state.Node, MapLayout(ref data));
            state.StyleRevision = styleRevision;
            state.Controls = controls;
            state.StyleSource = data._elementStyle;
        }
    }

    private void SynchronizeContentMeasurement(ref ElementData data, LayoutState state)
    {
        var metrics = Metrics(data);
        bool hasSizer = data.ContentSizer != null;
        bool textChanged = metrics != state.Metrics;
        // A cached sizer is keyed on its revision, not on the delegate: declaration code
        // usually hands us a freshly allocated closure every frame.
        if (textChanged
            || (hasSizer && !data._cacheContentSizer)
            || state.HasSizer != hasSizer
            || state.Intrinsic != data._intrinsicSize
            || state.Revision != data._contentRevision)
        {
            _layoutTree.MarkDirty(state.Node);
            if (textChanged && data.IsRichText)
            {
                SetElementStorageById<Quill.Canvas.QuillRichText?>(data.ID, RichTextLayoutKey, null);
            }
        }

        state.Metrics = metrics;
        state.HasSizer = hasSizer;
        state.Intrinsic = data._intrinsicSize;
        state.Revision = data._contentRevision;
    }

    private LayoutBackend.Style MapLayout(ref ElementData data)
    {
        var style = data._elementStyle;
        UnitValue left = Margin(style, GuiProp.Left);
        UnitValue top = Margin(style, GuiProp.Top);
        UnitValue right = Margin(style, GuiProp.Right);
        UnitValue bottom = Margin(style, GuiProp.Bottom);
        UnitValue widthValue = style.GetUnit(GuiProp.Width), heightValue = style.GetUnit(GuiProp.Height);
        if (data.ParentIndex >= 0 && data.PositionType == PositionType.ParentDirected)
        {
            ref var parentData = ref GetElementData(data.ParentIndex);
            if (parentData.ContentWrap && parentData._justify == LayoutJustification.Fill)
            {
                if (parentData.LayoutType == LayoutType.Row)
                {
                    widthValue.Grow = 1;
                }
                else
                {
                    heightValue.Grow = 1;
                }
            }
        }

        bool absolute = data.PositionType == PositionType.SelfDirected;
        return LayoutBackend.Style.Default with
        {
            Width = widthValue.ToLayoutLength(),
            Height = heightValue.ToLayoutLength(),
            MinWidth = Bound(style.GetUnit(GuiProp.MinWidth), "MinWidth"),
            MaxWidth = Bound(style.GetUnit(GuiProp.MaxWidth), "MaxWidth"),
            MinHeight = Bound(style.GetUnit(GuiProp.MinHeight), "MinHeight"),
            MaxHeight = Bound(style.GetUnit(GuiProp.MaxHeight), "MaxHeight"),
            Padding = new LayoutBackend.LengthEdges(
                Bound(style.GetUnit(GuiProp.PaddingLeft), "PaddingLeft"),
                Bound(style.GetUnit(GuiProp.PaddingTop), "PaddingTop"),
                Bound(style.GetUnit(GuiProp.PaddingRight), "PaddingRight"),
                Bound(style.GetUnit(GuiProp.PaddingBottom), "PaddingBottom")),
            Margin = new LayoutBackend.LengthEdges(
                left.ToLayoutLength(),
                top.ToLayoutLength(),
                right.ToLayoutLength(),
                bottom.ToLayoutLength()),
            Layout = ToLayoutMode(data.LayoutType),
            Wrap = data.ContentWrap,
            Reverse = data._reverseLayout,
            Hidden = !data.Visible,
            GridColumns = data._gridColumns == 0 ? 1 : data._gridColumns,
            AlignItems = ToAlign(data._alignItems ?? LayoutAlignment.Start),
            AlignSelf = data._alignSelf.HasValue ? ToAlign(data._alignSelf.Value) : null,
            Justify = ToJustify(data._justify),
            Gap = style.GetGap(),
            LineGap = style.GetLineGap(),
            Position = absolute ? LayoutBackend.Position.Absolute : LayoutBackend.Position.Flow,
            Left = Anchor(style, GuiProp.AnchorLeft),
            Right = Anchor(style, GuiProp.AnchorRight),
            Top = Anchor(style, GuiProp.AnchorTop),
            Bottom = Anchor(style, GuiProp.AnchorBottom),
            AspectRatio = Math.Max(0, style.GetAspectRatio())
        };
    }

    /// <summary>Anchors are pixel offsets from the parent's content box. Auto means unanchored.</summary>
    private static float? Anchor(ElementStyle style, GuiProp property)
    {
        UnitValue value = style.GetUnit(property);
        return value.IsAuto ? null : Bound(value, property.ToString()).Px;
    }

    private static LayoutBackend.Align ToAlign(LayoutAlignment alignment) => alignment switch
    {
        LayoutAlignment.Center => LayoutBackend.Align.Center,
        LayoutAlignment.End => LayoutBackend.Align.End,
        LayoutAlignment.Stretch => LayoutBackend.Align.Stretch,
        _ => LayoutBackend.Align.Start
    };

    // Fill is a Paper-level spelling: the backend has no such mode, MapLayout grows the children.
    private static LayoutBackend.Justify ToJustify(LayoutJustification? justify) => justify switch
    {
        LayoutJustification.Center => LayoutBackend.Justify.Center,
        LayoutJustification.End => LayoutBackend.Justify.End,
        LayoutJustification.SpaceBetween => LayoutBackend.Justify.SpaceBetween,
        LayoutJustification.SpaceAround => LayoutBackend.Justify.SpaceAround,
        LayoutJustification.SpaceEvenly => LayoutBackend.Justify.SpaceEvenly,
        _ => LayoutBackend.Justify.Start
    };

    private static LayoutBackend.LayoutMode ToLayoutMode(LayoutType layout) => layout switch
    {
        LayoutType.Row => LayoutBackend.LayoutMode.Row,
        LayoutType.Grid => LayoutBackend.LayoutMode.Grid,
        LayoutType.Overlay => LayoutBackend.LayoutMode.Overlay,
        _ => LayoutBackend.LayoutMode.Column
    };

    /// <summary>Size bounds and padding carry pixels and percentages. Intrinsic or flexible values
    /// belong on a size or a spacer element, so they are rejected rather than silently dropped.</summary>
    private static LayoutBackend.Length Bound(UnitValue value, string property)
    {
        if (value.HasGrow || value.HasAuto || value.HasShrink)
        {
            throw new NotSupportedException($"{property} supports pixel and percent values. Express intrinsic or flexible bounds and padding as sizes or spacer elements.");
        }

        return value.ToLayoutLength();
    }

    /// <summary>An element's own margin. Auto means none: the parent insets with padding and
    /// separates siblings with Gap, so there is nothing to inherit from it.</summary>
    private static UnitValue Margin(ElementStyle style, GuiProp property)
    {
        UnitValue value = style.GetUnit(property);
        return value.IsAuto ? UnitValue.ZeroPixels : value;
    }

    private void CopyLayout(int index, float x, float y, bool hidden, float screenWidth, float screenHeight)
    {
        ref var data = ref GetElementData(index);
        hidden |= !data.Visible;
        var rect = hidden ? default : _layoutTree.GetRect(_layoutStates[data.ID].Node);
        data.RelativeX = rect.X;
        data.RelativeY = rect.Y;
        data.LayoutWidth = rect.Width;
        data.LayoutHeight = rect.Height;
        data.X = hidden ? 0 : x + rect.X;
        data.Y = hidden ? 0 : y + rect.Y;
        if (!hidden && data._clampToScreen)
        {
            float clampedX = Math.Max(4, Math.Min(screenWidth - rect.Width - 4, data.X));
            float clampedY = Math.Max(4, Math.Min(screenHeight - rect.Height - 4, data.Y));
            data.RelativeX += clampedX - data.X;
            data.RelativeY += clampedY - data.Y;
            data.X = clampedX;
            data.Y = clampedY;
        }

        foreach (int child in data.ChildIndices)
        {
            CopyLayout(child, data.X, data.Y, hidden, screenWidth, screenHeight);
        }
    }
}
