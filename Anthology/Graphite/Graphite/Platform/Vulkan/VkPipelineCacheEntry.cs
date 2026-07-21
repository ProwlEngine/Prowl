namespace Prowl.Graphite.Vk;

/// <summary>
/// Resolved Vulkan pipeline plus data cached by the program's pipeline cache.
/// </summary>
/// <remarks>
/// Layout, set count, and offset count are copied from the source program so
/// the draw hot path is a plain field read, not a chain of dereferences. Invariant
/// for the program's life, so safe to cache here.
/// </remarks>
internal readonly struct VkPipelineCacheEntry
{
    /// <summary>Graphics pipeline handle owned by the cache.</summary>
    public readonly Silk.NET.Vulkan.Pipeline Pipeline;

    /// <summary>Compatibility render pass for pipeline creation.</summary>
    public readonly Silk.NET.Vulkan.RenderPass CompatRenderPass;

    /// <summary>Program's pipeline layout. Owned by the program, not this entry.</summary>
    public readonly Silk.NET.Vulkan.PipelineLayout PipelineLayout;

    /// <summary>Descriptor set slot count in the layout.</summary>
    public readonly uint ResourceSetCount;

    /// <summary>Total dynamic offsets across all sets.</summary>
    public readonly int DynamicOffsetsCount;

    public VkPipelineCacheEntry(
        Silk.NET.Vulkan.Pipeline pipeline,
        Silk.NET.Vulkan.RenderPass compatRenderPass,
        Silk.NET.Vulkan.PipelineLayout pipelineLayout,
        uint resourceSetCount,
        int dynamicOffsetsCount)
    {
        Pipeline = pipeline;
        CompatRenderPass = compatRenderPass;
        PipelineLayout = pipelineLayout;
        ResourceSetCount = resourceSetCount;
        DynamicOffsetsCount = dynamicOffsetsCount;
    }
}
