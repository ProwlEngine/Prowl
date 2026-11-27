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
    public void CountsOnlyTheStagesThatReadEachBuffer()
    {
        GraphicsDeviceLimits limits = new() { PerStage = DescriptorCounts.Unlimited with { StorageBuffers = 2 } };

        string? exceeded = limits.Exceeded([Buffers]);

        Assert.NotNull(exceeded);
        Assert.Contains("Vertex", exceeded);
        Assert.Contains("3 storage buffers", exceeded);

        GraphicsDeviceLimits generous = new()
        {
            PerStage = new DescriptorCounts(16, 12, 8, 4, 16, 4, 128),
            PerStageWithTables = new DescriptorCounts(500000, 72, 8, 500000, 500000, 500000, 500000),
        };

        Assert.Null(generous.Exceeded([Textures, Buffers]));

        ResourceLayoutDescription combined = new(0, Element("Tex", ResourceKind.TextureReadOnly, 0, options: ResourceLayoutElementOptions.CombinedImageSampler), Element("Tex2", ResourceKind.TextureReadOnly, 1, options: ResourceLayoutElementOptions.CombinedImageSampler));
        GraphicsDeviceLimits combinedLimits = new() { PerProgram = DescriptorCounts.Unlimited with { Samplers = 1 } };

        string? combinedExceeded = combinedLimits.Exceeded([combined]);

        Assert.NotNull(combinedExceeded);
        Assert.Contains("2 samplers", combinedExceeded);

        ResourceLayoutDescription samplerLayout = new(0, Element("Tex", ResourceKind.TextureReadOnly, 0), Element("S", ResourceKind.Sampler, 1, 8), Element("Buf", ResourceKind.StructuredBufferReadOnly, 2));
        GraphicsDeviceLimits samplerLimits = new() { PerStage = DescriptorCounts.Unlimited with { Resources = 2 } };

        Assert.Null(samplerLimits.Exceeded([samplerLayout]));
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

        GraphicsDeviceLimits ignored = new() { PerStageWithTables = DescriptorCounts.Unlimited with { StorageBuffers = 0 }, PerProgramWithTables = DescriptorCounts.Unlimited with { StorageBuffers = 0 } };

        Assert.Null(ignored.Exceeded([Buffers]));
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

        (int separateGraphics, int separatePresent) = VkGraphicsDevice.ChooseQueueFamilies([QueueFlags.TransferBit, GraphicsCompute, QueueFlags.TransferBit], [true, false, true], needsPresent: true);

        Assert.Equal((1, 0), (separateGraphics, separatePresent));

        (int computeGraphics, _) = VkGraphicsDevice.ChooseQueueFamilies([QueueFlags.GraphicsBit, GraphicsCompute], [false, false], needsPresent: false);

        Assert.Equal(1, computeGraphics);
    }

    private static SurfaceFormatKHR Surface(Format format) => new(format, ColorSpaceKHR.SpaceSrgbNonlinearKhr);

    [Fact]
    public void AndroidRgbaSurfacesServeBothEncodings()
    {
        SurfaceFormatKHR[] android = [Surface(Format.R8G8B8A8Unorm), Surface(Format.R8G8B8A8Srgb), Surface(Format.R5G6B5UnormPack16)];

        Assert.Equal(Format.R8G8B8A8Srgb, VkSwapchain.ChooseSurfaceFormat(android, srgb: true)?.Format);
        Assert.Equal(Format.R8G8B8A8Unorm, VkSwapchain.ChooseSurfaceFormat(android, srgb: false)?.Format);

        SurfaceFormatKHR[] desktop = [Surface(Format.R8G8B8A8Srgb), Surface(Format.B8G8R8A8Srgb), Surface(Format.B8G8R8A8Unorm)];

        Assert.Equal(Format.B8G8R8A8Srgb, VkSwapchain.ChooseSurfaceFormat(desktop, srgb: true)?.Format);

        Assert.Null(VkSwapchain.ChooseSurfaceFormat([Surface(Format.A2B10G10R10UnormPack32)], srgb: true));
    }
}
