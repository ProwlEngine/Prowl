namespace Prowl.Graphite.D3D11;

internal unsafe partial class D3D11CommandBuffer
{
    private void Constructor_RecordAllocation()
    {
        if (!GraphicsDevice.ProfilingEnabled)
            return;

        _gd.RecordAllocation(AllocBin.CommandBuffer, 0);
    }

    private void Dispose_RecordFree()
    {
        if (!GraphicsDevice.ProfilingEnabled)
            return;

        _gd.RecordFree(AllocBin.CommandBuffer, 0);
    }
}
