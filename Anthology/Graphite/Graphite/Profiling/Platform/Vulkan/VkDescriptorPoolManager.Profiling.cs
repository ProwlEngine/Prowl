namespace Prowl.Graphite.Vk;

internal partial class VkDescriptorPoolManager
{
    private void RecordAllocation() => _gd.Counters.Allocate(AllocBin.ResourceSet);

    private void RecordFree() => _gd.Counters.Free(AllocBin.ResourceSet);
}
