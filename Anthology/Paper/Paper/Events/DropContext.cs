// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI.LayoutEngine;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.PaperUI.Events
{
    /// <summary>
    /// Information passed to an <c>AcceptDrop</c> callback when a drag is released over a valid target.
    /// Provides the geometry the handler typically needs (local pointer, normalized 0..1 within the
    /// element rect) so handlers can compute drop zones (above / into / below for reorder, quadrants
    /// for grid drops, etc.) without re-querying Paper.
    /// </summary>
    public readonly struct DropContext
    {
        /// <summary>The element that accepted the drop.</summary>
        public readonly ElementHandle Target;

        /// <summary>The drop target's full layout rect.</summary>
        public readonly Rect Rect;

        /// <summary>Pointer position in screen / global coordinates.</summary>
        public readonly Float2 PointerPosition;

        /// <summary>Pointer position relative to the target element's top-left corner.</summary>
        public readonly Float2 LocalPosition;

        /// <summary>Pointer position normalized to the element's rect — (0,0) top-left, (1,1) bottom-right.</summary>
        public readonly Float2 NormalizedPosition;

        public DropContext(ElementHandle target, Rect rect, Float2 pointerPos)
        {
            Target = target;
            Rect = rect;
            PointerPosition = pointerPos;
            LocalPosition = new Float2(pointerPos.X - rect.Min.X, pointerPos.Y - rect.Min.Y);
            NormalizedPosition = new Float2(
                rect.Size.X > 0 ? LocalPosition.X / (float)rect.Size.X : 0,
                rect.Size.Y > 0 ? LocalPosition.Y / (float)rect.Size.Y : 0);
        }
    }
}
