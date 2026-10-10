using System;
using System.Diagnostics;

using Prowl.Graphite.RenderGraph;
using Prowl.Vector;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkCommandBuffer
{
    private TargetLoadStoreOps _pendingOps = DefaultOps;

    private static TargetLoadStoreOps DefaultOps => new(AttachmentOps.Loaded, AttachmentOps.Loaded);

    private static AttachmentLoadOp ToVk(LoadAction action) => action switch
    {
        LoadAction.Clear => AttachmentLoadOp.Clear,
        LoadAction.DontCare => AttachmentLoadOp.DontCare,
        _ => AttachmentLoadOp.Load
    };

    private static AttachmentStoreOp ToVk(StoreAction action)
        => action == StoreAction.DontCare ? AttachmentStoreOp.DontCare : AttachmentStoreOp.Store;

    private protected override void ClearColorTargetCore(uint index, Color clearColor)
    {
        ClearValue clearValue = new()
        {
            Color = new ClearColorValue(clearColor.R, clearColor.G, clearColor.B, clearColor.A)
        };

        EnsureRenderPassActive();
        {
            ClearAttachment clearAttachment = new()
            {
                ColorAttachment = index,
                AspectMask = ImageAspectFlags.ColorBit,
                ClearValue = clearValue
            };

            ClearRect clearRect = new()
            {
                BaseArrayLayer = 0,
                LayerCount = 1,
                Rect = new Rect2D(new Offset2D(0, 0), new Extent2D(_currentFramebuffer.RenderableWidth, _currentFramebuffer.RenderableHeight))
            };

            _gd.Vk.CmdClearAttachments(_cb, 1, in clearAttachment, 1, in clearRect);
        }
    }

    private protected override void ClearDepthStencilCore(float depth, byte stencil)
    {
        ClearValue clearValue = new()
        {
            DepthStencil = new ClearDepthStencilValue(depth, stencil)
        };

        EnsureRenderPassActive();
        {
            if (_currentFramebufferMode == FramebufferMode.GraphDepthReadOnly)
                throw new RenderException("Cannot clear a depth attachment the current pass declared DepthReadOnly.");

            ImageAspectFlags aspect = FormatHelpers.IsStencilFormat(_currentFramebuffer.DepthTarget!.Value.Target.Format)
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
    }

    private protected override void SetFramebufferCore(Framebuffer fb, in TargetLoadStoreOps ops)
    {
        if (_activeRenderPass.Handle != default)
            EndCurrentRenderPass();

        VkFramebufferBase vkFB = Util.AssertSubtype<Framebuffer, VkFramebufferBase>(fb);
        _currentFramebuffer = vkFB;
        _currentFramebufferEverActive = false;
        _hasResolvedPipeline = false;
        _pendingOps = ops;

        if (fb is VkSwapchainFramebuffer scFB && !_usedSwapchains.Contains(scFB.Swapchain))
            _usedSwapchains.Add(scFB.Swapchain);
    }

    private FramebufferMode ResolveFramebufferMode(VkFramebufferBase fb)
    {
        int total = 0;
        int graph = 0;
        bool depthReadOnly = false;
        foreach (FramebufferAttachment attachment in fb.ColorTargets)
            CountAttachment(attachment.Target, isDepth: false, ref total, ref graph, ref depthReadOnly);
        if (fb.DepthTarget is FramebufferAttachment depth)
            CountAttachment(depth.Target, isDepth: true, ref total, ref graph, ref depthReadOnly);

        if (graph != 0 && graph != total)
        {
            throw new RenderException(
                "A framebuffer cannot mix graph attachments declared by the current pass with textures outside the graph.");
        }

        if (graph == 0)
            return FramebufferMode.Resting;
        return depthReadOnly ? FramebufferMode.GraphDepthReadOnly : FramebufferMode.Graph;
    }

    private void CountAttachment(Texture texture, bool isDepth, ref int total, ref int graph, ref bool depthReadOnly)
    {
        total++;
        TextureState? state = StateOf(texture);
        if (state == TextureState.Attachment || (isDepth && state == TextureState.DepthReadOnly))
        {
            graph++;
            depthReadOnly |= state == TextureState.DepthReadOnly;
        }
        else if (state != null)
        {
            throw new RenderException(
                $"Texture '{texture.Name}' is in state {state} for the current pass and cannot be a framebuffer attachment. " +
                "Declare it as an Attachment (or DepthReadOnly depth), or transition it first.");
        }
    }

    internal override uint RecordBarriers(ReadOnlySpan<TextureBarrier> textures, BufferAccess bufferSrc, BufferAccess bufferDst)
    {
        SealRenderPass();
        return VkBarriers.Record(_gd, _cb, textures, bufferSrc, bufferDst);
    }

    private void SealRenderPass()
    {
        if (_activeRenderPass.Handle == default && !_currentFramebufferEverActive && _currentFramebuffer != null)
            BeginCurrentRenderPass();
        EnsureNoRenderPass();
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

        _currentFramebufferMode = ResolveFramebufferMode(_currentFramebuffer);
        bool hasDepth = _currentFramebuffer.DepthTarget != null;
        bool clearsDepth = hasDepth && _pendingOps.Depth.Load == LoadAction.Clear;
        if (_currentFramebufferMode == FramebufferMode.GraphDepthReadOnly && clearsDepth)
            throw new RenderException("Cannot clear a depth attachment the current pass declared DepthReadOnly.");

        RenderPassOps passOps = new(
            ToVk(_pendingOps.Color.Load),
            ToVk(_pendingOps.Depth.Load),
            ToVk(_pendingOps.Color.Store),
            ToVk(_pendingOps.Depth.Store));

        int colorCount = _currentFramebuffer.ColorTargets.Count;
        Span<ClearValue> clearValues = stackalloc ClearValue[(int)_currentFramebuffer.AttachmentCount + 1];
        Color clearColor = _pendingOps.Color.ClearColor;
        for (int i = 0; i < colorCount; i++)
            clearValues[i].Color = new ClearColorValue(clearColor.R, clearColor.G, clearColor.B, clearColor.A);
        if (hasDepth)
            clearValues[colorCount].DepthStencil = new ClearDepthStencilValue(_pendingOps.Depth.ClearDepth, _pendingOps.Depth.ClearStencil);

        fixed (ClearValue* clearValuesPtr = clearValues)
        {
            RenderPassBeginInfo renderPassBI = new()
            {
                SType = StructureType.RenderPassBeginInfo,
                RenderArea = new Rect2D(new Offset2D(0, 0), new Extent2D(_currentFramebuffer.RenderableWidth, _currentFramebuffer.RenderableHeight)),
                Framebuffer = _currentFramebuffer.CurrentFramebuffer,
                RenderPass = _currentFramebuffer.GetRenderPass(_currentFramebufferMode, passOps),
                ClearValueCount = _currentFramebuffer.AttachmentCount,
                PClearValues = clearValuesPtr
            };

            _gd.Vk.CmdBeginRenderPass(_cb, in renderPassBI, SubpassContents.Inline);
            _activeRenderPass = renderPassBI.RenderPass;
        }

        _pendingOps.Color.Load = LoadAction.Load;
        _pendingOps.Depth.Load = LoadAction.Load;
    }

    private void EndCurrentRenderPass()
    {
        Debug.Assert(_activeRenderPass.Handle != default);
        _gd.Vk.CmdEndRenderPass(_cb);
        _activeRenderPass = default;
    }
}
