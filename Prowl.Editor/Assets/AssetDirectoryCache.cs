namespace Prowl.Editor.Assets
{
    public class AssetDirectoryCache(DirectoryInfo root)
    {
        public class DirNode(DirectoryInfo directory)
        {
            public DirectoryInfo Directory = directory;
            public List<DirNode> SubDirectories = [];
            public List<FileNode> Files = [];
        }

        public class FileNode
        {
            public FileInfo File;
            public Guid AssetID;
            public AssetDatabase.SubAssetCache[] SubAssets;

            public FileNode(FileInfo file)
            {
                File = file;

                AssetID = Guid.Empty;
                SubAssets = Array.Empty<AssetDatabase.SubAssetCache>();
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
        private DirectoryInfo _rootDir = root;

        public void Refresh()
        {
            _rootNode = BuildDirectoryTree(_rootDir);
        }

        private DirNode BuildDirectoryTree(DirectoryInfo directory)
        {
            DirNode node = new(directory);
            try
            {
                var directories = directory.GetDirectories();
                foreach (DirectoryInfo subDirectory in directories)
                {
                    if (!subDirectory.Exists)
                        continue;

                    DirNode subNode = BuildDirectoryTree(subDirectory);
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
}