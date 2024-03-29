using Prowl.Runtime;
using Prowl.Runtime.Utils;
using System.IO.Compression;

namespace Prowl.Editor.Assets
{
    public static partial class AssetDatabase
    {

        #region Public Methods

        /// <summary>
        /// Exports all assets to build packages at the specified destination directory.
        /// </summary>
        /// <param name="destination">The destination directory.</param>
        public static void ExportAllBuildPackages(DirectoryInfo destination)
        {
            if (!destination.Exists)
            {
                Debug.LogError("Cannot export package, Folder does not exist.");
                return;
            }

            // Get all assets
            var assets = assetGuidToMeta.Keys.ToArray();
            ExportBuildPackages(assets, destination);
        }

        /// <summary>
        /// Exports build packages for the specified assets to the destination directory.
        /// </summary>
        /// <param name="assetsToExport">The assets to export.</param>
        /// <param name="destination">The destination directory.</param>
        public static void ExportBuildPackages(Guid[] assetsToExport, DirectoryInfo destination)
        {
            if (!destination.Exists)
            {
                Debug.LogError("Cannot export package, Folder does not exist.");
                return;
            }

            int packageIndex = 0;
            Debug.Log($"Creating First Package {packageIndex}");
            FileInfo firstPackage = new(Path.Combine(destination.FullName, $"Data{packageIndex++}.prowl"));

            // Create the package
            var package = AssetBundle.CreateNew(firstPackage);
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
                    FileInfo next = new(Path.Combine(destination.FullName, $"Data{packageIndex++}.prowl"));
                    package = AssetBundle.CreateNew(next);
                }

                if (TryGetFile(assetGuid, out var assetPath))
                {
                    package.AddAsset(ToRelativePath(assetPath), assetGuid, asset);
                }

                count++;
                if (count % 10 == 0 || count >= maxCount - 5)
                {
                    float percentComplete = ((float)count / (float)maxCount) * 100f;
                    Debug.Log($"Exporting Assets To Stream: {count}/{maxCount} - {percentComplete}%");
                }
            }
            Debug.Log($"Packing...");
            package.Dispose();
        }

        /// <summary>
        /// Exports a package from the specified directory.
        /// </summary>
        /// <param name="directory">The directory to export the package from.</param>
        /// <param name="includeDependencies">Whether to include dependencies in the package.</param>
        public static void ExportPackage(DirectoryInfo directory, bool includeDependencies = false)
        {
#warning TODO: Handle Dependencies
            if (includeDependencies) throw new NotImplementedException("Dependency tracking is not implemented yet.");

            ImFileDialogInfo imFileDialogInfo = new() {
                title = "Export Package",
                directoryPath = new DirectoryInfo(Project.ProjectDirectory),
                fileName = "New Package.prowlpackage",
                type = ImGuiFileDialogType.SaveFile,
                OnComplete = (path) => {
                    var file = new FileInfo(path);
                    if (File.Exists(file.FullName))
                    {
                        Debug.LogError("Cannot export package, File already exists.");
                        return;
                    }

                    // If no extension (or wrong extension) add .scene
                    if (!file.Extension.Equals(".prowlpackage", StringComparison.OrdinalIgnoreCase))
                        file = new FileInfo(file.FullName + ".prowlpackage");

                    // Create the package
                    // Shh, but packages are just zip files ;)
                    using Stream destination = file.OpenWrite();
                    ZipFile.CreateFromDirectory(directory.FullName, destination);
                }
            };
        }

        /// <summary>
        /// Imports a package from the specified file.
        /// </summary>
        /// <param name="packageFile">The package file to import.</param>
        public static void ImportPackage(FileInfo packageFile)
        {
            if (!File.Exists(packageFile.FullName))
            {
                Debug.LogError("Cannot import package, File does not exist.");
                return;
            }

            // Extract the package
            using Stream source = packageFile.OpenRead();
            ZipFile.ExtractToDirectory(packageFile.FullName, Project.ProjectPackagesDirectory);

#warning TODO: Handle if we already have the asset in our asset database (just dont import it)

            Update();
        }

        #endregion

    }

}