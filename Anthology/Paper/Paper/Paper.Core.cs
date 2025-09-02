using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;

using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;
using Prowl.Scribe;

namespace Prowl.PaperUI
{
    public partial class Paper
    {
        #region Fields & Properties

        // Layout and hierarchy management
        private ElementHandle _rootElementHandle;
        internal Stack<ElementHandle> _elementStack = new Stack<ElementHandle>();
        private readonly Stack<ulong> _IDStack = new();
        private readonly Dictionary<ulong, Element> _createdElements = [];

        private readonly Dictionary<ulong, Hashtable> _storage = [];

        // Rendering context
        private Canvas _canvas;
        private ICanvasRenderer _renderer;
        private double _width;
        private double _height;
        private Stopwatch _timer = new();

        // Events
        public Action? OnEndOfFramePreLayout = null;
        public Action? OnEndOfFramePostLayout = null;

        // Performance metrics
        public double MillisecondsSpent { get; private set; }
        public uint CountOfAllElements { get; private set; }

        // Public properties
        public Rect ScreenRect => new Rect(0, 0, _width, _height);
        public Element RootElement => GetElementWrapper(_rootElementHandle);
        public Canvas Canvas => _canvas;

        /// <summary>
        /// Gets the current parent element in the element hierarchy.
        /// </summary>
        public Element CurrentParent => GetElementWrapper(_elementStack.Peek());

        #endregion

        #region Initialization and Frame Management

        /// <summary>
        /// Initializes Paper with a renderer and dimensions.
        /// </summary>
        /// <param name="renderer">The canvas renderer implementation</param>
        /// <param name="width">Viewport width</param>
        /// <param name="height">Viewport height</param>
        /// <param name="fontAtlas">Font atlas settings for text rendering</param>
        public Paper(ICanvasRenderer renderer, double width, double height, FontAtlasSettings fontAtlas)
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
        public void SetResolution(double width, double height)
        {
            _width = width;
            _height = height;
        }

        public void AddFont(string fontPath)
        {
            _canvas.AddFont(fontPath);
        }

        public void AddFont(byte[] fontData)
        {
            _canvas.AddFont(fontData);
        }

        public void LoadSystemFonts(params string[] priorityFonts)
        {
            _canvas.LoadSystemFonts(priorityFonts);
        }

        /// <summary>
        /// Begins a new UI frame, resetting the element hierarchy.
        /// </summary>
        /// <param name="deltaTime">Time elapsed since the last frame</param>
        /// <param name="frameBufferScale">Optional framebuffer scaling factor</param>
        public void BeginFrame(double deltaTime, Vector2? frameBufferScale = null)
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

            StartInputFrame(frameBufferScale ?? new Vector2(1, 1));
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
            RootElement.Layout();
            OnEndOfFramePostLayout?.Invoke();

            // Post-layout callbacks
            CallPostLayoutRecursive(RootElement);

            // Reset rendering state
            _canvas.ResetState();

            // Input and interaction handling
            HandleInteractions();

            // Render all elements
            List<Element> overlayElements = new();
            List<Element> modalElements = new();
            RenderElement(RootElement, Layer.Base, overlayElements, modalElements);
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
            OfOfFrameCleanupStyles(_createdElements);

            // Performance measurement
            _timer.Stop();
            MillisecondsSpent = _timer.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// Calls post-layout callbacks for an element and its children.
        /// </summary>
        private void CallPostLayoutRecursive(Element element)
        {
            _elementStack.Push(element.Handle);
            try
            {
                element?.OnPostLayout?.Invoke(element, new Rect(element.X, element.Y, element.LayoutWidth, element.LayoutHeight));
                for (int i = 0; i < element?.Children.Count; i++)
                {
                    var child = element.Children[i];
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
        private void RenderElement(Element element, Layer currentLayer, List<Element>? overlayElements, List<Element>? modalElements)
        {
            if (element.Visible == false)
                return;

            if (currentLayer == Layer.Base)
            {
                if (element.Layer == Layer.Overlay)
                {
                    overlayElements?.Add(element);
                    return;
                }
                else if (element.Layer == Layer.Topmost)
                {
                    modalElements?.Add(element);
                    return;
                }
            }
            else if (currentLayer == Layer.Overlay)
            {
                if (element.Layer == Layer.Topmost)
                {
                    modalElements?.Add(element);
                    return;
                }
            }

            var rect = new Rect(element.X, element.Y, element.LayoutWidth, element.LayoutHeight);
            _canvas.SaveState();

            // Apply element transform
            Transform2D styleTransform = element._elementStyle.GetTransformForElement(rect);
            _canvas.TransformBy(styleTransform);

            // Draw box shadow before background
            var rounded = (Vector4)element._elementStyle.GetValue(GuiProp.Rounded);
            var boxShadow = (BoxShadow)element._elementStyle.GetValue(GuiProp.BoxShadow);
            if (boxShadow.IsVisible)
            {
                _canvas.SaveState();
                double buffer = boxShadow.Blur * 0.5;
                double sx = rect.x + boxShadow.OffsetX - buffer - boxShadow.Spread;
                double sy = rect.y + boxShadow.OffsetY - buffer - boxShadow.Spread;
                double sw = rect.width + (buffer * 2) + (boxShadow.Spread * 2);
                double sh = rect.height + (buffer * 2) + (boxShadow.Spread * 2);
                float radi = (float)(Math.Max(Math.Max(rounded.x, rounded.y), Math.Max(rounded.z, rounded.w)));
                _canvas.SetBoxBrush(
                    sx + sw / 2,
                    sy + sh / 2,
                    sw,
                    sh,
                    radi,
                    (float)boxShadow.Blur,
                    boxShadow.Color,
                    Color.FromArgb(0, boxShadow.Color));

                buffer = (boxShadow.Blur) * 1.0;
                sx = rect.x + boxShadow.OffsetX - buffer - boxShadow.Spread;
                sy = rect.y + boxShadow.OffsetY - buffer - boxShadow.Spread;
                sw = rect.width + (buffer * 2) + (boxShadow.Spread * 2);
                sh = rect.height + (buffer * 2) + (boxShadow.Spread * 2);

                _canvas.RoundedRectFilled(sx, sy, sw, sh,
                    rounded.x, rounded.y,
                    rounded.z, rounded.w,
                    Color.White);
                _canvas.RestoreState();
            }

            // Draw background (gradient overrides background color)
            var gradient = (Gradient)element._elementStyle.GetValue(GuiProp.BackgroundGradient);
            if (gradient.Type != GradientType.None)
            {
                switch (gradient.Type)
                {
                    case GradientType.Linear:
                        double lx1 = rect.x + gradient.X1 * rect.width;
                        double ly1 = rect.y + gradient.Y1 * rect.height;
                        double lx2 = rect.x + gradient.X2 * rect.width;
                        double ly2 = rect.y + gradient.Y2 * rect.height;
                        _canvas.SetLinearBrush(lx1, ly1, lx2, ly2, gradient.Color1, gradient.Color2);
                        break;
                    case GradientType.Radial:
                        double rcx = rect.x + gradient.X1 * rect.width;
                        double rcy = rect.y + gradient.Y1 * rect.height;
                        double ir = gradient.InnerRadius * Math.Min(rect.width, rect.height);
                        double or = gradient.OuterRadius * Math.Min(rect.width, rect.height);
                        _canvas.SetRadialBrush(rcx, rcy, ir, or, gradient.Color1, gradient.Color2);
                        break;
                    case GradientType.Box:
                        double bcx = rect.x + gradient.X1 * rect.width;
                        double bcy = rect.y + gradient.Y1 * rect.height;
                        double bw = gradient.Width * rect.width;
                        double bh = gradient.Height * rect.height;
                        float brad = gradient.Radius * (float)Math.Min(rect.width, rect.height);
                        float bfeather = gradient.Feather * (float)Math.Min(rect.width, rect.height);
                        _canvas.SetBoxBrush(bcx, bcy, bw, bh, brad, bfeather, gradient.Color1, gradient.Color2);
                        break;
                }
                _canvas.RoundedRectFilled(rect.x, rect.y, rect.width, rect.height, rounded.x, rounded.y, rounded.z, rounded.w, Color.White);
                _canvas.ClearBrush();
            }
            else
            {
                var backgroundColor = (Color)element._elementStyle.GetValue(GuiProp.BackgroundColor);
                if (backgroundColor.A > 0)
                    _canvas.RoundedRectFilled(rect.x, rect.y, rect.width, rect.height, rounded.x, rounded.y, rounded.z, rounded.w, backgroundColor);
            }

            // Draw border if needed
            var borderColor = (Color)element._elementStyle.GetValue(GuiProp.BorderColor);
            var borderWidth = (double)element._elementStyle.GetValue(GuiProp.BorderWidth);
            if (borderWidth > 0.0f && borderColor.A > 0)
            {
                _canvas.BeginPath();
                _canvas.RoundedRect(rect.x-1, rect.y-1, rect.width+1, rect.height+1, rounded.x, rounded.y, rounded.z, rounded.w);
                _canvas.SetStrokeColor(borderColor);
                _canvas.SetStrokeWidth(borderWidth);
                _canvas.Stroke();
            }

            // Apply scissor if enabled
            if (element._scissorEnabled)
            {
                _canvas.IntersectScissor(rect.x, rect.y, rect.width, rect.height);
            }

            // Draw text style
            if (!string.IsNullOrEmpty(element.Paragraph))
            {
                _canvas.SaveState();
                element.DrawText(rect.x, rect.y, (float)rect.width, (float)rect.height);
                _canvas.RestoreState();
            }


            // Process custom render actions
            if (element._renderCommands != null)
            {
                foreach (var cmd in element._renderCommands)
                {
                    _canvas.SaveState();
                    _elementStack.Push(element.Handle);
                    try
                    {
                        cmd.RenderAction?.Invoke(_canvas, rect);
                    }
                    finally
                    {
                        _elementStack.Pop();
                        _canvas.RestoreState();
                    }
                }
            }

            // Scrollbars offset the position of children
            bool hasScrollState = this.HasElementStorage(element, "ScrollState");
            ScrollState scrollState = new();
            if (hasScrollState)
            {
                _canvas.SaveState();
                scrollState = this.GetElementStorage<ScrollState>(element, "ScrollState");
                var transform = Transform2D.CreateTranslation(-scrollState.Position);
                _canvas.TransformBy(transform);
            }

            // Draw children
            foreach (var child in element.Children)
                RenderElement(child, currentLayer, overlayElements, modalElements);

            // Draw scrollbars if needed
            if (hasScrollState)
            {
                _canvas.RestoreState();

                Scroll flags = element.ScrollFlags;
                bool needsHorizontalScroll = scrollState.ContentSize.x > scrollState.ViewportSize.x && (flags & Scroll.ScrollX) != 0;
                bool needsVerticalScroll = scrollState.ContentSize.y > scrollState.ViewportSize.y && (flags & Scroll.ScrollY) != 0;
                bool shouldShowScrollbars = (flags & Scroll.Hidden) == 0 &&
                                           (((flags & Scroll.AutoHide) == 0) || needsHorizontalScroll || needsVerticalScroll);

                // Draw scrollbars if needed
                if (shouldShowScrollbars)
                {
                    // Check for custom scrollbar renderer
                    var customRenderer = element.CustomScrollbarRenderer;

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

        /// <summary>
        /// Draws the default scrollbars for a scrollable element.
        /// </summary>
        private void DrawDefaultScrollbars(Canvas canvas, Rect rect, ScrollState state, Scroll flags)
        {
            // Calculate scrollbar positions and sizes
            bool hasHorizontal = state.ContentSize.x > state.ViewportSize.x && (flags & Scroll.ScrollX) != 0;
            bool hasVertical = state.ContentSize.y > state.ViewportSize.y && (flags & Scroll.ScrollY) != 0;

            if (hasVertical)
            {
                var (trackX, trackY, trackWidth, trackHeight, thumbY, thumbHeight) = state.CalculateVerticalScrollbar(rect, flags);


                // Draw vertical scrollbar track
                canvas.RoundedRectFilled(trackX, trackY, trackWidth, trackHeight, 10, 10, 10, 10, Color.FromArgb(50, 0, 0, 0));

                // Draw vertical scrollbar thumb - highlight if hovered or dragging
                Color thumbColor = state.IsVerticalScrollbarHovered || state.IsDraggingVertical
                    ? Color.FromArgb(220, 130, 130, 130)
                    : Color.FromArgb(180, 100, 100, 100);

                canvas.RoundedRectFilled(trackX + ScrollState.ScrollbarPadding, thumbY, trackWidth - ScrollState.ScrollbarPadding * 2, thumbHeight, 10, 10, 10, 10, thumbColor);
            }

            if (hasHorizontal)
            {
                var (trackX, trackY, trackWidth, trackHeight, thumbX, thumbWidth) = state.CalculateHorizontalScrollbar(rect, flags);

                // Draw horizontal scrollbar track
                canvas.RoundedRectFilled(trackX, trackY, trackWidth, trackHeight, 10, 10, 10, 10, Color.FromArgb(50, 0, 0, 0));

                // Draw horizontal scrollbar thumb - highlight if hovered or dragging
                Color thumbColor = state.IsHorizontalScrollbarHovered || state.IsDraggingHorizontal
                    ? Color.FromArgb(220, 130, 130, 130)
                    : Color.FromArgb(180, 100, 100, 100);

                canvas.RoundedRectFilled(thumbX, trackY + ScrollState.ScrollbarPadding, thumbWidth, trackHeight - ScrollState.ScrollbarPadding * 2, 10, 10, 10, 10, thumbColor);
            }
        }

        #endregion

        #region Element Management

        /// <summary>
        /// Finds an element by its unique ID.
        /// </summary>
        /// <param name="id">The ID to search for</param>
        /// <returns>The found element or null if not found</returns>
        public Element? FindElementByID(ulong id)
        {
            if (_createdElements.TryGetValue(id, out var element))
                return element;
                
            var handle = FindElementHandleByID(id);
            return handle.HasValue ? GetElementWrapper(handle.Value) : null;
        }

        /// <summary>
        /// Creates a generic layout container.
        /// </summary>
        /// <param name="stringID">String identifier for the element</param>
        /// <param name="intID">Line number based identifier (auto-provided as Source Line Number)</param>
        /// <returns>A builder for configuring the element</returns>
        public ElementBuilder Box(string stringID, [CallerLineNumber] int intID = 0)
        {
            ArgumentNullException.ThrowIfNull(stringID);

            ulong storageHash = 0;
            if(_IDStack.Count > 0)
                storageHash = (ulong)HashCode.Combine(CurrentParent.ID, _IDStack.Peek(), stringID, intID);
            else
                storageHash = (ulong)HashCode.Combine(CurrentParent.ID, stringID, intID);

            if (_createdElements.ContainsKey(storageHash))
                throw new Exception("Element already exists with this ID: " + stringID + ":" + intID + " = " + storageHash + " Parent: " + CurrentParent.ID + "\nPlease use a different ID.");

            var handle = CreateElement(storageHash);
            var element = GetElementWrapper(handle);
            var builder = new ElementBuilder(this, element);
            _createdElements.Add(storageHash, element);

            AddChild(element);

            return builder;
        }

        /// <summary>
        /// Creates a row layout container (horizontal layout).
        /// </summary>
        public ElementBuilder Row(string stringID, [CallerLineNumber] int intID = 0)
            => Box(stringID, intID).LayoutType(LayoutType.Row);

        /// <summary>
        /// Creates a column layout container (vertical layout).
        /// </summary>
        public ElementBuilder Column(string stringID, [CallerLineNumber] int intID = 0)
            => Box(stringID, intID).LayoutType(LayoutType.Column);

        /// <summary>
        /// Moves the current parent element to the root of the hierarchy.
        /// Useful for things like popups or modals that need to be rendered at the top level.
        /// You can combine this with Depth to ensure something always renders ontop!
        /// </summary>
        /// <exception cref="Exception"></exception>
        public void MoveToRoot()
        {
            if (CurrentParent == null)
                throw new Exception("Not currently inside an Element.");

            if(CurrentParent.Parent != null)
                CurrentParent.Parent.Children.Remove(CurrentParent);

            RootElement.Children.Add(CurrentParent);
        }

        /// <summary>
        /// Adds a child element to the current parent.
        /// </summary>
        internal void AddChild(Element element)
        {
            if (element.Parent != null)
                throw new Exception("Element already has a parent.");

            element.Parent = CurrentParent;
            CurrentParent.Children.Add(element);
        }

        public void AddActionElement(Action<Canvas, Rect> renderAction) => AddActionElement(CurrentParent, renderAction);

        /// <summary>
        /// Adds a custom render action to an element.
        /// </summary>
        public void AddActionElement(Element element, Action<Canvas, Rect> renderAction)
        {
            ArgumentNullException.ThrowIfNull(element);
            ArgumentNullException.ThrowIfNull(renderAction);

            element._renderCommands ??= new();
            element._renderCommands.Add(new ElementRenderCommand {
                Element = element,
                RenderAction = renderAction,
            });
        }

        #endregion

        #region ID Stack Management

        /// <summary>
        /// Pushes an ID onto the ID stack to create a new scope.
        /// </summary>
        public void PushID(ulong id)
        {
            _IDStack.Push(id);
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
        public T GetRootStorage<T>(string key) => GetElementStorage<T>(RootElement, key, default);
        /// <summary> Set a value in the root element </summary>
        public void SetRootStorage<T>(string key, T value) => SetElementStorage(RootElement, key, value);

        /// <summary> Get a value from the current element's storage </summary>
        public T GetElementStorage<T>(string key, T defaultValue = default) => GetElementStorage(CurrentParent, key, defaultValue);

        /// <summary> Get a value from the current element's storage </summary>
        public T GetElementStorage<T>(Element el, string key, T defaultValue = default)
        {
            if (!_storage.TryGetValue(el.ID, out var storage))
                return defaultValue;

            if (storage.ContainsKey(key))
                return (T)storage[key]!;

            return defaultValue;
        }

        /// <summary> Check if a key exists in the current element's storage </summary>
        public bool HasElementStorage(Element el, string key) => _storage.TryGetValue(el.ID, out var storage) && storage.ContainsKey(key);

        /// <summary> Set a value in the current element's storage </summary>
        public void SetElementStorage<T>(string key, T value) => SetElementStorage(CurrentParent, key, value);
        /// <summary> Set a value in the current element's storage </summary>
        public void SetElementStorage<T>(Element el, string key, T value)
        {
            if (!_storage.TryGetValue(el.ID, out var storage))
                _storage[el.ID] = storage = [];

            storage[key] = value;
        }

        #region Text Field Storage Helpers

        /// <summary>
        /// Gets the current value of a text field.
        /// </summary>
        public string GetTextFieldValue(Element element)
        {
            return this.GetElementStorage<string>(element, "Value", "");
        }

        /// <summary>
        /// Sets the value of a text field.
        /// </summary>
        public void SetTextFieldValue(Element element, string value)
        {
            this.SetElementStorage(element, "Value", value);
            this.SetElementStorage(element, "CursorPosition", value.Length);
            this.SetElementStorage(element, "SelectionStart", -1);
            this.SetElementStorage(element, "SelectionEnd", -1);
        }

        #endregion

        #endregion

        #region Layout Helpers

        /// <summary>
        /// Creates a stretch unit value with the specified factor.
        /// </summary>
        public UnitValue Stretch(double factor = 1f) => UnitValue.Stretch(factor);

        /// <summary>
        /// Creates a pixel-based unit value.
        /// </summary>
        public UnitValue Pixels(double value) => UnitValue.Pixels(value);

        /// <summary>
        /// Creates a percentage-based unit value with optional pixel offset.
        /// </summary>
        public UnitValue Percent(double value, double pixelOffset = 0f) => UnitValue.Percentage(value, pixelOffset);

        /// <summary>
        /// Creates an auto-sized unit value.
        /// </summary>
        public UnitValue Auto => UnitValue.Auto;

        #endregion
    }
}
