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
        public Float2 Position;
        public Float2 ContentSize;
        public Float2 ViewportSize;

        // Interaction state
        public bool IsDraggingVertical;
        public bool IsDraggingHorizontal;
        public Float2 DragStartPosition;
        public Float2 ScrollStartPosition;

        // UI interaction state
        public bool IsVerticalScrollbarHovered;
        public bool IsHorizontalScrollbarHovered;

        // Constants for scrollbar rendering
        public const float ScrollbarSize = 12;
        public const float ScrollbarMinSize = 20;
        public const float ScrollbarPadding = 2;

        /// <summary>
        /// Gets the maximum scroll position.
        /// </summary>
        public Float2 MaxScroll => new Float2(
            Maths.Max(0, ContentSize.X - ViewportSize.X),
            Maths.Max(0, ContentSize.Y - ViewportSize.Y)
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
            Float2 max = MaxScroll;
            Position = new Float2(
                Maths.Clamp(Position.X, 0, max.X),
                Maths.Clamp(Position.Y, 0, max.Y)
            );
        }

        /// <summary>
        /// Calculates the vertical scrollbar dimensions based on the element rect.
        /// </summary>
        public (float x, float y, float width, float height, float thumbY, float thumbHeight) CalculateVerticalScrollbar(Rect rect, Scroll flags)
        {
            bool hasHorizontal = NeedsHorizontalScroll(flags);

            // Calculate track dimensions
            float trackHeight = rect.Size.Y;
            if (hasHorizontal)
                trackHeight -= ScrollbarSize;

            float trackX = rect.Min.X + rect.Size.X - ScrollbarSize;
            float trackY = rect.Min.Y;

            // Calculate thumb dimensions
            float thumbHeight = Maths.Max(ScrollbarMinSize,
                (ViewportSize.Y / ContentSize.Y) * trackHeight);

            float thumbY = trackY;
            if (MaxScroll.Y > 0)
                thumbY += (Position.Y / MaxScroll.Y) * (trackHeight - thumbHeight);

            return (trackX, trackY, ScrollbarSize, trackHeight, thumbY, thumbHeight);
        }

        /// <summary>
        /// Calculates the horizontal scrollbar dimensions based on the element rect.
        /// </summary>
        public (float x, float y, float width, float height, float thumbX, float thumbWidth) CalculateHorizontalScrollbar(Rect rect, Scroll flags)
        {
            bool hasVertical = NeedsVerticalScroll(flags);

            // Calculate track dimensions
            float trackWidth = rect.Size.X;
            if (hasVertical)
                trackWidth -= ScrollbarSize;

            float trackX = rect.Min.X;
            float trackY = rect.Min.Y + rect.Size.Y - ScrollbarSize;

            // Calculate thumb dimensions
            float thumbWidth = Maths.Max(ScrollbarMinSize,
                (ViewportSize.X / ContentSize.X) * trackWidth);

            float thumbX = trackX;
            if (MaxScroll.X > 0)
                thumbX += (Position.X / MaxScroll.X) * (trackWidth - thumbWidth);

            return (trackX, trackY, trackWidth, ScrollbarSize, thumbX, thumbWidth);
        }

        /// <summary>
        /// Checks if a point is over the vertical scrollbar.
        /// </summary>
        public bool IsPointOverVerticalScrollbar(Float2 point, Rect rect, Scroll flags)
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
        public bool IsPointOverHorizontalScrollbar(Float2 point, Rect rect, Scroll flags)
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
        public void HandleVerticalScrollbarDrag(Float2 mousePos, Rect rect, Scroll flags)
        {
            if (!IsDraggingVertical)
                return;

            var (_, trackY, _, trackHeight, _, thumbHeight) = CalculateVerticalScrollbar(rect, flags);

            float dragDelta = mousePos.Y - DragStartPosition.Y;
            float scrollableHeight = trackHeight - thumbHeight;

            if (scrollableHeight > 0)
            {
                float scrollRatio = dragDelta / scrollableHeight;
                Position = new Float2(
                    Position.X,
                    ScrollStartPosition.Y + (scrollRatio * MaxScroll.Y)
                );

                ClampScrollPosition();
            }
        }

        /// <summary>
        /// Handles scrollbar dragging for horizontal scrollbar.
        /// </summary>
        public void HandleHorizontalScrollbarDrag(Float2 mousePos, Rect rect, Scroll flags)
        {
            if (!IsDraggingHorizontal)
                return;

            var (trackX, _, trackWidth, _, _, thumbWidth) = CalculateHorizontalScrollbar(rect, flags);

            float dragDelta = mousePos.X - DragStartPosition.X;
            float scrollableWidth = trackWidth - thumbWidth;

            if (scrollableWidth > 0)
            {
                float scrollRatio = dragDelta / scrollableWidth;
                Position = new Float2(
                    ScrollStartPosition.X + (scrollRatio * MaxScroll.X),
                    Position.Y
                );

                ClampScrollPosition();
            }
        }
    }
}
