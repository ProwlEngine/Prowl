namespace Prowl.Graphite.Vk;

/// <summary>Resolved Vulkan pipeline plus its compatibility render pass, owned by the program's cache.</summary>
internal readonly struct VkPipelineCacheEntry
{
    public readonly Silk.NET.Vulkan.Pipeline Pipeline;

    public readonly Silk.NET.Vulkan.RenderPass CompatRenderPass;

    public VkPipelineCacheEntry(
        Silk.NET.Vulkan.Pipeline pipeline,
        Silk.NET.Vulkan.RenderPass compatRenderPass)
    {
        Pipeline = pipeline;
        CompatRenderPass = compatRenderPass;
    }
}
