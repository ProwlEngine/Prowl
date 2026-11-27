namespace Prowl.Graphite.Vk;

internal unsafe partial class VkComputeProgram
{
    private long _profiledShaderBytes;

    private void Constructor_RecordAllocations(ShaderStageDescription stage)
    {
        _profiledShaderBytes = stage.ShaderBytes.Length;
        _gd.Counters.Allocate(AllocBin.Shader, _profiledShaderBytes);
        _gd.Counters.Allocate(AllocBin.Pipeline);
    }

    private void DisposeCore_RecordFrees()
    {
        _gd.Counters.Free(AllocBin.Shader, _profiledShaderBytes);
        _gd.Counters.Free(AllocBin.Pipeline);
    }
}
