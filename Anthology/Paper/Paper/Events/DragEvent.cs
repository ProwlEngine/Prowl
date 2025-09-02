// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI.LayoutEngine;
using Prowl.Vector;

namespace Prowl.PaperUI.Events;

public class DragEvent : ElementEvent
{
    public Vector2 StartPosition { get; }
    public Vector2 Delta { get; }
    public Vector2 TotalDelta { get; }

    public DragEvent(ElementHandle source, Rect elementRect, Vector2 pointerPos, Vector2 startPos, Vector2 delta, Vector2 totalDelta)
        : base(source, elementRect, pointerPos)
    {
        StartPosition = startPos;
        Delta = delta;
        TotalDelta = totalDelta;
    }
}
