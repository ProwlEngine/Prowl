using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

using Prowl.Echo;
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
    public abstract BuildResult Build(BuildSettings settings, Action<string, float> progress);

    // ================================================================
    //  Shared utilities for all pipelines
    // ================================================================

    /// <summary>Collect assets based on build settings.</summary>
    protected AssetCollector.CollectionResult CollectAssets(BuildSettings settings, Action<string, float> progress)
    {
        progress("Collecting assets...", 0.1f);

        var sceneGuids = settings.Scenes
            .Where(s => s.Enabled)
            .Select(s => s.SceneGuid)
            .Where(g => g != Guid.Empty)
            .ToList();

        bool depsOnly = settings.AssetMode == AssetExportMode.DependenciesOnly;
        return AssetCollector.Collect(sceneGuids, depsOnly);
    }

    /// <summary>Copy binary cache files to output as loose files.</summary>
    protected int CopyLooseAssets(HashSet<Guid> assets, string outputAssetsDir, Action<string, float> progress)
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
                progress($"Copying assets... ({count}/{assets.Count})", 0.2f + 0.3f * count / assets.Count);
        }

        return count;
    }

    /// <summary>Pack assets into .prowlpak ZipArchive files, auto-splitting at maxSizeMB.</summary>
    protected int PackAssets(HashSet<Guid> assets, string outputAssetsDir, int maxSizeMB, Action<string, float> progress)
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
                    progress($"Packing assets... ({count}/{assets.Count})", 0.2f + 0.3f * count / assets.Count);
            }
        }
        finally
        {
            currentPak?.Dispose();
        }

        return count;
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
    protected void ExportSettings(string outputSettingsDir, Action<string, float> progress)
    {
        progress("Exporting settings...", 0.6f);
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
}
