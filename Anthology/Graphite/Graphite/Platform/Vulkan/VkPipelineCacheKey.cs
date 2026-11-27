using System;

namespace Prowl.Graphite.Vk;

/// <summary>
/// Composite key for the program's pipeline cache, resolves a pipeline at draw time.
/// </summary>
/// <remarks>
/// Program already owns blend, depth-stencil, rasterizer, layouts, and shader modules,
/// so the key only needs per-draw varying state. Outputs and Topology compared by value.
/// </remarks>
internal readonly record struct VkPipelineCacheKey
{
    /// <summary>Render target output description.</summary>
    public readonly OutputDescription Outputs;

    /// <summary>Primitive topology baked into the pipeline.</summary>
    public readonly PrimitiveTopology Topology;

    public VkPipelineCacheKey(OutputDescription outputs, PrimitiveTopology topology)
    {
        Outputs = outputs;
        Topology = topology;
    }
}
