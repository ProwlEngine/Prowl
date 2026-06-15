using System.Diagnostics;

namespace Prowl.Graphite.OpenGL;

internal unsafe partial class OpenGLTexture
{
#if PROFILE_USAGE
    // Set only for textures this instance owns (the description ctor). Native-handle wrappers
    // leave this false so they are not double-counted, matching the other backends.
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
