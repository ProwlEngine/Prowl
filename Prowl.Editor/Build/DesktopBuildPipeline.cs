using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Prowl.Runtime;

namespace Prowl.Editor.Build;

/// <summary>
/// Build pipeline for Windows, Linux, and macOS desktop targets.
/// </summary>
public class DesktopBuildPipeline : BuildPipeline
{
    public override string DisplayName => "Desktop";
    public override string[] SupportedRuntimeIdentifiers => ["win-x64", "linux-x64", "osx-x64", "osx-arm64"];

    public override BuildResult Build(BuildSettings settings, Action<string, float> progress)
    {
        var sw = Stopwatch.StartNew();
        var log = new StringBuilder();
        var errors = new StringBuilder();

        var project = Project.Current;
        if (project == null)
            return new BuildResult { Success = false, Errors = "No project open." };

        try
        {
            // 0. Compile user scripts if needed
            progress("Compiling scripts...", 0.0f);
            var (gameScripts, _) = Scripting.ScriptCompiler.ClassifyScripts(project);
            if (gameScripts.Count > 0)
            {
                var compileResult = Scripting.ScriptCompiler.CompileAll(project);
                if (!compileResult.Success)
                    return new BuildResult { Success = false, Errors = $"Script compilation failed:\n{compileResult.Errors}" };
                log.AppendLine("Scripts compiled successfully.");
            }

            // 1. Validate
            progress("Validating...", 0.05f);
            var enabledScenes = settings.Scenes.Where(s => s.Enabled && s.SceneGuid != Guid.Empty).ToList();
            if (enabledScenes.Count == 0)
            {
                return new BuildResult { Success = false, Errors = "No scenes in build. Add at least one scene." };
            }

            string outputDir = Path.IsPathRooted(settings.OutputDirectory)
                ? settings.OutputDirectory
                : Path.Combine(project.RootPath, settings.OutputDirectory);
            Directory.CreateDirectory(outputDir);

            string contentDir = Path.Combine(outputDir, "Content");
            string settingsDir = Path.Combine(contentDir, "Settings");

            // Clean previous build output
            if (Directory.Exists(contentDir))
                try { Directory.Delete(contentDir, true); } catch { }

            // 1b. Always save current scene before building (build uses the cache which comes from the .scene file)
            if (EditorSceneManager.CurrentScenePath != null)
            {
                EditorSceneManager.Save();
                log.AppendLine("Auto-saved current scene.");
            }
            else
            {
                Runtime.Debug.LogWarning("[Build] Current scene has no save path. Save it first for accurate build.");
            }

            // 1c. Reimport all build scenes to ensure caches are fresh
            var db = EditorAssetDatabase.Instance;
            foreach (var sceneEntry in enabledScenes)
            {
                if (db != null && sceneEntry.SceneGuid != Guid.Empty)
                    db.Reimport(sceneEntry.SceneGuid);
            }

            // 2. Collect assets and ensure all caches exist
            var collection = CollectAssets(settings, progress);
            log.AppendLine($"Collected {collection.AllAssets.Count} assets, {collection.ResourcesMap.Count} resources.");

            // Reimport any collected assets missing their cache file
            progress("Verifying asset caches...", 0.15f);
            int reimported = 0;
            foreach (var guid in collection.AllAssets)
            {
                string cachePath = Path.Combine(project.CachePath, $"{guid}.asset");
                if (!File.Exists(cachePath))
                {
                    db?.Reimport(guid);
                    reimported++;
                }
            }
            if (reimported > 0)
                log.AppendLine($"Reimported {reimported} assets with missing caches.");

            // 3. Clean and generate player source
            progress("Generating player...", 0.3f);
            string buildTempDir = project.BuildTempPath;
            if (Directory.Exists(buildTempDir))
                try { Directory.Delete(buildTempDir, true); } catch { }
            Directory.CreateDirectory(buildTempDir);

            Guid defaultScene = enabledScenes[0].SceneGuid;

            // For embedded mode, copy assets to temp dir before csproj generation
            // so they can be included as EmbeddedResource
            List<string>? embeddedAssetPaths = null;
            if (settings.PackagingMode == AssetPackagingMode.Embedded)
            {
                progress("Preparing embedded assets...", 0.35f);
                string embeddedDir = Path.Combine(buildTempDir, "Assets");
                Directory.CreateDirectory(embeddedDir);
                CopyLooseAssets(collection.AllAssets, embeddedDir, progress);
                GenerateManifest(Path.Combine(embeddedDir, "asset_manifest.bin"),
                    collection.AllAssets, collection.ResourcesMap, defaultScene);

                // Collect paths for csproj
                embeddedAssetPaths = new();
                foreach (var f in Directory.EnumerateFiles(embeddedDir, "*.*", SearchOption.AllDirectories))
                    embeddedAssetPaths.Add(f);
            }

            GeneratePlayerSource(project, settings, defaultScene, buildTempDir);
            GeneratePlayerCsproj(project, settings, buildTempDir, embeddedAssetPaths);
            log.AppendLine("Generated player source and project.");

            // 4. Compile

            progress("Compiling player...", 0.7f);
            string csprojPath = Path.Combine(buildTempDir, $"{project.Name}.Player.csproj");
            var (exitCode, stdout, stderr) = RunDotnetPublish(
                csprojPath,
                settings.Config.ToString(),
                settings.RuntimeIdentifier,
                settings.SelfContained,
                outputDir);

            log.AppendLine(stdout);
            if (!string.IsNullOrEmpty(stderr))
                errors.AppendLine(stderr);

            if (exitCode != 0)
            {
                Scripting.ScriptCompiler.LogBuildOutput(stdout, stderr);
                return new BuildResult
                {
                    Success = false,
                    OutputPath = outputDir,
                    Log = log.ToString(),
                    Errors = errors.ToString(),
                    Duration = sw.Elapsed,
                    AssetCount = 0
                };
            }

            // 4b. Copy pre-compiled game scripts assembly
            if (File.Exists(project.GameAssemblyPath))
            {
                File.Copy(project.GameAssemblyPath, Path.Combine(outputDir, Path.GetFileName(project.GameAssemblyPath)), true);
                log.AppendLine($"Copied game assembly: {Path.GetFileName(project.GameAssemblyPath)}");
            }

            // 5. Package assets AFTER publish (publish may clean the output dir)
            // For embedded mode, assets were already baked into the assembly at compile time
            progress("Packaging assets...", 0.8f);
            int assetCount = collection.AllAssets.Count;
            if (settings.PackagingMode != AssetPackagingMode.Embedded)
            {
                Directory.CreateDirectory(contentDir);
                switch (settings.PackagingMode)
                {
                    case AssetPackagingMode.ProwlPak:
                        assetCount = PackAssets(collection.AllAssets, contentDir, settings.MaxPakSizeMB, progress);
                        break;
                    case AssetPackagingMode.LooseFiles:
                        assetCount = CopyLooseAssets(collection.AllAssets, contentDir, progress);
                        break;
                }
                GenerateManifest(Path.Combine(contentDir, "asset_manifest.bin"),
                    collection.AllAssets, collection.ResourcesMap, defaultScene);
            }
            log.AppendLine($"Packaged {assetCount} assets ({settings.PackagingMode}).");

            // 6. Export settings
            ExportSettings(settingsDir, progress);
            log.AppendLine("Exported project settings.");

            // 7. Copy engine's native runtimes (miniaudioex etc.) that aren't from NuGet
            progress("Copying native libraries...", 0.9f);
            string engineDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            string engineRuntimes = Path.Combine(engineDir, "runtimes");
            if (Directory.Exists(engineRuntimes))
            {
                foreach (var file in Directory.EnumerateFiles(engineRuntimes, "*.*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(engineRuntimes, file);
                    string dest = Path.Combine(outputDir, "runtimes", relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    if (!File.Exists(dest))
                        File.Copy(file, dest, true);
                }
            }

            // 8. Clean temp
            if (Directory.Exists(buildTempDir))
                try { Directory.Delete(buildTempDir, true); } catch { }

            // Done
            progress("Build complete!", 1.0f);
            sw.Stop();

            Runtime.Debug.Log($"[Build] Desktop build completed in {sw.Elapsed.TotalSeconds:F1}s → {outputDir}");

            return new BuildResult
            {
                Success = true,
                OutputPath = outputDir,
                Log = log.ToString(),
                Duration = sw.Elapsed,
                AssetCount = assetCount
            };
        }
        catch (Exception ex)
        {
            return new BuildResult
            {
                Success = false,
                Errors = ex.ToString(),
                Duration = sw.Elapsed
            };
        }
    }

    private void GeneratePlayerSource(Project project, BuildSettings settings, Guid defaultSceneGuid, string outputDir)
    {
        string productName = "Prowl Game";
        try { productName = ProjectSettingsRegistry.Get<GeneralSettings>().ProductName; } catch { }

        // Program.cs
        File.WriteAllText(Path.Combine(outputDir, "Program.cs"), $$"""
            using System;
            using System.IO;
            using System.Reflection;
            using System.Runtime.InteropServices;
            using Prowl.Runtime;

            // Register a native library resolver that probes runtimes/{rid}/native/ next to exe
            NativeLibrary.SetDllImportResolver(typeof(Prowl.Runtime.Game).Assembly, (name, asm, paths) =>
            {
                string baseDir = AppContext.BaseDirectory;
                string rid = RuntimeInformation.RuntimeIdentifier;
                string[] rids = [rid];
                if (rid.Contains('-')) rids = [rid, rid[..rid.IndexOf('-')]];

                string[] exts = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? [".dll", ""]
                    : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? [".dylib", ""]
                    : [".so", ".so.1", ""];

                foreach (var r in rids)
                {
                    foreach (var ext in exts)
                    {
                        string path = Path.Combine(baseDir, "runtimes", r, "native", name + ext);
                        if (File.Exists(path) && NativeLibrary.TryLoad(path, out var handle))
                            return handle;
                    }
                }
                return IntPtr.Zero;
            });

            new DesktopPlayer().Run("{{productName}}", {{settings.WindowWidth}}, {{settings.WindowHeight}});

            class DesktopPlayer : Game
            {
                public override void Initialize()
                {
                    Application.IsPlaying = true;
                    Application.IsEditor = false;
                    Application.DataPath = System.AppContext.BaseDirectory;

                    // Load user game scripts assembly
                    string gameAssembly = Path.Combine(Application.DataPath, "{{project.Name}}.Game.dll");
                    if (File.Exists(gameAssembly))
                        Assembly.LoadFrom(gameAssembly);

                    // Initialize asset database
                    var db = new PlayerAssetDatabase(AssetPackagingMode.{{settings.PackagingMode}}, "Content");
                    AssetDatabase.Current = db;
                    GameResources.Initialize(db.ResourcesMap);

                    // Load default scene
                    var scene = db.LoadScene(Guid.Parse("{{defaultSceneGuid}}"));
                    if (scene != null)
                        Prowl.Runtime.Resources.Scene.Load(scene);
                    else
                        Debug.LogError("Failed to load default scene.");

                    // Apply project settings (physics needs scene loaded first)
                    PlayerSettingsLoader.Apply(Path.Combine(Application.DataPath, "Content", "Settings"));
                }

                public override void OnUpdate(Prowl.Runtime.Resources.Scene? scene) => scene?.Update();
                public override void OnRender(Prowl.Runtime.Resources.Scene? scene) => scene?.Render();
                public override void OnGui(Prowl.Runtime.Resources.Scene? scene, Prowl.PaperUI.Paper paper) => scene?.OnGui(paper);
            }
            """);
    }

    private void GeneratePlayerCsproj(Project project, BuildSettings settings, string outputDir, List<string>? embeddedAssetPaths = null)
    {
        string engineDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        string runtimeDll = Path.Combine(engineDir, "Prowl.Runtime.dll");
        string versionDefine = Scripting.ScriptCompiler.GetVersionDefine();

        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine("    <OutputType>Exe</OutputType>");
        sb.AppendLine("    <TargetFramework>net10.0</TargetFramework>");
        sb.AppendLine("    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>");
        sb.AppendLine("    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>");
        sb.AppendLine("    <Nullable>enable</Nullable>");
        sb.AppendLine($"    <AssemblyName>{project.Name}</AssemblyName>");
        sb.AppendLine($"    <DefineConstants>PROWL;{versionDefine}</DefineConstants>"); // NO PROWL_EDITOR
        sb.AppendLine($"    <RuntimeIdentifier>{settings.RuntimeIdentifier}</RuntimeIdentifier>");
        if (settings.SelfContained)
            sb.AppendLine("    <SelfContained>true</SelfContained>");
        sb.AppendLine("  </PropertyGroup>");

        // Reference Prowl.Runtime Private=true forces fresh copy from HintPath
        sb.AppendLine("  <ItemGroup>");
        sb.AppendLine($"    <Reference Include=\"Prowl.Runtime\">");
        sb.AppendLine($"      <HintPath>{runtimeDll}</HintPath>");
        sb.AppendLine("      <Private>true</Private>");
        sb.AppendLine("      <SpecificVersion>false</SpecificVersion>");
        sb.AppendLine("    </Reference>");
        sb.AppendLine("  </ItemGroup>");

        // NuGet packages must use PackageReference for native deps (Silk.NET GLFW, OpenAL, etc.)
        sb.AppendLine("  <ItemGroup>");
        sb.AppendLine("    <PackageReference Include=\"Jitter2\" Version=\"2.8.3\" />");
        sb.AppendLine("    <PackageReference Include=\"Magick.NET-Q16-AnyCPU\" Version=\"14.11.1\" />");
        sb.AppendLine("    <PackageReference Include=\"Prowl.Echo\" Version=\"2.1.2\" />");
        sb.AppendLine("    <PackageReference Include=\"Prowl.Paper\" Version=\"1.5.1\" />");
        sb.AppendLine("    <PackageReference Include=\"Silk.NET\" Version=\"2.22.0\" />");
        sb.AppendLine("    <PackageReference Include=\"Silk.NET.OpenAL.Soft.Native\" Version=\"1.23.1\" />");
        sb.AppendLine("  </ItemGroup>");

        // User NuGet packages from ProjectSettings/Packages.json
        Scripting.ScriptCompiler.AppendNuGetPackages(sb, project);


        // Compile items just the generated Program.cs (user scripts are a separate pre-compiled DLL)
        sb.AppendLine("  <ItemGroup>");
        sb.AppendLine($"    <Compile Include=\"Program.cs\" />");
        sb.AppendLine("  </ItemGroup>");

        // Embedded assets (for Embedded packaging mode)
        if (embeddedAssetPaths != null && embeddedAssetPaths.Count > 0)
        {
            sb.AppendLine("  <ItemGroup>");
            foreach (var assetPath in embeddedAssetPaths)
            {
                string rel = Path.GetRelativePath(outputDir, assetPath).Replace('\\', '/');
                // LogicalName determines the resource name used by Assembly.GetManifestResourceStream
                string logicalName = "Assets." + Path.GetFileName(assetPath);
                if (assetPath.EndsWith("asset_manifest.bin"))
                    logicalName = "Assets._manifest.bin";

                sb.AppendLine($"    <EmbeddedResource Include=\"{rel}\">");
                sb.AppendLine($"      <LogicalName>{logicalName}</LogicalName>");
                sb.AppendLine("    </EmbeddedResource>");
            }
            sb.AppendLine("  </ItemGroup>");
        }

        sb.AppendLine("</Project>");

        File.WriteAllText(Path.Combine(outputDir, $"{project.Name}.Player.csproj"), sb.ToString());
    }
}
