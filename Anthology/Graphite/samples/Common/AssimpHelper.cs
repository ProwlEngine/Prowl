using System.Runtime.InteropServices;
using Silk.NET.Assimp;

namespace SampleBase
{
    /// <summary>
    /// Provides a correctly configured Assimp instance. Works around a Silk.NET 2.x bug where
    /// the Linux library name is "libassimp.so.5" (Assimp 5.x ABI) but the struct definitions
    /// match Assimp 6.x (which uses "libassimp.so.6"). Loading the wrong version causes struct
    /// size mismatches (e.g. aiQuatKey is 24 bytes in v5 but 32 bytes in v6) and corrupt
    /// animation data.
    /// </summary>
    public static class AssimpHelper
    {
        public static Assimp GetApi()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return new Assimp(Assimp.CreateDefaultContext(
                    new[] { "libassimp.so.6", "libassimp.so.5", "libassimp" }));
            }

            return Assimp.GetApi();
        }
    }
}
