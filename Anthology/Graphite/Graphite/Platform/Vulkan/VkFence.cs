using Silk.NET.Vulkan;

using VkFenceHandle = Silk.NET.Vulkan.Fence;

namespace Prowl.Graphite.Vk;

internal unsafe class VkFence : Fence
{
    private readonly VkGraphicsDevice _gd;
    private VkFenceHandle _fence;

    public VkFenceHandle DeviceFence => _fence;

    public VkFence(VkGraphicsDevice gd, bool signaled)
    {
        _gd = gd;
        FenceCreateInfo fenceCI = new()
        {
            SType = StructureType.FenceCreateInfo,
            Flags = signaled ? FenceCreateFlags.SignaledBit : 0
        };
        _gd.Vk.CreateFence(_gd.Device, in fenceCI, null, out _fence).CheckResult();
    }

    public override void Reset()
    {
        _gd.ResetFence(this);
    }

    public override bool Signaled => _gd.Vk.GetFenceStatus(_gd.Device, _fence) == Result.Success;

    private protected override void NameChanged(string name) => _gd.SetResourceName(this, name);

    private protected override void DisposeCore()
    {
        _gd.Vk.DestroyFence(_gd.Device, _fence, null);
    }
}
