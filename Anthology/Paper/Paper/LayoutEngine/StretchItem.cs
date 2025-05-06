namespace Prowl.PaperUI.LayoutEngine
{
    internal class StretchItem
    {
        public enum ItemTypes { Before, Size, After }

        public int Index { get; set; }
        public double Factor { get; set; }
        public ItemTypes ItemType { get; set; }
        public double Violation { get; set; } = 0f;
        public double Computed { get; set; } = 0f;
        public bool Frozen { get; set; } = false;
        public double Min { get; set; }
        public double Max { get; set; }

        public StretchItem(int index, double factor, ItemTypes itemType, double min, double max)
        {
            Index = index;
            Factor = factor;
            ItemType = itemType;
            Min = min;
            Max = max;
        }
    }
}
