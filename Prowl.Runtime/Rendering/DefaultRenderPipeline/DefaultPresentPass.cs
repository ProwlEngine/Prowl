// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;

using Prowl.Runtime.GUI;

using RenderTexture = Prowl.Graphite.RenderTexture;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Copies the pipeline's final color into <see cref="CameraView.Target"/> when set, otherwise blits it to
/// the swapchain and composites the optional UI renderer on top.
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
        _finalHandle = builder.GetInputTexture(ClearPass.Output);

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
            // Offscreen viewport: same size/format by construction, so a raw copy is enough.
            // UI is only composited when presenting to the swapchain.
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
