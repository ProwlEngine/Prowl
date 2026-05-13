using Silk.NET.Vulkan;
using VkFenceHandle = Silk.NET.Vulkan.Fence;

namespace NeoVeldrid.Vk
{
    internal unsafe class VkFence : Fence
    {
        private readonly VkGraphicsDevice _gd;
        private VkFenceHandle _fence;
        private string _name;
        private bool _destroyed;

        public VkFenceHandle DeviceFence => _fence;

        public VkFence(VkGraphicsDevice gd, bool signaled)
        {
            _gd = gd;
            FenceCreateInfo fenceCI = new FenceCreateInfo
            {
                SType = StructureType.FenceCreateInfo,
                Flags = signaled ? FenceCreateFlags.SignaledBit : 0
            };
            Result result = _gd.Vk.CreateFence(_gd.Device, in fenceCI, null, out _fence);
            VulkanUtil.CheckResult(result);
        }

        public override void Reset()
        {
            _gd.ResetFence(this);
        }

        public override bool Signaled => _gd.Vk.GetFenceStatus(_gd.Device, _fence) == Result.Success;
        public override bool IsDisposed => _destroyed;

        public override string Name
        {
            get => _name;
            set
            {
                _name = value; _gd.SetResourceName(this, value);
            }
        }

        public override void Dispose()
        {
            if (!_destroyed)
            {
                _gd.Vk.DestroyFence(_gd.Device, _fence, null);
                _destroyed = true;
            }
        }
    }
}
