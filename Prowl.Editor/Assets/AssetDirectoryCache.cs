// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Editor.Assets;

public class AssetDirectoryCache(DirectoryInfo root)
{
    public class DirNode(DirectoryInfo directory, DirNode? parent)
    {
        public readonly DirectoryInfo Directory = directory;
        public readonly DirNode? Parent = parent;
        public readonly List<DirNode> SubDirectories = [];
        public readonly List<FileNode> Files = [];
    }

    public class FileNode
    {
        public readonly FileInfo File;
        public Guid AssetID;
        public readonly AssetDatabase.SubAssetCache[] SubAssets;

        public FileNode(FileInfo file)
        {
            File = file;

            AssetID = Guid.Empty;
            SubAssets = [];
            if (AssetDatabase.TryGetGuid(file, out var guid))
            {
                AssetID = guid;
                SubAssets = AssetDatabase.GetSubAssetsCache(guid);
            }
        }
    }

    public DirNode RootNode => _rootNode;
    public DirectoryInfo Root => _rootNode.Directory;
    public string RootName => _rootNode.Directory.Name;
    public string RootDirectoryPath => _rootNode.Directory.FullName;


    private DirNode _rootNode;
    private readonly DirectoryInfo _rootDir = root;

    public bool PathToNode(string path, out DirNode node)
    {
        node = _rootNode;

        if (!IsPathInsideDirectory(path, node.Directory, out var relativePath))
            return false;

        string[] pathParts = relativePath.Split(Path.DirectorySeparatorChar);
        for (int i = 0; i < pathParts.Length; i++)
        {
            string part = pathParts[i];
            if (string.IsNullOrEmpty(part))
                continue;

            DirNode nextNode = node.SubDirectories.Find(n =>
                n.Directory.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (nextNode is null)
                return false;

            node = nextNode;
        }

        return true;
    }

    static bool IsPathInsideDirectory(string path, DirectoryInfo directoryInfo, out string relativePath)
    {
        relativePath = string.Empty;
        ArgumentException.ThrowIfNullOrEmpty(path, "Path cannot be null or empty.");

        ArgumentNullException.ThrowIfNull(nameof(directoryInfo), "DirectoryInfo cannot be null.");

        // Get the absolute paths
        string directoryFullPath = Path.GetFullPath(directoryInfo.FullName);
        string fullPath = Path.GetFullPath(path);

        // Check if the fullPath starts with the directoryFullPath
        bool result = fullPath.StartsWith(directoryFullPath, StringComparison.OrdinalIgnoreCase);
        if (result) // Get the relative path
            relativePath = fullPath.Substring(directoryFullPath.Length);
        return result;
    }

    public void Refresh()
    {
        _rootNode = BuildDirectoryTree(_rootDir, null);
    }

    private DirNode BuildDirectoryTree(DirectoryInfo directory, DirNode? parent)
    {
        DirNode node = new(directory, parent);
        try
        {
            var directories = directory.GetDirectories();
            foreach (DirectoryInfo subDirectory in directories)
            {
                if (!subDirectory.Exists)
                    continue;

                DirNode subNode = BuildDirectoryTree(subDirectory, node);
                node.SubDirectories.Add(subNode);
            }

            var files = directory.GetFiles();
            foreach (FileInfo file in files)
            {
                if (!File.Exists(file.FullName))
                    continue;

                // Ignore ".meta" files
                if (file.Extension.Equals(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;

                node.Files.Add(new(file));
            }
        }
        catch (Exception)
        {
            // Handle any exceptions that occur during directory traversal
        }

        return node;
    }
}
