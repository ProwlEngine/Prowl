namespace Prowl.Graphite.Vk;

internal unsafe partial class VkCommandBuffer
{
    private void Constructor_RecordAllocation()
    {
        _gd.Counters.Allocate(AllocBin.CommandBuffer, 0);
    }

    private void DisposeCore_RecordFree()
    {
        _gd.Counters.Free(AllocBin.CommandBuffer, 0);
    }
}
