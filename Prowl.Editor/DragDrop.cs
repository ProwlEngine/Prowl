using System;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Vector;

namespace Prowl.Editor;

/// <summary>
/// Base class for drag payloads. Subclass for different drag types.
/// </summary>
public abstract class DragPayload
{
    public abstract string DisplayName { get; }
    public abstract string Icon { get; }
}

/// <summary>Payload for dragging assets from the Project panel.</summary>
public class AssetDragPayload : DragPayload
{
    public Guid AssetGuid { get; }
    public string AssetName { get; }
    public Type? AssetType { get; }

    public override string DisplayName => AssetName;
    public override string Icon => EditorIcons.Cube;

    public AssetDragPayload(Guid guid, string name, Type? type)
    {
        AssetGuid = guid;
        AssetName = name;
        AssetType = type;
    }
}

/// <summary>Payload for dragging GameObjects in the hierarchy.</summary>
public class GameObjectDragPayload : DragPayload
{
    public GameObject[] GameObjects { get; }

    public override string DisplayName => GameObjects.Length == 1
        ? GameObjects[0].Name
        : $"{GameObjects.Length} objects";
    public override string Icon => EditorIcons.Cube;

    public GameObjectDragPayload(GameObject go) : this([go]) { }
    public GameObjectDragPayload(GameObject[] gos) => GameObjects = gos;
}

/// <summary>
/// Global drag & drop system for the editor.
/// Start a drag from any panel, drop in any panel that accepts the payload type.
/// </summary>
public static class DragDrop
{
    public static bool IsDragging { get; private set; }
    public static DragPayload? Payload { get; private set; }
    public static Float2 DragPosition { get; private set; }

    public static void StartDrag(DragPayload payload)
    {
        IsDragging = true;
        Payload = payload;
        DragPosition = new Float2(Input.MousePosition.X, Input.MousePosition.Y);
    }

    public static void UpdateDrag()
    {
        if (!IsDragging) return;
        DragPosition = new Float2(Input.MousePosition.X, Input.MousePosition.Y);

        // Cancel on escape
        if (Input.GetKeyDown(KeyCode.Escape))
            Cancel();

        // End drag on mouse release
        if (Input.GetMouseButtonUp(0))
            IsDragging = false; // Payload remains for one frame for drop targets to read
    }

    /// <summary>End the drag and consume the payload. Returns the payload or null.</summary>
    public static DragPayload? EndDrag()
    {
        var p = Payload;
        Payload = null;
        IsDragging = false;
        return p;
    }

    public static void Cancel()
    {
        IsDragging = false;
        Payload = null;
    }

    /// <summary>
    /// Check if a specific payload type is being dragged.
    /// </summary>
    public static bool IsDraggingType<T>() where T : DragPayload
        => IsDragging && Payload is T;

    /// <summary>
    /// Try to accept a drop of a specific payload type. Returns the payload if
    /// the mouse was released this frame over the element, null otherwise.
    /// Call this inside an element's scope.
    /// </summary>
    public static T? AcceptDrop<T>(bool isHovered) where T : DragPayload
    {
        if (!isHovered || Payload is not T typed) return null;

        // Mouse just released = drop
        if (!IsDragging && Payload != null)
        {
            Payload = null;
            return typed;
        }

        return null;
    }

    /// <summary>
    /// Draw the drag visual (ghost label following cursor). Call from EndGui.
    /// </summary>
    public static void DrawVisual(Paper paper)
    {
        if (!IsDragging || Payload == null) return;

        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        paper.Box("dd_ghost")
            .PositionType(PositionType.SelfDirected)
            .Position((float)DragPosition.X + 12, (float)DragPosition.Y + 4)
            .Width(UnitValue.Auto).Height(22)
            .BackgroundColor(System.Drawing.Color.FromArgb(200, 40, 40, 45))
            .BorderColor(EditorTheme.Accent).BorderWidth(1)
            .Rounded(4).ChildLeft(6).ChildRight(6)
            .IsNotInteractable()
            .Layer(Layer.Topmost)
            .Text($"{Payload.Icon}  {Payload.DisplayName}", font)
            .TextColor(EditorTheme.Ink500)
            .FontSize(EditorTheme.FontSize - 2)
            .Alignment(TextAlignment.MiddleLeft);
    }
}
