namespace Prowl.PaperUI.LayoutEngine
{
    internal class ChildElementInfo(Element element)
    {
        public Element Element { get; set; } = element;
        public float CrossBefore { get; set; }
        public float Cross { get; set; }
        public float CrossAfter { get; set; }
        public float MainBefore { get; set; }
        public float Main { get; set; }
        public float MainAfter { get; set; }
    }
}
