// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI.Events;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.PaperUI.LayoutEngine;

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

/// <summary> Configuration data for a UI element, including its identifiers, interactivity flags, event callbacks, layout properties, and font settings. </summary>
public struct ElementData
{
    public int ID;

    // Events
    public bool IsFocusable;
    /// <summary> When true, the element ignores all input events (mouse, keyboard, touch). </summary>
    public bool IsNotInteractable;
    /// <summary> When true, events on this element do not propagate to parent elements. </summary>
    public bool StopPropagation;

    /// <summary>Mouse cursor shape requested while this element is hovered. Defaults to
    /// <see cref="PaperCursor.Inherit"/> (take the nearest ancestor's, or the arrow at the root).</summary>
    public PaperCursor Cursor;

    /// <summary>Mouse cursor shape requested while this element is pressed/dragged. Defaults to
    /// <see cref="PaperCursor.Inherit"/>, which falls back to <see cref="Cursor"/> during a drag.</summary>
    public PaperCursor CursorDragging;

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

    /// <summary> Called after this element's layout is computed, with the element handle and its final layout rectangle. </summary>
    public Action<ElementHandle, Rect> OnPostLayout;


    // Hierarchy
    /// <summary> Index of the parent element in the owner's element array, or -1 if this element is the root. </summary>
    public int ParentIndex;
    /// <summary> Indices of child elements in the owning element array. </summary>
    public List<int> ChildIndices;

    /// <summary> Whether this element inherits its parent's interaction state (mouse hover, press, etc.) instead of tracking its own. </summary>
    public bool IsHookedToParent;

    /// <summary> Whether this element has one or more children that are hooked to it. An optimization flag for interaction hooking. </summary>
    public bool IsAHookedParent;

    /// <summary>  Tab navigation - element's position in tab order (-1 means not focusable via tab) </summary>
    public int TabIndex;

    /// <summary> Whether the element is rendered and participates in layout. Defaults to true. </summary>
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
    /// <summary> How text wraps within the element's bounds. Defaults to NoWrap. </summary>
    public TextWrapMode WrapMode;
    /// <summary>When true (single-line only), text wider than the element is cut and suffixed with
    /// an ellipsis that is guaranteed to fit.</summary>
    public bool Truncate;
    public TextAlignment TextAlignment;

    /// <summary>Flex-wrap: parent-directed children flow onto new lines when they overrun the main axis.</summary>
    public bool ContentWrap;
    /// <summary>How leftover main-axis space on each wrapped line is distributed.</summary>
    public WrapJustify WrapJustify;

    // Cached text layout objects (RichText is persisted across frames via element storage so animation start time survives -- see Paper.Core.cs ProcessText / DrawText paths.)
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

    // Per-frame memo for ProcessText. Layout runs an element several times per frame during
    // stretch resolution, and DrawText calls ProcessText again at render; each pass otherwise
    // re-derives identical settings and repeats the layout-cache lookups. The first compute of
    // the frame fills these; later calls reuse the size while the width still applies.
    // ElementData is recreated every frame, so _textMemoValid starts false automatically.
    internal Float2 _textMemoSize;
    internal float _textMemoWidth;
    internal bool _textMemoWidthIndependent;
    internal bool _textMemoValid;

    // Layout results
    /// <summary> Whether text layout has been computed for this element this frame. Set to true by ProcessText so subsequent layout and render passes skip redundant work. </summary>
    public bool ProcessedText;
    /// <summary> The resolved X position of the top-left corner of this element's layout rectangle. </summary>
    public float X;
    /// <summary> The resolved Y position of the top-left corner of this element's layout rectangle. </summary>
    public float Y;
    /// <summary> The width of this element as computed by the layout engine, in the parent's coordinate space. </summary>
    public float LayoutWidth;
    /// <summary> The Height of this element as computed by the layout engine, in the parent's coordinate space </summary>
    public float LayoutHeight;
    /// <summary> The resolved X position of the top-left corner of this element's layout rectangle, in the parent's coordinate space. </summary>
    public float RelativeX;
    /// <summary> The resolved Y position of the top-left corner of this element's layout rectangle, in the parent's coordinate space. </summary>
    public float RelativeY;

    /// <summary> Callback that measures the element's content size given optional width and height constraints. Returns the measured (width, height) or null if the measurement is unavailable. </summary>
    public Func<float?, float?, (float, float)?> ContentSizer;

    /// <summary> Gets the bounding rectangle of this element in sceen (or root) cordinate space, derived from X, Y, LayoutWidth and LayoutHeight. </summary>
    public readonly Rect LayoutRect => new Rect(X, Y, X + LayoutWidth, Y + LayoutHeight);

    /// <summary> Creates a new ElementData with the given ID and all fields set to their default values. </summary>
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
            Truncate = false,
            TextAlignment = TextAlignment.Left,
            ContentWrap = false,
            WrapJustify = WrapJustify.Start,
            _quillMarkdown = null,
            _quillRichText = null,
            _textLayout = null,
            _renderCommands = null,
            _foregroundRenderCommands = null,
            // Assigned from the persistent per-id style store by CreateElement instead of a fresh
            // throwaway allocation each frame (the old new ElementStyle() here was discarded by
            // UpdateStyles, which repoints this at the _activeStyles entry anyway).
            _elementStyle = null,
            _scissorEnabled = false,
            _clampToScreen = false,
            // Default to Layer.Base (0). Fully qualified because the RHS shadows the LHS
            // field name in an object initializer when the type is a static class.
            Layer = PaperUI.Layer.Base,
            ProcessedText = false,
        };
    }
}
