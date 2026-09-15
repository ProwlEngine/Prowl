// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;

using Prowl.Runtime.Resources;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Clears the scene gbuffer per the camera's clear flags, draws the skybox when requested, then the scene's opaque geometry.
/// </summary>
public sealed class OpaquePass : RasterPass<CameraView>
{
    private TextureHandle _gbuffer;

    public override string Name => "Opaque";

    public override void Setup(RenderContextBuilder builder)
    {
        _gbuffer = SetTargets(builder, SceneResources.GBuffer, SceneResources.GBufferDesc(),
            ops: new TargetLoadStoreOps(AttachmentOps.Cleared, AttachmentOps.Cleared));
    }

    public override void Render(RenderContext<CameraView> context)
    {
        CameraView view = context.View;
        Camera camera = view.Camera;
        SceneTargets targets = SceneTargets.Resolve(context, _gbuffer);

        CommandBuffer cmd = context.GetCommandBuffer(Name);
        BindTarget(context, cmd, camera.ClearColor);
        cmd.SetProperties(view.FrameProperties);

        switch (camera.ClearFlags)
        {
            case CameraClearFlags.Skybox:
                SkyboxRenderer.Render(cmd);
                break;

            case CameraClearFlags.Depth:
            case CameraClearFlags.Nothing:
                Resources.RenderTexture? target = view.Target;
                if (target != null)
                {
                    cmd.Blit(target.MainTexture.Handle, targets.Framebuffer, DefaultRenderPipeline.GetBlitMaterial());
                    cmd.SetProperties(view.FrameProperties);
                }
                break;
        }

        DrawOpaqueGeometry(context, cmd);

        context.SubmitCommandBuffer(cmd);
    }

    private static void DrawOpaqueGeometry(RenderContext<CameraView> context, CommandBuffer cmd)
    {
        CameraView view = context.View;
        Scene? scene = view.Camera.Scene;
        if (scene == null)
            return;

        IReadOnlyList<IRenderable> renderables = scene.Culler.Renderables;
        RenderableDrawer.EmitCulled(cmd, renderables, view.Cull.Culled);
        RenderableDrawer.Draw(cmd, view.Camera, renderables, view.Cull.Opaque, PassTags.Opaque);
    }
}
