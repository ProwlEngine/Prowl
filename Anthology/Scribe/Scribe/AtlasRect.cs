namespace Prowl.Scribe
{
    /// <summary>
    /// Represents a rectangular area, typically in a texture atlas.
    /// </summary>
    public struct AtlasRect
    {
        public int X;
        public int Y;
        public int Width;
        public int Height;

        public AtlasRect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }
}
