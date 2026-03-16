#if !EXCLUDE_OPENGL_BACKEND
using System.Diagnostics;
using Silk.NET.Maths;
using Silk.NET.Windowing;

namespace Veldrid.StartupUtilities
{
    /// <summary>
    /// An OpenGL or OpenGL ES version (major.minor).
    /// </summary>
    internal readonly record struct GLVersion(int Major, int Minor)
    {
        public override string ToString() => $"{Major}.{Minor}";
    }

    /// <summary>
    /// Probes the system for the highest available OpenGL / OpenGL ES version by creating
    /// temporary hidden windows, matching upstream Veldrid's SDL2-based probing behavior.
    /// Results are cached after the first probe.
    /// </summary>
    internal static class OpenGLVersionProbe
    {
        /// <summary>OpenGL versions to probe, highest first.</summary>
        public static readonly GLVersion[] GLVersions =
            [new(4, 6), new(4, 3), new(4, 0), new(3, 3), new(3, 0)];

        /// <summary>OpenGL ES versions to probe, highest first.</summary>
        public static readonly GLVersion[] GLESVersions =
            [new(3, 2), new(3, 0)];

        private static readonly object s_lock = new();
        private static GLVersion? s_maxGLVersion;
        private static GLVersion? s_maxGLESVersion;

        /// <summary>
        /// Probes for the highest OpenGL (or OpenGL ES) version supported by the system.
        /// Tested versions are defined by <see cref="GLVersions"/> and <see cref="GLESVersions"/>.
        /// </summary>
        /// <returns>True if a supported version was found.</returns>
        public static bool TryGetMaxVersion(bool gles, out GLVersion version)
        {
            lock (s_lock)
            {
                ref var cached = ref gles ? ref s_maxGLESVersion : ref s_maxGLVersion;
                cached ??= Probe(gles);
                version = cached.Value;
                return version.Major > 0;
            }
        }

        private static GLVersion Probe(bool gles)
        {
            foreach (var version in gles ? GLESVersions : GLVersions)
            {
                if (TestVersion(gles, version))
                    return version;
            }

            return new GLVersion(0, 0);
        }

        private static bool TestVersion(bool gles, GLVersion version)
        {
            IWindow window = null;
            try
            {
                var ctxApi = gles ? ContextAPI.OpenGLES : ContextAPI.OpenGL;
                var flags = gles ? ContextFlags.Default : ContextFlags.ForwardCompatible;

                var options = new WindowOptions
                {
                    Title = string.Empty,
                    Position = new Vector2D<int>(0, 0),
                    Size = new Vector2D<int>(1, 1),
                    IsVisible = false,
                    API = new GraphicsAPI(ctxApi, ContextProfile.Core, flags, new APIVersion(version.Major, version.Minor)),
                };

                window = Window.Create(options);
                window.Initialize();

                return window.GLContext != null;
            }
            catch
            {
                Debug.WriteLine($"Unable to create {(gles ? "ES" : "GL")} {version} context.");
                return false;
            }
            finally
            {
                window?.Dispose();
            }
        }
    }
}
#endif
