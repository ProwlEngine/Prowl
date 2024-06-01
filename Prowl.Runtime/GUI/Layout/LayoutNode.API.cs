namespace Prowl.Runtime.GUI.Layout
{
    public partial class LayoutNode
    {
        public LayoutNode Layout(LayoutType layout, bool controlX = true, bool controlY = true)
        {
            _layout = layout;
            _layoutX = controlX;
            _layoutY = controlY;
            return this;
        }

        public LayoutNode FitContent() => FitContentWidth().FitContentHeight();

        public LayoutNode FitContentWidth()
        {
            _fitContentX = true;
            return this;
        }

        public LayoutNode FitContentHeight()
        {
            _fitContentY = true;
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

        public LayoutNode Expand(int xOffset = 0, int yOffset = 0) => Width(Size.Percentage(1f, xOffset)).Height(Size.Percentage(1f, yOffset));
        public LayoutNode ExpandWidth(int pixelOffset = 0) => Width(Size.Percentage(1f, pixelOffset));
        public LayoutNode ExpandHeight(int pixelOffset = 0) => Height(Size.Percentage(1f, pixelOffset));

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

        public LayoutNode Margin(Offset margins) => Margin(margins, margins, margins, margins);
        public LayoutNode MarginTop(Offset margin) => Margin(margin, 0, 0, 0);
        public LayoutNode MarginRight(Offset margin) => Margin(0, margin, 0, 0);
        public LayoutNode MarginBottom(Offset margin) => Margin(0, 0, margin, 0);
        public LayoutNode MarginLeft(Offset margin) => Margin(0, 0, 0, margin);
        public LayoutNode Margin(Offset vertical, Offset horizontal) => Margin(vertical, horizontal, vertical, horizontal);

        public LayoutNode Margin(Offset top, Offset right, Offset bottom, Offset left)
        {
            _marginTop = top;
            _marginRight = right;
            _marginBottom = bottom;
            _marginLeft = left;
            return this;
        }

        public LayoutNode Padding(Offset paddings) => Padding(paddings, paddings, paddings, paddings);
        public LayoutNode PaddingTop(Offset padding) => Padding(padding, 0, 0, 0);
        public LayoutNode PaddingRight(Offset padding) => Padding(0, padding, 0, 0);
        public LayoutNode PaddingBottom(Offset padding) => Padding(0, 0, padding, 0);
        public LayoutNode PaddingLeft(Offset padding) => Padding(0, 0, 0, padding);
        public LayoutNode Padding(Offset vertical, Offset horizontal) => Padding(vertical, horizontal, vertical, horizontal);
        public LayoutNode Padding(Offset top, Offset right, Offset bottom, Offset left)
        {
            _paddingTop = top;
            _paddingRight = right;
            _paddingBottom = bottom;
            _paddingLeft = left;
            return this;
        }

        public LayoutNode PositionRelativeTo(LayoutNode node)
        {
            _positionRelativeTo = node;
            return this;
        }

        public LayoutNode SizeRelativeTo(LayoutNode node)
        {
            _sizeRelativeTo = node;
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
            _positionRelativeTo = newParent;
            _sizeRelativeTo = newParent;
        }

        public LayoutNode AppendNode(string ID) => Gui.Node(this, ID);
    }
}
