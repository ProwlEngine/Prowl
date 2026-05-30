using System;
using System.Diagnostics;
using System.Linq;

using Silk.NET.Vulkan;

using VkApi = Silk.NET.Vulkan.Vk;


namespace Prowl.Veldrid.Vk;


public static class VkApiExtensions
{
    private static volatile bool s_isLoaded;

    public static unsafe bool IsLoaded(this VkApi vk)
    {
        if (s_isLoaded)
            return true;

        try
        {
            uint propCount;
            vk.EnumerateInstanceExtensionProperties((byte*)null, &propCount, null);
            return s_isLoaded = true;
        }
        catch
        {
            return s_isLoaded = false;
        }
    }


    public static unsafe string[] EnumerateInstanceExtensionProperties(this VkApi vk, ReadOnlySpan<byte> pLayerName)
    {
        fixed (byte* layerName = pLayerName)
            return EnumerateInstanceExtensionProperties(vk, layerName);
    }


    public static unsafe string[] EnumerateInstanceExtensionProperties(this VkApi vk, byte* pLayerName)
    {
        if (!vk.IsLoaded())
            return [];

        uint propCount = 0;
        Result result = vk.EnumerateInstanceExtensionProperties(pLayerName, ref propCount, null);

        if (result != Result.Success || propCount == 0)
            return [];

        ExtensionProperties* props = stackalloc ExtensionProperties[(int)propCount];
        vk.EnumerateInstanceExtensionProperties(pLayerName, ref propCount, props);

        string[] extensions = new string[propCount];
        for (int i = 0; i < propCount; i++)
            extensions[i] = Util.GetString(props[i].ExtensionName);

        return extensions;
    }


    public static unsafe string[] EnumerateInstanceLayers(this VkApi vk, ReadOnlySpan<LayerProperties> layerProperties)
    {
        fixed (LayerProperties* layerProps = layerProperties)
            return EnumerateInstanceLayers(vk, layerProps);
    }


    public static unsafe string[] EnumerateInstanceLayers(this VkApi vk, LayerProperties* layerProperties)
    {
        if (!vk.IsLoaded())
            return [];

        uint propCount = 0;
        vk.EnumerateInstanceLayerProperties(ref propCount, layerProperties).CheckResult();

        if (propCount == 0)
            return [];

        LayerProperties* props = stackalloc LayerProperties[(int)propCount];
        vk.EnumerateInstanceLayerProperties(ref propCount, props);

        string[] ret = new string[propCount];
        for (int i = 0; i < propCount; i++)
            ret[i] = Util.GetString(props[i].LayerName);

        return ret;
    }


    public static bool TryFindMemoryType(this VkApi vk, PhysicalDeviceMemoryProperties memProperties, uint typeFilter, MemoryPropertyFlags properties, out uint typeIndex)
    {
        typeIndex = 0;

        for (int i = 0; i < memProperties.MemoryTypeCount; i++)
        {
            if (((typeFilter & (1 << i)) != 0)
                && (memProperties.MemoryTypes[i].PropertyFlags & properties) == properties)
            {
                typeIndex = (uint)i;
                return true;
            }
        }

        return false;
    }


    public static unsafe void TransitionImageLayout(
        this VkApi vk,
        Silk.NET.Vulkan.CommandBuffer cb,
        Image image,
        uint baseMipLevel,
        uint levelCount,
        uint baseArrayLayer,
        uint layerCount,
        ImageAspectFlags aspectMask,
        ImageLayout oldLayout,
        ImageLayout newLayout)
    {
        Debug.Assert(oldLayout != newLayout);
        ImageMemoryBarrier barrier = new(sType: StructureType.ImageMemoryBarrier)
        {
            OldLayout = oldLayout,
            NewLayout = newLayout,
            SrcQueueFamilyIndex = VkApi.QueueFamilyIgnored,
            DstQueueFamilyIndex = VkApi.QueueFamilyIgnored,
            Image = image
        };

        barrier.SubresourceRange.AspectMask = aspectMask;
        barrier.SubresourceRange.BaseMipLevel = baseMipLevel;
        barrier.SubresourceRange.LevelCount = levelCount;
        barrier.SubresourceRange.BaseArrayLayer = baseArrayLayer;
        barrier.SubresourceRange.LayerCount = layerCount;

        PipelineStageFlags srcStageFlags = PipelineStageFlags.None;
        PipelineStageFlags dstStageFlags = PipelineStageFlags.None;

        if ((oldLayout == ImageLayout.Undefined || oldLayout == ImageLayout.Preinitialized) && newLayout == ImageLayout.TransferDstOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.None;
            barrier.DstAccessMask = AccessFlags.TransferWriteBit;
            srcStageFlags = PipelineStageFlags.TopOfPipeBit;
            dstStageFlags = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.ShaderReadOnlyOptimal && newLayout == ImageLayout.TransferSrcOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.ShaderReadBit;
            barrier.DstAccessMask = AccessFlags.TransferReadBit;
            srcStageFlags = PipelineStageFlags.FragmentShaderBit;
            dstStageFlags = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.ShaderReadOnlyOptimal && newLayout == ImageLayout.TransferDstOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.ShaderReadBit;
            barrier.DstAccessMask = AccessFlags.TransferWriteBit;
            srcStageFlags = PipelineStageFlags.FragmentShaderBit;
            dstStageFlags = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.Preinitialized && newLayout == ImageLayout.TransferSrcOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.None;
            barrier.DstAccessMask = AccessFlags.TransferReadBit;
            srcStageFlags = PipelineStageFlags.TopOfPipeBit;
            dstStageFlags = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.Preinitialized && newLayout == ImageLayout.General)
        {
            barrier.SrcAccessMask = AccessFlags.None;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;
            srcStageFlags = PipelineStageFlags.TopOfPipeBit;
            dstStageFlags = PipelineStageFlags.ComputeShaderBit;
        }
        else if (oldLayout == ImageLayout.Preinitialized && newLayout == ImageLayout.ShaderReadOnlyOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.None;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;
            srcStageFlags = PipelineStageFlags.TopOfPipeBit;
            dstStageFlags = PipelineStageFlags.FragmentShaderBit;
        }
        else if (oldLayout == ImageLayout.General && newLayout == ImageLayout.ShaderReadOnlyOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.TransferReadBit;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;
            srcStageFlags = PipelineStageFlags.TransferBit;
            dstStageFlags = PipelineStageFlags.FragmentShaderBit;
        }
        else if (oldLayout == ImageLayout.ShaderReadOnlyOptimal && newLayout == ImageLayout.General)
        {
            barrier.SrcAccessMask = AccessFlags.ShaderReadBit;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;
            srcStageFlags = PipelineStageFlags.FragmentShaderBit;
            dstStageFlags = PipelineStageFlags.ComputeShaderBit;
        }

        else if (oldLayout == ImageLayout.TransferSrcOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.TransferReadBit;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;
            srcStageFlags = PipelineStageFlags.TransferBit;
            dstStageFlags = PipelineStageFlags.FragmentShaderBit;
        }
        else if (oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;
            srcStageFlags = PipelineStageFlags.TransferBit;
            dstStageFlags = PipelineStageFlags.FragmentShaderBit;
        }
        else if (oldLayout == ImageLayout.TransferSrcOptimal && newLayout == ImageLayout.TransferDstOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.TransferReadBit;
            barrier.DstAccessMask = AccessFlags.TransferWriteBit;
            srcStageFlags = PipelineStageFlags.TransferBit;
            dstStageFlags = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.TransferSrcOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
            barrier.DstAccessMask = AccessFlags.TransferReadBit;
            srcStageFlags = PipelineStageFlags.TransferBit;
            dstStageFlags = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.ColorAttachmentOptimal && newLayout == ImageLayout.TransferSrcOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.ColorAttachmentWriteBit;
            barrier.DstAccessMask = AccessFlags.TransferReadBit;
            srcStageFlags = PipelineStageFlags.ColorAttachmentOutputBit;
            dstStageFlags = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.ColorAttachmentOptimal && newLayout == ImageLayout.TransferDstOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.ColorAttachmentWriteBit;
            barrier.DstAccessMask = AccessFlags.TransferWriteBit;
            srcStageFlags = PipelineStageFlags.ColorAttachmentOutputBit;
            dstStageFlags = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.ColorAttachmentOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.ColorAttachmentWriteBit;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;
            srcStageFlags = PipelineStageFlags.ColorAttachmentOutputBit;
            dstStageFlags = PipelineStageFlags.FragmentShaderBit;
        }
        else if (oldLayout == ImageLayout.DepthStencilAttachmentOptimal && newLayout == ImageLayout.ShaderReadOnlyOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.DepthStencilAttachmentWriteBit;
            barrier.DstAccessMask = AccessFlags.ShaderReadBit;
            srcStageFlags = PipelineStageFlags.LateFragmentTestsBit;
            dstStageFlags = PipelineStageFlags.FragmentShaderBit;
        }
        else if (oldLayout == ImageLayout.ColorAttachmentOptimal && newLayout == ImageLayout.PresentSrcKhr)
        {
            barrier.SrcAccessMask = AccessFlags.ColorAttachmentWriteBit;
            barrier.DstAccessMask = AccessFlags.MemoryReadBit;
            srcStageFlags = PipelineStageFlags.ColorAttachmentOutputBit;
            dstStageFlags = PipelineStageFlags.BottomOfPipeBit;
        }
        else if (oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.PresentSrcKhr)
        {
            barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
            barrier.DstAccessMask = AccessFlags.MemoryReadBit;
            srcStageFlags = PipelineStageFlags.TransferBit;
            dstStageFlags = PipelineStageFlags.BottomOfPipeBit;
        }
        else if (oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.ColorAttachmentOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
            barrier.DstAccessMask = AccessFlags.ColorAttachmentWriteBit;
            srcStageFlags = PipelineStageFlags.TransferBit;
            dstStageFlags = PipelineStageFlags.ColorAttachmentOutputBit;
        }
        else if (oldLayout == ImageLayout.TransferDstOptimal && newLayout == ImageLayout.DepthStencilAttachmentOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.TransferWriteBit;
            barrier.DstAccessMask = AccessFlags.DepthStencilAttachmentWriteBit;
            srcStageFlags = PipelineStageFlags.TransferBit;
            dstStageFlags = PipelineStageFlags.LateFragmentTestsBit;
        }
        else if (oldLayout == ImageLayout.General && newLayout == ImageLayout.TransferSrcOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.ShaderWriteBit;
            barrier.DstAccessMask = AccessFlags.TransferReadBit;
            srcStageFlags = PipelineStageFlags.ComputeShaderBit;
            dstStageFlags = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.General && newLayout == ImageLayout.TransferDstOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.ShaderWriteBit;
            barrier.DstAccessMask = AccessFlags.TransferWriteBit;
            srcStageFlags = PipelineStageFlags.ComputeShaderBit;
            dstStageFlags = PipelineStageFlags.TransferBit;
        }
        else if (oldLayout == ImageLayout.PresentSrcKhr && newLayout == ImageLayout.TransferSrcOptimal)
        {
            barrier.SrcAccessMask = AccessFlags.MemoryReadBit;
            barrier.DstAccessMask = AccessFlags.TransferReadBit;
            srcStageFlags = PipelineStageFlags.BottomOfPipeBit;
            dstStageFlags = PipelineStageFlags.TransferBit;
        }
        else
        {
            Debug.Fail("Invalid image layout transition.");
        }

        vk.CmdPipelineBarrier(
            cb,
            srcStageFlags,
            dstStageFlags,
            DependencyFlags.None,
            0, null,
            0, null,
            1, &barrier);
    }


    [Conditional("DEBUG")]
    public static void CheckResult(this Result result)
    {
        if (result != Result.Success)
            throw new RenderException("Unsuccessful VkResult: " + result);
    }
}
