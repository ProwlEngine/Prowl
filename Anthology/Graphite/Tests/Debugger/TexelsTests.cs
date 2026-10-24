using System;
using Prowl.Graphite.Debugger.Viewer;
using Prowl.Graphite.Debugging;
using Xunit;

namespace Prowl.Graphite.Debugger.Tests;

public class TexelsTests
{
    private const uint Size = 4096;
    private static readonly CopyRegion Region = new(0, 0, 0, Size, Size, 1, PixelFormat.D16_UNorm);

    [Fact]
    public void Changed_DownsampledCatchesOneTexelOffTheSampleGrid()
    {
        (byte[] before, byte[] after, int x, int y) = OneChangedTexel();
        int step = Texels.PreviewStep(Region);

        bool[] changed = Texels.Changed(before, after, Region, step);

        (int width, _) = Texels.PreviewSize(Region, step);
        Assert.True(step > 1);
        Assert.True(changed[y / step * width + x / step]);
        Assert.Equal(1, Array.FindAll(changed, c => c).Length);
    }

    [Fact]
    public void Diff_FindsExactlyTheChangedTexelInTheBottomHalfOfD16()
    {
        (byte[] before, byte[] after, int x, int y) = OneChangedTexel();

        TexelChanges changes = Texels.Diff(before, after, Region);

        Assert.True(changes.Contains(x, y));
        Assert.False(changes.Contains(x + 1, y));
        Assert.False(changes.Contains(x - 1, y));
    }

    private static (byte[], byte[], int, int) OneChangedTexel()
    {
        byte[] before = new byte[Size * Size * 2];
        byte[] after = (byte[])before.Clone();
        int x = 7, y = (int)Size - 5;
        after[(y * Size + x) * 2 + 1] = 1;
        return (before, after, x, y);
    }
}
