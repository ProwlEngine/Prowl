using Silk.NET.Vulkan;

using VkSamplerHandle = Silk.NET.Vulkan.Sampler;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkSampler : Sampler
{
    private readonly VkGraphicsDevice _gd;
    private readonly VkSamplerHandle _sampler;

    public VkSamplerHandle DeviceSampler => _sampler;

    public ResourceRefCount RefCount { get; }

    public VkSampler(VkGraphicsDevice gd, ref SamplerDescription description)
    {
        _gd = gd;
        VkFormats.GetFilterParams(description.Filter, out Filter minFilter, out Filter magFilter, out SamplerMipmapMode mipmapMode);

        SamplerCreateInfo samplerCI = new()
        {
            SType = StructureType.SamplerCreateInfo,
            AddressModeU = VkFormats.ToVkSamplerAddressMode(description.AddressModeU),
            AddressModeV = VkFormats.ToVkSamplerAddressMode(description.AddressModeV),
            AddressModeW = VkFormats.ToVkSamplerAddressMode(description.AddressModeW),
            MinFilter = minFilter,
            MagFilter = magFilter,
            MipmapMode = mipmapMode,
            CompareEnable = description.ComparisonKind != null,
            CompareOp = description.ComparisonKind != null
                ? VkFormats.ToVkCompareOp(description.ComparisonKind.Value)
                : CompareOp.Never,
            AnisotropyEnable = description.Filter == SamplerFilter.Anisotropic,
            MaxAnisotropy = description.MaximumAnisotropy,
            MinLod = description.MinimumLod,
            MaxLod = description.MaximumLod,
            MipLodBias = description.LodBias,
            BorderColor = VkFormats.ToVkSamplerBorderColor(description.BorderColor)
        };

        _gd.Vk.CreateSampler(_gd.Device, in samplerCI, null, out _sampler);
        RefCount = new ResourceRefCount(DestroyNative);

        _gd.Profiler?.Allocate(AllocBin.Sampler, 0);
    }

    private protected override void NameChanged(string name) => _gd.SetResourceName(this, name);

    private protected override void DisposeCore()
    {
        RefCount.Decrement();
    }

    private void DestroyNative()
    {
        _gd.Vk.DestroySampler(_gd.Device, _sampler, null);
        _gd.Profiler?.Free(AllocBin.Sampler, 0);
    }
}
