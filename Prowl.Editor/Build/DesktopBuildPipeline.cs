using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Runtime;

namespace Prowl.Editor.Build;

/// <summary>
/// Build pipeline for Windows, Linux, and macOS desktop targets.
/// </summary>
public class DesktopBuildPipeline : BuildPipeline
{
    public override string DisplayName => "Desktop";
    public override string[] SupportedRuntimeIdentifiers => ["win-x64", "linux-x64", "osx-x64", "osx-arm64"];

    public override BuildResult Build(BuildSettings settings, BuildProgress? progress)
    {
        return BuildAsync(Project.Current!.RootPath, settings, settings.OutputDirectory).GetAwaiter().GetResult();
    }


    public override async Task<BuildResult> BuildAsync(
        string projectPath,
        BuildSettings settings,
        string? outputDirectory = null,
        BuildProgress? progress = null,
        CancellationToken cancellation = default)
    {
        var sw = Stopwatch.StartNew();

        var project = Project.Current;
        if (project == null)
            return new BuildResult { Success = false, Errors = "No project open." };

        try
        {
            // 0. Compile user scripts if needed
            progress?.Log("Compiling scripts...");
            var (gameScripts, _) = Scripting.ScriptCompiler.ClassifyScripts(project);
            if (gameScripts.Count > 0)
            {
                var compileResult = Scripting.ScriptCompiler.CompileAll(project);
                if (!compileResult.Success)
                    return new BuildResult { Success = false, Errors = $"Script compilation failed:\n{compileResult.Errors}" };
                progress?.Log("Scripts compiled successfully.");
            }

            // 1. Validate
            progress?.Log("Validating project...", 0.05f);
            var enabledScenes = settings.Scenes.Where(s => s.Enabled && s.SceneGuid != Guid.Empty).ToList();
            if (enabledScenes.Count == 0)
            {
                return new BuildResult { Success = false, Errors = "No scenes in build. Add at least one scene." };
            }

            DesktopBuildProfile desktopProfile = settings.GetProfile<DesktopBuildProfile>(this.GetType());

            outputDirectory = Path.IsPathRooted(settings.OutputDirectory)
                ? settings.OutputDirectory
                : Path.Combine(project.RootPath, settings.OutputDirectory);

            // Clean destination so previous build artefacts don't linger
            if (Directory.Exists(outputDirectory))
                Directory.Delete(outputDirectory, recursive: true);
            Directory.CreateDirectory(outputDirectory);



            string contentDir = Path.Combine(outputDirectory, "Content");
            string settingsDir = Path.Combine(contentDir, "Settings");

            // Clean previous build output
            if (Directory.Exists(contentDir))
                try { Directory.Delete(contentDir, true); } catch { }

            // 1b. Always save current scene before building (build uses the cache which comes from the .scene file)
            if (EditorSceneManager.CurrentScenePath != null)
            {
                EditorSceneManager.Save();
                progress?.Log("Auto-saved current scene.");
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

            progress?.Log("Start collecting assets...");

            // 2. Collect assets and ensure all caches exist
            var collection = CollectAssets(settings, progress);
            progress?.Log($"Collected {collection.AllAssets.Count} assets, {collection.ResourcesMap.Count} resources.");

            // Reimport any collected assets missing their cache file
            progress?.Log("Verifying asset caches...", 0.15f);
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
                progress?.Log($"Reimported {reimported} assets with missing caches.");

            // 3. Clean and generate player source
            progress?.Log("Generating player...", 0.3f);
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
                progress?.Log("Preparing embedded assets...", 0.35f);
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

            GeneratePlayerSource(project, settings, desktopProfile, defaultScene, buildTempDir);
            GeneratePlayerCsproj(project, settings, desktopProfile, buildTempDir, embeddedAssetPaths);
            progress?.Log("Generated player source and project.");

            // 4. Compile

            progress?.Log("Compiling player...", 0.7f);

            string csprojPath = Path.Combine(buildTempDir, $"{project.Name}.Player.csproj");

            var args = new StringBuilder();
            args.Append($"publish \"{csprojPath}\"");
            args.Append($" -c {settings.Config.ToString()}");
            args.Append($" -r {desktopProfile.RuntimeIdentifier}");
            args.Append($" -o \"{outputDirectory}\"");
            args.Append($" --self-contained {desktopProfile.SelfContained.ToString().ToLowerInvariant()}");

            var (exitCode, stdout, stderr) = await RunDotnetAsync(args.ToString(),
                progress,
                cancellation).ConfigureAwait(false);

            progress?.Log(stdout);
            if (!string.IsNullOrEmpty(stderr))
                progress?.Log(stderr);

            if (exitCode != 0)
            {
                Scripting.ScriptCompiler.LogBuildOutput(stdout, stderr);
                return new BuildResult
                {
                    Success = false,
                    OutputPath = outputDirectory,
                    Log = progress?.ToString(LogSeverity.Normal),
                    Errors = progress?.ToString(LogSeverity.Error),
                    Duration = sw.Elapsed,
                    AssetCount = 0
                };
            }

            // 4b. Copy pre-compiled game scripts assembly
            if (File.Exists(project.GameAssemblyPath))
            {
                File.Copy(project.GameAssemblyPath, Path.Combine(outputDirectory, Path.GetFileName(project.GameAssemblyPath)), true);
                progress?.Log($"Copied game assembly: {Path.GetFileName(project.GameAssemblyPath)}");
            }

            // 4c. Organize assemblies — move dependency DLLs to runtimes/ subfolder
            OrganizePublishOutput(outputDirectory, project.Name);

            // 5. Package assets AFTER publish (publish may clean the output dir)
            // For embedded mode, assets were already baked into the assembly at compile time
            progress?.Log("Packaging assets...", 0.8f);
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
            progress?.Log($"Packaged {assetCount} assets ({settings.PackagingMode}).");

            // 6. Export settings
            ExportSettings(settingsDir, progress);
            progress?.Log("Exported project settings.");

            // 7. Copy engine-custom native libraries (e.g. miniaudioex) that aren't provided by NuGet.
            //    NuGet-provided natives (glfw3, soft_oal, Magick.Native) are already handled by dotnet publish.
            progress?.Log("Copying native libraries...", 0.9f);
            string engineDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
            string engineRuntimes = Path.Combine(engineDir, "runtimes");
            if (Directory.Exists(engineRuntimes))
            {
                foreach (var file in Directory.EnumerateFiles(engineRuntimes, "*.*", SearchOption.AllDirectories))
                {
                    //if (!IsEngineNativeLib(Path.GetFileName(file))) continue;

                    string relative = Path.GetRelativePath(engineRuntimes, file);
                    string dest = Path.Combine(outputDirectory, "runtimes", relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    File.Copy(file, dest, true);
                }
            }

            // 8. Clean temp
            if (Directory.Exists(buildTempDir))
                try { Directory.Delete(buildTempDir, true); } catch { }

            // Done
            progress?.Log("Build complete!", 1.0f);
            sw.Stop();

            Runtime.Debug.Log($"[Build] Desktop build completed in {sw.Elapsed.TotalSeconds:F1}s → {outputDirectory}");

            return new BuildResult
            {
                Success = true,
                OutputPath = outputDirectory,
                Log = progress?.ToString(),
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

    private void GeneratePlayerSource(Project project, BuildSettings settings, DesktopBuildProfile desktopProfile, Guid defaultSceneGuid, string outputDir)
    {
        string productName = "Prowl Game";
        try { productName = ProjectSettingsRegistry.Get<GeneralSettings>().ProductName; } catch { }

        // Program.cs
        File.WriteAllText(Path.Combine(outputDir, "Program.cs"), $$"""
            using System;
            using System.IO;
            using System.Linq;
            using System.Collections;
            using System.Collections.Generic;
            using System.Reflection;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;
            using System.Runtime.Loader;

            new DesktopPlayer().Run("{{productName}}", {{desktopProfile.WindowWidth}}, {{desktopProfile.WindowHeight}});

            internal static class ModuleInitializer
            {
                [ModuleInitializer]
                public static void Initialize()
                {
                    // 1. Managed assembly resolver
                    AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
                    {
                        string dllName = assemblyName.Name + ".dll";
                        string probePath = Path.Combine(AppContext.BaseDirectory, "runtimes", dllName);

                        // Log to file or debug output (Console may not be ready yet)
                        Log($"[Resolver] Probing: {probePath}");

                        if (File.Exists(probePath))
                        {
                            Log($"[Resolver] Found! Loading {probePath}");
                            return context.LoadFromAssemblyPath(probePath);
                        }
                            Log($"[Resolver] Not Found {probePath}!");
                        return null;
                    };

                    // 2. Native resolver (optional, but register early as well)
                    NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), ResolveNativeLibrary);
                    //NativeLibrary.SetDllImportResolver(typeof(Prowl.Runtime.Game).Assembly, ResolveNativeLibrary);

                    AssemblyLoadContext.Default.ResolvingUnmanagedDll += (assembly, libraryName) =>
                    {
                        Log($"[Native] Global resolver: {libraryName} requested by {assembly?.GetName()?.Name ?? "unknown"}");

                        string baseDir = AppContext.BaseDirectory;
                        string rid = RuntimeInformation.RuntimeIdentifier;

                        // Build RID fallback chain
                        List<string> rids = new();
                        if (!string.IsNullOrEmpty(rid))
                        {
                            rids.Add(rid);
                            int dash = rid.IndexOf('-');
                            while (dash > 0)
                            {
                                rid = rid.Substring(0, dash);
                                rids.Add(rid);
                                dash = rid.IndexOf('-');
                            }
                        }
                        rids.Add("any");
                        rids.Add("");

                        string[] exts = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                            ? new[] { ".dll", "" }
                            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                                ? new[] { ".dylib", "" }
                                : new[] { ".so", ".so.1", "" };

                        string[] nameVariants = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                            ? new[] { libraryName }
                            : new[] { libraryName, "lib" + libraryName };

                        foreach (var r in rids)
                        {
                            string nativeSubDir = string.IsNullOrEmpty(r)
                                ? Path.Combine("runtimes", "native")
                                : Path.Combine("runtimes", r, "native");

                            foreach (var name in nameVariants)
                            foreach (var ext in exts)
                            {
                                string fullPath = Path.Combine(baseDir, nativeSubDir, name + ext);
                                Log($"[Native] Probing: {fullPath}");

                                if (File.Exists(fullPath))
                                {
                                    if (NativeLibrary.TryLoad(fullPath, out IntPtr handle))
                                    {
                                        Log($"[Native] SUCCESS: {fullPath}");
                                        return handle;
                                    }
                                    else
                                    {
                                        Log($"[Native] TryLoad FAILED (missing dependency?): {fullPath}");
                                    }
                                }
                            }
                        }

                        // Fallback to root directory
                        foreach (var name in nameVariants)
                        foreach (var ext in exts)
                        {
                            string rootPath = Path.Combine(baseDir, name + ext);
                            Log($"[Native] Root probe: {rootPath}");
                            if (File.Exists(rootPath) && NativeLibrary.TryLoad(rootPath, out IntPtr handle))
                            {
                                Log($"[Native] SUCCESS from root: {rootPath}");
                                return handle;
                            }
                        }

                        Log($"[Native] NOT FOUND: {libraryName}");
                        return IntPtr.Zero;
                    };
                }

                public static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
                {
                    Log($"[Native] Resolving: {libraryName} for {assembly.GetName().Name}");

                    string baseDir = AppContext.BaseDirectory;
                    string rid = RuntimeInformation.RuntimeIdentifier;

                    // Build RID fallback chain
                    List<string> rids = new();
                    if (!string.IsNullOrEmpty(rid))
                    {
                        rids.Add(rid);
                        int dash = rid.IndexOf('-');
                        while (dash > 0)
                        {
                            rid = rid.Substring(0, dash);
                            rids.Add(rid);
                            dash = rid.IndexOf('-');
                        }
                    }
                    rids.Add("any");
                    rids.Add("");

                    string[] exts = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? new[] { ".dll", "" }
                        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                            ? new[] { ".dylib", "" }
                            : new[] { ".so", ".so.1", "" };

                    string[] nameVariants = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? new[] { libraryName }
                        : new[] { libraryName, "lib" + libraryName };

                    foreach (var r in rids)
                    {
                        string nativeSubDir = string.IsNullOrEmpty(r)
                            ? Path.Combine("runtimes", "native")
                            : Path.Combine("runtimes", r, "native");

                        foreach (var name in nameVariants)
                        foreach (var ext in exts)
                        {
                            string fullPath = Path.Combine(baseDir, nativeSubDir, name + ext);
                            Log($"[Native] Probing: {fullPath}");

                            if (File.Exists(fullPath))
                            {
                                if (NativeLibrary.TryLoad(fullPath, out IntPtr handle))
                                {
                                    Log($"[Native] SUCCESS: {fullPath}");
                                    return handle;
                                }
                                else
                                {
                                    Log($"[Native] TryLoad FAILED (missing dependency?): {fullPath}");
                                }
                            }
                        }
                    }

                    // Last chance: root directory
                    foreach (var name in nameVariants)
                    foreach (var ext in exts)
                    {
                        string rootPath = Path.Combine(baseDir, name + ext);
                        if (File.Exists(rootPath) && NativeLibrary.TryLoad(rootPath, out IntPtr handle))
                        {
                            Log($"[Native] SUCCESS from root: {rootPath}");
                            return handle;
                        }
                    }

                    Log($"[Native] NOT FOUND: {libraryName}");
                    return IntPtr.Zero;
                }

                private static void Log(string message)
                {
                    // Write to a file since Console may not be available yet.
                    File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "resolver.log"), $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
                }
            }

            class DesktopPlayer : Prowl.Runtime.Game
            {
                public override void Initialize()
                {
                    Prowl.Runtime.Application.IsPlaying = true;
                    Prowl.Runtime.Application.IsEditor = false;
                    Prowl.Runtime.Application.DataPath = System.AppContext.BaseDirectory;

                    NativeLibrary.SetDllImportResolver(typeof(Prowl.Runtime.Game).Assembly, ModuleInitializer.ResolveNativeLibrary);

                    // Load user game scripts assembly
                    string gameAssembly = Path.Combine(Prowl.Runtime.Application.DataPath, "{{project.Name}}.Game.dll");
                    if (File.Exists(gameAssembly))
                        Assembly.LoadFrom(gameAssembly);

                    // Initialize asset database
                    var db = new Prowl.Runtime.PlayerAssetDatabase(Prowl.Runtime.AssetPackagingMode.{{settings.PackagingMode}}, "Content");
                    Prowl.Runtime.AssetDatabase.Current = db;
                    Prowl.Runtime.GameResources.Initialize(db.ResourcesMap);

                    // Load default scene
                    var scene = db.LoadScene(Guid.Parse("{{defaultSceneGuid}}"));
                    if (scene != null)
                        Prowl.Runtime.Resources.Scene.Load(scene);
                    else
                        Prowl.Runtime.Debug.LogError("Failed to load default scene.");

                    // Apply project settings (physics needs scene loaded first)
                    Prowl.Runtime.PlayerSettingsLoader.Apply(Path.Combine(Prowl.Runtime.Application.DataPath, "Content", "Settings"));

                }

                public override void OnUpdate(Prowl.Runtime.Resources.Scene? scene) => scene?.Update();
                public override void OnRender(Prowl.Runtime.Resources.Scene? scene) => scene?.Render();
                public override void OnGui(Prowl.Runtime.Resources.Scene? scene, Prowl.PaperUI.Paper paper) => scene?.OnGui(paper);
            }
            """);
    }

    private void GeneratePlayerCsproj(Project project, BuildSettings settings, DesktopBuildProfile desktopProfile, string outputDir, List<string>? embeddedAssetPaths = null)
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
        sb.AppendLine($"    <DefineConstants>PROWL;{versionDefine};{FinalizeDefineString(settings, this)}</DefineConstants>"); // NO PROWL_EDITOR
        sb.AppendLine($"    <RuntimeIdentifier>{desktopProfile.RuntimeIdentifier}</RuntimeIdentifier>");


        sb.AppendLine("    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>");

        if (desktopProfile.SelfContained)
        {
            sb.AppendLine("    <SelfContained>true</SelfContained>");

        }
        sb.AppendLine("  </PropertyGroup>");

        // PublishTrimmed should still be regarded as experimental.
        // Using Partial as TrimMode should keep the engine from trimming the wrong assemblies,
        // But at this time it's unknown if it breaks anything with different build configurations
        // Forcing TrimmerRootAssembly to Prowl.Runtime prevents unexpected trimming from core assembly as well.
        if (desktopProfile.PublishTrimmed)
        {
            sb.AppendLine("  <PropertyGroup>");
            sb.AppendLine("    <PublishTrimmed>true</PublishTrimmed>");
            sb.AppendLine("    <TrimMode>partial</TrimMode>");
            sb.AppendLine("  </PropertyGroup>");
            sb.AppendLine("  <ItemGroup>");
            sb.AppendLine("     <TrimmerRootAssembly Include=\"Prowl.Runtime\" />");
            sb.AppendLine("  </ItemGroup>");
        }

        // Reference Prowl.Runtime Private=true forces fresh copy from HintPath
        sb.AppendLine("  <ItemGroup>");
        sb.AppendLine($"    <Reference Include=\"Prowl.Runtime\">");
        sb.AppendLine($"      <HintPath>{runtimeDll}</HintPath>");
        sb.AppendLine("      <Private>true</Private>");
        sb.AppendLine("      <SpecificVersion>false</SpecificVersion>");
        sb.AppendLine("    </Reference>");
        sb.AppendLine("  </ItemGroup>");

        ListDependencies(sb);

        // User NuGet packages from ProjectSettings/Packages.json. Desktop builds bundle the
        // runtime / non-editor packages (EditorOnly packages live only inside the editor).
        Scripting.ScriptCompiler.AppendNuGetPackages(sb, project, isEditorAssembly: false);


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

    public void ListDependencies(StringBuilder sb)
    {
        // Read PackageReferences from assembly metadata embedded by the MSBuild
        // EmbedPackageReferences target in Prowl.Runtime.csproj. This works regardless
        // of whether the source tree is present — the data lives in the compiled DLL.
        var packages = GetRuntimePackageReferences();

        sb.AppendLine("  <ItemGroup>");
        foreach (var (name, version) in packages)
            sb.AppendLine($"    <PackageReference Include=\"{name}\" Version=\"{version}\" />");
        sb.AppendLine("  </ItemGroup>");
    }

    /// <summary>
    /// Reads PackageReference metadata stamped into the Prowl.Runtime assembly
    /// by the EmbedPackageReferences MSBuild target. Each entry is an
    /// AssemblyMetadataAttribute with Key = "PackageReference:{Name}" and Value = version.
    /// Filters out SDK-implicit packages (e.g. Microsoft.NET.ILLink.Tasks).
    /// </summary>
    private static List<(string Name, string Version)> GetRuntimePackageReferences()
    {
        var result = new List<(string, string)>();
        var runtimeAssembly = typeof(Prowl.Runtime.EngineObject).Assembly;
        const string prefix = "PackageReference:";

        foreach (var attr in runtimeAssembly.GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>())
        {
            if (attr.Key == null || !attr.Key.StartsWith(prefix) || string.IsNullOrEmpty(attr.Value))
                continue;

            string packageName = attr.Key.Substring(prefix.Length);

            // Skip SDK-implicit packages that aren't real dependencies
            if (packageName.StartsWith("Microsoft.NET.", StringComparison.OrdinalIgnoreCase))
                continue;

            result.Add((packageName, attr.Value));
        }

        return result;
    }

    public override string GetExecutablePath(string outputPath, BuildSettings settings)
    {
        var profile = settings.GetProfile<DesktopBuildProfile>(GetType());
        string exe = Path.Combine(outputPath,
            Project.Current!.Name + (profile.Platform == BuildTarget.Windows ? ".exe" : ""));
        return exe;
    }

    /// <summary>
    /// Moves DLLs from the publish root into a runtimes/ subfolder,
    /// keeping only the player executable, game assembly, and core runtime in the root.
    /// </summary>
    private static void OrganizePublishOutput(string outputDir, string projectName)
    {
        string libsDir = Path.Combine(outputDir, "runtimes");
        Directory.CreateDirectory(libsDir);

        // Files that must remain in the root for the app to start
        var keepInRoot = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"{projectName}.dll",
            $"{projectName}.exe",
            $"{projectName}.pdb",
            $"{projectName}.Game.dll",
            $"{projectName}.Game.pdb",
            "System.Private.CoreLib.dll",
            "System.Runtime.dll",
            "System.Runtime.InteropServices.dll",
            "System.Runtime.Loader.dll",
            "System.Runtime.dll",
            "Prowl.Runtime.dll",
            "Prowl.Runtime.pdb",
        };

        foreach (var file in Directory.GetFiles(outputDir, "*.dll"))
        {
            string fileName = Path.GetFileName(file);
            if (keepInRoot.Contains(fileName)) continue;

            // Skip native (unmanaged) DLLs — only move managed assemblies
            try { AssemblyName.GetAssemblyName(file); }
            catch (BadImageFormatException) { continue; }



            string dest = Path.Combine(libsDir, fileName);
            //Runtime.Debug.Log($"WillMove to: {dest}");
            File.Move(file, dest, true);

            // Also move corresponding PDB if present
            string pdbPath = Path.ChangeExtension(file, ".pdb");
            if (File.Exists(pdbPath))
            {
                string pdbDest = Path.Combine(libsDir, Path.GetFileName(pdbPath));
                File.Move(pdbPath, pdbDest, true);
            }
        }
    }
}
