using Prowl.Graphite.Vk;

using Silk.NET.Vulkan;

using Xunit;

namespace Prowl.Graphite.Tests;

public class DescriptorLimitTests
{
    private const ShaderStages Graphics = ShaderStages.Vertex | ShaderStages.Fragment;

    private static ResourceLayoutElementDescription Element(string name, ResourceKind kind, int binding, uint count = 0, ShaderStages stages = Graphics, ResourceLayoutElementOptions options = ResourceLayoutElementOptions.None)
        => new(name, kind, stages, binding, options) { ArrayCount = count };

    private static readonly ResourceLayoutDescription Textures = new(0, Element("Textures", ResourceKind.TextureReadOnly, 0, 40960), Element("Samplers", ResourceKind.Sampler, 1, 64)) { Table = true };
    private static readonly ResourceLayoutDescription Buffers = new(1, Element("A", ResourceKind.StructuredBufferReadOnly, 0), Element("B", ResourceKind.StructuredBufferReadOnly, 1), Element("C", ResourceKind.StructuredBufferReadOnly, 2, stages: ShaderStages.Vertex));

    [Fact]
    public void FitsWithinGenerousLimits()
    {
        GraphicsDeviceLimits limits = new()
        {
            PerStage = new DescriptorCounts(16, 12, 8, 4, 16, 4, 128),
            PerStageWithTables = new DescriptorCounts(500000, 72, 8, 500000, 500000, 500000, 500000),
        };

        Assert.Null(limits.Exceeded([Textures, Buffers]));
    }

    [Fact]
    public void CountsOnlyTheStagesThatReadEachBuffer()
    {
        GraphicsDeviceLimits limits = new() { PerStage = DescriptorCounts.Unlimited with { StorageBuffers = 2 } };

        string? exceeded = limits.Exceeded([Buffers]);

        Assert.NotNull(exceeded);
        Assert.Contains("Vertex", exceeded);
        Assert.Contains("3 storage buffers", exceeded);
    }

    [Fact]
    public void TablesCountAgainstTheUpdateAfterBindLimitsOnly()
    {
        GraphicsDeviceLimits limits = new()
        {
            PerStage = DescriptorCounts.Unlimited with { SampledImages = 16 },
            PerStageWithTables = DescriptorCounts.Unlimited with { SampledImages = 16384 },
        };

        string? exceeded = limits.Exceeded([Textures]);

        Assert.NotNull(exceeded);
        Assert.Contains("40960 sampled images, the limit is 16384", exceeded);
    }

    [Fact]
    public void TableLimitsAreIgnoredWithoutATable()
    {
        GraphicsDeviceLimits limits = new() { PerStageWithTables = DescriptorCounts.Unlimited with { StorageBuffers = 0 }, PerProgramWithTables = DescriptorCounts.Unlimited with { StorageBuffers = 0 } };

        Assert.Null(limits.Exceeded([Buffers]));
    }

    [Fact]
    public void CombinedImageSamplersCountAsBothSamplerAndImage()
    {
        ResourceLayoutDescription combined = new(0, Element("Tex", ResourceKind.TextureReadOnly, 0, options: ResourceLayoutElementOptions.CombinedImageSampler), Element("Tex2", ResourceKind.TextureReadOnly, 1, options: ResourceLayoutElementOptions.CombinedImageSampler));
        GraphicsDeviceLimits limits = new() { PerProgram = DescriptorCounts.Unlimited with { Samplers = 1 } };

        string? exceeded = limits.Exceeded([combined]);

        Assert.NotNull(exceeded);
        Assert.Contains("2 samplers", exceeded);
    }

    [Fact]
    public void ResourcesLeaveSamplersOut()
    {
        ResourceLayoutDescription layout = new(0, Element("Tex", ResourceKind.TextureReadOnly, 0), Element("S", ResourceKind.Sampler, 1, 8), Element("Buf", ResourceKind.StructuredBufferReadOnly, 2));
        GraphicsDeviceLimits limits = new() { PerStage = DescriptorCounts.Unlimited with { Resources = 2 } };

        Assert.Null(limits.Exceeded([layout]));
    }

    [Fact]
    public void SetIndicesPastTheBoundSetLimitAreReported()
    {
        GraphicsDeviceLimits limits = new() { MaxBoundDescriptorSets = 4 };
        ResourceLayoutDescription user = new(4, Element("D", ResourceKind.StructuredBufferReadOnly, 0));

        Assert.Contains("5 descriptor sets", limits.Exceeded([Buffers, user]));
    }
}

public class VulkanDeviceChoiceTests
{
    private const QueueFlags GraphicsCompute = QueueFlags.GraphicsBit | QueueFlags.ComputeBit | QueueFlags.TransferBit;

    [Fact]
    public void PrefersTheGraphicsFamilyThatAlsoPresents()
    {
        (int graphics, int present) = VkGraphicsDevice.ChooseQueueFamilies([GraphicsCompute, GraphicsCompute], [false, true], needsPresent: true);

        Assert.Equal((1, 1), (graphics, present));
    }

    [Fact]
    public void FallsBackToASeparatePresentFamily()
    {
        (int graphics, int present) = VkGraphicsDevice.ChooseQueueFamilies([QueueFlags.TransferBit, GraphicsCompute, QueueFlags.TransferBit], [true, false, true], needsPresent: true);

        Assert.Equal((1, 0), (graphics, present));
    }

    [Fact]
    public void SkipsGraphicsFamiliesWithoutCompute()
    {
        (int graphics, _) = VkGraphicsDevice.ChooseQueueFamilies([QueueFlags.GraphicsBit, GraphicsCompute], [false, false], needsPresent: false);

        Assert.Equal(1, graphics);
    }

    [Fact]
    public void ReportsWhenNothingPresents()
    {
        Assert.Equal((0, -1), VkGraphicsDevice.ChooseQueueFamilies([GraphicsCompute, QueueFlags.SparseBindingBit], [false, false], needsPresent: true));
    }

    [Fact]
    public void TimestampsWrapWithinTheirValidBits()
    {
        Assert.Equal(16ul, VkGraphicsDevice.TimestampTicks((1ul << 48) - 6, 10, 48));
        Assert.Equal(5ul, VkGraphicsDevice.TimestampTicks(10, 15, 64));
        Assert.Equal(5ul, VkGraphicsDevice.TimestampTicks(0xFFFF_0000_0000_000Aul, 15, 36));
        Assert.Equal(0ul, VkGraphicsDevice.TimestampTicks(10, 15, 0));
    }

    private static SurfaceFormatKHR Surface(Format format) => new(format, ColorSpaceKHR.SpaceSrgbNonlinearKhr);

    [Fact]
    public void AndroidRgbaSurfacesServeBothEncodings()
    {
        SurfaceFormatKHR[] android = [Surface(Format.R8G8B8A8Unorm), Surface(Format.R8G8B8A8Srgb), Surface(Format.R5G6B5UnormPack16)];

        Assert.Equal(Format.R8G8B8A8Srgb, VkSwapchain.ChooseSurfaceFormat(android, srgb: true)?.Format);
        Assert.Equal(Format.R8G8B8A8Unorm, VkSwapchain.ChooseSurfaceFormat(android, srgb: false)?.Format);
    }

    [Fact]
    public void BgraStaysPreferredWhereItExists()
    {
        SurfaceFormatKHR[] desktop = [Surface(Format.R8G8B8A8Srgb), Surface(Format.B8G8R8A8Srgb), Surface(Format.B8G8R8A8Unorm)];

        Assert.Equal(Format.B8G8R8A8Srgb, VkSwapchain.ChooseSurfaceFormat(desktop, srgb: true)?.Format);
    }

    [Fact]
    public void SrgbWithoutAnyMatchIsNull()
    {
        Assert.Null(VkSwapchain.ChooseSurfaceFormat([Surface(Format.A2B10G10R10UnormPack32)], srgb: true));
    }

    [Fact]
    public void CompositeAlphaFallsBackToInherit()
    {
        Assert.Equal(CompositeAlphaFlagsKHR.InheritBitKhr, VkSwapchain.ChooseCompositeAlpha(CompositeAlphaFlagsKHR.InheritBitKhr));
        Assert.Equal(CompositeAlphaFlagsKHR.OpaqueBitKhr, VkSwapchain.ChooseCompositeAlpha(CompositeAlphaFlagsKHR.OpaqueBitKhr | CompositeAlphaFlagsKHR.InheritBitKhr));
    }

    [Fact]
    public void PreRotationTakesTheCurrentTransform()
    {
        const SurfaceTransformFlagsKHR All = SurfaceTransformFlagsKHR.IdentityBitKhr | SurfaceTransformFlagsKHR.Rotate90BitKhr | SurfaceTransformFlagsKHR.Rotate180BitKhr | SurfaceTransformFlagsKHR.Rotate270BitKhr;

        Assert.Equal((SurfaceTransformFlagsKHR.Rotate90BitKhr, SwapchainTransform.Rotate90), VkSwapchain.ChooseTransform(All, SurfaceTransformFlagsKHR.Rotate90BitKhr, preRotate: true));
        Assert.Equal((SurfaceTransformFlagsKHR.IdentityBitKhr, SwapchainTransform.Identity), VkSwapchain.ChooseTransform(All, SurfaceTransformFlagsKHR.Rotate90BitKhr, preRotate: false));
    }

    [Fact]
    public void ASurfaceWithoutIdentityKeepsItsTransform()
    {
        Assert.Equal((SurfaceTransformFlagsKHR.Rotate270BitKhr, SwapchainTransform.Rotate270), VkSwapchain.ChooseTransform(SurfaceTransformFlagsKHR.Rotate270BitKhr, SurfaceTransformFlagsKHR.Rotate270BitKhr, preRotate: false));
    }
}
