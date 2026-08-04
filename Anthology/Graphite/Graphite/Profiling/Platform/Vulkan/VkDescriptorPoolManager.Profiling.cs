namespace Prowl.Graphite.Vk;

internal partial class VkDescriptorPoolManager
{
    private void RecordAllocation() => _gd.Profiler?.Allocate(AllocBin.ResourceSet, 0);

    private void RecordFree() => _gd.Profiler?.Free(AllocBin.ResourceSet, 0);
}
