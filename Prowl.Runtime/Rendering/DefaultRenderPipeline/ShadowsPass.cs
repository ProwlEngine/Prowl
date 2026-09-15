// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite.RenderGraph;

namespace Prowl.Runtime.Rendering;

/// <summary>Shadow map rendering. Placeholder: declares no resources and records nothing yet.</summary>
public sealed class ShadowsPass : IPass<CameraView>
{
    public string Name => "Shadows";

    public void Setup(RenderContextBuilder builder) { }

    public void Render(RenderContext<CameraView> context) { }
}
