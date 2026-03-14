namespace Veldrid.StartupUtilities
{
    /// <summary>
    /// Event arguments for mouse wheel scroll events.
    /// </summary>
    public readonly struct MouseWheelEventArgs
    {
        /// <summary>
        /// The mouse state at the time of the event.
        /// </summary>
        public MouseState State { get; }

        /// <summary>
        /// The scroll wheel delta. Positive values indicate scrolling up/forward.
        /// </summary>
        public float WheelDelta { get; }

        public MouseWheelEventArgs(MouseState state, float wheelDelta)
        {
            State = state;
            WheelDelta = wheelDelta;
        }
    }
}
