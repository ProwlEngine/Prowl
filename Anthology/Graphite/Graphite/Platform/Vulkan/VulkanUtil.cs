using System;
using System.Diagnostics;

using Silk.NET.Vulkan;

using VkApi = Silk.NET.Vulkan.Vk;

namespace Prowl.Graphite.Vk;

internal unsafe static class VulkanUtilUgley
{
    public static void TransitionImageLayout(
        VkApi vk,
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
}
