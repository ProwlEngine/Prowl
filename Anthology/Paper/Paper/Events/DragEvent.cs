// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI.LayoutEngine;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.PaperUI.Events;

/// <summary> Defines the phases of a drag operation: Start, Dragging, and End. </summary>
public enum DragPhase
{
    Start,
    Dragging,
    End
}

/// <summary> Provides data for drag pointer events, including the start position, the per-frame delta, the total accumulated delta, and the current drag phase (Start, Dragging, or End). </summary>
public class DragEvent : ElementEvent
{
    /// <summary> Gets the pointer position at the start of the drag, in the element's coordinate space. </summary>
    public Float2 StartPosition { get; }
    /// <summary> Gets the change in pointer position since the last drag event, in screen coordinates. </summary>
    public Float2 Delta { get; }
    /// <summary> Gets the accumulated drag distance since the drag started, as opposed to Delta which is the change since the last event. </summary>
    public Float2 TotalDelta { get; }

    /// <summary> Gets the phase of the drag operation (Start, Dragging, or End) that this event represents. </summary>
    public DragPhase Phase { get; }

    /// <summary> Initialises a new DragEvent with the given source element, geometry, pointer delta values, and drag phase. </summary>
    public DragEvent(ElementHandle source, Rect elementRect, Float2 pointerPos, Float2 startPos, Float2 delta, Float2 totalDelta, DragPhase phase = DragPhase.Start)
        : base(source, elementRect, pointerPos)
    {
        StartPosition = startPos;
        Delta = delta;
        TotalDelta = totalDelta;
        Phase = phase;
    }
}
