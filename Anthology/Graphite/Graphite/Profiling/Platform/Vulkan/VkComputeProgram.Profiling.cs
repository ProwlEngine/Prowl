namespace Prowl.Graphite.Vk;

internal unsafe partial class VkComputeProgram
{
    // Shader bytecode recorded at creation, replayed on free so the live gauge settles.
    private long _profiledShaderBytes;

    private void Constructor_RecordAllocations(ShaderStageDescription stage)
    {
        if (_gd.Profiler is not { } profiler)
            return;

        _profiledShaderBytes = stage.ShaderBytes.Length;
        profiler.Allocate(AllocBin.Shader, _profiledShaderBytes);
        profiler.Allocate(AllocBin.Pipeline, 0);
    }

    private void DisposeCore_RecordFrees()
    {
        if (_gd.Profiler is not { } profiler)
            return;

        profiler.Free(AllocBin.Shader, _profiledShaderBytes);
        profiler.Free(AllocBin.Pipeline, 0);
    }
}
