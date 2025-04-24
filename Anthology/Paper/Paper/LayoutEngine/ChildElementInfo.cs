namespace Prowl.PaperUI.LayoutEngine
{
    internal class ChildElementInfo(Element element)
    {
        public Element Element { get; set; } = element;
        public double CrossBefore { get; set; }
        public double Cross { get; set; }
        public double CrossAfter { get; set; }
        public double MainBefore { get; set; }
        public double Main { get; set; }
        public double MainAfter { get; set; }
    }
}
