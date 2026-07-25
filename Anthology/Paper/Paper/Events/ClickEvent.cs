// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.PaperUI.LayoutEngine;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.PaperUI.Events;

/// <summary> Defines the stage or type of a click interaction: Press, Release, Click, DoubleClick, RightClick, or Held. </summary>
public enum ClickPhase
{
    Click,
    Press,
    Release,
    DoubleClick,
    RightClick,
    /// <summary> The phase during which a mouse button is continuously held down after the initial press. </summary>
    Held
}

/// <summary> Represents a mouse click event raised by a UI element, including the button and click phase. </summary>
public class ClickEvent : ElementEvent
{
    /// <summary> Gets the mouse button that triggered this event. </summary>
    public PaperMouseBtn Button { get; }

    /// <summary>
    /// Identifies which click handler this event targets during bubbling.
    /// </summary>
    public ClickPhase Phase { get; }

    /// <summary> Initializes a new click event with the given source element, element rectangle, pointer position, mouse button, and click phase. </summary>
    public ClickEvent(ElementHandle source, Rect elementRect, Float2 pointerPos, PaperMouseBtn button, ClickPhase phase = ClickPhase.Click)
        : base(source, elementRect, pointerPos)
    {
        Button = button;
        Phase = phase;
    }
}
