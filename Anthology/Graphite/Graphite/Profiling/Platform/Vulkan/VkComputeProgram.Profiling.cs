namespace Prowl.Graphite.Vk;

internal unsafe partial class VkComputeProgram
{
    // Shader bytecode recorded at creation, replayed on free so the live gauge settles.
    private long _profiledShaderBytes;

    private void Constructor_RecordAllocations(ShaderStageDescription stage)
    {
        if (!GraphicsDevice.ProfilingEnabled)
            return;

        _profiledShaderBytes = stage.ShaderBytes.Length;
        _gd.RecordAllocation(AllocBin.Shader, _profiledShaderBytes);
        _gd.RecordAllocation(AllocBin.Pipeline, 0);
    }

    private void DisposeCore_RecordFrees()
    {
        if (!GraphicsDevice.ProfilingEnabled)
            return;

        _gd.RecordFree(AllocBin.Shader, _profiledShaderBytes);
        _gd.RecordFree(AllocBin.Pipeline, 0);
    }
}
