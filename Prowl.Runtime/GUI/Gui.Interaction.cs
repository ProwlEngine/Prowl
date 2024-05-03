using Silk.NET.SDL;
using System.Collections.Generic;
using static Prowl.Runtime.NodeSystem.NodePort;

namespace Prowl.Runtime.GUI
{
    public partial class Gui
    {
        private static Vector2 mousePosition => Input.MousePosition;

        private Dictionary<ulong, Interactable> _interactables = [];

        internal ulong FocusID = 0;
        internal ulong? ActiveID = 0;
        internal ulong HoveredID = 0;
        internal ulong PreviousInteractableID = 0;
        internal Rect ActiveRect = Rect.Zero;

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
            }
            else if (ActiveID == 0)
            {
                ActiveID = null;
            }
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

            if (!_interactables.TryGetValue(interactID, out Interactable interact))
                interact = new(this, interactID, rect);
            interact._rect = rect;

            _interactables[interactID] = interact;

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
        public void PreviousControlFocus() => FocusID = PreviousInteractableID;
    }

    public struct Interactable
    {
        public ulong ID => _id;

        private Gui _gui;
        internal ulong _id;
        internal Rect _rect;

        internal Interactable(Gui g, ulong id, Rect rect)
        {
            _gui = g;
            _id = id;
            _rect = rect;
        }

        public void UpdateContext(bool onlyHovered = false)
        {
            // Check if mouse is inside the clip rect
            var clip = _gui._drawList[_gui.CurrentZIndex]._ClipRectStack.Peek();
            var overClip = _gui.IsMouseOverRect(new(clip.x, clip.y, (clip.z - clip.x), (clip.w - clip.y)));
            if (!overClip)
                return;



            // Make sure mouse is also over our rect
            if (_gui.IsMouseOverRect(_rect))
            {
                _gui.HoveredID = _id;

                if (_gui.ActiveID == 0 && _gui.IsPointerDown(Silk.NET.Input.MouseButton.Left) && !onlyHovered)
                {
                    _gui.ActiveID = _id;
                    _gui.ActiveRect = _rect;
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