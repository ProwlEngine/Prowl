using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

// Lets VkDescriptorBinder read the Vulkan-specific descriptor plumbing off a ShaderProgram without
// caring whether it is backing a graphics draw or a compute dispatch.
internal interface IVkDescriptorProgram
{
    DescriptorSetLayout[] DescriptorSetLayouts { get; }
    DescriptorResourceCounts[] PerSetCounts { get; }
    PipelineLayout PipelineLayout { get; }
    uint ResourceSetCount { get; }
    VkDescriptorSetCache DescriptorCache { get; }
}
