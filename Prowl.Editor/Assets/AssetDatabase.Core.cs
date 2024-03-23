using Hexa.NET.ImGui;
using Prowl.Editor.EditorWindows;
using Prowl.Runtime;
using Prowl.Runtime.Utils;
using System.IO;
using System.Reflection;
using Debug = Prowl.Runtime.Debug;

namespace Prowl.Editor.Assets
{
    public static partial class AssetDatabase
    {
        #region Properties

        public static Guid LastLoadedAssetID { get; private set; } = Guid.Empty;
        public static string TempAssetDirectory => Path.Combine(Project.ProjectDirectory, "Library/AssetDatabase");

        #endregion

        #region Events

        public static event Action<Guid, FileInfo>? AssetRemoved;
        public static event Action<FileInfo>? Pinged;

        #endregion

        #region Private Fields

        static readonly List<DirectoryInfo> rootFolders = [];
        static double RefreshTimer = 0f;
        static bool lastFocused = false;
        static readonly Dictionary<string, MetaFile> assetPathToMeta = new(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<Guid, MetaFile> assetGuidToMeta = [];
        static readonly Dictionary<string, DateTime> fileLastWriteTimes = [];
        static readonly Dictionary<Guid, SerializedAsset> guidToAssetData = [];

        #endregion


        #region Public Methods

        internal static void InternalUpdate()
        {
            if (Window.IsFocused)
            {
                RefreshTimer += Time.deltaTime;
                if (!lastFocused || RefreshTimer > 5f)
                    Update();
            }
            lastFocused = Window.IsFocused;
        }

        /// <summary>
        /// Gets the list of root folders in the AssetDatabase.
        /// </summary>
        /// <returns>List of root folders.</returns>
        public static List<DirectoryInfo> GetRootFolders() => rootFolders;

        /// <summary>
        /// Adds a root folder to the AssetDatabase.
        /// </summary>
        /// <param name="rootFolder">The path to the root folder.</param>
        public static void AddRootFolder(string rootFolder)
        {
            ArgumentNullException.ThrowIfNullOrEmpty(rootFolder);

            var rootPath = Path.Combine(Project.ProjectDirectory, rootFolder);
            var info = new DirectoryInfo(rootPath);

            if (!info.Exists)
                info.Create();

            if (rootFolders.Contains(info))
                throw new ArgumentException("Root Folder already exists in the Asset Database");

            rootFolders.Add(info);
        }

        /// <summary>
        /// Checks for changes in the AssetDatabase.
        /// Call manually when you make changes to the asset files to ensure the changes are loaded
        /// </summary>
        public static void Update()
        {
            RefreshTimer = 0f;

            HashSet<string> currentFiles = [];
            List<string> toReimport = [];

            foreach (var root in rootFolders)
            {
                var files = Directory.GetFiles(root.FullName, "*", SearchOption.AllDirectories)
                    .Where(file => !file.EndsWith(".meta"));

                foreach (var file in files)
                {
                    // Only process files that are supported by an importer, the rest are ignored
                    if (ImporterAttribute.SupportsExtension(Path.GetExtension(file)))
                    {
                        currentFiles.Add(file);

                        if (!fileLastWriteTimes.TryGetValue(file, out var lastWriteTime)
                            || !MetaFile.HasMeta(new FileInfo(file)))
                        {
                            // New file
                            Debug.Log("Asset Added: " + file);
                            lastWriteTime = File.GetLastWriteTime(file);
                            fileLastWriteTimes[file] = lastWriteTime;
                            if (ProcessFile(file))
                                toReimport.Add(file);
                        }
                        else if (File.GetLastWriteTime(file) != lastWriteTime)
                        {
                            // File modified
                            Debug.Log("Asset Updated: " + file);
                            lastWriteTime = File.GetLastWriteTime(file);
                            fileLastWriteTimes[file] = lastWriteTime;
                            if (ProcessFile(file))
                                toReimport.Add(file);
                        }
                    }
                }
            }

            // Defer the Reimports untill after all Meta files are loaded/updated
            foreach(var file in toReimport)
            {
                Reimport(new(file));
                var color = ImGui.ColorConvertU32ToFloat4(AssetsWindow.GetFileColor(Path.GetExtension(file)));
                EditorGui.Notify("Imported", $"{ToRelativePath(new(file))}!", color, ImGuiToastType.Success);
            }

            // Check for missing paths
            var missingPaths = fileLastWriteTimes.Keys.Except(currentFiles).ToList();
            foreach (var file in missingPaths)
            {
                fileLastWriteTimes.Remove(file);
                bool hasMeta = assetPathToMeta.TryGetValue(file, out var meta);

                if (hasMeta)
                {
                    assetPathToMeta.Remove(file);

                    // The asset could have moved, in which case that's all we need todo
                    // But, if the guid leads to a meta file which has THIS asset path, then no new asset exists
                    // As it would have been updated from the code above and ProcessFile()
                    // Which means it didn't just move somewhere else, its gone
                    if (assetGuidToMeta.TryGetValue(meta.guid, out var existingMeta))
                    {
                        if (existingMeta.AssetPath.FullName.Equals(file, StringComparison.OrdinalIgnoreCase))
                        {
                            assetGuidToMeta.Remove(meta.guid);
                            DestroyStoredAsset(meta.guid);

                            Debug.Log("Asset Deleted: " + file);
                            AssetRemoved?.Invoke(meta.guid, new FileInfo(file));
                        }
                    }
                }

            }
        }

        /// <summary>
        /// Process a File Change
        /// </summary>
        /// <param name="file"></param>
        /// <returns>True if a reimport is needed</returns>
        static bool ProcessFile(string file)
        {
            ArgumentNullException.ThrowIfNullOrEmpty(file);
            var fileInfo = new FileInfo(file);

            var meta = MetaFile.Load(fileInfo);
            if (meta != null)
            {
                // Update the cache to record this new Meta file, Guid and Path
                bool hasMetaAlready = assetGuidToMeta.ContainsKey(meta.guid);
                assetGuidToMeta[meta.guid] = meta;
                assetPathToMeta[fileInfo.FullName] = meta;

                if (hasMetaAlready)
                {
                    if (meta.lastModified != fileInfo.LastWriteTimeUtc)
                    {
                        // File modified, reimport
                        return true;
                    }
                }
                else
                {
                    // New file with meta, import
                    return false;
                }
            }
            else
            {
                // No meta file, create and import
                var newMeta = new MetaFile(fileInfo);
                if (newMeta.importer == null)
                {
                    EditorGui.Notify("No Importer Found", $"No importer found for file:\n{fileInfo.FullName}", new Color(0.8f, 0.1f, 0.1f, 1), ImGuiToastType.Error);
                    return false;
                }
                newMeta.Save();

                assetGuidToMeta[newMeta.guid] = newMeta;
                assetPathToMeta[fileInfo.FullName] = newMeta;
                return true;
            }
            return false; // No need to reimport, nothing changed
        }

        /// <summary>
        /// Tries to get the GUID of a file.
        /// </summary>
        /// <param name="file">The file to get the GUID for.</param>
        /// <param name="guid">The GUID of the file.</param>
        /// <returns>True if the GUID was found, false otherwise.</returns>
        public static bool TryGetGuid(FileInfo file, out Guid guid)
        {
            guid = Guid.Empty;
            ArgumentNullException.ThrowIfNull(file);
            if (!File.Exists(file.FullName)) throw new FileNotFoundException("File does not exist.", file.FullName);
            if(assetPathToMeta.TryGetValue(file.FullName, out var meta))
            {
                guid = meta.guid;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Tries to get the file with the specified GUID.
        /// </summary>
        /// <param name="guid">The GUID of the file.</param>
        /// <param name="file">The file with the specified GUID.</param>
        /// <returns>True if the file was found, false otherwise.</returns>
        public static bool TryGetFile(Guid guid, out FileInfo? file)
        {
            file = null;
            if (guid == Guid.Empty) throw new ArgumentException("Asset Guid cannot be empty", nameof(guid));
            if (assetGuidToMeta.TryGetValue(guid, out var meta))
            {
                file = new FileInfo(meta.AssetPath.FullName);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Clears the AssetDatabase.
        /// You shouldnt call this unless you know what your doing!
        /// </summary>
        public static void Clear()
        {
            rootFolders.Clear();
            assetGuidToMeta.Clear();
            fileLastWriteTimes.Clear();
        }

        /// <summary>
        /// Reimports all assets in the AssetDatabase.
        /// </summary>
        public static void ReimportAll()
        {
            foreach (var root in rootFolders)
                ReimportFolder(root);
        }

        /// <summary>
        /// Reimports all assets in the specified directory.
        /// </summary>
        /// <param name="directory">The directory to reimport assets from.</param>
        public static void ReimportFolder(DirectoryInfo directory)
        {
            ArgumentNullException.ThrowIfNull(directory);
            var files = directory.GetFiles("*", SearchOption.AllDirectories);
            foreach (var file in files)
                Reimport(file);
        }

        /// <summary>
        /// Reimports an asset from the specified file.
        /// </summary>
        /// <param name="assetFile">The asset file to reimport.</param>
        /// <param name="disposeExisting">Whether to dispose the existing asset in memory before reimporting.</param>
        /// <returns>True if the asset was reimported successfully, false otherwise.</returns>
        public static bool Reimport(FileInfo assetFile, bool disposeExisting = true)
        {
            Debug.Log($"Attempting to Import {Path.GetRelativePath(Project.ProjectDirectory, assetFile.FullName)}!");
            ArgumentNullException.ThrowIfNull(assetFile);
            var color = ImGui.ColorConvertU32ToFloat4(AssetsWindow.GetFileColor(assetFile.Extension));

            // Dispose if we already have it
            if (disposeExisting)
                if (TryGetGuid(assetFile, out var assetGuid))
                    DestroyStoredAsset(assetGuid);

            // make sure path exists
            if (!File.Exists(assetFile.FullName))
            {
                EditorGui.Notify("Import Failed", $"Failed to import {ToRelativePath(assetFile)}. Asset does not exist.", color, ImGuiToastType.Error);
                return false;
            }

            var meta = MetaFile.Load(assetFile);
            if (meta == null)
            {
                EditorGui.Notify("Import Failed", $"No valid meta file found for asset: {ToRelativePath(assetFile)}", color, ImGuiToastType.Error);
                return false;
            }
            if (meta.importer == null)
            {
                EditorGui.Notify("Import Failed", $"No valid importer found for asset: {ToRelativePath(assetFile)}", color, ImGuiToastType.Error);
                return false;
            }

            // Import the asset
            SerializedAsset ctx = new();
            try
            {
                meta.importer.Import(ctx, assetFile);
            }
            catch (Exception e)
            {
                EditorGui.Notify("Importer Failed", $"Failed to import {ToRelativePath(assetFile)}. Reason: {e.Message}", color, ImGuiToastType.Error);
                return false; // Import failed
            }

            if (!ctx.HasMain)
            {
                EditorGui.Notify("Importer Failed", $"Failed to import {ToRelativePath(assetFile)}. No main object found.", color, ImGuiToastType.Error);
                return false; // Import failed no Main Object
            }

            // Delete the old imported asset if it exists
            var serialized = GetSerializedFile(meta.guid);
            if (File.Exists(serialized.FullName)) 
                serialized.Delete();

            // Save the asset
            ctx.SaveToFile(serialized, out var dependencies);

            // Update the meta file (LastModified is set by MetaFile.Load)
            meta.assetTypes = new string[ctx.SubAssets.Count];
            for (int i = 0; i < ctx.SubAssets.Count; i++)
                meta.assetTypes[i] = ctx.SubAssets[i].GetType().FullName!;
            meta.dependencies = dependencies.ToList();
            meta.Save();
            return true;
        }

        /// <summary>
        /// Loads an asset of the specified type from the specified file path and file ID.
        /// </summary>
        /// <typeparam name="T">The type of the asset to load.</typeparam>
        /// <param name="assetPath">The file path of the asset to load.</param>
        /// <param name="fileID">The file ID of the asset to load.</param>
        /// <returns>The loaded asset, or null if the asset could not be loaded.</returns>
        public static T? LoadAsset<T>(FileInfo assetPath, int fileID) where T : EngineObject
        {
            ArgumentNullException.ThrowIfNull(assetPath);
            if (TryGetGuid(assetPath, out var guid))
                return LoadAsset<T>(guid, fileID);
            return null;
        }

        /// <summary>
        /// Loads an asset of the specified type from the specified GUID and file ID.
        /// </summary>
        /// <typeparam name="T">The type of the asset to load.</typeparam>
        /// <param name="assetGuid">The GUID of the asset to load.</param>
        /// <param name="fileID">The file ID of the asset to load.</param>
        /// <returns>The loaded asset, or null if the asset could not be loaded.</returns>
        public static T? LoadAsset<T>(Guid assetGuid, int fileID) where T : EngineObject
        {
            if (assetGuid == Guid.Empty) throw new ArgumentException("Asset Guid cannot be empty", nameof(assetGuid));

            try {
                var serialized = LoadAsset(assetGuid);
                if (serialized == null) return null;
                T? asset = null;
                if (fileID == 0)
                {
                    // Main Asset
                    if (serialized.Main is not T) return null;
                    asset = (T)serialized.Main;
                }
                else
                {
                    // Sub Asset
                    if (serialized.SubAssets[fileID - 1] is not T) return null;
                    asset = (T)serialized.SubAssets[fileID - 1];
                }
                asset.AssetID = assetGuid;
                asset.FileID = (short)fileID;
                return asset;
            } catch (Exception e) {
                Console.WriteLine(e.ToString());
                throw new InvalidCastException($"Something went wrong loading asset.");
            }
        }

        /// <summary>
        /// Loads a serialized asset from the specified file path.
        /// </summary>
        /// <param name="assetPath">The file path of the serialized asset to load.</param>
        /// <returns>The loaded serialized asset, or null if the asset could not be loaded.</returns>
        public static SerializedAsset? LoadAsset(FileInfo assetPath)
        {
            ArgumentNullException.ThrowIfNull(assetPath);
            if (TryGetGuid(assetPath, out var guid))
                return LoadAsset(guid);
            return null;
        }

        /// <summary>
        /// Loads a serialized asset from the specified GUID.
        /// </summary>
        /// <param name="assetGuid">The GUID of the serialized asset to load.</param>
        /// <returns>The loaded serialized asset, or null if the asset could not be loaded.</returns>
        public static SerializedAsset? LoadAsset(Guid assetGuid)
        {
            if (assetGuid == Guid.Empty) throw new ArgumentException("Asset Guid cannot be empty", nameof(assetGuid));

            if (guidToAssetData.TryGetValue(assetGuid, out SerializedAsset? value))
                return value;

            if (!TryGetFile(assetGuid, out var asset))
                return null;

            if (!File.Exists(asset!.FullName)) throw new FileNotFoundException("Asset file does not exist.", asset.FullName);

            FileInfo serializedAssetPath = GetSerializedFile(assetGuid);
            if (!File.Exists(serializedAssetPath.FullName))
                if (!Reimport(asset)) {
                    Debug.LogError($"Failed to import {serializedAssetPath.FullName}!");
                    EditorGui.Notify($"Failed to Import {serializedAssetPath.FullName}", "Reason: Failed to import asset.", new Color(0.8f, 0.1f, 0.1f, 1), ImGuiToastType.Error);
                    throw new Exception($"Failed to import {serializedAssetPath.FullName}");
                }
            try {
                var serializedAsset = SerializedAsset.FromSerializedAsset(serializedAssetPath.FullName);
                guidToAssetData[assetGuid] = serializedAsset;
                return serializedAsset;
            } catch {
                Debug.LogError($"Failed to load serialized asset {serializedAssetPath.FullName}!");
                EditorGui.Notify($"Failed to load serialized asset {serializedAssetPath.FullName}", "", new Color(0.8f, 0.1f, 0.1f, 1), ImGuiToastType.Error);
                return null; // Failed file might be in use?
            }
        }

        #endregion

        #region Private Methods

        private static void DestroyStoredAsset(Guid guid)
        {
            if (guidToAssetData.TryGetValue(guid, out SerializedAsset? value))
            {
                value.Destroy();
                guidToAssetData.Remove(guid);
            }
        }

        #endregion
    }
}