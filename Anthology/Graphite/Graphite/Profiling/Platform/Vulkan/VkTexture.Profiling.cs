namespace Prowl.Graphite.Vk;

internal unsafe partial class VkTexture
{
    // Actual Vulkan allocation size recorded at creation, replayed on free so the live gauge settles.
    private long _profiledBytes;

    private void Constructor_RecordAllocation(long bytes)
    {
        if (!GraphicsDevice.ProfilingEnabled)
            return;

        _profiledBytes = bytes;
        _gd.RecordAllocation(AllocBin.Texture, bytes);
    }

    private void DisposeCore_RecordFree()
    {
        if (!GraphicsDevice.ProfilingEnabled)
            return;

        _gd.RecordFree(AllocBin.Texture, _profiledBytes);
    }
}
