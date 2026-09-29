// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.IO;

#if WINDOWS
using Microsoft.VisualBasic.FileIO;
#endif

namespace Prowl.Runtime.Utils;

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
                return;
#if WINDOWS
            FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
#else
            File.Delete(path);
#endif
        }
    }
}
