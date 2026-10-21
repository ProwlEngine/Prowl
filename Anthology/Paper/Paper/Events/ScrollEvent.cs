// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI.LayoutEngine;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.PaperUI.Events;

/// <summary> Represents a scroll wheel event on an element. Delta is the scroll amount and direction (positive = down/right, negative = up/left, in arbitrary units). </summary>
public class ScrollEvent : ElementEvent
{
    /// <summary> Gets the scroll delta, in logical units. Positive values indicate scrolling down or right; negative values indicate up or left, depending on the scroll direction. </summary>
    public float Delta { get; }

    /// <summary> Horizontal scroll from a tilting wheel or a trackpad. Positive scrolls toward the start (left), like a positive <see cref="Delta"/> scrolls up. Zero for a plain vertical wheel. </summary>
    public float DeltaX { get; }

    /// <summary> Initializes a new ScrollEvent with the specified source element, element rectangle, pointer position, and scroll delta (positive for upward/forward scrolling, negative for downward/backward). </summary>
    public ScrollEvent(ElementHandle source, Rect elementRect, Float2 pointerPos, float delta, float deltaX = 0f)
        : base(source, elementRect, pointerPos)
    {
        Delta = delta;
        DeltaX = deltaX;
    }
}
