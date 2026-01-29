// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

using Prowl.Runtime.Graphite;

namespace Prowl.Runtime.Test.Graphite;

[Collection("Graphite")]
public class TextureTests
{
    private readonly GraphiteTestFixture _fixture;

    public TextureTests(GraphiteTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void CreateTexture2D_WithValidDescriptor_Succeeds()
    {
        var descriptor = new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 256,
            Height = 256,
            Depth = 1,
            MipLevels = 1,
            ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.Sampled,
            SampleCount = SampleCount.Count1,
            DebugName = "TestTexture2D"
        };

        using var texture = _fixture.Device.CreateTexture(in descriptor);

        Assert.NotNull(texture);
        Assert.Equal(256u, texture.Width);
        Assert.Equal(256u, texture.Height);
        Assert.Equal(TextureFormat.RGBA8Unorm, texture.Format);
    }

    [Fact]
    public void CreateTexture2D_WithMipmaps_Succeeds()
    {
        var descriptor = new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 256,
            Height = 256,
            Depth = 1,
            MipLevels = 0, // Auto-calculate
            ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.Sampled,
            SampleCount = SampleCount.Count1
        };

        using var texture = _fixture.Device.CreateTexture(in descriptor);

        Assert.NotNull(texture);
        // 256 = 2^8, so 9 mip levels (256, 128, 64, 32, 16, 8, 4, 2, 1)
        Assert.Equal(9u, texture.MipLevels);
    }

    [Fact]
    public void CreateTexture3D_Succeeds()
    {
        var descriptor = new TextureDescriptor
        {
            Dimension = TextureDimension.Texture3D,
            Width = 64,
            Height = 64,
            Depth = 64,
            MipLevels = 1,
            ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.Sampled,
            SampleCount = SampleCount.Count1
        };

        using var texture = _fixture.Device.CreateTexture(in descriptor);

        Assert.NotNull(texture);
        Assert.Equal(64u, texture.Depth);
    }

    [Fact]
    public void CreateTextureCube_Succeeds()
    {
        var descriptor = new TextureDescriptor
        {
            Dimension = TextureDimension.TextureCube,
            Width = 128,
            Height = 128,
            Depth = 1,
            MipLevels = 1,
            ArrayLayers = 6,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.Sampled,
            SampleCount = SampleCount.Count1
        };

        using var texture = _fixture.Device.CreateTexture(in descriptor);

        Assert.NotNull(texture);
        Assert.Equal(6u, texture.ArrayLayers);
    }

    [Fact]
    public void CreateTexture_RenderTarget_Succeeds()
    {
        var descriptor = new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 512,
            Height = 512,
            Depth = 1,
            MipLevels = 1,
            ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.RenderTarget | TextureUsage.Sampled,
            SampleCount = SampleCount.Count1
        };

        using var texture = _fixture.Device.CreateTexture(in descriptor);

        Assert.NotNull(texture);
        Assert.True(texture.Usage.HasFlag(TextureUsage.RenderTarget));
    }

    [Fact]
    public void CreateTexture_DepthFormat_Succeeds()
    {
        var descriptor = new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 512,
            Height = 512,
            Depth = 1,
            MipLevels = 1,
            ArrayLayers = 1,
            Format = TextureFormat.Depth24PlusStencil8,
            Usage = TextureUsage.RenderTarget,
            SampleCount = SampleCount.Count1
        };

        using var texture = _fixture.Device.CreateTexture(in descriptor);

        Assert.NotNull(texture);
        Assert.Equal(TextureFormat.Depth24PlusStencil8, texture.Format);
    }

    [Theory]
    [InlineData(TextureFormat.R8Unorm)]
    [InlineData(TextureFormat.RG8Unorm)]
    [InlineData(TextureFormat.RGBA8Unorm)]
    [InlineData(TextureFormat.RGBA16Float)]
    [InlineData(TextureFormat.RGBA32Float)]
    [InlineData(TextureFormat.R32Float)]
    public void CreateTexture_VariousFormats_Succeeds(TextureFormat format)
    {
        var descriptor = new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 64,
            Height = 64,
            Depth = 1,
            MipLevels = 1,
            ArrayLayers = 1,
            Format = format,
            Usage = TextureUsage.Sampled,
            SampleCount = SampleCount.Count1
        };

        using var texture = _fixture.Device.CreateTexture(in descriptor);

        Assert.NotNull(texture);
        Assert.Equal(format, texture.Format);
    }

    [Fact]
    public void UpdateTexture_WithData_Succeeds()
    {
        var descriptor = new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 4,
            Height = 4,
            Depth = 1,
            MipLevels = 1,
            ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.CopyDestination,
            SampleCount = SampleCount.Count1
        };

        using var texture = _fixture.Device.CreateTexture(in descriptor);

        // 4x4 RGBA = 64 bytes
        var data = new byte[64];
        for (int i = 0; i < data.Length; i++)
            data[i] = (byte)(i % 256);

        var updateDesc = new TextureUpdateDescriptor
        {
            MipLevel = 0,
            ArrayLayer = 0,
            X = 0,
            Y = 0,
            Z = 0,
            Width = 4,
            Height = 4,
            Depth = 1
        };

        _fixture.Device.UpdateTexture(texture, in updateDesc, data);

        // Should complete without error
        Assert.NotNull(texture);
    }

    [Fact]
    public void GenerateMipmaps_Succeeds()
    {
        var descriptor = new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 64,
            Height = 64,
            Depth = 1,
            MipLevels = 0, // Auto
            ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.Sampled | TextureUsage.CopyDestination,
            SampleCount = SampleCount.Count1
        };

        using var texture = _fixture.Device.CreateTexture(in descriptor);

        // Upload base mip data
        var data = new byte[64 * 64 * 4];
        var updateDesc = new TextureUpdateDescriptor
        {
            MipLevel = 0,
            ArrayLayer = 0,
            X = 0, Y = 0, Z = 0,
            Width = 64, Height = 64, Depth = 1
        };
        _fixture.Device.UpdateTexture(texture, in updateDesc, data);

        // Generate mipmaps
        _fixture.Device.GenerateMipmaps(texture);

        // Should complete without error
        Assert.NotNull(texture);
    }

    [Fact]
    public void Texture_Dispose_CleansUpResources()
    {
        var descriptor = new TextureDescriptor
        {
            Dimension = TextureDimension.Texture2D,
            Width = 64,
            Height = 64,
            Depth = 1,
            MipLevels = 1,
            ArrayLayers = 1,
            Format = TextureFormat.RGBA8Unorm,
            Usage = TextureUsage.Sampled,
            SampleCount = SampleCount.Count1
        };

        var texture = _fixture.Device.CreateTexture(in descriptor);
        Assert.NotNull(texture);

        // Should dispose without error
        texture.Dispose();
    }

    [Fact]
    public void GetSwapchainTexture_ReturnsValidTexture()
    {
        var swapchainTex = _fixture.Device.GetSwapchainTexture();

        Assert.NotNull(swapchainTex);
        Assert.Equal(TextureUsage.RenderTarget, swapchainTex.Usage);
    }
}
