using System.Diagnostics;

namespace Prowl.Graphite.Vk;

internal unsafe partial class VkComputeProgram
{
#if PROFILE_USAGE
    // Shader bytecode recorded at creation, replayed on free so the live gauge settles.
    private long _profiledShaderBytes;
#endif

    [Conditional("PROFILE_USAGE")]
    private void Constructor_RecordAllocations(ShaderStageDescription stage)
    {
#if PROFILE_USAGE
        _profiledShaderBytes = stage.ShaderBytes.Length;
        _gd.RecordAllocation(AllocBin.Shader, _profiledShaderBytes);
#endif
        _gd.RecordAllocation(AllocBin.Pipeline, 0);
    }

    [Conditional("PROFILE_USAGE")]
    private void DisposeCore_RecordFrees()
    {
#if PROFILE_USAGE
        _gd.RecordFree(AllocBin.Shader, _profiledShaderBytes);
#endif
        _gd.RecordFree(AllocBin.Pipeline, 0);
    }
}
