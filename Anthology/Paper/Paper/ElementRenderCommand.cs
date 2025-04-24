using Prowl.Quill;
using Prowl.Vector;

namespace Prowl.PaperUI
{
    /// <summary>
    /// Represents a drawable UI element command.
    /// </summary>
    internal class ElementRenderCommand
    {
        public enum ElementType
        {
            Text,
            RenderAction,
        }

        public ElementType Type { get; set; }
        public LayoutEngine.Element Element { get; set; }
        public Text? Text { get; set; }
        public Action<Canvas, Rect>? RenderAction { get; set; }
    }
}
