// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite.RenderGraph;

namespace Prowl.Runtime.Rendering;

/// <summary>Post-processing stack. Placeholder: declares no resources and records nothing yet.</summary>
public sealed class PostProcessingPass : IPass<CameraView>
{
    public string Name => "PostProcessing";

    public void Setup(RenderContextBuilder builder) { }

    public void Render(RenderContext<CameraView> context) { }
}
