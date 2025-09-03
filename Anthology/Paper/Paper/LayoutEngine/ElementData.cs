using Prowl.PaperUI.Events;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;

namespace Prowl.PaperUI.LayoutEngine
{
    public struct ElementData
    {
        public ulong ID;

        // Events
        public bool IsFocusable;
        public bool IsNotInteractable;
        public bool StopPropagation;

        // Event handlers
        public Action<ClickEvent> OnClick;
        public Action<ClickEvent> OnPress;
        public Action<ClickEvent> OnRelease;
        public Action<ClickEvent> OnDoubleClick;
        public Action<ClickEvent> OnRightClick;
        public Action<ClickEvent> OnHeld;

        public Action<DragEvent> OnDragStart;
        public Action<DragEvent> OnDragging;
        public Action<DragEvent> OnDragEnd;

        public Action<ScrollEvent> OnScroll;
        public Action<ElementEvent> OnHover;
        public Action<ElementEvent> OnEnter;
        public Action<ElementEvent> OnLeave;

        public Action<KeyEvent> OnKeyPressed;
        public Action<TextInputEvent> OnTextInput;
        public Action<FocusEvent> OnFocusChange;

        public Action<ElementHandle, Rect> OnPostLayout;

        public Scroll ScrollFlags;
        public Action<Canvas, Rect, ScrollState> CustomScrollbarRenderer;

        // Hierarchy
        public int ParentIndex;
        public List<int> ChildIndices;
        
        // Interaction hooking - whether this element inherits parent's interaction state
        public bool IsHookedToParent;
        
        // Interaction hooking - whether this element has hooked children (optimization flag)
        public bool IsAHookedParent;
        
        // Tab navigation - element's position in tab order (-1 means not focusable via tab)
        public int TabIndex;

        public bool Visible;

        // Layout properties
        public LayoutType LayoutType;
        public PositionType PositionType;

        // Text properties
        public bool IsMarkdown;
        public string Paragraph;
        public FontFile Font;
        public FontFile FontBold;
        public FontFile FontItalic;
        public FontFile FontBoldItalic;
        public FontFile FontMono;
        public FontStyle FontStyle;
        public TextWrapMode WrapMode;
        public TextAlignment TextAlignment;

        // Cached text layout objects
        internal object _quillMarkdown;
        internal TextLayout _textLayout;

        // Rendering
        internal List<ElementRenderCommand> _renderCommands;
        internal ElementStyle _elementStyle;
        internal bool _scissorEnabled;

        public Layer Layer;

        // Layout results
        public bool ProcessedText;
        public double X;
        public double Y;
        public double LayoutWidth;
        public double LayoutHeight;
        public double RelativeX;
        public double RelativeY;

        // Content sizing for auto-sized elements
        public Func<double?, double?, (double, double)?> ContentSizer;

        public readonly Rect LayoutRect => new Rect(X, Y, LayoutWidth, LayoutHeight);

        public static ElementData Create(ulong id)
        {
            return new ElementData
            {
                ID = id,
                IsFocusable = true,
                IsNotInteractable = false,
                StopPropagation = false,
                ParentIndex = -1,
                ChildIndices = new List<int>(),
                IsHookedToParent = false,
                IsAHookedParent = false,
                TabIndex = -1,
                Visible = true,
                LayoutType = LayoutType.Column,
                PositionType = PositionType.ParentDirected,
                IsMarkdown = false,
                Paragraph = null,
                Font = null,
                FontStyle = FontStyle.Regular,
                WrapMode = TextWrapMode.NoWrap,
                TextAlignment = TextAlignment.Left,
                _quillMarkdown = null,
                _textLayout = null,
                _renderCommands = null,
                _elementStyle = new ElementStyle(),
                _scissorEnabled = false,
                Layer = Layer.Base,
                ProcessedText = false,
                ScrollFlags = Scroll.None
            };
        }
    }
}
