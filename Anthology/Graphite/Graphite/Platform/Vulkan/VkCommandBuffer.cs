using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using Prowl.Vector;

using Silk.NET.Vulkan;

using VkApi = Silk.NET.Vulkan.Vk;
using VkBufferHandle = Silk.NET.Vulkan.Buffer;
using VkImageHandle = Silk.NET.Vulkan.Image;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkCommandBuffer : CommandBuffer
{
    private readonly VkGraphicsDevice _gd;
    private CommandPool _pool;
    private Silk.NET.Vulkan.CommandBuffer _cb;
    private bool _destroyed;

    /// <summary>
    /// True if not mid-recording, so it can be reset and reused. Begun-but-not-ended cannot recycle, must dispose instead.
    /// </summary>
    internal bool CanRecycle => !_commandBufferBegun && !_destroyed;

    private bool _commandBufferBegun;
    private bool _commandBufferEnded;
    private Rect2D[] _scissorRects = Array.Empty<Rect2D>();

    private readonly List<VkTexture> _preDrawSampledImages = [];

    private readonly VkDescriptorBinder _descriptorBinder;

    // Execution-timing query pool for the current recording, taken by the submission path once
    // End() has written the closing timestamp. Never read back on this object after that point -
    // a later Begin() may reuse this wrapper for a new recording while the old one is still in flight.
    private QueryPool? _pendingTimingPool;

    // Graphics State
    private VkFramebufferBase _currentFramebuffer;
    private bool _currentFramebufferEverActive;
    private RenderPass _activeRenderPass;
    private VkPipelineCacheEntry _currentResolvedPipeline;
    private bool _hasResolvedPipeline;
    private PrimitiveTopology _resolvedTopology;

    private bool _newFramebuffer; // Render pass cycle state

    private string _name;

    public CommandPool CommandPool => _pool;
    public Silk.NET.Vulkan.CommandBuffer CommandBuffer => _cb;

    public ResourceRefCount RefCount { get; }

    public override bool IsDisposed => _destroyed;

    public VkCommandBuffer(VkGraphicsDevice gd, ref CommandBufferDescription description)
        : base(gd.Features, gd.UniformBufferMinOffsetAlignment, gd.StructuredBufferMinOffsetAlignment)
    {
        _gd = gd;
        CommandPoolCreateInfo poolCI = new()
        {
            SType = StructureType.CommandPoolCreateInfo,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
            QueueFamilyIndex = gd.GraphicsQueueIndex
        };
        _gd.Vk.CreateCommandPool(_gd.Device, in poolCI, null, out _pool).CheckResult();

        _cb = GetNextCommandBuffer();
        RefCount = new ResourceRefCount(DisposeCore);
        _descriptorBinder = new VkDescriptorBinder(this, gd);

        Constructor_RecordAllocation();
    }

    internal PropertySet ActiveProperties => _activeProperties;
    internal uint ActivePropertiesEpoch => _activePropertiesEpoch;
    internal void QueuePreDrawSampledImage(VkTexture tex) => _preDrawSampledImages.Add(tex);

    internal override void Begin()
    {
        if (_commandBufferBegun)
        {
            throw new RenderException(
                "CommandBuffer must be in its initial state, or End() must have been called, for Begin() to be valid to call.");
        }
        if (_commandBufferEnded)
        {
            _commandBufferEnded = false;
            HasEnded = false;
            _cb = GetNextCommandBuffer();
            if (_currentStagingInfo != null)
            {
                RecycleStagingInfo(_currentStagingInfo);
            }
        }

        _currentStagingInfo = GetStagingResourceInfo();

        CommandBufferBeginInfo beginInfo = new()
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };
        _gd.Vk.BeginCommandBuffer(_cb, in beginInfo);
        _commandBufferBegun = true;
        _pendingTimingPool = _gd.BeginTiming(_cb);

        ClearCachedState();
        _currentFramebuffer = null;
        _currentShaderProgram = null;
        _currentResolvedPipeline = default;
        _hasResolvedPipeline = false;
        _resolvedTopology = default;
        Util.ClearArray(_scissorRects);

        _currentComputeProgram = null;

        // A fresh recording binds into a fresh execution: previously-resolved descriptor sets and
        // transient UBO ranges belong to the prior execution and must not be reused.
        _descriptorBinder.ClearForNewRecording();
    }

    private protected override void DrawCore(uint vertexCount, uint instanceCount, uint vertexStart, uint instanceStart)
    {
        PreDrawCommand();
        BindVertexBuffersFromSource();
        _gd.Vk.CmdDraw(_cb, vertexCount, instanceCount, vertexStart, instanceStart);
    }

    private protected override void DrawIndexedCore(uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart)
    {
        PreDrawCommand();
        BindVertexBuffersFromSource();
        BindIndexBufferFromSource();
        _gd.Vk.CmdDrawIndexed(_cb, _currentIndexCount, instanceCount, indexStart, vertexOffset, instanceStart);
    }

    private protected override void DrawIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        PreDrawCommand();
        BindVertexBuffersFromSource();
        indirectBuffer.MarkInFlight(_gd, ExecutionId);
        VkBuffer vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(indirectBuffer);
        AddStagingResource(vkBuffer.RefCount);
        _gd.Vk.CmdDrawIndirect(_cb, vkBuffer.DeviceBuffer, offset, drawCount, stride);
    }

    private protected override void DrawIndexedIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        PreDrawCommand();
        BindVertexBuffersFromSource();
        BindIndexBufferFromSource();
        indirectBuffer.MarkInFlight(_gd, ExecutionId);
        VkBuffer vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(indirectBuffer);
        AddStagingResource(vkBuffer.RefCount);
        _gd.Vk.CmdDrawIndexedIndirect(_cb, vkBuffer.DeviceBuffer, offset, drawCount, stride);
    }

    private void BindVertexBuffersFromSource()
    {
        IReadOnlyList<VertexLayoutDescription> layouts = _currentShaderProgram.VertexLayouts;
        int count = layouts.Count;
        if (count == 0) return;

        VkBufferHandle* buffers = stackalloc VkBufferHandle[count];
        ulong* offsets = stackalloc ulong[count];

        for (int slot = 0; slot < count; slot++)
        {
            VertexLayoutDescription layout = layouts[slot];
            _currentVertexSource!.ResolveSlot((uint)slot, in layout, out VertexBinding binding);
            CheckVertexBindingUsage(in binding, (uint)slot);
            binding.Buffer.MarkInFlight(_gd, ExecutionId);

            VkBuffer vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(binding.Buffer);
            buffers[slot] = vkBuffer.DeviceBuffer;
            offsets[slot] = binding.Offset;

            AddStagingResource(vkBuffer.RefCount);
        }

        _gd.Vk.CmdBindVertexBuffers(_cb, 0u, (uint)count, buffers, offsets);
    }

    private void BindIndexBufferFromSource()
    {
        bool has = _currentVertexSource!.TryGetIndexBuffer(out DeviceBuffer ib, out IndexFormat fmt, out uint indexCount);
        _currentIndexCount = indexCount;
        DrawIndexed_AssertIndexBufferResolved(has);
        CheckIndexBufferUsage(ib);
        ib.MarkInFlight(_gd, ExecutionId);

        VkBuffer vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(ib);
        _gd.Vk.CmdBindIndexBuffer(_cb, vkBuffer.DeviceBuffer, 0, VkFormats.VdToVkIndexFormat(fmt));
        AddStagingResource(vkBuffer.RefCount);
    }

    private void PreDrawCommand()
    {
        TransitionImages(_preDrawSampledImages, ImageLayout.ShaderReadOnlyOptimal);
        _preDrawSampledImages.Clear();

        ResolveAndBindGraphicsPipeline();

        // Resolve + transition property textures (must precede the render pass) and prepare descriptor
        // sets. Returns false when nothing changed since the last draw and the sets are still bound.
        bool renderPassActive = _activeRenderPass.Handle != default;
        bool needBind = _descriptorBinder.Prepare(
            _currentShaderProgram,
            reportProgram: _currentShaderProgram,
            isCompute: false,
            isGraphics: true,
            renderPassActive);

        EnsureRenderPassActive();

        if (needBind)
            _descriptorBinder.EmitBind(_currentResolvedPipeline.PipelineLayout, PipelineBindPoint.Graphics);
    }

    private void ResolveAndBindGraphicsPipeline()
    {
        PrimitiveTopology srcTopology = _currentVertexSource!.Topology;

        if (_hasResolvedPipeline && _resolvedTopology == srcTopology) return;

        if (_currentShaderProgram == null || _currentFramebuffer == null)
        {
            throw new RenderException("Cannot draw: no graphics GraphicsProgram or Framebuffer bound.");
        }

        VkPipelineCacheKey key = new(
            _framebufferOutputs!.Value,
            srcTopology);

        _currentResolvedPipeline = _currentShaderProgram.GetOrAddPipeline(in key);
        _resolvedTopology = srcTopology;
        _hasResolvedPipeline = true;

        _gd.Vk.CmdBindPipeline(_cb, PipelineBindPoint.Graphics, _currentResolvedPipeline.Pipeline);
    }

    private void TransitionImages(List<VkTexture> sampledTextures, ImageLayout layout)
    {
        for (int i = 0; i < sampledTextures.Count; i++)
        {
            VkTexture tex = sampledTextures[i];
            tex.TransitionImageLayout(_cb, 0, tex.MipLevels, 0, tex.ActualArrayLayers, layout);
        }
    }

    private protected override void DispatchCore(uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        PreDispatchCommand();

        _gd.Vk.CmdDispatch(_cb, groupCountX, groupCountY, groupCountZ);
    }

    private void PreDispatchCommand()
    {
        EnsureNoRenderPass();

        TransitionImages(_preDrawSampledImages, ImageLayout.ShaderReadOnlyOptimal);
        _preDrawSampledImages.Clear();

        bool renderPassActive = _activeRenderPass.Handle != default;
        bool needBind = _descriptorBinder.Prepare(
            _currentComputeProgram,
            reportProgram: _currentShaderProgram,
            isCompute: true,
            isGraphics: false,
            renderPassActive);

        if (needBind)
            _descriptorBinder.EmitBind(_currentComputeProgram.PipelineLayout, PipelineBindPoint.Compute);
    }

    private protected override void DispatchIndirectCore(DeviceBuffer indirectBuffer, uint offset)
    {
        PreDispatchCommand();

        indirectBuffer.MarkInFlight(_gd, ExecutionId);
        VkBuffer vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(indirectBuffer);
        AddStagingResource(vkBuffer.RefCount);
        _gd.Vk.CmdDispatchIndirect(_cb, vkBuffer.DeviceBuffer, offset);
    }

    protected override void ResolveTextureCore(Texture source, Texture destination)
    {
        if (_activeRenderPass.Handle != default)
        {
            EndCurrentRenderPass();
        }

        VkTexture vkSource = Util.AssertSubtype<Texture, VkTexture>(source);
        AddStagingResource(vkSource.RefCount);
        VkTexture vkDestination = Util.AssertSubtype<Texture, VkTexture>(destination);
        AddStagingResource(vkDestination.RefCount);
        ImageAspectFlags aspectFlags = ((source.Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil)
            ? ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit
            : ImageAspectFlags.ColorBit;
        ImageResolve region = new()
        {
            Extent = new Extent3D { Width = source.Width, Height = source.Height, Depth = source.Depth },
            SrcSubresource = new ImageSubresourceLayers { LayerCount = 1, AspectMask = aspectFlags },
            DstSubresource = new ImageSubresourceLayers { LayerCount = 1, AspectMask = aspectFlags }
        };

        vkSource.TransitionImageLayout(_cb, 0, 1, 0, 1, ImageLayout.TransferSrcOptimal);
        vkDestination.TransitionImageLayout(_cb, 0, 1, 0, 1, ImageLayout.TransferDstOptimal);

        _gd.Vk.CmdResolveImage(
            _cb,
            vkSource.OptimalDeviceImage,
             ImageLayout.TransferSrcOptimal,
            vkDestination.OptimalDeviceImage,
            ImageLayout.TransferDstOptimal,
            1,
            in region);

        if ((vkDestination.Usage & TextureUsage.Sampled) != 0)
        {
            vkDestination.TransitionImageLayout(_cb, 0, 1, 0, 1, ImageLayout.ShaderReadOnlyOptimal);
        }
    }

    internal override void End()
    {
        if (!_commandBufferBegun)
        {
            throw new RenderException("CommandBuffer must have been started before End() may be called.");
        }

        _commandBufferBegun = false;
        _commandBufferEnded = true;
        HasEnded = true;

        if (!_currentFramebufferEverActive && _currentFramebuffer != null)
        {
            BeginCurrentRenderPass();
        }

        if (_activeRenderPass.Handle != default)
        {
            EndCurrentRenderPass();
            _currentFramebuffer!.TransitionToFinalLayout(_cb);
        }

        _gd.EndTiming(_cb, _pendingTimingPool);
        _gd.Vk.EndCommandBuffer(_cb);
        _submittedCommandBuffers.Add(_cb);
    }

    // Reads and clears the timing pool End() wrote into, for the submission path to attach to
    // this specific submission. Must be called before any later Begin() on this wrapper.
    internal QueryPool? TakePendingTimingPool()
    {
        QueryPool? pool = _pendingTimingPool;
        _pendingTimingPool = null;
        return pool;
    }

    private protected override void SetVertexSourceCore(IVertexSource source)
    {
        _hasResolvedPipeline = false;
    }

    private VkGraphicsProgram _currentShaderProgram;
    private VkComputeProgram _currentComputeProgram;

    private protected override void SetShaderCore(GraphicsProgram program)
    {
        VkGraphicsProgram sp = Util.AssertSubtype<GraphicsProgram, VkGraphicsProgram>(program);
        if (_currentShaderProgram == sp) return;

        _currentShaderProgram = sp;
        _hasResolvedPipeline = false;
        AddStagingResource(sp.RefCount);
    }

    private protected override void SetComputeShaderCore(ComputeProgram program)
    {
        VkComputeProgram cp = Util.AssertSubtype<ComputeProgram, VkComputeProgram>(program);
        _currentComputeProgram = cp;
        _gd.Vk.CmdBindPipeline(_cb, PipelineBindPoint.Compute, cp.DevicePipeline);
        AddStagingResource(cp.RefCount);
    }

    private protected override void SetPropertiesCore(PropertySet properties) { }

    // Sets are content-addressed in the cache, so clearing needs no invalidation here.
    private protected override void ClearPropertiesCore() { }

    public override void SetScissorRect(uint index, uint x, uint y, uint width, uint height)
    {
        if (index == 0 || _gd.Features.MultipleViewports)
        {
            Rect2D scissor = new(new Offset2D((int)x, (int)y), new Extent2D(width, height));
            if (!scissor.Equals(_scissorRects[index]))
            {
                _scissorRects[index] = scissor;
                _gd.Vk.CmdSetScissor(_cb, index, 1, in scissor);
            }
        }
    }

    public override void SetViewport(uint index, ref Viewport viewport)
    {
        if (index == 0 || _gd.Features.MultipleViewports)
        {
            float vpY = _gd.IsClipSpaceYInverted
                ? viewport.Y
                : viewport.Height + viewport.Y;
            float vpHeight = _gd.IsClipSpaceYInverted
                ? viewport.Height
                : -viewport.Height;

            Silk.NET.Vulkan.Viewport vkViewport = new()
            {
                X = viewport.X,
                Y = vpY,
                Width = viewport.Width,
                Height = vpHeight,
                MinDepth = viewport.MinDepth,
                MaxDepth = viewport.MaxDepth
            };

            _gd.Vk.CmdSetViewport(_cb, index, 1, in vkViewport);
        }
    }

    private protected override void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
    {
        VkBuffer stagingBuffer = GetStagingBuffer(sizeInBytes);
        _gd.UpdateBuffer(stagingBuffer, 0, source, sizeInBytes);
        CopyBuffer(stagingBuffer, 0, buffer, bufferOffsetInBytes, sizeInBytes);
        buffer.MarkInFlight(_gd, ExecutionId);
    }

    [Conditional("DEBUG")]
    private void DebugFullPipelineBarrier()
    {
        MemoryBarrier memoryBarrier = new()
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.IndirectCommandReadBit |
                   AccessFlags.IndexReadBit |
                   AccessFlags.VertexAttributeReadBit |
                   AccessFlags.UniformReadBit |
                   AccessFlags.InputAttachmentReadBit |
                   AccessFlags.ShaderReadBit |
                   AccessFlags.ShaderWriteBit |
                   AccessFlags.ColorAttachmentReadBit |
                   AccessFlags.ColorAttachmentWriteBit |
                   AccessFlags.DepthStencilAttachmentReadBit |
                   AccessFlags.DepthStencilAttachmentWriteBit |
                   AccessFlags.TransferReadBit |
                   AccessFlags.TransferWriteBit |
                   AccessFlags.HostReadBit |
                   AccessFlags.HostWriteBit,
            DstAccessMask = AccessFlags.IndirectCommandReadBit |
                   AccessFlags.IndexReadBit |
                   AccessFlags.VertexAttributeReadBit |
                   AccessFlags.UniformReadBit |
                   AccessFlags.InputAttachmentReadBit |
                   AccessFlags.ShaderReadBit |
                   AccessFlags.ShaderWriteBit |
                   AccessFlags.ColorAttachmentReadBit |
                   AccessFlags.ColorAttachmentWriteBit |
                   AccessFlags.DepthStencilAttachmentReadBit |
                   AccessFlags.DepthStencilAttachmentWriteBit |
                   AccessFlags.TransferReadBit |
                   AccessFlags.TransferWriteBit |
                   AccessFlags.HostReadBit |
                   AccessFlags.HostWriteBit
        };

        _gd.Vk.CmdPipelineBarrier(
            _cb,
            PipelineStageFlags.AllCommandsBit, // srcStageMask
            PipelineStageFlags.AllCommandsBit, // dstStageMask
            0,
            1,                                  // memoryBarrierCount
            &memoryBarrier,                     // pMemoryBarriers
            0, null,
            0, null);
    }

    public override string Name
    {
        get => _name;
        set
        {
            _name = value;
            _gd.SetResourceName(this, value);
        }
    }

    private protected override void PushDebugGroupCore(string name)
    {
        vkCmdDebugMarkerBeginEXT_t func = _gd.MarkerBegin;
        if (func == null) { return; }

        DebugMarkerMarkerInfoEXT markerInfo = new() { SType = StructureType.DebugMarkerMarkerInfoExt };

        byte* utf8Ptr = stackalloc byte[Utf8Stack.ByteCount(name)];
        Utf8Stack.Write(name, utf8Ptr);

        markerInfo.PMarkerName = utf8Ptr;

        func(_cb, &markerInfo);
    }

    private protected override void PopDebugGroupCore()
    {
        vkCmdDebugMarkerEndEXT_t func = _gd.MarkerEnd;
        if (func == null) { return; }

        func(_cb);
    }

    private protected override void InsertDebugMarkerCore(string name)
    {
        vkCmdDebugMarkerInsertEXT_t func = _gd.MarkerInsert;
        if (func == null) { return; }

        DebugMarkerMarkerInfoEXT markerInfo = new() { SType = StructureType.DebugMarkerMarkerInfoExt };

        byte* utf8Ptr = stackalloc byte[Utf8Stack.ByteCount(name)];
        Utf8Stack.Write(name, utf8Ptr);

        markerInfo.PMarkerName = utf8Ptr;

        func(_cb, &markerInfo);
    }

    public override void Dispose()
    {
        RefCount.Decrement();
    }

    private void DisposeCore()
    {
        if (!_destroyed)
        {
            _destroyed = true;
            _gd.Vk.DestroyCommandPool(_gd.Device, _pool, null);

            Debug.Assert(_submittedStagingInfos.Count == 0);

            foreach (VkBuffer buffer in _availableStagingBuffers)
            {
                buffer.Dispose();
            }

            DisposeCore_RecordFree();
        }
    }
}
