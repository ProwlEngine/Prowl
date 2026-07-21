// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite.RenderGraph;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Runtime-wide singleton holding the project's active render pipeline. Set by the "Rendering" project
/// setting; falls back to a lazily-created <see cref="DefaultRenderPipeline"/> when no
/// <see cref="RenderPipelineAsset"/> is assigned. This is the only pipeline instance every camera in the
/// scene/editor render loop dispatches through - there is no per-camera override.
/// </summary>
public static class RenderPipelineManager
{
    private static DefaultRenderPipeline? s_default;

    public static AssetRef<RenderPipelineAsset> Asset;

    public static RenderPipeline<CameraView> Current => Asset.Res?.Pipeline ?? (s_default ??= new DefaultRenderPipeline());
}
