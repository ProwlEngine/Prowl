using System.Collections.Generic;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice
{
    private readonly object _initLock = new();
    private CommandPool _initPool;
    private Silk.NET.Vulkan.CommandBuffer _initCb;

    internal void ClearColorTexture(VkTexture texture, ClearColorValue color)
    {
        ImageSubresourceRange range = new(
             ImageAspectFlags.ColorBit,
             0,
             texture.MipLevels,
             0,
             texture.ActualArrayLayers);

        lock (_initLock)
        {
            Silk.NET.Vulkan.CommandBuffer cb = BeginInitCommands(texture, out CommandPool immediatePool);
            VkBarriers.Transition(this, cb, texture, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
            Vk.CmdClearColorImage(cb, texture.OptimalDeviceImage, ImageLayout.TransferDstOptimal, &color, 1, &range);
            VkBarriers.Transition(this, cb, texture, ImageLayout.TransferDstOptimal, VkBarriers.RestingLayout(texture));
            EndInitCommands(cb, immediatePool);
        }
    }

    internal void ClearDepthTexture(VkTexture texture, ClearDepthStencilValue clearValue)
    {
        ImageSubresourceRange range = new(
            texture.AspectMask,
            0,
            texture.MipLevels,
            0,
            texture.ActualArrayLayers);

        lock (_initLock)
        {
            Silk.NET.Vulkan.CommandBuffer cb = BeginInitCommands(texture, out CommandPool immediatePool);
            VkBarriers.Transition(this, cb, texture, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
            Vk.CmdClearDepthStencilImage(
                cb,
                texture.OptimalDeviceImage,
                ImageLayout.TransferDstOptimal,
                &clearValue,
                1,
                &range);
            VkBarriers.Transition(this, cb, texture, ImageLayout.TransferDstOptimal, VkBarriers.RestingLayout(texture));
            EndInitCommands(cb, immediatePool);
        }
    }

    internal void TransitionFromUndefined(VkTexture texture, ImageLayout layout)
    {
        lock (_initLock)
        {
            Silk.NET.Vulkan.CommandBuffer cb = BeginInitCommands(texture, out CommandPool immediatePool);
            VkBarriers.Transition(this, cb, texture, ImageLayout.Undefined, layout);
            EndInitCommands(cb, immediatePool);
        }
    }

    internal void FlushPendingInitCommands()
    {
        lock (_initLock)
        {
            if (_initPool.Handle == 0)
                return;

            CommandPool pool = _initPool;
            _initPool = default;
            EndAndSubmitImmediate(pool, _initCb);
        }
    }

    private Silk.NET.Vulkan.CommandBuffer BeginInitCommands(VkTexture texture, out CommandPool immediatePool)
    {
        if (texture.IsSwapchainTexture)
        {
            immediatePool = AcquireImmediatePool();
            return BeginImmediateCommandBuffer(immediatePool);
        }

        immediatePool = default;
        if (_initPool.Handle == 0)
        {
            _initPool = AcquireImmediatePool();
            _initCb = BeginImmediateCommandBuffer(_initPool);
        }

        return _initCb;
    }

    private void EndInitCommands(Silk.NET.Vulkan.CommandBuffer cb, CommandPool immediatePool)
    {
        if (immediatePool.Handle != 0)
            EndAndSubmitImmediate(immediatePool, cb);
    }
}
