using Newtonsoft.Json;
using Prowl.Editor;
using Prowl.Editor.Assets;
using Prowl.Runtime.Utils;
using System.Diagnostics;

namespace Prowl.Runtime.Assets
{
    public static class AssetDatabase
    {
        /// <summary> A Utility class for AssetBase to ensure GUID's and Asset paths are loaded and synced correctly. </summary>
        static class GuidPathHolder
        {
            static readonly Dictionary<Guid, string> guidToPath = new();
            static readonly Dictionary<string, Guid> pathToGuid = new(StringComparer.OrdinalIgnoreCase);

            /// <summary>Gets the collection of asset paths.</summary>
            public static IReadOnlyCollection<string> Paths => pathToGuid.Keys;

            /// <summary>Gets the collection of GUIDs.</summary>
            public static IReadOnlyCollection<Guid> GUIDs => guidToPath.Keys;

            /// <summary>Adds a GUID and its associated relative asset path.</summary>
            public static void Add(Guid guid, string relativeAssetPath)
            {
                if (guid == Guid.Empty || string.IsNullOrWhiteSpace(relativeAssetPath)) throw new ArgumentException("Guid cannot be empty and relativeAssetPath cannot be null or whitespace");
                guidToPath[guid] = relativeAssetPath;
                pathToGuid[relativeAssetPath] = guid;
            }

            /// <summary>Removes a GUID and its associated relative asset path.</summary>
            public static void Remove(Guid guid)
            {
                pathToGuid.Remove(guidToPath.GetValueOrDefault(guid, ""));
                guidToPath.Remove(guid);
            }

            /// <summary>Removes a relative asset path and its associated GUID.</summary>
            public static void Remove(string relativeAssetPath)
            {
                guidToPath.Remove(pathToGuid.GetValueOrDefault(relativeAssetPath, Guid.Empty));
                pathToGuid.Remove(relativeAssetPath);
            }

            /// <summary>Clears all GUIDs and paths.</summary>
            public static void Clear()
            {
                guidToPath.Clear();
                pathToGuid.Clear();
            }

            /// <summary>Checks if the given GUID exists in the collection.</summary>
            public static bool Contains(Guid guid) => guidToPath.ContainsKey(guid);

            /// <summary>Gets the GUID associated with the given relative asset path.</summary>
            public static Guid GetGuid(string relativeAssetPath)
            {
                relativeAssetPath = AssetDatabase.NormalizeString(relativeAssetPath);
                if (string.IsNullOrWhiteSpace(relativeAssetPath)) throw new ArgumentException("Path cannot be null or whitespace");
                return pathToGuid.TryGetValue(relativeAssetPath, out var guid) ? guid : Guid.Empty;
            }

            /// <summary>Gets the relative asset path associated with the given GUID.</summary>
            public static string? GetPath(Guid assetGuid)
            {
                if (assetGuid == Guid.Empty) throw new ArgumentException("assetGuid cannot be Empty");
                if (guidToPath.TryGetValue(assetGuid, out var path))
                    return path;
                return null;
            }
        }

        static readonly List<DirectoryInfo> rootFolders = new();
        static readonly List<FileSystemWatcher> rootWatchers = new();

        // Serialized Asset
        static readonly Dictionary<Guid, SerializedAsset> guidToAssetData = new();
        static readonly List<Guid> dirtyAssetData = new();
        static readonly Queue<Guid> refreshedMeta = new();

        static int isEditing = 0;

        public static string TempAssetDirectory => Path.Combine(Project.ProjectDirectory, "Library/AssetDatabase");

        public static event Action<Guid, string>? AssetRemoved;

        public static List<DirectoryInfo> GetRootfolders() => rootFolders;

        public static void AddRootFolder(string rootFolder)
        {
            if (string.IsNullOrWhiteSpace(rootFolder))
                throw new ArgumentException("Root Folder cannot be null or whitespace");

            var rootPath = Path.Combine(Project.ProjectDirectory, rootFolder);
            var info = new DirectoryInfo(rootPath);

            if (!info.Exists)
                info.Create();

            if (rootFolders.Contains(info))
                throw new ArgumentException("Root Folder already exists in the Asset Database");

            rootFolders.Add(info);
            RefreshAll();

            var watcher = new FileSystemWatcher(info.FullName)
            {
                EnableRaisingEvents = true,
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.Size
            };

            static void OnChangedOrRenamed(object sender, FileSystemEventArgs e)
            {
                if (!File.Exists(e.FullPath)) return;

                string ext = Path.GetExtension(e.FullPath);
                if (!ext.Equals(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    if ((File.GetAttributes(e.FullPath) & FileAttributes.Directory) == FileAttributes.Directory)
                        Refresh(new DirectoryInfo(e.FullPath));
                    else
                        Refresh(new FileInfo(e.FullPath));
                }
            }

            watcher.Changed += OnChangedOrRenamed;
            watcher.Renamed += OnChangedOrRenamed;

            rootWatchers.Add(watcher);
        }

        public static bool Contains(Guid guid) => guid != Guid.Empty && GuidPathHolder.Contains(guid);

        public static Guid GUIDFromAssetPath(string relativeAssetPath) =>
            GuidPathHolder.GetGuid(NormalizeString(relativeAssetPath));

        public static Guid GUIDFromAssetPath(FileInfo assetPath) =>
            GuidPathHolder.GetGuid(FileToRelative(assetPath));

        public static string? GUIDToAssetPath(Guid assetGuid) => GuidPathHolder.GetPath(assetGuid);

        public static void StartEditingAsset() => isEditing++;
        public static void StopEditingAsset() => isEditing = Math.Max(0, isEditing - 1);

        public static void MoveAsset(string oldRelativeAssetPath, string newRelativeAssetPath, bool overwrite = false)
        {
            StartEditingAsset();
            oldRelativeAssetPath = NormalizeString(oldRelativeAssetPath);
            newRelativeAssetPath = NormalizeString(newRelativeAssetPath);

            // Move Asset file & meta file if it exists
            var oldAsset = RelativeToFile(oldRelativeAssetPath);
            var newAsset = RelativeToFile(newRelativeAssetPath);
            if (oldAsset.Exists && !newAsset.Exists)
                oldAsset.MoveTo(newAsset.FullName, overwrite);

            var oldMeta = new FileInfo(oldAsset.FullName + ".meta");
            var newMeta = new FileInfo(newAsset.FullName + ".meta");
            if (oldMeta.Exists && !newMeta.Exists)
                oldMeta.MoveTo(newMeta.FullName, overwrite);

            Remove(oldRelativeAssetPath);
            if (!Reimport(newRelativeAssetPath))
                Debug.LogError($"Failed to import {newRelativeAssetPath}!", true);
            StopEditingAsset();
        }

        /// <summary>
        /// Copy an asset from one location to another. This will also copy the meta file and assign a new Guid.
        /// </summary>
        /// <param name="overwrite">If newRelativeAssetPath already exists, should we just overwrite it?</param>
        public static void CopyAsset(string relativeAssetPath, string newRelativeAssetPath, bool overwrite = false)
        {
            StartEditingAsset();
            relativeAssetPath = NormalizeString(relativeAssetPath);
            newRelativeAssetPath = NormalizeString(newRelativeAssetPath);

            // Move Asset file & meta file if it exists
            var oldAsset = RelativeToFile(relativeAssetPath);
            var newAsset = RelativeToFile(newRelativeAssetPath);
            if (oldAsset.Exists && !newAsset.Exists)
                oldAsset.CopyTo(newAsset.FullName, overwrite);

            // Load and save meta to new path
            var oldMeta = LoadMeta(relativeAssetPath);
            if (oldMeta == null) return; // Failed
            oldMeta.guid = Guid.NewGuid();
            oldMeta.Save(new FileInfo(newAsset.FullName + ".meta"));
            StopEditingAsset();
        }

        /// <summary>
        /// Rename an existing asset
        /// </summary>
        /// <param name="assetGuid">An existing Asset</param>
        /// <param name="newName">The name</param>
        /// <exception cref="ArgumentException"></exception>
        public static void RenameAsset(Guid assetGuid, string newName)
        {
            StartEditingAsset();
            if (assetGuid == Guid.Empty) throw new ArgumentException("Asset Guid cannot be empty", nameof(assetGuid));
            if (string.IsNullOrWhiteSpace(newName)) throw new ArgumentException("New Name cannot be null or whitespace", nameof(newName));

            string? relativeAssetPath = GuidPathHolder.GetPath(assetGuid);
            if (relativeAssetPath == null) throw new ArgumentException("Asset Guid does not exist in the Asset Database", nameof(assetGuid));

            string oldRelativeAssetPath = relativeAssetPath;
            string newRelativeAssetPath = Path.GetDirectoryName(relativeAssetPath) + "/" + newName + Path.GetExtension(relativeAssetPath);
            MoveAsset(oldRelativeAssetPath, newRelativeAssetPath);
            StopEditingAsset();
        }

        /// <summary>
        /// Opens the asset with the operating systems Default Program
        /// </summary>
        /// <param name="relativeAssetPath"></param>
        public static void OpenAsset(string relativeAssetPath)
        {
            using Process fileopener = new Process();

            FileInfo info = RelativeToFile(relativeAssetPath);

            fileopener.StartInfo.FileName = "explorer";
            fileopener.StartInfo.Arguments = "\"" + info.FullName + "\"";
            fileopener.Start();
        }

        public static void Clear()
        {
            foreach (var watcher in rootWatchers)
                watcher.Dispose();
            rootWatchers.Clear();
            rootFolders.Clear();
            GuidPathHolder.Clear();
        }

        public static void ReimportAll()
        {
            foreach(var root in rootFolders)
                ReimportFolder(root);
        }

        public static void ReimportFolder(DirectoryInfo directory)
        {
            if(GetRelativePath(directory.FullName) != null)
            {
                // It exists in one of our root folders
                // Go over all files and reimport
                var files = directory.GetFiles("*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var relativeAssetPath = FileToRelative(file);
                    Reimport(relativeAssetPath);
                }
            }
        }

        public static bool Reimport(string relativeAssetPath)
        {

            // Dispose if we already have it
            Guid assetGuid = GUIDFromAssetPath(relativeAssetPath);
            if (assetGuid != Guid.Empty)
            {
                if (guidToAssetData.ContainsKey(assetGuid))
                {
                    var asset = guidToAssetData[assetGuid];
                    asset.Main.DestroyImmediate();
                    asset.SubAssets.ForEach(x => x.DestroyImmediate());
                    guidToAssetData.Remove(assetGuid);
                }
            }

            // Cleanup input string, Makes it consistent
            relativeAssetPath = NormalizeString(relativeAssetPath);

            var meta = LoadMeta(relativeAssetPath);
            if (meta == null)
                return false; // No valid Meta file
            if (meta.importer == null)
                return false; // No valid Importer

            var assetFile = RelativeToFile(relativeAssetPath);

            // Import the asset
            SerializedAsset ctx = new();
            try
            {
                meta.importer.Import(ctx, assetFile);
            }
            catch
            {
                return false; // Import failed
            }
            if (!ctx.HasMain)
                return false; // Import failed no Main Object

            var serialized = GetSerializedFile(relativeAssetPath);
            if (serialized.Exists) // Delete the old asset
                serialized.Delete();

            // Save the asset
            StartEditingAsset();
            meta.lastModified = DateTime.UtcNow;
            SaveMeta(meta, relativeAssetPath);
            ctx.SaveToFile(serialized);
            StopEditingAsset();
            return true;
        }

        public static void Remove(string relativeAssetPath)
        {
            StartEditingAsset();
            // Cleanup input string, Makes it consistent
            relativeAssetPath = NormalizeString(relativeAssetPath);

            var assetFile = RelativeToFile(relativeAssetPath);
            if (assetFile.Exists)
                assetFile.Delete();

            var serialized = GetSerializedFile(relativeAssetPath);
            if (serialized.Exists)
                serialized.Delete();

            Guid assetGuid = GuidPathHolder.GetGuid(relativeAssetPath);

            if (guidToAssetData.ContainsKey(assetGuid))
            {
                var asset = guidToAssetData[assetGuid];
                asset.Main.DestroyImmediate();
                asset.SubAssets.ForEach(x => x.DestroyImmediate());
            }

            guidToAssetData.Remove(assetGuid);
            dirtyAssetData.Remove(assetGuid);
            GuidPathHolder.Remove(relativeAssetPath);

            AssetRemoved?.Invoke(assetGuid, relativeAssetPath);
            StopEditingAsset();
        }

        public static T? LoadAsset<T>(string relativeAssetPath) where T : EngineObject => LoadAsset<T>(GuidPathHolder.GetGuid(relativeAssetPath));

        public static T? LoadAsset<T>(Guid assetGuid) where T : EngineObject
        {
            try
            {
                var serialized = LoadAsset(assetGuid);
                if (serialized == null) return null;
                if (serialized.Main is not T asset) return null;
                asset.AssetID = assetGuid;
                return asset;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                throw new InvalidCastException($"Something went wrong loading asset.");
            }
        }

        public static SerializedAsset LoadAsset(string relativeAssetPath) => LoadAsset(GuidPathHolder.GetGuid(relativeAssetPath));

        public static SerializedAsset LoadAsset(Guid assetGuid)
        {
            if (assetGuid == Guid.Empty) throw new ArgumentException("Asset Guid cannot be empty", nameof(assetGuid));

            if (guidToAssetData.ContainsKey(assetGuid))
                return guidToAssetData[assetGuid];

            string? relativeAssetPath = GuidPathHolder.GetPath(assetGuid);
            if (relativeAssetPath == null) return null; // Asset is missing from database

            FileInfo asset = RelativeToFile(GuidPathHolder.GetPath(assetGuid));
            if (!asset.Exists) throw new FileNotFoundException("Asset file does not exist", asset.FullName);


            FileInfo serializedAssetPath = GetSerializedFile(relativeAssetPath);
            if (!serializedAssetPath.Exists)
                if (!Reimport(relativeAssetPath))
                {
                    Debug.LogError($"Failed to import {serializedAssetPath.FullName}!", true);
                    throw new Exception($"Failed to import {serializedAssetPath.FullName}");
                }
            var serializedAsset = SerializedAsset.FromSerializedAsset(serializedAssetPath.FullName);
            guidToAssetData[assetGuid] = serializedAsset;
            return serializedAsset;
        }

        public static void AddObjectToAsset(Guid assetGuid, string name, EngineObject obj)
        {
            var serializedAsset = LoadAsset(assetGuid);
            if (serializedAsset.SubAssets.Contains(obj)) throw new Exception("Asset already contains this sub asset!");
            serializedAsset.SubAssets.Add(obj);
            dirtyAssetData.Add(assetGuid);
        }

        public static void RemoveObjectFromAsset(Guid assetGuid, EngineObject obj)
        {
            var serializedAsset = LoadAsset(assetGuid);
            if (!serializedAsset.SubAssets.Contains(obj)) throw new Exception("Asset does not contain this sub asset!");
            serializedAsset.SubAssets.Remove(obj);
            dirtyAssetData.Add(assetGuid);
        }

        public static void SaveAssetIfDirty(Guid assetGuid)
        {
            if (dirtyAssetData.Contains(assetGuid) && guidToAssetData.TryGetValue(assetGuid, out var serializedAsset))
            {
                StartEditingAsset();
                var assetFile = RelativeToFile(GuidPathHolder.GetPath(assetGuid));
                serializedAsset.SaveToFile(assetFile);
                dirtyAssetData.Remove(assetGuid);
                StopEditingAsset();
            }
        }

        public static void SaveAssets()
        {
            StartEditingAsset();
            while (dirtyAssetData.Count > 0)
            {
                Guid guid = dirtyAssetData[dirtyAssetData.Count - 1];
                dirtyAssetData.RemoveAt(dirtyAssetData.Count - 1);

                // Save asset if we have it loaded
                if (guidToAssetData.TryGetValue(guid, out var asset))
                {
                    var assetFile = RelativeToFile(GuidPathHolder.GetPath(guid));
                    asset.SaveToFile(assetFile);
                }
            }
            StopEditingAsset();
        }

        public static string? GetRelativePath(string fullFilePath)
        {
            foreach (var rootFolder in rootFolders)
            {
                string rootFolderPath = rootFolder.FullName;
                if (fullFilePath.StartsWith(rootFolderPath, StringComparison.OrdinalIgnoreCase))
                {
                    string relativePath = fullFilePath.Substring(rootFolderPath.Length);

                    // Ensure the relative path doesn't start with a directory separator
                    if (relativePath.StartsWith(Path.DirectorySeparatorChar.ToString()))
                        relativePath = relativePath.Substring(1);

                    // Add the root folder name to the relative path
                    relativePath = Path.Combine(rootFolder.Name, relativePath);

                    return relativePath;
                }
            }
            return null;
        }

        public static void RefreshAll()
        {
            if (isEditing > 0) return;
            foreach (var root in rootFolders)
                Refresh(root);
            ReimportDirtyMeta();
        }

        public static void Refresh(DirectoryInfo directory)
        {
            if (isEditing > 0) return;

            var files = directory.GetFiles("*", SearchOption.AllDirectories);
            foreach (var fullAssetPath in files)
                Refresh(fullAssetPath);

            ReimportDirtyMeta();
        }

        public static void Refresh(FileInfo fullAssetPath)
        {
            if (fullAssetPath.Extension.Equals(".meta", StringComparison.OrdinalIgnoreCase))
            {
                // If we have no asset file, delete the meta file
                string assetPath = Path.ChangeExtension(fullAssetPath.FullName, null);
                if (!File.Exists(assetPath)) // Asset doesnt exist
                    fullAssetPath.Delete(); // Delete Meta
            }
            else
            {
                // Relative path to the asset
                var relativeAssetPath = FileToRelative(fullAssetPath);
                //string relativeAssetPath = GetAssetRelativePath(root, fullAssetPath);
                relativeAssetPath = NormalizeString(relativeAssetPath);

                // Make sure we have a meta file
                var meta = LoadMeta(relativeAssetPath);
                if (meta == null) // no meta, cannot import or handle this asset type
                    return;

                var serialized = GetSerializedFile(relativeAssetPath);
                if (!serialized.Exists) // We dont have the asset, Import it
                    refreshedMeta.Enqueue(meta.guid);
            }
        }

        static void ReimportDirtyMeta()
        {
            while (refreshedMeta.TryDequeue(out var guid))
            {
                string relativeAssetPath = GUIDToAssetPath(guid);
                if (!Reimport(relativeAssetPath))
                    Debug.LogError($"Failed to import {relativeAssetPath}!", true);
            }
            // Use Parallel.ForEach to process multiple items concurrently
            //if (refreshedMeta.Count > 0)
            //{
            //    Parallel.ForEach(refreshedMeta.GetConsumingEnumerable(), guid =>
            //    {
            //        string relativeAssetPath = GUIDToAssetPath(guid);
            //        if (!Reimport(relativeAssetPath))
            //            Console.Error.WriteLine($"Failed to import {relativeAssetPath}!");
            //    });
            //}
        }

        public static MetaFile? LoadMeta(string relativeAssetPath)
        {
            relativeAssetPath = NormalizeString((string)relativeAssetPath);
            var info = RelativeToFile(relativeAssetPath);
            var meta = new FileInfo(info.FullName + ".meta");
            if (!meta.Exists)
            {
                return GenerateNewMetaFile(relativeAssetPath);
            }
            else
            {
                // Load meta file
                var metaFile = MetaFile.Load(meta);
                if(metaFile == null)
                {
                    Debug.LogError($"Failed to load meta file for {relativeAssetPath}, Regenerating!");
                    meta.Delete();
                    return GenerateNewMetaFile(relativeAssetPath);
                }
                GuidPathHolder.Add(metaFile.guid, relativeAssetPath);
                return metaFile;
            }
        }

        static MetaFile? GenerateNewMetaFile(string relativeAssetPath)
        {
            var assetFile = new FileInfo(RelativeToFile(relativeAssetPath).FullName);
            // Create meta file
            var metaFile = new MetaFile(relativeAssetPath);
            metaFile.lastModified = assetFile.LastWriteTimeUtc;
            if (metaFile.importer == null) return null;
            SaveMeta(metaFile, relativeAssetPath);
            GuidPathHolder.Add(metaFile.guid, relativeAssetPath);
            return metaFile;
        }

        static void SaveMeta(MetaFile meta, string relativeAssetPath)
        {
            StartEditingAsset();
            var metaFile = new FileInfo(RelativeToFile(relativeAssetPath).FullName + ".meta");
            meta.Save(metaFile);
            StopEditingAsset();
        }

        // Construct a path relative to root folder but including its name, so like "Assets/Textures/Texture1.png"
        public static string GetAssetRelativePath(DirectoryInfo directory, FileInfo fullAssetPath)
        {
            // Start with creating one relative, this misses the name parent directory
            var path = Path.GetRelativePath(directory.FullName, fullAssetPath.FullName);
            // We currently have "Textures/Texture1.png", Add the root name in
            path = Path.Combine(directory.Name, path);
            return path;
        }

        static string NormalizeString(string path) => path.Replace(@"\\", @"\").Replace(@"\", @"/");

        static FileInfo GetSerializedFile(string relativeAssetPath)
        {
            relativeAssetPath.Replace(@"/", "_").Replace(".", "_");
            return new FileInfo(Path.Combine(TempAssetDirectory, relativeAssetPath) + ".serialized");
        }

        public static FileInfo RelativeToFile(string relativeAssetPath)
        {
            return new FileInfo(Path.Combine(Project.ProjectDirectory, relativeAssetPath));
        }

        public static string FileToRelative(FileInfo file)
        {
            return Path.GetRelativePath(Project.ProjectDirectory, file.FullName);
        }

        public static DirectoryInfo GetRootFolder(string relativeAssetPath)
        {
            // The first part of the path is the Root folder
            var rootFolder = relativeAssetPath.Split('/')[0];
            //return new DirectoryInfo(Path.Combine(Project.ProjectDirectory, rootFolder));
            foreach (var root in rootFolders)
            {
                if (root.Name.Equals(rootFolder, StringComparison.OrdinalIgnoreCase))
                    return root;
            }
            return null;
        }
    }

    public class MetaFile
    {
        [JsonIgnore] public FileInfo AssetPath { get; set; }
        public Guid guid; 
        public DateTime lastModified;
        public ScriptedImporter importer;

        /// <summary>Default constructor for MetaFile.</summary>
        public MetaFile() { }

        /// <summary>Constructor for MetaFile with a relative asset path.</summary>
        /// <param name="relativeAssetPath">The relative path of the asset.</param>
        public MetaFile(string relativeAssetPath)
        {
            var importerType = ImporterAttribute.GetImporter(Path.GetExtension(relativeAssetPath));
            if (importerType == null)
                return;

            this.AssetPath = AssetDatabase.RelativeToFile(relativeAssetPath);
            this.guid = Guid.NewGuid(); 
            this.lastModified = DateTime.UtcNow;
            this.importer = Activator.CreateInstance(importerType) as ScriptedImporter;
        }

        /// <summary>Save the MetaFile to a specified file or default to the associated asset file with a ".meta" extension.</summary>
        /// <param name="file">The file to save the meta data.</param>
        public void Save(FileInfo? file = null)
        {
            file ??= new FileInfo(AssetPath.FullName + ".meta");
            AssetDatabase.StartEditingAsset();
            File.WriteAllText(file.FullName, JsonUtility.Serialize(this));
            AssetDatabase.StopEditingAsset();
        }

        /// <summary>Load a MetaFile from the specified file.</summary>
        /// <param name="file">The file to load the meta data from.</param>
        /// <returns>The loaded MetaFile.</returns>
        public static MetaFile? Load(FileInfo file)
        {
            if (!file.Exists) throw new FileNotFoundException("Meta file does not exist.", file.FullName);
            var meta = JsonUtility.DeserializeFromPath<MetaFile>(file.FullName);
            meta!.AssetPath = new FileInfo(Path.ChangeExtension(file.FullName, null));
            return meta;
        }
    }
}