using System;
using System.Buffers;
using Silk.NET.Vulkan;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using VkApi = Silk.NET.Vulkan.Vk;
using VkBufferHandle = Silk.NET.Vulkan.Buffer;
using VkImageHandle = Silk.NET.Vulkan.Image;
using Prowl.Vector;

namespace Prowl.Veldrid.Vk;

internal unsafe class VkCommandBuffer : CommandBuffer
{
    private readonly VkGraphicsDevice _gd;
    private CommandPool _pool;
    private Silk.NET.Vulkan.CommandBuffer _cb;
    private bool _destroyed;

    private bool _commandBufferBegun;
    private bool _commandBufferEnded;
    private Rect2D[] _scissorRects = Array.Empty<Rect2D>();

    private ClearValue[] _clearValues = Array.Empty<ClearValue>();
    private bool[] _validColorClearValues = Array.Empty<bool>();
    private ClearValue? _depthClearValue;
    private readonly List<VkTexture> _preDrawSampledImages = [];

    // Graphics State
    private VkFramebufferBase _currentFramebuffer;
    private bool _currentFramebufferEverActive;
    private RenderPass _activeRenderPass;
    private VkPipelineCacheEntry _currentResolvedPipeline;
    private bool _hasResolvedPipeline;
    private PrimitiveTopology _resolvedTopology;
    private uint _currentIndexCount;

    private bool _newFramebuffer; // Render pass cycle state

    // Resource bind cache: (programKey, setIndex, mergedResourceVersion) -> DescriptorSet
    private readonly Dictionary<(object, int, uint), DescriptorSet> _resourceBindCache = [];

    // Tracks the VkBuffer handle written into each cached descriptor set's UBO bindings.
    // Key: (programKey, setIndex); Value: backing VkBuffer handles per UBO element.
    // Used to detect transient overflow (allocator switched to a new backing buffer).
    private readonly Dictionary<(object, int), VkBufferHandle[]> _uboBackingBuffers = [];

    private string _name;

    private readonly object _commandBufferListLock = new();
    private readonly Queue<Silk.NET.Vulkan.CommandBuffer> _availableCommandBuffers = new();
    private readonly List<Silk.NET.Vulkan.CommandBuffer> _submittedCommandBuffers = [];

    private StagingResourceInfo _currentStagingInfo;
    private readonly object _stagingLock = new();
    private readonly Dictionary<Silk.NET.Vulkan.CommandBuffer, StagingResourceInfo> _submittedStagingInfos = [];
    private readonly List<StagingResourceInfo> _availableStagingInfos = [];
    private readonly List<VkBuffer> _availableStagingBuffers = [];

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
    }

    private Silk.NET.Vulkan.CommandBuffer GetNextCommandBuffer()
    {
        lock (_commandBufferListLock)
        {
            if (_availableCommandBuffers.Count > 0)
            {
                Silk.NET.Vulkan.CommandBuffer cachedCB = _availableCommandBuffers.Dequeue();
                _gd.Vk.ResetCommandBuffer(cachedCB, 0).CheckResult();
                return cachedCB;
            }
        }

        CommandBufferAllocateInfo cbAI = new()
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _pool,
            CommandBufferCount = 1,
            Level = CommandBufferLevel.Primary
        };
        _gd.Vk.AllocateCommandBuffers(_gd.Device, in cbAI, out Silk.NET.Vulkan.CommandBuffer cb).CheckResult();
        return cb;
    }

    public void CommandBufferSubmitted(Silk.NET.Vulkan.CommandBuffer cb)
    {
        RefCount.Increment();
        foreach (ResourceRefCount rrc in _currentStagingInfo.Resources)
        {
            rrc.Increment();
        }

        lock (_stagingLock)
        {
            _submittedStagingInfos.Add(cb, _currentStagingInfo);
        }
        _currentStagingInfo = null;
    }

    public void CommandBufferCompleted(Silk.NET.Vulkan.CommandBuffer completedCB)
    {

        lock (_commandBufferListLock)
        {
            for (int i = 0; i < _submittedCommandBuffers.Count; i++)
            {
                Silk.NET.Vulkan.CommandBuffer submittedCB = _submittedCommandBuffers[i];
                if (submittedCB.Handle == completedCB.Handle)
                {
                    _availableCommandBuffers.Enqueue(completedCB);
                    _submittedCommandBuffers.RemoveAt(i);
                    i -= 1;
                }
            }
        }

        lock (_stagingLock)
        {
            if (_submittedStagingInfos.Remove(completedCB, out StagingResourceInfo? info))
            {
                RecycleStagingInfo(info);
            }
        }

        RefCount.Decrement();
    }

    public override void Begin()
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

        ClearCachedState();
        _resourceBindCache.Clear();
        _uboBackingBuffers.Clear();
        _currentFramebuffer = null;
        _currentShaderProgram = null;
        _currentResolvedPipeline = default;
        _hasResolvedPipeline = false;
        _resolvedTopology = default;
        Util.ClearArray(_scissorRects);

        _currentComputeProgram = null;
    }

    private protected override void ClearColorTargetCore(uint index, Color clearColor)
    {
        ClearValue clearValue = new()
        {
            Color = new ClearColorValue(clearColor.R, clearColor.G, clearColor.B, clearColor.A)
        };

        if (_activeRenderPass.Handle != default)
        {
            ClearAttachment clearAttachment = new()
            {
                ColorAttachment = index,
                AspectMask = ImageAspectFlags.ColorBit,
                ClearValue = clearValue
            };

            Texture colorTex = _currentFramebuffer.ColorTargets[(int)index].Target;
            ClearRect clearRect = new()
            {
                BaseArrayLayer = 0,
                LayerCount = 1,
                Rect = new Rect2D(new Offset2D(0, 0), new Extent2D(colorTex.Width, colorTex.Height))
            };

            _gd.Vk.CmdClearAttachments(_cb, 1, in clearAttachment, 1, in clearRect);
        }
        else
        {
            // Queue up the clear value for the next RenderPass.
            _clearValues[index] = clearValue;
            _validColorClearValues[index] = true;
        }
    }

    private protected override void ClearDepthStencilCore(float depth, byte stencil)
    {
        ClearValue clearValue = new()
        {
            DepthStencil = new ClearDepthStencilValue(depth, stencil)
        };

        if (_activeRenderPass.Handle != default)
        {
            ImageAspectFlags aspect = FormatHelpers.IsStencilFormat(_currentFramebuffer.DepthTarget.Value.Target.Format)
                ? ImageAspectFlags.DepthBit | ImageAspectFlags.StencilBit
                : ImageAspectFlags.DepthBit;
            ClearAttachment clearAttachment = new()
            {
                AspectMask = aspect,
                ClearValue = clearValue
            };

            uint renderableWidth = _currentFramebuffer.RenderableWidth;
            uint renderableHeight = _currentFramebuffer.RenderableHeight;
            if (renderableWidth > 0 && renderableHeight > 0)
            {
                ClearRect clearRect = new()
                {
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                    Rect = new Rect2D(new Offset2D(0, 0), new Extent2D(renderableWidth, renderableHeight))
                };

                _gd.Vk.CmdClearAttachments(_cb, 1, in clearAttachment, 1, in clearRect);
            }
        }
        else
        {
            // Queue up the clear value for the next RenderPass.
            _depthClearValue = clearValue;
        }
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
        VkBuffer vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(indirectBuffer);
        _currentStagingInfo.Resources.Add(vkBuffer.RefCount);
        _gd.Vk.CmdDrawIndirect(_cb, vkBuffer.DeviceBuffer, offset, drawCount, stride);
    }

    private protected override void DrawIndexedIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        PreDrawCommand();
        BindVertexBuffersFromSource();
        BindIndexBufferFromSource();
        VkBuffer vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(indirectBuffer);
        _currentStagingInfo.Resources.Add(vkBuffer.RefCount);
        _gd.Vk.CmdDrawIndexedIndirect(_cb, vkBuffer.DeviceBuffer, offset, drawCount, stride);
    }

    private void BindVertexBuffersFromSource()
    {
        System.Collections.Generic.IReadOnlyList<VertexLayoutDescription> layouts = _currentShaderProgram.VertexLayouts;
        int count = layouts.Count;
        if (count == 0) return;

        VkBufferHandle* buffers = stackalloc VkBufferHandle[count];
        ulong* offsets = stackalloc ulong[count];

        for (int slot = 0; slot < count; slot++)
        {
            VertexLayoutDescription layout = layouts[slot];
            _currentVertexSource.ResolveSlot((uint)slot, in layout, out VertexBinding binding);
            CheckVertexBindingUsage(in binding, (uint)slot);

            VkBuffer vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(binding.Buffer);
            buffers[slot] = vkBuffer.DeviceBuffer;
            offsets[slot] = binding.Offset;

            _currentStagingInfo.Resources.Add(vkBuffer.RefCount);
        }

        _gd.Vk.CmdBindVertexBuffers(_cb, 0u, (uint)count, buffers, offsets);
    }

    private void BindIndexBufferFromSource()
    {
        bool has = _currentVertexSource.TryGetIndexBuffer(out DeviceBuffer ib, out IndexFormat fmt, out uint indexCount);
        _currentIndexCount = indexCount;
        Debug.Assert(has, "Validation must have already trapped a missing index buffer on indexed-draw paths.");
        CheckIndexBufferUsage(ib);

        VkBuffer vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(ib);
        _gd.Vk.CmdBindIndexBuffer(_cb, vkBuffer.DeviceBuffer, 0, VkFormats.VdToVkIndexFormat(fmt));
        _currentStagingInfo.Resources.Add(vkBuffer.RefCount);
    }

    private void PreDrawCommand()
    {
        TransitionImages(_preDrawSampledImages, ImageLayout.ShaderReadOnlyOptimal);
        _preDrawSampledImages.Clear();

        ResolveAndBindGraphicsPipeline();

        // Transition property textures to the right layout before the render pass begins.
        TransitionPropertyTextures(isCompute: false);

        EnsureRenderPassActive();

        BindPropertySets(
            _currentShaderProgram,
            _currentShaderProgram.ResourceLayoutsArray,
            _currentShaderProgram.DescriptorSetLayouts,
            _currentShaderProgram.PerSetCounts,
            _currentShaderProgram.ResourceSetCount,
            _currentShaderProgram.TotalDynamicUboCount,
            _currentResolvedPipeline.PipelineLayout,
            PipelineBindPoint.Graphics,
            isCompute: false);
    }

    private void ResolveAndBindGraphicsPipeline()
    {
        PrimitiveTopology srcTopology = _currentVertexSource.Topology;

        if (_hasResolvedPipeline && _resolvedTopology == srcTopology) return;

        if (_currentShaderProgram == null || _currentFramebuffer == null)
        {
            throw new RenderException("Cannot draw: no graphics ShaderProgram or Framebuffer bound.");
        }

        VkPipelineCacheKey key = new(
            _framebufferOutputs,
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

    public override void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        PreDispatchCommand();

        _gd.Vk.CmdDispatch(_cb, groupCountX, groupCountY, groupCountZ);
    }

    private void PreDispatchCommand()
    {
        EnsureNoRenderPass();

        TransitionImages(_preDrawSampledImages, ImageLayout.ShaderReadOnlyOptimal);
        _preDrawSampledImages.Clear();

        TransitionPropertyTextures(isCompute: true);

        BindPropertySets(
            _currentComputeProgram,
            _currentComputeProgram.ResourceLayoutsArray,
            _currentComputeProgram.DescriptorSetLayouts,
            _currentComputeProgram.PerSetCounts,
            _currentComputeProgram.ResourceSetCount,
            _currentComputeProgram.TotalDynamicUboCount,
            _currentComputeProgram.PipelineLayout,
            PipelineBindPoint.Compute,
            isCompute: true);
    }

    private protected override void DispatchIndirectCore(DeviceBuffer indirectBuffer, uint offset)
    {
        PreDispatchCommand();

        VkBuffer vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(indirectBuffer);
        _currentStagingInfo.Resources.Add(vkBuffer.RefCount);
        _gd.Vk.CmdDispatchIndirect(_cb, vkBuffer.DeviceBuffer, offset);
    }

    protected override void ResolveTextureCore(Texture source, Texture destination)
    {
        if (_activeRenderPass.Handle != default)
        {
            EndCurrentRenderPass();
        }

        VkTexture vkSource = Util.AssertSubtype<Texture, VkTexture>(source);
        _currentStagingInfo.Resources.Add(vkSource.RefCount);
        VkTexture vkDestination = Util.AssertSubtype<Texture, VkTexture>(destination);
        _currentStagingInfo.Resources.Add(vkDestination.RefCount);
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

    public override void End()
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
            _currentFramebuffer.TransitionToFinalLayout(_cb);
        }

        _gd.Vk.EndCommandBuffer(_cb);
        _submittedCommandBuffers.Add(_cb);
    }

    private protected override void SetFramebufferCore(Framebuffer fb)
    {
        if (_activeRenderPass.Handle != default)
        {
            EndCurrentRenderPass();
        }
        else if (!_currentFramebufferEverActive && _currentFramebuffer != null)
        {
            // This forces any queued up texture clears to be emitted.
            BeginCurrentRenderPass();
            EndCurrentRenderPass();
        }

        if (_currentFramebuffer != null)
        {
            _currentFramebuffer.TransitionToFinalLayout(_cb);
        }

        VkFramebufferBase vkFB = Util.AssertSubtype<Framebuffer, VkFramebufferBase>(fb);
        _currentFramebuffer = vkFB;
        _currentFramebufferEverActive = false;
        _newFramebuffer = true;
        _hasResolvedPipeline = false;
        Util.EnsureArrayMinimumSize(ref _scissorRects, Math.Max(1, (uint)vkFB.ColorTargets.Count));
        uint clearValueCount = (uint)vkFB.ColorTargets.Count;
        Util.EnsureArrayMinimumSize(ref _clearValues, clearValueCount + 1); // Leave an extra space for the depth value (tracked separately).
        Util.ClearArray(_validColorClearValues);
        Util.EnsureArrayMinimumSize(ref _validColorClearValues, clearValueCount);
        _currentStagingInfo.Resources.Add(vkFB.RefCount);

        if (fb is VkSwapchainFramebuffer scFB)
        {
            _currentStagingInfo.Resources.Add(scFB.Swapchain.RefCount);
        }
    }

    private void EnsureRenderPassActive()
    {
        if (_activeRenderPass.Handle == default)
        {
            BeginCurrentRenderPass();
        }
    }

    private void EnsureNoRenderPass()
    {
        if (_activeRenderPass.Handle != default)
        {
            EndCurrentRenderPass();
        }
    }

    private void BeginCurrentRenderPass()
    {
        Debug.Assert(_activeRenderPass.Handle == default);
        Debug.Assert(_currentFramebuffer != null);
        _currentFramebufferEverActive = true;

        uint attachmentCount = _currentFramebuffer.AttachmentCount;
        bool haveAnyAttachments = _currentFramebuffer.ColorTargets.Count > 0 || _currentFramebuffer.DepthTarget != null;
        bool haveAllClearValues = _depthClearValue.HasValue || _currentFramebuffer.DepthTarget == null;
        bool haveAnyClearValues = _depthClearValue.HasValue;
        for (int i = 0; i < _currentFramebuffer.ColorTargets.Count; i++)
        {
            if (!_validColorClearValues[i])
            {
                haveAllClearValues = false;
            }
            else
            {
                haveAnyClearValues = true;
            }
        }

        RenderPassBeginInfo renderPassBI = new()
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(_currentFramebuffer.RenderableWidth, _currentFramebuffer.RenderableHeight)),
            Framebuffer = _currentFramebuffer.CurrentFramebuffer
        };

        if (!haveAnyAttachments || !haveAllClearValues)
        {
            renderPassBI.RenderPass = _newFramebuffer
                ? _currentFramebuffer.RenderPassNoClear_Init
                : _currentFramebuffer.RenderPassNoClear_Load;
            _gd.Vk.CmdBeginRenderPass(_cb, in renderPassBI, SubpassContents.Inline);
            _activeRenderPass = renderPassBI.RenderPass;

            if (haveAnyClearValues)
            {
                if (_depthClearValue.HasValue)
                {
                    ClearDepthStencilCore(_depthClearValue.Value.DepthStencil.Depth, (byte)_depthClearValue.Value.DepthStencil.Stencil);
                    _depthClearValue = null;
                }

                for (uint i = 0; i < _currentFramebuffer.ColorTargets.Count; i++)
                {
                    if (_validColorClearValues[i])
                    {
                        _validColorClearValues[i] = false;
                        ClearValue vkClearValue = _clearValues[i];
                        Color clearColor = new(
                            vkClearValue.Color.Float32_0,
                            vkClearValue.Color.Float32_1,
                            vkClearValue.Color.Float32_2,
                            vkClearValue.Color.Float32_3);
                        ClearColorTarget(i, clearColor);
                    }
                }
            }
        }
        else
        {
            // We have clear values for every attachment.
            renderPassBI.RenderPass = _currentFramebuffer.RenderPassClear;
            fixed (ClearValue* clearValuesPtr = &_clearValues[0])
            {
                renderPassBI.ClearValueCount = attachmentCount;
                renderPassBI.PClearValues = clearValuesPtr;
                if (_depthClearValue.HasValue)
                {
                    _clearValues[_currentFramebuffer.ColorTargets.Count] = _depthClearValue.Value;
                    _depthClearValue = null;
                }
                _gd.Vk.CmdBeginRenderPass(_cb, in renderPassBI, SubpassContents.Inline);
                _activeRenderPass = _currentFramebuffer.RenderPassClear;
                Util.ClearArray(_validColorClearValues);
            }
        }

        _newFramebuffer = false;
    }

    private void EndCurrentRenderPass()
    {
        Debug.Assert(_activeRenderPass.Handle != default);
        _gd.Vk.CmdEndRenderPass(_cb);
        _currentFramebuffer.TransitionToIntermediateLayout(_cb);
        _activeRenderPass = default;

        // Place a barrier between RenderPasses, so that color / depth outputs
        // can be read in subsequent passes.
        _gd.Vk.CmdPipelineBarrier(
            _cb,
            PipelineStageFlags.BottomOfPipeBit,
            PipelineStageFlags.TopOfPipeBit,
            0,
            0,
            null,
            0,
            null,
            0,
            null);
    }

    private protected override void SetVertexSourceCore(IVertexSource source)
    {
        _hasResolvedPipeline = false;
    }

    private VkShaderProgram _currentShaderProgram;
    private VkComputeProgram _currentComputeProgram;

    private protected override void SetShaderCore(ShaderProgram program)
    {
        VkShaderProgram sp = Util.AssertSubtype<ShaderProgram, VkShaderProgram>(program);
        if (_currentShaderProgram == sp) return;

        _currentShaderProgram = sp;
        _hasResolvedPipeline = false;
        _currentStagingInfo.Resources.Add(sp.RefCount);
    }

    private protected override void SetComputeShaderCore(ComputeProgram program)
    {
        VkComputeProgram cp = Util.AssertSubtype<ComputeProgram, VkComputeProgram>(program);
        _currentComputeProgram = cp;
        _gd.Vk.CmdBindPipeline(_cb, PipelineBindPoint.Compute, cp.DevicePipeline);
        _currentStagingInfo.Resources.Add(cp.RefCount);
    }

    private protected override void SetPropertiesCore(PropertySet properties) { }

    private protected override void ClearPropertiesCore()
    {
        _resourceBindCache.Clear();
        _uboBackingBuffers.Clear();
    }

    private void TransitionPropertyTextures(bool isCompute)
    {
        ResourceLayoutDescription[] layouts = isCompute
            ? _currentComputeProgram.ResourceLayoutsArray
            : _currentShaderProgram.ResourceLayoutsArray;

        foreach (ResourceLayoutDescription layout in layouts)
        {
            foreach (ResourceLayoutElementDescription elem in layout.Elements)
            {
                if (elem.Kind != ResourceKind.TextureReadOnly && elem.Kind != ResourceKind.TextureReadWrite)
                    continue;

                VkTexture tex;
                if (_mergedTable.Entries.TryGetValue(elem.Name, out PropertyEntry entry)
                    && entry.Kind == PropertyEntryKind.Texture)
                {
                    Texture resolved = entry.Texture ?? entry.TextureView?.Target;
                    tex = resolved != null ? (VkTexture)resolved : GetMissingTexture(elem.Kind);
                }
                else
                {
                    tex = GetMissingTexture(elem.Kind);
                }

                ImageLayout targetLayout = elem.Kind == ResourceKind.TextureReadOnly
                    ? ImageLayout.ShaderReadOnlyOptimal
                    : ImageLayout.General;

                tex.TransitionImageLayout(_cb, 0, tex.MipLevels, 0, tex.ActualArrayLayers, targetLayout);

                if (elem.Kind == ResourceKind.TextureReadWrite && (tex.Usage & TextureUsage.Sampled) != 0)
                    _preDrawSampledImages.Add(tex);
            }
        }
    }

    private VkTexture GetMissingTexture(ResourceKind kind)
        => (VkTexture)(kind == ResourceKind.TextureReadWrite ? _gd.NullTextureRW2D : _gd.NullTexture2D);

    private unsafe void BindPropertySets(
        object programKey,
        ResourceLayoutDescription[] resourceLayouts,
        DescriptorSetLayout[] dslLayouts,
        DescriptorResourceCounts[] perSetCounts,
        uint setCount,
        int totalDynamicUboCount,
        Silk.NET.Vulkan.PipelineLayout pipelineLayout,
        PipelineBindPoint bindPoint,
        bool isCompute)
    {
        if (setCount == 0) return;

        VkDescriptorPoolManager framePool = _gd.GetFrameDescriptorPool(_gd.CurrentFrame.RingSlot);
        uint resourceVersion = _mergedTable.ResourceVersion;
        uint uniformVersion = _mergedTable.UniformVersion;

        DescriptorSet* sets = stackalloc DescriptorSet[(int)setCount];
        uint* dynOffsets = stackalloc uint[totalDynamicUboCount > 0 ? totalDynamicUboCount : 1];
        int dynOffsetCount = 0;

        for (int setIdx = 0; setIdx < (int)setCount; setIdx++)
        {
            ResourceLayoutDescription layout = resourceLayouts[setIdx];
            (object programKey, int setIdx, uint resourceVersion) cacheKey = (programKey, setIdx, resourceVersion);

            bool miss = !_resourceBindCache.TryGetValue(cacheKey, out DescriptorSet ds);
            if (!miss && UboBackingBufferChanged(programKey, setIdx, in layout, isCompute, uniformVersion))
            {
                _resourceBindCache.Remove(cacheKey);
                miss = true;
            }

            if (miss)
            {
                DescriptorAllocationToken tok = framePool.Allocate(perSetCounts[setIdx], dslLayouts[setIdx]);
                ds = tok.Set;
                WriteDescriptorSlot(programKey, (uint)setIdx, in layout, ds, isCompute, uniformVersion);
                _resourceBindCache[cacheKey] = ds;
            }

            sets[setIdx] = ds;
            AppendDynOffsets(programKey, (uint)setIdx, in layout, isCompute, uniformVersion, dynOffsets, ref dynOffsetCount);
        }

        _gd.Vk.CmdBindDescriptorSets(_cb, bindPoint, pipelineLayout, 0, setCount, sets, (uint)dynOffsetCount, dynOffsets);
    }

    private bool UboBackingBufferChanged(
        object programKey, int setIdx, in ResourceLayoutDescription layout,
        bool isCompute, uint uniformVersion)
    {
        if (!_uboBackingBuffers.TryGetValue((programKey, setIdx), out VkBufferHandle[] stored))
            return false;

        int idx = 0;
        foreach (ResourceLayoutElementDescription elem in layout.Elements)
        {
            if (elem.Kind != ResourceKind.UniformBuffer) continue;
            DeviceBufferRange range = ResolveUboRange(programKey, (uint)setIdx, in elem, isCompute, uniformVersion);
            VkBuffer vkBuf = Util.AssertSubtype<DeviceBuffer, VkBuffer>(range.Buffer);
            if (idx >= stored.Length || stored[idx].Handle != vkBuf.DeviceBuffer.Handle)
                return true;
            idx++;
        }
        return false;
    }

    private unsafe void AppendDynOffsets(
        object programKey, uint setIdx, in ResourceLayoutDescription layout,
        bool isCompute, uint uniformVersion, uint* dynOffsets, ref int dynOffsetCount)
    {
        const int MaxUbosPerSet = 16;
        (int binding, uint offset)* uboData = stackalloc (int, uint)[MaxUbosPerSet];
        int uboCount = 0;

        foreach (ResourceLayoutElementDescription elem in layout.Elements)
        {
            if (elem.Kind != ResourceKind.UniformBuffer) continue;
            DeviceBufferRange range = ResolveUboRange(programKey, setIdx, in elem, isCompute, uniformVersion);
            uboData[uboCount++] = (elem.BindingIndex, range.Offset);
        }

        // Sort by binding index (Vulkan requires dynamic offsets in binding-number order).
        for (int i = 1; i < uboCount; i++)
        {
            (int binding, uint offset) key = uboData[i];
            int j = i - 1;
            while (j >= 0 && uboData[j].Item1 > key.Item1) { uboData[j + 1] = uboData[j]; j--; }
            uboData[j + 1] = key;
        }

        for (int i = 0; i < uboCount; i++)
            dynOffsets[dynOffsetCount++] = uboData[i].Item2;
    }

    private unsafe void WriteDescriptorSlot(
        object programKey, uint setIdx, in ResourceLayoutDescription layout,
        DescriptorSet dstSet, bool isCompute, uint uniformVersion)
    {
        const int MaxElems = 64;
        WriteDescriptorSet* writes = stackalloc WriteDescriptorSet[MaxElems];
        DescriptorBufferInfo* bufInfos = stackalloc DescriptorBufferInfo[MaxElems];
        DescriptorImageInfo* imgInfos = stackalloc DescriptorImageInfo[MaxElems];
        int writeCount = 0, bufIdx = 0, imgIdx = 0;

        int uboCount = 0;
        foreach (ResourceLayoutElementDescription elem in layout.Elements)
            if (elem.Kind == ResourceKind.UniformBuffer) uboCount++;

        VkBufferHandle[]? uboBuffers = uboCount > 0 ? new VkBufferHandle[uboCount] : null;
        int uboTrackedIdx = 0;

        foreach (ResourceLayoutElementDescription elem in layout.Elements)
        {
            WriteDescriptorSet write = new()
            {
                SType = StructureType.WriteDescriptorSet,
                DstSet = dstSet,
                DstBinding = (uint)elem.BindingIndex,
                DescriptorCount = 1,
            };

            switch (elem.Kind)
            {
                case ResourceKind.UniformBuffer:
                    {
                        DeviceBufferRange range = ResolveUboRange(programKey, setIdx, in elem, isCompute, uniformVersion);
                        VkBuffer vkBuf = Util.AssertSubtype<DeviceBuffer, VkBuffer>(range.Buffer);
                        uboBuffers![uboTrackedIdx++] = vkBuf.DeviceBuffer;
                        bufInfos[bufIdx] = new DescriptorBufferInfo
                        {
                            Buffer = vkBuf.DeviceBuffer,
                            Offset = 0,
                            Range = range.SizeInBytes,
                        };
                        write.DescriptorType = DescriptorType.UniformBufferDynamic;
                        write.PBufferInfo = &bufInfos[bufIdx++];
                        break;
                    }

                case ResourceKind.StructuredBufferReadOnly:
                case ResourceKind.StructuredBufferReadWrite:
                    {
                        DeviceBufferRange range;
                        if (_mergedTable.Entries.TryGetValue(elem.Name, out PropertyEntry? ssboEntry)
                            && ssboEntry.Kind == PropertyEntryKind.Buffer)
                        {
                            range = ssboEntry.Buffer!.Value;
                        }
                        else
                        {
                            _gd.OnMissingProperty?.Invoke(
                                _currentShaderProgram,
                                null,
                                elem.Name, elem.Kind, setIdx, elem.BindingIndex);
                            range = new DeviceBufferRange(_gd.NullStructuredRW, 0, 0);
                        }
                        VkBuffer ssboVkBuf = Util.AssertSubtype<DeviceBuffer, VkBuffer>(range.Buffer);
                        bufInfos[bufIdx] = new DescriptorBufferInfo
                        {
                            Buffer = ssboVkBuf.DeviceBuffer,
                            Offset = range.Offset,
                            Range = range.SizeInBytes,
                        };
                        write.DescriptorType = DescriptorType.StorageBuffer;
                        write.PBufferInfo = &bufInfos[bufIdx++];
                        break;
                    }

                case ResourceKind.TextureReadOnly:
                    {
                        VkTextureView view = ResolveTextureView(in elem, isCompute, setIdx);
                        imgInfos[imgIdx] = new DescriptorImageInfo
                        {
                            ImageView = view.ImageView,
                            ImageLayout = ImageLayout.ShaderReadOnlyOptimal,
                        };
                        write.DescriptorType = DescriptorType.SampledImage;
                        write.PImageInfo = &imgInfos[imgIdx++];
                        break;
                    }

                case ResourceKind.TextureReadWrite:
                    {
                        VkTextureView view = ResolveTextureView(in elem, isCompute, setIdx);
                        imgInfos[imgIdx] = new DescriptorImageInfo
                        {
                            ImageView = view.ImageView,
                            ImageLayout = ImageLayout.General,
                        };
                        write.DescriptorType = DescriptorType.StorageImage;
                        write.PImageInfo = &imgInfos[imgIdx++];
                        break;
                    }

                case ResourceKind.Sampler:
                    {
                        VkSampler sampler = ResolveSampler(in elem, in layout);
                        imgInfos[imgIdx] = new DescriptorImageInfo
                        {
                            Sampler = sampler.DeviceSampler,
                        };
                        write.DescriptorType = DescriptorType.Sampler;
                        write.PImageInfo = &imgInfos[imgIdx++];
                        break;
                    }

                default:
                    continue;
            }

            writes[writeCount++] = write;
        }

        if (writeCount > 0)
            _gd.Vk.UpdateDescriptorSets(_gd.Device, (uint)writeCount, writes, 0, null);

        if (uboBuffers != null)
            _uboBackingBuffers[(programKey, (int)setIdx)] = uboBuffers;
    }

    private DeviceBufferRange ResolveUboRange(
        object programKey, uint setIdx, in ResourceLayoutElementDescription elem,
        bool isCompute, uint uniformVersion)
    {
        if (elem.UniformFields != null && elem.UniformFields.Length > 0)
            return GetOrBuildImplicitUbo(programKey, setIdx, elem.BindingIndex, elem.UniformFields, isCompute, uniformVersion);

        if (_mergedTable.Entries.TryGetValue(elem.Name, out PropertyEntry? uboEntry)
            && uboEntry.Kind == PropertyEntryKind.Buffer)
        {
            return uboEntry.Buffer!.Value;
        }

        _gd.OnMissingProperty?.Invoke(
            _currentShaderProgram,
            null,
            elem.Name, ResourceKind.UniformBuffer, setIdx, elem.BindingIndex);
        return _gd.CurrentFrame.AllocateTransient(16);
    }

    private unsafe DeviceBufferRange GetOrBuildImplicitUbo(
        object programKey, uint setIdx, int bindingIndex,
        UniformBlockField[] fields, bool isCompute, uint uniformVersion)
    {
        if (!isCompute)
        {
            var key = new UboCacheKey((ShaderProgram)programKey, setIdx, bindingIndex, uniformVersion);
            if (_frameUboCache.TryGetValue(key, out DeviceBufferRange cached)) return cached;
        }
        else
        {
            var key = new ComputeUboCacheKey((ComputeProgram)programKey, setIdx, bindingIndex, uniformVersion);
            if (_computeUboCache.TryGetValue(key, out DeviceBufferRange cached)) return cached;
        }

        uint totalSize = 0;
        foreach (UniformBlockField field in fields)
            totalSize = Math.Max(totalSize, field.Offset + field.Size);
        if (totalSize == 0) totalSize = 16;

        DeviceBufferRange range = _gd.CurrentFrame.AllocateTransient(totalSize);

        byte[] uploadBuf = ArrayPool<byte>.Shared.Rent((int)totalSize);
        try
        {
            Array.Clear(uploadBuf, 0, (int)totalSize);
            foreach (UniformBlockField field in fields)
            {
                if (_mergedTable.Entries.TryGetValue(field.Name, out PropertyEntry uEntry)
                    && uEntry.Kind == PropertyEntryKind.Uniform)
                {
                    ReadOnlySpan<byte> src = MemoryMarshal.CreateReadOnlySpan(
                        ref Unsafe.As<PropertyEntry.UniformPayload, byte>(ref uEntry.Uniform),
                        (int)field.Size);
                    src.CopyTo(uploadBuf.AsSpan((int)field.Offset, (int)field.Size));
                }
            }
            fixed (byte* ptr = uploadBuf)
                _gd.UpdateBuffer(range.Buffer, range.Offset, (IntPtr)ptr, totalSize);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(uploadBuf);
        }

        if (!isCompute)
            _frameUboCache[new UboCacheKey((ShaderProgram)programKey, setIdx, bindingIndex, uniformVersion)] = range;
        else
            _computeUboCache[new ComputeUboCacheKey((ComputeProgram)programKey, setIdx, bindingIndex, uniformVersion)] = range;

        return range;
    }

    private VkTextureView ResolveTextureView(
        in ResourceLayoutElementDescription elem, bool isCompute, uint setIdx)
    {
        if (_mergedTable.Entries.TryGetValue(elem.Name, out PropertyEntry texEntry)
            && texEntry.Kind == PropertyEntryKind.Texture)
        {
            if (texEntry.TextureView != null)
                return (VkTextureView)texEntry.TextureView;
            if (texEntry.Texture != null)
                return _gd.GetOrCreateDefaultView((VkTexture)texEntry.Texture);
        }

        _gd.OnMissingProperty?.Invoke(
            _currentShaderProgram,
            null,
            elem.Name, elem.Kind, setIdx, elem.BindingIndex);
        return _gd.GetOrCreateDefaultView(GetMissingTexture(elem.Kind));
    }

    private VkSampler ResolveSampler(
        in ResourceLayoutElementDescription elem, in ResourceLayoutDescription layout)
    {
        // Rule 1: explicit SetSampler(name) entry
        if (_mergedTable.Entries.TryGetValue(elem.Name, out PropertyEntry samplerEntry)
            && samplerEntry.Kind == PropertyEntryKind.Sampler
            && samplerEntry.Sampler != null)
        {
            return (VkSampler)samplerEntry.Sampler;
        }

        // Rule 2: SetTexture(name, _, sampler) where the texture element shares this name
        foreach (ResourceLayoutElementDescription other in layout.Elements)
        {
            if (other.Name == elem.Name
                && (other.Kind == ResourceKind.TextureReadOnly || other.Kind == ResourceKind.TextureReadWrite))
            {
                if (_mergedTable.Entries.TryGetValue(elem.Name, out PropertyEntry texEntry)
                    && texEntry.Kind == PropertyEntryKind.Texture
                    && texEntry.Sampler != null)
                {
                    return (VkSampler)texEntry.Sampler;
                }
                break;
            }
        }

        // Rule 3: fallback to LinearSampler
        return (VkSampler)_gd.LinearSampler;
    }

    public override void SetScissorRect(uint index, uint x, uint y, uint width, uint height)
    {
        if (index == 0 || _gd.Features.MultipleViewports)
        {
            Rect2D scissor = new(new Offset2D((int)x, (int)y), new Extent2D((uint)width, (uint)height));
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
    }

    private protected override void CopyBufferCore(
        DeviceBuffer source,
        uint sourceOffset,
        DeviceBuffer destination,
        uint destinationOffset,
        uint sizeInBytes)
    {
        EnsureNoRenderPass();

        VkBuffer srcVkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(source);
        _currentStagingInfo.Resources.Add(srcVkBuffer.RefCount);
        VkBuffer dstVkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(destination);
        _currentStagingInfo.Resources.Add(dstVkBuffer.RefCount);

        BufferCopy region = new()
        {
            SrcOffset = sourceOffset,
            DstOffset = destinationOffset,
            Size = sizeInBytes
        };

        _gd.Vk.CmdCopyBuffer(_cb, srcVkBuffer.DeviceBuffer, dstVkBuffer.DeviceBuffer, 1, in region);

        bool needToProtectUniform = destination.Usage.HasFlag(BufferUsage.UniformBuffer);

        MemoryBarrier barrier = new()
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = needToProtectUniform ? AccessFlags.UniformReadBit : AccessFlags.VertexAttributeReadBit
        };
        _gd.Vk.CmdPipelineBarrier(
            _cb,
            PipelineStageFlags.TransferBit, needToProtectUniform ?
                PipelineStageFlags.VertexShaderBit | PipelineStageFlags.ComputeShaderBit |
                PipelineStageFlags.FragmentShaderBit | PipelineStageFlags.GeometryShaderBit |
                PipelineStageFlags.TessellationControlShaderBit | PipelineStageFlags.TessellationEvaluationShaderBit
                : PipelineStageFlags.VertexInputBit,
            0,
            1, in barrier,
            0, null,
            0, null);
    }

    private protected override void CopyTextureCore(
        Texture source,
        uint srcX, uint srcY, uint srcZ,
        uint srcMipLevel,
        uint srcBaseArrayLayer,
        Texture destination,
        uint dstX, uint dstY, uint dstZ,
        uint dstMipLevel,
        uint dstBaseArrayLayer,
        uint width, uint height, uint depth,
        uint layerCount)
    {
        EnsureNoRenderPass();
        CopyTextureCore_VkCommandBuffer(
            _gd.Vk,
            _cb,
            source, srcX, srcY, srcZ, srcMipLevel, srcBaseArrayLayer,
            destination, dstX, dstY, dstZ, dstMipLevel, dstBaseArrayLayer,
            width, height, depth, layerCount);

        VkTexture srcVkTexture = Util.AssertSubtype<Texture, VkTexture>(source);
        _currentStagingInfo.Resources.Add(srcVkTexture.RefCount);
        VkTexture dstVkTexture = Util.AssertSubtype<Texture, VkTexture>(destination);
        _currentStagingInfo.Resources.Add(dstVkTexture.RefCount);
    }

    internal static void CopyTextureCore_VkCommandBuffer(
        VkApi vk,
        Silk.NET.Vulkan.CommandBuffer cb,
        Texture source,
        uint srcX, uint srcY, uint srcZ,
        uint srcMipLevel,
        uint srcBaseArrayLayer,
        Texture destination,
        uint dstX, uint dstY, uint dstZ,
        uint dstMipLevel,
        uint dstBaseArrayLayer,
        uint width, uint height, uint depth,
        uint layerCount)
    {
        VkTexture srcVkTexture = Util.AssertSubtype<Texture, VkTexture>(source);
        VkTexture dstVkTexture = Util.AssertSubtype<Texture, VkTexture>(destination);

        bool sourceIsStaging = (source.Usage & TextureUsage.Staging) == TextureUsage.Staging;
        bool destIsStaging = (destination.Usage & TextureUsage.Staging) == TextureUsage.Staging;

        if (!sourceIsStaging && !destIsStaging)
        {
            ImageSubresourceLayers srcSubresource = new()
            {
                AspectMask = ImageAspectFlags.ColorBit,
                LayerCount = layerCount,
                MipLevel = srcMipLevel,
                BaseArrayLayer = srcBaseArrayLayer
            };

            ImageSubresourceLayers dstSubresource = new()
            {
                AspectMask = ImageAspectFlags.ColorBit,
                LayerCount = layerCount,
                MipLevel = dstMipLevel,
                BaseArrayLayer = dstBaseArrayLayer
            };

            ImageCopy region = new()
            {
                SrcOffset = new Offset3D { X = (int)srcX, Y = (int)srcY, Z = (int)srcZ },
                DstOffset = new Offset3D { X = (int)dstX, Y = (int)dstY, Z = (int)dstZ },
                SrcSubresource = srcSubresource,
                DstSubresource = dstSubresource,
                Extent = new Extent3D { Width = width, Height = height, Depth = depth }
            };

            srcVkTexture.TransitionImageLayout(
                cb,
                srcMipLevel,
                1,
                srcBaseArrayLayer,
                layerCount,
                ImageLayout.TransferSrcOptimal);

            dstVkTexture.TransitionImageLayout(
                cb,
                dstMipLevel,
                1,
                dstBaseArrayLayer,
                layerCount,
                ImageLayout.TransferDstOptimal);

            vk.CmdCopyImage(
                cb,
                srcVkTexture.OptimalDeviceImage,
                ImageLayout.TransferSrcOptimal,
                dstVkTexture.OptimalDeviceImage,
                ImageLayout.TransferDstOptimal,
                1,
                in region);

            if ((srcVkTexture.Usage & TextureUsage.Sampled) != 0)
            {
                srcVkTexture.TransitionImageLayout(
                    cb,
                    srcMipLevel,
                    1,
                    srcBaseArrayLayer,
                    layerCount,
                    ImageLayout.ShaderReadOnlyOptimal);
            }

            if ((dstVkTexture.Usage & TextureUsage.Sampled) != 0)
            {
                dstVkTexture.TransitionImageLayout(
                    cb,
                    dstMipLevel,
                    1,
                    dstBaseArrayLayer,
                    layerCount,
                    ImageLayout.ShaderReadOnlyOptimal);
            }
        }
        else if (sourceIsStaging && !destIsStaging)
        {
            VkBufferHandle srcBuffer = srcVkTexture.StagingBuffer;
            SubresourceLayout srcLayout = srcVkTexture.GetSubresourceLayout(
                srcVkTexture.CalculateSubresource(srcMipLevel, srcBaseArrayLayer));
            VkImageHandle dstImage = dstVkTexture.OptimalDeviceImage;
            dstVkTexture.TransitionImageLayout(
                cb,
                dstMipLevel,
                1,
                dstBaseArrayLayer,
                layerCount,
                ImageLayout.TransferDstOptimal);

            ImageSubresourceLayers dstSubresource = new()
            {
                AspectMask = ImageAspectFlags.ColorBit,
                LayerCount = layerCount,
                MipLevel = dstMipLevel,
                BaseArrayLayer = dstBaseArrayLayer
            };

            Util.GetMipDimensions(srcVkTexture, srcMipLevel, out uint mipWidth, out uint mipHeight, out uint mipDepth);
            uint blockSize = FormatHelpers.IsCompressedFormat(srcVkTexture.Format) ? 4u : 1u;
            uint bufferRowLength = Math.Max(mipWidth, blockSize);
            uint bufferImageHeight = Math.Max(mipHeight, blockSize);
            uint compressedX = srcX / blockSize;
            uint compressedY = srcY / blockSize;
            uint blockSizeInBytes = blockSize == 1
                ? FormatSizeHelpers.GetSizeInBytes(srcVkTexture.Format)
                : FormatHelpers.GetBlockSizeInBytes(srcVkTexture.Format);
            uint rowPitch = FormatHelpers.GetRowPitch(bufferRowLength, srcVkTexture.Format);
            uint depthPitch = FormatHelpers.GetDepthPitch(rowPitch, bufferImageHeight, srcVkTexture.Format);

            uint copyWidth = Math.Min(width, mipWidth);
            uint copyheight = Math.Min(height, mipHeight);

            BufferImageCopy regions = new()
            {
                BufferOffset = srcLayout.Offset
                    + (srcZ * depthPitch)
                    + (compressedY * rowPitch)
                    + (compressedX * blockSizeInBytes),
                BufferRowLength = bufferRowLength,
                BufferImageHeight = bufferImageHeight,
                ImageExtent = new Extent3D { Width = copyWidth, Height = copyheight, Depth = depth },
                ImageOffset = new Offset3D { X = (int)dstX, Y = (int)dstY, Z = (int)dstZ },
                ImageSubresource = dstSubresource
            };

            vk.CmdCopyBufferToImage(cb, srcBuffer, dstImage, ImageLayout.TransferDstOptimal, 1, in regions);

            if ((dstVkTexture.Usage & TextureUsage.Sampled) != 0)
            {
                dstVkTexture.TransitionImageLayout(
                    cb,
                    dstMipLevel,
                    1,
                    dstBaseArrayLayer,
                    layerCount,
                    ImageLayout.ShaderReadOnlyOptimal);
            }
        }
        else if (!sourceIsStaging && destIsStaging)
        {
            VkImageHandle srcImage = srcVkTexture.OptimalDeviceImage;
            srcVkTexture.TransitionImageLayout(
                cb,
                srcMipLevel,
                1,
                srcBaseArrayLayer,
                layerCount,
                ImageLayout.TransferSrcOptimal);

            VkBufferHandle dstBuffer = dstVkTexture.StagingBuffer;

            ImageAspectFlags aspect = (srcVkTexture.Usage & TextureUsage.DepthStencil) != 0
                ? ImageAspectFlags.DepthBit
                : ImageAspectFlags.ColorBit;

            Util.GetMipDimensions(dstVkTexture, dstMipLevel, out uint mipWidth, out uint mipHeight, out uint mipDepth);
            uint blockSize = FormatHelpers.IsCompressedFormat(srcVkTexture.Format) ? 4u : 1u;
            uint bufferRowLength = Math.Max(mipWidth, blockSize);
            uint bufferImageHeight = Math.Max(mipHeight, blockSize);
            uint compressedDstX = dstX / blockSize;
            uint compressedDstY = dstY / blockSize;
            uint blockSizeInBytes = blockSize == 1
                ? FormatSizeHelpers.GetSizeInBytes(dstVkTexture.Format)
                : FormatHelpers.GetBlockSizeInBytes(dstVkTexture.Format);
            uint rowPitch = FormatHelpers.GetRowPitch(bufferRowLength, dstVkTexture.Format);
            uint depthPitch = FormatHelpers.GetDepthPitch(rowPitch, bufferImageHeight, dstVkTexture.Format);

            BufferImageCopy* layers = stackalloc BufferImageCopy[(int)layerCount];
            for (uint layer = 0; layer < layerCount; layer++)
            {
                SubresourceLayout dstLayout = dstVkTexture.GetSubresourceLayout(
                    dstVkTexture.CalculateSubresource(dstMipLevel, dstBaseArrayLayer + layer));

                ImageSubresourceLayers srcSubresource = new()
                {
                    AspectMask = aspect,
                    LayerCount = 1,
                    MipLevel = srcMipLevel,
                    BaseArrayLayer = srcBaseArrayLayer + layer
                };

                BufferImageCopy region = new()
                {
                    BufferRowLength = bufferRowLength,
                    BufferImageHeight = bufferImageHeight,
                    BufferOffset = dstLayout.Offset
                        + (dstZ * depthPitch)
                        + (compressedDstY * rowPitch)
                        + (compressedDstX * blockSizeInBytes),
                    ImageExtent = new Extent3D { Width = width, Height = height, Depth = depth },
                    ImageOffset = new Offset3D { X = (int)srcX, Y = (int)srcY, Z = (int)srcZ },
                    ImageSubresource = srcSubresource
                };

                layers[layer] = region;
            }

            vk.CmdCopyImageToBuffer(cb, srcImage, ImageLayout.TransferSrcOptimal, dstBuffer, layerCount, layers);

            if ((srcVkTexture.Usage & TextureUsage.Sampled) != 0)
            {
                srcVkTexture.TransitionImageLayout(
                    cb,
                    srcMipLevel,
                    1,
                    srcBaseArrayLayer,
                    layerCount,
                    ImageLayout.ShaderReadOnlyOptimal);
            }
        }
        else
        {
            Debug.Assert(sourceIsStaging && destIsStaging);
            VkBufferHandle srcBuffer = srcVkTexture.StagingBuffer;
            SubresourceLayout srcLayout = srcVkTexture.GetSubresourceLayout(
                srcVkTexture.CalculateSubresource(srcMipLevel, srcBaseArrayLayer));
            VkBufferHandle dstBuffer = dstVkTexture.StagingBuffer;
            SubresourceLayout dstLayout = dstVkTexture.GetSubresourceLayout(
                dstVkTexture.CalculateSubresource(dstMipLevel, dstBaseArrayLayer));

            uint zLimit = Math.Max(depth, layerCount);
            if (!FormatHelpers.IsCompressedFormat(source.Format))
            {
                uint pixelSize = FormatSizeHelpers.GetSizeInBytes(srcVkTexture.Format);
                for (uint zz = 0; zz < zLimit; zz++)
                {
                    for (uint yy = 0; yy < height; yy++)
                    {
                        BufferCopy region = new()
                        {
                            SrcOffset = srcLayout.Offset
                                + srcLayout.DepthPitch * (zz + srcZ)
                                + srcLayout.RowPitch * (yy + srcY)
                                + pixelSize * srcX,
                            DstOffset = dstLayout.Offset
                                + dstLayout.DepthPitch * (zz + dstZ)
                                + dstLayout.RowPitch * (yy + dstY)
                                + pixelSize * dstX,
                            Size = width * pixelSize,
                        };

                        vk.CmdCopyBuffer(cb, srcBuffer, dstBuffer, 1, in region);
                    }
                }
            }
            else // IsCompressedFormat
            {
                uint denseRowSize = FormatHelpers.GetRowPitch(width, source.Format);
                uint numRows = FormatHelpers.GetNumRows(height, source.Format);
                uint compressedSrcX = srcX / 4;
                uint compressedSrcY = srcY / 4;
                uint compressedDstX = dstX / 4;
                uint compressedDstY = dstY / 4;
                uint blockSizeInBytes = FormatHelpers.GetBlockSizeInBytes(source.Format);

                for (uint zz = 0; zz < zLimit; zz++)
                {
                    for (uint row = 0; row < numRows; row++)
                    {
                        BufferCopy region = new()
                        {
                            SrcOffset = srcLayout.Offset
                                + srcLayout.DepthPitch * (zz + srcZ)
                                + srcLayout.RowPitch * (row + compressedSrcY)
                                + blockSizeInBytes * compressedSrcX,
                            DstOffset = dstLayout.Offset
                                + dstLayout.DepthPitch * (zz + dstZ)
                                + dstLayout.RowPitch * (row + compressedDstY)
                                + blockSizeInBytes * compressedDstX,
                            Size = denseRowSize,
                        };

                        vk.CmdCopyBuffer(cb, srcBuffer, dstBuffer, 1, in region);
                    }
                }

            }
        }
    }

    private protected override void GenerateMipmapsCore(Texture texture)
    {
        EnsureNoRenderPass();
        VkTexture vkTex = Util.AssertSubtype<Texture, VkTexture>(texture);
        _currentStagingInfo.Resources.Add(vkTex.RefCount);

        uint layerCount = vkTex.ArrayLayers;
        if ((vkTex.Usage & TextureUsage.Cubemap) != 0)
        {
            layerCount *= 6;
        }

        ImageBlit region;

        uint width = vkTex.Width;
        uint height = vkTex.Height;
        uint depth = vkTex.Depth;
        for (uint level = 1; level < vkTex.MipLevels; level++)
        {
            vkTex.TransitionImageLayoutNonmatching(_cb, level - 1, 1, 0, layerCount, ImageLayout.TransferSrcOptimal);
            vkTex.TransitionImageLayoutNonmatching(_cb, level, 1, 0, layerCount, ImageLayout.TransferDstOptimal);

            VkImageHandle deviceImage = vkTex.OptimalDeviceImage;
            uint mipWidth = Math.Max(width >> 1, 1);
            uint mipHeight = Math.Max(height >> 1, 1);
            uint mipDepth = Math.Max(depth >> 1, 1);

            region.SrcSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseArrayLayer = 0,
                LayerCount = layerCount,
                MipLevel = level - 1
            };
            region.SrcOffsets = default;
            region.SrcOffsets.Element0 = new Offset3D();
            region.SrcOffsets.Element1 = new Offset3D { X = (int)width, Y = (int)height, Z = (int)depth };
            region.DstOffsets = default;
            region.DstOffsets.Element0 = new Offset3D();

            region.DstSubresource = new ImageSubresourceLayers
            {
                AspectMask = ImageAspectFlags.ColorBit,
                BaseArrayLayer = 0,
                LayerCount = layerCount,
                MipLevel = level
            };

            region.DstOffsets.Element1 = new Offset3D { X = (int)mipWidth, Y = (int)mipHeight, Z = (int)mipDepth };
            _gd.Vk.CmdBlitImage(
                _cb,
                deviceImage, ImageLayout.TransferSrcOptimal,
                deviceImage, ImageLayout.TransferDstOptimal,
                1, &region,
                _gd.GetFormatFilter(vkTex.VkFormat));

            width = mipWidth;
            height = mipHeight;
            depth = mipDepth;
        }

        if ((vkTex.Usage & TextureUsage.Sampled) != 0)
        {
            vkTex.TransitionImageLayoutNonmatching(_cb, 0, vkTex.MipLevels, 0, layerCount, ImageLayout.ShaderReadOnlyOptimal);
        }
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

    private VkBuffer GetStagingBuffer(uint size)
    {
        lock (_stagingLock)
        {
            VkBuffer ret = null;
            foreach (VkBuffer buffer in _availableStagingBuffers)
            {
                if (buffer.SizeInBytes >= size)
                {
                    ret = buffer;
                    _availableStagingBuffers.Remove(buffer);
                    break;
                }
            }
            if (ret == null)
            {
                ret = (VkBuffer)_gd.ResourceFactory.CreateBuffer(new BufferDescription(size, BufferUsage.Staging));
                ret.Name = $"Staging Buffer (CommandBuffer {_name})";
            }

            _currentStagingInfo.BuffersUsed.Add(ret);
            return ret;
        }
    }

    private protected override void PushDebugGroupCore(string name)
    {
        vkCmdDebugMarkerBeginEXT_t func = _gd.MarkerBegin;
        if (func == null) { return; }

        DebugMarkerMarkerInfoEXT markerInfo = new() { SType = StructureType.DebugMarkerMarkerInfoExt };

        int byteCount = Encoding.UTF8.GetByteCount(name);
        byte* utf8Ptr = stackalloc byte[byteCount + 1];
        fixed (char* namePtr = name)
        {
            Encoding.UTF8.GetBytes(namePtr, name.Length, utf8Ptr, byteCount);
        }
        utf8Ptr[byteCount] = 0;

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

        int byteCount = Encoding.UTF8.GetByteCount(name);
        byte* utf8Ptr = stackalloc byte[byteCount + 1];
        fixed (char* namePtr = name)
        {
            Encoding.UTF8.GetBytes(namePtr, name.Length, utf8Ptr, byteCount);
        }
        utf8Ptr[byteCount] = 0;

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
        }
    }

    private class StagingResourceInfo
    {
        public List<VkBuffer> BuffersUsed { get; } = [];
        public HashSet<ResourceRefCount> Resources { get; } = [];
        public void Clear()
        {
            BuffersUsed.Clear();
            Resources.Clear();
        }
    }

    private StagingResourceInfo GetStagingResourceInfo()
    {
        lock (_stagingLock)
        {
            StagingResourceInfo ret;
            int availableCount = _availableStagingInfos.Count;
            if (availableCount > 0)
            {
                ret = _availableStagingInfos[availableCount - 1];
                _availableStagingInfos.RemoveAt(availableCount - 1);
            }
            else
            {
                ret = new StagingResourceInfo();
            }

            return ret;
        }
    }

    private void RecycleStagingInfo(StagingResourceInfo info)
    {
        lock (_stagingLock)
        {
            foreach (VkBuffer buffer in info.BuffersUsed)
            {
                _availableStagingBuffers.Add(buffer);
            }

            foreach (ResourceRefCount rrc in info.Resources)
            {
                rrc.Decrement();
            }

            info.Clear();

            _availableStagingInfos.Add(info);
        }
    }
}
