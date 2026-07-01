using System;

namespace Prowl.Graphite.Vk;

/// <summary>
/// Composite cache key used by <see cref="VkGraphicsProgram"/>'s per-program pipeline cache to
/// resolve a graphics <see cref="Silk.NET.Vulkan.Pipeline"/> at draw time.
/// </summary>
/// <remarks>
/// The owning program already determines every piece of program-owned pipeline state (blend,
/// depth-stencil, rasterizer, vertex layouts, resource layouts, descriptor set layouts, pipeline
/// layout, and shader modules), so the key only needs the per-draw varying state.
/// <see cref="Outputs"/> and <see cref="Topology"/> are compared by value.
/// </remarks>
internal readonly struct VkPipelineCacheKey : IEquatable<VkPipelineCacheKey>
{
    /// <summary>The render target output description for the pipeline.</summary>
    public readonly OutputDescription Outputs;

    /// <summary>The primitive topology baked into the pipeline.</summary>
    public readonly PrimitiveTopology Topology;

    public VkPipelineCacheKey(OutputDescription outputs, PrimitiveTopology topology)
    {
        Outputs = outputs;
        Topology = topology;
    }

    public bool Equals(VkPipelineCacheKey other)
        => Outputs.Equals(other.Outputs)
        && Topology == other.Topology;

    public override bool Equals(object? obj) => obj is VkPipelineCacheKey k && Equals(k);

    public override int GetHashCode()
        => HashCode.Combine(Outputs.GetHashCode(), (int)Topology);
}
