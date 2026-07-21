namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsProgram
{
    // Summed shader bytecode recorded at creation, replayed on free so the live gauge settles.
    private long _profiledShaderBytes;

    private void Constructor_RecordShaderAllocation(ShaderStageDescription[] stages)
    {
        if (_gd.Profiler is not { } profiler)
            return;

        long bytes = 0;
        for (int i = 0; i < stages.Length; i++)
            bytes += stages[i].ShaderBytes.Length;

        _profiledShaderBytes = bytes;
        profiler.Allocate(AllocBin.Shader, bytes);
    }

    private void DisposeCore_RecordFrees(int pipelineCount)
    {
        if (_gd.Profiler is not { } profiler)
            return;

        profiler.Free(AllocBin.Shader, _profiledShaderBytes);
        for (int i = 0; i < pipelineCount; i++)
            profiler.Free(AllocBin.Pipeline, 0);
    }
}
