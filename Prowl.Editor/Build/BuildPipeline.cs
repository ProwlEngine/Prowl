using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Echo;
using Prowl.Editor.AssetsDatabase;
using Prowl.Runtime;

namespace Prowl.Editor.Build;

/// <summary>
/// Abstract base for platform-specific build pipelines.
/// Subclass for Desktop, Android, Web, Console, etc.
/// </summary>
public abstract class BuildPipeline
{
    public abstract string DisplayName { get; }
    public abstract string[] SupportedRuntimeIdentifiers { get; }

    /// <summary>Execute the full build. Override for platform-specific steps.</summary>
    public abstract BuildResult Build(BuildSettings settings, BuildProgress? progress);

    /// <summary>
    /// Executes a build with a Task for async status reporting back to the engine.
    /// </summary>
    /// <param name="projectPath">The path of the project to build</param>
    /// <param name="settings">The settings to use for the build</param>
    /// <param name="outputDirectory">The path for the build output. Can be null.</param>
    /// <param name="progress">The <see cref="BuildProgress"/> object that stores the build progress for UI updates. Can be null.</param>
    /// <param name="cancellation">The cancellation token to stop the build midway.</param>
    /// <returns></returns>
    public abstract Task<BuildResult> BuildAsync(
        string projectPath,
        BuildSettings settings,
        string? outputDirectory = null,
        BuildProgress? progress = null,
        CancellationToken cancellation = default);

    // ================================================================
    //  Shared utilities for all pipelines
    // ================================================================

        /// <summary>Collect assets based on build settings.</summary>
    protected AssetCollector.CollectionResult CollectAssets(BuildSettings settings, BuildProgress? progress)
    {
        progress?.Log("Collecting assets...", 0.1f);

        var sceneGuids = settings.Scenes
            .Where(s => s.Enabled)
            .Select(s => s.SceneGuid)
            .Where(g => g != Guid.Empty)
            .ToList();

        bool depsOnly = settings.AssetMode == AssetExportMode.DependenciesOnly;
        return AssetCollector.Collect(sceneGuids, depsOnly);
    }

    /// <summary>Copy binary cache files to output as loose files.</summary>
    protected int CopyLooseAssets(HashSet<Guid> assets, string outputAssetsDir, BuildProgress? progress)
    {
        var project = Project.Current!;
        Directory.CreateDirectory(outputAssetsDir);
        int count = 0;

        foreach (var guid in assets)
        {
            string cachePath = Path.Combine(project.CachePath, $"{guid}.asset");
            if (File.Exists(cachePath))
            {
                string destPath = Path.Combine(outputAssetsDir, $"{guid}.asset");
                File.Copy(cachePath, destPath, true);
                count++;
            }

            if (count % 50 == 0)
                progress?.Log($"Copying assets... ({count}/{assets.Count})", 0.2f + 0.3f * count / assets.Count);
        }

        return count;
    }

    /// <summary>Pack assets into .prowlpak ZipArchive files, auto-splitting at maxSizeMB.</summary>
    protected int PackAssets(HashSet<Guid> assets, string outputAssetsDir, int maxSizeMB, BuildProgress? progress)
    {
        var project = Project.Current!;
        Directory.CreateDirectory(outputAssetsDir);

        // Clean old paks
        foreach (var old in Directory.GetFiles(outputAssetsDir, "*.prowlpak"))
            try { File.Delete(old); } catch { }

        long maxBytes = (long)maxSizeMB * 1024 * 1024;
        int pakIndex = 0;
        long currentSize = 0;
        int count = 0;

        ZipArchive? currentPak = null;

        try
        {
            foreach (var guid in assets)
            {
                string cachePath = Path.Combine(project.CachePath, $"{guid}.asset");
                if (!File.Exists(cachePath)) continue;

                var fileInfo = new FileInfo(cachePath);

                // Start new pak if needed
                if (currentPak == null || currentSize + fileInfo.Length > maxBytes)
                {
                    currentPak?.Dispose();
                    string pakPath = Path.Combine(outputAssetsDir, $"data_{pakIndex}.prowlpak");
                    currentPak = ZipFile.Open(pakPath, ZipArchiveMode.Create);
                    pakIndex++;
                    currentSize = 0;
                }

                // Add to pak
                currentPak.CreateEntryFromFile(cachePath, $"{guid}.asset", CompressionLevel.Optimal);
                currentSize += fileInfo.Length;
                count++;

                if (count % 50 == 0)
                    progress?.Log($"Packing assets... ({count}/{assets.Count})", 0.2f + 0.3f * count / assets.Count);
            }
        }
        finally
        {
            currentPak?.Dispose();
        }

        return count;
    }

    public abstract string GetExecutablePath(string outputPath, BuildSettings settings);

    internal static string FinalizeDefineString(BuildSettings settings, BuildPipeline pipeline)
    {
        var profile = settings.GetProfile(pipeline.GetType());
        var symbols = new List<string>(profile.ScriptingDefineSymbols);

        profile.ModifyDefines(symbols);

        var config = settings.Config;

        // For when profiling will be implemented
        if (config == BuildConfiguration.Debug)
            symbols.Add("PROWL_PROFILING");

        return string.Join(";", symbols.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    /// <summary>Generate the asset manifest as Echo binary.</summary>
    protected void GenerateManifest(string outputPath, HashSet<Guid> assets,
        Dictionary<string, Guid> resourcesMap, Guid defaultSceneGuid)
    {
        var root = EchoObject.NewCompound();
        root["defaultScene"] = new EchoObject(defaultSceneGuid.ToString());

        var assetsTag = EchoObject.NewCompound();
        foreach (var guid in assets)
            assetsTag[guid.ToString()] = new EchoObject($"{guid}.asset");
        root["assets"] = assetsTag;

        var resTag = EchoObject.NewCompound();
        foreach (var (path, guid) in resourcesMap)
            resTag[path] = new EchoObject(guid.ToString());
        root["resources"] = resTag;

        root.WriteToBinary(new FileInfo(outputPath));
    }

    /// <summary>Export only build-relevant project settings as JSON files.</summary>
    protected void ExportSettings(string outputSettingsDir, BuildProgress? progress)
    {
        progress?.Log("Exporting settings...", 0.6f);
        Directory.CreateDirectory(outputSettingsDir);

        var project = Project.Current!;
        if (!Directory.Exists(project.ProjectSettingsPath)) return;

        // Only export settings marked with ExportToBuild = true
        var exportNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ProjectSettingsRegistry.Entries)
            if (entry.ExportToBuild)
                exportNames.Add(entry.Name);

        foreach (var jsonFile in Directory.GetFiles(project.ProjectSettingsPath, "*.json"))
        {
            string name = Path.GetFileNameWithoutExtension(jsonFile);
            if (!exportNames.Contains(name)) continue;

            try
            {
                string destFile = Path.Combine(outputSettingsDir, Path.GetFileName(jsonFile));
                File.Copy(jsonFile, destFile, true);
            }
            catch (Exception ex)
            {
                Runtime.Debug.LogWarning($"[Build] Failed to export setting {jsonFile}: {ex.Message}");
            }
        }
    }

    /// <summary>Run dotnet publish and return result.</summary>
    protected (int exitCode, string stdout, string stderr) RunDotnetPublish(
        string csprojPath, string config, string rid, bool selfContained, string outputDir)
    {
        string args = $"publish \"{csprojPath}\" -c {config} -r {rid} " +
            $"--self-contained {selfContained.ToString().ToLower()} -o \"{outputDir}\"";

        return Scripting.ScriptCompiler.RunDotnetCommand(args, Path.GetDirectoryName(csprojPath)!);
    }

    protected static async Task<(int exitCode, string stdout, string stderr)> RunDotnetAsync(
        string arguments,
        BuildProgress? progress = null,
        CancellationToken cancellation = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start dotnet process.");

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        // Stream output line-by-line so the UI can show live progress
        var stdoutTask = Task.Run(async () =>
        {
            while (await process.StandardOutput.ReadLineAsync(cancellation).ConfigureAwait(false) is { } line)
            {
                stdoutBuilder.AppendLine(line);
                progress?.Log(line, ClassifyDotnetLine(line));
            }
        }, cancellation);

        var stderrTask = Task.Run(async () =>
        {
            while (await process.StandardError.ReadLineAsync(cancellation).ConfigureAwait(false) is { } line)
            {
                stderrBuilder.AppendLine(line);
                progress?.Log(line, Runtime.LogSeverity.Error);
            }
        }, cancellation);

        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        await process.WaitForExitAsync(cancellation).ConfigureAwait(false);

        if (cancellation.IsCancellationRequested && !process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        return (process.ExitCode, stdoutBuilder.ToString(), stderrBuilder.ToString());
    }

    /// <summary>
    /// Classifies a line of dotnet build output by severity.
    /// </summary>
    private static Runtime.LogSeverity ClassifyDotnetLine(string line)
    {
        if (line.Contains(": error ", StringComparison.OrdinalIgnoreCase))
            return Runtime.LogSeverity.Error;
        if (line.Contains(": warning ", StringComparison.OrdinalIgnoreCase))
            return Runtime.LogSeverity.Warning;
        if (line.Contains("Build succeeded", StringComparison.OrdinalIgnoreCase))
            return Runtime.LogSeverity.Success;
        return Runtime.LogSeverity.Normal;
    }
}
