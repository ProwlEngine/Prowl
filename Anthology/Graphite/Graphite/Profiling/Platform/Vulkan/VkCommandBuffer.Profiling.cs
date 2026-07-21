namespace Prowl.Graphite.Vk;

internal unsafe partial class VkCommandBuffer
{
    private void Constructor_RecordAllocation()
    {
        _gd.Profiler?.Allocate(AllocBin.CommandBuffer, 0);
    }

    private void DisposeCore_RecordFree()
    {
        _gd.Profiler?.Free(AllocBin.CommandBuffer, 0);
    }
}
