namespace Prowl.Graphite.Vk;

internal unsafe partial class VkCommandBuffer
{
    private void Constructor_RecordAllocation()
    {
        if (!GraphicsDevice.ProfilingEnabled)
            return;

        _gd.RecordAllocation(AllocBin.CommandBuffer, 0);
    }

    private void DisposeCore_RecordFree()
    {
        if (!GraphicsDevice.ProfilingEnabled)
            return;

        _gd.RecordFree(AllocBin.CommandBuffer, 0);
    }
}
