using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;
using Prowl.Scribe;
using Prowl.Vector.Geometry;
using Prowl.Vector.Spatial;

namespace Prowl.PaperUI
{
    public partial class Paper
    {
        #region Fields & Properties

        // Layout and hierarchy management
        private ElementHandle _rootElementHandle;
        internal Stack<ElementHandle> _elementStack = new Stack<ElementHandle>();
        private readonly Stack<int> _IDStack = new();
        private readonly HashSet<int> _createdElements = [];

        private readonly Dictionary<int, Hashtable> _storage = [];

        // Rendering context
        private Canvas _canvas;
        private ICanvasRenderer _renderer;
        private float _width;
        private float _height;
        private Stopwatch _timer = new();

        // Events
        public Action? OnEndOfFramePreLayout = null;
        public Action? OnEndOfFramePostLayout = null;

        // Performance metrics
        public float MillisecondsSpent { get; private set; }
        public uint CountOfAllElements { get; private set; }

        // Public properties
        public Rect ScreenRect => new Rect(0, 0, _width, _height);
        public ElementHandle RootElement => _rootElementHandle;
        public Canvas Canvas => _canvas;

        /// <summary>
        /// Gets the current parent element in the element hierarchy.
        /// </summary>
        public ElementHandle CurrentParent => _elementStack.Peek();

        #endregion

        #region Initialization and Frame Management

        /// <summary>
        /// Initializes Paper with a renderer and dimensions.
        /// </summary>
        /// <param name="renderer">The canvas renderer implementation</param>
        /// <param name="width">Viewport width</param>
        /// <param name="height">Viewport height</param>
        /// <param name="fontAtlas">Font atlas settings for text rendering</param>
        public Paper(ICanvasRenderer renderer, float width, float height, FontAtlasSettings fontAtlas)
        {
            _width = width;
            _height = height;
            _renderer = renderer;

            // Initialize element storage and create root element
            ClearElements();
            InitializeRootElement(_width, _height);
            _rootElementHandle = GetRootElementHandle();

            // Clear collections
            _elementStack.Clear();
            _IDStack.Clear();
            _createdElements.Clear();

            // Push the root element onto the stack
            _elementStack.Push(_rootElementHandle);

            // Create canvas
            _canvas = new Canvas(renderer, fontAtlas);

            InitializeInput();
        }

        /// <summary>
        /// Updates the viewport resolution.
        /// </summary>
        public void SetResolution(float width, float height)
        {
            _width = width;
            _height = height;
        }

        public void AddFallbackFont(FontFile font) => _canvas.AddFallbackFont(font);

        public IEnumerable<FontFile> EnumerateSystemFonts() => _canvas.EnumerateSystemFonts();

        public Float2 MeasureText(string text, float pixelSize, FontFile font, float letterSpacing = 0.0f) => _canvas.MeasureText(text, (float)pixelSize, font, (float)letterSpacing);

        public Float2 MeasureText(string text, TextLayoutSettings settings) => _canvas.MeasureText(text, settings);

        public TextLayout CreateLayout(string text, TextLayoutSettings settings) => _canvas.CreateLayout(text, settings);

        /// <summary>
        /// Begins a new UI frame, resetting the element hierarchy.
        /// </summary>
        /// <param name="deltaTime">Time elapsed since the last frame</param>
        /// <param name="frameBufferScale">Optional framebuffer scaling factor</param>
        public void BeginFrame(float deltaTime, Float2? frameBufferScale = null)
        {
            _timer.Restart();
            SetTime(deltaTime);

            _elementStack.Clear();

            // Reset with just the root element
            ClearElements();
            InitializeRootElement(_width, _height);
            _rootElementHandle = GetRootElementHandle();

            // Initialize stacks
            _elementStack.Push(_rootElementHandle);
            _IDStack.Clear();
            _IDStack.Push(0);
            _createdElements.Clear();

            // Reset Canvas
            _canvas.Clear();

            StartInputFrame(frameBufferScale ?? new Float2(1, 1));
        }

        /// <summary>
        /// Ends the current UI frame, performing layout calculations and rendering.
        /// </summary>
        public void EndFrame()
        {
            // Update element styles
            UpdateStyles(DeltaTime, RootElement);

            // Layout phase
            OnEndOfFramePreLayout?.Invoke();
            ElementLayout.Layout(_rootElementHandle, this);
            OnEndOfFramePostLayout?.Invoke();

            // Post-layout callbacks
            CallPostLayoutRecursive(RootElement);

            // Reset rendering state
            _canvas.ResetState();

            // Input and interaction handling
            HandleInteractions();

            // Render all elements
            List<ElementHandle> overlayElements = new();
            List<ElementHandle> modalElements = new();
            RenderElement(_rootElementHandle, Layer.Base, overlayElements, modalElements);
            foreach (var overlay in overlayElements)
                RenderElement(overlay, Layer.Overlay, null, modalElements);
            foreach (var modal in modalElements)
                RenderElement(modal, Layer.Topmost, null, null);

            // Update stats
            CountOfAllElements = (uint)_createdElements.Count;

            // Finalize rendering
            _canvas.Render();
            EndInputFrame();

            // Cleanup
            EndOfFrameCleanupStyles(_createdElements);
            EndOfFrameCleanupStorage();

            // Performance measurement
            _timer.Stop();
            MillisecondsSpent = (float)_timer.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// Calls post-layout callbacks for an element and its children.
        /// </summary>
        private void CallPostLayoutRecursive(ElementHandle handle)
        {
            if (handle.IsValid == false) return;

            _elementStack.Push(handle);
            try
            {
                handle.Data.OnPostLayout?.Invoke(handle, new Rect(handle.Data.X, handle.Data.Y, handle.Data.X + handle.Data.LayoutWidth, handle.Data.Y + handle.Data.LayoutHeight));
                for (int i = 0; i < handle.Data.ChildIndices.Count; i++)
                {
                    var child = new ElementHandle(this, handle.Data.ChildIndices[i]);
                    CallPostLayoutRecursive(child);
                }
            }
            finally
            {
                _elementStack.Pop();
            }
        }

        #endregion

        #region Rendering

        /// <summary>
        /// Renders an element and its children recursively with layering support.
        /// </summary>
        private void RenderElement(in ElementHandle handle, Layer currentLayer, List<ElementHandle>? overlayElements, List<ElementHandle>? modalElements)
        {
            ref var data = ref handle.Data;

            if (data.Visible == false)
                return;

            if (currentLayer == Layer.Base)
            {
                if (data.Layer == Layer.Overlay)
                {
                    overlayElements?.Add(handle);
                    return;
                }
                else if (data.Layer == Layer.Topmost)
                {
                    modalElements?.Add(handle);
                    return;
                }
            }
            else if (currentLayer == Layer.Overlay)
            {
                if (data.Layer == Layer.Topmost)
                {
                    modalElements?.Add(handle);
                    return;
                }
            }

            var rect = new Rect(data.X, data.Y, data.X + data.LayoutWidth, data.Y + data.LayoutHeight);
            _canvas.SaveState();

            // Apply element transform
            Transform2D styleTransform = data._elementStyle.GetTransformForElement(rect);
            _canvas.TransformBy(styleTransform);

            // Draw box shadow before background
            var rounded = (Float4)data._elementStyle.GetValue(GuiProp.Rounded);
            bool hasRounding = rounded.X > 0 || rounded.Y > 0 || rounded.Z > 0 || rounded.W > 0;
            var boxShadow = (BoxShadow)data._elementStyle.GetValue(GuiProp.BoxShadow);
            if (boxShadow.IsVisible)
            {
                float buffer = boxShadow.Blur * 0.5f;
                float sx = rect.Min.X + boxShadow.OffsetX - buffer - boxShadow.Spread;
                float sy = rect.Min.Y + boxShadow.OffsetY - buffer - boxShadow.Spread;
                float sw = rect.Size.X + (buffer * 2) + (boxShadow.Spread * 2);
                float sh = rect.Size.Y + (buffer * 2) + (boxShadow.Spread * 2);
                float radi = (float)(Maths.Max(Maths.Max(rounded.X, rounded.Y), Maths.Max(rounded.Z, rounded.W)));
                _canvas.SetBoxBrush(
                    sx + sw / 2,
                    sy + sh / 2,
                    sw,
                    sh,
                    radi,
                    (float)boxShadow.Blur,
                    boxShadow.Color,
                    Color32.FromArgb(0, boxShadow.Color));

                buffer = (boxShadow.Blur) * 1.0f;
                sx = rect.Min.X + boxShadow.OffsetX - buffer - boxShadow.Spread;
                sy = rect.Min.Y + boxShadow.OffsetY - buffer - boxShadow.Spread;
                sw = rect.Size.X + (buffer * 2) + (boxShadow.Spread * 2);
                sh = rect.Size.Y + (buffer * 2) + (boxShadow.Spread * 2);

                _canvas.RoundedRectFilled(sx, sy, sw, sh,
                    rounded.X, rounded.Y,
                    rounded.Z, rounded.W,
                    Prowl.Vector.Color.White);

                _canvas.ClearBrush();
            }

            // Draw background (gradient overrides background color)
            var gradient = (Gradient)data._elementStyle.GetValue(GuiProp.BackgroundGradient);
            if (gradient.Type != GradientType.None)
            {
                switch (gradient.Type)
                {
                    case GradientType.Linear:
                        float lx1 = rect.Min.X + gradient.X1 * rect.Size.X;
                        float ly1 = rect.Min.Y + gradient.Y1 * rect.Size.Y;
                        float lx2 = rect.Min.X + gradient.X2 * rect.Size.X;
                        float ly2 = rect.Min.Y + gradient.Y2 * rect.Size.Y;
                        _canvas.SetLinearBrush(lx1, ly1, lx2, ly2, gradient.Color1, gradient.Color2);
                        break;
                    case GradientType.Radial:
                        float rcx = rect.Min.X + gradient.X1 * rect.Size.X;
                        float rcy = rect.Min.Y + gradient.Y1 * rect.Size.Y;
                        float ir = gradient.InnerRadius * Maths.Min(rect.Size.X, rect.Size.Y);
                        float or = gradient.OuterRadius * Maths.Min(rect.Size.X, rect.Size.Y);
                        _canvas.SetRadialBrush(rcx, rcy, ir, or, gradient.Color1, gradient.Color2);
                        break;
                    case GradientType.Box:
                        float bcx = rect.Min.X + gradient.X1 * rect.Size.X;
                        float bcy = rect.Min.Y + gradient.Y1 * rect.Size.Y;
                        float bw = gradient.Width * rect.Size.X;
                        float bh = gradient.Height * rect.Size.Y;
                        float brad = gradient.Radius * (float)Maths.Min(rect.Size.X, rect.Size.Y);
                        float bfeather = gradient.Feather * (float)Maths.Min(rect.Size.X, rect.Size.Y);
                        _canvas.SetBoxBrush(bcx, bcy, bw, bh, brad, bfeather, gradient.Color1, gradient.Color2);
                        break;
                }
                if (hasRounding)
                {
                    _canvas.RoundedRectFilled(rect.Min.X, rect.Min.Y, rect.Size.X, rect.Size.Y, rounded.X, rounded.Y, rounded.Z, rounded.W, Color.White);
                }
                else
                {
                    _canvas.RectFilled(rect.Min.X, rect.Min.Y, rect.Size.X, rect.Size.Y, Color.White);
                }
                _canvas.ClearBrush();
            }
            else
            {
                var backgroundColor = (Color)data._elementStyle.GetValue(GuiProp.BackgroundColor);
                if (backgroundColor.A > 0)
                {
                    if (hasRounding)
                        _canvas.RoundedRectFilled(rect.Min.X, rect.Min.Y, rect.Size.X, rect.Size.Y, rounded.X, rounded.Y, rounded.Z, rounded.W, backgroundColor);
                    else
                        _canvas.RectFilled(rect.Min.X, rect.Min.Y, rect.Size.X, rect.Size.Y, backgroundColor);
                }
            }

            // Draw border if needed
            var borderColor = (Color)data._elementStyle.GetValue(GuiProp.BorderColor);
            var borderWidth = (float)data._elementStyle.GetValue(GuiProp.BorderWidth);
            if (borderWidth > 0.0f && borderColor.A > 0)
            {
                _canvas.BeginPath();
                if(hasRounding)
                    _canvas.RoundedRect(rect.Min.X-1, rect.Min.Y-1, rect.Size.X+1, rect.Size.Y+1, rounded.X, rounded.Y, rounded.Z, rounded.W);
                else
                    _canvas.Rect(rect.Min.X-1, rect.Min.Y-1, rect.Size.X+1, rect.Size.Y+1);
                _canvas.SetStrokeColor(borderColor);
                _canvas.SetStrokeWidth(borderWidth);
                _canvas.Stroke();
            }

            // Apply scissor if enabled
            if (data._scissorEnabled)
            {
                _canvas.IntersectScissor(rect.Min.X, rect.Min.Y, rect.Size.X, rect.Size.Y);
            }

            // Draw text style
            if (!string.IsNullOrEmpty(data.Paragraph))
            {
                _canvas.SaveState();
                DrawText(handle, rect.Min.X, rect.Min.Y, (float)rect.Size.X, (float)rect.Size.Y);
                _canvas.RestoreState();
            }


            // Process custom render actions
            if (data._renderCommands != null)
            {
                foreach (var cmd in data._renderCommands)
                {
                    //_canvas.SaveState(); // Making this the Users responsibility to ensure they restore state, since not all custom draws will change state.
                    _elementStack.Push(handle);
                    try
                    {
                        cmd.RenderAction?.Invoke(_canvas, rect);
                    }
                    finally
                    {
                        _elementStack.Pop();
                        //_canvas.RestoreState();
                    }
                }
            }

            // Scrollbars offset the position of children
            bool hasScrollState = this.HasElementStorage(handle, "ScrollState");
            ScrollState scrollState = new();
            if (hasScrollState)
            {
                _canvas.SaveState();
                scrollState = this.GetElementStorage<ScrollState>(handle, "ScrollState");
                var transform = Transform2D.CreateTranslation(-scrollState.Position);
                _canvas.TransformBy(transform);
            }

            // Draw children
            foreach (var childIndex in data.ChildIndices)
            {
                var child = new ElementHandle(this, childIndex);
                RenderElement(child, currentLayer, overlayElements, modalElements);
            }

            // Draw scrollbars if needed
            if (hasScrollState)
            {
                _canvas.RestoreState();

                Scroll flags = data.ScrollFlags;
                bool needsHorizontalScroll = scrollState.ContentSize.X > scrollState.ViewportSize.X && (flags & Scroll.ScrollX) != 0;
                bool needsVerticalScroll = scrollState.ContentSize.Y > scrollState.ViewportSize.Y && (flags & Scroll.ScrollY) != 0;
                bool shouldShowScrollbars = (flags & Scroll.Hidden) == 0 &&
                                           (((flags & Scroll.AutoHide) == 0) || needsHorizontalScroll || needsVerticalScroll);

                // Draw scrollbars if needed
                if (shouldShowScrollbars)
                {
                    // Check for custom scrollbar renderer
                    var customRenderer = data.CustomScrollbarRenderer;

                    if (customRenderer != null)
                    {
                        // Use custom renderer
                        customRenderer(_canvas, rect, scrollState);
                    }
                    else
                    {
                        // Use default scrollbar rendering
                        DrawDefaultScrollbars(_canvas, rect, scrollState, flags);
                    }
                }
            }

            _canvas.RestoreState();
        }

        private void DrawText(in ElementHandle handle, float x, float y, float availableWidth, float availableHeight)
        {
            if (string.IsNullOrWhiteSpace(handle.Data.Paragraph)) return;

            //if (!handle.Data.ProcessedText)
                handle.Data.ProcessText(this, availableWidth);

            Canvas canvas = handle.Owner?.Canvas ?? throw new InvalidOperationException("Owner paper or canvas is not set.");

            var color = (Color32)(Color)handle.Data._elementStyle.GetValue(GuiProp.TextColor);

            // Calculate vertical alignment offset
            float yOffset = 0;
            Float2 textSize;

            if (handle.Data.IsMarkdown == false)
            {
                if (handle.Data._textLayout == null) throw new InvalidOperationException("Text layout is not processed.");

                textSize = (Float2)handle.Data._textLayout.Size;
            }
            else
            {
                if (handle.Data._quillMarkdown == null) throw new InvalidOperationException("Markdown layout is not processed.");

                var markdownResult = handle.Data._quillMarkdown as dynamic;
                textSize = markdownResult?.Size ?? Float2.Zero;
            }

            // Apply vertical alignment based on TextAlignment
            switch (handle.Data.TextAlignment)
            {
                case TextAlignment.MiddleLeft:
                case TextAlignment.MiddleCenter:
                case TextAlignment.MiddleRight:
                    yOffset = (availableHeight - textSize.Y) / 2.0f;
                    break;

                case TextAlignment.BottomLeft:
                case TextAlignment.BottomCenter:
                case TextAlignment.BottomRight:
                    yOffset = availableHeight - textSize.Y;
                    break;

                case TextAlignment.Left:
                case TextAlignment.Center:
                case TextAlignment.Right:
                default:
                    yOffset = 0; // Top alignment (default)
                    break;
            }

            // Apply the calculated offset to the y position
            float finalY = y + yOffset;

            if (handle.Data.IsMarkdown == false)
            {
                canvas.DrawLayout(handle.Data._textLayout, x, finalY, color);
            }
            else
            {
                var markdownResult = handle.Data._quillMarkdown;
                canvas.DrawMarkdown(markdownResult ?? new(), new Float2(x, finalY));
            }
        }

        /// <summary>
        /// Draws the default scrollbars for a scrollable element.
        /// </summary>
        private void DrawDefaultScrollbars(Canvas canvas, Rect rect, ScrollState state, Scroll flags)
        {
            // Calculate scrollbar positions and sizes
            bool hasHorizontal = state.ContentSize.X > state.ViewportSize.X && (flags & Scroll.ScrollX) != 0;
            bool hasVertical = state.ContentSize.Y > state.ViewportSize.Y && (flags & Scroll.ScrollY) != 0;

            if (hasVertical)
            {
                var (trackX, trackY, trackWidth, trackHeight, thumbY, thumbHeight) = state.CalculateVerticalScrollbar(rect, flags);


                // Draw vertical scrollbar track
                canvas.RoundedRectFilled(trackX, trackY, trackWidth, trackHeight, 10, 10, 10, 10, Color32.FromArgb(50, 0, 0, 0));

                // Draw vertical scrollbar thumb - highlight if hovered or dragging
                Color thumbColor = state.IsVerticalScrollbarHovered || state.IsDraggingVertical
                    ? Color32.FromArgb(220, 130, 130, 130)
                    : Color32.FromArgb(180, 100, 100, 100);

                canvas.RoundedRectFilled(trackX + ScrollState.ScrollbarPadding, thumbY + ScrollState.ScrollbarPadding, trackWidth - ScrollState.ScrollbarPadding * 2, thumbHeight - ScrollState.ScrollbarPadding * 2, 10, 10, 10, 10, thumbColor);
            }

            if (hasHorizontal)
            {
                var (trackX, trackY, trackWidth, trackHeight, thumbX, thumbWidth) = state.CalculateHorizontalScrollbar(rect, flags);

                // Draw horizontal scrollbar track
                canvas.RoundedRectFilled(trackX, trackY, trackWidth, trackHeight, 10, 10, 10, 10, Color32.FromArgb(50, 0, 0, 0));

                // Draw horizontal scrollbar thumb - highlight if hovered or dragging
                Color thumbColor = state.IsHorizontalScrollbarHovered || state.IsDraggingHorizontal
                    ? Color32.FromArgb(220, 130, 130, 130)
                    : Color32.FromArgb(180, 100, 100, 100);

                canvas.RoundedRectFilled(thumbX + ScrollState.ScrollbarPadding, trackY + ScrollState.ScrollbarPadding, thumbWidth - ScrollState.ScrollbarPadding * 2, trackHeight - ScrollState.ScrollbarPadding * 2, 10, 10, 10, 10, thumbColor);
            }
        }

        #endregion

        #region Element Management

        /// <summary>
        /// Finds an element by its unique ID.
        /// </summary>
        /// <param name="id">The ID to search for</param>
        /// <returns>The found element or null if not found</returns>
        public ElementHandle FindElementByID(int id)
        {
            var handle = FindElementHandleByID(id);
            return handle;
        }

        /// <summary>
        /// Creates a generic layout container.
        /// </summary>
        /// <param name="stringID">String identifier for the element</param>
        /// <param name="intID">Integer identifier useful for when creating elements in loops</param>
        /// <param name="lineID">Line number based identifier (auto-provided as Source Line Number)</param>
        /// <returns>A builder for configuring the element</returns>
        public ElementBuilder Box(string stringID, int intID = 0, [CallerLineNumber] int lineID = 0)
        {
            ArgumentNullException.ThrowIfNull(stringID);

            int storageHash = HashCode.Combine(CurrentParent.Data.ID, _IDStack.Peek(), stringID, intID, lineID);

            if (!_createdElements.Add(storageHash))
                throw new Exception($"Element already exists with this ID: {stringID}:{intID}:{lineID} = {storageHash} Parent: {CurrentParent.Data.ID}\nPlease use a different ID.");

            var handle = CreateElement(storageHash);
            var builder = new ElementBuilder(this, handle);

            AddChild(ref handle);

            return builder;
        }

        /// <summary>
        /// Creates a row layout container (horizontal layout).
        /// </summary>
        public ElementBuilder Row(string stringID, int intID = 0, [CallerLineNumber] int lineID = 0)
            => Box(stringID, intID, lineID).LayoutType(LayoutType.Row);

        /// <summary>
        /// Creates a column layout container (vertical layout).
        /// </summary>
        public ElementBuilder Column(string stringID, int intID = 0, [CallerLineNumber] int lineID = 0)
            => Box(stringID, intID, lineID).LayoutType(LayoutType.Column);

        /// <summary>
        /// Moves the current parent element to the root of the hierarchy.
        /// Useful for things like popups or modals that need to be rendered at the top level.
        /// You can combine this with Depth to ensure something always renders ontop!
        /// </summary>
        /// <exception cref="Exception"></exception>
        public void MoveToRoot()
        {
            if (CurrentParent.IsValid == false)
                throw new Exception("Not currently inside an Element.");

            var parentHandle = CurrentParent.GetParentHandle();
            if (parentHandle.IsValid)
                parentHandle.Data.ChildIndices.Remove(CurrentParent.Index);

            RootElement.Data.ChildIndices.Add(CurrentParent.Index);
        }

        /// <summary>
        /// Adds a child element to the current parent.
        /// </summary>
        internal void AddChild(ref ElementHandle handle)
        {
            var parentHandle = handle.GetParentHandle();

            if (parentHandle.IsValid)
                throw new Exception("Element already has a parent.");

            handle.Data.ParentIndex = CurrentParent.Index;
            CurrentParent.Data.ChildIndices.Add(handle.Index);
        }

        public void AddActionElement(Action<Canvas, Rect> renderAction)
        {
            var current = CurrentParent;
            AddActionElement(ref current, renderAction);
        }

        /// <summary>
        /// Adds a custom render action to an element.
        /// </summary>
        public void AddActionElement(ref ElementHandle handle, Action<Canvas, Rect> renderAction)
        {
            ArgumentNullException.ThrowIfNull(handle);
            ArgumentNullException.ThrowIfNull(renderAction);

            handle.Data._renderCommands ??= new();
            handle.Data._renderCommands.Add(new ElementRenderCommand {
                Element = handle,
                RenderAction = renderAction,
            });
        }

        #endregion

        #region ID Stack Management

        /// <summary>
        /// Pushes an ID onto the ID stack to create a new scope.
        /// </summary>
        public void PushID(string id)
        {
            PushID(id.GetHashCode());
        }

        /// <summary>
        /// Pushes an ID onto the ID stack to create a new scope.
        /// </summary>
        public void PushID(int id)
        {
            _IDStack.Push(HashCode.Combine(id, _IDStack.Peek()));
        }

        /// <summary>
        /// Pops the current ID from the stack, returning to the parent scope.
        /// </summary>
        public void PopID()
        {
            if (_IDStack.Count > 1)
                _IDStack.Pop();
            else
                throw new Exception("Cannot pop the root ID.");
        }

        #endregion

        #region Element Storage

        /// <summary> Get a value from the global GUI storage this persists across all Frames and Elements </summary>
        public T GetRootStorage<T>(string key) => GetElementStorage<T>(_rootElementHandle, key, default);
        /// <summary> Set a value in the root element </summary>
        public void SetRootStorage<T>(string key, T value) => SetElementStorage(_rootElementHandle, key, value);

        /// <summary> Get a value from the current element's storage </summary>
        public T GetElementStorage<T>(string key, T defaultValue = default) => GetElementStorage(CurrentParent, key, defaultValue);

        /// <summary> Get a value from the current element's storage </summary>
        public T GetElementStorage<T>(ElementHandle el, string key, T defaultValue = default)
        {
            if (!_storage.TryGetValue(el.Data.ID, out var storage))
                return defaultValue;

            if (storage.ContainsKey(key))
                return (T)storage[key]!;

            return defaultValue;
        }

        /// <summary> Check if a key exists in the current element's storage </summary>
        public bool HasElementStorage(ElementHandle el, string key) => _storage.TryGetValue(el.Data.ID, out var storage) && storage.ContainsKey(key);

        /// <summary> Set a value in the current element's storage </summary>
        public void SetElementStorage<T>(string key, T value) => SetElementStorage(CurrentParent, key, value);
        /// <summary> Set a value in the current element's storage </summary>
        public void SetElementStorage<T>(ElementHandle el, string key, T value)
        {
            if (!_storage.TryGetValue(el.Data.ID, out var storage))
                _storage[el.Data.ID] = storage = [];

            storage[key] = value;
        }

        private void EndOfFrameCleanupStorage()
        {
            var keys = _storage.Keys.ToArray();
            foreach (var storedID in keys)
            {
                if (_createdElements.Contains(storedID))
                    continue;

                // We didnt create this element this frame, so it no longer exists, delete any storage for it.
                _storage.Remove(storedID);
            }
        }

        #endregion

        #region Layout Helpers

        /// <summary>
        /// Creates a stretch unit value with the specified factor.
        /// </summary>
        public UnitValue Stretch(float factor = 1f) => UnitValue.Stretch(factor);

        /// <summary>
        /// Creates a pixel-based unit value.
        /// </summary>
        public UnitValue Pixels(float value) => UnitValue.Pixels(value);

        /// <summary>
        /// Creates a percentage-based unit value with optional pixel offset.
        /// </summary>
        public UnitValue Percent(float value, float pixelOffset = 0f) => UnitValue.Percentage(value, pixelOffset);

        /// <summary>
        /// Creates an auto-sized unit value.
        /// </summary>
        public UnitValue Auto => UnitValue.Auto;

        #endregion
    }
}
