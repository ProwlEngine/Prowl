using System.Diagnostics;

namespace Prowl.Veldrid.OpenGL;

internal unsafe partial class OpenGLGraphicsProgram
{
#if PROFILE_USAGE
    // Summed shader bytecode recorded at creation, replayed on free so the live gauge settles.
    private long _profiledShaderBytes;
#endif

    [Conditional("PROFILE_USAGE")]
    private void Constructor_RecordShaderAllocation(ShaderStageDescription[] stages)
    {
#if PROFILE_USAGE
        long bytes = 0;
        for (int i = 0; i < stages.Length; i++)
            bytes += stages[i].ShaderBytes.Length;

        _profiledShaderBytes = bytes;
        _gd.RecordAllocation(AllocBin.Shader, bytes);
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
