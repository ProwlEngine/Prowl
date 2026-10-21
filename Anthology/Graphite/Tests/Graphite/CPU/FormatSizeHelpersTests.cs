using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Xunit;

namespace Prowl.Graphite.Tests;

public class FormatSizeHelpersTests : IDisposable
{
    private TraceListener[] _traceListeners;

    public FormatSizeHelpersTests()
    {
        // temporarily disables debug trace listeners to prevent Debug.Assert
        // from causing test failures in cases where we're explicitly trying
        // to test invalid inputs
        _traceListeners = new TraceListener[Trace.Listeners.Count];
        Trace.Listeners.CopyTo(_traceListeners, 0);
        Trace.Listeners.Clear();
    }

    public void Dispose()
    {
        Trace.Listeners.AddRange(_traceListeners);
    }

    [Fact]
    public void GetSizeInBytes_DefinedForAllVertexElementFormats()
    {
        foreach (VertexElementFormat format in System.Enum.GetValues(typeof(VertexElementFormat)))
        {
            Assert.True(0 < format.GetSizeInBytes());
        }
    }

    private static HashSet<PixelFormat> CompressedPixelFormats = [
        .. System.Enum.GetValues<PixelFormat>().Where(format => format.ToString().StartsWith("ASTC_")),

        PixelFormat.BC1_Rgba_UNorm,
        PixelFormat.BC1_Rgba_UNorm_SRgb,
        PixelFormat.BC1_Rgb_UNorm,
        PixelFormat.BC1_Rgb_UNorm_SRgb,

        PixelFormat.BC2_UNorm,
        PixelFormat.BC2_UNorm_SRgb,

        PixelFormat.BC3_UNorm,
        PixelFormat.BC3_UNorm_SRgb,

        PixelFormat.BC4_SNorm,
        PixelFormat.BC4_UNorm,

        PixelFormat.BC5_SNorm,
        PixelFormat.BC5_UNorm,

        PixelFormat.BC7_UNorm,
        PixelFormat.BC7_UNorm_SRgb,

        PixelFormat.ETC2_R8_G8_B8_A1_UNorm,
        PixelFormat.ETC2_R8_G8_B8_A8_UNorm,
        PixelFormat.ETC2_R8_G8_B8_UNorm,
    ];
    private static IEnumerable<PixelFormat> UncompressedPixelFormats
        = System.Enum.GetValues(typeof(PixelFormat)).Cast<PixelFormat>()
            .Where(format => !CompressedPixelFormats.Contains(format));
    public static IEnumerable<object[]> CompressedPixelFormatMemberData => CompressedPixelFormats.Select(format => new object[] { format });
    public static IEnumerable<object[]> UncompressedPixelFormatMemberData => UncompressedPixelFormats.Select(format => new object[] { format });

    [Theory]
    [MemberData(nameof(UncompressedPixelFormatMemberData))]
    public void GetSizeInBytes_DefinedForAllNonCompressedPixelFormats(PixelFormat format)
    {
        Assert.True(0 < format.GetSizeInBytes());
    }

    [Theory]
    [MemberData(nameof(CompressedPixelFormatMemberData))]
    public void GetSizeInBytes_ThrowsForAllCompressedPixelFormats(PixelFormat format)
    {
        Assert.ThrowsAny<RenderException>(() => format.GetSizeInBytes());
    }

    [Theory]
    [InlineData(PixelFormat.BC1_Rgba_UNorm, 2u, 1u, 8u)]
    [InlineData(PixelFormat.BC1_Rgba_UNorm, 13u, 7u, 4u * 2u * 8u)]
    [InlineData(PixelFormat.BC7_UNorm, 16u, 16u, 16u * 16u)]
    [InlineData(PixelFormat.ASTC_4x4_UNorm, 1u, 1u, 16u)]
    [InlineData(PixelFormat.ASTC_6x6_UNorm_SRgb, 13u, 7u, 3u * 2u * 16u)]
    [InlineData(PixelFormat.ASTC_12x10_UNorm, 25u, 21u, 3u * 3u * 16u)]
    public void GetRegionSize_CountsPartialEdgeBlocks(PixelFormat format, uint width, uint height, uint expected)
    {
        Assert.Equal(expected, FormatHelpers.GetRegionSize(width, height, 1, format));
    }

    [Fact]
    public void EveryAstcFormatHasBlockDimensionsAndAnSrgbPartner()
    {
        foreach (PixelFormat format in System.Enum.GetValues<PixelFormat>().Where(format => format.ToString().StartsWith("ASTC_")))
        {
            string[] size = format.ToString().Split('_')[1].Split('x');
            Assert.Equal((uint.Parse(size[0]), uint.Parse(size[1])), FormatHelpers.GetBlockDimensions(format));
            Assert.Equal(16u, FormatHelpers.GetBlockSizeInBytes(format));

            PixelFormat partner = format.ToString().EndsWith("_SRgb")
                ? System.Enum.Parse<PixelFormat>(format.ToString()[..^5])
                : System.Enum.Parse<PixelFormat>(format + "_SRgb");
            Assert.True(FormatHelpers.IsFormatViewCompatible(partner, format));
            Assert.Equal(FormatHelpers.GetViewFamilyFormat(format), FormatHelpers.GetViewFamilyFormat(partner));
        }
    }
}
