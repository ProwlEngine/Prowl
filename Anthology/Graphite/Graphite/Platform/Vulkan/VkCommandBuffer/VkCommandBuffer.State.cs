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
    private FramebufferMode _currentFramebufferMode;

    private VkGraphicsProgram _currentShaderProgram;
    private VkComputeProgram _currentComputeProgram;
    private VkPipelineCacheEntry _currentResolvedPipeline;
    private bool _hasResolvedPipeline;
    private PrimitiveTopology _resolvedTopology;

    private Rect2D[] _scissorRects = Array.Empty<Rect2D>();
    private Viewport[] _viewports = Array.Empty<Viewport>();

    private readonly List<VkTexture> _temporaryStorageImages = [];

    internal PropertySet ActiveProperties => _activeProperties;
    internal uint ActivePropertiesEpoch => _activePropertiesEpoch;

    internal bool IsTemporaryStorage(VkTexture tex) => _temporaryStorageImages.Contains(tex);

    internal void BeginTemporaryStorage(VkTexture tex)
    {
        VkBarriers.Transition(_gd, _cb, tex, VkBarriers.RestingLayout(tex), ImageLayout.General);
        _temporaryStorageImages.Add(tex);
    }

    private void EndTemporaryStorage()
    {
        foreach (VkTexture tex in _temporaryStorageImages)
            VkBarriers.Transition(_gd, _cb, tex, ImageLayout.General, VkBarriers.RestingLayout(tex));

        _temporaryStorageImages.Clear();
    }

    private void ClearGraphicsState()
    {
        _currentFramebuffer = null;
        _temporaryStorageImages.Clear();
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

    private protected override void SetShaderCore(GraphicsProgram program)
    {
        VkGraphicsProgram sp = Util.AssertSubtype<GraphicsProgram, VkGraphicsProgram>(program);
        if (_currentShaderProgram == sp) return;

        _currentShaderProgram = sp;
        _hasResolvedPipeline = false;
    }

    private protected override void SetComputeShaderCore(ComputeProgram program)
    {
        VkComputeProgram cp = Util.AssertSubtype<ComputeProgram, VkComputeProgram>(program);
        if (_currentComputeProgram == cp) return;

        _currentComputeProgram = cp;
        _gd.Vk.CmdBindPipeline(_cb, PipelineBindPoint.Compute, cp.DevicePipeline);
    }

    private protected override void SetPropertiesCore(PropertySet properties) { }

    // Sets are content-addressed in the cache, so clearing needs no invalidation here.
    private protected override void ClearPropertiesCore() { }

    public override void SetScissorRect(uint x, uint y, uint width, uint height)
    {
        Rect2D scissor = new(new Offset2D((int)x, (int)y), new Extent2D(width, height));
        if (scissor.Equals(_scissorRects[0])) return;

        _scissorRects[0] = scissor;
        _gd.Vk.CmdSetScissor(_cb, 0, 1, in scissor);
    }

    public override void SetViewport(Viewport viewport)
    {
        if (viewport.Equals(_viewports[0])) return;
        _viewports[0] = viewport;

        Silk.NET.Vulkan.Viewport vkViewport = new()
        {
            X = viewport.X,
            Y = viewport.Height + viewport.Y,
            Width = viewport.Width,
            Height = -viewport.Height,
            MinDepth = viewport.MinDepth,
            MaxDepth = viewport.MaxDepth
        };

        _gd.Vk.CmdSetViewport(_cb, 0, 1, in vkViewport);
    }
}
