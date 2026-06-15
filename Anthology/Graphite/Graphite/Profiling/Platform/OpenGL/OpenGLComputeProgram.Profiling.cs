using System.Diagnostics;

namespace Prowl.Graphite.OpenGL;

internal unsafe partial class OpenGLComputeProgram
{
#if PROFILE_USAGE
    // Shader bytecode recorded at creation, replayed on free so the live gauge settles.
    private long _profiledShaderBytes;
#endif

    [Conditional("PROFILE_USAGE")]
    private void Constructor_RecordShaderAllocation()
    {
#if PROFILE_USAGE
        _profiledShaderBytes = _stageDescription.ShaderBytes.Length;
        _gd.RecordAllocation(AllocBin.Shader, _profiledShaderBytes);
#endif
    }

    [Conditional("PROFILE_USAGE")]
    private void Dispose_RecordShaderFree()
    {
#if PROFILE_USAGE
        _gd.RecordFree(AllocBin.Shader, _profiledShaderBytes);
#endif
    }
}
