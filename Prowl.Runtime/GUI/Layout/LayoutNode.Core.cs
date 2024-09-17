// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Runtime.GUI.Layout;

public partial class LayoutNode
{
    private const double ScrollBarSize = 10;
    private const double MinScrollBarSize = 17;

    public struct PostLayoutData(LayoutNode node)
    {
        internal LayoutNode _node = node;

        public readonly bool IsVScrollVisible => ShowVScroll && ContentRect.height > Rect.height;
        public readonly bool IsHScrollVisible => ShowHScroll && ContentRect.width > Rect.width;

        public readonly double VScrollWidth => IsVScrollVisible ? ScrollBarSize : 0;
        public readonly double HScrollHeight => IsHScrollVisible ? ScrollBarSize : 0;

        public readonly Rect InnerRect => new(GlobalContentPosition, new(GlobalContentWidth, GlobalContentHeight));
        public readonly Rect InnerRect_NoScroll => new(GlobalContentPosition, new(GlobalContentWidth + VScrollWidth, GlobalContentHeight + HScrollHeight));
        public readonly Rect Rect => new(GlobalPosition, new(Scale.x, Scale.y));
        public readonly Rect OuterRect => new(GlobalPosition - Margins.TopLeft, new(Scale.x + Margins.Horizontal, Scale.y + Margins.Vertical));

        public readonly Vector2 GlobalPosition
        {
            get
            {
                if (_node == null)
                    return Vector2.zero;
                Vector2 globalPosition = _node.Parent != null ? _node.Parent.LayoutData.GlobalContentPosition + Position + Margins.TopLeft : Position;
                if (_node.Parent != null && !_node._ignore)
                    globalPosition -= new Vector2(_node.Parent.HScroll, _node.Parent.VScroll);
                return globalPosition;
            }
        }

        public readonly Vector2 GlobalContentPosition => GlobalPosition + Paddings.TopLeft;
        public readonly double GlobalContentWidth => Scale.x - Paddings.Horizontal - VScrollWidth;
        public readonly double GlobalContentHeight => Scale.y - Paddings.Vertical - HScrollHeight;

        // Cached
        //public Vector2 Scale = node._data.Scale;
        public Vector2 Scale = node._data.Scale;
        public Vector2 MaxScale = node._data.MaxScale;
        public Spacing Margins = node._data.Margins;
        public Spacing Paddings = node._data.Paddings;
        public Vector2 Position = node._data.Position;
        public Rect ContentRect = node._data.ContentRect;

        // Keep the Scrolls cached
        public double VScroll = node.VScroll;
        public double HScroll = node.HScroll;
        public readonly bool ShowVScroll = node._showVScroll;
        public readonly bool ShowHScroll = node._showHScroll;
    }

    public bool HasLayoutData => _data._node == this;
    public PostLayoutData LayoutData
    {
        get
        {
            return _data;
        }
        internal set
        {
            _data = value;
        }
    }

    public Gui Gui { get; private set; }
    public LayoutNode Parent { get; internal set; }
    public double VScroll
    {
        get => _data.VScroll;
        set => _data.VScroll = value;
    }
    public double HScroll
    {
        get => _data.HScroll;
        set => _data.HScroll = value;
    }
    public ulong ID { get; private set; } = 0;

    private PostLayoutData _data;
    private Offset _positionX = Offset.Default;
    private Offset _positionY = Offset.Default;
    private Size _width = Size.Default;
    private Size _height = Size.Default;
    private Size _maxWidth = Size.Max;
    private Size _maxHeight = Size.Max;
    private Offset _marginLeft = Offset.Default;
    private Offset _marginRight = Offset.Default;
    private Offset _marginTop = Offset.Default;
    private Offset _marginBottom = Offset.Default;
    private Offset _paddingLeft = Offset.Default;
    private Offset _paddingRight = Offset.Default;
    private Offset _paddingTop = Offset.Default;
    private Offset _paddingBottom = Offset.Default;
    private bool _showVScroll = false;
    private bool _showHScroll = false;
    private bool _ignore = false;
    private bool _fitContentX = false;
    private double _fitContentXPerc = 1f;
    private bool _fitContentY = false;
    private double _fitContentYPerc = 1f;
    private bool _centerContent = false;
    private bool _canScaleChildren = false;
    private LayoutNode _positionRelativeTo;
    private LayoutNode _sizeRelativeTo;

    private LayoutType _layout = LayoutType.None;
    private bool _layoutX = false;
    private bool _layoutY = false;
    private Size _layoutXSpacing = Size.Default;
    private Size _layoutYSpacing = Size.Default;
    internal ClipType _clipped = ClipType.None;

    internal ulong _nextAnimationFrame = 0;
    internal int _nextAnimation = 0;

    internal int ZIndex = 0;

    internal readonly List<LayoutNode> Children = new List<LayoutNode>();

    public LayoutNode(LayoutNode? parent, Gui gui, ulong storageHash)
    {
        ID = storageHash;
        Gui = gui;
        if (parent != null)
        {
            SetNewParent(parent);
            ZIndex = parent.ZIndex;
        }
    }

    public int GetNextAnimation()
    {
        if (_nextAnimationFrame != Gui.frameCount)
        {
            // New frame for this node, reset animation index
            _nextAnimationFrame = Gui.frameCount;
            _nextAnimation = 0;
        }
        return _nextAnimation++;
    }

    // TODO: This cache needs to be improved, we should only update what we absolutely need to when we need it, right now its possibly slower than just recalculating on get
    public void UpdateCache()
    {
        _data = new(this);

        // Cache scale first
        UpdateScaleCache();

        // Then finally position (Relies on Scale and Padding)
        UpdatePositionCache();

        foreach (var child in Children)
            child.UpdateCache();
    }

    public void UpdateScaleCache()
    {
        // Then Margin/Paddings (They rely on Scale)
        if (_positionRelativeTo != null)
        {
            double x = _positionRelativeTo._data.Scale.x;
            double y = _positionRelativeTo._data.Scale.y;
            _data.Margins = new(
                _marginLeft.ToPixels(x),
                _marginRight.ToPixels(x),
                _marginTop.ToPixels(y),
                _marginBottom.ToPixels(y)
            );
            _data.Paddings = new(
                _paddingLeft.ToPixels(x),
                _paddingRight.ToPixels(x),
                _paddingTop.ToPixels(y),
                _paddingBottom.ToPixels(y)
            );
        }
        else
        {
            _data.Margins = new(_marginLeft.ToPixels(0), _marginRight.ToPixels(0), _marginTop.ToPixels(0), _marginBottom.ToPixels(0));
            _data.Paddings = new(_paddingLeft.ToPixels(0), _paddingRight.ToPixels(0), _paddingTop.ToPixels(0), _paddingBottom.ToPixels(0));
        }

        if (_sizeRelativeTo != null)
        {
            var width = _sizeRelativeTo._data.GlobalContentWidth;
            var height = _sizeRelativeTo._data.GlobalContentHeight;
            var maxW = _maxWidth.ToPixels(width);
            var maxH = _maxHeight.ToPixels(height);

            _data.Scale = new(
                Math.Min(_width.ToPixels(width) - _data.Margins.Horizontal, maxW),
                Math.Min(_height.ToPixels(height) - _data.Margins.Vertical, maxH)
            );

            _data.MaxScale = new(maxW, maxH);
        }
        else
        {
            var maxW = _maxWidth.ToPixels(0);
            var maxH = _maxHeight.ToPixels(0);

            _data.Scale = new(
                Math.Min(_width.ToPixels(0) - _data.Margins.Horizontal, maxW),
                Math.Min(_height.ToPixels(0) - _data.Margins.Vertical, maxH)
            );

            _data.MaxScale = new(maxW, maxH);
        }
    }

    public void UpdatePositionCache()
    {
        if (_positionRelativeTo != null)
        {
            _data.Position = new(
                _positionX.ToPixels(_positionRelativeTo._data.GlobalContentWidth),
                _positionY.ToPixels(_positionRelativeTo._data.GlobalContentHeight)
            );
        }
        else
        {
            _data.Position = new(
                _positionX.ToPixels(0),
                _positionY.ToPixels(0)
            );
        }
    }

    public void ProcessLayout()
    {
        DoScaleChildren();
        foreach (var child in Children)
            child.ProcessLayout();
        //DoScaleChildren(); // TODO: Do we need to do this twice?

        UpdatePositionCache();

        ApplyLayout();

        if (_centerContent)
        {
            Vector2 childrenCenter = new Vector2();
            if (_layout == LayoutType.Grid)
            {
                int childCount = 0;
                foreach (var childB in Children)
                {
                    if (childB._ignore) continue;
                    childrenCenter += childB._data.Rect.Center;
                    childCount++;
                }
                childrenCenter /= new Vector2(childCount, childCount);
            }

            foreach (var child in Children)
            {
                if (child._ignore) continue;

                switch (_layout)
                {
                    case LayoutType.Column:
                        child._positionX = (_data.GlobalContentWidth - child._data.Scale.x - child._data.Margins.Horizontal) / 2;
                        break;
                    case LayoutType.Row:
                        child._positionY = (_data.GlobalContentHeight - child._data.Scale.y - child._data.Margins.Vertical) / 2;
                        break;
                    case LayoutType.Grid:
                        // TODO: This isnt working correctly, All the layout types should use just this, but it needs to work properly first
                        Vector2 ourCenter = new Vector2(_data.GlobalContentWidth / 2, _data.GlobalContentHeight / 2);
                        Vector2 offset = ourCenter - childrenCenter;
                        child._positionX = child._data.Position.x + offset.x;
                        child._positionY = child._data.Position.y + offset.y;
                        break;
                    default:
                        child._positionX = (_data.GlobalContentWidth - child._data.Scale.x - child._data.Margins.Horizontal) / 2;
                        child._positionY = (_data.GlobalContentHeight - child._data.Scale.y - child._data.Margins.Vertical) / 2;
                        break;
                }
                child.UpdatePositionCache();
            }
        }

        if (Children.Count > 0)
        {
            _data.ContentRect = Children[0]._data.OuterRect;
            for (int i = 1; i < Children.Count; i++)
            {
                var child = Children[i];
                if (child._ignore) continue;
                _data.ContentRect = Rect.CombineRect(_data.ContentRect, child._data.OuterRect);
            }
        }
        else _data.ContentRect = new Rect();


        ApplyFitContent();

    }

    internal void DrawScrollbars()
    {
        // Draw Scroll bars
        if (_data.IsVScrollVisible)
        {
            Gui.WidgetStyle style = new(30);

            LayoutNode n;
            using ((n = AppendNode("_VScroll")).Width(ScrollBarSize).Height(Size.Percentage(1f)).Left(Offset.Percentage(1f)).IgnoreLayout().Enter())
            {
                Rect scrollRect = n.LayoutData.InnerRect;
                double overflowHeight = LayoutData.ContentRect.height - LayoutData.InnerRect.height;

                double scrollRatio = LayoutData.InnerRect.height / LayoutData.ContentRect.height;
                double scrollBarHeight = Math.Max(MinScrollBarSize, scrollRatio * scrollRect.height);

                double scrollBarY = (VScroll / overflowHeight) * (scrollRect.height - scrollBarHeight);

                Rect barRect = new(scrollRect.x, scrollRect.y + scrollBarY, scrollRect.width, scrollBarHeight);

                Interactable interact = Gui.GetInteractable(barRect);

                if (interact.TakeFocus() || interact.IsActive())
                {
                    Gui.Draw2D.DrawRectFilled(barRect, style.ActiveColor, style.Roundness);
                    {
                        VScroll += Gui.PointerDelta.y / scrollRatio;
                        Gui.layoutDirty = true;
                    }
                }
                else if (interact.IsHovered()) Gui.Draw2D.DrawRectFilled(barRect, style.HoveredColor, style.Roundness);
                else Gui.Draw2D.DrawRectFilled(barRect, style.BGColor * 1.8f, style.Roundness);

                if (Gui.IsPointerHovering(LayoutData.InnerRect) && Gui.PointerWheel != 0)
                {
                    VScroll -= Gui.PointerWheel * 10;
                    Gui.layoutDirty = true;
                }

                VScroll = MathD.Clamp(VScroll, 0, overflowHeight);
            }
        }
        else if (VScroll != 0)
        {
            VScroll = 0;
            Gui.layoutDirty = true;
        }

        if (_data.IsHScrollVisible)
        {
            Gui.WidgetStyle style = new(30);

            LayoutNode n;
            using ((n = AppendNode("_HScroll")).Height(ScrollBarSize).Width(Size.Percentage(1f)).Top(Offset.Percentage(1f)).IgnoreLayout().Enter())
            {
                Rect scrollRect = n.LayoutData.InnerRect;
                double overflowHeight = LayoutData.ContentRect.width - LayoutData.InnerRect.width;

                double scrollRatio = LayoutData.InnerRect.width / LayoutData.ContentRect.width;
                double scrollBarWidth = Math.Max(MinScrollBarSize, scrollRatio * scrollRect.width);

                double scrollBarX = (HScroll / overflowHeight) * (scrollRect.width - scrollBarWidth);

                Rect barRect = new(scrollRect.x + scrollBarX, scrollRect.y, scrollBarWidth, scrollRect.height);

                Interactable interact = Gui.GetInteractable(barRect);

                if (interact.TakeFocus() || interact.IsActive())
                {
                    Gui.Draw2D.DrawRectFilled(barRect, style.ActiveColor, style.Roundness);
                    {
                        HScroll += Gui.PointerDelta.x / scrollRatio;
                        Gui.layoutDirty = true;
                    }
                }
                else if (interact.IsHovered()) Gui.Draw2D.DrawRectFilled(barRect, style.HoveredColor, style.Roundness);
                else Gui.Draw2D.DrawRectFilled(barRect, style.BGColor * 1.8f, style.Roundness);

                //if (Gui.IsPointerHovering(LayoutData.InnerRect) && Gui.PointerWheel != 0)
                //{
                //    HScroll -= Gui.PointerWheel * 10;
                //    Gui.layoutDirty = true;
                //}

                HScroll = MathD.Clamp(HScroll, 0, overflowHeight);
            }
        }
        else if (HScroll != 0)
        {
            HScroll = 0;
            Gui.layoutDirty = true;
        }
    }

    internal void DoScaleChildren()
    {
        if (!_canScaleChildren) return;

        var scalableChildren = Children.Where(c => !c._ignore).ToList();
        double totalAvailableWidth = _data.GlobalContentWidth;
        double totalAvailableHeight = _data.GlobalContentHeight;

        if (_layout == LayoutType.Row)
        {
            double remainingWidth = totalAvailableWidth;
            double remainingChildren = scalableChildren.Count;

            foreach (var child in scalableChildren)
            {
                double width = remainingWidth / remainingChildren;
                width = Math.Min(width, child._data.MaxScale.x);
                child._width = Math.Max(width - child._data.Margins.Horizontal, 0);
                remainingWidth -= width;
                remainingChildren--;
            }
        }

        if (_layout == LayoutType.Column)
        {
            double remainingHeight = totalAvailableHeight;
            double remainingChildren = scalableChildren.Count;

            foreach (var child in scalableChildren)
            {
                double height = remainingHeight / remainingChildren;
                height = Math.Min(height, child._data.MaxScale.y);
                child._height = Math.Max(height - child._data.Margins.Vertical, 0);
                remainingHeight -= height;
                remainingChildren--;
            }
        }

        UpdateCache();
    }

    internal void ApplyLayout()
    {
        double x = 0, y = 0;
        switch (_layout)
        {
            case LayoutType.Column:
                foreach (var child in Children)
                {
                    if (child._ignore) continue;
                    if (_layoutX) child._positionX = 0;
                    if (_layoutY) child._positionY = y;
                    y += child._data.Margins.Vertical + child._data.Scale.y;
                    y += _layoutYSpacing.ToPixels(_data.GlobalContentHeight);
                    child.UpdatePositionCache();
                }
                break;
            case LayoutType.ColumnReversed:
                y = _data.GlobalContentHeight;
                foreach (var child in Children)
                {
                    if (child._ignore) continue;
                    if (_layoutY)
                    {
                        y -= child._data.Margins.Vertical + child._data.Scale.y;
                        child._positionY = y;
                    }
                    if (_layoutX) child._positionX = 0;
                    y -= _layoutYSpacing.ToPixels(_data.GlobalContentHeight);
                    child.UpdatePositionCache();
                }
                break;
            case LayoutType.Row:
                foreach (var child in Children)
                {
                    if (child._ignore) continue;
                    if (_layoutX) child._positionX = x;
                    if (_layoutY) child._positionY = 0;
                    x += child._data.Margins.Horizontal + child._data.Scale.x;
                    x += _layoutXSpacing.ToPixels(_data.GlobalContentWidth);
                    child.UpdatePositionCache();
                }
                break;
            case LayoutType.RowReversed:
                x = _data.GlobalContentWidth;
                foreach (var child in Children)
                {
                    if (child._ignore) continue;
                    if (_layoutX)
                    {
                        x -= child._data.Margins.Horizontal + child._data.Scale.x;
                        child._positionX = x;
                    }
                    if (_layoutY) child._positionY = 0;
                    x -= _layoutXSpacing.ToPixels(_data.GlobalContentWidth);
                    child.UpdatePositionCache();
                }
                break;
            case LayoutType.Grid:
            case LayoutType.GridReversed:

                List<LayoutNode> gridChildren = Children.Where(c => !c._ignore).ToList();
                if (_layout == LayoutType.GridReversed)
                    gridChildren.Reverse();

                double maxY = 0;
                foreach (var child in gridChildren)
                {
                    if (x + child._data.Scale.x + child._data.Margins.Horizontal > _data.GlobalContentWidth)
                    {
                        y += maxY;
                        x = 0;
                        maxY = 0;
                    }

                    if (_layoutX) child._positionX = x;
                    if (_layoutY) child._positionY = y;
                    x += child._data.Margins.Horizontal + child._data.Scale.x;

                    if (child._data.Margins.Vertical + child._data.Scale.y > maxY)
                        maxY = child._data.Margins.Vertical + child._data.Scale.y;
                    child.UpdatePositionCache();
                }
                break;
            default:
                break;
        }
    }

    public void ApplyFitContent()
    {
        if (!_fitContentX && !_fitContentY)
            return;

        if (_fitContentX)
            _width = MathD.Lerp(0, _data.ContentRect.width + _data.Paddings.Horizontal, _fitContentXPerc);
        if (_fitContentY)
            _height = MathD.Lerp(0, _data.ContentRect.height + _data.Paddings.Vertical, _fitContentYPerc);
        UpdateCache();
    }

    public ulong GetHashCode64()
    {
        ulong hash = 17;
        hash = hash * 23 + ID;
        hash = hash * 23 + _positionX.GetHashCode64();
        hash = hash * 23 + _positionY.GetHashCode64();
        hash = hash * 23 + _width.GetHashCode64();
        hash = hash * 23 + _height.GetHashCode64();
        hash = hash * 23 + _maxWidth.GetHashCode64();
        hash = hash * 23 + _maxHeight.GetHashCode64();
        hash = hash * 23 + _marginLeft.GetHashCode64();
        hash = hash * 23 + _marginRight.GetHashCode64();
        hash = hash * 23 + _marginTop.GetHashCode64();
        hash = hash * 23 + _marginBottom.GetHashCode64();
        hash = hash * 23 + _paddingLeft.GetHashCode64();
        hash = hash * 23 + _paddingRight.GetHashCode64();
        hash = hash * 23 + _paddingTop.GetHashCode64();
        hash = hash * 23 + _paddingBottom.GetHashCode64();
        hash = hash * 23 + (ulong)_ignore.GetHashCode();
        hash = hash * 23 + (ulong)_fitContentX.GetHashCode();
        hash = hash * 23 + (ulong)_fitContentXPerc.GetHashCode();
        hash = hash * 23 + (ulong)_fitContentY.GetHashCode();
        hash = hash * 23 + (ulong)_fitContentYPerc.GetHashCode();
        hash = hash * 23 + (ulong)_centerContent.GetHashCode();
        hash = hash * 23 + (ulong)_canScaleChildren.GetHashCode();
        hash = hash * 23 + (ulong)_layout.GetHashCode();
        hash = hash * 23 + (ulong)_clipped.GetHashCode();
        hash = hash * 23 + (ulong)VScroll.GetHashCode();
        hash = hash * 23 + (ulong)HScroll.GetHashCode();
        hash = hash * 23 + (ulong)_nextAnimation.GetHashCode();
        return hash;
    }
}
