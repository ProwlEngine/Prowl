﻿// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.GUI.Layout;

namespace Prowl.Runtime.GUI;

public partial class Gui
{
    public ulong FocusID { get; internal set; } = 0;
    public ulong? ActiveID { get; internal set; } = 0;
    public ulong HoveredID { get; internal set; } = 0;
    public Rect ActiveRect { get; internal set; } = Rect.Zero;
    public Interactable? PreviousInteractable { get; private set; }


    private Dictionary<ulong, Interactable> _oldinteractables = [];
    private List<(double, Rect)> _oldblockers = [];
    private readonly Dictionary<ulong, Interactable> _interactables = [];
    private readonly List<(double, Rect)> _blockers = [];
    private readonly Dictionary<int, int> _zInteractableCounter = [];


    private void StartInteractionFrame()
    {
        HoveredID = 0;
        PreviousInteractable = null;
    }

    private void EndInteractionFrame()
    {
        if (!IsPointerDown(MouseButton.Left))
        {
            ActiveID = 0;
            DragDrop_Clear();
        }
        else if (ActiveID == 0)
        {
            ActiveID = null;
        }

        _oldinteractables.Clear();
        _oldblockers.Clear();
        _oldinteractables = new(_interactables);
        _oldblockers = [.._blockers];
        _interactables.Clear();
        _blockers.Clear();
        _zInteractableCounter.Clear();
    }

    private double GetNextInteractableLayer(int zIndex)
    {
        if (!_zInteractableCounter.TryGetValue(zIndex, out int count))
            count = 0;

        _zInteractableCounter[zIndex] = count + 1;

        // ZIndex.count - supports up to 1k interactables per Z Index
        return zIndex + (count / 1000.0);
    }

    public double GetCurrentInteractableZLayer()
    {
        if (!_zInteractableCounter.TryGetValue(CurrentZIndex, out int count))
            count = 0;
        return CurrentZIndex + (count / 1000.0);
    }

    public void ClearFocus()
    {
        FocusID = 0;
    }

    /// <summary>
    /// Block all interactables in the given rect below this ZIndex/Node
    /// Usefull for Popups/Windows when you want to prevent interaction with things below/behind it
    /// </summary>
    public void BlockInteractables(Rect rect)
        => _blockers.Add((GetNextInteractableLayer(CurrentZIndex), rect));

    /// <summary>
    /// Get an interactable for the current node
    /// Creates a new interactable if one does not exist
    /// Otherwise returned the existing one found on this node
    /// </summary>
    /// <returns></returns>
    public Interactable GetInteractable(Rect? interactArea = null) => GetInteractable(CurrentNode, interactArea);

    /// <summary>
    /// Get an interactable for the current node
    /// Creates a new interactable if one does not exist
    /// Otherwise returned the existing one found on this node
    /// </summary>
    /// <returns></returns>
    public Interactable GetInteractable(LayoutNode target, Rect? interactArea = null)
    {
        Rect rect = interactArea ?? target.LayoutData.Rect;

        rect.Clip(Draw2D.PeekClip());

        if (_interactables.ContainsKey(target.ID))
            return _interactables[target.ID];

        if (!_oldinteractables.TryGetValue(target.ID, out Interactable interact))
            interact = new(this, target.ID, rect, GetNextInteractableLayer(CurrentZIndex));
        else
        {
            interact._rect = rect;
            interact.zIndex = GetNextInteractableLayer(CurrentZIndex);
        }

        _interactables[target.ID] = interact;
        interact.UpdateContext();
        PreviousInteractable = interact;
        return interact;
    }

    /// <summary>
    /// Check if the a position is blocked by any interactable or blocker that is above the given ZIndex
    /// If no ZIndex is given, it will use the current ZIndex
    /// </summary>
    public bool IsBlockedByInteractable(Vector2 pos, double zIndex = -1, ulong ignoreID = 0)
    {
        if (MathD.ApproximatelyEquals(zIndex, -1))
        {
            if (!_zInteractableCounter.TryGetValue(CurrentZIndex, out int count))
                count = 0;

            // ZIndex.count - supports up to 1k interactables per Z Index
            zIndex = CurrentZIndex + (count / 1000.0);
        }

        // Check if there is any interactable with a higher ZIndex that intersects the current position
        bool isObstructed = false;
        foreach (var interactable in _oldinteractables.Values)
        {
            if (interactable._id != ignoreID && interactable.zIndex > zIndex && interactable._rect.Contains(pos))
            {
                isObstructed = true;
                break;
            }
        }

        if (!isObstructed)
        {
            foreach (var blocker in _oldblockers)
            {
                if (blocker.Item1 > zIndex && blocker.Item2.Contains(pos))
                {
                    isObstructed = true;
                    break;
                }
            }
        }

        return isObstructed;
    }

    internal bool IsPointerOver(Rect rect) => PointerPos.x >= rect.x && PointerPos.x <= rect.x + rect.width && PointerPos.y >= rect.y && PointerPos.y <= rect.y + rect.height;
    /// <inheritdoc cref="IsPointerHovering(Rect)"/>
    public bool IsPointerHovering() => IsPointerHovering(CurrentNode.LayoutData.Rect);
    /// <summary>
    /// Checks if the pointer is hovering over the given rect
    /// Taking into account the current clip rect
    /// It does not however take into account Interactables that may be blocking the pointer
    /// For that use <see cref="IsBlockedByInteractable(Vector2, double, ulong)"/>
    /// If your looking for a way to check if the pointer can interact with something, use <see cref="IsNodeHovered(Rect?)"/> or <see cref="GetInteractable(Rect?)"/>.IsHovered()
    /// </summary>
    public bool IsPointerHovering(Rect rect)
    {
        var clip = Draw2D.PeekClip();

        var overClip = IsPointerOver(clip);

        return overClip && IsPointerOver(rect);
    }

    /// <summary> A Shortcut to <see cref="GetInteractable(Rect?)"/>.TakeFocus() </summary>
    public bool IsNodePressed(bool onrelease) => GetInteractable().TakeFocus(onrelease);
    public bool IsNodePressed(Rect? interactArea = null, bool onrelease = false) => GetInteractable(interactArea).TakeFocus(onrelease);
    /// <summary> A Shortcut to <see cref="GetInteractable(Rect?)"/>.IsHovered() </summary>
    public bool IsNodeHovered(Rect? interactArea = null) => GetInteractable(interactArea).IsHovered();
    /// <summary> A Shortcut to <see cref="GetInteractable(Rect?)"/>.IsFocused() </summary>
    public bool IsNodeFocused(Rect? interactArea = null) => GetInteractable(interactArea).IsFocused();
    /// <summary> A Shortcut to <see cref="GetInteractable(Rect?)"/>.IsActive() </summary>
    public bool IsNodeActive(Rect? interactArea = null) => GetInteractable(interactArea).IsActive();

    public bool PreviousInteractableIsHovered() => HoveredID == (PreviousInteractable?.ID ?? 0);
    public bool PreviousInteractableIsActive() => ActiveID == (PreviousInteractable?.ID);
    public bool PreviousInteractableIsFocus() => FocusID == (PreviousInteractable?.ID);

    public void FocusPreviousInteractable()
    {
        if ((PreviousInteractable?.ID ?? 0) != 0)
            FocusID = PreviousInteractable!.Value.ID;
    }

    #region Drag & Drop

    internal static bool IsDragDropActive = false;
    internal static ulong DragDropID;

    public bool DragDrop_Source(out LayoutNode? node)
    {
        node = null;
        if (PreviousInteractableIsActive() && (IsDragDropActive || IsPointerMoving))
        {
            IsDragDropActive = true;
            DragDropID = PreviousInteractable?.ID ?? 0;
            using ((node = rootNode.AppendNode("_DragDrop")).Left(PointerPos.x).Top(PointerPos.y).IgnoreLayout().Enter())
            {
                SetZIndex(50000);

                // Clamp node position so that its always in screen bounds
                var rect = CurrentNode.LayoutData.Rect;
                if (rect.x + rect.width > ScreenRect.width)
                    CurrentNode.Left(ScreenRect.width - rect.width);
                if (rect.y + rect.height > ScreenRect.height)
                    CurrentNode.Top(ScreenRect.height - rect.height);

            }
            return true;
        }
        return false;
    }

    public static void DragDrop_Clear()
    {
        if (IsDragDropActive)
        {
            IsDragDropActive = false;
            DragDropID = 0;
        }
    }

    public bool DragDrop_Target()
    {
        if (IsDragDropActive)
            if (PreviousInteractableIsHovered())
                return true;
        return false;
    }

    public bool DragDrop_Accept()
    {
        if (IsDragDropActive)
        {
            if (PreviousInteractableIsHovered() && !IsPointerDown(MouseButton.Left))
            {
                IsDragDropActive = false;
                DragDropID = 0;
                return true;
            }
        }
        return false;
    }

    #endregion
}

public struct Interactable
{
    public ulong ID => _id;
    public Rect Rect => _rect;
    public double ZIndex => zIndex;

    private readonly Gui _gui;
    internal ulong _id;
    internal Rect _rect;
    internal double zIndex;

    internal Interactable(Gui gui, ulong id, Rect rect, double z)
    {
        _gui = gui;
        _id = id;
        _rect = rect;
        zIndex = z;
    }

    internal void UpdateContext(bool onlyHovered = false)
    {
        if (Gui.IsDragDropActive && Gui.DragDropID == ID)
        {
            if (_gui.HoveredID == _id)
                _gui.HoveredID = 0;
            return;
        }

        // Check if mouse is inside the clip rect
        var clip = _gui.Draw2D.PeekClip();
        var overClip = _gui.IsPointerOver(clip);
        if (!overClip)
            return;

        // Make sure mouse is also over our rect
        if (_gui.IsPointerOver(_rect))
        {
            if (!_gui.IsBlockedByInteractable(_gui.PointerPos, zIndex, _id))
            {
                _gui.HoveredID = _id;

                if (_gui.ActiveID == 0 && _gui.IsPointerDown(MouseButton.Left) && !onlyHovered)
                {
                    _gui.ActiveID = _id;
                    _gui.ActiveRect = _rect;
                }
            }
        }
    }

    /// <summary>
    /// Check if the Interactable is hovered and clicked on if it is, it will take focus
    /// </summary>
    /// <returns>True on the frame the Interactable took focus, Great for Buttons: if(Interactable.TakeFocus())</returns>
    public bool TakeFocus(bool onrelease = false, MouseButton focusBtn = MouseButton.Left)
    {
        // Clicking on another Interactable will remove focus
        //if (_gui.FocusID == _id && _gui.HoveredID != _id && _gui.IsPointerDown(Veldrid.MouseButton.Left))
        //    _gui.FocusID = 0;

        // If we are hovered and active, we are focused
        //if (_gui.HoveredID == _id && _gui.ActiveID == _id && !_gui.IsPointerDown(Veldrid.MouseButton.Left))
        if (_gui.HoveredID == _id && _gui.ActiveID == _id && _gui.IsPointerClick(focusBtn))
        {
            _gui.FocusID = _id;
            return true;
        }

        return false;
    }

    /// <summary> Check if the Interactable is hovered </summary>
    public bool IsHovered() => _gui.HoveredID == _id;
    /// <summary> Check if the Interactable is active </summary>
    public bool IsActive() => _gui.ActiveID == _id;
    /// <summary> Check if the Interactable is focused </summary>
    public bool IsFocused() => _gui.FocusID == _id;
}
