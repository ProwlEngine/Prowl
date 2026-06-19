using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

/// <summary>
/// Builds and tears down the descriptor-set-layout + pipeline-layout state shared by
/// <see cref="VkGraphicsProgram"/> and <see cref="VkComputeProgram"/>. Both program kinds derive
/// from different core bases, so this logic is shared by composition rather than inheritance.
/// </summary>
internal static unsafe partial class VkDescriptorLayoutBuilder
{
    /// <summary>
    /// Creates one descriptor-set layout per declared set (empty layouts fill gaps), the owning
    /// pipeline layout, and the per-set resource counts for the supplied resource layouts.
    /// </summary>
    public static (
        DescriptorSetLayout[] DescriptorSetLayouts,
        DescriptorResourceCounts[] PerSetCounts,
        PipelineLayout PipelineLayout,
        uint ResourceSetCount,
        int TotalDynamicUboCount,
        DescriptorSetLayout EmptyDescriptorSetLayout)
        Build(VkGraphicsDevice gd, ResourceLayoutDescription[] descs)
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
        int dynamicTotal = 0;
        for (int i = 0; i < setCount; i++)
        {
            if (dsls[i].Handle == 0)
            {
                if (emptyDsl.Handle == 0)
                    emptyDsl = CreateEmptyDescriptorSetLayout(gd);
                dsls[i] = emptyDsl;
            }
            dynamicTotal += (int)counts[i].UniformBufferDynamicCount;
        }

        PipelineLayout pipelineLayout = BuildPipelineLayout(gd, dsls, setCount);
        return (dsls, counts, pipelineLayout, setCount, dynamicTotal, emptyDsl);
    }

    /// <summary>
    /// Destroys the pipeline layout and every descriptor-set layout built by <see cref="Build"/>,
    /// including the shared empty layout. The caller owns disposal of pipelines and shader modules.
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
            gd.RecordFree(AllocBin.ResourceLayout, 0);
        }

        foreach (DescriptorSetLayout dsl in descriptorSetLayouts)
        {
            if (dsl.Handle != 0 && dsl.Handle != emptyDescriptorSetLayout.Handle)
            {
                gd.Vk.DestroyDescriptorSetLayout(gd.Device, dsl, null);
                gd.RecordFree(AllocBin.ResourceLayout, 0);
            }
        }
    }

    private static (DescriptorSetLayout dsl, DescriptorResourceCounts counts) CreateDescriptorSetLayout(
        VkGraphicsDevice gd, ref ResourceLayoutDescription desc)
    {
        ResourceLayoutElementDescription[] elems = desc.Elements;
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[elems.Length];

        uint uniformBufferDynamic = 0, sampledImage = 0, sampler = 0, storageBuffer = 0, storageImage = 0;

        for (int i = 0; i < elems.Length; i++)
        {
            ref ResourceLayoutElementDescription elem = ref elems[i];
            DescriptorType descType = GetDescriptorType(elem.Kind);
            bindings[i] = new DescriptorSetLayoutBinding
            {
                Binding = (uint)elem.BindingIndex,
                DescriptorType = descType,
                DescriptorCount = 1,
                StageFlags = VkFormats.VdToVkShaderStages(elem.Stages),
            };
            switch (descType)
            {
                case DescriptorType.UniformBufferDynamic: uniformBufferDynamic++; break;
                case DescriptorType.SampledImage: sampledImage++; break;
                case DescriptorType.Sampler: sampler++; break;
                case DescriptorType.StorageBuffer: storageBuffer++; break;
                case DescriptorType.StorageImage: storageImage++; break;
            }
        }

        DescriptorSetLayoutCreateInfo dslCI = new()
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = (uint)elems.Length,
            PBindings = bindings,
        };
        gd.Vk.CreateDescriptorSetLayout(gd.Device, in dslCI, null, out DescriptorSetLayout dsl).CheckResult();
        gd.RecordAllocation(AllocBin.ResourceLayout, 0);

        return (dsl, new DescriptorResourceCounts(0, uniformBufferDynamic, sampledImage, sampler, storageBuffer, 0, storageImage));
    }

    /// <summary>
    /// Returns the Vulkan descriptor type for the given <see cref="ResourceKind"/>.
    /// All uniform buffers are <c>UNIFORM_BUFFER_DYNAMIC</c>; textures use separate image/sampler descriptors.
    /// </summary>
    private static DescriptorType GetDescriptorType(ResourceKind kind) => kind switch
    {
        ResourceKind.UniformBuffer => DescriptorType.UniformBufferDynamic,
        ResourceKind.StructuredBufferReadOnly => DescriptorType.StorageBuffer,
        ResourceKind.StructuredBufferReadWrite => DescriptorType.StorageBuffer,
        ResourceKind.TextureReadOnly => DescriptorType.SampledImage,
        ResourceKind.TextureReadWrite => DescriptorType.StorageImage,
        ResourceKind.Sampler => DescriptorType.Sampler,
        _ => throw Illegal.Value<ResourceKind>(),
    };

    private static PipelineLayout BuildPipelineLayout(VkGraphicsDevice gd, DescriptorSetLayout[] dsls, uint setCount)
    {
        DescriptorSetLayout* dslsPtr = stackalloc DescriptorSetLayout[(int)setCount];
        for (int i = 0; i < setCount; i++) dslsPtr[i] = dsls[i];

        PipelineLayoutCreateInfo plCI = new()
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = setCount,
            PSetLayouts = dslsPtr,
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
        gd.RecordAllocation(AllocBin.ResourceLayout, 0);
        return dsl;
    }
}
