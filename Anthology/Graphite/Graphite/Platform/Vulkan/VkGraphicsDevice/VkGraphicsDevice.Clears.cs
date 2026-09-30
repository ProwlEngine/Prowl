using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice
{
    internal void ClearColorTexture(VkTexture texture, ClearColorValue color)
    {
        ImageSubresourceRange range = new(
             ImageAspectFlags.ColorBit,
             0,
             texture.MipLevels,
             0,
             texture.ActualArrayLayers);
        SharedCommandPool pool = GetFreeCommandPool();
        Silk.NET.Vulkan.CommandBuffer cb = pool.BeginNewCommandBuffer();
        VkBarriers.Transition(this, cb, texture, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
        Vk.CmdClearColorImage(cb, texture.OptimalDeviceImage, ImageLayout.TransferDstOptimal, &color, 1, &range);
        VkBarriers.Transition(this, cb, texture, ImageLayout.TransferDstOptimal, VkBarriers.RestingLayout(texture));
        pool.EndAndSubmit(cb);
    }

    internal void ClearDepthTexture(VkTexture texture, ClearDepthStencilValue clearValue)
    {
        ImageSubresourceRange range = new(
            texture.AspectMask,
            0,
            texture.MipLevels,
            0,
            texture.ActualArrayLayers);
        SharedCommandPool pool = GetFreeCommandPool();
        Silk.NET.Vulkan.CommandBuffer cb = pool.BeginNewCommandBuffer();
        VkBarriers.Transition(this, cb, texture, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
        Vk.CmdClearDepthStencilImage(
            cb,
            texture.OptimalDeviceImage,
            ImageLayout.TransferDstOptimal,
            &clearValue,
            1,
            &range);
        VkBarriers.Transition(this, cb, texture, ImageLayout.TransferDstOptimal, VkBarriers.RestingLayout(texture));
        pool.EndAndSubmit(cb);
    }

    internal void TransitionFromUndefined(VkTexture texture, ImageLayout layout)
    {
        SharedCommandPool pool = GetFreeCommandPool();
        Silk.NET.Vulkan.CommandBuffer cb = pool.BeginNewCommandBuffer();
        VkBarriers.Transition(this, cb, texture, ImageLayout.Undefined, layout);
        pool.EndAndSubmit(cb);
    }
}
