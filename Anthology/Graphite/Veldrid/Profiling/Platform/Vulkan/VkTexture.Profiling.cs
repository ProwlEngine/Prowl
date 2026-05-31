using System.Diagnostics;

namespace Prowl.Veldrid.Vk;

internal unsafe partial class VkTexture
{
#if PROFILE_USAGE
    // Actual Vulkan allocation size recorded at creation, replayed on free so the live gauge settles.
    private long _profiledBytes;
#endif

    [Conditional("PROFILE_USAGE")]
    private void Constructor_RecordAllocation(long bytes)
    {
#if PROFILE_USAGE
        _profiledBytes = bytes;
#endif
        _gd.RecordAllocation(AllocBin.Texture, bytes);
    }

    [Conditional("PROFILE_USAGE")]
    private void DisposeCore_RecordFree()
    {
#if PROFILE_USAGE
        _gd.RecordFree(AllocBin.Texture, _profiledBytes);
#endif
    }
}
