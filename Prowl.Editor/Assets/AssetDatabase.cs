using JetBrains.Annotations;
using Prowl.Editor;
using Prowl.Editor.Assets;
using Prowl.Runtime.Utils;
using System.Diagnostics;
using System.IO.Compression;

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

            /// <summary>Checks if the given relativeAssetPath exists in the collection.</summary>
            public static bool Contains(string relativeAssetPath) => pathToGuid.ContainsKey(relativeAssetPath);

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

        public class AssetDatabaseSettings : IProjectSetting
        {
            [Tooltip("Auto recompile all scripts when a change is detected.")]
            public bool m_AutoRecompile = true;
        }

        public static AssetDatabaseSettings Settings => Project.ProjectSettings.GetSetting<AssetDatabaseSettings>();

        public static Guid LastLoadedAssetID { get; private set; } = Guid.Empty;

        static readonly List<DirectoryInfo> rootFolders = new();
        static readonly List<FileSystemWatcher> rootWatchers = new();

        // Serialized Asset
        static readonly Dictionary<Guid, SerializedAsset> guidToAssetData = new();
        static readonly List<Guid> dirtyAssetData = new();
        static readonly Queue<Guid> refreshedMeta = new();

        static int isEditing = 0;
        static bool scriptsDirty = false;
        static Stack<DirectoryInfo> dirtyDirectories = new();
        static Stack<FileInfo> dirtyFiles = new();
        static double RefreshTimer = 0f;

        public static string TempAssetDirectory => Path.Combine(Project.ProjectDirectory, "Library/AssetDatabase");

        public static event Action<Guid, string>? AssetRemoved;
        public static event Action<string>? Pinged;

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
            ReimportDirtyMeta();

            var watcher = new FileSystemWatcher(info.FullName)
            {
                EnableRaisingEvents = true,
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.DirectoryName | NotifyFilters.FileName | NotifyFilters.Size
            };

            static void OnChangedOrRenamed(object sender, FileSystemEventArgs e)
            {
                RefreshTimer = 0f;
                if (!File.Exists(e.FullPath) && !Directory.Exists(e.FullPath)) return;

                string? ext = Path.GetExtension(e.FullPath);
                if (ext == null || !ext.Equals(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    if (ext != null && ext.Equals(".cs", StringComparison.OrdinalIgnoreCase))
                        scriptsDirty = true;

                    if ((File.GetAttributes(e.FullPath) & FileAttributes.Directory) == FileAttributes.Directory)
                        dirtyDirectories.Push(new DirectoryInfo(e.FullPath));
                    else
                        dirtyFiles.Push(new FileInfo(e.FullPath));
                }
                
            }

            static void OnDeleted(object sender, FileSystemEventArgs e)
            {
                RefreshTimer = 0f;
                string ext = Path.GetExtension(e.FullPath);
                if (ext != null && ext.Equals(".cs", StringComparison.OrdinalIgnoreCase))
                    scriptsDirty = true;

                var parent = Directory.GetParent(e.FullPath);
                if (parent.Exists)
                    dirtyDirectories.Push(parent);
                else
                    foreach (var root in rootFolders)
                        dirtyDirectories.Push(root);
            }

            watcher.Deleted += OnDeleted;
            watcher.Created += OnChangedOrRenamed;
            watcher.Changed += OnChangedOrRenamed;
            watcher.Renamed += OnChangedOrRenamed;

            rootWatchers.Add(watcher);
        }

        public static void Update()
        {
            if (isEditing > 0) RefreshTimer = 0f;

            if (Window.IsFocused) {
                // Refresh timer gets set back to 0 every time a change is detected in the file system
                // This is an extra helper to help make sure we refresh after for example a folder is copied into the project and not during.
                RefreshTimer += Time.time;
                if (RefreshTimer > 0.5f) {
                    if (scriptsDirty) {
                        scriptsDirty = false;
                        if (Settings.m_AutoRecompile)
                            EditorApplication.Instance.RegisterReloadOfExternalAssemblies();
                    }

                    bool changed = false;
                    HashSet<string> allPaths = new();
                    while (dirtyDirectories.TryPop(out var dir))
                        foreach (var fullAssetPath in dir.GetFiles("*", SearchOption.AllDirectories))
                            allPaths.Add(FileToRelative(fullAssetPath));
                    while (dirtyFiles.TryPop(out var file))
                        allPaths.Add(FileToRelative(file));

                    for (int i = 0; i < allPaths.Count; i++) {
                        string relativeAssetPath = allPaths.ElementAt(i);
                        if (relativeAssetPath == null) continue;
                        if (Reimport(relativeAssetPath))
                            changed = true;
                    }

                    if (changed) {
                        ReimportDirtyMeta();

                        AssetDatabase.CleanupCache();
                    }
                }
            }
        }

        public static bool Contains(string relativeAssetPath) => GuidPathHolder.Contains(NormalizeString(relativeAssetPath));
        public static bool Contains(Guid guid) => guid != Guid.Empty && GuidPathHolder.Contains(guid);

        public static Guid GUIDFromAssetPath(string relativeAssetPath) =>
            GuidPathHolder.GetGuid(NormalizeString(relativeAssetPath));

        public static Guid GUIDFromAssetPath(FileInfo assetPath) =>
            GuidPathHolder.GetGuid(FileToRelative(assetPath));

        public static string? GUIDToAssetPath(Guid assetGuid) => GuidPathHolder.GetPath(assetGuid);

        public static void StartEditingAsset() => isEditing++;
        public static void StopEditingAsset() => isEditing = Math.Max(0, isEditing - 1);

        public static void Ping(Guid guid) 
        {
            if (guid != Guid.Empty) 
                Ping(GUIDToAssetPath(guid));
        }
        public static void Ping(string relativeAssetPath) => Pinged?.Invoke(relativeAssetPath);

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

        public static void ExportAllBuildPackages(DirectoryInfo directoryInfo)
        {
            if (!directoryInfo.Exists)
            {
                Debug.LogError("Cannot export package, Folder does not exist.");
                return;
            }

            // Get all assets
            var assets = AssetDatabase.GuidPathHolder.GUIDs.ToArray();
            ExportBuildPackages(assets, directoryInfo);
        }

        public static void ExportBuildPackages(Guid[] assetsToExport, DirectoryInfo destination)
        {
            if (!destination.Exists)
            {
                Debug.LogError("Cannot export package, Folder does not exist.");
                return;
            }

            int packageIndex = 0;
            Debug.Log($"Creating First Package {packageIndex}");
            FileInfo firstPackage = new FileInfo(Path.Combine(destination.FullName, $"Data{packageIndex++}.prowl"));

            // Create the package
            var package = AssetBuildPackage.CreateNew(firstPackage);
            int count = 0;
            int maxCount = assetsToExport.Length;
            foreach (var assetGuid in assetsToExport)
            {
                var asset = LoadAsset(assetGuid);
                if (asset == null)
                {
                    Debug.LogError($"Failed to load asset {assetGuid}!");
                    continue;
                }

#warning TODO: We need to do (package.SizeInGB + SizeOfAsset > 4f) instead of just SizeInGB but for now this works
                if (package.SizeInGB > 3f)
                {
                    Debug.Log($"Packing, Reached 4GB...");
                    package.Dispose();
                    Debug.Log($"Creating New Package {packageIndex}");
                    FileInfo next = new FileInfo(Path.Combine(destination.FullName, $"Data{packageIndex++}.prowl"));
                    package = AssetBuildPackage.CreateNew(next);
                }

                string relativeAssetPath = GUIDToAssetPath(assetGuid);
                package.AddAsset(relativeAssetPath, assetGuid, asset);

                count++;
                if(count % 10 == 0 || count >= maxCount - 5)
                {
                    float percentComplete = ((float)count / (float)maxCount) * 100f;
                    Debug.Log($"Exporting Assets To Stream: {count}/{maxCount} - {percentComplete}%");
                }
            }
            Debug.Log($"Packing...");
            package.Dispose();
        }

        public static void ExportPackage(DirectoryInfo directory, bool includeDependencies = false)
        {
#warning TODO: Handle Dependencies
            if (includeDependencies) throw new NotImplementedException("Dependency tracking is not implemented yet.");

            ImFileDialogInfo imFileDialogInfo = new ImFileDialogInfo()
            {
                title = "Export Package",
                directoryPath = new DirectoryInfo(Project.ProjectDirectory),
                fileName = "New Package.prowlpackage",
                type = ImGuiFileDialogType.SaveFile,
                OnComplete = (path) =>
                {
                    var file = new FileInfo(path);
                    if (file.Exists)
                    {
                        Debug.LogError("Cannot export package, File already exists.");
                        return;
                    }

                    // If no extension (or wrong extension) add .scene
                    if (!file.Extension.Equals(".prowlpackage", StringComparison.OrdinalIgnoreCase))
                        file = new FileInfo(file.FullName + ".prowlpackage");

                    // Create the package
                    // Shh, but packages are just zip files ;)
                    using Stream dest = file.OpenWrite();
                    ZipFile.CreateFromDirectory(directory.FullName, dest);
                }
            };
        }

        public static void ImportPackage(FileInfo packageFile)
        {
            if (!packageFile.Exists)
            {
                Debug.LogError("Cannot import package, File does not exist.");
                return;
            }

            // Extract the package
            using Stream source = packageFile.OpenRead();
            ZipFile.ExtractToDirectory(packageFile.FullName, Project.ProjectPackagesDirectory);

#warning TODO: Handle if we already have the asset in our asset database (just dont import it)

            // Import all assets
            RefreshAll();
            ReimportDirtyMeta();
        }

        /// <summary>
        /// Opens the asset with the operating systems Default Program
        /// </summary>
        /// <param name="relativeAssetPath"></param>
        public static void OpenAsset(string relativeAssetPath)
        {
            using Process fileopener = new();

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

        public static bool Reimport(string relativeAssetPath, bool disposeExisting = true)
        {

            // Dispose if we already have it
            if (true)
            {
                Guid assetGuid = GUIDFromAssetPath(relativeAssetPath);
                if (assetGuid != Guid.Empty)
                {
                    if (guidToAssetData.ContainsKey(assetGuid))
                    {
                        var asset = guidToAssetData[assetGuid];
                        asset.Main.DestroyImmediate();
                        guidToAssetData.Remove(assetGuid);
                    }
                }
            }

            // Make sure path exists
            var assetFile = RelativeToFile(relativeAssetPath);
            if (!assetFile.Exists)
                return false; // Asset doesnt exist

            // Cleanup input string, Makes it consistent
            relativeAssetPath = NormalizeString(relativeAssetPath);

            var meta = LoadMeta(relativeAssetPath);
            if (meta == null)
                return false; // No valid Meta file
            if (meta.importer == null)
                return false; // No valid Importer

            // Import the asset
            SerializedAsset ctx = new();
            try
            {
                meta.importer.Import(ctx, assetFile);
            }
            catch (Exception e)
            {
                ImGuiNotify.InsertNotification("Failed to Import Material.", new Color(0.8f, 0.1f, 0.1f, 1), "Reason: " + e.Message);
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

        public static SerializedAsset? LoadAsset(string relativeAssetPath) => LoadAsset(GuidPathHolder.GetGuid(relativeAssetPath));

        public static SerializedAsset? LoadAsset(Guid assetGuid)
        {
            if (assetGuid == Guid.Empty) throw new ArgumentException("Asset Guid cannot be empty", nameof(assetGuid));

            if (guidToAssetData.ContainsKey(assetGuid))
                return guidToAssetData[assetGuid];

            string? relativeAssetPath = GuidPathHolder.GetPath(assetGuid);
            if (relativeAssetPath == null) return null; // Asset is missing from database

            FileInfo asset = RelativeToFile(GuidPathHolder.GetPath(assetGuid));
            if (!asset.Exists) throw null;


            FileInfo serializedAssetPath = GetSerializedFile(relativeAssetPath);
            if (!serializedAssetPath.Exists)
                if (!Reimport(relativeAssetPath))
                {
                    Debug.LogError($"Failed to import {serializedAssetPath.FullName}!", true);
                    throw new Exception($"Failed to import {serializedAssetPath.FullName}");
                }
            try
            {
                var serializedAsset = SerializedAsset.FromSerializedAsset(serializedAssetPath.FullName);
                guidToAssetData[assetGuid] = serializedAsset;
                return serializedAsset;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load serialized asset {serializedAssetPath.FullName}!", true);
                return null; // Failed file might be in use?
            }
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
                {
                    fullAssetPath.Delete(); // Delete Meta
                    // Deleted a Serialized Asset file if we have one
                    var serialized = GetSerializedFile(FileToRelative(new FileInfo(assetPath)));
                    if (serialized.Exists)
                        serialized.Delete();
                }
            }
            else
            {
                // Relative path to the asset
                var relativeAssetPath = FileToRelative(fullAssetPath);
                //string relativeAssetPath = GetAssetRelativePath(root, fullAssetPath);
                relativeAssetPath = NormalizeString(relativeAssetPath);
                var serialized = GetSerializedFile(relativeAssetPath);

                // Make sure we have a meta file
                var meta = LoadMeta(relativeAssetPath);
                if (meta == null) // no meta, cannot import or handle this asset type
                {
                    if(serialized.Exists)
                        serialized.Delete(); // Delete Serialized Asset as the meta file doesnt exist, this serialized asset isnt guranteed to be valid anymore
                    return;
                }

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

            AssetDatabase.CleanupCache();
        }

        public static void CleanupCache()
        {
            // Delete all serialized assets that are no longer in the asset database
            var files = new DirectoryInfo(TempAssetDirectory).GetFiles("*.serialized", SearchOption.AllDirectories);
            // List of modified directories
            var modifiedDirectories = new HashSet<DirectoryInfo>();
            foreach (var file in files) {
                string relativeAssetPath = GetRelativeFromSerializedFile(file);
                if (!Contains(relativeAssetPath)) {
                    file.Delete();
                    if(file.Directory != null)
                        modifiedDirectories.Add(file.Directory);
                }
            }

            // Ensure all empty modified directories are deleted
            bool change = true;
            while (change) {
                change = false;
                foreach (var directory in modifiedDirectories) {
                    if (directory.Exists && directory.GetFiles().Length == 0 && directory.GetDirectories().Length == 0) {
                        directory.Delete();
                        change = true;
                    }
                }
            }
        }

        public static MetaFile? LoadMeta(string relativeAssetPath)
        {
            relativeAssetPath = NormalizeString((string)relativeAssetPath);
            var info = RelativeToFile(relativeAssetPath);
            var meta = new FileInfo(info.FullName + ".meta");
            if (!meta.Exists)
            {
                var newMeta = GenerateNewMetaFile(relativeAssetPath);
                LastLoadedAssetID = newMeta?.guid ?? Guid.Empty;
                return newMeta;
            }
            else
            {
                // Load meta file
                var metaFile = MetaFile.Load(meta);
                if(metaFile == null)
                {
                    Debug.LogError($"Failed to load meta file for {relativeAssetPath}, Regenerating!");
                    meta.Delete();
                    var newMeta = GenerateNewMetaFile(relativeAssetPath);
                    LastLoadedAssetID = newMeta?.guid ?? Guid.Empty;
                    return newMeta;
                }
                GuidPathHolder.Add(metaFile.guid, relativeAssetPath);
                LastLoadedAssetID = metaFile.guid;
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

        // https://stackoverflow.com/questions/5617320/given-full-path-check-if-path-is-subdirectory-of-some-other-path-or-otherwise
        public static bool FileIsInProject(FileInfo file)
        {
            string normalizedPath = Path.GetFullPath(file.FullName.Replace('/', '\\').WithEnding("\\"));
            string normalizedBaseDirPath = Path.GetFullPath(Project.ProjectAssetDirectory.Replace('/', '\\').WithEnding("\\"));
            return normalizedPath.StartsWith(normalizedBaseDirPath, StringComparison.OrdinalIgnoreCase);
        }

        static string WithEnding([CanBeNull] this string str, string ending)
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

        static string Right([NotNull] this string value, int length)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (length < 0) throw new ArgumentOutOfRangeException("length", length, "Length is less than zero");
            return (length < value.Length) ? value.Substring(value.Length - length) : value;
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

        static string GetRelativeFromSerializedFile(FileInfo serializedFile)
        {
            string relativeAssetPath = Path.GetRelativePath(TempAssetDirectory, serializedFile.FullName);
            relativeAssetPath = relativeAssetPath.Replace("_", ".").Replace(".serialized", "");
            relativeAssetPath = NormalizeString(relativeAssetPath);
            return relativeAssetPath;
        }

        public static FileInfo RelativeToFile(string relativeAssetPath)
        {
            return new FileInfo(Path.Combine(Project.ProjectDirectory, relativeAssetPath));
        }

        public static string FileToRelative(FileInfo file)
        {
            return NormalizeString(Path.GetRelativePath(Project.ProjectDirectory, file.FullName));
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
        public FileInfo AssetPath { get; set; }
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
            CompoundTag tag = (CompoundTag)TagSerializer.Serialize(this);
            StringTagConverter.WriteToFile(tag, file);
            AssetDatabase.StopEditingAsset();
        }

        /// <summary>Load a MetaFile from the specified file.</summary>
        /// <param name="file">The file to load the meta data from.</param>
        /// <returns>The loaded MetaFile.</returns>
        public static MetaFile? Load(FileInfo file)
        {
            if (!file.Exists) throw new FileNotFoundException("Meta file does not exist.", file.FullName);
            var tag = StringTagConverter.ReadFromFile(file);
            var meta = TagSerializer.Deserialize<MetaFile>(tag);
            meta!.AssetPath = new FileInfo(Path.ChangeExtension(file.FullName, null));
            return meta;
        }
    }
}