namespace Prowl.Graphite.OpenGL;

internal partial class OpenGLCommandBuffer
{
    private void Constructor_RecordAllocation()
    {
        if (!GraphicsDevice.ProfilingEnabled)
            return;

        _gd.RecordAllocation(AllocBin.CommandBuffer, 0);
    }

    private void DestroyResources_RecordFree()
    {
        if (!GraphicsDevice.ProfilingEnabled)
            return;

        _gd.RecordFree(AllocBin.CommandBuffer, 0);
    }
}
