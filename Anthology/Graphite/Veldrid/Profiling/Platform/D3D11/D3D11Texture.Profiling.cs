using System.Diagnostics;

namespace Prowl.Veldrid.D3D11;

internal unsafe partial class D3D11Texture
{
#if PROFILE_USAGE
    // Set only for textures this instance owns (the description ctor). Swapchain back-buffer
    // wrappers leave this false so they are not double-counted, matching the Vulkan backend.
    private bool _profiledAlloc;
    private long _profiledBytes;
#endif

    [Conditional("PROFILE_USAGE")]
    private void Constructor_RecordAllocation()
    {
#if PROFILE_USAGE
        _profiledBytes = ProfilingTextureEstimate.EstimateBytes(this);
        _profiledAlloc = true;
        _gd.RecordAllocation(AllocBin.Texture, _profiledBytes);
#endif
    }

    [Conditional("PROFILE_USAGE")]
    private void DisposeCore_RecordFree()
    {
#if PROFILE_USAGE
        if (_profiledAlloc)
            _gd.RecordFree(AllocBin.Texture, _profiledBytes);
#endif
    }
}
