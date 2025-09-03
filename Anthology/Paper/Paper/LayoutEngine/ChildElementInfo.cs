namespace Prowl.PaperUI.LayoutEngine
{
    internal class ChildElementInfo
    {
        public ElementHandle Element;
        public double CrossBefore;
        public double Cross;
        public double CrossAfter;
        public double MainBefore;
        public double Main;
        public double MainAfter;

        public ChildElementInfo(ElementHandle element)
        {
            Element = element;
        }
    }
}
