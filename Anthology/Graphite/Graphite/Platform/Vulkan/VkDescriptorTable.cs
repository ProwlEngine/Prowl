using System.Collections.Generic;

using Silk.NET.Vulkan;

namespace Prowl.Graphite.Vk;

internal sealed unsafe class VkDescriptorTable : DescriptorTable
{
    private readonly VkGraphicsDevice _gd;
    private readonly DescriptorSetLayout _layout;
    private readonly DescriptorPool _pool;

    internal DescriptorSet Set { get; }

    public VkDescriptorTable(VkGraphicsDevice gd, in ResourceLayoutDescription layout)
        : base(layout)
    {
        _gd = gd;
        ResourceLayoutDescription description = Layout;
        (_layout, _) = VkDescriptorLayoutBuilder.CreateDescriptorSetLayout(gd, ref description);

        Dictionary<DescriptorType, uint> counts = new();
        foreach (ResourceLayoutElementDescription element in description.Elements)
        {
            DescriptorType type = VkDescriptorLayoutBuilder.GetDescriptorType(element.Kind, element.Options);
            counts[type] = counts.GetValueOrDefault(type) + element.Count;
        }

        DescriptorPoolSize* sizes = stackalloc DescriptorPoolSize[counts.Count];
        int n = 0;
        foreach ((DescriptorType type, uint count) in counts)
            sizes[n++] = new DescriptorPoolSize(type, count);

        DescriptorPoolCreateInfo poolCI = new()
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            Flags = DescriptorPoolCreateFlags.UpdateAfterBindBit,
            MaxSets = 1,
            PoolSizeCount = (uint)n,
            PPoolSizes = sizes,
        };
        _gd.Vk.CreateDescriptorPool(_gd.Device, in poolCI, null, out _pool).CheckResult();

        DescriptorSetLayout setLayout = _layout;
        DescriptorSetAllocateInfo allocateInfo = new()
        {
            SType = StructureType.DescriptorSetAllocateInfo,
            DescriptorPool = _pool,
            DescriptorSetCount = 1,
            PSetLayouts = &setLayout,
        };
        _gd.Vk.AllocateDescriptorSets(_gd.Device, in allocateInfo, out DescriptorSet set).CheckResult();
        Set = set;
    }

    private protected override void WriteCore(int element, in ResourceLayoutElementDescription description, uint index, in DescriptorTableEntry entry)
    {
        DescriptorType type = VkDescriptorLayoutBuilder.GetDescriptorType(description.Kind, description.Options);
        WriteDescriptorSet write = new()
        {
            SType = StructureType.WriteDescriptorSet,
            DstSet = Set,
            DstBinding = (uint)description.BindingIndex,
            DstArrayElement = index,
            DescriptorCount = 1,
            DescriptorType = type,
        };

        DescriptorImageInfo image = default;
        DescriptorBufferInfo buffer = default;
        if (entry.Buffer is { } range)
        {
            buffer = new DescriptorBufferInfo(Util.AssertSubtype<DeviceBuffer, VkBuffer>(range.Buffer).DeviceBuffer, range.Offset, range.SizeInBytes);
            write.PBufferInfo = &buffer;
        }
        else
        {
            if (entry.View != null || entry.Texture != null)
            {
                VkTextureView view = entry.View != null
                    ? Util.AssertSubtype<TextureView, VkTextureView>(entry.View)
                    : Util.AssertSubtype<Texture, VkTexture>(entry.Texture!).DefaultView;
                image.ImageView = view.ImageView;
                image.ImageLayout = description.Kind == ResourceKind.TextureReadWrite ? ImageLayout.General : VkBarriers.RestingLayout(view.Target);
            }

            if (entry.Sampler != null && type is DescriptorType.Sampler or DescriptorType.CombinedImageSampler)
                image.Sampler = Util.AssertSubtype<Sampler, VkSampler>(entry.Sampler).DeviceSampler;

            write.PImageInfo = &image;
        }

        _gd.Vk.UpdateDescriptorSets(_gd.Device, 1, in write, 0, null);
    }

    private protected override void NameChanged(string name) => _gd.SetResourceName(this, name);

    private protected override void DisposeCore() => _gd.DisposeWhenRetired(DestroyNative);

    private void DestroyNative()
    {
        _gd.Vk.DestroyDescriptorPool(_gd.Device, _pool, null);
        _gd.Vk.DestroyDescriptorSetLayout(_gd.Device, _layout, null);
        _gd.Counters.Free(AllocBin.ResourceLayout);
    }
}
