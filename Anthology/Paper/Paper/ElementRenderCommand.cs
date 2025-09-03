using Prowl.Quill;
using Prowl.Vector;

namespace Prowl.PaperUI
{
    /// <summary>
    /// Represents a drawable UI element command.
    /// </summary>
    internal class ElementRenderCommand
    {
        public LayoutEngine.ElementHandle Element { get; set; }
        public Action<Canvas, Rect>? RenderAction { get; set; }
    }
}
