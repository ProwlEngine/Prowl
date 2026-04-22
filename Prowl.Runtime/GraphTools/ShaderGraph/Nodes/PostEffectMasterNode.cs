// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

/// <summary>
/// Master output for the <c>PostEffect</c> shader type. Fullscreen pass that reads
/// the current scene-color render target (<c>_MainTex</c>) and writes a modified
/// colour back. The graph just computes the output colour — the framework handles
/// the fullscreen vertex transform and sampler binding.
/// </summary>
public sealed class PostEffectMasterNode : MasterNodeBase
{
    public override string Title => "Post Effect Output";

    protected override void DefineNode()
    {
        // The only input — whatever RGB(A) the graph evaluates to for this pixel.
        // Default is (0, 0, 0, 1): fully opaque black if the user wires nothing.
        AddInput<Float4>("Color", new Float4(0f, 0f, 0f, 1f),
            tooltip: "Output colour of the effect. Wire Scene Color in to start with a passthrough.");
    }
}
