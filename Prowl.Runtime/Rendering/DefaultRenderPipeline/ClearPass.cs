// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;

using RenderTexture = Prowl.Graphite.RenderTexture;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Produces the view-sized camera color/depth target cleared to the camera's clear color.
/// </summary>
public sealed class ClearPass : IPass<CameraView>
{
    public const string Output = "_CameraColor";

    private TextureHandle _outputHandle;

    public string Name => "Clear";

    public void Setup(RenderContextBuilder builder)
    {
        _outputHandle = builder.GetOutputTexture(Output, GraphTextureDesc.ViewSized(depth: true));
    }

    public void Render(RenderContext<CameraView> context)
    {
        RenderTexture output = context.GetRenderTexture(_outputHandle);
        CommandBuffer cmd = context.GetCommandBuffer(Name);

        cmd.SetFramebuffer(output.Framebuffer);
        cmd.SetProperties(context.View.FrameProperties);
        cmd.ClearColorTarget(0, context.View.Camera.ClearColor);
        cmd.ClearDepthStencil(1f, 0);

        context.SubmitCommandBuffer(cmd);
    }
}
