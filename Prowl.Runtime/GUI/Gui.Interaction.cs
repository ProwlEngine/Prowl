namespace Prowl.Runtime.GUI
{
    public partial class Gui
    {
        private static Vector2 mousePosition => Input.MousePosition;

        public Interactable GetInteractable() => new(this, CurrentNode.LayoutData.InnerRect);
        public Interactable GetInteractable(Rect rect) => new(this, rect);

        public bool IsMouseOverRect(Rect rect) => mousePosition.x >= rect.x && mousePosition.x <= rect.x + rect.width && mousePosition.y >= rect.y && mousePosition.y <= rect.y + rect.height;
        public bool IsHovering(bool inner = false) => IsHovering(inner ? CurrentNode.LayoutData.InnerRect : CurrentNode.LayoutData.Rect);
        public bool IsHovering(Rect rect)
        {
            var clip = _drawList[CurrentZIndex]._ClipRectStack.Peek();

            var overClip = IsMouseOverRect(new(clip.x, clip.y, (clip.z - clip.x), (clip.w - clip.y)));

            return overClip && IsMouseOverRect(rect);
        }
        public bool IsPressed(bool inner = false) => IsHovering(inner) && Input.GetMouseButton(0);
        public bool WasPressed(bool inner = false) => IsHovering(inner) && Input.GetMouseButtonUp(0);
        public bool IsClicked(bool inner = false) => IsHovering(inner) && Input.GetMouseButtonDown(0);
        public bool IsRightClicked(bool inner = false) => IsHovering(inner) && Input.GetMouseButtonDown(1);
        public bool IsHeld(bool inner = false) => IsHovering(inner) && Input.GetMouseButton(0) && !Input.GetMouseButtonDown(0);

        public bool IsPressed(Rect rect) => IsHovering(rect) && Input.GetMouseButton(0);
        public bool WasPressed(Rect rect) => IsHovering(rect) && Input.GetMouseButtonUp(0);
        public bool IsClicked(Rect rect) => IsHovering(rect) && Input.GetMouseButtonDown(0);
        public bool IsRightClicked(Rect rect) => IsHovering(rect) && Input.GetMouseButtonDown(1);
        public bool IsHeld(Rect rect) => IsHovering(rect) && Input.GetMouseButton(0) && !Input.GetMouseButtonDown(0);
    }

    public struct Interactable
    {
        private bool _hovered;
        private bool? _pressed;
        private bool? _clicked;
        private bool? _rightclicked;
        private bool? _held;

        internal Interactable(Gui g, Rect rect)
        {
            _hovered = g.IsHovering(rect);
        }

        public bool OnHover() => _hovered;
        public bool OnPressed()
        {
            if (!_hovered) return false;
            _pressed ??= Input.GetMouseButton(0);
            return _pressed.Value;
        }
        public bool OnClick()
        {
            if (!_hovered) return false;
            _clicked ??= Input.GetMouseButtonDown(0);
            return _clicked.Value;
        }
        public bool OnRightClick()
        {
            if (!_hovered) return false;
            _rightclicked ??= Input.GetMouseButtonDown(1);
            return _rightclicked.Value;
        }
        public bool OnHold()
        {
            if (!_hovered) return false;
            _held ??= Input.GetMouseButton(0) && !Input.GetMouseButtonDown(0);
            return _held.Value;
        }
    }
}