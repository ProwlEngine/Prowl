using Prowl.Runtime;
using System.Diagnostics;
using System.Reflection;

namespace Prowl.Editor.Assets
{
    public static partial class AssetDatabase
    {

        #region Public Methods

        /// <summary>
        /// Converts a file path to a relative path within the project.
        /// </summary>
        /// <param name="file">The file to convert to a relative path.</param>
        /// <returns>The relative path of the file within the project.</returns>
        public static string ToRelativePath(FileInfo file) => Path.GetRelativePath(Project.ProjectDirectory, file.FullName);

        /// <summary>
        /// Converts a relative path within the project to a full file path.
        /// </summary>
        /// <param name="relativePath">The relative path to convert to a full file path.</param>
        /// <returns>The full file path of the relative path.</returns>
        public static FileInfo FromRelativePath(string relativePath) => new(Path.Combine(Project.ProjectDirectory, relativePath));

        /// <summary>
        /// Opens the specified file with the operating system's default program.
        /// </summary>
        /// <param name="file">The file to open.</param>
        public static void OpenPath(FileSystemInfo file)
        {
            if (OperatingSystem.IsWindows())
                Process.Start("explorer.exe", file.FullName);
            else if (OperatingSystem.IsLinux())
                Process.Start("xdg-open", file.FullName);
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", file.FullName);
        }

        /// <summary>
        /// Gets the GUID of a file from its relative path.
        /// </summary>
        /// <param name="relativePath">The relative path of the file.</param>
        /// <returns>The GUID of the file.</returns>
        public static Guid GuidFromRelativePath(string relativePath)
        {
            FileInfo path = FromRelativePath(relativePath);
            if (TryGetGuid(path, out var guid))
                return guid;
            return Guid.Empty;
        }

        /// <summary>
        /// Checks if the AssetDatabase contains a file with the specified GUID.
        /// </summary>
        /// <param name="assetID">The GUID of the file.</param>
        /// <returns>True if the file exists in the AssetDatabase, false otherwise.</returns>
        public static bool Contains(Guid assetID) => assetGuidToMeta.ContainsKey(assetID);

        /// <summary>
        /// Pings a file with the specified GUID.
        /// </summary>
        /// <param name="guid">The GUID of the file.</param>
        public static void Ping(Guid guid)
        {
            if (guid == Guid.Empty) return;
            if (TryGetFile(guid, out var file))
                Ping(file);
        }

        /// <summary>
        /// Pings a file.
        /// </summary>
        /// <param name="file">The file to ping.</param>
        public static void Ping(FileInfo file) => Pinged?.Invoke(file);

        /// <summary>
        /// Gets the relative path of a file within the project.
        /// </summary>
        /// <param name="fullFilePath">The full file path of the file.</param>
        /// <returns>The relative path of the file within the project, or null if the file is not within the project.</returns>
        public static string? GetRelativePath(string fullFilePath)
        {
            foreach (var rootFolder in rootFolders)
            {
                string rootFolderPath = rootFolder.FullName;
                if (fullFilePath.StartsWith(rootFolderPath, StringComparison.OrdinalIgnoreCase))
                {
                    string relativePath = fullFilePath[rootFolderPath.Length..];

                    // Ensure the relative path doesn't start with a directory separator
                    if (relativePath.StartsWith(Path.DirectorySeparatorChar.ToString()))
                        relativePath = relativePath[1..];

                    // Add the root folder name to the relative path
                    relativePath = Path.Combine(rootFolder.Name, relativePath);

                    return relativePath;
                }
            }
            return null;
        }

        public static HashSet<Guid> AllThatDependOn(Guid dependsOn)
        {
            HashSet<Guid> result = new();
            // Go over all stored meta files and return any that depend on the specified GUID
            foreach (var meta in assetGuidToMeta.Values)
            {
                if (meta.dependencies.Contains(dependsOn))
                    result.Add(meta.guid);
            }
            return result;
        }

        /// <summary>
        /// Checks if a file is within the project directory.
        /// </summary>
        /// <param name="file">The file to check.</param>
        /// <returns>True if the file is within the project directory, false otherwise.</returns>
        // https://stackoverflow.com/questions/5617320/given-full-path-check-if-path-is-subdirectory-of-some-other-path-or-otherwise
        public static bool FileIsInProject(FileInfo file)
        {
            string normalizedPath = Path.GetFullPath(file.FullName.Replace('/', '\\').WithEnding("\\"));
            string normalizedBaseDirPath = Path.GetFullPath(Project.ProjectAssetDirectory.Replace('/', '\\').WithEnding("\\"));
            return normalizedPath.StartsWith(normalizedBaseDirPath, StringComparison.OrdinalIgnoreCase);
        }

        public static void GenerateUniqueAssetPath(ref DirectoryInfo dir)
        {
            string name = dir.Name;
            if (dir.Exists)
            {
                int counter = 1;
                while (dir.Exists)
                {
                    dir = new DirectoryInfo(Path.Combine(dir.Parent.FullName, $"{name} ({counter})"));
                    counter++;
                }
            }
        }

        public static void GenerateUniqueAssetPath(ref FileInfo file)
        {
            string name = Path.GetFileNameWithoutExtension(file.FullName);
            string ext = file.Extension;
            if (File.Exists(file.FullName))
            {
                int counter = 1;
                while (File.Exists(file.FullName))
                {
                    file = new FileInfo(Path.Combine(file.Directory.FullName, $"{name} ({counter}){ext}"));
                    counter++;
                }
            }
        }

        public static List<(string, Guid, ushort)> GetAllAssetsOfType(Type type)
        {
            // Go over all loaded meta files and check the Importers type
            List<(string, Guid, ushort)> result = new();
            foreach (var meta in assetGuidToMeta.Values)
            {
                var names = meta.assetNames;
                var types = meta.assetTypes;
                if(names.Length != types.Length)
                {
                    Runtime.Debug.LogWarning($"Meta file {meta.guid} has mismatched names and types at path {AssetDatabase.GetRelativePath(meta.AssetPath.FullName)}");
                    continue;
                }
                for (ushort i = 0; i < types.Length; i++)
                {
                    if (types[i].Equals(type.FullName, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add((names[i], meta.guid, i));
                    }
                }

            }
            return result;
        }

        public static Type GetTypeOfAsset(Guid guid, ushort fileID)
        {
            if(assetGuidToMeta.TryGetValue(guid, out var meta))
                if(meta.assetTypes.Length > fileID)
                    return RuntimeUtils.FindType(meta.assetTypes[fileID]);
            return null;
        }

        public struct SubAssetCache
        {
            public string name;
            public Type? type;
        }

        public static SubAssetCache[] GetSubAssetsCache(Guid guid)
        {
            if (assetGuidToMeta.TryGetValue(guid, out var meta))
            {
                SubAssetCache[] result = new SubAssetCache[meta.assetNames.Length];
                for (int i = 0; i < meta.assetNames.Length; i++)
                {
                    result[i] = new SubAssetCache {
                        name = meta.assetNames[i],
                        type = RuntimeUtils.FindType(meta.assetTypes[i])
                    };
                }
                return result;
            }
            return Array.Empty<SubAssetCache>();
        }

        #endregion

        #region Private Methods

        static string WithEnding(this string str, string ending)
        {
            if (str == null) return ending;
            string result = str;

            // Right() is 1-indexed, so include these cases
            // * Append no characters
            // * Append up to N characters, where N is ending length
            for (int i = 0; i <= ending.Length; i++)
            {
                string tmp = result + ending.Right(i);
                if (tmp.EndsWith(ending))
                    return tmp;
            }

            return result;
        }

        static string Right(this string value, int length)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length), length, "Length is less than zero");
            return (length < value.Length) ? value[^length..] : value;
        }

        static FileInfo GetSerializedFile(Guid assetGuid)
        {
            return new FileInfo(Path.Combine(TempAssetDirectory, assetGuid.ToString("D")) + ".serialized");
        }

        #endregion

    }

}