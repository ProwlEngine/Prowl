namespace Prowl.Graphite.D3D11;

internal unsafe partial class D3D11Texture
{
    // Set only for textures this instance owns (the description ctor). Swapchain back-buffer
    // wrappers leave this false so they are not double-counted, matching the Vulkan backend.
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
