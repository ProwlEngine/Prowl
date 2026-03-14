using System.Numerics;

namespace Veldrid.StartupUtilities
{
    /// <summary>
    /// Event arguments for mouse move events.
    /// </summary>
    public readonly struct MouseMoveEventArgs
    {
        /// <summary>
        /// The mouse state at the time of the event.
        /// </summary>
        public MouseState State { get; }

        /// <summary>
        /// The current mouse position in client coordinates.
        /// </summary>
        public Vector2 MousePosition { get; }

        public MouseMoveEventArgs(MouseState state, Vector2 mousePosition)
        {
            State = state;
            MousePosition = mousePosition;
        }
    }
}
