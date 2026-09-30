// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.IO;
using Prowl.Runtime.Core.Platforms;

namespace Prowl.Runtime.Utils;

public static class DirectoryExtensions
{
    extension (Directory)
    {
        /// <summary>
        /// Deletes a directory safely. On Windows, this will send the directory to the recycle bin. On other platforms, it will delete the directory directly.
        /// </summary>
        /// <param name="path">The path of the directory to delete.</param>
        public static void DeleteSafe(string path)
        {
            if (!Directory.Exists(path))
                throw new DirectoryNotFoundException($"The directory '{path}' does not exist.");
#if WINDOWS
            WindowsPlatform.DeleteSafe(path);
#elif MACOS
            MacPlatform.DeleteSafe(path);
#else
            Directory.Delete(path, recursive);
#endif
        }
    }
}
