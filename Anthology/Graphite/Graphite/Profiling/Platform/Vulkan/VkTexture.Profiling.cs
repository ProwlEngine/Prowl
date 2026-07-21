namespace Prowl.Graphite.Vk;

internal unsafe partial class VkTexture
{
    // Actual Vulkan allocation size recorded at creation, replayed on free so the live gauge settles.
    private long _profiledBytes;

    private void Constructor_RecordAllocation(long bytes)
    {
        if (_gd.Profiler is not { } profiler)
            return;

        _profiledBytes = bytes;
        profiler.Allocate(AllocBin.Texture, bytes);
    }

    private void DisposeCore_RecordFree()
    {
        if (_gd.Profiler is not { } profiler)
            return;

        profiler.Free(AllocBin.Texture, _profiledBytes);
    }
}
