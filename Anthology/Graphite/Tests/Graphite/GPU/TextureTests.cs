using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using Prowl.Vector;

using Xunit;

namespace Prowl.Graphite.Tests;

public abstract partial class TextureTestBase<T> : GraphicsDeviceTestBase<T> where T : GraphicsDeviceCreator
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public unsafe void Update_ThenRead_Succeeds_R32Float(bool useArrayOverload)
    {
        Texture texture = RF.CreateTexture(
            TextureDescription.Texture2D(1024, 1024, 1, 1, PixelFormat.R32_Float, TextureUsage.Sampled));

        float[] data = Enumerable.Range(0, 1024 * 1024).Select(i => (float)i).ToArray();

        fixed (float* dataPtr = data)
        {
            if (useArrayOverload)
            {
                GD.UpdateTexture(texture, data, new TextureRegion(0, 0, 0, 1024, 1024, 1));
            }
            else
            {
                GD.UpdateTexture(texture, (IntPtr)dataPtr, 1024 * 1024 * 4, new TextureRegion(0, 0, 0, 1024, 1024, 1));
            }
        }

        TexelData<float> map = ReadTexture<float>(texture);
        for (int y = 0; y < 1024; y++)
        {
            for (int x = 0; x < 1024; x++)
            {
                int index = y * 1024 + x;
                Assert.Equal(index, map[x, y]);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public unsafe void Update_ThenRead_SingleMip_Succeeds_R16UNorm(bool useArrayOverload)
    {
        Texture texture = RF.CreateTexture(
            TextureDescription.Texture2D(1024, 1024, 3, 1, PixelFormat.R16_UNorm, TextureUsage.Sampled));

        ushort[] data = Enumerable.Range(0, 256 * 256).Select(i => (ushort)i).ToArray();

        fixed (ushort* dataPtr = data)
        {
            if (useArrayOverload)
            {
                GD.UpdateTexture(texture, data, new TextureRegion(0, 0, 0, 256, 256, 1, 2, 0));
            }
            else
            {
                GD.UpdateTexture(texture, (IntPtr)dataPtr, 256 * 256 * sizeof(ushort), new TextureRegion(0, 0, 0, 256, 256, 1, 2, 0));
            }
        }

        TexelData<ushort> map = ReadTexture<ushort>(texture, 2);
        for (int y = 0; y < 256; y++)
        {
            for (int x = 0; x < 256; x++)
            {
                Assert.Equal((ushort)(y * 256 + x), map[x, y]);
            }
        }
    }

    [Fact]
    public unsafe void Update_ThenCopySingleMip_Succeeds_R16UNorm()
    {
        TextureDescription desc = TextureDescription.Texture2D(
            1024, 1024, 3, 1, PixelFormat.R16_UNorm, TextureUsage.Sampled);
        Texture src = RF.CreateTexture(desc);
        Texture dst = RF.CreateTexture(desc);

        ushort[] data = Enumerable.Range(0, 256 * 256).Select(i => (ushort)i).ToArray();

        fixed (ushort* dataPtr = data)
        {
            GD.UpdateTexture(src, (IntPtr)dataPtr, 256 * 256 * sizeof(ushort), new TextureRegion(0, 0, 0, 256, 256, 1, 2, 0));
        }

        GD.RunTestGraph((context, cl) =>
        {
            cl.CopyTexture(src, dst, 2, 0);
        });
        GD.WaitForIdle();

        TexelData<ushort> map = ReadTexture<ushort>(dst, 2);
        for (int y = 0; y < 256; y++)
        {
            for (int x = 0; x < 256; x++)
            {
                Assert.Equal((ushort)(y * 256 + x), map[x, y]);
            }
        }
    }


    [Fact]
    public void CreateTextureViewFromTextureWithArrayLayers()
    {
        const uint TexSize = 4;
        const uint MipLevels = 1;
        const uint ArrayLayers = 6;

        TextureDescription texDesc = TextureDescription.Texture2D(
            TexSize, TexSize, MipLevels, ArrayLayers, PixelFormat.R8_UNorm, TextureUsage.Storage | TextureUsage.Sampled);
        Texture tex = RF.CreateTexture(texDesc);

        for (uint mip = 0; mip < MipLevels; mip++)
        {
            for (uint layer = 0; layer < ArrayLayers; layer++)
            {
                uint mipSize = TexSize >> (int)mip;
                byte[] data = Enumerable.Repeat((layer + 1) * 42, (int)(mipSize * mipSize)).Select(n => (byte)n).ToArray();
                GD.UpdateTexture(tex, data, new TextureRegion(0, 0, 0, mipSize, mipSize, 1, mip, layer));
            }
        }

        TextureView textureView = RF.CreateTextureView(tex);
        Assert.NotNull(textureView);
    }

    [Fact]
    public void CubeMap_UpdateAndRead()
    {
        const uint TexSize = 4;
        const uint MipLevels = 3;

        TextureDescription texDesc = TextureDescription.CreateCube(
            TexSize, MipLevels, 1, PixelFormat.R8_UNorm, 0);
        Texture tex = RF.CreateTexture(texDesc);

        for (uint mip = 0; mip < MipLevels; mip++)
        {
            for (uint face = 0; face < 6; face++)
            {
                uint mipSize = TexSize >> (int)mip;
                byte[] data = Enumerable.Repeat((face + 1) * 42, (int)(mipSize * mipSize)).Select(n => (byte)n).ToArray();
                GD.UpdateTexture(tex, data, new TextureRegion(0, 0, 0, mipSize, mipSize, 1, mip, face));
            }
        }

        foreach (int mip in Enumerable.Range(0, (int)MipLevels))
        {
            foreach (int face in Enumerable.Range(0, 6))
            {
                uint mipSize = TexSize >> mip;
                byte expectedColor = (byte)((face + 1) * 42);
                TexelData<byte> map = ReadTexture<byte>(tex, (uint)mip, (uint)face);

                foreach (int x in Enumerable.Range(0, (int)mipSize))
                {
                    foreach (int y in Enumerable.Range(0, (int)mipSize))
                    {
                        Assert.Equal(expectedColor, map[x, y]);
                    }
                }

            }
        }
    }

    [Fact]
    public void CubeMap_CreateViewWithSingleMipLevel()
    {
        const uint TexSize = 4;
        const uint MipLevels = 3;

        TextureDescription texDesc = TextureDescription.CreateCube(
            TexSize, MipLevels, 1, PixelFormat.R8_UNorm, TextureUsage.Sampled);
        Texture tex = RF.CreateTexture(texDesc);

        for (uint mip = 0; mip < MipLevels; mip++)
        {
            for (uint face = 0; face < 6; face++)
            {
                uint mipSize = TexSize >> (int)mip;
                byte[] data = Enumerable.Repeat((face + 1) * 42, (int)(mipSize * mipSize)).Select(n => (byte)n).ToArray();
                GD.UpdateTexture(tex, data, new TextureRegion(0, 0, 0, mipSize, mipSize, 1, mip, face));
            }
        }

        TextureView view = RF.CreateTextureView(new TextureViewDescription(tex, 0, 1, 0, 1));
        Assert.NotNull(view);
    }

    [Fact]
    public void CubeMap_Copy_OneMip()
    {
        const uint TexSize = 64;
        const uint MipLevels = 1;

        TextureDescription srcDesc = TextureDescription.CreateCube(
            TexSize, MipLevels, 1, PixelFormat.R8_UNorm, 0);
        TextureDescription dstDesc = TextureDescription.Texture2D(
            TexSize, TexSize, MipLevels, 6, PixelFormat.R8_UNorm, TextureUsage.Sampled);
        Texture src = RF.CreateTexture(srcDesc);
        Texture dst = RF.CreateTexture(dstDesc);

        for (uint face = 0; face < 6; face++)
        {
            byte[] data = Enumerable.Repeat((face + 1) * 42, (int)(TexSize * TexSize)).Select(n => (byte)n).ToArray();
            GD.UpdateTexture(src, data, new TextureRegion(0, 0, 0, TexSize, TexSize, 1, 0, face));
        }

        GD.RunTestGraph((context, cl) =>
        {
            cl.CopyTexture(src, dst);
        });
        GD.WaitForIdle();

        foreach (int mip in Enumerable.Range(0, (int)MipLevels))
        {
            foreach (int face in Enumerable.Range(0, 6))
            {
                uint mipSize = (uint)(TexSize / (1 << mip));
                byte expectedColor = (byte)((face + 1) * 42);
                TexelData<byte> map = ReadTexture<byte>(dst, (uint)mip, (uint)face);

                foreach (int x in Enumerable.Range(0, (int)mipSize))
                {
                    foreach (int y in Enumerable.Range(0, (int)mipSize))
                    {
                        Assert.Equal(expectedColor, map[x, y]);
                    }
                }

            }
        }
    }

    [Fact]
    public void CubeMap_Copy_FromNonCubeMapWith6ArrayLayers()
    {
        const uint TexSize = 64;
        const uint MipLevels = 1;

        TextureDescription srcDesc = TextureDescription.Texture2D(
            TexSize, TexSize, MipLevels, 6, PixelFormat.R8_UNorm, TextureUsage.Sampled);
        TextureDescription dstDesc = TextureDescription.CreateCube(
            TexSize, MipLevels, 1, PixelFormat.R8_UNorm, TextureUsage.Sampled);
        Texture src = RF.CreateTexture(srcDesc);
        Texture dst = RF.CreateTexture(dstDesc);

        for (uint face = 0; face < 6; face++)
        {
            byte[] data = Enumerable.Repeat((face + 1) * 42, (int)(TexSize * TexSize)).Select(n => (byte)n).ToArray();
            GD.UpdateTexture(src, data, new TextureRegion(0, 0, 0, TexSize, TexSize, 1, 0, face));
        }

        GD.RunTestGraph((context, cl) =>
        {
            for (uint face = 0; face < 6; face++)
                cl.CopyTexture(src, dst, 0, face);
        });
        GD.WaitForIdle();

        foreach (int mip in Enumerable.Range(0, (int)MipLevels))
        {
            foreach (int face in Enumerable.Range(0, 6))
            {
                uint mipSize = (uint)(TexSize / (1 << mip));
                byte expectedColor = (byte)((face + 1) * 42);
                TexelData<byte> map = ReadTexture<byte>(dst, (uint)mip, (uint)face);

                foreach (int x in Enumerable.Range(0, (int)mipSize))
                {
                    foreach (int y in Enumerable.Range(0, (int)mipSize))
                    {
                        Assert.Equal(expectedColor, map[x, y]);
                    }
                }

            }
        }
    }

    [Fact]
    public void CubeMap_Copy_MultipleMip_CopySingleMipFaces()
    {
        const uint TexSize = 64;
        const uint MipLevels = 3;
        const uint CopiedMip = 1;

        TextureDescription srcDesc = TextureDescription.CreateCube(
            TexSize, MipLevels, 1, PixelFormat.R8_UNorm, 0);
        TextureDescription dstDesc = TextureDescription.Texture2D(
            TexSize, TexSize, MipLevels, 6, PixelFormat.R8_UNorm, TextureUsage.Sampled);
        Texture src = RF.CreateTexture(srcDesc);
        Texture dst = RF.CreateTexture(dstDesc);

        for (uint mip = 0; mip < MipLevels; mip++)
        {
            uint mipSize = (uint)(TexSize / (1 << (int)mip));
            for (uint face = 0; face < 6; face++)
            {
                byte[] data = Enumerable.Repeat((face + 1) * 42, (int)(mipSize * mipSize)).Select(n => (byte)n).ToArray();
                GD.UpdateTexture(src, data, new TextureRegion(0, 0, 0, mipSize, mipSize, 1, mip, face));
            }
        }

        GD.RunTestGraph((context, cl) =>
        {
            for (uint face = 0; face < 6; face++)
                cl.CopyTexture(src, dst, CopiedMip, face);
        });
        GD.WaitForIdle();

        for (uint mip = 0; mip < MipLevels; mip++)
        {
            for (uint face = 0; face < 6; face++)
            {
                uint mipSize = (uint)(TexSize / (1 << (int)mip));
                byte expectedColor = mip == CopiedMip ? (byte)((face + 1) * 42) : (byte)0;
                TexelData<byte> map = ReadTexture<byte>(dst, mip, face);
                for (int y = 0; y < mipSize; y++)
                    for (int x = 0; x < mipSize; x++)
                    {
                        Assert.Equal(expectedColor, map[x, y]);
                    }
            }
        }
    }

    [Fact]
    public void CubeMap_Copy_MultipleMip_AllAtOnce()
    {
        const uint TexSize = 64;
        const uint MipLevels = 2;

        TextureDescription srcDesc = TextureDescription.CreateCube(
            TexSize, MipLevels, 1, PixelFormat.R8_UNorm, 0);
        TextureDescription dstDesc = TextureDescription.Texture2D(
            TexSize, TexSize, MipLevels, 6, PixelFormat.R8_UNorm, TextureUsage.Sampled);
        Texture src = RF.CreateTexture(srcDesc);
        Texture dst = RF.CreateTexture(dstDesc);

        for (uint mip = 0; mip < MipLevels; mip++)
        {
            uint mipSize = (uint)(TexSize / (1 << (int)mip));
            for (uint face = 0; face < 6; face++)
            {
                byte[] data = Enumerable.Repeat((face + 1) * 42, (int)(mipSize * mipSize)).Select(n => (byte)n).ToArray();
                GD.UpdateTexture(src, data, new TextureRegion(0, 0, 0, mipSize, mipSize, 1, mip, face));
            }
        }

        GD.RunTestGraph((context, cl) =>
        {
            cl.CopyTexture(src, dst);
        });
        GD.WaitForIdle();

        foreach (int mip in Enumerable.Range(0, (int)MipLevels))
        {
            foreach (int face in Enumerable.Range(0, 6))
            {
                uint mipSize = (uint)(TexSize / (1 << mip));
                byte expectedColor = (byte)((face + 1) * 42);
                TexelData<byte> map = ReadTexture<byte>(dst, (uint)mip, (uint)face);

                foreach (int x in Enumerable.Range(0, (int)mipSize))
                {
                    foreach (int y in Enumerable.Range(0, (int)mipSize))
                    {
                        Assert.Equal(expectedColor, map[x, y]);
                    }
                }

            }
        }
    }

    [Fact]
    public void CubeMap_Copy_MultipleMip_SpecificArrayLayer()
    {
        const uint TexSize = 64;
        const uint MipLevels = 2;
        const uint CopiedArrayLayer = 3;

        TextureDescription srcDesc = TextureDescription.CreateCube(
            TexSize, MipLevels, 1, PixelFormat.R8_UNorm, 0);
        TextureDescription dstDesc = TextureDescription.Texture2D(
            TexSize, TexSize, MipLevels, CopiedArrayLayer + 1, PixelFormat.R8_UNorm, TextureUsage.Sampled);
        Texture src = RF.CreateTexture(srcDesc);
        Texture dst = RF.CreateTexture(dstDesc);

        for (uint mip = 0; mip < MipLevels; mip++)
        {
            uint mipSize = (uint)(TexSize / (1 << (int)mip));
            for (uint face = 0; face < 6; face++)
            {
                byte[] data = Enumerable.Repeat((face + 1) * 42, (int)(mipSize * mipSize)).Select(n => (byte)n).ToArray();
                GD.UpdateTexture(src, data, new TextureRegion(0, 0, 0, mipSize, mipSize, 1, mip, face));
            }
        }

        GD.RunTestGraph((context, cl) =>
        {
            for (uint mip = 0; mip < MipLevels; mip++)
                cl.CopyTexture(src, dst, mip, CopiedArrayLayer);
        });
        GD.WaitForIdle();

        for (uint mip = 0; mip < MipLevels; mip++)
        {
            for (uint face = 0; face <= CopiedArrayLayer; face++)
            {
                uint mipSize = (uint)(TexSize / (1 << (int)mip));
                byte expectedColor = face == CopiedArrayLayer ? (byte)((face + 1) * 42) : (byte)0;
                TexelData<byte> map = ReadTexture<byte>(dst, mip, face);
                for (int y = 0; y < mipSize; y++)
                    for (int x = 0; x < mipSize; x++)
                    {
                        Assert.Equal(expectedColor, map[x, y]);
                    }
            }
        }
    }

    [Theory]
    [InlineData(64, 7)]
    [InlineData(64, 4)]
    [InlineData(64, 2)]
    [InlineData(32, 6)]
    [InlineData(32, 4)]
    [InlineData(32, 2)]
    [InlineData(4, 3)]
    [InlineData(4, 2)]
    [InlineData(2, 2)]
    public void CubeMap_GenerateMipmaps(uint TexSize, uint MipLevels)
    {
        TextureDescription texDesc = TextureDescription.CreateCube(
            TexSize, MipLevels, 1, PixelFormat.R8_UNorm, 0);
        Texture tex = RF.CreateTexture(texDesc);

        for (uint face = 0; face < 6; face++)
        {
            byte[] data = Enumerable.Repeat((face + 1) * 42, (int)(TexSize * TexSize)).Select(n => (byte)n).ToArray();
            GD.UpdateTexture(tex, data, new TextureRegion(0, 0, 0, TexSize, TexSize, 1, 0, face));
        }

        foreach (int face in Enumerable.Range(0, 6))
        {
            uint mipSize = TexSize;
            byte expectedColor = (byte)((face + 1) * 42);
            TexelData<byte> map = ReadTexture<byte>(tex, 0, (uint)face);

            foreach (int x in Enumerable.Range(0, (int)mipSize))
            {
                foreach (int y in Enumerable.Range(0, (int)mipSize))
                {
                    Assert.Equal(expectedColor, map[x, y]);
                }
            }

        }

        GD.RunTestGraph((context, cl) =>
        {
            cl.GenerateMipmaps(tex);
        });
        GD.WaitForIdle();

        foreach (int mip in Enumerable.Range(0, (int)MipLevels))
        {
            foreach (int face in Enumerable.Range(0, 6))
            {
                uint mipSize = (uint)(TexSize / (1 << mip));
                byte expectedColor = (byte)((face + 1) * 42);
                TexelData<byte> map = ReadTexture<byte>(tex, (uint)mip, (uint)face);

                foreach (int x in Enumerable.Range(0, (int)mipSize))
                {
                    foreach (int y in Enumerable.Range(0, (int)mipSize))
                    {
                        Assert.Equal(expectedColor, map[x, y]);
                    }
                }

            }
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(32)]
    public void ArrayLayers_WriteAndRead_SmallTextures(uint TexSize)
    {
        const uint ArrayLayers = 6;
        const uint ArrayColorDelta = 255 / ArrayLayers;

        TextureDescription texDesc = TextureDescription.Texture2D(
            TexSize, TexSize, 1, ArrayLayers, PixelFormat.R8_UNorm, TextureUsage.Sampled);
        Texture tex = RF.CreateTexture(texDesc);

        for (uint layer = 0; layer < ArrayLayers; layer++)
        {
            byte[] data = Enumerable.Repeat(layer * ArrayColorDelta, (int)(TexSize * TexSize)).Select(n => (byte)n).ToArray();
            GD.UpdateTexture(tex, data, new TextureRegion(0, 0, 0, TexSize, TexSize, 1, 0, layer));
        }

        for (uint layer = 0; layer < ArrayLayers; layer++)
        {
            byte expectedColor = (byte)(layer * ArrayColorDelta);
            TexelData<byte> map = ReadTexture<byte>(tex, 0, layer);
            for (int y = 0; y < TexSize; y++)
                for (int x = 0; x < TexSize; x++)
                {
                    Assert.Equal(expectedColor, map[x, y]);
                }
        }
    }

    [Fact]
    public void ArrayLayers_WriteAndRead()
    {
        const uint TexSize = 64;
        const uint ArrayLayers = 6;
        const uint ArrayColorDelta = 255 / ArrayLayers;

        TextureDescription texDesc = TextureDescription.Texture2D(
            TexSize, TexSize, 1, ArrayLayers, PixelFormat.R8_UNorm, TextureUsage.Sampled);
        Texture tex = RF.CreateTexture(texDesc);

        for (uint layer = 0; layer < ArrayLayers; layer++)
        {
            byte[] data = Enumerable.Repeat(layer * ArrayColorDelta, (int)(TexSize * TexSize)).Select(n => (byte)n).ToArray();
            GD.UpdateTexture(tex, data, new TextureRegion(0, 0, 0, TexSize, TexSize, 1, 0, layer));
        }

        for (uint layer = 0; layer < ArrayLayers; layer++)
        {
            byte expectedColor = (byte)(layer * ArrayColorDelta);
            TexelData<byte> map = ReadTexture<byte>(tex, 0, layer);
            for (int y = 0; y < TexSize; y++)
                for (int x = 0; x < TexSize; x++)
                {
                    Assert.Equal(expectedColor, map[x, y]);
                }
        }
    }

    [Fact]
    public void ArrayLayers_WriteAndCopyAndRead()
    {
        const uint TexSize = 64;
        const uint MipLevels = 2;
        const uint ArrayLayers = 6;
        const uint ArrayColorDelta = 255 / ArrayLayers;

        TextureDescription texDesc = TextureDescription.Texture2D(
            TexSize, TexSize, MipLevels, ArrayLayers, PixelFormat.R8_UNorm, TextureUsage.Sampled);
        Texture tex = RF.CreateTexture(texDesc);
        texDesc.Usage = TextureUsage.Sampled;
        Texture readback = RF.CreateTexture(texDesc);

        for (uint mip = 0; mip < MipLevels; mip++)
        {
            for (uint layer = 0; layer < ArrayLayers; layer++)
            {
                uint mipSize = MipLevels >> (int)mip;
                byte[] data = Enumerable.Repeat(layer * ArrayColorDelta, (int)(mipSize * mipSize)).Select(n => (byte)n).ToArray();
                GD.UpdateTexture(tex, data, new TextureRegion(0, 0, 0, mipSize, mipSize, 1, mip, layer));
            }
        }

        GD.RunTestGraph((context, cl) =>
        {
            cl.CopyTexture(tex, readback);
        });
        GD.WaitForIdle();

        for (uint mip = 0; mip < MipLevels; mip++)
        {
            for (uint layer = 0; layer < ArrayLayers; layer++)
            {
                uint mipSize = MipLevels >> (int)mip;
                byte expectedColor = (byte)(layer * ArrayColorDelta);
                TexelData<byte> map = ReadTexture<byte>(readback, 0, layer);
                for (int y = 0; y < mipSize; y++)
                    for (int x = 0; x < mipSize; x++)
                    {
                        Assert.Equal(expectedColor, map[x, y]);
                    }
            }
        }
    }

    [Theory]
    [InlineData(PixelFormat.BC1_Rgb_UNorm, 8, 0, 0, 64, 64)]
    [InlineData(PixelFormat.BC1_Rgb_UNorm, 8, 8, 4, 16, 16)]
    [InlineData(PixelFormat.BC1_Rgb_UNorm_SRgb, 8, 0, 0, 64, 64)]
    [InlineData(PixelFormat.BC1_Rgb_UNorm_SRgb, 8, 8, 4, 16, 16)]
    [InlineData(PixelFormat.BC1_Rgba_UNorm, 8, 0, 0, 64, 64)]
    [InlineData(PixelFormat.BC1_Rgba_UNorm, 8, 8, 4, 16, 16)]
    [InlineData(PixelFormat.BC1_Rgba_UNorm_SRgb, 8, 0, 0, 64, 64)]
    [InlineData(PixelFormat.BC1_Rgba_UNorm_SRgb, 8, 8, 4, 16, 16)]
    [InlineData(PixelFormat.BC2_UNorm, 16, 0, 0, 64, 64)]
    [InlineData(PixelFormat.BC2_UNorm, 16, 8, 4, 16, 16)]
    [InlineData(PixelFormat.BC2_UNorm_SRgb, 16, 0, 0, 64, 64)]
    [InlineData(PixelFormat.BC2_UNorm_SRgb, 16, 8, 4, 16, 16)]
    [InlineData(PixelFormat.BC3_UNorm, 16, 0, 0, 64, 64)]
    [InlineData(PixelFormat.BC3_UNorm, 16, 8, 4, 16, 16)]
    [InlineData(PixelFormat.BC3_UNorm_SRgb, 16, 0, 0, 64, 64)]
    [InlineData(PixelFormat.BC3_UNorm_SRgb, 16, 8, 4, 16, 16)]
    [InlineData(PixelFormat.BC4_UNorm, 8, 0, 0, 16, 16)]
    [InlineData(PixelFormat.BC4_UNorm, 8, 8, 4, 16, 16)]
    [InlineData(PixelFormat.BC4_SNorm, 8, 0, 0, 16, 16)]
    [InlineData(PixelFormat.BC4_SNorm, 8, 8, 4, 16, 16)]
    [InlineData(PixelFormat.BC5_UNorm, 16, 0, 0, 16, 16)]
    [InlineData(PixelFormat.BC5_UNorm, 16, 8, 4, 16, 16)]
    [InlineData(PixelFormat.BC5_SNorm, 16, 0, 0, 16, 16)]
    [InlineData(PixelFormat.BC5_SNorm, 16, 8, 4, 16, 16)]
    [InlineData(PixelFormat.BC7_UNorm, 16, 0, 0, 16, 16)]
    [InlineData(PixelFormat.BC7_UNorm, 16, 8, 4, 16, 16)]
    [InlineData(PixelFormat.BC7_UNorm_SRgb, 16, 0, 0, 16, 16)]
    [InlineData(PixelFormat.BC7_UNorm_SRgb, 16, 8, 4, 16, 16)]
    public unsafe void Copy_Compressed_Texture(PixelFormat format, uint blockSizeInBytes, uint srcX, uint srcY, uint copyWidth, uint copyHeight)
    {
        if (!GD.GetPixelFormatSupport(format, TextureType.Texture2D, TextureUsage.Sampled))
        {
            return;
        }

        Texture copySrc = RF.CreateTexture(TextureDescription.Texture2D(
            64, 64, 1, 1, format, TextureUsage.Sampled));
        Texture copyDst = RF.CreateTexture(TextureDescription.Texture2D(
            copyWidth, copyHeight, 1, 1, format, TextureUsage.Sampled));

        const int numPixelsInBlockSide = 4;
        const int numPixelsInBlock = 16;

        uint totalDataSize = copyWidth * copyHeight / numPixelsInBlock * blockSizeInBytes;
        byte[] data = new byte[totalDataSize];

        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)i;
        }
        fixed (byte* dataPtr = data)
        {
            GD.UpdateTexture(copySrc, (IntPtr)dataPtr, totalDataSize, new TextureRegion(srcX, srcY, 0, copyWidth, copyHeight, 1));
        }

        GD.RunTestGraph((context, cl) =>
        {
            cl.CopyTexture(
                copySrc, srcX, srcY, 0, 0, 0,
                copyDst, 0, 0, 0, 0, 0,
                copyWidth, copyHeight, 1, 1);
        });
        GD.WaitForIdle();

        byte[] view = ReadTextureBytes(copyDst, new TextureRegion(0, 0, 0, copyWidth, copyHeight, 1), totalDataSize);
        for (int i = 0; i < data.Length; i++)
        {
            Assert.Equal(data[i], view[i]);
        }
    }

    [InlineData(true)]
    [InlineData(false)]
    [Theory]
    public unsafe void Copy_Compressed_Array(bool separateLayerCopies)
    {
        PixelFormat format = PixelFormat.BC3_UNorm;
        if (!GD.GetPixelFormatSupport(format, TextureType.Texture2D, TextureUsage.Sampled))
        {
            return;
        }

        TextureDescription texDesc = TextureDescription.Texture2D(
            16, 16,
            1, 4,
            format,
            TextureUsage.Sampled);

        Texture copySrc = RF.CreateTexture(texDesc);
        Texture copyDst = RF.CreateTexture(texDesc);

        for (uint layer = 0; layer < copySrc.ArrayLayers; layer++)
        {
            int byteCount = 16 * 16;
            byte[] data = Enumerable.Range(0, byteCount).Select(i => (byte)(i + layer)).ToArray();
            GD.UpdateTexture(copySrc, data, new TextureRegion(0, 0, 0, 16, 16, 1, 0, layer));
        }

        GD.WaitForExecution(GD.RunTestGraph((context, copyCL) =>
        {
            if (separateLayerCopies)
            {
                for (uint layer = 0; layer < copySrc.ArrayLayers; layer++)
                {
                    copyCL.CopyTexture(copySrc, 0, 0, 0, 0, layer, copyDst, 0, 0, 0, 0, layer, 16, 16, 1, 1);
                }
            }
            else
            {
                copyCL.CopyTexture(copySrc, 0, 0, 0, 0, 0, copyDst, 0, 0, 0, 0, 0, 16, 16, 1, copySrc.ArrayLayers);
            }
        }));

        for (uint layer = 0; layer < copyDst.ArrayLayers; layer++)
        {
            byte[] map = ReadTextureBytes(copyDst, new TextureRegion(0, 0, 0, 16, 16, 1, 0, layer), 16 * 16);
            for (int index = 0; index < 64 * 4; index++)
            {
                Assert.Equal((byte)(index + layer), map[index]);
            }
        }
    }

    [Fact]
    public unsafe void Update_ThenRead_3D()
    {
        Texture tex3D = RF.CreateTexture(TextureDescription.Texture3D(
            10, 10, 10, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

        Color32[] data = new Color32[tex3D.Width * tex3D.Height * tex3D.Depth];
        for (int z = 0; z < tex3D.Depth; z++)
            for (int y = 0; y < tex3D.Height; y++)
                for (int x = 0; x < tex3D.Width; x++)
                {
                    int index = (int)(z * tex3D.Width * tex3D.Height + y * tex3D.Height + x);
                    data[index] = new Color32((byte)x, (byte)y, (byte)z, 1);
                }

        fixed (Color32* dataPtr = data)
        {
            GD.UpdateTexture(tex3D, (IntPtr)dataPtr, (uint)(data.Length * Unsafe.SizeOf<Color32>()), new TextureRegion(0, 0, 0, tex3D.Width, tex3D.Height, tex3D.Depth));
        }

        TexelData<Color32> view = ReadTexture<Color32>(tex3D);
        for (int z = 0; z < tex3D.Depth; z++)
            for (int y = 0; y < tex3D.Height; y++)
                for (int x = 0; x < tex3D.Width; x++)
                {
                    Assert.Equal(new Color32((byte)x, (byte)y, (byte)z, 1), view[x, y, z]);
                }
    }

    [Fact]
    public unsafe void Update_ThenRead_1D()
    {

        Texture tex1D = RF.CreateTexture(
            TextureDescription.Texture1D(100, 1, 1, PixelFormat.R16_UNorm, TextureUsage.Sampled));
        ushort[] data = Enumerable.Range(0, (int)tex1D.Width).Select(i => (ushort)(i * 2)).ToArray();
        fixed (ushort* dataPtr = &data[0])
        {
            GD.UpdateTexture(tex1D, (IntPtr)dataPtr, (uint)(data.Length * sizeof(ushort)), new TextureRegion(0, 0, 0, tex1D.Width, 1, 1));
        }

        TexelData<ushort> view = ReadTexture<ushort>(tex1D);
        for (int i = 0; i < tex1D.Width; i++)
        {
            Assert.Equal((ushort)(i * 2), view[i]);
        }
    }

    [Fact]
    public void Copy_1DTo1D_WithOffset()
    {

        Texture tex1D = RF.CreateTexture(
            TextureDescription.Texture1D(100, 1, 1, PixelFormat.R16_UNorm, TextureUsage.Sampled));
        Texture dst1D = RF.CreateTexture(
            TextureDescription.Texture1D(150, 1, 1, PixelFormat.R16_UNorm, TextureUsage.Sampled));

        ushort[] data = Enumerable.Range(0, (int)tex1D.Width).Select(i => (ushort)(i * 2)).ToArray();
        GD.UpdateTexture(tex1D, data, new TextureRegion(tex1D.Width));

        GD.RunTestGraph((context, cl) =>
        {
            cl.CopyTexture(
                tex1D, 0, 0, 0, 0, 0,
                dst1D, 25, 0, 0, 0, 0,
                tex1D.Width, 1, 1, 1);
        });
        GD.WaitForIdle();

        TexelData<ushort> readView = ReadTexture<ushort>(dst1D);
        for (int i = 0; i < tex1D.Width; i++)
        {
            Assert.Equal((ushort)(i * 2), readView[i + 25]);
        }
    }

    [Fact]
    public void Update_MultipleMips_1D()
    {

        Texture tex1D = RF.CreateTexture(TextureDescription.Texture1D(
            100, 5, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

        for (uint level = 0; level < tex1D.MipLevels; level++)
        {
            uint mipWidth = Math.Max(1, tex1D.Width >> (int)level);
            Color32[] writeData = new Color32[mipWidth];
            for (int i = 0; i < writeData.Length; i++)
            {
                writeData[i] = new Color32((byte)i, (byte)(i * 2), (byte)level, 1);
            }
            GD.UpdateTexture(tex1D, writeData, new TextureRegion(0, 0, 0, mipWidth, 1, 1, level, 0));
        }

        for (uint level = 0; level < tex1D.MipLevels; level++)
        {
            TexelData<Color32> readView = ReadTexture<Color32>(tex1D, level);
            for (int i = 0; i < readView.Length; i++)
            {
                Assert.Equal(new Color32((byte)i, (byte)(i * 2), (byte)level, 1), readView[i]);
            }
        }
    }

    [Fact]
    public void Copy_DifferentMip_1DTo1D()
    {

        Texture tex1D = RF.CreateTexture(
            TextureDescription.Texture1D(200, 2, 1, PixelFormat.R16_UNorm, TextureUsage.Sampled));
        Texture dst1D = RF.CreateTexture(
            TextureDescription.Texture1D(100, 1, 1, PixelFormat.R16_UNorm, TextureUsage.Sampled));

        ushort[] data = Enumerable.Range(0, 100).Select(i => (ushort)(i * 2)).ToArray();
        GD.UpdateTexture(tex1D, data, new TextureRegion(0, 0, 0, 100, 1, 1, 1, 0));

        GD.RunTestGraph((context, cl) =>
        {
            cl.CopyTexture(
                tex1D, 0, 0, 0, 1, 0,
                dst1D, 0, 0, 0, 0, 0,
                dst1D.Width, 1, 1, 1);
        });
        GD.WaitForIdle();

        TexelData<ushort> readView = ReadTexture<ushort>(dst1D);
        for (int i = 0; i < dst1D.Width; i++)
        {
            Assert.Equal((ushort)(i * 2), readView[i]);
        }
    }

    [Fact]
    public void Copy_WithOffsets_2D()
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
    }

    [Fact]
    public void Copy_ArrayToNonArray()
    {
        Texture src = RF.CreateTexture(TextureDescription.Texture2D(
            10, 10, 1, 10, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        Texture dst = RF.CreateTexture(TextureDescription.Texture2D(
            10, 10, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

        Color32[] writeData = new Color32[src.Width * src.Height];
        for (int y = 0; y < src.Height; y++)
            for (int x = 0; x < src.Width; x++)
            {
                writeData[y * src.Width + x] = new Color32((byte)x, (byte)y, 0, 1);
            }
        GD.UpdateTexture(src, writeData, new TextureRegion(0, 0, 0, src.Width, src.Height, 1, 0, 5));

        GD.RunTestGraph((context, cl) =>
        {
            cl.CopyTexture(
                src, 0, 0, 0, 0, 5,
                dst, 0, 0, 0, 0, 0,
                10, 10, 1, 1);
        });
        GD.WaitForIdle();

        TexelData<Color32> readView = ReadTexture<Color32>(dst);
        for (int y = 0; y < dst.Height; y++)
            for (int x = 0; x < dst.Width; x++)
            {
                Assert.Equal(new Color32((byte)x, (byte)y, 0, 1), readView[x, y]);
            }
    }

    [Fact]
    public void Update_ThenRead_MultipleArrayLayers()
    {
        Texture src = RF.CreateTexture(TextureDescription.Texture2D(
            10, 10, 1, 10, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

        for (uint layer = 0; layer < src.ArrayLayers; layer++)
        {
            Color32[] writeData = new Color32[src.Width * src.Height];
            for (int y = 0; y < src.Height; y++)
                for (int x = 0; x < src.Width; x++)
                {
                    writeData[y * src.Width + x] = new Color32((byte)x, (byte)y, (byte)layer, 1);
                }
            GD.UpdateTexture(src, writeData, new TextureRegion(0, 0, 0, src.Width, src.Height, 1, 0, layer));
        }

        for (uint layer = 0; layer < src.ArrayLayers; layer++)
        {
            TexelData<Color32> readView = ReadTexture<Color32>(src, 0, layer);
            for (int y = 0; y < src.Height; y++)
                for (int x = 0; x < src.Width; x++)
                {
                    Assert.Equal(new Color32((byte)x, (byte)y, (byte)layer, 1), readView[x, y]);
                }
        }
    }

    [Fact]
    public unsafe void Update_WithOffset_2D()
    {
        Texture tex2D = RF.CreateTexture(TextureDescription.Texture2D(
            100, 100, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

        Color32[] data = new Color32[50 * 30];
        for (uint y = 0; y < 30; y++)
            for (uint x = 0; x < 50; x++)
            {
                data[y * 50 + x] = new Color32((byte)x, (byte)y, 0, 1);
            }

        fixed (Color32* dataPtr = &data[0])
        {
            GD.UpdateTexture(tex2D, (IntPtr)dataPtr, (uint)(data.Length * sizeof(Color32)), new TextureRegion(50, 70, 0, 50, 30, 1));
        }

        TexelData<Color32> readView = ReadTexture<Color32>(tex2D);
        for (int y = 0; y < 30; y++)
            for (int x = 0; x < 50; x++)
            {
                Assert.Equal(new Color32((byte)x, (byte)y, 0, 1), readView[x + 50, y + 70]);
            }
    }

    [Fact]
    public unsafe void Update_NonMultipleOfFourWithCompressedTexture_2D()
    {
        Texture tex2D = RF.CreateTexture(TextureDescription.Texture2D(
            2, 2, 1, 1, PixelFormat.BC1_Rgb_UNorm, TextureUsage.Sampled));

        byte[] data = new byte[16];

        fixed (byte* dataPtr = &data[0])
        {
            GD.UpdateTexture(tex2D, (IntPtr)dataPtr, (uint)data.Length, new TextureRegion(0, 0, 0, 4, 4, 1));
        }
    }

    [Fact]
    public void Update_NonZeroMip_3D()
    {
        Texture tex3D = RF.CreateTexture(TextureDescription.Texture3D(
            40, 40, 40, 3, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

        Color32[] writeData = new Color32[10 * 10 * 10];
        for (int z = 0; z < 10; z++)
            for (int y = 0; y < 10; y++)
                for (int x = 0; x < 10; x++)
                {
                    writeData[(z * 10 + y) * 10 + x] = new Color32((byte)x, (byte)y, (byte)z, 1);
                }
        GD.UpdateTexture(tex3D, writeData, new TextureRegion(0, 0, 0, 10, 10, 10, 2, 0));

        TexelData<Color32> readView = ReadTexture<Color32>(tex3D, 2);
        for (int z = 0; z < 10; z++)
            for (int y = 0; y < 10; y++)
                for (int x = 0; x < 10; x++)
                {
                    Assert.Equal(new Color32((byte)x, (byte)y, (byte)z, 1), readView[x, y, z]);
                }
    }

    [Fact]
    public unsafe void Update_Copy_Read_3D()
    {
        Texture tex3D = RF.CreateTexture(TextureDescription.Texture3D(
            16, 16, 16, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));
        Color32[] data = new Color32[16 * 16 * 16];
        for (int z = 0; z < 16; z++)
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    int index = (int)(z * tex3D.Width * tex3D.Height + y * tex3D.Height + x);
                    data[index] = new Color32((byte)x, (byte)y, (byte)z, 1);
                }

        fixed (Color32* dataPtr = data)
        {
            GD.UpdateTexture(tex3D, (IntPtr)dataPtr, (uint)(data.Length * Unsafe.SizeOf<Color32>()), new TextureRegion(0, 0, 0, tex3D.Width, tex3D.Height, tex3D.Depth));
        }

        Texture copy = RF.CreateTexture(TextureDescription.Texture3D(
            16, 16, 16, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

        GD.RunTestGraph((context, cl) =>
        {
            cl.CopyTexture(tex3D, copy);
        });
        GD.WaitForIdle();

        TexelData<Color32> view = ReadTexture<Color32>(copy);
        for (int z = 0; z < tex3D.Depth; z++)
            for (int y = 0; y < tex3D.Height; y++)
                for (int x = 0; x < tex3D.Width; x++)
                {
                    Assert.Equal(new Color32((byte)x, (byte)y, (byte)z, 1), view[x, y, z]);
                }
    }

    [Fact]
    public unsafe void Copy_NonSquareTexture()
    {
        Texture src = RF.CreateTexture(
            TextureDescription.Texture2D(512, 128, 1, 1, PixelFormat.R8_UNorm, TextureUsage.Sampled));
        byte[] data = Enumerable.Repeat((byte)255, (int)(src.Width * src.Height)).ToArray();
        fixed (byte* dataPtr = data)
        {
            GD.UpdateTexture(src, (IntPtr)dataPtr, (uint)data.Length, new TextureRegion(0, 0, 0, src.Width, src.Height, 1));
        }

        Texture dst = RF.CreateTexture(
            TextureDescription.Texture2D(512, 128, 1, 1, PixelFormat.R8_UNorm, TextureUsage.Sampled));
        byte[] data2 = Enumerable.Repeat((byte)100, (int)(dst.Width * dst.Height)).ToArray();
        fixed (byte* dataPtr2 = data2)
        {
            GD.UpdateTexture(dst, (IntPtr)dataPtr2, (uint)data2.Length, new TextureRegion(0, 0, 0, dst.Width, dst.Height, 1));
        }

        GD.RunTestGraph((context, cl) =>
        {
            cl.CopyTexture(src, dst);
        });
        GD.WaitForIdle();

        TexelData<byte> readView = ReadTexture<byte>(dst);
        for (uint y = 0; y < dst.Height; y++)
            for (uint x = 0; x < dst.Width; x++)
            {
                Assert.Equal(255, readView[x, y]);
            }

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

    [Theory]
    [InlineData(TextureUsage.Sampled)]
    [InlineData(TextureUsage.RenderTarget)]
    [InlineData(TextureUsage.Storage)]
    [InlineData(TextureUsage.Sampled | TextureUsage.RenderTarget)]
    public unsafe void GenerateMipmaps(TextureUsage usage)
    {
        TextureDescription texDesc = TextureDescription.Texture2D(
            1024, 1024, 11, 1,
            PixelFormat.R32_G32_B32_A32_Float,
            usage);
        Texture tex = RF.CreateTexture(texDesc);

        Color[] pixelData = Enumerable.Repeat(Color.Red, 1024 * 1024).ToArray();
        fixed (Color* pixelDataPtr = pixelData)
        {
            GD.UpdateTexture(tex, (IntPtr)pixelDataPtr, 1024 * 1024 * 16, new TextureRegion(0, 0, 0, 1024, 1024, 1));
        }

        GD.RunTestGraph((context, cl) =>
        {
            cl.GenerateMipmaps(tex);
        });
        GD.WaitForIdle();

        for (uint level = 1; level < 11; level++)
        {
            TexelData<Color> readView = ReadTexture<Color>(tex, level);
            uint mipWidth = Math.Max(1, (uint)(tex.Width / Math.Pow(2, level)));
            uint mipHeight = Math.Max(1, (uint)(tex.Width / Math.Pow(2, level)));
            Assert.Equal(Color.Red, readView[mipWidth - 1, mipHeight - 1]);
        }
    }

    [Fact]
    public void CopyTexture_SmallCompressed()
    {
        Texture src = RF.CreateTexture(TextureDescription.Texture2D(16, 16, 4, 1, PixelFormat.BC3_UNorm, TextureUsage.Sampled));
        Texture dst = RF.CreateTexture(TextureDescription.Texture2D(16, 16, 4, 1, PixelFormat.BC3_UNorm, TextureUsage.Sampled));

        GD.RunTestGraph((context, cl) =>
        {
            cl.CopyTexture(
                src, 0, 0, 0, 3, 0,
                dst, 0, 0, 0, 3, 0,
                4, 4, 1, 1);
        });
        GD.WaitForIdle();
    }

    [Theory]
    [InlineData(PixelFormat.BC1_Rgb_UNorm)]
    [InlineData(PixelFormat.BC1_Rgb_UNorm_SRgb)]
    [InlineData(PixelFormat.BC1_Rgba_UNorm)]
    [InlineData(PixelFormat.BC1_Rgba_UNorm_SRgb)]
    [InlineData(PixelFormat.BC2_UNorm)]
    [InlineData(PixelFormat.BC2_UNorm_SRgb)]
    [InlineData(PixelFormat.BC3_UNorm)]
    [InlineData(PixelFormat.BC3_UNorm_SRgb)]
    [InlineData(PixelFormat.BC4_UNorm)]
    [InlineData(PixelFormat.BC4_SNorm)]
    [InlineData(PixelFormat.BC5_UNorm)]
    [InlineData(PixelFormat.BC5_SNorm)]
    [InlineData(PixelFormat.BC7_UNorm)]
    [InlineData(PixelFormat.BC7_UNorm_SRgb)]
    public void CreateSmallTexture(PixelFormat format)
    {
        Texture tex = RF.CreateTexture(TextureDescription.Texture2D(1, 1, 1, 1, format, TextureUsage.Sampled));
        Assert.Equal(1u, tex.Width);
        Assert.Equal(1u, tex.Height);
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
