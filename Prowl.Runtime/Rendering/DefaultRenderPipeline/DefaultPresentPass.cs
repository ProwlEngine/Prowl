// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;

using Prowl.Runtime.GUI;

using RenderTexture = Prowl.Graphite.RenderTexture;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// The runtime-provided default presenter for <see cref="DefaultRenderPipeline"/>: blits the pipeline's
/// final content into <see cref="CameraView.Target"/>'s framebuffer when set (an offscreen viewport
/// render - editor Scene/Game view, a custom render-to-texture camera), or draws it into the swapchain
/// otherwise (a bare runtime camera with no explicit target). When the pipeline has a
/// <see cref="DefaultRenderPipeline.UIRenderer"/>, its drawn UI is composited on top only in the
/// swapchain case - an offscreen render never gets UI drawn onto it, regardless of the flag.
/// </summary>
public sealed class DefaultPresentPass : IPresentPass<CameraView>
{
    private readonly PaperRenderer<CameraView>? _uiRenderer;

    private TextureHandle _finalHandle;
    private TextureHandle _uiHandle;

    public DefaultPresentPass(PaperRenderer<CameraView>? uiRenderer = null)
    {
        _uiRenderer = uiRenderer;
    }

    public string Name => "Present";

    public void Setup(PresentContextBuilder builder)
    {
        _finalHandle = builder.GetInputTexture(DefaultChain.Final);

        if (_uiRenderer != null)
            _uiHandle = builder.GetInputTexture(_uiRenderer.SceneResourceId);

        // Harmless when unused: SwapchainTarget is only consulted below when the view has no explicit
        // Target, and Present() is what actually arms the present.
        builder.RequestSwapchain();
    }

    public void Present(RenderContext<CameraView> context)
    {
        RenderTexture source = context.GetRenderTexture(_finalHandle);

        Resources.RenderTexture? target = context.View.Target;
        if (target != null)
        {
            // Offscreen viewport - same size/format by construction (GraphTextureDesc.ViewSized off
            // the view's own pixel size), so a raw copy is enough, no shader needed. No UI here even
            // when a UIRenderer is set - only the swapchain-presenting camera gets UI composited in.
            CommandBuffer copyCmd = context.GetCommandBuffer(Name);
            copyCmd.CopyTexture(source.ColorTextures[0], target.MainTexture.Handle);
            context.SubmitCommandBuffer(copyCmd);
            return;
        }

        Framebuffer? swap = context.SwapchainTarget;
        if (swap == null)
            return;

        // The swapchain's format can differ from the chain's (sRGB variants, BGRA, ...), so this goes
        // through a shader blit rather than a raw texture copy.
        CommandBuffer cmd = context.GetCommandBuffer(Name);
        cmd.Blit(source.ColorTextures[0], swap, DefaultRenderPipeline.GetBlitMaterial());

        if (_uiRenderer != null)
        {
            RenderTexture uiScene = context.GetRenderTexture(_uiHandle);
            _uiRenderer.CompositeInto(cmd, uiScene.ColorTextures[0], swap);
        }

        context.SubmitCommandBuffer(cmd);
        context.Present();
    }
}
