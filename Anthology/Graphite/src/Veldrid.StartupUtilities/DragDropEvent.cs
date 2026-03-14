namespace Veldrid.StartupUtilities
{
    /// <summary>
    /// Event arguments for file drag-and-drop events.
    /// </summary>
    public readonly struct DragDropEvent
    {
        /// <summary>
        /// The path of the dropped file.
        /// </summary>
        public string File { get; }

        public DragDropEvent(string file)
        {
            File = file;
        }
    }
}
