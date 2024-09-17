// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.IO.Compression;
using System.Text;

using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Resolver;
using NuGet.Versioning;

using Prowl.Editor.Preferences;
using Prowl.Runtime;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.Assets;

public static partial class AssetDatabase
{
    /// <summary>
    /// The packages the projects wants.
    /// Key: Package ID
    /// Value: Package Version
    /// </summary>
    public static readonly Dictionary<string, string> DesiredPackages = [];

    public static readonly List<IPackageSearchMetadata> Packages = [];

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
                float percentComplete = (count / (float)maxCount) * 100f;
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
    public static void ExportProwlPackage(DirectoryInfo directory, bool includeDependencies = false)
    {
#warning TODO: Handle Dependencies, We do track them now, just need to support that here
        if (includeDependencies) throw new NotImplementedException("Dependency tracking is not implemented yet.");

        FileDialogContext imFileDialogInfo = new()
        {
            title = "Export Package",
            parentDirectory = Project.Active.ProjectDirectory,
            resultName = "New Package.prowlpackage",
            type = FileDialogType.SaveFile,
            OnComplete = (path) =>
            {
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
    public static void ImportProwlPackage(FileInfo packageFile)
    {
        if (!File.Exists(packageFile.FullName))
        {
            Debug.LogError("Cannot import package, File does not exist.");
            return;
        }

        // Extract the package
        using Stream source = packageFile.OpenRead();
        ZipFile.ExtractToDirectory(packageFile.FullName, Project.Active.PackagesDirectory.FullName);

#warning TODO: Handle if we already have the asset in our asset database (just dont import it)

        Update();
    }

    #endregion

    #region Nuget Packages

    public static IPackageSearchMetadata? GetInstalledPackage(string packageId)
    {
        return Packages.Where(x => x.Identity.Id == packageId).FirstOrDefault();
    }

    internal static async void LoadPackages()
    {
        // Load Packages.txt
        string packagesPath = Path.Combine(Project.Active.PackagesDirectory.FullName, "Packages.txt");
        if (File.Exists(packagesPath))
        {
            string[] lines = File.ReadAllText(packagesPath).Split(';');
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                string[] nodes = line.Split('=');
                if (nodes.Length != 2)
                {
                    Debug.LogError($"Invalid Package: {line}");
                    continue;
                }

                DesiredPackages[nodes[0]] = nodes[1];
            }
        }

        // Validate Packages
        foreach (var pair in DesiredPackages)
        {
            var dependency = (await GetPackageMetadata(pair.Key, Project.Active.PackagesDirectory.FullName, PackageManagerPreferences.Instance.IncludePrerelease))
                             .Where(x => x.Identity.Version.ToString() == pair.Value).FirstOrDefault();
            if (dependency == null)
            {
                // The package is not installed, Lets try to install it
                try
                {
                    await InstallPackage(pair.Key, pair.Value);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error Installing Package: {pair.Key} {pair.Value} : {ex.Message}");
                }
            }
            else
            {
                Debug.Log($"Found Package: {pair.Key} : {pair.Value}");
                Packages.Add(dependency);
            }
        }
    }

    public static void SaveProjectPackagesFile()
    {
        StringBuilder sb = new StringBuilder();
        foreach (var pair in DesiredPackages)
        {
            sb.AppendLine($"{pair.Key}={pair.Value};");
        }
        File.WriteAllText(Path.Combine(Project.Active.PackagesDirectory.FullName, "Packages.txt"), sb.ToString());
    }

    public static async Task<List<NuGetVersion>> GetPackageVersions(string packageId, string source, bool includePrerelease)
    {
        ArgumentNullException.ThrowIfNull(packageId, nameof(packageId));
        ArgumentNullException.ThrowIfNull(source, nameof(source));

        SourceRepository repository = Repository.Factory.GetCoreV3(source);
        FindPackageByIdResource resource = await repository.GetResourceAsync<FindPackageByIdResource>();
        IEnumerable<NuGetVersion> versions = await resource.GetAllVersionsAsync(packageId, new(), NugetLogger.Instance, CancellationToken.None);
        return includePrerelease ? versions.ToList() : versions.Where(x => x.IsPrerelease == false).ToList();
    }

    public static async Task<List<IPackageSearchMetadata>> SearchPackages(string packageKeyword, string source, bool includePrerelease)
    {
        ArgumentNullException.ThrowIfNull(packageKeyword, nameof(packageKeyword));
        ArgumentNullException.ThrowIfNull(source, nameof(source));

        SourceRepository repository = Repository.Factory.GetCoreV3(source);
        PackageSearchResource resource = await repository.GetResourceAsync<PackageSearchResource>();
        SearchFilter searchFilter = new SearchFilter(includePrerelease);
        var results = await resource.SearchAsync(packageKeyword, searchFilter, 0, 50, NugetLogger.Instance, CancellationToken.None);
        return results.Where(p => p.Tags.Contains("Prowl")).ToList(); // TODO: Is this a good way to handle Tags?
    }

    public static async Task<List<IPackageSearchMetadata>> GetPackageMetadata(string packageId, string source, bool includePrerelease)
    {
        ArgumentNullException.ThrowIfNull(packageId, nameof(packageId));
        ArgumentNullException.ThrowIfNull(source, nameof(source));

        SourceRepository repository = Repository.Factory.GetCoreV3(source);
        PackageMetadataResource resource = await repository.GetResourceAsync<PackageMetadataResource>();
        return (await resource.GetMetadataAsync(packageId, includePrerelease, true, new(), NugetLogger.Instance, CancellationToken.None)).ToList();
    }

    public static async Task InstallPackage(string packageId, string version)
    {
        ArgumentNullException.ThrowIfNull(packageId, nameof(packageId));
        ArgumentNullException.ThrowIfNull(version, nameof(version));

        var nuGetFramework = NuGetFramework.ParseFolder("net8.0"); // TODO: Not really sure what todo with this? should we use "Prowl" instead of net8.0?
        var settings = Settings.LoadDefaultSettings(null);
        var srp = new SourceRepositoryProvider(new PackageSourceProvider(settings), Repository.Provider.GetCoreV3());

        using (var cache = new SourceCacheContext())
        {
            List<SourceRepository> repositories = new();
            foreach (var source in PackageManagerPreferences.Instance.Sources)
            {
                if (source.IsEnabled)
                {
                    var sourceRepo = srp.CreateRepository(new PackageSource(source.Source, source.Name, true));
                    repositories.Add(sourceRepo);
                }
            }

            var available = new HashSet<SourcePackageDependencyInfo>(PackageIdentityComparer.Default);
            await GetPackageDependencies(new(packageId, NuGetVersion.Parse(version)), nuGetFramework, cache, NugetLogger.Instance, repositories, available);

            PackageResolverContext resolverContext = new(DependencyBehavior.Lowest, [packageId], [], [], [], available, srp.GetRepositories().Select(s => s.PackageSource), NugetLogger.Instance);

            PackageResolver resolver = new();

            var packagesToInstall = resolver.Resolve(resolverContext, CancellationToken.None).Select(p => available.Single(x => PackageIdentityComparer.Default.Equals(x, p)));

            PackagePathResolver packagePathResolver = new(Project.Active.PackagesDirectory.FullName);
            PackageExtractionContext packageExtractionContext = new(PackageSaveMode.Defaultv3, XmlDocFileSaveMode.None, ClientPolicyContext.GetClientPolicy(settings, NugetLogger.Instance), NugetLogger.Instance);

            FrameworkReducer frameworkReducer = new();
            PackageDownloadContext downloadContext = new(cache);

            foreach (var packageToInstall in packagesToInstall)
            {
                var installedPath = packagePathResolver.GetInstalledPath(packageToInstall);
                if (installedPath == null)
                {
                    var downloadResource = await packageToInstall.Source.GetResourceAsync<DownloadResource>(CancellationToken.None);
                    var downloadResult = await downloadResource.GetDownloadResourceResultAsync(packageToInstall, downloadContext, SettingsUtility.GetGlobalPackagesFolder(settings), NugetLogger.Instance, CancellationToken.None);

                    await PackageExtractor.ExtractPackageAsync(downloadResult.PackageSource, downloadResult.PackageStream, packagePathResolver, packageExtractionContext, CancellationToken.None);
                }

                DesiredPackages[packageToInstall.Id] = packageToInstall.Version.ToString();

                var metaData = (await GetPackageMetadata(packageId, Project.Active.PackagesDirectory.FullName, PackageManagerPreferences.Instance.IncludePrerelease)).Where(x => x.Identity.Version.ToString() == packageToInstall.Version.ToString()).FirstOrDefault();

                Packages.RemoveAll(x => x.Identity.Id == packageId);
                Packages.Add(metaData);

                SaveProjectPackagesFile();
            }
        }

        Update();
    }

    public static void UninstallPackage(string packageId, string version)
    {
        ArgumentNullException.ThrowIfNull(packageId, nameof(packageId));
        ArgumentNullException.ThrowIfNull(version, nameof(version));

        // Simply delete the folder & remove from the list
        var packageDirectory = Path.Combine(Project.Active.PackagesDirectory.FullName, $"{packageId}.{version}");
        if (Directory.Exists(packageDirectory))
            Directory.Delete(packageDirectory, true);

        Packages.RemoveAll(x => x.Identity.Id == packageId);
        DesiredPackages.Remove(packageId);

        SaveProjectPackagesFile();

        Update();
    }

    public static async Task GetPackageDependencies(PackageIdentity package, NuGetFramework framework, SourceCacheContext cacheContext, ILogger logger, IEnumerable<SourceRepository> repositories, ISet<SourcePackageDependencyInfo> availablePackages)
    {
        if (availablePackages.Contains(package))
            return;

        foreach (var sourceRepository in repositories)
        {
            var dependencyInfoResource = await sourceRepository.GetResourceAsync<DependencyInfoResource>();
            var dependencyInfo = await dependencyInfoResource.ResolvePackage(package, framework, cacheContext, logger, CancellationToken.None);

            if (dependencyInfo == null)
                continue;

            foreach (var dependency in dependencyInfo.Dependencies)
                await GetPackageDependencies(new(dependency.Id, dependency.VersionRange.MinVersion), framework, cacheContext, logger, repositories, availablePackages);
        }
    }

    public class NugetLogger : LoggerBase
    {
        private static ILogger? _instance;
        public static ILogger Instance => _instance ??= new NugetLogger();

        public override void Log(ILogMessage message) => Debug.Log(message.Message);

        public override void Log(LogLevel level, string data)
        {
            switch (level)
            {
                case LogLevel.Debug or LogLevel.Verbose or LogLevel.Minimal: Debug.Log(data); break;
                case LogLevel.Warning:                                       Debug.LogWarning(data); break;
                case LogLevel.Error:                                         Debug.LogError(data); break;
            }
        }

        public override Task LogAsync(ILogMessage message)
        {
            Debug.Log(message.Message);
            return Task.CompletedTask;
        }

        public override Task LogAsync(LogLevel level, string data)
        {
            switch (level)
            {
                case LogLevel.Debug or LogLevel.Verbose or LogLevel.Minimal: Debug.Log(data); break;
                case LogLevel.Warning:                                       Debug.LogWarning(data); break;
                case LogLevel.Error:                                         Debug.LogError(data); break;
            }
            return Task.CompletedTask;
        }
    }

    #endregion

}
