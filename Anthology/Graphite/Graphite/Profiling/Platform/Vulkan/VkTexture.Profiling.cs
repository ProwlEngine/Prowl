namespace Prowl.Graphite.Vk;

internal unsafe partial class VkTexture
{
    private long _profiledBytes;

    private void Constructor_RecordAllocation(long bytes)
    {
        _profiledBytes = bytes;
        _gd.Counters.Allocate(AllocBin.Texture, bytes);
    }

    private void DisposeCore_RecordFree()
    {
        _gd.Counters.Free(AllocBin.Texture, _profiledBytes);
    }
}
