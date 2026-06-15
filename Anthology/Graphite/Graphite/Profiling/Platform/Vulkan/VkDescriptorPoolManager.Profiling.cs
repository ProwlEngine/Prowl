using System.Diagnostics;

namespace Prowl.Graphite.Vk;

internal partial class VkDescriptorPoolManager
{
#if PROFILE_USAGE
    // Descriptor sets currently live in this manager. Per-set frees decrement it; a wholesale
    // ResetAll reclaims whatever remains. Always mutated under the manager's _lock.
    private long _profiledLiveSets;
#endif

    [Conditional("PROFILE_USAGE")]
    private void Allocate_RecordAllocation()
    {
#if PROFILE_USAGE
        _profiledLiveSets++;
#endif
        _gd.RecordAllocation(AllocBin.ResourceSet, 0);
    }

    [Conditional("PROFILE_USAGE")]
    private void Free_RecordFree()
    {
#if PROFILE_USAGE
        _profiledLiveSets--;
#endif
        _gd.RecordFree(AllocBin.ResourceSet, 0);
    }

    [Conditional("PROFILE_USAGE")]
    private void ResetAll_RecordFrees()
    {
#if PROFILE_USAGE
        for (long i = 0; i < _profiledLiveSets; i++)
            _gd.RecordFree(AllocBin.ResourceSet, 0);
        _profiledLiveSets = 0;
#endif
    }
}
