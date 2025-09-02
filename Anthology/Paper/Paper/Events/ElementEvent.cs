// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI.LayoutEngine;
using Prowl.Vector;

namespace Prowl.PaperUI.Events;

public class ElementEvent
{
    // The element that triggered the event
    public ElementHandle Source { get; }

    // The calculated layout rectangle of the element
    public Rect ElementRect { get; }

    // The raw pointer position in screen coordinates
    public Vector2 PointerPosition { get; }

    // The pointer position normalized to the element (0,0 = top-left, 1,1 = bottom-right)
    public Vector2 NormalizedPosition { get; }

    // The pointer position relative to the element's top-left corner
    public Vector2 RelativePosition { get; }

    public ElementEvent(ElementHandle source, Rect elementRect, Vector2 pointerPos)
    {
        Source = source;
        ElementRect = elementRect;
        PointerPosition = pointerPos;

        // Calculate relative position (pointer position relative to element's origin)
        RelativePosition = new Vector2(
            pointerPos.x - elementRect.x,
            pointerPos.y - elementRect.y
        );

        // Calculate normalized position (0-1 range within the element)
        NormalizedPosition = new Vector2(
            elementRect.width > 0 ? RelativePosition.x / elementRect.width : 0,
            elementRect.height > 0 ? RelativePosition.y / elementRect.height : 0
        );
    }
}
