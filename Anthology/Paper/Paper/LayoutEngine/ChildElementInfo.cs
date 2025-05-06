namespace Prowl.PaperUI.LayoutEngine
{
    internal class ChildElementInfo
    {
        public Element Element { get; set; }
        public double CrossBefore { get; set; }
        public double Cross { get; set; }
        public double CrossAfter { get; set; }
        public double MainBefore { get; set; }
        public double Main { get; set; }
        public double MainAfter { get; set; }

        public ChildElementInfo(Element element)
        {
            Element = element;
        }
    }
}
