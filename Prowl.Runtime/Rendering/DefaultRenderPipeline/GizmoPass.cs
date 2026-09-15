// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;

using RenderTexture = Prowl.Graphite.RenderTexture;

namespace Prowl.Runtime.Rendering;

/// <summary>Draws editor gizmos over the scene color, depth-testing against the scene depth copy.</summary>
public sealed class GizmoPass : RasterPass<CameraView>
{
    private TextureHandle _gbuffer;
    private TextureHandle _depthCopy;

    public override string Name => "Gizmos";

    public override void Setup(RenderContextBuilder builder)
    {
        _depthCopy = builder.GetInputTexture(SceneResources.DepthCopy);
        _gbuffer = SetTargets(builder, SceneResources.GBuffer, SceneResources.GBufferDesc(),
            ops: new TargetLoadStoreOps(AttachmentOps.Loaded, AttachmentOps.Loaded));
    }

    public override void Render(RenderContext<CameraView> context)
    {
        CameraView view = context.View;
        if (!view.Data.DisplayGizmos)
            return;

        SceneTargets.Resolve(context, _gbuffer);
        RenderTexture depthCopy = context.GetRenderTexture(_depthCopy);

        CommandBuffer cmd = context.GetCommandBuffer(Name);
        BindTarget(context, cmd);
        cmd.SetProperties(view.FrameProperties);
        GizmoRenderer.Render(cmd, depthCopy.DepthTexture!);
        context.SubmitCommandBuffer(cmd);
    }
}
