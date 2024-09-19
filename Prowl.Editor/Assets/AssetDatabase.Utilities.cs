// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Diagnostics;

using Prowl.Runtime;

namespace Prowl.Editor.Assets;

public static partial class AssetDatabase
{

    #region Public Methods

    public static bool PathToCachedNode(string path, out AssetDirectoryCache.DirNode? node)
    {
        node = null;
        foreach (var tuple in rootFolders)
        {
            if (tuple.Item2.PathToNode(path, out node))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Converts a file path to a relative path within the project.
    /// </summary>
    /// <param name="file">The file to convert to a relative path.</param>
    /// <returns>The relative path of the file within the project.</returns>
    public static string ToRelativePath(FileInfo file) => Path.GetRelativePath(Project.Active.ProjectPath, file.FullName);

    /// <summary>
    /// Converts a relative path within the project to a full file path.
    /// </summary>
    /// <param name="relativePath">The relative path to convert to a full file path.</param>
    /// <returns>The full file path of the relative path.</returns>
    public static FileInfo FromRelativePath(string relativePath) => new(Path.Combine(Project.Active.ProjectPath, relativePath));

    /// <summary>
    /// Opens the specified file with the operating system's default program.
    /// </summary>
    /// <param name="file">The file to open.</param>
    public static void OpenPath(FileSystemInfo file, int line = 0, int character = 0)
    {
        var prefs = Preferences.EditorPreferences.Instance;

        if (!string.IsNullOrWhiteSpace(prefs.fileEditor))
        {
            if (!string.IsNullOrWhiteSpace(prefs.fileEditorArgs))
            {
                string args = prefs.fileEditorArgs;

                args = args.Replace("${ProjectDirectory}", Project.Active.ProjectPath);
                args = args.Replace("${File}", file.FullName);
                args = args.Replace("${Line}", line.ToString());
                args = args.Replace("${Character}", character.ToString());

                ProcessStartInfo info = new();
                info.UseShellExecute = true;
                info.Arguments = args;
                info.FileName = prefs.fileEditor;

                Process.Start(info);
            }
            else
            {
                ProcessStartInfo info = new();
                info.UseShellExecute = true;
                info.Arguments = file.FullName;
                info.FileName = prefs.fileEditor;

                Process.Start(info);
            }
        }
        else if (OperatingSystem.IsWindows())
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
            string rootFolderPath = rootFolder.Item1.FullName;
            if (fullFilePath.StartsWith(rootFolderPath, StringComparison.OrdinalIgnoreCase))
            {
                string relativePath = fullFilePath[rootFolderPath.Length..];

                // Ensure the relative path doesn't start with a directory separator
                if (relativePath.StartsWith(Path.DirectorySeparatorChar.ToString()))
                    relativePath = relativePath[1..];

                // Add the root folder name to the relative path
                relativePath = Path.Combine(rootFolder.Item1.Name, relativePath);

                return relativePath;
            }
        }
        return null;
    }

    public static void GetDependenciesDeep(Guid assetID, ref HashSet<Guid> dependencies)
    {
        if (assetGuidToMeta.TryGetValue(assetID, out var meta))
        {
            var dependsOn = meta.dependencies.ToHashSet();
            foreach (var dependency in dependsOn)
            {
                if (!dependencies.Contains(dependency))
                {
                    dependencies.Add(dependency);
                    GetDependenciesDeep(dependency, ref dependencies);
                }
            }
        }
    }

    public static HashSet<Guid> AllThatDependOn(Guid dependsOn)
    {
        HashSet<Guid> result = [];
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
        return file.FullName.StartsWith(Project.Active.AssetDirectory.FullName, StringComparison.OrdinalIgnoreCase);
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

    public static List<(string, Guid, ushort)> GetAllAssetsOfType<T>() where T : EngineObject => GetAllAssetsOfType(typeof(T));
    public static List<(string name, Guid assetID, ushort fileID)> GetAllAssetsOfType(Type type)
    {
        // Go over all loaded meta files and check the Importers type
        List<(string, Guid, ushort)> result = [];
        foreach (var meta in assetGuidToMeta.Values)
        {
            var names = meta.assetNames;
            var types = meta.assetTypes;
            if (names.Length != types.Length)
            {
                Runtime.Debug.LogWarning($"Meta file {meta.guid} has mismatched names and types at path {GetRelativePath(meta.AssetPath.FullName)}");
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

    public static Type? GetTypeOfAsset(Guid guid, ushort fileID)
    {
        if (assetGuidToMeta.TryGetValue(guid, out var meta))
            if (meta.assetTypes.Length > fileID)
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
                result[i] = new SubAssetCache
                {
                    name = meta.assetNames[i],
                    type = RuntimeUtils.FindType(meta.assetTypes[i])
                };
            }
            return result;
        }
        return [];
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
