// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;

using Prowl.Runtime.Resources;

using RenderTexture = Prowl.Graphite.RenderTexture;

namespace Prowl.Runtime.Rendering;

/// <summary>Transparent geometry over the scene color, back to front, then the editor grid.</summary>
public sealed class TransparentsPass : RasterPass<CameraView>
{
    private TextureHandle _gbuffer;
    private TextureHandle _depthCopy;

    public override string Name => "Transparents";

    public override void Setup(RenderContextBuilder builder)
    {
        _depthCopy = builder.GetInputTexture(SceneResources.DepthCopy);
        _gbuffer = SetTargets(builder, SceneResources.GBuffer, SceneResources.GBufferDesc(),
            ops: new TargetLoadStoreOps(AttachmentOps.Loaded, AttachmentOps.Loaded));
    }

    public override void Render(RenderContext<CameraView> context)
    {
        CameraView view = context.View;
        SceneTargets.Resolve(context, _gbuffer);

        CommandBuffer cmd = context.GetCommandBuffer(Name);
        BindTarget(context, cmd);
        cmd.SetProperties(view.FrameProperties);

        Scene? scene = view.Camera.Scene;
        if (scene != null)
            RenderableDrawer.Draw(cmd, view.Camera, scene.Culler.Renderables, view.Cull.Transparent, PassTags.Transparent);

        if (view.Data.DisplayGrid)
        {
            RenderTexture depthCopy = context.GetRenderTexture(_depthCopy);
            GridRenderer.Render(cmd, view.Camera.Transform.Position, depthCopy.DepthTexture!);
        }

        context.SubmitCommandBuffer(cmd);
    }
}
