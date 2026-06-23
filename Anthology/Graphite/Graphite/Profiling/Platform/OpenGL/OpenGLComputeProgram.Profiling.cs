namespace Prowl.Graphite.OpenGL;

internal unsafe partial class OpenGLComputeProgram
{
    // Shader bytecode recorded at creation, replayed on free so the live gauge settles.
    private long _profiledShaderBytes;

    private void Constructor_RecordShaderAllocation()
    {
        if (!GraphicsDevice.ProfilingEnabled)
            return;

        _profiledShaderBytes = _stageDescription.ShaderBytes.Length;
        _gd.RecordAllocation(AllocBin.Shader, _profiledShaderBytes);
    }

    private void Dispose_RecordShaderFree()
    {
        if (!GraphicsDevice.ProfilingEnabled)
            return;

        _gd.RecordFree(AllocBin.Shader, _profiledShaderBytes);
    }
}
