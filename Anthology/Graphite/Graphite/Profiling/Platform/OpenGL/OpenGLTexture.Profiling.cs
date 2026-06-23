namespace Prowl.Graphite.OpenGL;

internal unsafe partial class OpenGLTexture
{
    // Set only for textures this instance owns (the description ctor). Native-handle wrappers
    // leave this false so they are not double-counted, matching the other backends.
    private bool _profiledAlloc;
    private long _profiledBytes;

    private void Constructor_RecordAllocation()
    {
        if (!GraphicsDevice.ProfilingEnabled)
            return;

        _profiledBytes = ProfilingTextureEstimate.EstimateBytes(this);
        _profiledAlloc = true;
        _gd.RecordAllocation(AllocBin.Texture, _profiledBytes);
    }

    private void DisposeCore_RecordFree()
    {
        if (!GraphicsDevice.ProfilingEnabled)
            return;

        if (_profiledAlloc)
            _gd.RecordFree(AllocBin.Texture, _profiledBytes);
    }
}
