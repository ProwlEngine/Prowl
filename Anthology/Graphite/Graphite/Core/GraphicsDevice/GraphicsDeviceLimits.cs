using System;

namespace Prowl.Graphite;

/// <summary>
/// Descriptors of each kind, as a pipeline layout holds them or as a device limits them.
/// </summary>
/// <param name="Samplers">Samplers, including the sampler half of combined image samplers.</param>
/// <param name="UniformBuffers">Uniform buffers.</param>
/// <param name="UniformBuffersDynamic">Uniform buffers with a dynamic offset, which every Graphite uniform buffer is.</param>
/// <param name="StorageBuffers">Structured buffers.</param>
/// <param name="SampledImages">Read only textures, including the image half of combined image samplers.</param>
/// <param name="StorageImages">Read write textures.</param>
/// <param name="Resources">Every descriptor but samplers.</param>
public readonly record struct DescriptorCounts(uint Samplers, uint UniformBuffers, uint UniformBuffersDynamic, uint StorageBuffers, uint SampledImages, uint StorageImages, uint Resources)
{
    /// <summary>No limit on any kind.</summary>
    public static DescriptorCounts Unlimited => new(uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue, uint.MaxValue);

    internal string? Exceeding(in DescriptorCounts limit)
    {
        if (Samplers > limit.Samplers) return $"{Samplers} samplers, the limit is {limit.Samplers}";
        if (UniformBuffers > limit.UniformBuffers) return $"{UniformBuffers} uniform buffers, the limit is {limit.UniformBuffers}";
        if (UniformBuffersDynamic > limit.UniformBuffersDynamic) return $"{UniformBuffersDynamic} dynamic uniform buffers, the limit is {limit.UniformBuffersDynamic}";
        if (StorageBuffers > limit.StorageBuffers) return $"{StorageBuffers} storage buffers, the limit is {limit.StorageBuffers}";
        if (SampledImages > limit.SampledImages) return $"{SampledImages} sampled images, the limit is {limit.SampledImages}";
        if (StorageImages > limit.StorageImages) return $"{StorageImages} storage images, the limit is {limit.StorageImages}";
        if (Resources > limit.Resources) return $"{Resources} resources, the limit is {limit.Resources}";
        return null;
    }
}

/// <summary>
/// Limits of a device that differ between GPUs, mobile ones especially.
/// </summary>
public sealed class GraphicsDeviceLimits
{
    /// <summary>Largest width or height of a 2D texture.</summary>
    public uint MaxTextureDimension2D { get; init; } = 4096;

    /// <summary>Components a vertex stage can write to its outputs, four per location.</summary>
    public uint MaxVertexOutputComponents { get; init; } = 64;

    /// <summary>Components a fragment stage can read from its inputs, four per location.</summary>
    public uint MaxFragmentInputComponents { get; init; } = 64;

    /// <summary>Colour targets a framebuffer can have.</summary>
    public uint MaxColorAttachments { get; init; } = 4;

    /// <summary>Descriptor sets one program can use, so set indices go up to one less.</summary>
    public uint MaxBoundDescriptorSets { get; init; } = 4;

    /// <summary>Descriptors one stage can read from the sets that are not descriptor tables.</summary>
    public DescriptorCounts PerStage { get; init; } = DescriptorCounts.Unlimited;

    /// <summary>Descriptors one stage can read from every set, descriptor tables included, when any set is a table.</summary>
    public DescriptorCounts PerStageWithTables { get; init; } = DescriptorCounts.Unlimited;

    /// <summary>Descriptors one program can hold across every stage in the sets that are not descriptor tables.</summary>
    public DescriptorCounts PerProgram { get; init; } = DescriptorCounts.Unlimited;

    /// <summary>Descriptors one program can hold across every stage in every set, descriptor tables included, when any set is a table.</summary>
    public DescriptorCounts PerProgramWithTables { get; init; } = DescriptorCounts.Unlimited;

    /// <summary>
    /// What a program with these resource layouts would hold past the device's limits, or null when it fits.
    /// Colour targets, which count as fragment stage resources too, are not included.
    /// </summary>
    public string? Exceeded(ReadOnlySpan<ResourceLayoutDescription> layouts)
    {
        uint sets = 0;
        bool tables = false;
        foreach (ResourceLayoutDescription layout in layouts)
        {
            sets = Math.Max(sets, layout.Set + 1);
            tables |= layout.Table;
        }
        if (sets > MaxBoundDescriptorSets)
            return $"{sets} descriptor sets, the device binds at most {MaxBoundDescriptorSets}";

        for (int bit = 0; bit < 8; bit++)
        {
            ShaderStages stage = (ShaderStages)(1 << bit);
            string? error = Count(layouts, stage, tables: false).Exceeding(PerStage) ?? (tables ? Count(layouts, stage, tables: true).Exceeding(PerStageWithTables) : null);
            if (error != null)
                return $"the {stage} stage reads {error}";
        }

        string? total = Count(layouts, ShaderStages.None, tables: false).Exceeding(PerProgram) ?? (tables ? Count(layouts, ShaderStages.None, tables: true).Exceeding(PerProgramWithTables) : null);
        return total == null ? null : $"the program holds {total}";
    }

    // Counts the descriptors a stage reads, or every descriptor for None
    private static DescriptorCounts Count(ReadOnlySpan<ResourceLayoutDescription> layouts, ShaderStages stage, bool tables)
    {
        uint samplers = 0, uniform = 0, storage = 0, sampled = 0, images = 0;
        foreach (ResourceLayoutDescription layout in layouts)
        {
            if (layout.Table && !tables)
                continue;
            foreach (ResourceLayoutElementDescription element in layout.Elements)
            {
                if (stage != ShaderStages.None && (element.Stages & stage) == 0)
                    continue;
                uint count = element.Count;
                switch (element.Kind)
                {
                    case ResourceKind.UniformBuffer:
                        uniform += count;
                        break;
                    case ResourceKind.StructuredBufferReadOnly or ResourceKind.StructuredBufferReadWrite:
                        storage += count;
                        break;
                    case ResourceKind.TextureReadOnly:
                        sampled += count;
                        if ((element.Options & ResourceLayoutElementOptions.CombinedImageSampler) != 0)
                            samplers += count;
                        break;
                    case ResourceKind.TextureReadWrite:
                        images += count;
                        break;
                    case ResourceKind.Sampler:
                        samplers += count;
                        break;
                }
            }
        }
        return new DescriptorCounts(samplers, uniform, uniform, storage, sampled, images, uniform + storage + sampled + images);
    }
}
