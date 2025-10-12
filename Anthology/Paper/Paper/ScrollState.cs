using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.PaperUI
{
    /// <summary>
    /// Represents the scrolling state of an element.
    /// </summary>
    public struct ScrollState
    {
        // Basic state properties
        public Double2 Position;
        public Double2 ContentSize;
        public Double2 ViewportSize;

        // Interaction state
        public bool IsDraggingVertical;
        public bool IsDraggingHorizontal;
        public Double2 DragStartPosition;
        public Double2 ScrollStartPosition;

        // UI interaction state
        public bool IsVerticalScrollbarHovered;
        public bool IsHorizontalScrollbarHovered;

        // Constants for scrollbar rendering
        public const double ScrollbarSize = 12;
        public const double ScrollbarMinSize = 20;
        public const double ScrollbarPadding = 2;

        /// <summary>
        /// Gets the maximum scroll position.
        /// </summary>
        public Double2 MaxScroll => new Double2(
            Math.Max(0, ContentSize.X - ViewportSize.X),
            Math.Max(0, ContentSize.Y - ViewportSize.Y)
        );

        /// <summary>
        /// Determines if horizontal scrolling is needed for the given flags.
        /// </summary>
        public bool NeedsHorizontalScroll(Scroll flags) =>
            ContentSize.X > ViewportSize.X && (flags & Scroll.ScrollX) != 0;

        /// <summary>
        /// Determines if vertical scrolling is needed for the given flags.
        /// </summary>
        public bool NeedsVerticalScroll(Scroll flags) =>
            ContentSize.Y > ViewportSize.Y && (flags & Scroll.ScrollY) != 0;

        /// <summary>
        /// Determines if scrollbars should be visible for the given flags.
        /// </summary>
        public bool AreScrollbarsHidden(Scroll flags) => (flags & Scroll.Hidden) != 0;

        /// <summary>
        /// Clamps the scroll position to valid values.
        /// </summary>
        public void ClampScrollPosition()
        {
            Double2 max = MaxScroll;
            Position = new Double2(
                Math.Clamp(Position.X, 0, max.X),
                Math.Clamp(Position.Y, 0, max.Y)
            );
        }

        /// <summary>
        /// Calculates the vertical scrollbar dimensions based on the element rect.
        /// </summary>
        public (double x, double y, double width, double height, double thumbY, double thumbHeight) CalculateVerticalScrollbar(Rect rect, Scroll flags)
        {
            bool hasHorizontal = NeedsHorizontalScroll(flags);

            // Calculate track dimensions
            double trackHeight = rect.Size.Y;
            if (hasHorizontal)
                trackHeight -= ScrollbarSize;

            double trackX = rect.Min.X + rect.Size.X - ScrollbarSize;
            double trackY = rect.Min.Y;

            // Calculate thumb dimensions
            double thumbHeight = Math.Max(ScrollbarMinSize,
                (ViewportSize.Y / ContentSize.Y) * trackHeight);

            double thumbY = trackY;
            if (MaxScroll.Y > 0)
                thumbY += (Position.Y / MaxScroll.Y) * (trackHeight - thumbHeight);

            return (trackX, trackY, ScrollbarSize, trackHeight, thumbY, thumbHeight);
        }

        /// <summary>
        /// Calculates the horizontal scrollbar dimensions based on the element rect.
        /// </summary>
        public (double x, double y, double width, double height, double thumbX, double thumbWidth) CalculateHorizontalScrollbar(Rect rect, Scroll flags)
        {
            bool hasVertical = NeedsVerticalScroll(flags);

            // Calculate track dimensions
            double trackWidth = rect.Size.X;
            if (hasVertical)
                trackWidth -= ScrollbarSize;

            double trackX = rect.Min.X;
            double trackY = rect.Min.Y + rect.Size.Y - ScrollbarSize;

            // Calculate thumb dimensions
            double thumbWidth = Math.Max(ScrollbarMinSize,
                (ViewportSize.X / ContentSize.X) * trackWidth);

            double thumbX = trackX;
            if (MaxScroll.X > 0)
                thumbX += (Position.X / MaxScroll.X) * (trackWidth - thumbWidth);

            return (trackX, trackY, trackWidth, ScrollbarSize, thumbX, thumbWidth);
        }

        /// <summary>
        /// Checks if a point is over the vertical scrollbar.
        /// </summary>
        public bool IsPointOverVerticalScrollbar(Double2 point, Rect rect, Scroll flags)
        {
            if (!NeedsVerticalScroll(flags))
                return false;

            var (trackX, trackY, trackWidth, trackHeight, _, _) = CalculateVerticalScrollbar(rect, flags);

            return point.X >= trackX &&
                   point.X <= trackX + trackWidth &&
                   point.Y >= trackY &&
                   point.Y <= trackY + trackHeight;
        }

        /// <summary>
        /// Checks if a point is over the horizontal scrollbar.
        /// </summary>
        public bool IsPointOverHorizontalScrollbar(Double2 point, Rect rect, Scroll flags)
        {
            if (!NeedsHorizontalScroll(flags))
                return false;

            var (trackX, trackY, trackWidth, trackHeight, _, _) = CalculateHorizontalScrollbar(rect, flags);

            return point.X >= trackX &&
                   point.X <= trackX + trackWidth &&
                   point.Y >= trackY &&
                   point.Y <= trackY + trackHeight;
        }

        /// <summary>
        /// Handles scrollbar dragging for vertical scrollbar.
        /// </summary>
        public void HandleVerticalScrollbarDrag(Double2 mousePos, Rect rect, Scroll flags)
        {
            if (!IsDraggingVertical)
                return;

            var (_, trackY, _, trackHeight, _, thumbHeight) = CalculateVerticalScrollbar(rect, flags);

            double dragDelta = mousePos.Y - DragStartPosition.Y;
            double scrollableHeight = trackHeight - thumbHeight;

            if (scrollableHeight > 0)
            {
                double scrollRatio = dragDelta / scrollableHeight;
                Position = new Double2(
                    Position.X,
                    ScrollStartPosition.Y + (scrollRatio * MaxScroll.Y)
                );

                ClampScrollPosition();
            }
        }

        /// <summary>
        /// Handles scrollbar dragging for horizontal scrollbar.
        /// </summary>
        public void HandleHorizontalScrollbarDrag(Double2 mousePos, Rect rect, Scroll flags)
        {
            if (!IsDraggingHorizontal)
                return;

            var (trackX, _, trackWidth, _, _, thumbWidth) = CalculateHorizontalScrollbar(rect, flags);

            double dragDelta = mousePos.X - DragStartPosition.X;
            double scrollableWidth = trackWidth - thumbWidth;

            if (scrollableWidth > 0)
            {
                double scrollRatio = dragDelta / scrollableWidth;
                Position = new Double2(
                    ScrollStartPosition.X + (scrollRatio * MaxScroll.X),
                    Position.Y
                );

                ClampScrollPosition();
            }
        }
    }
}
