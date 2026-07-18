// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;

using Prowl.Runtime.Resources;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Copies the shadows chain forward, draws the skybox shader before anything else, then (in the
/// editor) records the gizmos on top.
/// </summary>
public sealed class OpaquePass : CopyChainPass
{
    private Material? s_skybox;

    public OpaquePass() : base("Opaque", DefaultChain.Opaque, present: false, inputId: DefaultChain.Shadows) { }

    protected override void OnRender(RenderContext<DrawCommand> context, CommandBuffer cmd, RenderTexture output)
    {
        Shader? skyShader = Shader.LoadDefault(DefaultShader.ProceduralSkybox);
        if (skyShader.IsValid())
        {
            s_skybox ??= new Material(skyShader);
            cmd.Blit(output, s_skybox);
        }

        if (context.Data.DisplayGizmos)
            GizmoRenderer.Render(cmd);
    }
}
