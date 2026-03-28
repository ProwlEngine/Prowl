using System.IO;
using System.Runtime.InteropServices;
using Silk.NET.Assimp;

namespace SampleBase
{
    /// <summary>
    /// Provides a correctly configured Assimp instance. Works around a Silk.NET 2.x bug where
    /// the native library name points to Assimp 5.x (libassimp.so.5 / libassimp.5.dylib) but
    /// the struct definitions match Assimp 6.x. Loading the wrong version causes struct size
    /// mismatches (e.g. aiQuatKey is 24 bytes in v5 but 32 bytes in v6) and corrupt animation data.
    /// Affects Linux and macOS. Windows ships Assimp64.dll which is already v6.
    /// </summary>
    public static class AssimpHelper
    {
        public static Assimp GetApi()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try
                {
                    return new Assimp(Assimp.CreateDefaultContext(
                        new[] { "libassimp.so.6", "libassimp.so.5", "libassimp" }));
                }
                catch (FileNotFoundException)
                {
                    // Custom names bypass Silk.NET's SearchPathContainer. Fall back to
                    // the default loader which knows about NuGet runtimes/ directories.
                    // This loads libassimp.so.5 (wrong ABI for animation keyframes) but
                    // works for basic model loading.
                    return Assimp.GetApi();
                }
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                try
                {
                    return new Assimp(Assimp.CreateDefaultContext(
                        new[] { "libassimp.6.dylib", "libassimp.5.dylib", "libassimp" }));
                }
                catch (FileNotFoundException)
                {
                    return Assimp.GetApi();
                }
            }

            return Assimp.GetApi();
        }
    }
}
