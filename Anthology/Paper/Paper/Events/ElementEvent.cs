// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI.LayoutEngine;
using Prowl.Vector;
using Prowl.Vector.Geometry;
using Prowl.Vector.Spatial;

namespace Prowl.PaperUI.Events;

/// <summary> Represents event data for pointer-based UI events, providing the source element, its layout rectangle, and pointer positions in multiple coordinate spaces. </summary>
public class ElementEvent
{
    // The element that triggered the event
    /// <summary> The element that triggered the event. </summary>
    public ElementHandle Source { get; internal set; }

    // The calculated layout rectangle of the element
    /// <summary> The calculated layout rectangle of the element. </summary>
    public Rect ElementRect { get; internal set; }

    // The raw pointer position in screen coordinates
    /// <summary> Gets the raw pointer position in screen coordinates. </summary>
    public Float2 PointerPosition { get; }

    /// <summary>
    /// The pointer in the element's own layout space, the space <see cref="ElementRect"/> is in. The
    /// same as <see cref="PointerPosition"/> unless the element or an ancestor is transformed, and then
    /// this is the one to compare against the rectangle.
    /// </summary>
    public Float2 LocalPosition { get; private set; }

    // The pointer position normalized to the element (0,0 = top-left, 1,1 = bottom-right)
    /// <summary> The pointer position normalized to the element (0,0 = top-left, 1,1 = bottom-right). </summary>
    public Float2 NormalizedPosition { get; internal set; }

    // The pointer position relative to the element's top-left corner
    /// <summary> The pointer position relative to the element's top-left corner </summary>
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

    /// <summary> Initializes a new event with the given source element, its layout rectangle, and the raw pointer position on screen. Relative and normalized positions are computed automatically. </summary>
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

    /// <summary> A screen point in the source element's layout space. </summary>
    public Float2 ToLocal(Float2 screenPoint)
    {
        if (!Source.IsValid) return screenPoint;

        ref ElementData data = ref Source.Data;
        return data._isIdentityWorldTransform ? screenPoint : data._worldInverse.TransformPoint(screenPoint);
    }

    /// <summary>
    /// A screen distance in the source element's layout space: a drag of 20 pixels across an element
    /// scaled by 2 covers 10 of its own units.
    /// </summary>
    public Float2 ToLocalVector(Float2 screenVector)
    {
        if (!Source.IsValid) return screenVector;

        ref ElementData data = ref Source.Data;
        if (data._isIdentityWorldTransform) return screenVector;

        Transform2D inverse = data._worldInverse;
        return inverse.TransformPoint(screenVector) - inverse.TransformPoint(Float2.Zero);
    }

    private void UpdateRelativePositions()
    {
        // The pointer arrives in screen space while the rectangle is in layout space, and the two only
        // agree when nothing above the element is transformed.
        LocalPosition = ToLocal(PointerPosition);

        RelativePosition = new Float2(
            LocalPosition.X - ElementRect.Min.X,
            LocalPosition.Y - ElementRect.Min.Y
        );

        // Calculate normalized position (0-1 range within the element)
        NormalizedPosition = new Float2(
            ElementRect.Size.X > 0 ? RelativePosition.X / ElementRect.Size.X : 0,
            ElementRect.Size.Y > 0 ? RelativePosition.Y / ElementRect.Size.Y : 0
        );
    }
}
