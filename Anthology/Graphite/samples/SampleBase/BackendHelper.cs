using System;
using NeoVeldrid;
using NeoVeldrid.StartupUtilities;

namespace SampleBase
{
    /// <summary>
    /// Parses the NEOVELDRID_BACKEND environment variable to select a graphics backend for testing.
    /// Accepts: d3d11, vulkan, opengl, opengles. Falls back to the platform default if not set.
    /// </summary>
    public static class BackendHelper
    {
        public static GraphicsBackend GetPreferredBackend()
        {
            string envBackend = Environment.GetEnvironmentVariable("NEOVELDRID_BACKEND");
            if (!string.IsNullOrEmpty(envBackend))
            {
                return envBackend.ToLowerInvariant() switch
                {
                    "d3d11" or "direct3d11" => GraphicsBackend.Direct3D11,
                    "vulkan" or "vk" => GraphicsBackend.Vulkan,
                    "opengl" or "gl" => GraphicsBackend.OpenGL,
                    "opengles" or "gles" => GraphicsBackend.OpenGLES,
                    _ => throw new InvalidOperationException(
                        $"Unknown NEOVELDRID_BACKEND value: '{envBackend}'. Use: d3d11, vulkan, opengl, opengles")
                };
            }
            return NeoVeldridStartup.GetPlatformDefaultBackend();
        }
    }
}
