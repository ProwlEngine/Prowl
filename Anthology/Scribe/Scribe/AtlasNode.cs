namespace Prowl.Scribe
{
    /// <summary>
    /// Represents a node in the font atlas, typically a free rectangular space.
    /// For a skyline bottom-left bin packer, this might store skyline segments.
    /// </summary>
    internal struct AtlasNode
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public AtlasNode(int x, int y, int width, int height = 0)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }
}
