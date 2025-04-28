using Prowl.PaperUI.Events;
using Prowl.Quill;
using Prowl.Vector;

namespace Prowl.PaperUI.LayoutEngine
{
    public partial class Element
    {
        public ulong ID { get; internal set; } = 0;

        // Events
        public bool IsFocusable { get; set; } = true;
        public bool IsNotInteractable { get; set; } = false;

        public Action<ClickEvent>? OnClick { get; set; }
        public Action<ClickEvent>? OnPress { get; set; }
        public Action<ClickEvent>? OnRelease { get; set; }
        public Action<ClickEvent>? OnDoubleClick { get; set; }
        public Action<ClickEvent>? OnRightClick { get; set; }
        public Action<ClickEvent>? OnHeld { get; set; }

        public Action<DragEvent>? OnDragStart { get; set; }
        public Action<DragEvent>? OnDragging { get; set; }
        public Action<DragEvent>? OnDragEnd { get; set; }

        public Action<ScrollEvent>? OnScroll { get; set; }
        public Action<ElementEvent>? OnHover { get; set; }
        public Action<ElementEvent>? OnEnter { get; set; }
        public Action<ElementEvent>? OnLeave { get; set; }

        public Action<KeyEvent>? OnKeyPressed { get; set; }
        public Action<TextInputEvent>? OnTextInput { get; set; }
        public Action<FocusEvent>? OnFocusChange { get; set; }

        public Action<Element, Rect>? OnPostLayout { get; set; }

        public Scroll ScrollFlags { get; set; } = Scroll.None;
        public Action<Canvas, Rect, ScrollState> CustomScrollbarRenderer { get; set; }

        public Element? Parent { get; internal set; }
        public List<Element> Children { get; } = [];

        public bool Visible = true;

        internal double ZLayer = 0.0f;
        internal LayoutType LayoutType = LayoutType.Column;
        internal PositionType PositionType = PositionType.ParentDirected;

        // Rendering
        internal List<ElementRenderCommand>? _renderCommands = null;
        internal ElementStyle _elementStyle = new();
        internal bool _scissorEnabled = false;

        internal List<Element> GetSortedChildren => Children.OrderBy(c => c.ZLayer).ToList();

        // Layout results
        internal double X;
        internal double Y;
        internal double LayoutWidth;
        internal double LayoutHeight;
        internal double RelativeX;
        internal double RelativeY;
        internal Rect LayoutRect => new Rect(X, Y, LayoutWidth, LayoutHeight);

        // Content sizing for auto-sized elements
        internal Func<double?, double?, (double, double)?> ContentSizer { get; set; }

        internal void AddRenderElement(ElementRenderCommand element)
        {
            _renderCommands ??= new List<ElementRenderCommand>();
            _renderCommands.Add(element);
        }

        internal List<ElementRenderCommand>? GetRenderElements() => _renderCommands;
    }
}
