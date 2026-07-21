namespace Prowl.Graphite.Vk;

internal partial class VkDescriptorPoolManager
{
    // Descriptor sets currently live in this manager. Per-set frees decrement it; a wholesale
    // ResetAll reclaims whatever remains. Always mutated under the manager's _lock.
    private long _profiledLiveSets;

    private void Allocate_RecordAllocation()
    {
        if (_gd.Profiler is not { } profiler)
            return;

        _profiledLiveSets++;
        profiler.Allocate(AllocBin.ResourceSet, 0);
    }

    private void Free_RecordFree()
    {
        if (_gd.Profiler is not { } profiler)
            return;

        _profiledLiveSets--;
        profiler.Free(AllocBin.ResourceSet, 0);
    }

    private void ResetAll_RecordFrees()
    {
        if (_gd.Profiler is not { } profiler)
            return;

        for (long i = 0; i < _profiledLiveSets; i++)
            profiler.Free(AllocBin.ResourceSet, 0);
        _profiledLiveSets = 0;
    }
}
