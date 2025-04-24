using FontStashSharp;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Vector;

using System.Drawing;

namespace Prowl.PaperUI.Extras
{
    /// <summary>
    /// Contains all visual styling properties for a window
    /// </summary>
    public class WindowStyle
    {
        // Visual properties
        public Color BackgroundStyle { get; set; } = Color.FromArgb(255, 30, 30, 46);
        public Color TitleBarStyle { get; set; } = Color.FromArgb(255, 40, 40, 56);
        public Color TextColor { get; set; } = Color.FromArgb(255, 226, 232, 240);
        public Color BorderColor { get; set; } = Color.FromArgb(100, 255, 255, 255);
        public Color CloseButtonStyle { get; set; } = Color.FromArgb(80, 255, 100, 100);
        public Color ResizeHandleIndicatorColor { get; set; } = Color.FromArgb(180, 94, 104, 202);

        public double BorderWidth { get; set; } = 1.0f;
        public double CornerRadius { get; set; } = 8.0f;
        public double TitleBarHeight { get; set; } = 32.0f;

        // Predefined styles
        public static WindowStyle Default => new WindowStyle();

        public static WindowStyle Dark => new WindowStyle {
            BackgroundStyle = Color.FromArgb(255, 20, 20, 30),
            TitleBarStyle = Color.FromArgb(255, 30, 30, 40),

            TextColor = Color.FromArgb(255, 200, 200, 210),
            BorderColor = Color.FromArgb(100, 100, 100, 120)
        };

        public static WindowStyle Light => new WindowStyle {
            BackgroundStyle = Color.FromArgb(255, 240, 240, 245),
            TitleBarStyle = Color.FromArgb(255, 220, 220, 230),

            TextColor = Color.FromArgb(255, 40, 40, 50),
            BorderColor = Color.FromArgb(100, 180, 180, 190)
        };

        // Clone the style
        public WindowStyle Clone()
        {
            return new WindowStyle {
                BackgroundStyle = this.BackgroundStyle,
                TextColor = this.TextColor,
                BorderColor = this.BorderColor,
                CloseButtonStyle = this.CloseButtonStyle,
                ResizeHandleIndicatorColor = this.ResizeHandleIndicatorColor,
                BorderWidth = this.BorderWidth,
                CornerRadius = this.CornerRadius,
                TitleBarHeight = this.TitleBarHeight
            };
        }
    }

    // Window state storage class
    public class WindowState
    {
        // Window identification and properties
        public string Id { get; }
        public string Title { get; set; }
        public bool IsResizable { get; set; } = true;
        public bool IsDraggable { get; set; } = true;
        public bool HasTitleBar { get; set; } = true;
        public bool HasCloseButton { get; set; } = true;
        public bool IsModal { get; set; } = false;
        public Vector2 Position { get; set; }
        public Vector2 Size { get; set; }
        public Vector2 MinSize { get; set; } = new Vector2(200, 100);
        public int ZOrder { get; set; } = 0;

        internal Vector2 StartDraggingPosition;

        // For tracking internal state
        internal Element ContentNode { get; set; }
        internal Element WindowNode { get; set; }
        internal ResizeDirection CurrentResizeDirection { get; set; } = ResizeDirection.None;

        public WindowState(string id, string title, Vector2 position, Vector2 size)
        {
            Id = id;
            Title = title;
            Position = position;
            Size = size;
        }

        public enum ResizeDirection
        {
            None,
            North,
            South,
            East,
            West,
            NorthEast,
            NorthWest,
            SouthEast,
            SouthWest
        }
    }

    // Window Manager to handle window state
    public static class WindowManager
    {
        private static Dictionary<string, WindowState> _windowStates = new Dictionary<string, WindowState>();
        private static SpriteFontBase _windowFont;

        private static byte _setNextWindowPosition = 0; // 0 = no, 1 = every frame, 2 = first time
        private static Vector2 _position = new Vector2(0, 0);
        private static byte _setNextWindowSize = 0; // 0 = no, 1 = every frame, 2 = first time
        private static Vector2 _size = new Vector2(0, 0);

        public static void SetWindowFont(SpriteFontBase font)
        {
            _windowFont = font;
        }

        public static void SetNextWindowPosition(Vector2 position, bool everyFrame = false)
        {
            _setNextWindowPosition = (byte)(everyFrame ? 1 : 2);
            _position = position;
        }

        public static void SetNextWindowSize(Vector2 size, bool everyFrame = false)
        {
            _setNextWindowSize = (byte)(everyFrame ? 1 : 2);
            _size = size;
        }

        /// <summary>
        /// Immediate mode Window function. Creates or updates a window with the given ID.
        /// Returns true if the window is open and content should be rendered.
        /// </summary>
        public static void Window(string id, string title, Action contentFunc) => Window(id, title, null, contentFunc);

        /// <summary>
        /// Immediate mode Window function with options. Creates or updates a window with the given ID.
        /// Returns true if the window is open and content should be rendered.
        /// </summary>
        public static void Window(string id, string title, WindowStyle? style, Action contentFunc)
        {
            // Get or create window state
            if (!_windowStates.TryGetValue(id, out WindowState state))
            {
                state = new WindowState(id, title, new(100, 100), new(200, 200));
                // Update position and size if set to first time
                if (_setNextWindowPosition == 2) state.Position = _position;
                if (_setNextWindowSize == 2) state.Size = _size;
                _windowStates.Add(id, state);
            }

            // Update position and size if set to every frame
            if (_setNextWindowPosition == 1) state.Position = _position;
            if (_setNextWindowSize == 1)     state.Size = _size;

            _setNextWindowPosition = 0; // Reset to no
            _setNextWindowSize = 0; // Reset to no

            style ??= new WindowStyle();

            // Update state from parameters
            state.Title = title;

            RenderWindow(style, state, contentFunc);

        }

        private static void RenderWindow(WindowStyle style, WindowState state, Action contentFunc)
        {
            if (_windowFont == null)
            {
                throw new InvalidOperationException("Window font not set. Call SetWindowFont() before using windows.");
            }

            // Main window container
            using (Paper.Box($"Window_{state.Id}")
                .Depth(100_000 + state.ZOrder)
                .PositionType(PositionType.SelfDirected)
                .Position(state.Position.x, state.Position.y)

                .Size(state.Size.x, state.Size.y)
                .MaxLeft(Paper.Percent(100, -state.Size.x))
                .MaxTop(Paper.Percent(100, -state.Size.y))
                .MinWidth(state.MinSize.x)
                .MinHeight(state.MinSize.y)
                .BackgroundColor(style.BackgroundStyle)
                .BorderColor(style.BorderColor)
                .BorderWidth(style.BorderWidth)
                .OnClick((rect) => BringWindowToFront(state.Id))
                .OnPostLayout((node, rect) => {
                    // Set the window position to the new position layouting calculated
                    state.Position = new Vector2(node.RelativeX, node.RelativeY);
                    state.Size = new Vector2(node.LayoutWidth, node.LayoutHeight);
                    
                    // Get parent bounds (viewport)
                    var viewport = new Rect(0, 0, node.Parent.LayoutWidth, node.Parent.LayoutHeight);
                    
                    // If window is larger than viewport, scale it down to fit
                    double windowWidth = Math.Min(state.Size.x, viewport.width);
                    double windowHeight = Math.Min(state.Size.y, viewport.height);
                    
                    // Calculate window position to keep it in bounds
                    double x = state.Position.x;
                    double y = state.Position.y;
                    
                    // Adjust horizontal position to keep window inside viewport
                    if (x < viewport.x)
                        x = viewport.x; // Window extends beyond left edge
                    else if (x + windowWidth > viewport.x + viewport.width)
                        x = viewport.x + viewport.width - windowWidth; // Window extends beyond right edge
                    
                    // Adjust vertical position to keep window inside viewport
                    if (y < viewport.y)
                        y = viewport.y; // Window extends beyond top edge
                    else if(y + windowHeight > viewport.y + viewport.height)
                        y = viewport.y + viewport.height - windowHeight; // Window extends beyond bottom edge
                    
                    
                    // Update window position and size
                    state.Position = new Vector2(x, y);
                    state.Size = new Vector2(windowWidth, windowHeight);
                })
                .Enter())
            {
                state.WindowNode = Paper.CurrentParent;

                if (state.HasTitleBar)
                    RenderTitleBar(style, state);

                // Content area
                using (Paper.Box($"WindowContent_{state.Id}")
                    .Margin(8)
                    .Clip()
                    .Enter())
                {
                    // Store the content node for possible scissoring/clipping
                    state.ContentNode = Paper.CurrentParent;

                    // Call the content callback
                    contentFunc?.Invoke();
                }

                // Handle window resizing
                if (state.IsResizable)
                    AddResizeHandles(style, state);
            }
        }

        private static void RenderTitleBar(WindowStyle style, WindowState state)
        {
            using (Paper.Row($"WindowTitleBar_{state.Id}")
                .Height(style.TitleBarHeight)
                .BackgroundColor(style.TitleBarStyle)
                .OnDragStart((pos) => {
                    if (state.IsDraggable)
                    {
                        // Start dragging
                        state.StartDraggingPosition = state.Position;
                        BringWindowToFront(state.Id);
                    }
                })
                .OnDragging((start, rect) => {
                    if (state.IsDraggable)
                    {
                        // Update window position based on drag
                        var totalDelta = Paper.PointerPos - start;
                        state.Position = new Vector2(state.StartDraggingPosition.x + totalDelta.x, state.StartDraggingPosition.y + totalDelta.y);

                        BringWindowToFront(state.Id);
                    }
                })
                .Enter())
            {
                // Title text
                using (Paper.Box($"WindowTitle_{state.Id}")
                    .Margin(8, 0, 0, 0)
                    .Text(Text.Left(state.Title, _windowFont, style.TextColor))
                    .Enter()) { }

                // Spacer to push buttons to the right
                using (Paper.Box($"TitleBarSpacer_{state.Id}")
                    .Enter()) { }

                if (state.HasCloseButton)
                {
                    using (Paper.Box($"CloseButton_{state.Id}")
                        .Width(24)
                        .Height(24)
                        .Margin(0, 4, 8, 4)
                        .BackgroundColor(style.CloseButtonStyle)
                        .Text(Text.Center("×", _windowFont, style.TextColor))
                        //.OnClick(() => state.IsOpen = false)
                        .Enter()) { }
                }
            }
        }

        private static void AddResizeHandles(WindowStyle style, WindowState state)
        {
            const int handleSize = 8;

            // Create an array of resize handle definitions
            (WindowState.ResizeDirection dir, double l, double t, double w, double h)[] handles = [
                (dir: WindowState.ResizeDirection.North, l: handleSize*2, t: 0, w: state.Size.x-handleSize*4, h: handleSize),
                (dir: WindowState.ResizeDirection.South, l: handleSize*2, t: state.Size.y-handleSize, w: state.Size.x-handleSize*4, h: handleSize),
                (dir: WindowState.ResizeDirection.East, l: state.Size.x-handleSize, t: handleSize*2, w: handleSize, h: state.Size.y-handleSize*4),
                (dir: WindowState.ResizeDirection.West, l: 0, t: handleSize*2, w: handleSize, h: state.Size.y-handleSize*4),
                (dir: WindowState.ResizeDirection.NorthEast, l: state.Size.x-handleSize, t: 0, w: handleSize, h: handleSize),
                (dir: WindowState.ResizeDirection.NorthWest, l: 0, t: 0, w: handleSize, h: handleSize),
                (dir: WindowState.ResizeDirection.SouthEast, l: state.Size.x-handleSize, t: state.Size.y-handleSize, w: handleSize, h: handleSize),
                (dir: WindowState.ResizeDirection.SouthWest, l: 0, t: state.Size.y-handleSize, w: handleSize, h: handleSize)
            ];

            // Create all handles from the array
            foreach (var handle in handles)
            {
                using (Paper.Box($"ResizeHandle_{handle.dir}_{state.Id}")
                    .PositionType(PositionType.SelfDirected)
                    .Left(handle.l).Top(handle.t).Width(handle.w).Height(handle.h)
                    .OnDragging((start, rect) => ResizeWindow(state, Paper.PointerDelta, handle.dir))
                    .OnHover((pos) => state.CurrentResizeDirection = handle.dir)
                    .OnLeave((PositionType) => state.CurrentResizeDirection = WindowState.ResizeDirection.None)
                    .Enter()) { }
            }

            // Draw cursor and visual indicators for resize handles
            if (state.CurrentResizeDirection != WindowState.ResizeDirection.None)
            {
                UpdateCursor(state.CurrentResizeDirection);
                DrawResizeHandleIndicators(style, state);
            }

            state.CurrentResizeDirection = WindowState.ResizeDirection.None;
        }

        private static void ResizeWindow(WindowState state, Vector2 delta, WindowState.ResizeDirection direction)
        {
            BringWindowToFront(state.Id);

            switch (direction)
            {
                case WindowState.ResizeDirection.North:
                    state.Position = new Vector2(state.Position.x, state.Position.y + delta.y);
                    state.Size = new Vector2(state.Size.x, state.Size.y - delta.y);
                    break;

                case WindowState.ResizeDirection.South:
                    state.Size = new Vector2(state.Size.x, state.Size.y + delta.y);
                    break;

                case WindowState.ResizeDirection.East:
                    state.Size = new Vector2(state.Size.x + delta.x, state.Size.y);
                    break;

                case WindowState.ResizeDirection.West:
                    state.Position = new Vector2(state.Position.x + delta.x, state.Position.y);
                    state.Size = new Vector2(state.Size.x - delta.x, state.Size.y);
                    break;

                case WindowState.ResizeDirection.NorthEast:
                    state.Position = new Vector2(state.Position.x, state.Position.y + delta.y);
                    state.Size = new Vector2(state.Size.x + delta.x, state.Size.y - delta.y);
                    break;

                case WindowState.ResizeDirection.NorthWest:
                    state.Position = new Vector2(state.Position.x + delta.x, state.Position.y + delta.y);
                    state.Size = new Vector2(state.Size.x - delta.x, state.Size.y - delta.y);
                    break;

                case WindowState.ResizeDirection.SouthEast:
                    state.Size = new Vector2(state.Size.x + delta.x, state.Size.y + delta.y);
                    break;

                case WindowState.ResizeDirection.SouthWest:
                    state.Position = new Vector2(state.Position.x + delta.x, state.Position.y);
                    state.Size = new Vector2(state.Size.x - delta.x, state.Size.y + delta.y);
                    break;
            }
        }

        private static void UpdateCursor(WindowState.ResizeDirection direction)
        {
        }

        private static void DrawResizeHandleIndicators(WindowStyle style, WindowState state)
        {
            // Add visual indicators for active resize handle
            if (state.WindowNode != null)
            {
                Paper.AddActionElement(state.WindowNode, (vg, rect) => {
                    const double lineThickness = 4.0f;
                    const double linePadding = 10.0f;
                    vg.SetStrokeColor(style.ResizeHandleIndicatorColor);
                    vg.SetStrokeWidth(lineThickness);

                    // Define the edge coordinates for each direction
                    switch (state.CurrentResizeDirection)
                    {
                        case WindowState.ResizeDirection.North:
                        case WindowState.ResizeDirection.NorthEast:
                        case WindowState.ResizeDirection.NorthWest:
                            // Top edge
                            vg.BeginPath();
                            vg.MoveTo(rect.x + linePadding, rect.y + lineThickness / 2);
                            vg.LineTo(rect.x + rect.width - linePadding, rect.y + lineThickness / 2);
                            vg.Stroke();
                            break;
                        case WindowState.ResizeDirection.South:
                        case WindowState.ResizeDirection.SouthEast:
                        case WindowState.ResizeDirection.SouthWest:
                            // Bottom edge
                            vg.BeginPath();
                            vg.MoveTo(rect.x + linePadding, rect.y + rect.height - lineThickness / 2);
                            vg.LineTo(rect.x + rect.width - linePadding, rect.y + rect.height - lineThickness / 2);
                            vg.Stroke();
                            break;
                    }

                    switch (state.CurrentResizeDirection)
                    {
                        case WindowState.ResizeDirection.East:
                        case WindowState.ResizeDirection.NorthEast:
                        case WindowState.ResizeDirection.SouthEast:
                            // Right edge
                            vg.BeginPath();
                            vg.MoveTo(rect.x + rect.width - lineThickness / 2, rect.y + linePadding);
                            vg.LineTo(rect.x + rect.width - lineThickness / 2, rect.y + rect.height - linePadding);
                            vg.Stroke();
                            break;
                        case WindowState.ResizeDirection.West:
                        case WindowState.ResizeDirection.NorthWest:
                        case WindowState.ResizeDirection.SouthWest:
                            // Left edge
                            vg.BeginPath();
                            vg.MoveTo(rect.x + lineThickness / 2, rect.y + linePadding);
                            vg.LineTo(rect.x + lineThickness / 2, rect.y + rect.height - linePadding);
                            vg.Stroke();
                            break;
                    }
                });
            }
        }

        /// <summary>
        /// Brings a window to the front based on its ID
        /// </summary>
        public static void BringWindowToFront(string id)
        {
            if (_windowStates.TryGetValue(id, out WindowState state))
            {
                // Find highest Z-order
                int maxZ = _windowStates.Values.Max(w => w.ZOrder);
                state.ZOrder = maxZ + 1;
            }
            else
            {
                throw new ArgumentException($"Window with ID '{id}' not found");
            }
        }

        /// <summary>
        /// Closes all windows
        /// </summary>
        public static void CloseAllWindows()
        {
            _windowStates.Clear();
        }
    }
}
