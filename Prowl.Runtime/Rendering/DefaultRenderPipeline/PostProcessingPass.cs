// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;

using RenderTexture = Prowl.Graphite.RenderTexture;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Final pass in the chain. Copies the volumetrics chain into its output and marks it as the main
/// output, so the pipeline presents it to the camera target.
/// </summary>
public sealed class PostProcessingPass : CopyChainPass
{
    public PostProcessingPass() : base("PostProcessing", DefaultChain.Final, present: true, inputId: DefaultChain.Volumetrics) { }

    protected override void OnRender(RenderContext<CameraView> context, CommandBuffer cmd, RenderTexture output)
        => EmitPlaceholderCommandBuffers(context, "PostProcessing", 2);
}
