// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI.LayoutEngine;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.PaperUI.Events;

public class DragEvent : ElementEvent
{
    public Double2 StartPosition { get; }
    public Double2 Delta { get; }
    public Double2 TotalDelta { get; }

    public DragEvent(ElementHandle source, Rect elementRect, Double2 pointerPos, Double2 startPos, Double2 delta, Double2 totalDelta)
        : base(source, elementRect, pointerPos)
    {
        StartPosition = startPos;
        Delta = delta;
        TotalDelta = totalDelta;
    }
}
