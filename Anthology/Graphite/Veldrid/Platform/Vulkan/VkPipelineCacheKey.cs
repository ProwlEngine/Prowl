using System;
using System.Runtime.CompilerServices;

namespace Prowl.Veldrid.Vk;

/// <summary>
/// Composite cache key used by <see cref="VkPipelineCache"/> to resolve a graphics
/// <see cref="Silk.NET.Vulkan.Pipeline"/> at draw time.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Program"/> is compared by reference identity. A <see cref="VkShaderProgram"/>
/// is immutable after construction; its instance identity uniquely determines every piece
/// of program-owned pipeline state (blend, depth-stencil, rasterizer, vertex layouts,
/// resource layouts, descriptor set layouts, pipeline layout, and shader modules).
/// </para>
/// <para>
/// <see cref="Outputs"/> and <see cref="Topology"/> are compared by value.
/// </para>
/// </remarks>
internal readonly struct VkPipelineCacheKey : IEquatable<VkPipelineCacheKey>
{
    /// <summary>The shader program (reference identity).</summary>
    public readonly VkShaderProgram Program;

    /// <summary>The render target output description for the pipeline.</summary>
    public readonly OutputDescription Outputs;

    /// <summary>The primitive topology baked into the pipeline.</summary>
    public readonly PrimitiveTopology Topology;

    public VkPipelineCacheKey(VkShaderProgram program, OutputDescription outputs, PrimitiveTopology topology)
    {
        Program = program;
        Outputs = outputs;
        Topology = topology;
    }

    public bool Equals(VkPipelineCacheKey other)
        => ReferenceEquals(Program, other.Program)
        && Outputs.Equals(other.Outputs)
        && Topology == other.Topology;

    public override bool Equals(object? obj) => obj is VkPipelineCacheKey k && Equals(k);

    public override int GetHashCode()
        => HashCode.Combine(RuntimeHelpers.GetHashCode(Program), Outputs.GetHashCode(), (int)Topology);
}
