using System.Collections.Generic;
using System.Numerics;

namespace NeoVeldrid.StartupUtilities
{
    /// <summary>
    /// Accumulates input events from Silk.NET callbacks and provides them as a NeoVeldrid InputSnapshot.
    /// </summary>
    public class SimpleInputSnapshot : InputSnapshot
    {
        private readonly List<KeyEvent> _keyEvents = new();
        private readonly List<MouseEvent> _mouseEvents = new();
        private readonly List<char> _keyCharPresses = new();
        private readonly bool[] _mouseDown = new bool[(int)MouseButton.LastButton + 1];

        public IReadOnlyList<KeyEvent> KeyEvents => _keyEvents;
        public IReadOnlyList<MouseEvent> MouseEvents => _mouseEvents;
        public IReadOnlyList<char> KeyCharPresses => _keyCharPresses;
        public Vector2 MousePosition { get; internal set; }
        public float WheelDelta { get; internal set; }

        public bool IsMouseDown(MouseButton button)
        {
            int index = (int)button;
            return index < _mouseDown.Length && _mouseDown[index];
        }

        internal void AddKeyEvent(KeyEvent ev) => _keyEvents.Add(ev);
        internal void AddMouseEvent(MouseEvent ev) => _mouseEvents.Add(ev);
        internal void AddKeyChar(char c) => _keyCharPresses.Add(c);

        internal void SetMouseButton(MouseButton button, bool down)
        {
            int index = (int)button;
            if (index < _mouseDown.Length)
                _mouseDown[index] = down;
        }

        internal void Clear()
        {
            _keyEvents.Clear();
            _mouseEvents.Clear();
            _keyCharPresses.Clear();
            WheelDelta = 0;
        }

        internal void CopyTo(SimpleInputSnapshot other)
        {
            System.Diagnostics.Debug.Assert(this != other);

            other._mouseEvents.Clear();
            foreach (var me in _mouseEvents) { other._mouseEvents.Add(me); }

            other._keyEvents.Clear();
            foreach (var ke in _keyEvents) { other._keyEvents.Add(ke); }

            other._keyCharPresses.Clear();
            foreach (var kcp in _keyCharPresses) { other._keyCharPresses.Add(kcp); }

            other.MousePosition = MousePosition;
            other.WheelDelta = WheelDelta;
            _mouseDown.CopyTo(other._mouseDown, 0);
        }
    }
}
