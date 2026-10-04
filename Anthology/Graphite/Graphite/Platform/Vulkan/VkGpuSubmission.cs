using Silk.NET.Vulkan;

using VkFenceHandle = Silk.NET.Vulkan.Fence;

namespace Prowl.Graphite.Vk;

internal sealed unsafe class VkGpuSubmission : GpuSubmission
{
    private readonly VkGraphicsDevice _gd;
    private bool _complete;

    internal VkFenceHandle Fence;

    internal object SyncRoot { get; } = new();

    internal VkGpuSubmission(VkGraphicsDevice gd)
    {
        _gd = gd;
    }

    internal void MarkComplete() => _complete = true;

    public override bool IsComplete
    {
        get
        {
            bool done;
            lock (SyncRoot)
            {
                done = _complete || _gd.Vk.GetFenceStatus(_gd.Device, Fence) == Result.Success;
            }
            if (done)
                _gd.PollSubmissions();
            return done;
        }
    }

    public override bool Wait(ulong nanosecondTimeout = ulong.MaxValue)
    {
        bool done;
        lock (SyncRoot)
        {
            if (_complete)
                return true;

            VkFenceHandle fence = Fence;
            done = _gd.Vk.WaitForFences(_gd.Device, 1, &fence, true, nanosecondTimeout) == Result.Success;
        }
        if (done)
            _gd.PollSubmissions();
        return done;
    }
}
