// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;

using RenderTexture = Prowl.Graphite.RenderTexture;

namespace Prowl.Runtime.Rendering;

/// <summary>Blits the scene color into the RGBA8 final target the present pass reads.</summary>
public sealed class FinalBlitPass : RasterPass<CameraView>
{
    private TextureHandle _gbuffer;
    private TextureHandle _final;

    public override string Name => "FinalBlit";

    public override void Setup(RenderContextBuilder builder)
    {
        _gbuffer = builder.GetInputTexture(SceneResources.GBuffer);
        _final = SetTarget(builder, SceneResources.Final, SceneResources.FinalDesc(),
            ops: new TargetLoadStoreOps(AttachmentOps.Cleared, AttachmentOps.Discard));
    }

    public override void Render(RenderContext<CameraView> context)
    {
        SceneTargets targets = SceneTargets.Resolve(context, _gbuffer);
        RenderTexture final = context.GetRenderTexture(_final);

        CommandBuffer cmd = context.GetCommandBuffer(Name);
        BindTarget(context, cmd);
        cmd.Blit(targets.Color, final.Framebuffer, DefaultRenderPipeline.GetBlitMaterial());
        context.SubmitCommandBuffer(cmd);
    }
}
