// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.GUI.Layout;

public partial class LayoutNode
{
    public LayoutNode Layout(LayoutType layout, bool controlX = true, bool controlY = true)
    {
        _layout = layout;
        _layoutX = controlX;
        _layoutY = controlY;
        return this;
    }

    public LayoutNode Spacing(Size spacing)
    {
        _layoutYSpacing = spacing;
        _layoutXSpacing = spacing;
        return this;
    }

    public LayoutNode FitContent() => FitContentWidth().FitContentHeight();

    public LayoutNode FitContentWidth(double percentage = 1f)
    {
        _fitContentX = true;
        _fitContentXPerc = percentage;
        return this;
    }

    public LayoutNode FitContentHeight(double percentage = 1f)
    {
        _fitContentY = true;
        _fitContentYPerc = percentage;
        return this;
    }

    public LayoutNode CenterContent()
    {
        _centerContent = true;
        return this;
    }

    public LayoutNode ScaleChildren()
    {
        _canScaleChildren = true;
        return this;
    }

    public LayoutNode Expand(double xOffset = 0, double yOffset = 0) => ExpandWidth(xOffset).ExpandHeight(yOffset);
    public LayoutNode ExpandWidth(double pixelOffset = 0) => Width(Size.Percentage(1f, pixelOffset));
    public LayoutNode ExpandHeight(double pixelOffset = 0) => Height(Size.Percentage(1f, pixelOffset));

    public LayoutNode Scale(Size size) => Width(size).Height(size);
    public LayoutNode Scale(Size width, Size height) => Width(width).Height(height);

    public LayoutNode Width(Size width)
    {
        _width = width;
        return this;
    }

    public LayoutNode Height(Size height)
    {
        _height = height;
        return this;
    }

    public LayoutNode MaxWidth(Size maxwidth)
    {
        _maxWidth = maxwidth;
        return this;
    }

    public LayoutNode MaxHeight(Size maxheight)
    {
        _maxHeight = maxheight;
        return this;
    }

    public LayoutNode TopLeft(Offset topleft) => TopLeft(topleft, topleft);
    public LayoutNode TopLeft(Offset left, Offset top) => Left(left).Top(top);

    public LayoutNode Left(Offset left)
    {
        _positionX = left;
        return this;
    }

    public LayoutNode Top(Offset top)
    {
        _positionY = top;
        return this;
    }

    public LayoutNode IgnoreLayout()
    {
        _ignore = true;
        return this;
    }

    public LayoutNode Clip(bool outerRect = false)
    {
        _clipped = outerRect ? ClipType.Outer : ClipType.Inner;
        return this;
    }

    public LayoutNode Scroll(bool vertical = true, bool horizontal = true, Gui.WidgetStyle? inputstyle = null)
    {
        Gui.ScollableNodes.Add(this);

        _scrollStyle = inputstyle ?? new(30);
        _showVScroll = vertical;
        _showHScroll = horizontal;
        return this;
    }

    public LayoutNode Padding(double paddings) => Padding(paddings, paddings, paddings, paddings);
    public LayoutNode PaddingTop(double padding) => Padding(padding, 0, 0, 0);
    public LayoutNode PaddingRight(double padding) => Padding(0, padding, 0, 0);
    public LayoutNode PaddingBottom(double padding) => Padding(0, 0, padding, 0);
    public LayoutNode PaddingLeft(double padding) => Padding(0, 0, 0, padding);
    public LayoutNode Padding(double vertical, double horizontal) => Padding(vertical, horizontal, vertical, horizontal);
    public LayoutNode Padding(double top, double right, double bottom, double left)
    {
        _paddingTop = top;
        _paddingRight = right;
        _paddingBottom = bottom;
        _paddingLeft = left;
        return this;
    }

    public LayoutNodeScope Enter()
    {
        var scope = new LayoutNodeScope(this);
        Gui.PushNode(scope);
        return scope;
    }

    public void SetNewParent(LayoutNode newParent)
    {
        if (Parent != null)
            Parent.Children.Remove(this);
        Parent = newParent;
        Parent.Children.Add(this);
        //Parent.GetNextNode();
    }

    public LayoutNode AppendNode(string ID) => Gui.Node(this, ID);
}
