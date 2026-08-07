using System;
using System.Collections.Generic;

using Silk.NET.Vulkan;

using VkBufferHandle = Silk.NET.Vulkan.Buffer;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkCommandBuffer
{
    private IVertexSource _vbCacheSource;
    private VkGraphicsProgram _vbCacheProgram;
    private int _vbCacheCount;
    private VertexBinding[] _vbCacheBindings = Array.Empty<VertexBinding>();
    private ResourceRefCount[] _vbCacheRefCounts = Array.Empty<ResourceRefCount>();

    private VkBufferHandle _ibCacheBuffer;
    private IndexFormat _ibCacheFormat;
    private bool _ibCacheValid;

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
        VkBuffer vkBuffer = ResolveIndirectBuffer(indirectBuffer);
        _gd.Vk.CmdDrawIndirect(_cb, vkBuffer.DeviceBuffer, offset, drawCount, stride);
    }

    private protected override void DrawIndexedIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        PreDrawCommand();
        BindVertexBuffersFromSource();
        BindIndexBufferFromSource();
        VkBuffer vkBuffer = ResolveIndirectBuffer(indirectBuffer);
        _gd.Vk.CmdDrawIndexedIndirect(_cb, vkBuffer.DeviceBuffer, offset, drawCount, stride);
    }

    private protected override void DispatchCore(uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        PreDispatchCommand();
        _gd.Vk.CmdDispatch(_cb, groupCountX, groupCountY, groupCountZ);
    }

    private protected override void DispatchIndirectCore(DeviceBuffer indirectBuffer, uint offset)
    {
        PreDispatchCommand();
        VkBuffer vkBuffer = ResolveIndirectBuffer(indirectBuffer);
        _gd.Vk.CmdDispatchIndirect(_cb, vkBuffer.DeviceBuffer, offset);
    }

    private VkBuffer ResolveIndirectBuffer(DeviceBuffer indirectBuffer)
    {
        indirectBuffer.MarkInFlight(_gd, ExecutionId);
        VkBuffer vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(indirectBuffer);
        AddStagingResource(vkBuffer.RefCount);
        return vkBuffer;
    }

    private void PreDrawCommand()
    {
        FlushPreDrawSampledImages();

        ResolveAndBindGraphicsPipeline();

        // Resolve + transition property textures (must precede the render pass) and prepare descriptor
        // sets. Returns false when nothing changed since the last draw and the sets are still bound.
        bool needBind = _descriptorBinder.Prepare(
            _currentShaderProgram,
            reportProgram: _currentShaderProgram,
            isGraphics: true,
            renderPassActive: _activeRenderPass.Handle != default);

        EnsureRenderPassActive();

        if (needBind)
            _descriptorBinder.EmitBind(_currentResolvedPipeline.PipelineLayout, PipelineBindPoint.Graphics);
    }

    private void PreDispatchCommand()
    {
        EnsureNoRenderPass();

        FlushPreDrawSampledImages();

        bool needBind = _descriptorBinder.Prepare(
            _currentComputeProgram,
            reportProgram: _currentShaderProgram,
            isGraphics: false,
            renderPassActive: _activeRenderPass.Handle != default);

        if (needBind)
            _descriptorBinder.EmitBind(_currentComputeProgram.PipelineLayout, PipelineBindPoint.Compute);
    }

    private void FlushPreDrawSampledImages()
    {
        foreach (VkTexture tex in _preDrawSampledImages)
        {
            tex.TransitionImageLayout(_cb, 0, tex.MipLevels, 0, tex.ActualArrayLayers, ImageLayout.ShaderReadOnlyOptimal);
        }

        _preDrawSampledImages.Clear();
    }

    private void ResolveAndBindGraphicsPipeline()
    {
        PrimitiveTopology srcTopology = _currentVertexSource!.Topology;

        if (_hasResolvedPipeline && _resolvedTopology == srcTopology) return;

        if (_currentShaderProgram == null || _currentFramebuffer == null)
        {
            throw new RenderException("Cannot draw: no graphics GraphicsProgram or Framebuffer bound.");
        }

        VkPipelineCacheKey key = new(_framebufferOutputs!.Value, srcTopology);

        _currentResolvedPipeline = _currentShaderProgram.GetOrAddPipeline(in key);
        _resolvedTopology = srcTopology;
        _hasResolvedPipeline = true;

        _gd.Vk.CmdBindPipeline(_cb, PipelineBindPoint.Graphics, _currentResolvedPipeline.Pipeline);
    }

    private void BindVertexBuffersFromSource()
    {
        VkGraphicsProgram program = _currentShaderProgram;
        IReadOnlyList<VertexLayoutDescription> layouts = program.VertexLayouts;
        int count = layouts.Count;

        bool captureForProfiler = WantsDrawBufferCapture;
        if (captureForProfiler)
            BeginDrawBufferCapture();

        if (count == 0) return;

        IVertexSource source = _currentVertexSource!;

        // Same source + program as last draw: reuse the resolved bindings, skip re-resolving and rebinding.
        if (_vbCacheSource == source && _vbCacheProgram == program && _vbCacheCount == count)
        {
            for (int slot = 0; slot < count; slot++)
            {
                VertexBinding binding = _vbCacheBindings[slot];
                binding.Buffer.MarkInFlight(_gd, ExecutionId);
                AddStagingResource(_vbCacheRefCounts[slot]);

                if (captureForProfiler)
                    CaptureResolvedVertexBinding(in binding);
            }
            return;
        }

        Util.EnsureArrayMinimumSize(ref _vbCacheBindings, (uint)count);
        Util.EnsureArrayMinimumSize(ref _vbCacheRefCounts, (uint)count);

        VkBufferHandle* buffers = stackalloc VkBufferHandle[count];
        ulong* offsets = stackalloc ulong[count];

        for (int slot = 0; slot < count; slot++)
        {
            VertexLayoutDescription layout = layouts[slot];
            source.ResolveSlot((uint)slot, in layout, out VertexBinding binding);
            CheckVertexBindingUsage(in binding, (uint)slot);
            binding.Buffer.MarkInFlight(_gd, ExecutionId);

            if (captureForProfiler)
                CaptureResolvedVertexBinding(in binding);

            VkBuffer vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(binding.Buffer);
            buffers[slot] = vkBuffer.DeviceBuffer;
            offsets[slot] = binding.Offset;

            AddStagingResource(vkBuffer.RefCount);

            _vbCacheBindings[slot] = binding;
            _vbCacheRefCounts[slot] = vkBuffer.RefCount;
        }

        _gd.Vk.CmdBindVertexBuffers(_cb, 0u, (uint)count, buffers, offsets);

        _vbCacheSource = source;
        _vbCacheProgram = program;
        _vbCacheCount = count;
    }

    private void BindIndexBufferFromSource()
    {
        bool has = _currentVertexSource!.TryGetIndexBuffer(out DeviceBuffer ib, out IndexFormat fmt, out uint indexCount);
        _currentIndexCount = indexCount;
        DrawIndexed_AssertIndexBufferResolved(has);
        CheckIndexBufferUsage(ib);
        ib.MarkInFlight(_gd, ExecutionId);

        if (WantsDrawBufferCapture)
            CaptureResolvedIndexBinding(ib, fmt, indexCount);

        VkBuffer vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(ib);
        AddStagingResource(vkBuffer.RefCount);

        VkBufferHandle nativeBuffer = vkBuffer.DeviceBuffer;
        if (_ibCacheValid && _ibCacheBuffer.Handle == nativeBuffer.Handle && _ibCacheFormat == fmt)
            return;

        _gd.Vk.CmdBindIndexBuffer(_cb, nativeBuffer, 0, VkFormats.ToVkIndexFormat(fmt));

        _ibCacheBuffer = nativeBuffer;
        _ibCacheFormat = fmt;
        _ibCacheValid = true;
    }
}
