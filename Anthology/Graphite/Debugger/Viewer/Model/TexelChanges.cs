namespace Prowl.Graphite.Debugger.Viewer;

/// <summary>Texels one event changed, as a bitmask over the rectangle that bounds them.</summary>
internal sealed class TexelChanges(int x, int y, int width, int height)
{
    public static readonly TexelChanges None = new(0, 0, 0, 0);

    private readonly ulong[] _bits = new ulong[((long)width * height + 63) / 64];

    public void Add(int px, int py)
    {
        int i = (py - y) * width + (px - x);
        _bits[i >> 6] |= 1ul << (i & 63);
    }

    public bool Contains(int px, int py)
    {
        int lx = px - x, ly = py - y;
        if (lx < 0 || ly < 0 || lx >= width || ly >= height)
            return false;
        int i = ly * width + lx;
        return (_bits[i >> 6] & (1ul << (i & 63))) != 0;
    }
}
