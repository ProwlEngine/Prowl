namespace Prowl.PaperUI.LayoutEngine
{
    internal class ChildElementInfo
    {
        public ElementHandle Element;
        public float CrossBefore;
        public float Cross;
        public float CrossAfter;
        public float MainBefore;
        public float Main;
        public float MainAfter;

        public ChildElementInfo(ElementHandle element)
        {
            Element = element;
        }
    }
}
