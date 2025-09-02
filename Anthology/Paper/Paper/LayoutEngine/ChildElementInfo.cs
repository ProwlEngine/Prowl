namespace Prowl.PaperUI.LayoutEngine
{
    internal class ChildElementInfo
    {
        public ElementHandle Element { get; set; }
        public double CrossBefore { get; set; }
        public double Cross { get; set; }
        public double CrossAfter { get; set; }
        public double MainBefore { get; set; }
        public double Main { get; set; }
        public double MainAfter { get; set; }

        public ChildElementInfo(ElementHandle element)
        {
            Element = element;
        }
    }
}
