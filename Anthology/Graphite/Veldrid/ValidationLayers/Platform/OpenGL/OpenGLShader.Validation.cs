using System.Diagnostics;

namespace Prowl.Veldrid.OpenGL;

internal unsafe partial class OpenGLShader
{
    [Conditional("VALIDATE_USAGE")]
    private static void OpenGLShader_CheckComputeSupport(OpenGLGraphicsDevice gd, ShaderStages stage)
    {
#if VALIDATE_USAGE
        if (stage == ShaderStages.Compute && !gd.Extensions.ComputeShaders)
        {
            if (gd.BackendType == GraphicsBackend.OpenGLES)
            {
                throw new RenderException("Compute shaders require OpenGL ES 3.1.");
            }
            else
            {
                throw new RenderException($"Compute shaders require OpenGL 4.3 or ARB_compute_shader.");
            }
        }
#endif
    }
}
