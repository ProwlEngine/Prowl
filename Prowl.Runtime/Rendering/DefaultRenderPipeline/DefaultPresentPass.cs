// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;

using Prowl.Runtime.GUI;

using RenderTexture = Prowl.Graphite.RenderTexture;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Final pass of <see cref="DefaultRenderPipeline"/>. Blits the pipeline's final color into the view's target, and
/// composites the optional UI renderer on top when the view presents to the swapchain.
/// </summary>
public sealed class DefaultPresentPass : IPass<CameraView>
{
    private readonly PaperRenderer<CameraView>? _uiRenderer;

    private TextureHandle _finalHandle;
    private TextureHandle _uiHandle;
    private TextureHandle _targetHandle;

    public DefaultPresentPass(PaperRenderer<CameraView>? uiRenderer = null)
    {
        _uiRenderer = uiRenderer;
    }

    public string Name => "Present";

    public void Setup(RenderContextBuilder builder)
    {
        _finalHandle = builder.DeclareInputTexture(ClearPass.Output);

        if (_uiRenderer != null)
            _uiHandle = builder.DeclareInputTexture(_uiRenderer.SceneResourceId);

        _targetHandle = builder.DeclareViewTarget();
    }

    public void Render(RenderContext<CameraView> context, CommandBuffer cmd)
    {
        RenderTexture source = context.GetRenderTexture(_finalHandle);
        Framebuffer target = context.GetRenderTexture(_targetHandle).Framebuffer;

        cmd.Blit(source.ColorTextures[0], target, DefaultRenderPipeline.GetBlitMaterial());

        if (_uiRenderer != null && context.View.TargetSwapchain)
        {
            RenderTexture uiScene = context.GetRenderTexture(_uiHandle);
            _uiRenderer.CompositeInto(cmd, uiScene.ColorTextures[0], target);
        }
    }
}
