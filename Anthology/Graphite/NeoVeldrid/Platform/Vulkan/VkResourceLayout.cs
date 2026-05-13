using Silk.NET.Vulkan;
using static NeoVeldrid.Vk.VulkanUtil;

namespace NeoVeldrid.Vk
{
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
            uint sampledImageCount = 0;
            uint samplerCount = 0;
            uint storageBufferCount = 0;
            uint storageImageCount = 0;

            for (int i = 0; i < elements.Length; i++)
            {
                bindings[i].Binding = (uint)elements[i].BindingIndex;
                bindings[i].DescriptorCount = 1;
                DescriptorType descriptorType = VkFormats.VdToVkDescriptorType(elements[i].Kind);
                bindings[i].DescriptorType = descriptorType;
                bindings[i].StageFlags = VkFormats.VdToVkShaderStages(elements[i].Stages);

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
                    case DescriptorType.StorageBuffer:
                        storageBufferCount += 1;
                        break;
                }
            }

            DescriptorResourceCounts = new DescriptorResourceCounts(
                uniformBufferCount,
                sampledImageCount,
                samplerCount,
                storageBufferCount,
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
}
