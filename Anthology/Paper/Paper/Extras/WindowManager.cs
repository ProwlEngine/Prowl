//using FontStashSharp;
//
//using Prowl.PaperUI.LayoutEngine;
//using Prowl.Scribe.Internal;
//using Prowl.Vector;
//
//using System.Drawing;
//
//namespace Prowl.PaperUI.Extras
//{
//    /// <summary>
//    /// Helper for specifying padding or margins.
//    /// </summary>
//    public struct Thickness
//    {
//        public UnitValue Left, Top, Right, Bottom;
//
//        public Thickness(UnitValue all) : this(all, all, all, all) { }
//        public Thickness(UnitValue horizontal, UnitValue vertical) : this(horizontal, vertical, horizontal, vertical) { }
//        public Thickness(UnitValue left, UnitValue top, UnitValue right, UnitValue bottom)
//        {
//            Left = left; Top = top; Right = right; Bottom = bottom;
//        }
//    }
//
//    /// <summary>
//    /// Horizontal alignment options.
//    /// </summary>
//    public enum HorizontalAlignment
//    {
//        Left,
//        Center,
//        Right
//    }
//
//    /// <summary>
//    /// Contains all visual styling properties for a window
//    /// </summary>
//    public class WindowStyle
//    {
//        // Visual properties
//
//        public StyleTemplate? BackgroundStyle = null;
//
//        public StyleTemplate? TitlebarStyle = null;
//        public StyleTemplate? TitlebarHoveredStyle = null;
//
//        public StyleTemplate? CloseButtonStyle = null;
//        public StyleTemplate? CloseButtonHoveredStyle = null;
//
//        public StyleTemplate? ContentStyle = null;
//
//        public UnitValue TitleMargin = 8f; // Margin around the title text
//
//        public bool HasTitleBar { get; set; } = true;
//        public Color TextColor { get; set; } = Color.FromArgb(255, 226, 232, 240);
//        public Color ResizeHandleIndicatorColor { get; set; } = Color.FromArgb(180, 94, 104, 202);
//
//        public string CloseButtonIcon { get; set; } = "×";
//        public bool ShowCloseButton { get; set; } = true; // Effective only if ShowTitleBar is true
//        public FontInfo? TitleFont { get; set; } // If null, WindowManager's default font is used
//        public HorizontalAlignment TitleAlignment { get; set; } = HorizontalAlignment.Left;
//
//        internal StyleTemplate GetBackgroundTemplate()
//        {
//            var baseStyle = new StyleTemplate();
//            baseStyle.BackgroundColor(Color.FromArgb(255, 30, 30, 46));
//            baseStyle.BorderColor(Color.FromArgb(100, 255, 255, 255));
//            baseStyle.BorderWidth(1.0f);
//            baseStyle.Rounded(8.0f);
//
//            if (BackgroundStyle != null)
//                BackgroundStyle.ApplyTo(baseStyle);
//
//            return baseStyle;
//        }
//
//        internal StyleTemplate GetTitlebarTemplate()
//        {
//            var baseStyle = new StyleTemplate();
//            baseStyle.BackgroundColor(Color.FromArgb(255, 40, 40, 56));
//            baseStyle.RoundedTop(8.0f);
//            baseStyle.Margin(1f); // So background border doesn't overlap with title bar
//            baseStyle.Height(32f);
//
//            if (TitlebarStyle != null)
//                TitlebarStyle.ApplyTo(baseStyle);
//
//            return baseStyle;
//        }
//
//        internal StyleTemplate GetTitlebarHoveredTemplate() => TitlebarHoveredStyle ?? new();
//
//        internal StyleTemplate GetCloseButtonTemplate(Paper paper)
//        {
//            var baseStyle = new StyleTemplate();
//            baseStyle.BackgroundColor(Color.FromArgb(80, 255, 100, 100));
//            baseStyle.Rounded(4.0f);
//            baseStyle.Size(24f);
//            baseStyle.Margin(paper.Stretch(), 8, paper.Stretch(), paper.Stretch());
//            if (CloseButtonStyle != null)
//                CloseButtonStyle.ApplyTo(baseStyle);
//            return baseStyle;
//        }
//
//        internal StyleTemplate GetCloseButtonHoveredTemplate()
//        {
//            var baseStyle = new StyleTemplate();
//            baseStyle.BackgroundColor(Color.FromArgb(120, 255, 120, 120));
//            if (CloseButtonHoveredStyle != null)
//                CloseButtonHoveredStyle.ApplyTo(baseStyle);
//            return baseStyle;
//        }
//
//        internal StyleTemplate GetContentTemplate()
//        {
//            var baseStyle = new StyleTemplate();
//            baseStyle.Margin(8f);
//            if (ContentStyle != null)
//                ContentStyle.ApplyTo(baseStyle);
//            return baseStyle;
//        }
//    }
//
//    // Window state storage class
//    public class WindowState
//    {
//        // Window identification and properties
//        public string Id { get; }
//        public string Title { get; set; }
//        public bool PreviousOpen { get; set; } = true;
//        public bool IsResizable { get; set; } = true;
//        public bool IsDraggable { get; set; } = true;
//        public bool IsModal { get; set; } = false; // For future use in input handling
//        public Vector2 Position { get; set; }
//        public Vector2 Size { get; set; }
//        public Vector2 MinSize { get; set; } = new Vector2(150, 80); // Adjusted min size
//        public int ZOrder { get; set; } = 0;
//
//        internal Vector2 StartDraggingPosition;
//
//        // For tracking internal state
//        internal Element ContentNode { get; set; }
//        internal Element WindowNode { get; set; }
//        internal ResizeDirection CurrentResizeDirection { get; set; } = ResizeDirection.None;
//        internal bool IsCloseButtonHovered { get; set; } = false; // For hover effect
//
//        public WindowState(string id, string title, Vector2 position, Vector2 size)
//        {
//            Id = id;
//            Title = title;
//            Position = position;
//            Size = size;
//        }
//
//        public enum ResizeDirection
//        {
//            None, North, South, East, West, NorthEast, NorthWest, SouthEast, SouthWest
//        }
//    }
//
//    // Window Manager to handle window state
//    public class WindowManager
//    {
//        private Dictionary<string, WindowState> _windowStates = new Dictionary<string, WindowState>();
//        private Paper _paper;
//
//        // Collection to track windows requested to close in the last event cycle
//        private HashSet<string> _windowsPendingCloseRequest = new HashSet<string>();
//
//        // "SetNextWindow" properties for IMGUI style configuration
//        private byte _setNextWindowPosition = 0; // 0 = no, 1 = every frame, 2 = first time
//        private Vector2 _position = new Vector2(0, 0);
//        private byte _setNextWindowSize = 0; // 0 = no, 1 = every frame, 2 = first time
//        private Vector2 _size = new Vector2(0, 0);
//        private bool? _nextWindowIsResizable = null;
//        private bool? _nextWindowIsDraggable = null;
//        private bool? _nextWindowIsModal = null;
//        private Vector2? _nextWindowMinSize = null;
//
//        public WindowManager(Paper paper)
//        {
//            _paper = paper ?? throw new ArgumentNullException(nameof(paper));
//        }
//
//        public void SetNextWindowPosition(Vector2 position, bool everyFrame = false)
//        {
//            _setNextWindowPosition = (byte)(everyFrame ? 1 : 2);
//            _position = position;
//        }
//
//        public void SetNextWindowSize(Vector2 size, bool everyFrame = false)
//        {
//            _setNextWindowSize = (byte)(everyFrame ? 1 : 2);
//            _size = size;
//        }
//
//        public void SetNextWindowIsResizable(bool isResizable) => _nextWindowIsResizable = isResizable;
//        public void SetNextWindowIsDraggable(bool isDraggable) => _nextWindowIsDraggable = isDraggable;
//        public void SetNextWindowIsModal(bool isModal) => _nextWindowIsModal = isModal;
//        public void SetNextWindowMinSize(Vector2 minSize) => _nextWindowMinSize = minSize;
//
//        /// <summary>
//        /// Immediate mode Window function. Creates or updates a window.
//        /// The window's visibility is controlled by the 'isOpen' boolean.
//        /// The contentFunc is invoked if the window is open.
//        /// </summary>
//        public void Window(string id, ref bool isOpen, string title, Action contentFunc) => Window(id, ref isOpen, title, null, contentFunc);
//
//        /// <summary>
//        /// Immediate mode Window function with style. Creates or updates a window.
//        /// The window's visibility is controlled by the 'isOpen' boolean.
//        /// The contentFunc is invoked if the window is open.
//        /// </summary>
//        public void Window(string id, ref bool isOpen, string title, WindowStyle? style, Action contentFunc)
//        {
//            // Step 1: Process pending close requests from the previous frame's event cycle
//            // (Assuming UI events are processed on a single thread, otherwise lock _windowsPendingCloseRequest)
//            if (_windowsPendingCloseRequest.Contains(id))
//            {
//                isOpen = false; // Update the user's boolean
//                _windowsPendingCloseRequest.Remove(id); // Consume the request
//            }
//
//            // Step 2: Check the user's boolean. If false, remove state and don't render.
//            if (!isOpen)
//            {
//                if (_windowStates.ContainsKey(id))
//                {
//                    _windowStates.Remove(id);
//                }
//                ResetSetNextFlags(); // "SetNext" calls were not used for this closed window
//                return;
//            }
//
//            style ??= new WindowStyle();
//
//            WindowState state;
//            bool isNewWindow = false;
//            if (!_windowStates.TryGetValue(id, out state))
//            {
//                isNewWindow = true;
//                Vector2 initialPosition = _setNextWindowPosition == 2 ? _position : new Vector2(100, 100);
//                Vector2 initialSize = _setNextWindowSize == 2 ? _size : new Vector2(300, 200);
//                state = new WindowState(id, title, initialPosition, initialSize);
//
//                if (_nextWindowIsResizable.HasValue) state.IsResizable = _nextWindowIsResizable.Value;
//                if (_nextWindowIsDraggable.HasValue) state.IsDraggable = _nextWindowIsDraggable.Value;
//                if (_nextWindowIsModal.HasValue) state.IsModal = _nextWindowIsModal.Value;
//                if (_nextWindowMinSize.HasValue) state.MinSize = _nextWindowMinSize.Value;
//
//                _windowStates.Add(id, state);
//            }
//
//            state.Title = title;
//
//            if (_setNextWindowPosition == 1 || (isNewWindow && _setNextWindowPosition == 2)) state.Position = _position;
//            if (_setNextWindowSize == 1 || (isNewWindow && _setNextWindowSize == 2)) state.Size = _size;
//
//            RenderWindow(style, state, contentFunc);
//
//            ResetSetNextFlags();
//        }
//
//        private void ResetSetNextFlags()
//        {
//            _setNextWindowPosition = 0;
//            _setNextWindowSize = 0;
//            _nextWindowIsResizable = null;
//            _nextWindowIsDraggable = null;
//            _nextWindowIsModal = null;
//            _nextWindowMinSize = null;
//        }
//
//        private void RenderWindow(WindowStyle style, WindowState state, Action contentFunc)
//        {
//            // Main window container
//            var bgStyle = style.GetBackgroundTemplate();
//            using (_paper.Box($"Window_{state.Id}")
//                .PositionType(PositionType.SelfDirected)
//                .Position(state.Position.x, state.Position.y)
//
//                .Size(state.Size.x, state.Size.y)
//                .MaxLeft(_paper.Percent(100, -state.Size.x))
//                .MaxTop(_paper.Percent(100, -state.Size.y))
//                .MinWidth(state.MinSize.x)
//                .MinHeight(state.MinSize.y)
//                .Style(bgStyle)
//                .OnClick((rect) => BringWindowToFront(state.Id))
//                .OnPostLayout((node, rect) => {
//                    // Set the window position to the new position layouting calculated
//                    state.Position = new Vector2(node.RelativeX, node.RelativeY);
//                    state.Size = new Vector2(node.LayoutWidth, node.LayoutHeight);
//
//                    // Get parent bounds (viewport)
//                    var viewport = new Rect(0, 0, node.Parent.LayoutWidth, node.Parent.LayoutHeight);
//
//                    // If window is larger than viewport, scale it down to fit
//                    double windowWidth = Math.Min(state.Size.x, viewport.width);
//                    double windowHeight = Math.Min(state.Size.y, viewport.height);
//
//                    // Calculate window position to keep it in bounds
//                    double x = state.Position.x;
//                    double y = state.Position.y;
//
//                    // Adjust horizontal position to keep window inside viewport
//                    if (x < viewport.x)
//                        x = viewport.x; // Window extends beyond left edge
//                    else if (x + windowWidth > viewport.x + viewport.width)
//                        x = viewport.x + viewport.width - windowWidth; // Window extends beyond right edge
//
//                    // Adjust vertical position to keep window inside viewport
//                    if (y < viewport.y)
//                        y = viewport.y; // Window extends beyond top edge
//                    else if (y + windowHeight > viewport.y + viewport.height)
//                        y = viewport.y + viewport.height - windowHeight; // Window extends beyond bottom edge
//
//
//                    // Update window position and size
//                    state.Position = new Vector2(x, y);
//                    state.Size = new Vector2(windowWidth, windowHeight);
//                })
//                .Enter())
//            {
//                state.WindowNode = _paper.CurrentParent;
//
//                if (style.HasTitleBar)
//                    RenderTitleBar(style, state);
//
//                // Content area
//                var contentStyle = style.GetContentTemplate();
//                using (_paper.Box($"WindowContent_{state.Id}")
//                    .Style(contentStyle)
//                    .Clip() // Ensures content doesn't draw outside this box
//                    .Enter())
//                {
//                    state.ContentNode = _paper.CurrentParent;
//                    contentFunc?.Invoke();
//                }
//
//                if (state.IsResizable)
//                    AddResizeHandles(style, state);
//            }
//
//
//            state.CurrentResizeDirection = WindowState.ResizeDirection.None; // Reset Hovered State
//        }
//
//        private void RenderTitleBar(WindowStyle style, WindowState state)
//        {
//            var titleBarStyle = style.GetTitlebarTemplate();
//            var titleBarHoverStyle = style.GetTitlebarHoveredTemplate();
//            using (_paper.Row($"WindowTitleBar_{state.Id}")
//                .Style(titleBarStyle)
//                .Hovered
//                    .Style(titleBarHoverStyle)
//                    .End()
//                .OnDragStart((pos) => {
//                    if (state.IsDraggable)
//                    {
//                        // Start dragging
//                        state.StartDraggingPosition = state.Position;
//                        BringWindowToFront(state.Id);
//                    }
//                })
//                .OnDragging((e) => {
//                    if (state.IsDraggable)
//                    {
//                        // Update window position based on drag
//                        var totalDelta = _paper.PointerPos - e.StartPosition;
//                        state.Position = new Vector2(state.StartDraggingPosition.x + totalDelta.x, state.StartDraggingPosition.y + totalDelta.y);
//                    }
//                })
//                .Enter())
//            {
//                FontInfo titleFont = style.TitleFont ?? _windowFont;
//
//                // Handle Title Alignment
//                if (style.TitleAlignment == HorizontalAlignment.Right || style.TitleAlignment == HorizontalAlignment.Center)
//                {
//                    using (_paper.Box($"TitleBarLeadingSpacer_{state.Id}").Enter()) { } // Flexible spacer
//                }
//
//                // Title Text
//                var paperTextAligner = Text.Left;
//                if (style.TitleAlignment == HorizontalAlignment.Center) paperTextAligner = Text.Center;
//                else if (style.TitleAlignment == HorizontalAlignment.Right) paperTextAligner = Text.Right;
//
//                using (_paper.Box($"WindowTitle_{state.Id}")
//                    // Adjust margin for alignment: only left margin for left-align, only right for right-align
//                    .Margin(style.TitleAlignment == HorizontalAlignment.Left ? style.TitleMargin : 0,
//                            style.TitleAlignment == HorizontalAlignment.Right ? style.TitleMargin : 0,
//                            0, 0)
//                    .Text(paperTextAligner(state.Title, titleFont, style.TextColor))
//                    .Enter()) { }
//
//                if (style.TitleAlignment == HorizontalAlignment.Left || style.TitleAlignment == HorizontalAlignment.Center)
//                {
//                    using (_paper.Box($"TitleBarTrailingSpacer_{state.Id}").Enter()) { } // Flexible spacer
//                }
//
//                // Close Button
//                if (style.ShowCloseButton)
//                {
//                    var closeButtonStyle = style.GetCloseButtonTemplate(_paper);
//                    var closeButtonHoverStyle = style.GetCloseButtonHoveredTemplate();
//                    _paper.Box($"CloseButton_{state.Id}")
//                        .PositionType(PositionType.SelfDirected)
//                        .Style(closeButtonStyle)
//                        .Text(Text.Center(style.CloseButtonIcon, titleFont, style.TextColor)) // Use titleFont for close button for consistency
//                        .OnClick((_) =>
//                        {
//                            // Request close on click
//                            _windowsPendingCloseRequest.Add(state.Id);
//                        })
//                        .Hovered
//                            .Style(closeButtonHoverStyle)
//                            .End();
//                }
//            }
//        }
//
//        private void AddResizeHandles(WindowStyle style, WindowState state)
//        {
//            const int handleSize = 8;
//
//            // Create an array of resize handle definitions
//            (WindowState.ResizeDirection dir, double l, double t, double w, double h)[] handles = [
//                (dir: WindowState.ResizeDirection.North, l: handleSize*2, t: 0, w: state.Size.x-handleSize*4, h: handleSize),
//                (dir: WindowState.ResizeDirection.South, l: handleSize*2, t: state.Size.y-handleSize, w: state.Size.x-handleSize*4, h: handleSize),
//                (dir: WindowState.ResizeDirection.East, l: state.Size.x-handleSize, t: handleSize*2, w: handleSize, h: state.Size.y-handleSize*4),
//                (dir: WindowState.ResizeDirection.West, l: 0, t: handleSize*2, w: handleSize, h: state.Size.y-handleSize*4),
//                (dir: WindowState.ResizeDirection.NorthEast, l: state.Size.x-handleSize, t: 0, w: handleSize, h: handleSize),
//                (dir: WindowState.ResizeDirection.NorthWest, l: 0, t: 0, w: handleSize, h: handleSize),
//                (dir: WindowState.ResizeDirection.SouthEast, l: state.Size.x-handleSize, t: state.Size.y-handleSize, w: handleSize, h: handleSize),
//                (dir: WindowState.ResizeDirection.SouthWest, l: 0, t: state.Size.y-handleSize, w: handleSize, h: handleSize)
//            ];
//
//            foreach (var handle in handles)
//            {
//                using (_paper.Box($"ResizeHandle_{handle.dir}_{state.Id}")
//                    .PositionType(PositionType.SelfDirected)
//                    .Left(handle.l).Top(handle.t).Width(handle.w).Height(handle.h)
//                    .OnDragging((e) => ResizeWindow(state, _paper.PointerDelta, handle.dir))
//                    .OnHover((pos) => state.CurrentResizeDirection = handle.dir) // Set which handle is currently hovered
//                    .Enter()) { }
//            }
//
//            // Draw cursor and visual indicators for resize handles
//            if (state.CurrentResizeDirection != WindowState.ResizeDirection.None)
//            {
//                UpdateCursor(state.CurrentResizeDirection);
//                DrawResizeHandleIndicators(style, state);
//            }
//        }
//
//        private void ResizeWindow(WindowState state, Vector2 delta, WindowState.ResizeDirection direction)
//        {
//            BringWindowToFront(state.Id); // Bring to front when resize interaction starts
//
//            Vector2 newPos = state.Position;
//            Vector2 newSize = state.Size;
//
//            switch (direction)
//            {
//                case WindowState.ResizeDirection.North:
//                    newPos.y += delta.y; newSize.y -= delta.y; break;
//                case WindowState.ResizeDirection.South:
//                    newSize.y += delta.y; break;
//                case WindowState.ResizeDirection.East:
//                    newSize.x += delta.x; break;
//                case WindowState.ResizeDirection.West:
//                    newPos.x += delta.x; newSize.x -= delta.x; break;
//                case WindowState.ResizeDirection.NorthEast:
//                    newPos.y += delta.y; newSize.x += delta.x; newSize.y -= delta.y; break;
//                case WindowState.ResizeDirection.NorthWest:
//                    newPos.x += delta.x; newPos.y += delta.y; newSize.x -= delta.x; newSize.y -= delta.y; break;
//                case WindowState.ResizeDirection.SouthEast:
//                    newSize.x += delta.x; newSize.y += delta.y; break;
//                case WindowState.ResizeDirection.SouthWest:
//                    newPos.x += delta.x; newSize.x -= delta.x; newSize.y += delta.y; break;
//            }
//
//            // Clamp to MinSize
//            if (newSize.x < state.MinSize.x)
//            {
//                if (direction == WindowState.ResizeDirection.West || direction == WindowState.ResizeDirection.NorthWest || direction == WindowState.ResizeDirection.SouthWest)
//                    newPos.x -= (state.MinSize.x - newSize.x);
//                newSize.x = state.MinSize.x;
//            }
//            if (newSize.y < state.MinSize.y)
//            {
//                if (direction == WindowState.ResizeDirection.North || direction == WindowState.ResizeDirection.NorthWest || direction == WindowState.ResizeDirection.NorthEast)
//                    newPos.y -= (state.MinSize.y - newSize.y);
//                newSize.y = state.MinSize.y;
//            }
//
//            state.Position = newPos;
//            state.Size = newSize;
//        }
//
//        private void UpdateCursor(WindowState.ResizeDirection direction)
//        {
//        }
//
//        private void DrawResizeHandleIndicators(WindowStyle style, WindowState state)
//        {
//            // Add visual indicators for active resize handle
//            if (state.WindowNode != null)
//            {
//                _paper.AddActionElement(state.WindowNode, (vg, rect) => {
//                    const double lineThickness = 4.0f;
//                    const double linePadding = 10.0f;
//                    vg.SetStrokeColor(style.ResizeHandleIndicatorColor);
//                    vg.SetStrokeWidth(lineThickness);
//
//                    // Define the edge coordinates for each direction
//                    switch (state.CurrentResizeDirection)
//                    {
//                        case WindowState.ResizeDirection.North:
//                        case WindowState.ResizeDirection.NorthEast:
//                        case WindowState.ResizeDirection.NorthWest:
//                            // Top edge
//                            vg.BeginPath();
//                            vg.MoveTo(rect.x + linePadding, rect.y + lineThickness / 2);
//                            vg.LineTo(rect.x + rect.width - linePadding, rect.y + lineThickness / 2);
//                            vg.Stroke();
//                            break;
//                        case WindowState.ResizeDirection.South:
//                        case WindowState.ResizeDirection.SouthEast:
//                        case WindowState.ResizeDirection.SouthWest:
//                            // Bottom edge
//                            vg.BeginPath();
//                            vg.MoveTo(rect.x + linePadding, rect.y + rect.height - lineThickness / 2);
//                            vg.LineTo(rect.x + rect.width - linePadding, rect.y + rect.height - lineThickness / 2);
//                            vg.Stroke();
//                            break;
//                    }
//
//                    switch (state.CurrentResizeDirection)
//                    {
//                        case WindowState.ResizeDirection.East:
//                        case WindowState.ResizeDirection.NorthEast:
//                        case WindowState.ResizeDirection.SouthEast:
//                            // Right edge
//                            vg.BeginPath();
//                            vg.MoveTo(rect.x + rect.width - lineThickness / 2, rect.y + linePadding);
//                            vg.LineTo(rect.x + rect.width - lineThickness / 2, rect.y + rect.height - linePadding);
//                            vg.Stroke();
//                            break;
//                        case WindowState.ResizeDirection.West:
//                        case WindowState.ResizeDirection.NorthWest:
//                        case WindowState.ResizeDirection.SouthWest:
//                            // Left edge
//                            vg.BeginPath();
//                            vg.MoveTo(rect.x + lineThickness / 2, rect.y + linePadding);
//                            vg.LineTo(rect.x + lineThickness / 2, rect.y + rect.height - linePadding);
//                            vg.Stroke();
//                            break;
//                    }
//                });
//            }
//        }
//
//        /// <summary>
//        /// Brings a window to the front based on its ID
//        /// </summary>
//        public void BringWindowToFront(string id)
//        {
//            if (_windowStates.TryGetValue(id, out WindowState state))
//            {
//                // Find highest Z-order
//                int maxZ = _windowStates.Values.Max(w => w.ZOrder);
//                state.ZOrder = maxZ + 1;
//            }
//            else
//            {
//                throw new ArgumentException($"Window with ID '{id}' not found");
//            }
//        }
//
//        /// <summary>
//        /// Closes all windows
//        /// </summary>
//        public void CloseAllWindows()
//        {
//            _windowStates.Clear();
//        }
//    }
//}
//
