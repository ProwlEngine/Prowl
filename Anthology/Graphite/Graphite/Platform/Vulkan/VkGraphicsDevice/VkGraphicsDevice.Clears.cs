using System.Collections.Generic;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice
{
    private readonly object _initLock = new();
    private readonly List<ResourceRefCount> _initRetained = [];
    private SharedCommandPool? _initPool;
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
            Silk.NET.Vulkan.CommandBuffer cb = BeginInitCommands(texture, out SharedCommandPool? immediatePool);
            VkBarriers.Transition(this, cb, texture, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
            Vk.CmdClearColorImage(cb, texture.OptimalDeviceImage, ImageLayout.TransferDstOptimal, &color, 1, &range);
            VkBarriers.Transition(this, cb, texture, ImageLayout.TransferDstOptimal, VkBarriers.RestingLayout(texture));
            EndInitCommands(texture, cb, immediatePool);
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
            Silk.NET.Vulkan.CommandBuffer cb = BeginInitCommands(texture, out SharedCommandPool? immediatePool);
            VkBarriers.Transition(this, cb, texture, ImageLayout.Undefined, ImageLayout.TransferDstOptimal);
            Vk.CmdClearDepthStencilImage(
                cb,
                texture.OptimalDeviceImage,
                ImageLayout.TransferDstOptimal,
                &clearValue,
                1,
                &range);
            VkBarriers.Transition(this, cb, texture, ImageLayout.TransferDstOptimal, VkBarriers.RestingLayout(texture));
            EndInitCommands(texture, cb, immediatePool);
        }
    }

    internal void TransitionFromUndefined(VkTexture texture, ImageLayout layout)
    {
        lock (_initLock)
        {
            Silk.NET.Vulkan.CommandBuffer cb = BeginInitCommands(texture, out SharedCommandPool? immediatePool);
            VkBarriers.Transition(this, cb, texture, ImageLayout.Undefined, layout);
            EndInitCommands(texture, cb, immediatePool);
        }
    }

    internal void FlushPendingInitCommands()
    {
        lock (_initLock)
        {
            if (_initPool is not { } pool)
                return;

            _initPool = null;
            pool.Retained.AddRange(_initRetained);
            _initRetained.Clear();
            pool.EndAndSubmit(_initCb, waitAcquire: false);
        }
    }

    private Silk.NET.Vulkan.CommandBuffer BeginInitCommands(VkTexture texture, out SharedCommandPool? immediatePool)
    {
        if (texture.IsSwapchainTexture)
        {
            immediatePool = GetFreeCommandPool();
            return immediatePool.BeginNewCommandBuffer();
        }

        immediatePool = null;
        if (_initPool == null)
        {
            _initPool = GetFreeCommandPool();
            _initCb = _initPool.BeginNewCommandBuffer();
        }

        return _initCb;
    }

    private void EndInitCommands(VkTexture texture, Silk.NET.Vulkan.CommandBuffer cb, SharedCommandPool? immediatePool)
    {
        if (immediatePool != null)
        {
            immediatePool.EndAndSubmit(cb);
            return;
        }

        texture.RefCount.Increment();
        _initRetained.Add(texture.RefCount);
    }
}
