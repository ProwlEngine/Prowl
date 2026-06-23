namespace Prowl.Graphite.Vk;

internal unsafe partial class VkGraphicsProgram
{
    // Summed shader bytecode recorded at creation, replayed on free so the live gauge settles.
    private long _profiledShaderBytes;

    private void Constructor_RecordShaderAllocation(ShaderStageDescription[] stages)
    {
        if (!GraphicsDevice.ProfilingEnabled)
            return;

        long bytes = 0;
        for (int i = 0; i < stages.Length; i++)
            bytes += stages[i].ShaderBytes.Length;

        _profiledShaderBytes = bytes;
        _gd.RecordAllocation(AllocBin.Shader, bytes);
    }

    private void DisposeCore_RecordFrees(int pipelineCount)
    {
        if (!GraphicsDevice.ProfilingEnabled)
            return;

        _gd.RecordFree(AllocBin.Shader, _profiledShaderBytes);
        for (int i = 0; i < pipelineCount; i++)
            _gd.RecordFree(AllocBin.Pipeline, 0);
    }
}
