namespace Prowl.PaperUI.LayoutEngine
{
    internal class StretchItem
    {
        public enum ItemTypes { Before, Size, After }

        public int Index { get; set; }
        public float Factor { get; set; }
        public ItemTypes ItemType { get; set; }
        public float Violation { get; set; } = 0f;
        public float Computed { get; set; } = 0f;
        public bool Frozen { get; set; } = false;
        public float Min { get; set; }
        public float Max { get; set; }

        public StretchItem(int index, float factor, ItemTypes itemType, float min, float max)
        {
            Index = index;
            Factor = factor;
            ItemType = itemType;
            Min = min;
            Max = max;
        }
    }
}
