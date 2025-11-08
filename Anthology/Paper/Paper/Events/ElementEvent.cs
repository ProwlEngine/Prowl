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
        public ElementHandle Source { get; }

        // The calculated layout rectangle of the element
        public Rect ElementRect { get; }

        // The raw pointer position in screen coordinates
        public Float2 PointerPosition { get; }

        // The pointer position normalized to the element (0,0 = top-left, 1,1 = bottom-right)
        public Float2 NormalizedPosition { get; }

        // The pointer position relative to the element's top-left corner
        public Float2 RelativePosition { get; }

        public ElementEvent(ElementHandle source, Rect elementRect, Float2 pointerPos)
        {
            Source = source;
            ElementRect = elementRect;
            PointerPosition = pointerPos;

            // Calculate relative position (pointer position relative to element's origin)
            RelativePosition = new Float2(
                pointerPos.X - elementRect.Min.X,
                pointerPos.Y - elementRect.Min.Y
            );

            // Calculate normalized position (0-1 range within the element)
            NormalizedPosition = new Float2(
                elementRect.Size.X > 0 ? RelativePosition.X / elementRect.Size.X : 0,
                elementRect.Size.Y > 0 ? RelativePosition.Y / elementRect.Size.Y : 0
            );
        }
    }
}
