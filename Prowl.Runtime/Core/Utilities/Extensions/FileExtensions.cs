// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.IO;
using Prowl.Runtime.Core.Platforms;

namespace Prowl.Runtime;

public static class FileExtensions
{
    extension (File)
    {
        /// <summary>
        /// Deletes a file safely. On Windows, this will send the file to the recycle bin. On other platforms, it will delete the file directly.
        /// </summary>
        /// <param name="path">The path of the file to delete.</param>
        public static void DeleteSafe(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"The file '{path}' does not exist.");
#if WINDOWS
            WindowsPlatform.DeleteSafe(path);
#elif MACOS
            MacPlatform.DeleteSafe(path);
#else
            File.Delete(path);
#endif
        }
    }
}
