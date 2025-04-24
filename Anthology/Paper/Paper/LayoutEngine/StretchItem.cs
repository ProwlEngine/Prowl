namespace Prowl.PaperUI.LayoutEngine
{
    internal class StretchItem(int index, double factor, StretchItem.ItemTypes itemType, double min, double max)
    {
        public enum ItemTypes { Before, Size, After }

        public int Index { get; set; } = index;
        public double Factor { get; set; } = factor;
        public ItemTypes ItemType { get; set; } = itemType;
        public double Violation { get; set; } = 0f;
        public double Computed { get; set; } = 0f;
        public bool Frozen { get; set; } = false;
        public double Min { get; set; } = min;
        public double Max { get; set; } = max;
    }
}
