using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsDevice
{
    private const uint MinStagingBufferSize = 64;
    private const uint MaxStagingBufferSize = 512;

    private const int SharedCommandPoolCount = 4;
    private readonly Stack<SharedCommandPool> _sharedGraphicsCommandPools = new();
    private readonly object _graphicsCommandPoolLock = new();

    internal readonly object _stagingResourcesLock = new();
    private readonly List<VkTexture> _availableStagingTextures = [];
    private readonly List<VkBuffer> _availableStagingBuffers = [];

    private readonly Dictionary<Silk.NET.Vulkan.CommandBuffer, VkTexture> _submittedStagingTextures
        = [];
    private readonly Dictionary<Silk.NET.Vulkan.CommandBuffer, VkBuffer> _submittedStagingBuffers
        = [];
    internal readonly Dictionary<Silk.NET.Vulkan.CommandBuffer, SharedCommandPool> _submittedSharedCommandPools
        = [];

    private protected override void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
    {
        VkBuffer vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(buffer);
        VkBuffer? copySrcVkBuffer = null;
        IntPtr mappedPtr;
        byte* destPtr;
        bool isPersistentMapped = vkBuffer.Memory.IsPersistentMapped;
        if (isPersistentMapped)
        {
            mappedPtr = (IntPtr)vkBuffer.Memory.BlockMappedPointer;
            destPtr = (byte*)mappedPtr + bufferOffsetInBytes;
        }
        else
        {
            copySrcVkBuffer = GetFreeStagingBuffer(sizeInBytes);
            mappedPtr = (IntPtr)copySrcVkBuffer.Memory.BlockMappedPointer;
            destPtr = (byte*)mappedPtr;
        }

        Unsafe.CopyBlock(destPtr, source.ToPointer(), sizeInBytes);

        if (!isPersistentMapped)
        {
            SharedCommandPool pool = GetFreeCommandPool();
            Silk.NET.Vulkan.CommandBuffer cb = pool.BeginNewCommandBuffer();

            BufferCopy copyRegion = new()
            {
                DstOffset = bufferOffsetInBytes,
                Size = sizeInBytes
            };
            _vk.CmdCopyBuffer(cb, copySrcVkBuffer!.DeviceBuffer, vkBuffer.DeviceBuffer, 1, in copyRegion);

            pool.EndAndSubmit(cb);
            lock (_stagingResourcesLock)
            {
                _submittedStagingBuffers.Add(cb, copySrcVkBuffer);
            }
        }
    }

    private SharedCommandPool GetFreeCommandPool()
    {
        lock (_graphicsCommandPoolLock)
        {
            if (_sharedGraphicsCommandPools.Count > 0)
                return _sharedGraphicsCommandPools.Pop();
        }

        return new SharedCommandPool(this, false);
    }

    private IntPtr MapBuffer(VkBuffer buffer, uint numBytes)
    {
        if (buffer.Memory.IsPersistentMapped)
        {
            return (IntPtr)buffer.Memory.BlockMappedPointer;
        }
        else
        {
            void* mappedPtr;
            _vk.MapMemory(Device, buffer.Memory.DeviceMemory, buffer.Memory.Offset, numBytes, 0, &mappedPtr).CheckResult();
            return (IntPtr)mappedPtr;
        }
    }

    private void UnmapBuffer(VkBuffer buffer)
    {
        if (!buffer.Memory.IsPersistentMapped)
        {
            _vk.UnmapMemory(Device, buffer.Memory.DeviceMemory);
        }
    }

    private protected override void UpdateTextureCore(
        Texture texture,
        IntPtr source,
        uint sizeInBytes,
        uint x,
        uint y,
        uint z,
        uint width,
        uint height,
        uint depth,
        uint mipLevel,
        uint arrayLayer)
    {
        VkTexture vkTex = Util.AssertSubtype<Texture, VkTexture>(texture);
        bool isStaging = (vkTex.Usage & TextureUsage.Staging) != 0;
        if (isStaging)
        {
            VkMemoryBlock memBlock = vkTex.Memory;
            uint subresource = texture.CalculateSubresource(mipLevel, arrayLayer);
            SubresourceLayout layout = vkTex.GetSubresourceLayout(subresource);
            byte* imageBasePtr = (byte*)memBlock.BlockMappedPointer + layout.Offset;

            uint srcRowPitch = FormatHelpers.GetRowPitch(width, texture.Format);
            uint srcDepthPitch = FormatHelpers.GetDepthPitch(srcRowPitch, height, texture.Format);
            Util.CopyTextureRegion(
                source.ToPointer(),
                0, 0, 0,
                srcRowPitch, srcDepthPitch,
                imageBasePtr,
                x, y, z,
                (uint)layout.RowPitch, (uint)layout.DepthPitch,
                width, height, depth,
                texture.Format);
        }
        else
        {
            VkTexture stagingTex = GetFreeStagingTexture(width, height, depth, texture.Format);
            UpdateTexture(stagingTex, source, sizeInBytes, 0, 0, 0, width, height, depth, 0, 0);
            SharedCommandPool pool = GetFreeCommandPool();
            Silk.NET.Vulkan.CommandBuffer cb = pool.BeginNewCommandBuffer();
            VkCommandBuffer.CopyTextureCore_VkCommandBuffer(
                _vk,
                cb,
                stagingTex, 0, 0, 0, 0, 0,
                texture, x, y, z, mipLevel, arrayLayer,
                width, height, depth, 1);
            lock (_stagingResourcesLock)
            {
                _submittedStagingTextures.Add(cb, stagingTex);
            }
            pool.EndAndSubmit(cb);
        }
    }

    private VkTexture GetFreeStagingTexture(uint width, uint height, uint depth, PixelFormat format)
    {
        uint totalSize = FormatHelpers.GetRegionSize(width, height, depth, format);
        lock (_stagingResourcesLock)
        {
            for (int i = 0; i < _availableStagingTextures.Count; i++)
            {
                VkTexture tex = _availableStagingTextures[i];
                if (tex.Memory.Size >= totalSize)
                {
                    _availableStagingTextures.RemoveAt(i);
                    tex.SetStagingDimensions(width, height, depth, format);
                    return tex;
                }
            }
        }

        uint texWidth = Math.Max(256, width);
        uint texHeight = Math.Max(256, height);
        VkTexture newTex = (VkTexture)ResourceFactory.CreateTexture(TextureDescription.Texture3D(
            texWidth, texHeight, depth, 1, format, TextureUsage.Staging));
        newTex.SetStagingDimensions(width, height, depth, format);

        return newTex;
    }

    private VkBuffer GetFreeStagingBuffer(uint size)
    {
        lock (_stagingResourcesLock)
        {
            for (int i = 0; i < _availableStagingBuffers.Count; i++)
            {
                VkBuffer buffer = _availableStagingBuffers[i];
                if (buffer.SizeInBytes >= size)
                {
                    _availableStagingBuffers.RemoveAt(i);
                    return buffer;
                }
            }
        }

        uint newBufferSize = Math.Max(MinStagingBufferSize, size);
        VkBuffer newBuffer = (VkBuffer)ResourceFactory.CreateBuffer(
            new BufferDescription(newBufferSize, BufferUsage.Staging));
        return newBuffer;
    }
}
