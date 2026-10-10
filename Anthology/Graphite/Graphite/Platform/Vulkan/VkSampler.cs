using Silk.NET.Vulkan;

using VkSamplerHandle = Silk.NET.Vulkan.Sampler;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkSampler : Sampler
{
    private readonly VkGraphicsDevice _gd;
    private readonly VkSamplerHandle _sampler;

    public VkSamplerHandle DeviceSampler => _sampler;


    public VkSampler(VkGraphicsDevice gd, in SamplerDescription description)
        : base(description)
    {
        _gd = gd;

        SamplerCreateInfo samplerCI = new()
        {
            SType = StructureType.SamplerCreateInfo,
            AddressModeU = VkFormats.ToVkSamplerAddressMode(description.AddressModeU),
            AddressModeV = VkFormats.ToVkSamplerAddressMode(description.AddressModeV),
            AddressModeW = VkFormats.ToVkSamplerAddressMode(description.AddressModeW),
            MinFilter = VkFormats.ToVkFilter(description.MinFilter),
            MagFilter = VkFormats.ToVkFilter(description.MagFilter),
            MipmapMode = VkFormats.ToVkMipmapMode(description.MipFilter),
            CompareEnable = description.ComparisonKind != null,
            CompareOp = description.ComparisonKind != null
                ? VkFormats.ToVkCompareOp(description.ComparisonKind.Value)
                : CompareOp.Never,
            AnisotropyEnable = description.MaximumAnisotropy > 1,
            MaxAnisotropy = description.MaximumAnisotropy,
            MinLod = description.MinimumLod,
            MaxLod = description.MaximumLod,
            MipLodBias = description.LodBias,
            BorderColor = VkFormats.ToVkSamplerBorderColor(description.BorderColor)
        };

        _gd.Vk.CreateSampler(_gd.Device, in samplerCI, null, out _sampler);

        _gd.Counters.Allocate(AllocBin.Sampler);
    }

    private protected override void NameChanged(string name) => _gd.SetResourceName(this, name);

    private protected override void DisposeCore()
    {
        _gd.DisposeWhenRetired(DestroyNative);
    }

    private void DestroyNative()
    {
        _gd.Vk.DestroySampler(_gd.Device, _sampler, null);
        _gd.Counters.Free(AllocBin.Sampler);
    }
}
