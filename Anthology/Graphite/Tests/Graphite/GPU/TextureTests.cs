using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

public abstract partial class TextureTestBase<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Theory]
    [InlineData(PixelFormat.BC1_Rgba_UNorm)]
    [InlineData(PixelFormat.ASTC_12x10_UNorm)]
    public void Compressed_MipChain_RoundTrips_Through_Partial_Blocks(PixelFormat format)
    {
        if (!GD.GetPixelFormatSupport(format, TextureType.Texture2D, TextureUsage.Sampled))
        {
            return;
        }

        // Odd sizes leave partial blocks at every level and mips smaller than one block.
        const uint width = 37, height = 21, mips = 6;
        Texture texture = RF.CreateTexture(TextureDescription.Texture2D(width, height, mips, 1, format, TextureUsage.Sampled));

        byte[][] levels = new byte[mips][];
        for (uint mip = 0; mip < mips; mip++)
        {
            uint w = Math.Max(1, width >> (int)mip), h = Math.Max(1, height >> (int)mip);
            uint size = FormatHelpers.GetRegionSize(w, h, 1, format);
            levels[mip] = Enumerable.Range(0, (int)size).Select(i => (byte)((i * 7) + mip)).ToArray();
            GD.UpdateTexture(texture, levels[mip], new TextureRegion(0, 0, 0, w, h, 1, mip, 0));
        }

        GD.WaitForIdle();

        for (uint mip = 0; mip < mips; mip++)
        {
            uint w = Math.Max(1, width >> (int)mip), h = Math.Max(1, height >> (int)mip);
            byte[] back = ReadTextureBytes(texture, new TextureRegion(0, 0, 0, w, h, 1, mip, 0), (uint)levels[mip].Length);
            Assert.Equal(levels[mip], back[..levels[mip].Length]);
        }

        if (format == PixelFormat.BC1_Rgba_UNorm)
        {
            Texture tiny = RF.CreateTexture(TextureDescription.Texture2D(2, 2, 1, 1, format, TextureUsage.Sampled));
            GD.UpdateTexture(tiny, new byte[16], new TextureRegion(0, 0, 0, 4, 4, 1));
        }
    }

    [Fact]
    public unsafe void Copy_WithOffsets_2D()
    {
        Texture src = RF.CreateTexture(TextureDescription.Texture2D(
            100, 100, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

        Texture dst = RF.CreateTexture(TextureDescription.Texture2D(
            100, 100, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

        Color32[] srcData = new Color32[src.Height * src.Width];
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
            {
                srcData[y * src.Width + x] = new Color32((byte)x, (byte)y, 0, 1);
            }

        GD.UpdateTexture(src, srcData, new TextureRegion(0, 0, 0, src.Width, src.Height, 1));

        GD.RunTestGraph((context, cl) =>
        {
            cl.CopyTexture(
                src,
                50, 50, 0, 0, 0,
                dst,
                10, 10, 0, 0, 0,
                50, 50, 1, 1);
        });
        GD.WaitForIdle();

        TexelData<Color32> readView = ReadTexture<Color32>(dst);
        for (int y = 10; y < 60; y++)
            for (int x = 10; x < 60; x++)
            {
                Assert.Equal(new Color32((byte)(x + 40), (byte)(y + 40), 0, 1), readView[x, y]);
            }

        Texture updated = RF.CreateTexture(TextureDescription.Texture2D(
            100, 100, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

        Color32[] updateData = new Color32[50 * 30];
        for (uint y = 0; y < 30; y++)
            for (uint x = 0; x < 50; x++)
            {
                updateData[y * 50 + x] = new Color32((byte)x, (byte)y, 0, 1);
            }

        fixed (Color32* dataPtr = &updateData[0])
        {
            GD.UpdateTexture(updated, (IntPtr)dataPtr, (uint)(updateData.Length * sizeof(Color32)), new TextureRegion(50, 70, 0, 50, 30, 1));
        }

        TexelData<Color32> updatedView = ReadTexture<Color32>(updated);
        for (int y = 0; y < 30; y++)
            for (int x = 0; x < 50; x++)
            {
                Assert.Equal(new Color32((byte)x, (byte)y, 0, 1), updatedView[x + 50, y + 70]);
            }
    }

    // Two component formats must not map to four component Vulkan formats, which a round trip alone would hide
    [Theory]
    [InlineData(PixelFormat.R32_G32_Float)]
    [InlineData(PixelFormat.R16_G16_Float)]
    public void ClearColorTarget_TwoComponentFloats_OnlyWriteTwoComponents(PixelFormat format)
    {
        const uint width = 4;
        const uint height = 1;

        Texture target = RF.CreateTexture(TextureDescription.Texture2D(width, height, 1, 1, format, TextureUsage.RenderTarget));
        Framebuffer fb = RF.CreateFramebuffer(new FramebufferDescription(null, target));

        GD.RunTestGraph((context, cl) =>
        {
            cl.SetFramebuffer(fb, new TargetLoadStoreOps(AttachmentOps.Clear(new Color(3f, 5f, 999f, 7777f)), AttachmentOps.Loaded));
        });
        GD.WaitForIdle();

        // 999 and 7777 stand in for the B and A channels, which the target must ignore
        if (format == PixelFormat.R32_G32_Float)
        {
            TexelData<Float2> view = ReadTexture<Float2>(target);
            for (int x = 0; x < width; x++)
                Assert.Equal(new Float2(3f, 5f), view[x, 0]);
        }
        else
        {
            TexelData<HalfFloat2> view = ReadTexture<HalfFloat2>(target);
            for (int x = 0; x < width; x++)
            {
                Assert.Equal((Half)3f, view[x, 0].X);
                Assert.Equal((Half)5f, view[x, 0].Y);
            }
        }

        fb.Dispose();
        target.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct HalfFloat2
    {
        public Half X;
        public Half Y;
    }

    [Theory]
    [MemberData(nameof(FormatCoverageData))]
    public unsafe void FormatCoverage_CopyThenRead(
        PixelFormat format, int rBits, int gBits, int bBits, int aBits,
        TextureType srcType,
        uint srcWidth, uint srcHeight, uint srcDepth, uint srcMipLevels, uint srcArrayLayers,
        TextureType dstType,
        uint dstWidth, uint dstHeight, uint dstDepth, uint dstMipLevels, uint dstArrayLayers,
        uint copyWidth, uint copyHeight, uint copyDepth,
        uint srcX, uint srcY, uint srcZ,
        uint srcMipLevel, uint srcArrayLayer,
        uint dstX, uint dstY, uint dstZ,
        uint dstMipLevel, uint dstArrayLayer)
    {
        if (!GD.GetPixelFormatSupport(format, srcType, TextureUsage.Sampled))
        {
            return;
        }

        Texture srcTex = RF.CreateTexture(new TextureDescription(
            srcWidth, srcHeight, srcDepth, srcMipLevels, srcArrayLayers,
            format, TextureUsage.Sampled, srcType));

        TextureDataReaderWriter tdrw = new(rBits, gBits, bBits, aBits);
        byte[] dataArray = tdrw.GetDataArray(srcWidth, srcHeight, srcDepth);
        long rowPitch = srcWidth * tdrw.PixelBytes;
        long depthPitch = rowPitch * srcHeight;
        fixed (byte* dataPtr = dataArray)
        {
            for (uint z = 0; z < srcDepth; z++)
            {
                for (uint y = 0; y < srcHeight; y++)
                {
                    for (uint x = 0; x < srcWidth; x++)
                    {
                        long offset = z * depthPitch + y * rowPitch + x * tdrw.PixelBytes;
                        WidePixel pixel = tdrw.GetTestPixel(x, y, z);
                        tdrw.WritePixel(dataPtr + offset, pixel);
                    }
                }
            }

            GD.UpdateTexture(srcTex, (IntPtr)dataPtr, (uint)dataArray.Length, new TextureRegion(0, 0, 0, srcWidth, srcHeight, srcDepth));
        }

        Texture dstTex = RF.CreateTexture(new TextureDescription(
            dstWidth, dstHeight, dstDepth, dstMipLevels, dstArrayLayers,
            format, TextureUsage.Sampled, dstType));

        GD.RunTestGraph((context, cl) =>
        {

            cl.CopyTexture(
                srcTex, srcX, srcY, srcZ, srcMipLevel, srcArrayLayer,
                dstTex, dstX, dstY, dstZ, dstMipLevel, dstArrayLayer,
                copyWidth, copyHeight, copyDepth, 1);

        });
        GD.WaitForIdle();

        byte[] map = ReadTextureBytes(
            dstTex,
            new TextureRegion(dstX, dstY, dstZ, copyWidth, copyHeight, copyDepth, dstMipLevel, dstArrayLayer),
            copyWidth * copyHeight * copyDepth * (uint)tdrw.PixelBytes);
        fixed (byte* mapPtr = map)
        {
            for (uint z = 0; z < copyDepth; z++)
            {
                for (uint y = 0; y < copyHeight; y++)
                {
                    for (uint x = 0; x < copyWidth; x++)
                    {
                        long offset = (z * copyHeight + y) * copyWidth * tdrw.PixelBytes + x * tdrw.PixelBytes;
                        WidePixel expected = tdrw.GetTestPixel(x, y, z);
                        WidePixel actual = tdrw.ReadPixel(mapPtr + offset);
                        Assert.Equal(expected, actual);
                    }
                }
            }
        }
    }

    public static IEnumerable<object[]> FormatCoverageData()
    {
        foreach (FormatProps props in s_allFormatProps)
        {
            yield return new object[]
            {
                props.Format, props.RedBits, props.GreenBits, props.BlueBits, props.AlphaBits,
                TextureType.Texture2D,
                64, 64, 1, 1, 1,
                TextureType.Texture2D,
                64, 64, 1, 1, 1,
                64, 64, 1,
                0, 0, 0,
                0, 0,
                0, 0, 0,
                0, 0
            };
        }
    }

    private static readonly FormatProps[] s_allFormatProps =
    [
        new FormatProps(PixelFormat.R8_UNorm, 8, 0, 0, 0),
        new FormatProps(PixelFormat.R8_SNorm, 8, 0, 0, 0),
        new FormatProps(PixelFormat.R8_UInt, 8, 0, 0, 0),
        new FormatProps(PixelFormat.R8_SInt, 8, 0, 0, 0),

        new FormatProps(PixelFormat.R16_UNorm, 16, 0, 0, 0),
        new FormatProps(PixelFormat.R16_SNorm, 16, 0, 0, 0),
        new FormatProps(PixelFormat.R16_UInt, 16, 0, 0, 0),
        new FormatProps(PixelFormat.R16_SInt, 16, 0, 0, 0),
        new FormatProps(PixelFormat.R16_Float, 16, 0, 0, 0),

        new FormatProps(PixelFormat.R32_UInt, 32, 0, 0, 0),
        new FormatProps(PixelFormat.R32_SInt, 32, 0, 0, 0),
        new FormatProps(PixelFormat.R32_Float, 32, 0, 0, 0),

        new FormatProps(PixelFormat.R8_G8_UNorm, 8, 8, 0, 0),
        new FormatProps(PixelFormat.R8_G8_SNorm, 8, 8, 0, 0),
        new FormatProps(PixelFormat.R8_G8_UInt, 8, 8, 0, 0),
        new FormatProps(PixelFormat.R8_G8_SInt, 8, 8, 0, 0),

        new FormatProps(PixelFormat.R16_G16_UNorm, 16, 16, 0, 0),
        new FormatProps(PixelFormat.R16_G16_SNorm, 16, 16, 0, 0),
        new FormatProps(PixelFormat.R16_G16_UInt, 16, 16, 0, 0),
        new FormatProps(PixelFormat.R16_G16_SInt, 16, 16, 0, 0),
        new FormatProps(PixelFormat.R16_G16_Float, 16, 16, 0, 0),

        new FormatProps(PixelFormat.R32_G32_UInt, 32, 32, 0, 0),
        new FormatProps(PixelFormat.R32_G32_SInt, 32, 32, 0, 0),
        new FormatProps(PixelFormat.R32_G32_Float, 32, 32, 0, 0),

        new FormatProps(PixelFormat.B8_G8_R8_A8_UNorm, 8, 8, 8, 8),
        new FormatProps(PixelFormat.R8_G8_B8_A8_UNorm, 8, 8, 8, 8),
        new FormatProps(PixelFormat.R8_G8_B8_A8_SNorm, 8, 8, 8, 8),
        new FormatProps(PixelFormat.R8_G8_B8_A8_UInt, 8, 8, 8, 8),
        new FormatProps(PixelFormat.R8_G8_B8_A8_SInt, 8, 8, 8, 8),

        new FormatProps(PixelFormat.R16_G16_B16_A16_UNorm, 16, 16, 16, 16),
        new FormatProps(PixelFormat.R16_G16_B16_A16_SNorm, 16, 16, 16, 16),
        new FormatProps(PixelFormat.R16_G16_B16_A16_UInt, 16, 16, 16, 16),
        new FormatProps(PixelFormat.R16_G16_B16_A16_SInt, 16, 16, 16, 16),
        new FormatProps(PixelFormat.R16_G16_B16_A16_Float, 16, 16, 16, 16),

        new FormatProps(PixelFormat.R32_G32_B32_A32_UInt, 32, 32, 32, 32),
        new FormatProps(PixelFormat.R32_G32_B32_A32_SInt, 32, 32, 32, 32),
        new FormatProps(PixelFormat.R32_G32_B32_A32_Float, 32, 32, 32, 32),

        new FormatProps(PixelFormat.R10_G10_B10_A2_UInt, 10, 10, 10, 2),
        new FormatProps(PixelFormat.R10_G10_B10_A2_UNorm, 10, 10, 10, 2),
        new FormatProps(PixelFormat.R11_G11_B10_Float, 11, 11, 10, 0)
    ];

    readonly struct FormatProps
    {
        public readonly PixelFormat Format;
        public readonly int RedBits;
        public readonly int BlueBits;
        public readonly int GreenBits;
        public readonly int AlphaBits;

        public FormatProps(PixelFormat format, int redBits, int blueBits, int greenBits, int alphaBits)
        {
            Format = format;
            RedBits = redBits;
            BlueBits = blueBits;
            GreenBits = greenBits;
            AlphaBits = alphaBits;
        }
    }
}

#if TEST_VULKAN
[Trait("Backend", "Vulkan")]
[Collection("GPU Tests")]
public class VulkanTextureTests : TextureTestBase<VulkanDeviceCreator> { }
#endif
