// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite;
using Prowl.Graphite.RenderGraph;

using RenderTexture = Prowl.Graphite.RenderTexture;

namespace Prowl.Runtime.Rendering;

/// <summary>Copies the transparents chain forward. Empty for now.</summary>
public sealed class VolumetricsPass : CopyChainPass
{
    public VolumetricsPass() : base("Volumetrics", DefaultChain.Volumetrics, present: false, inputId: DefaultChain.Transparents) { }

    protected override void OnRender(RenderContext<CameraView> context, CommandBuffer cmd, RenderTexture output)
        => EmitPlaceholderCommandBuffers(context, "Volumetrics", 2);
}
