// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;

using RenderTexture = Prowl.Graphite.RenderTexture;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Copies scene depth into a sampleable graph texture for the gizmo and grid shaders. Only records when either is displayed.
/// </summary>
public sealed class DepthCopyPass : IPass<CameraView>
{
    private TextureHandle _depthCopy;

    public string Name => "DepthCopy";

    public static bool IsNeeded(in RenderingData data) => data.DisplayGizmos || data.DisplayGrid;

    public void Setup(RenderContextBuilder builder)
    {
        _depthCopy = builder.GetOutputTexture(SceneResources.DepthCopy, SceneResources.DepthCopyDesc(),
            ops: new TargetLoadStoreOps(AttachmentOps.Discard, AttachmentOps.Loaded));
    }

    public void Render(RenderContext<CameraView> context)
    {
        CameraView view = context.View;
        if (!IsNeeded(view.Data) || !view.Targets.IsValid)
            return;

        RenderTexture copy = context.GetRenderTexture(_depthCopy);
        CommandBuffer cmd = context.GetCommandBuffer(Name);
        cmd.CopyTexture(view.Targets.Depth, copy.DepthTexture);
        context.SubmitCommandBuffer(cmd);
    }
}
