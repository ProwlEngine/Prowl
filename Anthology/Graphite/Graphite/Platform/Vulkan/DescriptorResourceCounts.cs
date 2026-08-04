namespace Prowl.Graphite.Vk;

/// <summary>
/// Descriptor counts for one set layout, covering the six descriptor types this backend allocates.
/// Uniform buffers are always dynamic and structured buffers never are, so the plain UniformBuffer
/// and StorageBufferDynamic types never appear.
/// </summary>
internal readonly struct DescriptorResourceCounts(
    uint uniformBufferDynamic,
    uint sampledImage,
    uint sampler,
    uint storageBuffer,
    uint storageImage,
    uint combinedImageSampler)
{
    public readonly uint UniformBufferDynamic = uniformBufferDynamic;
    public readonly uint SampledImage = sampledImage;
    public readonly uint Sampler = sampler;
    public readonly uint StorageBuffer = storageBuffer;
    public readonly uint StorageImage = storageImage;
    public readonly uint CombinedImageSampler = combinedImageSampler;

    public static DescriptorResourceCounts All(uint value) => new(value, value, value, value, value, value);

    public bool Covers(in DescriptorResourceCounts need)
        => UniformBufferDynamic >= need.UniformBufferDynamic
        && SampledImage >= need.SampledImage
        && Sampler >= need.Sampler
        && StorageBuffer >= need.StorageBuffer
        && StorageImage >= need.StorageImage
        && CombinedImageSampler >= need.CombinedImageSampler;

    public static DescriptorResourceCounts operator +(in DescriptorResourceCounts a, in DescriptorResourceCounts b)
        => new(a.UniformBufferDynamic + b.UniformBufferDynamic,
               a.SampledImage + b.SampledImage,
               a.Sampler + b.Sampler,
               a.StorageBuffer + b.StorageBuffer,
               a.StorageImage + b.StorageImage,
               a.CombinedImageSampler + b.CombinedImageSampler);

    public static DescriptorResourceCounts operator -(in DescriptorResourceCounts a, in DescriptorResourceCounts b)
        => new(a.UniformBufferDynamic - b.UniformBufferDynamic,
               a.SampledImage - b.SampledImage,
               a.Sampler - b.Sampler,
               a.StorageBuffer - b.StorageBuffer,
               a.StorageImage - b.StorageImage,
               a.CombinedImageSampler - b.CombinedImageSampler);
}
