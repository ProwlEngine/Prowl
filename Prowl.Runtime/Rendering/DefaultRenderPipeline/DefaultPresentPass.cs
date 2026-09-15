// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;

using Prowl.Runtime.GUI;

using RenderTexture = Prowl.Graphite.RenderTexture;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Blits <see cref="SceneResources.Final"/> into <see cref="CameraView.Target"/> when set, else into the swapchain,
/// compositing the injected UI only in the swapchain case.
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
        _finalHandle = builder.GetInputTexture(SceneResources.Final);

        if (_uiRenderer != null)
            _uiHandle = builder.GetInputTexture(_uiRenderer.SceneResourceId);

        builder.RequestSwapchain();
    }

    public void Present(RenderContext<CameraView> context)
    {
        RenderTexture source = context.GetRenderTexture(_finalHandle);

        Resources.RenderTexture? target = context.View.Target;
        if (target != null)
        {
            CommandBuffer targetCmd = context.GetCommandBuffer(Name);
            targetCmd.Blit(source.ColorTextures[0], target.frameBuffer, DefaultRenderPipeline.GetBlitMaterial());
            context.SubmitCommandBuffer(targetCmd);
            return;
        }

        Framebuffer? swap = context.SwapchainTarget;
        if (swap == null)
            return;

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
