namespace Prowl.Runtime.GUI.Layout
{
    public partial class LayoutNode
    {
        public LayoutNode Layout(LayoutType layout)
        {
            if (Gui.CurrentPass != Gui.Pass.BeforeLayout) return this;
            _layout = layout;
            return this;
        }

        public LayoutNode FitContent()
        {
            if (Gui.CurrentPass != Gui.Pass.BeforeLayout) return this;
            _fitContentX = true;
            _fitContentY = true;
            return this;
        }

        public LayoutNode FitContentWidth()
        {
            if (Gui.CurrentPass != Gui.Pass.BeforeLayout) return this;
            _fitContentX = true;
            return this;
        }

        public LayoutNode FitContentHeight()
        {
            if (Gui.CurrentPass != Gui.Pass.BeforeLayout) return this;
            _fitContentY = true;
            return this;
        }

        public LayoutNode CenterContent()
        {
            if (Gui.CurrentPass != Gui.Pass.BeforeLayout) return this;
            _centerContent = true;
            return this;
        }

        public LayoutNode AutoScaleChildren()
        {
            if (Gui.CurrentPass != Gui.Pass.BeforeLayout) return this;
            _canScaleChildren = true;
            return this;
        }

        public LayoutNode Width(Size width)
        {
            if (Gui.CurrentPass != Gui.Pass.BeforeLayout) return this;
            _width = width;
            return this;
        }

        public LayoutNode Height(Size height)
        {
            if (Gui.CurrentPass != Gui.Pass.BeforeLayout) return this;
            _height = height;
            return this;
        }

        public LayoutNode MaxWidth(Size maxwidth)
        {
            if (Gui.CurrentPass != Gui.Pass.BeforeLayout) return this;
            _maxWidth = maxwidth;
            return this;
        }

        public LayoutNode MaxHeight(Size maxheight)
        {
            if (Gui.CurrentPass != Gui.Pass.BeforeLayout) return this;
            _maxHeight = maxheight;
            return this;
        }
        public LayoutNode TopLeft(Offset topleft) => TopLeft(topleft, topleft);
        public LayoutNode TopLeft(Offset left, Offset top)
        {
            if (Gui.CurrentPass != Gui.Pass.BeforeLayout) return this;
            _positionX = left;
            _positionY = top;
            return this;
        }

        public LayoutNode Left(Offset left)
        {
            if (Gui.CurrentPass != Gui.Pass.BeforeLayout) return this;
            _positionX = left;
            return this;
        }

        public LayoutNode Top(Offset top)
        {
            if (Gui.CurrentPass != Gui.Pass.BeforeLayout) return this;
            _positionY = top;
            return this;
        }

        public LayoutNode IgnoreLayout()
        {
            if (Gui.CurrentPass != Gui.Pass.BeforeLayout) return this;
            _ignore = true;
            return this;
        }

        public LayoutNode Clip(bool outerRect = false)
        {
            if (Gui.CurrentPass != Gui.Pass.BeforeLayout) return this;
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
            if (Gui.CurrentPass != Gui.Pass.BeforeLayout) return this;
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
            if (Gui.CurrentPass != Gui.Pass.BeforeLayout) return this;
            _paddingTop = top;
            _paddingRight = right;
            _paddingBottom = bottom;
            _paddingLeft = left;
            return this;
        }

        public LayoutNode PositionRelativeTo(LayoutNode node)
        {
            if (Gui.CurrentPass != Gui.Pass.BeforeLayout) return this;
            _positionRelativeTo = node;
            return this;
        }

        public LayoutNode SizeRelativeTo(LayoutNode node)
        {
            if (Gui.CurrentPass != Gui.Pass.BeforeLayout) return this;
            _sizeRelativeTo = node;
            return this;
        }

        public LayoutNodeScope Enter()
        {
            return new LayoutNodeScope(this);
        }

        public void SetNewParent(LayoutNode newParent)
        {
            if (Parent != null)
                Parent.Children.Remove(this);
            Parent = newParent;
            _positionRelativeTo = newParent;
            _sizeRelativeTo = newParent;
            Parent.Children.Add(this);
        }

        public LayoutNode AppendNode() => Gui.Node();
    }
}
