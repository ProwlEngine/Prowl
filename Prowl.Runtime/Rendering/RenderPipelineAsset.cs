// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Graphite.RenderGraph;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// An asset that provides a <see cref="RenderPipeline{TView}"/> for camera rendering. Assign one via
/// the "Rendering" project setting (see <see cref="RenderPipelineManager"/>) to override the engine's
/// <see cref="DefaultRenderPipeline"/> - this is the only pipeline every camera renders through.
/// </summary>
public abstract class RenderPipelineAsset : EngineObject
{
    public abstract RenderPipeline<CameraView> Pipeline { get; }
}
