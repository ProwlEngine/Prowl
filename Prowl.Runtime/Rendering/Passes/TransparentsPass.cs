// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;

using Prowl.Runtime.Resources;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Copies the opaque chain forward, then (in the editor) records the reference grid on top.
/// </summary>
public sealed class TransparentsPass : CopyChainPass
{
    public TransparentsPass() : base("Transparents", DefaultChain.Transparents, present: false, inputId: DefaultChain.Opaque) { }

    protected override void OnRender(RenderContext<DrawCommand> context, CommandBuffer cmd, RenderTexture output)
    {
        if (context.Data.DisplayGrid && context.SceneDepthCopy != null)
        {
            context.BeginSample("Grid");
            GridRenderer.Render(cmd, context.CameraData.CameraPosition, context.SceneDepthCopy);
            context.EndSample();
        }
    }
}
