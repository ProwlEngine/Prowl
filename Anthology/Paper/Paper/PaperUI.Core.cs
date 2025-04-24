using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;

using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

namespace Prowl.PaperUI
{
    public static partial class Paper
    {
        #region Fields & Properties

        // Layout and hierarchy management
        private static LayoutEngine.Element _rootElement;
        internal static Stack<LayoutEngine.Element> _elementStack = new Stack<LayoutEngine.Element>();
        private static readonly Stack<ulong> _IDStack = new();
        private static readonly Dictionary<ulong, Element> _createdElements = [];

        // Rendering context
        private static Canvas _canvas;
        private static ICanvasRenderer _renderer;
        private static double _width;
        private static double _height;
        private static Stopwatch _timer = new();

        // Events
        public static Action? OnEndOfFramePreLayout = null;
        public static Action? OnEndOfFramePostLayout = null;

        // Performance metrics
        public static double MillisecondsSpent { get; private set; }
        public static uint CountOfAllElements { get; private set; }

        // Public properties
        public static Rect ScreenRect => new Rect(0, 0, _width, _height);
        public static Element RootElement => _rootElement;

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
        {
            _width = width;
            _height = height;
            _renderer = renderer;

            // Create root element
            _rootElement = new LayoutEngine.Element {
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

            InitializeInput();
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
            OnEndOfFramePreLayout?.Invoke();
            _rootElement.Layout();
            OnEndOfFramePostLayout?.Invoke();

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
            element?.OnPostLayout?.Invoke(element, new Rect(element.X, element.Y, element.LayoutWidth, element.LayoutHeight));
            foreach (var child in element.Children)
            {
                CallPostLayoutRecursive(child);
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
            //_canvas.BeginPath();
            //_canvas.RoundedRect(rect.x, rect.y, rect.width, rect.height, rounded.x, rounded.y, rounded.z, rounded.w);
            //_canvas.SetFillColor(backgroundColor);
            //_canvas.Fill();
            if(backgroundColor.A > 0)
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
                        cmd.RenderAction?.Invoke(_canvas, rect);
                    _canvas.RestoreState();
                }
            }

            // Draw children sorted by Z-order
            var sortedChildren = element.GetSortedChildren;
            foreach (var child in sortedChildren)
                RenderElement(child);

            _canvas.RestoreState();
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
            ulong storageHash = (ulong)HashCode.Combine(_IDStack.Peek(), stringID ?? string.Empty, intID);

            if (_createdElements.ContainsKey(storageHash))
                throw new Exception("Element already exists with this ID: " + stringID + ":" + intID + " = " + storageHash + " Parent: " + _IDStack.Peek() + "\nPlease use a different ID.");

            var builder = new ElementBuilder(storageHash);
            _createdElements.Add(storageHash, builder.Element);

            AddChild(builder.Element);

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
