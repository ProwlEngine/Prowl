// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI.LayoutEngine;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.PaperUI.Events
{
    public class ElementEvent
    {
        // The element that triggered the event
        public ElementHandle Source { get; internal set; }

        // The calculated layout rectangle of the element
        public Rect ElementRect { get; internal set; }

        // The raw pointer position in screen coordinates
        public Float2 PointerPosition { get; }

        // The pointer position normalized to the element (0,0 = top-left, 1,1 = bottom-right)
        public Float2 NormalizedPosition { get; internal set; }

        // The pointer position relative to the element's top-left corner
        public Float2 RelativePosition { get; internal set; }

        /// <summary>
        /// Whether propagation has been stopped for this event.
        /// When true, the event will not bubble to parent elements.
        /// </summary>
        public bool IsPropagationStopped { get; private set; }

        /// <summary>
        /// Stops this event from propagating (bubbling) to parent elements.
        /// Similar to DOM's Event.stopPropagation().
        /// This only affects the current event — other event types on the same
        /// element are not affected.
        /// </summary>
        public void StopPropagation() => IsPropagationStopped = true;

        public ElementEvent(ElementHandle source, Rect elementRect, Float2 pointerPos)
        {
            Source = source;
            ElementRect = elementRect;
            PointerPosition = pointerPos;

            UpdateRelativePositions();
        }

        /// <summary>
        /// Updates the Source, ElementRect, and derived positions for a new target element.
        /// Used internally when bubbling an event to parent elements.
        /// </summary>
        internal void Retarget(ElementHandle newSource, Rect newRect)
        {
            Source = newSource;
            ElementRect = newRect;
            UpdateRelativePositions();
        }

        private void UpdateRelativePositions()
        {
            // Calculate relative position (pointer position relative to element's origin)
            RelativePosition = new Float2(
                PointerPosition.X - ElementRect.Min.X,
                PointerPosition.Y - ElementRect.Min.Y
            );

            // Calculate normalized position (0-1 range within the element)
            NormalizedPosition = new Float2(
                ElementRect.Size.X > 0 ? RelativePosition.X / ElementRect.Size.X : 0,
                ElementRect.Size.Y > 0 ? RelativePosition.Y / ElementRect.Size.Y : 0
            );
        }
    }
}
