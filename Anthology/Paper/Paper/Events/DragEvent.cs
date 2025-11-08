// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI.LayoutEngine;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.PaperUI.Events
{
    public class DragEvent : ElementEvent
    {
        public Float2 StartPosition { get; }
        public Float2 Delta { get; }
        public Float2 TotalDelta { get; }

        public DragEvent(ElementHandle source, Rect elementRect, Float2 pointerPos, Float2 startPos, Float2 delta, Float2 totalDelta)
            : base(source, elementRect, pointerPos)
        {
            StartPosition = startPos;
            Delta = delta;
            TotalDelta = totalDelta;
        }
    }
}
