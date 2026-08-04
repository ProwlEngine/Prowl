using System.Collections.Concurrent;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice
{
    private readonly ConcurrentDictionary<Format, Filter> _filters = new();

    public override TextureSampleCount GetSampleCountLimit(PixelFormat format, bool depthFormat)
    {
        ImageUsageFlags usageFlags = ImageUsageFlags.SampledBit;
        usageFlags |= depthFormat ? ImageUsageFlags.DepthStencilAttachmentBit : ImageUsageFlags.ColorAttachmentBit;

        Vk.GetPhysicalDeviceImageFormatProperties(
            PhysicalDevice,
            VkFormats.ToVkPixelFormat(format),
            ImageType.Type2D,
            ImageTiling.Optimal,
            usageFlags,
            ImageCreateFlags.None,
            out ImageFormatProperties formatProperties);

        SampleCountFlags vkSampleCounts = formatProperties.SampleCounts;
        if ((vkSampleCounts & SampleCountFlags.Count32Bit) == SampleCountFlags.Count32Bit)
        {
            return TextureSampleCount.Count32;
        }
        else if ((vkSampleCounts & SampleCountFlags.Count16Bit) == SampleCountFlags.Count16Bit)
        {
            return TextureSampleCount.Count16;
        }
        else if ((vkSampleCounts & SampleCountFlags.Count8Bit) == SampleCountFlags.Count8Bit)
        {
            return TextureSampleCount.Count8;
        }
        else if ((vkSampleCounts & SampleCountFlags.Count4Bit) == SampleCountFlags.Count4Bit)
        {
            return TextureSampleCount.Count4;
        }
        else if ((vkSampleCounts & SampleCountFlags.Count2Bit) == SampleCountFlags.Count2Bit)
        {
            return TextureSampleCount.Count2;
        }

        return TextureSampleCount.Count1;
    }

    private protected override bool GetPixelFormatSupportCore(
        PixelFormat format,
        TextureType type,
        TextureUsage usage,
        out PixelFormatProperties properties)
    {
        Format vkFormat = VkFormats.ToVkPixelFormat(format, (usage & TextureUsage.DepthStencil) != 0);
        ImageType vkType = VkFormats.ToVkTextureType(type);
        ImageTiling tiling = usage == TextureUsage.Staging ? ImageTiling.Linear : ImageTiling.Optimal;
        ImageUsageFlags vkUsage = VkFormats.ToVkTextureUsage(usage);

        Result result = Vk.GetPhysicalDeviceImageFormatProperties(
            PhysicalDevice,
            vkFormat,
            vkType,
            tiling,
            vkUsage,
            ImageCreateFlags.None,
            out ImageFormatProperties vkProps);

        if (result == Result.ErrorFormatNotSupported)
        {
            properties = default;
            return false;
        }

        result.CheckResult();

        properties = new PixelFormatProperties(
           vkProps.MaxExtent.Width,
           vkProps.MaxExtent.Height,
           vkProps.MaxExtent.Depth,
           vkProps.MaxMipLevels,
           vkProps.MaxArrayLayers,
           (uint)vkProps.SampleCounts);
        return true;
    }

    internal Filter GetFormatFilter(Format format)
    {
        if (!_filters.TryGetValue(format, out Filter filter))
        {
            Vk.GetPhysicalDeviceFormatProperties(PhysicalDevice, format, out FormatProperties vkFormatProps);
            filter = (vkFormatProps.OptimalTilingFeatures & FormatFeatureFlags.SampledImageFilterLinearBit) != 0
                ? Filter.Linear
                : Filter.Nearest;
            _filters.TryAdd(format, filter);
        }

        return filter;
    }
}
