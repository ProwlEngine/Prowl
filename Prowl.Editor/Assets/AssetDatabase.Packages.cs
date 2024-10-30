// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using BepuPhysics.CollisionDetection;

using Prowl.Runtime;
using Prowl.Runtime.Utils;

using SemVersion;

namespace Prowl.Editor.Assets;

#warning TODO: Support other sources then just Github, should support any git source, Also would be nice to look into NuGet support

public class GithubPackageMetaData
{
    public string name { get; set; } = "";
    public string description { get; set; } = "";
    public string author { get; set; } = "";
    public string iconurl { get; set; } = "";
    public string license { get; set; } = "";
    public string homepage { get; set; } = "";
    public Dictionary<string, string> dependencies { get; set; } = [];
}

public static partial class AssetDatabase
{
    private static HttpClient s_httpClient;
    private static JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

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
                Debug.LogError($"Failed to load asset {assetGuid}.");
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

    #region Git Packages

    private static void ValidateIsReady()
    {
        if (!Project.HasProject) throw new Exception("Cannot load packages, No project is open.");
        ArgumentNullException.ThrowIfNullOrEmpty(Project.Active?.PackagesDirectory.FullName);
    }

    public static async Task ValidatePackages()
    {
        ValidateIsReady();
        Debug.Log("Validating packages...");

        DirectoryInfo packagesPath = Project.Active!.PackagesDirectory;
        string packagesJsonPath = Path.Combine(packagesPath.FullName, "Packages.json");

        // Initialize safety mechanisms
        using var lockManager = new AsyncFileLocker(packagesPath.FullName, ".package-lock");
        var dependencyDetector = new CircularDependencyDetector();

        // Acquire lock with timeout
        if (!await lockManager.AcquireLockAsync(TimeSpan.FromSeconds(30)))
            throw new TimeoutException("Could not acquire package manager lock. Another operation might be in progress.");

        // Load Packages.json with backup handling
        Dictionary<string, string> packageVersions = [];
        string? backupContent = null;
        if (File.Exists(packagesJsonPath))
        {
            try
            {
                backupContent = File.ReadAllText(packagesJsonPath);
                packageVersions = JsonSerializer.Deserialize<Dictionary<string, string>>(backupContent) ?? [];
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to parse Packages.json: {ex.Message}");
                if (backupContent != null)
                {
                    // Create backup before attempting repair
                    File.WriteAllText($"{packagesJsonPath}.bak", backupContent);
                    Debug.Log("Created backup of corrupted Packages.json");
                }
                return;
            }
        }
        else
        {
            Debug.LogWarning("Packages.json not found, creating new file...");
            packageVersions.Add("pfraces-graveyard/git-install", "0.5.0");
            await SafeWriteJson(packagesJsonPath, packageVersions);
        }

        // Build dependency graph
        foreach ((string package, string version) in packageVersions)
        {
            GithubPackageMetaData? packageJson = await LoadPackageJson(
                Path.Combine(packagesPath.FullName, ConvertToPath(package)!, "package.json"));

            if (packageJson?.dependencies != null)
            {
                foreach ((string depPackage, string _) in packageJson.dependencies)
                {
                    dependencyDetector.AddDependency(package, depPackage);
                }
            }
        }

        // Validate each package
        bool validateAgain = false;
        var packagesCopy = new Dictionary<string, string>(packageVersions);
        foreach (var (githubRepo, versionRange) in packagesCopy)
        {
            // Check for circular dependencies
            if (dependencyDetector.HasCircularDependency(githubRepo, out var circle))
            {
                Debug.LogError($"Circular dependency detected: {string.Join(" -> ", circle!)}");
                packageVersions.Remove(githubRepo);
                continue;
            }


            (bool isValid, bool needsValidation) = await ValidatePackage(githubRepo, versionRange, packagesPath,
                                                                   packagesJsonPath, packageVersions);
            validateAgain |= needsValidation;
            if (!isValid)
            {
                packageVersions.Remove(githubRepo);
                await SafeWriteJson(packagesJsonPath, packageVersions);
            }
        }

        // Clean up orphaned package directories
        await CleanupOrphanedPackages(packagesPath, packageVersions);

        // Recursively validate to handle new dependencies
        if (validateAgain)
            await ValidatePackages();
    }

    private static async Task<(bool isValid, bool validateAgain)> ValidatePackage(
        string githubRepo,
        string versionRange,
        DirectoryInfo packagesPath,
        string packagesJsonPath,
        Dictionary<string, string> packageVersions)
    {
        string githubPath = ConvertToPath(githubRepo) ?? throw new Exception($"Invalid GitHub repository path: {githubRepo}");
        string fileSafeName = githubPath.Replace('/', '.');
        string packageDir = Path.Combine(packagesPath.FullName, fileSafeName);
        string packageJsonPath = Path.Combine(packageDir, "package.json");

        Debug.Log($"Validating package {githubPath}...");

        try
        {
            SemanticVersion? currentVersion = await GetInstalledVersion(githubPath);

            // Handle corrupted or incomplete installations
            if (Directory.Exists(packageDir))
            {
                if (currentVersion == null)
                {
                    Debug.LogWarning($"Failed to get version for package {githubPath}, Uninstalling...");
                    await SafeDeleteDirectory(packageDir);
                }
                else if (!await ValidatePackageIntegrity(packageDir, packageJsonPath))
                {
                    Debug.LogWarning($"Package {githubPath} integrity check failed, reinstalling...");
                    await SafeDeleteDirectory(packageDir);
                    currentVersion = null;
                }
            }

            var range = new SemVersion.Parser.Range(versionRange);
            if (currentVersion is null || !range.IsSatisfied(currentVersion))
            {
                // Get available versions with retry logic
                List<SemanticVersion> versions = await RetryWithTimeout(
                    () => GetPackageVersions(githubPath),
                    maxAttempts: 3,
                    timeout: TimeSpan.FromSeconds(30)
                );

                SemanticVersion? bestVersion = FindBestVersion(versions, versionRange)
                    ?? throw new Exception($"No version matching '{versionRange}' found for {githubPath}");

                Directory.CreateDirectory(packageDir);

                try
                {
                    if (currentVersion is null)
                    {
                        Debug.Log($"Installing package {githubPath} version {bestVersion}...");
                        await ExecuteGitCommand($"clone \"https://github.com/{githubPath}.git\" \"{packageDir}\"");
                    }
                    else
                    {
                        Debug.Log($"Updating package {githubPath} from {currentVersion} to {bestVersion}...");
                    }

                    await ExecuteGitCommand($"-C \"{packageDir}\" fetch --tags");
                    await ExecuteGitCommand($"-C \"{packageDir}\" checkout -f tags/{bestVersion}");

                    // TODO: Implement package.json validation
                    // Verify package.json after installation/update
                    //if (!await ValidatePackageJson(packageJsonPath, githubPath))
                    //    throw new Exception("Package validation failed after installation");
                }
                catch (Exception ex)
                {
                    await SafeDeleteDirectory(packageDir);
                    throw new Exception($"Failed to install/update package: {ex.Message}");
                }
            }

            // If we got here, the package itself is valid
            // Now check dependencies and update validateAgain if needed
            bool validateAgain = await ValidateDependencies(
                packagesJsonPath,
                packageVersions,
                githubPath,
                packageJsonPath);

            return (true, validateAgain);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to validate package {githubPath}: {ex.Message}");
            await SafeDeleteDirectory(packageDir);
            return (false, false);
        }
    }

    private static async Task<bool> ValidateDependencies(
        string packagesJsonPath,
        Dictionary<string, string> packageVersions,
        string githubPath,
        string packageJsonPath)
    {
        GithubPackageMetaData? package = await LoadPackageJson(packageJsonPath);
        if (package?.dependencies == null)
            return false; // No dependencies means no need to validate again

        Debug.Log($"Validating dependencies for package {githubPath}...");
        bool changed = false;

        foreach ((string depRepo, string depVersion) in package.dependencies)
        {
            // Validate dependency format
            if (!IsValidDependencyFormat(depRepo, depVersion))
            {
                Debug.LogWarning($"Invalid dependency format: {depRepo} [{depVersion}] in {githubPath}");
                continue;
            }

            if (!packageVersions.TryGetValue(depRepo, out string? existingVersion))
            {
                packageVersions[depRepo] = depVersion;
                changed = true;
            }
            else
            {
                // Version conflict resolution
                var required = SemanticVersion.Parse(depVersion);
                var existing = SemanticVersion.Parse(existingVersion);

                // Simple highest-wins strategy
                if (required > existing)
                {
                    Debug.Log($"Upgrading {depRepo} from {existing} to {required} due to dependency requirement in {githubPath}");
                    packageVersions[depRepo] = required.ToString();
                    changed = true;
                }
            }
        }

        if (changed)
        {
            await SafeWriteJson(packagesJsonPath, packageVersions);
            return true;
        }

        return false;
    }

    // Helper methods
    private static async Task<bool> ValidatePackageIntegrity(string packageDir, string packageJsonPath)
    {
        // Check if git repository is valid
        try
        {
            if (File.Exists(packageJsonPath))
            {
                await ExecuteGitCommand($"-C \"{packageDir}\" status");
                return true;
            }
        }
        catch { }
        return false;
    }

    private static async Task<T> RetryWithTimeout<T>(
        Func<Task<T>> operation,
        int maxAttempts = 3,
        TimeSpan? timeout = null,
        TimeSpan? retryDelay = null)
    {
        timeout ??= TimeSpan.FromSeconds(30);
        retryDelay ??= TimeSpan.FromSeconds(1);

        using var cts = new CancellationTokenSource(timeout.Value);
        Exception? lastException = null;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                if (attempt > 0)
                    await Task.Delay(retryDelay.Value * attempt, cts.Token);

                return await operation();
            }
            catch (Exception ex) when (attempt < maxAttempts - 1 && !cts.Token.IsCancellationRequested)
            {
                lastException = ex;
                Debug.LogWarning($"Attempt {attempt + 1} failed: {ex.Message}. Retrying...");
            }
        }

        throw new TimeoutException(
            $"Operation failed after {maxAttempts} attempts within {timeout.Value.TotalSeconds} seconds",
            lastException);
    }

    private static async Task SafeDeleteDirectory(string path)
    {
        const int maxRetries = 3;
        const int retryDelayMs = 100;

        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    // Force removal of read-only attributes
                    foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                        File.SetAttributes(file, FileAttributes.Normal);
                    Directory.Delete(path, recursive: true);
                }
                return;
            }
            catch (IOException) when (i < maxRetries - 1)
            {
                await Task.Delay(retryDelayMs * (i + 1));
            }
        }
        throw new IOException($"Failed to delete directory {path} after {maxRetries} attempts");
    }

    private static async Task SafeWriteJson<T>(string path, T content)
    {
        string tempPath = path + ".tmp";
        string backupPath = path + ".bak";

        try
        {
            // Create backup of existing file if it exists
            if (File.Exists(path))
                File.Copy(path, backupPath, overwrite: true);

            // Write to temporary file
            await File.WriteAllTextAsync(
                tempPath,
                JsonSerializer.Serialize(content, s_jsonOptions));

            // Verify the written content can be deserialized
            try
            {
                T? verification = JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(tempPath))
                    ?? throw new JsonException("Verification failed - null content");
            }
            catch
            {
                // If verification fails, restore from backup
                if (File.Exists(backupPath))
                    File.Copy(backupPath, path, overwrite: true);
                throw;
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempPath))   File.Delete(tempPath);
            if (File.Exists(backupPath)) File.Delete(backupPath);
        }
    }

    private static async Task CleanupOrphanedPackages(DirectoryInfo packagesPath, Dictionary<string, string> validPackages)
    {
        foreach (DirectoryInfo dir in packagesPath.GetDirectories())
        {
            string packageName = dir.Name.Replace('.', '/');
            if (!validPackages.ContainsKey(packageName))
            {
                Debug.Log($"Removing orphaned package directory: {dir.Name}");
                await SafeDeleteDirectory(dir.FullName);
            }
        }
    }

    private static async Task<GithubPackageMetaData?> LoadPackageJson(string packageJsonPath)
    {
        if (!File.Exists(packageJsonPath))
        {
            Debug.LogError($"package.json not found at {packageJsonPath}");
            return null;
        }

        try
        {
            string jsonContent = await File.ReadAllTextAsync(packageJsonPath);
            GithubPackageMetaData? package = JsonSerializer.Deserialize<GithubPackageMetaData>(jsonContent);

            if (package == null)
            {
                Debug.LogError($"Failed to deserialize package.json at {packageJsonPath}");
                return null;
            }

            return package;
        }
        catch (JsonException ex)
        {
            Debug.LogError($"Invalid JSON in package.json at {packageJsonPath}: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Error loading package.json at {packageJsonPath}: {ex.Message}");
            return null;
        }
    }

    private static bool IsValidDependencyFormat(string repo, string version)
    {
        if (string.IsNullOrWhiteSpace(repo) || string.IsNullOrWhiteSpace(version))
            return false;

        // Validate repo format (should be "owner/repo")
        if (!repo.Contains('/') || repo.Count(c => c == '/') != 1)
            return false;

        // Validate version format (should be valid semantic version or range)
        try
        {
            _ = new SemVersion.Parser.Range(version); // Throws if invalid
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<SemanticVersion?> GetInstalledVersion(string githubRepo)
    {
        ValidateIsReady();

        githubRepo = ConvertToPath(githubRepo) ?? throw new Exception("Invalid GitHub repository path");
        string fileSafeName = githubRepo.Replace('/', '.');
        string packageDir = Path.Combine(Project.Active!.PackagesDirectory.FullName, fileSafeName);

        try
        {
            string result = await ExecuteGitCommand($"-C \"{packageDir}\" describe --tags");
            return string.IsNullOrWhiteSpace(result) ? null : SemanticVersion.Parse(result.Trim());
        }
        catch (Exception)
        {
            // If no tags are found or other error
            return null;
        }
    }

    /// <param name="githubRepo">The GitHub repository path, ex: 'ProwlEngine/Prowl', 'username/repository'</param>
    public static async Task<GithubPackageMetaData> GetPackageDetails(string githubRepo)
    {
        githubRepo = ConvertToPath(githubRepo) ?? throw new Exception("Invalid GitHub repository path");
        string fileSafeName = githubRepo.Replace('/', '.');
        string packageJsonPath = Path.Combine(Project.Active!.PackagesDirectory.FullName, fileSafeName, "package.json");
        if (File.Exists(packageJsonPath))
        {
            return await LoadPackageJson(packageJsonPath) ?? throw new Exception("Failed to load package.json");
        }

        // Convert git URL to raw GitHub URL
        string rawUrl = $"https://raw.githubusercontent.com/{githubRepo}/refs/heads/master/package.json";

        try
        {
            string response = await s_httpClient.GetStringAsync(rawUrl);
            return JsonSerializer.Deserialize<GithubPackageMetaData>(response);
        }
        catch (HttpRequestException ex)
        {
            throw new Exception($"Failed to fetch package.json from {rawUrl}", ex);
        }
    }

    /// <param name="githubRepo">The GitHub repository path, ex: 'ProwlEngine/Prowl', 'username/repository'</param>
    public static async Task<List<SemanticVersion>> GetPackageVersions(string githubRepo)
    {
        githubRepo = ConvertToPath(githubRepo) ?? throw new Exception("Invalid GitHub repository path");

        string url = $"https://github.com/{githubRepo}.git";
        string result = await ExecuteGitCommand($"ls-remote --tags {url}");
        var versions = new List<SemanticVersion>();

        foreach (string line in result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // Extract tag name from refs/tags/
            string[] parts = line.Split("refs/tags/");
            if (parts.Length == 2)
            {
                // Remove ^{} suffix that some tags have
                string tag = parts[1].Replace("^{}", "");
                var version = SemanticVersion.Parse(tag);
                if (!versions.Contains(version))
                    versions.Add(version);
            }
        }

        return versions;
    }

    private static SemanticVersion? FindBestVersion(List<SemanticVersion> semanticVersions, string versionRange)
    {
        try
        {
            var range = new SemVersion.Parser.Range(versionRange);

            // Filter versions that match the range and order by version (descending)
            return semanticVersions
                .Where(v => range.IsSatisfied(v))
                .OrderByDescending(v => v)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to evaluate version range '{versionRange}': {ex.Message}");
        }
    }

    private static string? ConvertToPath(string githubRepo)
    {
        // They may pass in the full URL, so we need to convert it to just the Username/Repository format

        // Handle null or empty
        if (string.IsNullOrWhiteSpace(githubRepo))
            return null;

        // if it has .git at the end, remove it
        if (githubRepo.EndsWith(".git"))
            githubRepo = githubRepo.Substring(0, githubRepo.Length - 4);

        // Already in correct format (Username/Repository)
        if (!githubRepo.Contains("github.com") && githubRepo.Count(c => c == '/') == 1)
            return githubRepo;

        // Handle different URL formats
        if (githubRepo.Contains("github.com"))
        {
            // Handle SSH format: git@github.com:Username/Repository
            if (githubRepo.StartsWith("git@"))
            {
                return githubRepo.Split(':')[1];
            }

            // Handle HTTPS format: https://github.com/Username/Repository
            var parts = githubRepo.Split("github.com/");
            if (parts.Length == 2)
            {
                return parts[1];
            }
        }

        return null;
    }

    private static async Task<string> ExecuteGitCommand(string arguments)
    {
        using var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        process.Start();
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new Exception($"Git command failed: {error}");
        }

        return output;
    }

    #endregion

}
