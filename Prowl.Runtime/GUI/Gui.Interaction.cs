using Prowl.Runtime.GUI.Layout;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Xml.Linq;

namespace Prowl.Runtime.GUI
{
    public partial class Gui
    {
        private static Vector2 mousePosition => Input.MousePosition;

        internal Dictionary<ulong, Interactable> _oldinteractables = [];
        internal List<(double, Rect)> _oldblockers = [];
        internal Dictionary<ulong, Interactable> _interactables = [];
        internal List<(double, Rect)> _blockers = [];
        internal Dictionary<int, int> _zInteractableCounter = [];

        public ulong FocusID = 0;
        public ulong? ActiveID = 0;
        public ulong HoveredID = 0;
        public Vector2 PreviousInteractablePointerPos;
        public ulong PreviousInteractableID = 0;
        public Rect ActiveRect = Rect.Zero;

        private void StartInteractionFrame()
        {
            HoveredID = 0;
            PreviousInteractableID = 0;
        }

        private void EndInteractionFrame()
        {
            if (!IsPointerDown(Silk.NET.Input.MouseButton.Left))
            {
                ActiveID = 0;
                ClearDragDrop();
            }
            else if (ActiveID == 0)
            {
                ActiveID = null;
            }

            _oldinteractables.Clear();
            _oldblockers.Clear();
            _oldinteractables = new(_interactables);
            _oldblockers = new(_blockers);
            _interactables.Clear();
            _blockers.Clear();
            _zInteractableCounter.Clear();
        }

        private double GetNextInteractableLayer(int zIndex)
        {
            if (!_zInteractableCounter.TryGetValue(zIndex, out int count))
                count = 0;

            _zInteractableCounter[zIndex] = count + 1;

            // ZIndex.count - supports up to 1k interactables per layer
            return zIndex + (count / 1000.0);
        }

        public void CreateBlocker(Rect rect)
        {
            _blockers.Add((GetNextInteractableLayer(CurrentZIndex), rect));
        }

        public Interactable GetInteractable(bool inner = false, bool hasScrollV = false)
        {
            Rect rect = inner ? CurrentNode.LayoutData.InnerRect : CurrentNode.LayoutData.Rect;
            if (hasScrollV)
                rect.width -= ScrollVWidth + ScrollVPadding;
            return GetInteractable(rect);
        }
        public Interactable GetInteractable(Rect rect)
        {
            ulong interactID = 17;
            interactID = interactID * 23 + (ulong)CurrentNode.GetNextInteractable();
            interactID = interactID * 23 + CurrentNode.ID;

            var z = GetNextInteractableLayer(CurrentZIndex);
            if (!_oldinteractables.TryGetValue(interactID, out Interactable interact))
                interact = new(this, interactID, rect, z);
            interact._rect = rect;
            interact.zIndex = z;

            _interactables[interactID] = interact;

            PreviousInteractablePointerPos = mousePosition;
            PreviousInteractableID = interactID;

            interact.UpdateContext();

            return interact;
        }

        public bool IsMouseOverRect(Rect rect) => mousePosition.x >= rect.x && mousePosition.x <= rect.x + rect.width && mousePosition.y >= rect.y && mousePosition.y <= rect.y + rect.height;
        public bool IsHovering(bool inner = false) => IsHovering(inner ? CurrentNode.LayoutData.InnerRect : CurrentNode.LayoutData.Rect);
        public bool IsHovering(Rect rect)
        {
            var clip = _drawList[CurrentZIndex]._ClipRectStack.Peek();

            var overClip = IsMouseOverRect(new(clip.x, clip.y, (clip.z - clip.x), (clip.w - clip.y)));

            return overClip && IsMouseOverRect(rect);
        }

        public bool PreviousControlIsHovered() => HoveredID == PreviousInteractableID;
        public bool PreviousControlIsActive() => ActiveID == PreviousInteractableID;
        public bool PreviousControlIsFocus() => FocusID == PreviousInteractableID;


        public static bool IsDragDropActive = false;
        public static ulong DragDropID;
        public bool DragDropSource(out LayoutNode? node)
        {
            node = null;
            if (PreviousControlIsActive() && (IsDragDropActive || IsPointerMoving))
            {
                IsDragDropActive = true;
                DragDropID = PreviousInteractableID;
                using ((node = _nodes[0].AppendNode()).Left(PointerPos.x).Top(PointerPos.y).IgnoreLayout().Enter())
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

        public static void ClearDragDrop()
        {
            if (IsDragDropActive)
            {
                IsDragDropActive = false;
                DragDropID = 0;
            }
        }

        public bool DragDropTarget()
        {
            if (IsDragDropActive)
                if (PreviousControlIsHovered())
                    return true;
            return false;
        }

        public bool AcceptDragDrop()
        {
            if (IsDragDropActive)
            {
                if (PreviousControlIsHovered() && !IsPointerDown(Silk.NET.Input.MouseButton.Left))
                {
                    IsDragDropActive = false;
                    DragDropID = 0;
                    return true;
                }
            }
            return false;
        }

    }

    public struct Interactable
    {
        public ulong ID => _id;

        private Gui _gui;
        internal ulong _id;
        internal Rect _rect;
        internal double zIndex;

        internal Interactable(Gui g, ulong id, Rect rect, double z)
        {
            _gui = g;
            _id = id;
            _rect = rect;
            zIndex = z;
        }

        public void UpdateContext(bool onlyHovered = false)
        {
            if (Gui.IsDragDropActive && Gui.DragDropID == ID)
            {
                if(_gui.HoveredID == _id)
                    _gui.HoveredID = 0;
                return;
            }

            // Check if mouse is inside the clip rect
            var clip = _gui._drawList[_gui.CurrentZIndex]._ClipRectStack.Peek();
            var overClip = _gui.IsMouseOverRect(new(clip.x, clip.y, (clip.z - clip.x), (clip.w - clip.y)));
            if (!overClip)
                return;

            // Make sure mouse is also over our rect
            if (_gui.IsMouseOverRect(_rect))
            {

                // Check if there is any interactable with a higher ZIndex that intersects the current position
                bool isObstructed = false;
                foreach (var interactable in _gui._oldinteractables.Values)
                {
                    if (interactable._id != _id && interactable.zIndex > zIndex && _gui.IsMouseOverRect(interactable._rect))
                    {
                        isObstructed = true;
                        break;
                    }
                }

                foreach (var blocker in _gui._oldblockers)
                {
                    if (blocker.Item1 > zIndex && _gui.IsMouseOverRect(blocker.Item2))
                    {
                        isObstructed = true;
                        break;
                    }
                }

                if (!isObstructed)
                {
                    _gui.HoveredID = _id;

                    if (_gui.ActiveID == 0 && _gui.IsPointerDown(Silk.NET.Input.MouseButton.Left) && !onlyHovered)
                    {
                        _gui.ActiveID = _id;
                        _gui.ActiveRect = _rect;
                    }
                }
            }
        }

        public bool TakeFocus()
        {
            // Clicking on another Interactable will remove focus
            if (_gui.FocusID == _id && _gui.HoveredID != _id && _gui.IsPointerDown(Silk.NET.Input.MouseButton.Left))
                _gui.FocusID = 0;

            // If we are hovered and active, we are focused
            if (_gui.HoveredID == _id && _gui.ActiveID == _id && !_gui.IsPointerDown(Silk.NET.Input.MouseButton.Left))
            {
                _gui.FocusID = _id;
                return true;
            }

            return false;
        }

        public bool IsHovered() => _gui.HoveredID == _id;
        public bool IsActive() => _gui.ActiveID == _id;
        public bool IsFocused() => _gui.FocusID == _id;
    }
}