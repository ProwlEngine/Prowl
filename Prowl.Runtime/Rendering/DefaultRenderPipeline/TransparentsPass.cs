// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;

using RenderTexture = Prowl.Graphite.RenderTexture;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Copies the opaque chain forward, then (in the editor) records the reference grid on top.
/// </summary>
public sealed class TransparentsPass : CopyChainPass
{
    public TransparentsPass() : base("Transparents", DefaultChain.Transparents, present: false, inputId: DefaultChain.Opaque) { }

    protected override void OnRender(RenderContext<CameraView> context, CommandBuffer cmd, RenderTexture output)
    {
        CameraView view = context.View;

        if (view.Data.DisplayGrid && view.SceneDepthCopy != null)
            GridRenderer.Render(cmd, view.Camera.Transform.Position, view.SceneDepthCopy);
    }
}
