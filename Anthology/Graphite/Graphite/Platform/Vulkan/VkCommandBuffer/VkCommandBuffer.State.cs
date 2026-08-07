using System;
using System.Collections.Generic;

using Prowl.Vector;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkCommandBuffer
{
    private VkFramebufferBase _currentFramebuffer;
    private bool _currentFramebufferEverActive;
    private RenderPass _activeRenderPass;
    private bool _newFramebuffer; // Render pass cycle state

    private VkGraphicsProgram _currentShaderProgram;
    private VkComputeProgram _currentComputeProgram;
    private VkPipelineCacheEntry _currentResolvedPipeline;
    private bool _hasResolvedPipeline;
    private PrimitiveTopology _resolvedTopology;

    private Rect2D[] _scissorRects = Array.Empty<Rect2D>();
    private Viewport[] _viewports = Array.Empty<Viewport>();

    private readonly List<VkTexture> _preDrawSampledImages = [];

    internal PropertySet ActiveProperties => _activeProperties;
    internal uint ActivePropertiesEpoch => _activePropertiesEpoch;
    internal void QueuePreDrawSampledImage(VkTexture tex) => _preDrawSampledImages.Add(tex);

    private void ClearGraphicsState()
    {
        _currentFramebuffer = null;
        _currentShaderProgram = null;
        _currentComputeProgram = null;
        _currentResolvedPipeline = default;
        _hasResolvedPipeline = false;
        _resolvedTopology = default;
        Util.ClearArray(_scissorRects);
        Util.ClearArray(_viewports);
        _vbCacheSource = null;
        _vbCacheProgram = null;
        _vbCacheCount = 0;
        _ibCacheBuffer = default;
        _ibCacheFormat = default;
        _ibCacheValid = false;
    }

    private protected override void SetVertexSourceCore(IVertexSource source)
    {
        _hasResolvedPipeline = false;
    }

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
        if (index != 0 && !_gd.Features.MultipleViewports) return;

        Rect2D scissor = new(new Offset2D((int)x, (int)y), new Extent2D(width, height));
        if (scissor.Equals(_scissorRects[index])) return;

        _scissorRects[index] = scissor;
        _gd.Vk.CmdSetScissor(_cb, index, 1, in scissor);
    }

    public override void SetViewport(uint index, ref Viewport viewport)
    {
        if (index != 0 && !_gd.Features.MultipleViewports) return;

        if (viewport.Equals(_viewports[index])) return;
        _viewports[index] = viewport;

        Silk.NET.Vulkan.Viewport vkViewport = new()
        {
            X = viewport.X,
            Y = _gd.IsClipSpaceYInverted ? viewport.Y : viewport.Height + viewport.Y,
            Width = viewport.Width,
            Height = _gd.IsClipSpaceYInverted ? viewport.Height : -viewport.Height,
            MinDepth = viewport.MinDepth,
            MaxDepth = viewport.MaxDepth
        };

        _gd.Vk.CmdSetViewport(_cb, index, 1, in vkViewport);
    }
}
