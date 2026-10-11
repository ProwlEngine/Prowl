using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

/// <summary>
/// Shared descriptor-set-layout and pipeline-layout builder for graphics and compute programs.
/// Composition not inheritance since they derive from different bases.
/// </summary>
internal static unsafe partial class VkDescriptorLayoutBuilder
{
    /// <summary>
    /// Builds one descriptor-set layout per declared set (gaps filled with empty layouts), the
    /// pipeline layout, and per-set resource counts.
    /// </summary>
    public static (
        DescriptorSetLayout[] DescriptorSetLayouts,
        DescriptorResourceCounts[] PerSetCounts,
        PipelineLayout PipelineLayout,
        uint ResourceSetCount,
        DescriptorSetLayout EmptyDescriptorSetLayout)
        Build(VkGraphicsDevice gd, ResourceLayoutDescription[] descs, PushConstantRange pushConstants)
    {
        uint maxSet = 0;
        bool any = false;
        for (int i = 0; i < descs.Length; i++)
        {
            if (!any || descs[i].Set > maxSet) maxSet = descs[i].Set;
            any = true;
        }
        uint setCount = any ? maxSet + 1 : 0;
        DescriptorSetLayout[] dsls = new DescriptorSetLayout[setCount];
        DescriptorResourceCounts[] counts = new DescriptorResourceCounts[setCount];

        for (int i = 0; i < descs.Length; i++)
        {
            uint set = descs[i].Set;
            if (dsls[set].Handle != 0)
                throw new RenderException($"Multiple ResourceLayouts share Set index {set}.");
            (dsls[set], counts[set]) = CreateDescriptorSetLayout(gd, ref descs[i]);
        }

        DescriptorSetLayout emptyDsl = default;
        for (int i = 0; i < setCount; i++)
        {
            if (dsls[i].Handle == 0)
            {
                if (emptyDsl.Handle == 0)
                    emptyDsl = CreateEmptyDescriptorSetLayout(gd);
                dsls[i] = emptyDsl;
            }
        }

        PipelineLayout pipelineLayout = BuildPipelineLayout(gd, dsls, setCount, pushConstants);
        return (dsls, counts, pipelineLayout, setCount, emptyDsl);
    }

    /// <summary>
    /// Destroys the pipeline layout and all descriptor-set layouts from Build, including the empty
    /// one. Caller still owns disposing pipelines and shader modules.
    /// </summary>
    public static void Destroy(
        VkGraphicsDevice gd,
        DescriptorSetLayout[] descriptorSetLayouts,
        DescriptorSetLayout emptyDescriptorSetLayout,
        PipelineLayout pipelineLayout)
    {
        gd.Vk.DestroyPipelineLayout(gd.Device, pipelineLayout, null);

        if (emptyDescriptorSetLayout.Handle != 0)
        {
            gd.Vk.DestroyDescriptorSetLayout(gd.Device, emptyDescriptorSetLayout, null);
            gd.Counters.Free(AllocBin.ResourceLayout);
        }

        foreach (DescriptorSetLayout dsl in descriptorSetLayouts)
        {
            if (dsl.Handle != 0 && dsl.Handle != emptyDescriptorSetLayout.Handle)
            {
                gd.Vk.DestroyDescriptorSetLayout(gd.Device, dsl, null);
                gd.Counters.Free(AllocBin.ResourceLayout);
            }
        }
    }

    internal static (DescriptorSetLayout dsl, DescriptorResourceCounts counts) CreateDescriptorSetLayout(
        VkGraphicsDevice gd, ref ResourceLayoutDescription desc)
    {
        ResourceLayoutElementDescription[] elems = desc.Elements;

        if (elems.Length > VkDescriptorBinder.MaxSetElements)
        {
            throw new RenderException(
                $"ResourceLayout for Set {desc.Set} declares {elems.Length} elements but at most " +
                $"{VkDescriptorBinder.MaxSetElements} are supported per set. Split the resources across additional sets.");
        }

        CheckTable(gd, in desc);

        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[elems.Length];
        DescriptorBindingFlags* bindingFlags = stackalloc DescriptorBindingFlags[elems.Length];

        uint uniformBufferDynamic = 0, sampledImage = 0, sampler = 0, storageBuffer = 0, storageImage = 0, combinedImageSampler = 0;

        for (int i = 0; i < elems.Length; i++)
        {
            ref ResourceLayoutElementDescription elem = ref elems[i];
            DescriptorType descType = GetDescriptorType(elem.Kind, elem.Options);
            bindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = (uint)elem.BindingIndex,
                DescriptorType = descType,
                DescriptorCount = elem.Count,
                StageFlags = VkFormats.ToVkShaderStages(elem.Stages),
            };
            bindingFlags[i] = DescriptorBindingFlags.PartiallyBoundBit | DescriptorBindingFlags.UpdateAfterBindBit | DescriptorBindingFlags.UpdateUnusedWhilePendingBit;
            switch (descType)
            {
                case DescriptorType.UniformBufferDynamic: uniformBufferDynamic++; break;
                case DescriptorType.SampledImage: sampledImage++; break;
                case DescriptorType.Sampler: sampler++; break;
                case DescriptorType.StorageBuffer: storageBuffer++; break;
                case DescriptorType.StorageImage: storageImage++; break;
                case DescriptorType.CombinedImageSampler: combinedImageSampler++; break;
            }
        }

        DescriptorSetLayoutBindingFlagsCreateInfo flagsCI = new()
        {
            SType = StructureType.DescriptorSetLayoutBindingFlagsCreateInfo,
            BindingCount = (uint)elems.Length,
            PBindingFlags = bindingFlags,
        };
        DescriptorSetLayoutCreateInfo dslCI = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = (uint)elems.Length,
            PBindings = bindings,
            PNext = desc.Table ? &flagsCI : null,
            Flags = desc.Table ? DescriptorSetLayoutCreateFlags.UpdateAfterBindPoolBit : 0,
        };
        gd.Vk.CreateDescriptorSetLayout(gd.Device, in dslCI, null, out DescriptorSetLayout dsl).CheckResult();
        gd.Counters.Allocate(AllocBin.ResourceLayout);

        return (dsl, new DescriptorResourceCounts(uniformBufferDynamic, sampledImage, sampler, storageBuffer, storageImage, combinedImageSampler));
    }

    /// <summary>
    /// Maps a resource kind to its Vulkan descriptor type. Uniform buffers are always dynamic;
    /// textures are separate image/sampler unless CombinedImageSampler is flagged.
    /// </summary>
    private static void CheckTable(VkGraphicsDevice gd, in ResourceLayoutDescription desc)
    {
        if (desc.Table && !gd.Features.DescriptorIndexing)
            throw new RenderException($"Set {desc.Set} is a descriptor table, which needs GraphicsDeviceFeatures.DescriptorIndexing.");

        foreach (ResourceLayoutElementDescription element in desc.Elements)
        {
            if (element.Count > 1 && !desc.Table)
                throw new RenderException($"'{PropertyID.ToString(element.Name)}' is an array of {element.Count}, which only a descriptor table set (ResourceLayoutDescription.Table) can hold.");
            if (desc.Table && element.Kind == ResourceKind.UniformBuffer)
                throw new RenderException($"'{PropertyID.ToString(element.Name)}' is a uniform buffer, which a descriptor table cannot hold, use a storage buffer.");
        }
    }

    internal static DescriptorType GetDescriptorType(ResourceKind kind, ResourceLayoutElementOptions options) => kind switch
    {
        ResourceKind.UniformBuffer => DescriptorType.UniformBufferDynamic,
        ResourceKind.StructuredBufferReadOnly => DescriptorType.StorageBuffer,
        ResourceKind.StructuredBufferReadWrite => DescriptorType.StorageBuffer,
        ResourceKind.TextureReadOnly => (options & ResourceLayoutElementOptions.CombinedImageSampler) != 0
            ? DescriptorType.CombinedImageSampler
            : DescriptorType.SampledImage,
        ResourceKind.TextureReadWrite => DescriptorType.StorageImage,
        ResourceKind.Sampler => DescriptorType.Sampler,
        _ => throw Illegal.Value<ResourceKind>(),
    };

    private static PipelineLayout BuildPipelineLayout(VkGraphicsDevice gd, DescriptorSetLayout[] dsls, uint setCount, PushConstantRange pushConstants)
    {
        DescriptorSetLayout* dslsPtr = stackalloc DescriptorSetLayout[(int)setCount];
        for (int i = 0; i < setCount; i++) dslsPtr[i] = dsls[i];

        Silk.NET.Vulkan.PushConstantRange range = new(VkFormats.ToVkShaderStages(pushConstants.Stages), 0, pushConstants.SizeInBytes);
        PipelineLayoutCreateInfo plCI = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = setCount,
            PSetLayouts = dslsPtr,
            PushConstantRangeCount = pushConstants.SizeInBytes > 0 ? 1u : 0u,
            PPushConstantRanges = &range,
        };
        gd.Vk.CreatePipelineLayout(gd.Device, in plCI, null, out PipelineLayout layout).CheckResult();
        return layout;
    }

    private static DescriptorSetLayout CreateEmptyDescriptorSetLayout(VkGraphicsDevice gd)
    {
        DescriptorSetLayoutCreateInfo dslCI = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 0,
            PBindings = null,
        };
        gd.Vk.CreateDescriptorSetLayout(gd.Device, in dslCI, null, out DescriptorSetLayout dsl).CheckResult();
        gd.Counters.Allocate(AllocBin.ResourceLayout);
        return dsl;
    }
}
