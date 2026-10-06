namespace Prowl.Graphite.Vk;

internal partial class VkDescriptorPoolManager
{
    private void RecordAllocation() => _gd.Counters.Allocate(AllocBin.ResourceSet, 0);

    private void RecordFree() => _gd.Counters.Free(AllocBin.ResourceSet, 0);
}
