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
    private readonly Float2 _startScreen, _deltaScreen, _totalScreen;

    /// <summary> Gets the pointer position at the start of the drag, in the element's layout space. </summary>
    public Float2 StartPosition => ToLocal(_startScreen);
    /// <summary> Gets the change in pointer position since the last drag event, in the element's layout space. </summary>
    public Float2 Delta => ToLocalVector(_deltaScreen);
    /// <summary> Gets the accumulated drag distance since the drag started, in the element's layout space, as opposed to Delta which is the change since the last event. </summary>
    public Float2 TotalDelta => ToLocalVector(_totalScreen);

    /// <summary> The change since the last drag event in screen pixels, whatever the element is transformed by. </summary>
    public Float2 ScreenDelta => _deltaScreen;
    /// <summary> The distance since the drag started in screen pixels, whatever the element is transformed by. </summary>
    public Float2 ScreenTotalDelta => _totalScreen;

    /// <summary> Gets the phase of the drag operation (Start, Dragging, or End) that this event represents. </summary>
    public DragPhase Phase { get; }

    /// <summary> Initialises a new DragEvent with the given source element, geometry, pointer delta values, and drag phase. </summary>
    public DragEvent(ElementHandle source, Rect elementRect, Float2 pointerPos, Float2 startPos, Float2 delta, Float2 totalDelta, DragPhase phase = DragPhase.Start)
        : base(source, elementRect, pointerPos)
    {
        _startScreen = startPos;
        _deltaScreen = delta;
        _totalScreen = totalDelta;
        Phase = phase;
    }
}
