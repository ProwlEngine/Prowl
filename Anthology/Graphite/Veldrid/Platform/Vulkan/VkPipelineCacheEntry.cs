namespace Prowl.Veldrid.Vk;

/// <summary>
/// Resolved Vulkan pipeline + companion data cached by <see cref="VkPipelineCache"/>.
/// </summary>
/// <remarks>
/// <see cref="PipelineLayout"/>, <see cref="ResourceSetCount"/>, and <see cref="DynamicOffsetsCount"/>
/// are copied from the source <see cref="VkShaderProgram"/> so the draw hot path is a single
/// field read rather than a chained property dereference. They are invariant for the program's
/// lifetime, so caching them in the entry is safe.
/// </remarks>
internal readonly struct VkPipelineCacheEntry
{
    /// <summary>The resolved graphics pipeline handle owned by the cache.</summary>
    public readonly Silk.NET.Vulkan.Pipeline Pipeline;

    /// <summary>The compatibility render pass created for <c>vkCreateGraphicsPipelines</c>.</summary>
    public readonly Silk.NET.Vulkan.RenderPass CompatRenderPass;

    /// <summary>The shader program's pipeline layout (owned by the program, not this entry).</summary>
    public readonly Silk.NET.Vulkan.PipelineLayout PipelineLayout;

    /// <summary>Number of descriptor set slots in the pipeline layout.</summary>
    public readonly uint ResourceSetCount;

    /// <summary>Total number of dynamic offsets across all sets in the pipeline layout.</summary>
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
