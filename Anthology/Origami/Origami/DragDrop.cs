// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// Base class for drag payloads. Subclass to carry any data type during a drag operation.
/// </summary>
public abstract class DragPayload
{
    /// <summary>Display name shown in the drag ghost tooltip.</summary>
    public abstract string DisplayName { get; }

    /// <summary>Icon glyph shown in the drag ghost.</summary>
    public virtual string Icon => "";
}

/// <summary>
/// Global drag and drop system for Origami. Supports typed payloads, hover feedback,
/// drop acceptance with validation, and an animated drag ghost visual.
///
/// Start a drag from any element, drop in any element that accepts the payload type.
/// One drag operation at a time. The payload persists for one frame after mouse release
/// so drop targets can detect and consume it.
/// </summary>
public static class DragDrop
{
    /// <summary>True while a drag is in progress (mouse held).</summary>
    public static bool IsDragging { get; private set; }

    /// <summary>The current drag payload, or null.</summary>
    public static DragPayload? Payload { get; private set; }

    /// <summary>Current drag position in Paper coordinates.</summary>
    public static Prowl.Vector.Float2 DragPosition { get; private set; }

    /// <summary>True on the frame the mouse was released after a drag. Payload still available for drop targets.</summary>
    public static bool IsDropFrame => !IsDragging && Payload != null;

    // ── Start / End / Cancel ─────────────────────────────────

    /// <summary>Begin a drag operation with the given payload.</summary>
    public static void StartDrag(DragPayload payload)
    {
        IsDragging = true;
        Payload = payload;
    }

    /// <summary>End the drag and consume the payload. Returns the payload or null.</summary>
    public static DragPayload? EndDrag()
    {
        var p = Payload;
        Payload = null;
        IsDragging = false;
        return p;
    }

    /// <summary>Cancel the drag, discarding the payload.</summary>
    public static void Cancel()
    {
        IsDragging = false;
        Payload = null;
    }

    // ── Query ────────────────────────────────────────────────

    /// <summary>Check if a specific payload type is being dragged (mouse still held).</summary>
    public static bool IsDraggingType<T>() where T : DragPayload
        => IsDragging && Payload is T;

    /// <summary>Check if a payload type is present (dragging OR on the drop frame).</summary>
    public static bool HasPayloadType<T>() where T : DragPayload
        => Payload is T;

    /// <summary>Get the payload as a specific type, or null.</summary>
    public static T? GetPayload<T>() where T : DragPayload
        => Payload as T;

    // ── Drop acceptance ──────────────────────────────────────

    /// <summary>
    /// Try to accept a drop of a specific type. Returns the payload if the mouse was
    /// released this frame over the hovered element. Consumes the payload.
    /// </summary>
    public static T? AcceptDrop<T>(bool isHovered) where T : DragPayload
    {
        if (!isHovered || Payload is not T typed) return null;
        if (IsDropFrame)
        {
            Payload = null;
            return typed;
        }
        return null;
    }

    /// <summary>Accept drop with additional validation predicate.</summary>
    public static T? AcceptDrop<T>(bool isHovered, Func<T, bool> validate) where T : DragPayload
    {
        if (!isHovered || Payload is not T typed) return null;
        if (IsDropFrame && validate(typed))
        {
            Payload = null;
            return typed;
        }
        return null;
    }

    // ── Per-frame update ─────────────────────────────────────

    /// <summary>Call once per frame. Tracks drag position and detects mouse release.</summary>
    public static void Update(Paper paper)
    {
        // Clear stale payload from previous drop frame
        if (!IsDragging && Payload != null)
        {
            Payload = null;
            return;
        }

        if (!IsDragging) return;

        DragPosition = paper.PointerPos;

        // Cancel on escape
        if (paper.IsKeyPressed(PaperKey.Escape))
        {
            Cancel();
            return;
        }

        // End drag on mouse release (payload persists for one frame for drop targets)
        if (!paper.IsPointerDown(PaperMouseBtn.Left))
            IsDragging = false;
    }

    // ── Visual ───────────────────────────────────────────────

    /// <summary>Draw the drag ghost visual (label following cursor). Call at end of frame.</summary>
    public static void DrawVisual(Paper paper)
    {
        if (!IsDragging || Payload == null) return;

        var theme = Origami.Current;
        var font = theme.Font;
        var m = theme.Metrics;
        if (font == null) return;

        string text = Payload.DisplayName;
        string icon = Payload.Icon;
        bool hasIcon = !string.IsNullOrEmpty(icon);

        string display = hasIcon ? $"{icon}  {text}" : text;

        float mx = (float)DragPosition.X + 12;
        float my = (float)DragPosition.Y + m.Spacing;

        paper.Box("dd_ghost")
            .PositionType(PositionType.SelfDirected)
            .Position(mx, my)
            .Width(UnitValue.Auto).Height(m.HeaderHeight)
            .BackgroundColor(Color.FromArgb(200, 40, 40, 45))
            .BorderColor(theme.Primary.C400).BorderWidth(1)
            .Rounded(m.Rounding).ChildLeft(m.Padding).ChildRight(m.Padding)
            .IsNotInteractable()
            .Layer(Layer.Topmost + 500)
            .Text(display, font)
            .TextColor(theme.Ink.C500)
            .FontSize(m.FontSizeSmall)
            .Alignment(TextAlignment.MiddleLeft);
    }
}
