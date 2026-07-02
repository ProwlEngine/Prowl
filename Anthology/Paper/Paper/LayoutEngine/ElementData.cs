using System;
using System.Collections.Generic;
using Prowl.PaperUI.Events;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.PaperUI.LayoutEngine
{
    /// <summary>How leftover main-axis space is distributed across items on a wrapped line.</summary>
    public enum WrapJustify
    {
        /// <summary>Pack items at the start of each line (leftover space trails at the end).</summary>
        Start,
        /// <summary>Center items on each line.</summary>
        Center,
        /// <summary>Pack items at the end of each line.</summary>
        End,
        /// <summary>Distribute leftover space as equal gaps between items.</summary>
        SpaceBetween,
        /// <summary>Distribute leftover space as equal gaps around every item.</summary>
        SpaceAround,
        /// <summary>Grow every item on the line equally so the line consumes the full width.</summary>
        Fill,
    }

    public struct ElementData
    {
        public int ID;

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
        public bool IsRichText;
        public string Paragraph;
        public FontFile Font;
        public FontFile FontBold;
        public FontFile FontItalic;
        public FontFile FontBoldItalic;
        public FontFile FontMono;
        public FontStyle FontStyle;
        public TextWrapMode WrapMode;
        public TextAlignment TextAlignment;

        /// <summary>Flex-wrap: parent-directed children flow onto new lines when they overrun the main axis.</summary>
        public bool ContentWrap;
        /// <summary>How leftover main-axis space on each wrapped line is distributed.</summary>
        public WrapJustify WrapJustify;

        // Cached text layout objects (RichText is persisted across frames via element storage so
        // animation start time survives — see Paper.Core.cs ProcessText / DrawText paths.)
        internal Quill.Canvas.QuillMarkdown? _quillMarkdown;
        internal Quill.Canvas.QuillRichText? _quillRichText;
        internal TextLayout _textLayout;

        // Rendering
        internal List<ElementRenderCommand> _renderCommands;
        internal List<ElementRenderCommand> _foregroundRenderCommands;
        internal ElementStyle _elementStyle;
        internal bool _scissorEnabled;
        internal bool _clampToScreen;

        // Culling bounds: the element's whole-subtree extent in its own local (layout) space, grown
        // to cover box shadow and every descendant. _cullHasLayerBreakout is set when any descendant
        // sits on a higher layer and therefore escapes this element's clip. Recomputed each frame
        // after layout; RenderElement uses them to skip fully-clipped subtrees.
        internal float _cullMinX, _cullMinY, _cullMaxX, _cullMaxY;
        internal bool _cullHasLayerBreakout;

        /// <summary>
        /// Layer assignment. Defaults to <see cref="Layer.Base"/> (0). Higher values render later
        /// and are hit-tested first. Use <see cref="Layer.Overlay"/> / <see cref="Layer.Topmost"/>
        /// for the well-known tiers, or any custom <see cref="int"/> for in-between tiers.
        /// </summary>
        public int Layer;

        // Layout results
        public bool ProcessedText;
        public float X;
        public float Y;
        public float LayoutWidth;
        public float LayoutHeight;
        public float RelativeX;
        public float RelativeY;

        // Content sizing for auto-sized elements
        public Func<float?, float?, (float, float)?> ContentSizer;

        public readonly Rect LayoutRect => new Rect(X, Y, X + LayoutWidth, Y + LayoutHeight);

        public static ElementData Create(int id)
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
                IsRichText = false,
                Paragraph = null,
                Font = null,
                FontStyle = FontStyle.Regular,
                WrapMode = TextWrapMode.NoWrap,
                TextAlignment = TextAlignment.Left,
                ContentWrap = false,
                WrapJustify = WrapJustify.Start,
                _quillMarkdown = null,
                _quillRichText = null,
                _textLayout = null,
                _renderCommands = null,
                _foregroundRenderCommands = null,
                _elementStyle = new ElementStyle(),
                _scissorEnabled = false,
                _clampToScreen = false,
                // Default to Layer.Base (0). Fully qualified because the RHS shadows the LHS
                // field name in an object initializer when the type is a static class.
                Layer = PaperUI.Layer.Base,
                ProcessedText = false,
            };
        }
    }
}
