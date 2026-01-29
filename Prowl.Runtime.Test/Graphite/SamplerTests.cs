// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Xunit;

using Prowl.Runtime.Graphite;

namespace Prowl.Runtime.Test.Graphite;

[Collection("Graphite")]
public class SamplerTests
{
    private readonly GraphiteTestFixture _fixture;

    public SamplerTests(GraphiteTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void CreateSampler_DefaultDescriptor_Succeeds()
    {
        var descriptor = new SamplerDescriptor
        {
            MinFilter = TextureFilter.Linear,
            MagFilter = TextureFilter.Linear,
            MipmapFilter = TextureFilter.Linear,
            AddressModeU = TextureAddressMode.Repeat,
            AddressModeV = TextureAddressMode.Repeat,
            AddressModeW = TextureAddressMode.Repeat,
            DebugName = "DefaultSampler"
        };

        using var sampler = _fixture.Device.CreateSampler(in descriptor);

        Assert.NotNull(sampler);
        Assert.Equal(TextureFilter.Linear, sampler.MinFilter);
        Assert.Equal(TextureFilter.Linear, sampler.MagFilter);
    }

    [Fact]
    public void CreateSampler_NearestFiltering_Succeeds()
    {
        var descriptor = new SamplerDescriptor
        {
            MinFilter = TextureFilter.Nearest,
            MagFilter = TextureFilter.Nearest,
            MipmapFilter = TextureFilter.Nearest,
            AddressModeU = TextureAddressMode.Repeat,
            AddressModeV = TextureAddressMode.Repeat,
            AddressModeW = TextureAddressMode.Repeat
        };

        using var sampler = _fixture.Device.CreateSampler(in descriptor);

        Assert.NotNull(sampler);
        Assert.Equal(TextureFilter.Nearest, sampler.MinFilter);
        Assert.Equal(TextureFilter.Nearest, sampler.MagFilter);
    }

    [Theory]
    [InlineData(TextureAddressMode.Repeat)]
    [InlineData(TextureAddressMode.MirrorRepeat)]
    [InlineData(TextureAddressMode.ClampToEdge)]
    [InlineData(TextureAddressMode.ClampToBorder)]
    public void CreateSampler_VariousAddressModes_Succeeds(TextureAddressMode mode)
    {
        var descriptor = new SamplerDescriptor
        {
            MinFilter = TextureFilter.Linear,
            MagFilter = TextureFilter.Linear,
            MipmapFilter = TextureFilter.Linear,
            AddressModeU = mode,
            AddressModeV = mode,
            AddressModeW = mode
        };

        using var sampler = _fixture.Device.CreateSampler(in descriptor);

        Assert.NotNull(sampler);
        Assert.Equal(mode, sampler.AddressModeU);
    }

    [Fact]
    public void CreateSampler_WithAnisotropy_Succeeds()
    {
        var descriptor = new SamplerDescriptor
        {
            MinFilter = TextureFilter.Linear,
            MagFilter = TextureFilter.Linear,
            MipmapFilter = TextureFilter.Linear,
            AddressModeU = TextureAddressMode.Repeat,
            AddressModeV = TextureAddressMode.Repeat,
            AddressModeW = TextureAddressMode.Repeat,
            MaxAnisotropy = 16
        };

        using var sampler = _fixture.Device.CreateSampler(in descriptor);

        Assert.NotNull(sampler);
        Assert.Equal(16u, sampler.MaxAnisotropy);
    }

    [Fact]
    public void CreateSampler_WithLodBias_Succeeds()
    {
        var descriptor = new SamplerDescriptor
        {
            MinFilter = TextureFilter.Linear,
            MagFilter = TextureFilter.Linear,
            MipmapFilter = TextureFilter.Linear,
            AddressModeU = TextureAddressMode.Repeat,
            AddressModeV = TextureAddressMode.Repeat,
            AddressModeW = TextureAddressMode.Repeat,
            MipLodBias = 1.0f,
            MinLod = 0,
            MaxLod = 10
        };

        using var sampler = _fixture.Device.CreateSampler(in descriptor);

        Assert.NotNull(sampler);
    }

    [Fact]
    public void CreateSampler_ShadowSampler_Succeeds()
    {
        var descriptor = new SamplerDescriptor
        {
            MinFilter = TextureFilter.Linear,
            MagFilter = TextureFilter.Linear,
            MipmapFilter = TextureFilter.Nearest,
            AddressModeU = TextureAddressMode.ClampToBorder,
            AddressModeV = TextureAddressMode.ClampToBorder,
            AddressModeW = TextureAddressMode.ClampToBorder,
            CompareFunction = CompareFunction.LessEqual,
            BorderColor = BorderColor.OpaqueWhite
        };

        using var sampler = _fixture.Device.CreateSampler(in descriptor);

        Assert.NotNull(sampler);
        Assert.Equal(CompareFunction.LessEqual, sampler.CompareFunction);
    }

    [Theory]
    [InlineData(BorderColor.TransparentBlack)]
    [InlineData(BorderColor.OpaqueBlack)]
    [InlineData(BorderColor.OpaqueWhite)]
    public void CreateSampler_VariousBorderColors_Succeeds(BorderColor borderColor)
    {
        var descriptor = new SamplerDescriptor
        {
            MinFilter = TextureFilter.Linear,
            MagFilter = TextureFilter.Linear,
            MipmapFilter = TextureFilter.Linear,
            AddressModeU = TextureAddressMode.ClampToBorder,
            AddressModeV = TextureAddressMode.ClampToBorder,
            AddressModeW = TextureAddressMode.ClampToBorder,
            BorderColor = borderColor
        };

        using var sampler = _fixture.Device.CreateSampler(in descriptor);

        Assert.NotNull(sampler);
    }

    [Fact]
    public void Sampler_Dispose_CleansUpResources()
    {
        var descriptor = new SamplerDescriptor
        {
            MinFilter = TextureFilter.Linear,
            MagFilter = TextureFilter.Linear,
            MipmapFilter = TextureFilter.Linear,
            AddressModeU = TextureAddressMode.Repeat,
            AddressModeV = TextureAddressMode.Repeat,
            AddressModeW = TextureAddressMode.Repeat
        };

        var sampler = _fixture.Device.CreateSampler(in descriptor);
        Assert.NotNull(sampler);

        // Should dispose without error
        sampler.Dispose();
    }
}
