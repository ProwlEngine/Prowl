using System;

using Silk.NET.Vulkan;

using static Prowl.Veldrid.Vk.VulkanUtil;

namespace Prowl.Veldrid.Vk;

internal unsafe class VkResourceLayout : ResourceLayout
{
    private readonly VkGraphicsDevice _gd;
    private readonly DescriptorSetLayout _dsl;
    private readonly DescriptorType[] _descriptorTypes;
    private bool _disposed;
    private string _name;

    public DescriptorSetLayout DescriptorSetLayout => _dsl;
    public DescriptorType[] DescriptorTypes => _descriptorTypes;
    public DescriptorResourceCounts DescriptorResourceCounts { get; }
    public new int DynamicBufferCount => (int)base.DynamicBufferCount;

    /// <summary>
    /// Element-array indices of dynamic-binding elements, sorted ascending by
    /// <see cref="ResourceLayoutElementDescription.BindingIndex"/>. Vulkan expects the
    /// <c>pDynamicOffsets</c> array passed to <c>vkCmdBindDescriptorSets</c> to be ordered
    /// by binding number within each set; the user-facing API passes offsets in
    /// element-array order, so the Vulkan backend uses this remap to reorder them.
    /// </summary>
    public int[] DynamicElementsByBindingOrder { get; }

    public override bool IsDisposed => _disposed;

    public VkResourceLayout(VkGraphicsDevice gd, ref ResourceLayoutDescription description)
        : base(ref description)
    {
        _gd = gd;
        DescriptorSetLayoutCreateInfo dslCI = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo
        };
        ResourceLayoutElementDescription[] elements = description.Elements;
        _descriptorTypes = new DescriptorType[elements.Length];
        DescriptorSetLayoutBinding* bindings = stackalloc DescriptorSetLayoutBinding[elements.Length];

        uint uniformBufferCount = 0;
        uint uniformBufferDynamicCount = 0;
        uint sampledImageCount = 0;
        uint samplerCount = 0;
        uint storageBufferCount = 0;
        uint storageBufferDynamicCount = 0;
        uint storageImageCount = 0;

        int[] dynamicOrder = DynamicBufferCount > 0 ? new int[DynamicBufferCount] : Array.Empty<int>();
        int dynamicOrderIdx = 0;

        for (int i = 0; i < elements.Length; i++)
        {
            bindings[i].Binding = (uint)elements[i].BindingIndex;
            bindings[i].DescriptorCount = 1;
            DescriptorType descriptorType = VkFormats.VdToVkDescriptorType(elements[i].Kind, elements[i].Options);
            bindings[i].DescriptorType = descriptorType;
            bindings[i].StageFlags = VkFormats.VdToVkShaderStages(elements[i].Stages);

            if ((elements[i].Options & ResourceLayoutElementOptions.DynamicBinding) != 0)
            {
                dynamicOrder[dynamicOrderIdx++] = i;
            }

            _descriptorTypes[i] = descriptorType;

            switch (descriptorType)
            {
                case DescriptorType.Sampler:
                    samplerCount += 1;
                    break;
                case DescriptorType.SampledImage:
                    sampledImageCount += 1;
                    break;
                case DescriptorType.StorageImage:
                    storageImageCount += 1;
                    break;
                case DescriptorType.UniformBuffer:
                    uniformBufferCount += 1;
                    break;
                case DescriptorType.UniformBufferDynamic:
                    uniformBufferDynamicCount += 1;
                    break;
                case DescriptorType.StorageBuffer:
                    storageBufferCount += 1;
                    break;
                case DescriptorType.StorageBufferDynamic:
                    storageBufferDynamicCount += 1;
                    break;
            }
        }

        // Stable sort by BindingIndex so the dynamic-offset array order we receive from the
        // caller (element-array order) can be remapped to Vulkan's required order (ascending
        // by binding number).
        if (dynamicOrder.Length > 1)
        {
            Array.Sort(dynamicOrder, (a, b) => elements[a].BindingIndex.CompareTo(elements[b].BindingIndex));
        }

        DynamicElementsByBindingOrder = dynamicOrder;

        DescriptorResourceCounts = new DescriptorResourceCounts(
            uniformBufferCount,
            uniformBufferDynamicCount,
            sampledImageCount,
            samplerCount,
            storageBufferCount,
            storageBufferDynamicCount,
            storageImageCount);

        dslCI.BindingCount = (uint)elements.Length;
        dslCI.PBindings = bindings;

        Result result = _gd.Vk.CreateDescriptorSetLayout(_gd.Device, in dslCI, null, out _dsl);
        CheckResult(result);
    }

    public override string Name
    {
        get => _name;
        set
        {
            _name = value;
            _gd.SetResourceName(this, value);
        }
    }

    public override void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _gd.Vk.DestroyDescriptorSetLayout(_gd.Device, _dsl, null);
        }
    }
}
