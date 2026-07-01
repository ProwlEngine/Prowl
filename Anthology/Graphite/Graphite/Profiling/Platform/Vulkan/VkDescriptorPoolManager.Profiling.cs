namespace Prowl.Graphite.Vk;

internal partial class VkDescriptorPoolManager
{
    // Descriptor sets currently live in this manager. Per-set frees decrement it; a wholesale
    // ResetAll reclaims whatever remains. Always mutated under the manager's _lock.
    private long _profiledLiveSets;

    private void Allocate_RecordAllocation()
    {
        if (!GraphicsDevice.ProfilingEnabled)
            return;

        _profiledLiveSets++;
        _gd.RecordAllocation(AllocBin.ResourceSet, 0);
    }

    private void Free_RecordFree()
    {
        if (!GraphicsDevice.ProfilingEnabled)
            return;

        _profiledLiveSets--;
        _gd.RecordFree(AllocBin.ResourceSet, 0);
    }

    private void ResetAll_RecordFrees()
    {
        if (!GraphicsDevice.ProfilingEnabled)
            return;

        for (long i = 0; i < _profiledLiveSets; i++)
            _gd.RecordFree(AllocBin.ResourceSet, 0);
        _profiledLiveSets = 0;
    }
}
