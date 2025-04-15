namespace Prowl.PaperUI.LayoutEngine
{
    internal class StretchItem(int index, float factor, StretchItem.ItemTypes itemType, float min, float max)
    {
        public enum ItemTypes { Before, Size, After }

        public int Index { get; set; } = index;
        public float Factor { get; set; } = factor;
        public ItemTypes ItemType { get; set; } = itemType;
        public float Violation { get; set; } = 0f;
        public float Computed { get; set; } = 0f;
        public bool Frozen { get; set; } = false;
        public float Min { get; set; } = min;
        public float Max { get; set; } = max;
    }
}
