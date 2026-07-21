using System;
using System.Diagnostics;

using Prowl.Vector;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkCommandBuffer
{
    private ClearValue[] _clearValues = Array.Empty<ClearValue>();
    private bool[] _validColorClearValues = Array.Empty<bool>();
    private ClearValue? _depthClearValue;

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
        else
        {
            // Queue up the clear value for the next RenderPass.
            _depthClearValue = clearValue;
        }
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
        AddStagingResource(vkFB.RefCount);

        if (fb is VkSwapchainFramebuffer scFB)
        {
            AddStagingResource(scFB.Swapchain.RefCount);
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

        // Barrier so color/depth outputs can be read in subsequent passes.
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
        _gd.Profiler?.RecordBarrier(BarrierBin.MemoryBarrier, 1);
    }
}
