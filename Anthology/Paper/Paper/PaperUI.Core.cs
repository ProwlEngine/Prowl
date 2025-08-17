using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;

using FontStashSharp;

using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

namespace Prowl.PaperUI
{
    public static partial class Paper
    {
        #region Fields & Properties
        // Context handling
        public class PaperContext
        {
            internal int ThreadId { get; set; } = Environment.CurrentManagedThreadId;
            public LayoutEngine.Element RootElement;
            public Stack<LayoutEngine.Element> ElementStack = new();
            public Stack<ulong> IDStack = new();
            public Dictionary<ulong, Element> CreatedElements = new();
            public Dictionary<ulong, Hashtable> Storage = new();
            public Canvas Canvas;
            public ICanvasRenderer Renderer;
            public double Width;
            public double Height;
            public Stopwatch Timer = new();
            public double MillisecondsSpent;
            public uint CountOfAllElements;

            // Subsystem state
            public InputState Input = new();
            public InteractionState Interaction = new();
            public StyleState Styles = new();
            public WindowingState Windows = new();

            // Custom data bag for user specific information
            public Dictionary<string, object> Data = new();

            // Input state container
            public class InputState
            {
                public bool CapturedKeyboard = false;
                public bool WantsCaptureKeyboard;

                public bool[] KeyCurState;
                public bool[] KeyPrevState;
                public double[] KeyPressedTime;
                public double[] KeyRepeatTimer;
                public bool[] KeyRepeating;
                public PaperKey LastKeyPressed = PaperKey.Unknown;

                public bool KeyAutoRepeatEnabled = true;
                public double AutoRepeatDelay = 0.8f;
                public double AutoRepeatRate = 0.05f;

                public bool[] PointerCurState;
                public bool[] PointerPrevState;
                public double[] PointerPressedTime;
                public Vector2[] PointerClickPos;
                public PaperMouseBtn LastButtonPressed = PaperMouseBtn.Unknown;
                public Vector2 PreviousPointerPos = Vector2.zero;
                public Vector2 PointerPos = Vector2.zero;
                public double PointerWheel = 0;
                public double[] PointerLastClickTime;
                public Vector2[] PointerLastClickPos;

                public Queue<char> InputString = new();

                public double DeltaTime = 0.016f;
                public double Time = 0f;
                public Vector2 FrameBufferScale = Vector2.one;

                public IClipboardHandler ClipboardHandler;

                public event Action<Vector2> OnPointerPosSet;
                public event Action<bool> OnCursorVisibilitySet;

                public void InvokePointerPos(Vector2 pos) => OnPointerPosSet?.Invoke(pos);
                public void InvokeCursorVisibility(bool v) => OnCursorVisibilitySet?.Invoke(v);
            }

            // Interaction state container
            public class InteractionState
            {
                public ulong TheHoveredElementId = 0;
                public ulong ActiveElementId = 0;
                public ulong FocusedElementId = 0;
                public Dictionary<ulong, bool> WasHoveredState = new();
                public Dictionary<ulong, Vector2> DragStartPos = new();
                public HashSet<ulong> ElementsInBubblePath = new();
                public Dictionary<ulong, bool> IsDragging = new();
            }

            // Style state container
            public class StyleState
            {
                internal Dictionary<ulong, ElementStyle> ActiveStyles = new();
                internal Dictionary<string, StyleTemplate> StyleTemplates = new();
            }

            // Window manager state container
            public class WindowingState
            {
                public Dictionary<string, Prowl.PaperUI.Extras.WindowState> WindowStates = new();
                public SpriteFontBase WindowFont;
                public HashSet<string> WindowsPendingCloseRequest = new();
                public byte SetNextWindowPosition = 0;
                public Vector2 NextPosition = new Vector2(0, 0);
                public byte SetNextWindowSize = 0;
                public Vector2 NextSize = new Vector2(0, 0);
                public bool? NextWindowIsResizable = null;
                public bool? NextWindowIsDraggable = null;
                public bool? NextWindowIsModal = null;
                public Vector2? NextWindowMinSize = null;
            }
        }

        private static readonly ThreadLocal<Stack<PaperContext>> _contextStack = new(() => new Stack<PaperContext>());
        private static Stack<PaperContext> ContextStack => _contextStack.Value!;

        private static PaperContext Current => ContextStack.Peek();

        /// <summary>
        /// Gets the currently active PaperUI context.
        /// </summary>
        public static PaperContext CurrentContext => Current;

        // Layout and hierarchy management
        private static LayoutEngine.Element _rootElement
        {
            get => Current.RootElement;
            set => Current.RootElement = value;
        }

        internal static Stack<LayoutEngine.Element> _elementStack => Current.ElementStack;
        private static Stack<ulong> _IDStack => Current.IDStack;
        private static Dictionary<ulong, Element> _createdElements => Current.CreatedElements;

        private static Dictionary<ulong, Hashtable> _storage => Current.Storage;

        // Rendering context
        private static Canvas _canvas
        {
            get => Current.Canvas;
            set => Current.Canvas = value;
        }

        private static ICanvasRenderer _renderer
        {
            get => Current.Renderer;
            set => Current.Renderer = value;
        }

        private static double _width
        {
            get => Current.Width;
            set => Current.Width = value;
        }

        private static double _height
        {
            get => Current.Height;
            set => Current.Height = value;
        }

        private static Stopwatch _timer => Current.Timer;

        // Performance metrics
        public static double MillisecondsSpent
        {
            get => Current.MillisecondsSpent;
            private set => Current.MillisecondsSpent = value;
        }

        public static uint CountOfAllElements
        {
            get => Current.CountOfAllElements;
            private set => Current.CountOfAllElements = value;
        }

        // Public properties
        public static Rect ScreenRect => new Rect(0, 0, _width, _height);
        public static Element RootElement => _rootElement;
        public static Canvas Canvas => _canvas;

        /// <summary>
        /// Gets the current parent element in the element hierarchy.
        /// </summary>
        public static LayoutEngine.Element CurrentParent => _elementStack.Peek();

        #endregion

        #region Initialization and Frame Management

        /// <summary>
        /// Initializes the ImGui system with a renderer and dimensions.
        /// </summary>
        /// <param name="renderer">The canvas renderer implementation</param>
        /// <param name="width">Viewport width</param>
        /// <param name="height">Viewport height</param>
        public static void Initialize(ICanvasRenderer renderer, double width, double height)
            => PushContext(renderer, width, height);

        /// <summary>
        /// Creates and pushes a new PaperUI context onto the stack.
        /// </summary>
        public static PaperContext PushContext(ICanvasRenderer renderer, double width, double height)
        {
            var ctx = new PaperContext { ThreadId = Environment.CurrentManagedThreadId };
            ContextStack.Push(ctx);

            _width = width;
            _height = height;
            _renderer = renderer;

            // Create root element
            _rootElement = new LayoutEngine.Element
            {
                ID = 0
            };
            _rootElement._elementStyle.SetDirectValue(GuiProp.Width, UnitValue.Pixels(_width));
            _rootElement._elementStyle.SetDirectValue(GuiProp.Height, UnitValue.Pixels(_height));

            // Clear collections
            _elementStack.Clear();
            _IDStack.Clear();
            _createdElements.Clear();

            // Push the root element onto the stack
            _elementStack.Push(_rootElement);

            // Create canvas
            _canvas = new Canvas(renderer);

            // Initialize input
            InitializeInput();

            return ctx;
        }

        /// <summary>
        /// Pushes an existing PaperUI context onto the stack.
        /// </summary>
        public static void PushContext(PaperContext ctx)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (ctx.ThreadId != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Context can only be used on the thread it was created on.");
            ContextStack.Push(ctx);
        }

        /// <summary>
        /// Pops the current PaperUI context from the stack.
        /// </summary>
        public static void PopContext()
        {
            if (ContextStack.Count > 0)
                ContextStack.Pop();
        }

        /// <summary>
        /// Updates the viewport resolution.
        /// </summary>
        public static void SetResolution(double width, double height)
        {
            _width = width;
            _height = height;
        }

        /// <summary>
        /// Begins a new UI frame, resetting the element hierarchy.
        /// </summary>
        /// <param name="deltaTime">Time elapsed since the last frame</param>
        /// <param name="frameBufferScale">Optional framebuffer scaling factor</param>
        public static void BeginFrame(double deltaTime, Vector2? frameBufferScale = null)
        {
            _timer.Restart();
            SetTime(deltaTime);

            _elementStack.Clear();

            // Reset with just the root element
            _rootElement = new LayoutEngine.Element {
                ID = 0
            };
            _rootElement._elementStyle.SetDirectValue(GuiProp.Width, UnitValue.Pixels(_width));
            _rootElement._elementStyle.SetDirectValue(GuiProp.Height, UnitValue.Pixels(_height));

            // Initialize stacks
            _elementStack.Push(_rootElement);
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
        public static void EndFrame()
        {
            // Update element styles
            UpdateStyles(DeltaTime, _rootElement);

            // Layout phase
            _rootElement.Layout();

            // Post-layout callbacks
            CallPostLayoutRecursive(_rootElement);

            // Reset rendering state
            _canvas.ResetState();

            // Input and interaction handling
            HandleInteractions();

            // Render all elements
            RenderElement(RootElement);

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
        private static void CallPostLayoutRecursive(LayoutEngine.Element element)
        {
            _elementStack.Push(element);
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
        /// Renders an element and its children recursively.
        /// </summary>
        private static void RenderElement(Element element)
        {
            if (element.Visible == false)
                return;

            var rect = new Rect(element.X, element.Y, element.LayoutWidth, element.LayoutHeight);
            _canvas.SaveState();

            // Apply element transform
            Transform2D styleTransform = element._elementStyle.GetTransformForElement(rect);
            _canvas.TransformBy(styleTransform);

            // Draw background
            var rounded = (Vector4)element._elementStyle.GetValue(GuiProp.Rounded);
            var backgroundColor = (Color)element._elementStyle.GetValue(GuiProp.BackgroundColor);
            if (backgroundColor.A > 0)
                _canvas.RoundedRectFilled(rect.x, rect.y, rect.width, rect.height, rounded.x, rounded.y, rounded.z, rounded.w, backgroundColor);

            // Draw border if needed
            var borderColor = (Color)element._elementStyle.GetValue(GuiProp.BorderColor);
            var borderWidth = (double)element._elementStyle.GetValue(GuiProp.BorderWidth);
            if (borderWidth > 0.0f && borderColor.A > 0)
            {
                _canvas.BeginPath();
                _canvas.RoundedRect(rect.x, rect.y, rect.width, rect.height, rounded.x, rounded.y, rounded.z, rounded.w);
                _canvas.SetStrokeColor(borderColor);
                _canvas.SetStrokeWidth(borderWidth);
                _canvas.Stroke();
            }

            // Apply scissor if enabled
            if (element._scissorEnabled)
            {
                _canvas.IntersectScissor(rect.x, rect.y, rect.width, rect.height);
            }

            // Process render commands
            if (element._renderCommands != null)
            {
                foreach (var cmd in element._renderCommands)
                {
                    _canvas.SaveState();
                    if (cmd.Type == ElementRenderCommand.ElementType.Text)
                        cmd.Text?.Draw(_canvas, rect);
                    else if (cmd.Type == ElementRenderCommand.ElementType.RenderAction)
                    {
                        _elementStack.Push(element);
                        try
                        {
                            cmd.RenderAction?.Invoke(_canvas, rect);
                        }
                        finally
                        {
                            _elementStack.Pop();
                        }
                    }
                    _canvas.RestoreState();
                }
            }

            // Scrollbars offset the position of children
            bool hasScrollState = Paper.HasElementStorage(element, "ScrollState");
            ScrollState scrollState = new();
            if (hasScrollState)
            {
                _canvas.SaveState();
                scrollState = Paper.GetElementStorage<ScrollState>(element, "ScrollState");
                var transform = Transform2D.CreateTranslation(-scrollState.Position);
                _canvas.TransformBy(transform);
            }

            // Draw children sorted by Z-order
            var sortedChildren = element.GetSortedChildren;
            foreach (var child in sortedChildren)
                RenderElement(child);

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
        private static void DrawDefaultScrollbars(Canvas canvas, Rect rect, ScrollState state, Scroll flags)
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
        public static Element? FindElementByID(ulong id)
        {
            if (_createdElements.TryGetValue(id, out var element))
                return element;
            return null;
        }

        /// <summary>
        /// Creates a generic layout container.
        /// </summary>
        /// <param name="stringID">String identifier for the element</param>
        /// <param name="intID">Line number based identifier (auto-provided as Source Line Number)</param>
        /// <returns>A builder for configuring the element</returns>
        public static ElementBuilder Box(string stringID, [CallerLineNumber] int intID = 0)
        {
            ArgumentNullException.ThrowIfNull(stringID);

            ulong storageHash = 0;
            if(_IDStack.Count > 0)
                storageHash = (ulong)HashCode.Combine(CurrentParent.ID, _IDStack.Peek(), stringID, intID);
            else
                storageHash = (ulong)HashCode.Combine(CurrentParent.ID, stringID, intID);

            if (_createdElements.ContainsKey(storageHash))
                throw new Exception("Element already exists with this ID: " + stringID + ":" + intID + " = " + storageHash + " Parent: " + CurrentParent.ID + "\nPlease use a different ID.");

            var builder = new ElementBuilder(storageHash);
            _createdElements.Add(storageHash, builder._element);

            AddChild(builder._element);

            return builder;
        }

        /// <summary>
        /// Creates a row layout container (horizontal layout).
        /// </summary>
        public static ElementBuilder Row(string stringID, [CallerLineNumber] int intID = 0)
            => Box(stringID, intID).LayoutType(LayoutType.Row);

        /// <summary>
        /// Creates a column layout container (vertical layout).
        /// </summary>
        public static ElementBuilder Column(string stringID, [CallerLineNumber] int intID = 0)
            => Box(stringID, intID).LayoutType(LayoutType.Column);

        /// <summary>
        /// Moves the current parent element to the root of the hierarchy.
        /// Useful for things like popups or modals that need to be rendered at the top level.
        /// You can combine this with Depth to ensure something always renders ontop!
        /// </summary>
        /// <exception cref="Exception"></exception>
        public static void MoveToRoot()
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
        internal static void AddChild(LayoutEngine.Element element)
        {
            if (element.Parent != null)
                throw new Exception("Element already has a parent.");

            element.Parent = CurrentParent;
            CurrentParent.Children.Add(element);
        }

        public static void AddActionElement(Action<Canvas, Rect> renderAction) => AddActionElement(CurrentParent, renderAction);

        /// <summary>
        /// Adds a custom render action to an element.
        /// </summary>
        public static void AddActionElement(Element element, Action<Canvas, Rect> renderAction)
        {
            ArgumentNullException.ThrowIfNull(element);
            ArgumentNullException.ThrowIfNull(renderAction);

            element._renderCommands ??= new();
            element._renderCommands.Add(new ElementRenderCommand {
                Type = ElementRenderCommand.ElementType.RenderAction,
                Element = element,
                RenderAction = renderAction,
            });
        }

        #endregion

        #region ID Stack Management

        /// <summary>
        /// Pushes an ID onto the ID stack to create a new scope.
        /// </summary>
        public static void PushID(ulong id)
        {
            _IDStack.Push(id);
        }

        /// <summary>
        /// Pops the current ID from the stack, returning to the parent scope.
        /// </summary>
        public static void PopID()
        {
            if (_IDStack.Count > 1)
                _IDStack.Pop();
            else
                throw new Exception("Cannot pop the root ID.");
        }

        #endregion

        #region Element Storage

        /// <summary> Get a value from the global GUI storage this persists across all Frames and Elements </summary>
        public static T GetRootStorage<T>(string key) => GetElementStorage<T>(_rootElement, key, default);
        /// <summary> Set a value in the root element </summary>
        public static void SetRootStorage<T>(string key, T value) => SetElementStorage(_rootElement, key, value);

        /// <summary> Get a value from the current element's storage </summary>
        public static T GetElementStorage<T>(string key, T defaultValue = default) => GetElementStorage(CurrentParent, key, defaultValue);

        /// <summary> Get a value from the current element's storage </summary>
        public static T GetElementStorage<T>(Element el, string key, T defaultValue = default)
        {
            if (!_storage.TryGetValue(el.ID, out var storage))
                return defaultValue;

            if (storage.ContainsKey(key))
                return (T)storage[key]!;

            return defaultValue;
        }

        /// <summary> Check if a key exists in the current element's storage </summary>
        public static bool HasElementStorage(Element el, string key) => _storage.TryGetValue(el.ID, out var storage) && storage.ContainsKey(key);

        /// <summary> Set a value in the current element's storage </summary>
        public static void SetElementStorage<T>(string key, T value) => SetElementStorage(CurrentParent, key, value);
        /// <summary> Set a value in the current element's storage </summary>
        public static void SetElementStorage<T>(Element el, string key, T value)
        {
            if (!_storage.TryGetValue(el.ID, out var storage))
                _storage[el.ID] = storage = [];

            storage[key] = value;
        }

        #region Text Field Storage Helpers

        /// <summary>
        /// Gets the current value of a text field.
        /// </summary>
        public static string GetTextFieldValue(Element element)
        {
            return Paper.GetElementStorage<string>(element, "Value", "");
        }

        /// <summary>
        /// Sets the value of a text field.
        /// </summary>
        public static void SetTextFieldValue(Element element, string value)
        {
            Paper.SetElementStorage(element, "Value", value);
            Paper.SetElementStorage(element, "CursorPosition", value.Length);
            Paper.SetElementStorage(element, "SelectionStart", -1);
            Paper.SetElementStorage(element, "SelectionEnd", -1);
        }

        #endregion

        #endregion

        #region Layout Helpers

        /// <summary>
        /// Creates a stretch unit value with the specified factor.
        /// </summary>
        public static UnitValue Stretch(double factor = 1f) => UnitValue.Stretch(factor);

        /// <summary>
        /// Creates a pixel-based unit value.
        /// </summary>
        public static UnitValue Pixels(double value) => UnitValue.Pixels(value);

        /// <summary>
        /// Creates a percentage-based unit value with optional pixel offset.
        /// </summary>
        public static UnitValue Percent(double value, double pixelOffset = 0f) => UnitValue.Percentage(value, pixelOffset);

        /// <summary>
        /// Creates an auto-sized unit value.
        /// </summary>
        public static UnitValue Auto => UnitValue.Auto;

        #endregion
    }
}
