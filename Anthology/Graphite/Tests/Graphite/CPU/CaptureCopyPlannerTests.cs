using System.Linq;

using Prowl.Graphite.Debugging;

using Xunit;

namespace Prowl.Graphite.Tests;

public class CaptureCopyPlannerTests
{
    [Theory]
    [InlineData(PixelFormat.D16_UNorm, 2, false)]
    [InlineData(PixelFormat.D32_Float, 4, false)]
    [InlineData(PixelFormat.D32_Float_S8_UInt, 4, true)]
    public void PlanTexture_DepthPlanesMatchTheFormat(PixelFormat format, uint bytesPerTexel, bool stencil)
    {
        TextureDescription description = TextureDescription.Texture2D(16, 16, 1, 1, format, TextureUsage.DepthStencil);

        CopyRegion[] regions = CaptureCopyPlanner.PlanTexture(description, out uint size);

        Assert.Equal(stencil, regions.Any(r => r.Format == PixelFormat.R8_UInt));
        Assert.Equal(16u * 16 * bytesPerTexel + (stencil ? 16u * 16 : 0), size);
    }
}
