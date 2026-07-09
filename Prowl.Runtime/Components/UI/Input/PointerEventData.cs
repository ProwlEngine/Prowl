// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.UI;

public sealed class PointerEventData
{
    /// <summary>The mouse button (or pointer id) that produced this event.</summary>
    public MouseButton Button;

    /// <summary>
    /// Current pointer position in *screen pixels* (window coordinates, +Y down from top-left).
    /// Use <see cref="DesignPosition"/> for a value in the hit canvas's design-pixel space.
    /// </summary>
    public Float2 Position;

    /// <summary>Pointer position one frame ago, in screen pixels.</summary>
    public Float2 PreviousPosition;

    /// <summary>Per-frame pointer delta in screen pixels.</summary>
    public Float2 Delta;

    /// <summary>
    /// Pointer position projected into the hit canvas's design-pixel space
    /// (+Y up, origin bottom-left). Same coordinate system as
    /// <see cref="RectTransform.ComputedRect"/>. Zero when no canvas was hit.
    /// </summary>
    public Float2 DesignPosition;

    /// <summary>Scroll wheel delta (Y) for this frame.</summary>
    public float ScrollDelta;

    /// <summary>The GameObject currently under the pointer (topmost UI hit). Null if none.</summary>
    public GameObject? Hovered;

    /// <summary>The GameObject the pointer was over at the start of the press.</summary>
    public GameObject? PressedOn;

    /// <summary>The GameObject currently being dragged (set after the drag threshold is crossed).</summary>
    public GameObject? Dragging;

    /// <summary>Position the pointer was at when the press began (screen pixels).</summary>
    public Float2 PressPosition;

    /// <summary>Time at which the most recent press happened.</summary>
    public float PressTime;

    /// <summary>True once the pointer has moved past <see cref="UIEventSystem.DragThreshold"/> after a press.</summary>
    public bool IsDragging;

    /// <summary>Number of clicks in the current multi-click streak (resets after <see cref="UIEventSystem.MultiClickWindow"/>).</summary>
    public int ClickCount;

    /// <summary>Engine time of the most recent click - used by the multi-click detector.</summary>
    public float LastClickTime;

    /// <summary>The element the most recent click landed on - the multi-click detector requires the
    /// streak to stay on the same target (clicking two different buttons is not a double-click).</summary>
    public GameObject? LastClickTarget;

    /// <summary>The canvas whose plane was hit this frame. Null if no hit.</summary>
    public GameCanvas? HitCanvas;

    /// <summary>Set this from inside a handler to stop the event bubbling up the hierarchy.</summary>
    public bool Used;

    /// <summary>Mark the event consumed.</summary>
    public void Use() => Used = true;

    internal void Reset()
    {
        Position = PreviousPosition = Delta = DesignPosition = Float2.Zero;
        ScrollDelta = 0f;
        Hovered = PressedOn = Dragging = null;
        PressPosition = Float2.Zero;
        PressTime = 0f;
        IsDragging = false;
        ClickCount = 0;
        LastClickTime = 0f;
        LastClickTarget = null;
        HitCanvas = null;
        Used = false;
    }
}
