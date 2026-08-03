using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Text;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Projects;
using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Projects.Settings;
using Prowl.Runtime;

using Prowl.Editor.Utils;

namespace Prowl.Editor.Build;

/// <summary>
/// Build pipeline for Windows, Linux, and macOS desktop targets.
/// </summary>
public class DesktopBuildPipeline : BuildPipeline
{
    public override string DisplayName => "Desktop";

    /// <summary>
    /// The player this pipeline ships, from <c>Players/Desktop</c>. Named once because it is referenced
    /// by the generated project, kept out of <c>runtimes/</c> by name, and called into by the generated
    /// entry program, and those three have to agree.
    /// </summary>
    private const string PlayerAssembly = "Prowl.Player.Desktop";

    // Steps that have no counterpart in the shared vocabulary. Stage ids are open precisely so a
    // pipeline can name its own without pushing them into the engine's list.
    private static readonly BuildStage PlanBuild = new("desktop-plan");
    private static readonly BuildStage PrepareEmbedded = new("desktop-prepare-embedded");
    private static readonly BuildStage OrganizeOutput = new("desktop-organize-output");

    // What remains editor bound: the script compiler, the plugin copier and the settings exporter all
    // need the live Project and BuildSettings, and none of that belongs in a portable BuildRequest.
    // Everything else the stages need now travels in the request. Set once, before the executor runs.
    private BuildSettings _settings = null!;
    private Project _project = null!;
    private BuildProgress? _progress;

    /// <summary>
    /// Guards the staged methods against being driven without <see cref="BuildAsync"/> having set the
    /// editor state up. A clear message beats a null reference somewhere inside a stage body.
    /// </summary>
    private void RequireEditorState()
    {
        if (_project == null || _settings == null)
            throw new InvalidOperationException(
                $"{nameof(DesktopBuildPipeline)} must be driven through {nameof(BuildAsync)}, which supplies the editor state its stages need.");
    }

    /// <summary>Paths, profile and assemblies worked out by Validate and read by everything after it.</summary>
    private sealed record DesktopPlan
    {
        public required DesktopBuildProfile Profile { get; init; }
        public required string TargetPlatform { get; init; }
        public required string OutputDirectory { get; init; }
        public required string ContentDir { get; init; }
        public required string SettingsDir { get; init; }
        public required string BuildTempDir { get; init; }
        public required Guid DefaultScene { get; init; }
        public required List<ScriptCompiler.BuildAssembly> Assemblies { get; init; }
    }

    private sealed record CollectedAssets(AssetCollector.CollectionResult Collection);
    private sealed record EmbeddedAssetPaths(List<string> Paths);
    private sealed record CopiedAssemblies(List<string> FileNames);
    private sealed record PackagedAssets(int Count);

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

        _settings = settings;
        _project = project;
        _progress = progress;

        var request = new BuildRequest
        {
            ProjectName = project.Name,
            ProjectRoot = project.RootPath,
            AssetCachePath = project.CachePath,
            TempPath = project.BuildTempPath,
            OutputDirectory = settings.OutputDirectory,
            Scenes = settings.Scenes.Where(s => s.Enabled && s.SceneGuid != Guid.Empty).Select(s => s.SceneGuid).ToList(),
            Configuration = settings.Config,
            Packaging = settings.PackagingMode,
            DependenciesOnly = settings.AssetMode == AssetExportMode.DependenciesOnly,
            MaxPackSizeMB = settings.MaxPakSizeMB,
            Profile = settings.GetOrCreateProfile(GetType()),
        };

        var context = new BuildContext(request, (message, severity) => progress?.Log(message,
            severity switch
            {
                BuildSeverity.Error => LogSeverity.Error,
                BuildSeverity.Warning => LogSeverity.Warning,
                _ => LogSeverity.Normal,
            }));

        var executor = new BuildExecutor(onProgress: (stage, done, total) =>
            progress?.Log($"{stage}: {done}/{total}", total > 0 ? Math.Clamp((float)done / total, 0f, 1f) : 0f));

        BuildOutcome outcome;
        try
        {
            outcome = await executor.RunAsync(this, context, cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Named, because the folder it half filled is the one the user will want to delete, and it
            // is a folder this build created rather than anything of theirs.
            string partial = context.TryGetOutput<DesktopPlan>(out var cancelledPlan) && cancelledPlan != null
                ? cancelledPlan.OutputDirectory
                : "";

            progress?.Log("Build cancelled.", LogSeverity.Warning);
            return new BuildResult { Success = false, Cancelled = true, OutputPath = partial, Duration = sw.Elapsed };
        }
        catch (Exception ex)
        {
            return new BuildResult { Success = false, Errors = ex.ToString(), Duration = sw.Elapsed };
        }

        sw.Stop();

        string resolvedOutput = context.TryGetOutput<DesktopPlan>(out var plan) && plan != null
            ? plan.OutputDirectory
            : settings.OutputDirectory;

        int assetCount = context.TryGetOutput<PackagedAssets>(out var packaged) && packaged != null
            ? packaged.Count
            : 0;

        string errors = string.Join(Environment.NewLine,
            outcome.Issues.Where(i => i.Severity == BuildSeverity.Error).Select(i => i.Message));

        if (outcome.Succeeded)
            Runtime.Debug.Log($"[Build] Desktop build completed in {sw.Elapsed.TotalSeconds:F1}s -> {resolvedOutput}");

        return new BuildResult
        {
            Success = outcome.Succeeded,
            OutputPath = resolvedOutput,
            Log = progress?.ToString() ?? "",
            Errors = errors,
            Duration = sw.Elapsed,
            AssetCount = assetCount,
        };
    }

    // ================================================================
    //  Staged plan
    // ================================================================

    /// <summary>
    /// Near linear, because the real pipeline is. Publish clears the output directory, so packing has to
    /// follow it, while embedding bakes assets into the assembly, so that has to precede generation.
    /// Those two orderings are why the graph is built per request rather than fixed on the pipeline.
    /// </summary>
    public override StageGraph CreateStageGraph(BuildRequest request)
    {
        bool embedded = request.Packaging == AssetPackagingMode.Embedded;

        // Validate is first and cheap on purpose. Everything it checks is instant, and finding out the
        // output directory is unusable after a full script compile is a minute of the user's time.
        var nodes = new List<StageNode>
        {
            new() { Stage = BuildStage.Validate, Resources = StageResources.Exclusive },
            new() { Stage = BuildStage.CompileCode, DependsOn = [BuildStage.Validate], Resources = StageResources.Exclusive },
            new() { Stage = PlanBuild, DependsOn = [BuildStage.CompileCode], Resources = StageResources.Exclusive },
            new() { Stage = BuildStage.ProcessAssets, DependsOn = [PlanBuild] },
        };

        BuildStage beforeGenerate = BuildStage.ProcessAssets;

        if (embedded)
        {
            nodes.Add(new StageNode { Stage = PrepareEmbedded, DependsOn = [BuildStage.ProcessAssets], Resources = StageResources.Exclusive });
            beforeGenerate = PrepareEmbedded;
        }

        nodes.Add(new StageNode { Stage = BuildStage.GeneratePlayer, DependsOn = [beforeGenerate], Resources = StageResources.Exclusive });
        nodes.Add(new StageNode { Stage = BuildStage.CompilePlayer, DependsOn = [BuildStage.GeneratePlayer], Resources = StageResources.Exclusive });
        nodes.Add(new StageNode { Stage = BuildStage.CopyRuntime, DependsOn = [BuildStage.CompilePlayer] });

        // Plugins write into runtimes/ and OrganizeOutput then sweeps the output root, so these three
        // stay a chain. Overlapping them would change which files Organize sees.
        nodes.Add(new StageNode { Stage = BuildStage.CopyPlugins, DependsOn = [BuildStage.CopyRuntime] });
        nodes.Add(new StageNode { Stage = OrganizeOutput, DependsOn = [BuildStage.CopyPlugins], Resources = StageResources.Exclusive });

        // After publish, which clears the output directory the manifest has to sit in.
        nodes.Add(new StageNode { Stage = BuildStage.WriteManifest, DependsOn = [OrganizeOutput] });
        nodes.Add(new StageNode { Stage = BuildStage.PackAssets, DependsOn = [BuildStage.WriteManifest] });
        nodes.Add(new StageNode { Stage = BuildStage.ExportSettings, DependsOn = [BuildStage.PackAssets] });
        nodes.Add(new StageNode { Stage = BuildStage.Finalize, DependsOn = [BuildStage.ExportSettings], Resources = StageResources.Exclusive });

        return new StageGraph(nodes);
    }

    /// <summary>
    /// Each stage is one step of the original method, wrapped so the executor owns ordering, failure
    /// policy and reporting. The bodies still call the same helpers and do the same work in the same
    /// order, which is what keeps this a restructuring rather than a rewrite.
    /// </summary>
    public override async IAsyncEnumerable<BuildOperation> PlanStageAsync(
        BuildStage stage, IBuildContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        RequireEditorState();
        await Task.CompletedTask;

        if (stage == BuildStage.Validate) yield return Step("validate", ValidateRequest);
        else if (stage == BuildStage.CompileCode) yield return Step("compile scripts", CompileScripts);
        else if (stage == PlanBuild) yield return Step("plan build", PlanTheBuild);
        else if (stage == BuildStage.ProcessAssets) yield return Step("collect assets", CollectAndVerify);
        else if (stage == PrepareEmbedded) yield return Step("prepare embedded assets", PrepareEmbeddedAssets);
        else if (stage == BuildStage.GeneratePlayer) yield return Step("generate player", GeneratePlayer);
        else if (stage == BuildStage.CompilePlayer) yield return Step("publish player", PublishPlayer);
        else if (stage == BuildStage.CopyRuntime) yield return Step("copy game assemblies", CopyGameAssemblies);
        else if (stage == BuildStage.CopyPlugins) yield return Step("copy plugins", CopyPluginsStage);
        else if (stage == OrganizeOutput) yield return Step("organize output", OrganizeOutputStage);
        else if (stage == BuildStage.WriteManifest) yield return Step("write player manifest", WritePlayerManifest);
        else if (stage == BuildStage.PackAssets) yield return Step("package assets", PackageAssets);
        else if (stage == BuildStage.ExportSettings) yield return Step("export settings", ExportSettingsStage);
        else if (stage == BuildStage.Finalize) yield return Step("finalize", FinalizeBuild);
    }

    private static BuildOperation Step(string what, Func<IBuildContext, CancellationToken, Task> body)
        => new BuildOperation.Custom(new StepHandler(body), what);

    private sealed class StepHandler(Func<IBuildContext, CancellationToken, Task> body) : IOperationHandler
    {
        public Task ExecuteAsync(IBuildContext context, CancellationToken ct) => body(context, ct);
    }

    // ================================================================
    //  Stage bodies
    // ================================================================

    private Task CompileScripts(IBuildContext context, CancellationToken ct)
    {
        context.Log("Compiling scripts...");
        var compileResult = ScriptCompiler.CompileAll(_project);
        if (!compileResult.Success)
            throw new InvalidOperationException($"Script compilation failed:\n{compileResult.Errors}");

        context.Log("Scripts compiled successfully.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Everything that can reject a build without doing any work. Runs before the script compile so a
    /// misconfigured build fails in a moment rather than after the slowest step in the pipeline.
    /// </summary>
    private Task ValidateRequest(IBuildContext context, CancellationToken ct)
    {
        context.Log("Validating project...");
        var request = context.Request;

        if (request.Scenes.Count == 0)
            throw new InvalidOperationException("No scenes in build. Add at least one scene.");

        if (request.Profile is not DesktopBuildProfile profile)
            throw new InvalidOperationException(
                $"A desktop build needs a {nameof(DesktopBuildProfile)}, got {request.Profile?.GetType().Name ?? "none"}.");

        var target = profile.Target;

        // Publish takes one identifier. A target naming several needs the architectures merged after the
        // fact, and shipping the first one under the target's name would be a lie about what was built.
        if (target.RuntimeIdentifiers.Count > 1)
            throw new InvalidOperationException(
                $"'{target.DisplayName}' covers {string.Join(", ", target.RuntimeIdentifiers)} and this pipeline " +
                "publishes one architecture at a time. Build each separately until merging is supported.");

        VerifyPluginArchitectures(target);

        if (!IsUsableOutputRoot(request.OutputDirectory, ResolveOutputDirectory(request), request.ProjectRoot))
            throw new InvalidOperationException(
                $"Build output directory '{request.OutputDirectory}' is not a usable place to build into. " +
                "Choose a folder outside the project's Assets, and not the project root itself.");

        return Task.CompletedTask;
    }

    /// <summary>
    /// Fails when a native plugin is built for a different architecture than the target.
    /// </summary>
    /// <remarks>
    /// A plugin declares its own CPU and is filed under the identifier that implies, so one left at the
    /// default x64 in an arm64 build lands in a folder the player never probes. That is invisible until
    /// the game runs and a P/Invoke fails, which is why it stops the build here instead.
    /// </remarks>
    private void VerifyPluginArchitectures(PlatformTarget target)
    {
        string architecture = ArchitectureOf(target.RuntimeIdentifiers[0]);

        var wrong = PluginScanner.ScanAll(_project)
            .Where(p => p.IsNative && p.AppliesToBuild(target.AssemblyPlatform ?? BuildPlatforms.Windows))
            .Where(p => !p.Cpu.Equals("AnyCPU", StringComparison.OrdinalIgnoreCase)
                     && !p.Cpu.Equals(architecture, StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{p.FileName} ({p.Cpu})")
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        if (wrong.Count > 0)
            throw new InvalidOperationException(
                $"'{target.DisplayName}' is {architecture}, but these native plugins are not: {string.Join(", ", wrong)}. " +
                "Set each plugin's CPU to match, or to AnyCPU.");
    }

    /// <summary>The architecture part of a runtime identifier, which is everything after the last dash.</summary>
    private static string ArchitectureOf(string runtimeIdentifier)
    {
        int dash = runtimeIdentifier.LastIndexOf('-');
        return dash >= 0 ? runtimeIdentifier[(dash + 1)..] : runtimeIdentifier;
    }

    private static string ResolveOutputDirectory(BuildRequest request)
        => Path.IsPathRooted(request.OutputDirectory)
            ? request.OutputDirectory
            : Path.Combine(request.ProjectRoot, request.OutputDirectory);

    /// <summary>Works out the paths and assemblies every later stage reads. Needs the compile to have run.</summary>
    private Task PlanTheBuild(IBuildContext context, CancellationToken ct)
    {
        // Everything the request carries is read from the request. Only the genuinely editor-owned
        // pieces below (the script compiler, the scene manager, the asset database) still reach out.
        var request = context.Request;
        var profile = (DesktopBuildProfile)request.Profile!;

        string targetPlatform = profile.Target.AssemblyPlatform ?? BuildPlatforms.Windows;
        var assemblies = ScriptCompiler.GetBuildAssemblies(_project, targetPlatform);

        string outputDirectory = CreateBuildDirectory(request);
        context.Log($"Building into {outputDirectory}");

        // Always save the current scene: the build reads the cache, which comes from the .scene file.
        if (EditorSceneManager.CurrentScenePath != null)
        {
            EditorSceneManager.Save();
            context.Log("Auto-saved current scene.");
        }
        else
        {
            Runtime.Debug.LogWarning("[Build] Current scene has no save path. Save it first for accurate build.");
        }

        var db = EditorAssetBackend.Instance;
        foreach (var scene in request.Scenes)
            db?.Reimport(scene);

        string buildTempDir = request.TempPath;
        if (Directory.Exists(buildTempDir))
            try { Directory.Delete(buildTempDir, true); } catch { }
        Directory.CreateDirectory(buildTempDir);

        string contentDir = Path.Combine(outputDirectory, "Content");

        context.SetOutput(new DesktopPlan
        {
            Profile = profile,
            TargetPlatform = targetPlatform,
            OutputDirectory = outputDirectory,
            ContentDir = contentDir,
            SettingsDir = Path.Combine(contentDir, "Settings"),
            BuildTempDir = buildTempDir,
            DefaultScene = request.DefaultScene,
            Assemblies = assemblies,
        });

        return Task.CompletedTask;
    }

    private Task CollectAndVerify(IBuildContext context, CancellationToken ct)
    {
        var plan = context.GetOutput<DesktopPlan>();

        context.Log("Start collecting assets...");
        var collection = CollectAssets(_settings, _progress);
        context.Log($"Collected {collection.AllAssets.Count} assets, {collection.ResourcesMap.Count} resources.");

        var db = EditorAssetBackend.Instance;
        int reimported = 0;
        foreach (var guid in collection.AllAssets)
        {
            ct.ThrowIfCancellationRequested();

            if (File.Exists(Path.Combine(context.Request.AssetCachePath, $"{guid}.asset"))) continue;
            db?.Reimport(guid);
            reimported++;
        }
        if (reimported > 0)
            context.Log($"Reimported {reimported} assets with missing caches.");

        ResolveVariants(context, plan, collection, ct);

        context.SetOutput(new CollectedAssets(collection));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Processors registered for this pipeline. Empty by default, so every asset ships in its imported
    /// form, which is what desktop does today.
    /// </summary>
    public IList<IAssetVariantProcessor> AssetProcessors { get; } = new List<IAssetVariantProcessor>();

    // What each asset ships as, for the ones a processor claimed. Read by OpenShippedAsset during
    // packaging, which is what puts the processed bytes in the build rather than the imported ones.
    private AssetVariantResolver? _resolver;
    private readonly Dictionary<Guid, ResolvedVariant> _variants = [];

    /// <summary>Processed bytes when a processor claimed this asset, the imported ones otherwise.</summary>
    protected override Stream? OpenShippedAsset(Guid guid)
    {
        if (_resolver != null && _variants.TryGetValue(guid, out var variant))
            return _resolver.OpenAsync(variant).GetAwaiter().GetResult();

        return base.OpenShippedAsset(guid);
    }

    /// <summary>
    /// Converts each shipped asset into the form this target wants, reusing anything already produced
    /// for the same content and processor.
    /// </summary>
    /// <remarks>
    /// With no processors registered this costs one dictionary lookup per asset and touches no disk,
    /// because selection happens before any hashing. It exists now so that registering a real processor
    /// is the only change needed, rather than also having to thread caching through the pipeline.
    /// </remarks>
    private void ResolveVariants(IBuildContext context, DesktopPlan plan, AssetCollector.CollectionResult collection, CancellationToken ct)
    {
        // Cleared before the guards below, or a second build on this instance with the processors gone
        // would still serve the previous build's variants out of OpenShippedAsset.
        _resolver = null;
        _variants.Clear();

        if (AssetProcessors.Count == 0) return;

        var source = EditorAssetBackend.Instance;
        if (source == null) return;

        var cache = new LocalVariantCache(Path.Combine(_project.LibraryPath, "BuildCache"));
        _resolver = new AssetVariantResolver(AssetProcessors, cache);

        // Sub-assets ship with their own GUID and their own cache file, but they are not entries in the
        // database: a Sprite lives inside its Texture's entry. Mapping them onto the parent is what lets
        // a processor see a path and an importer, and without it every sub-asset is silently skipped.
        var byGuid = new Dictionary<Guid, AssetEntry>();
        foreach (var entry in source.GetAllEntries())
        {
            byGuid[entry.Guid] = entry;
            foreach (var sub in entry.SubAssets)
                byGuid[sub.Guid] = entry;
        }

        int processed = 0, reused = 0;

        foreach (var guid in collection.AllAssets.OrderBy(g => g))
        {
            ct.ThrowIfCancellationRequested();

            if (!byGuid.TryGetValue(guid, out var asset)) continue;

            string imported = Path.Combine(context.Request.AssetCachePath, $"{guid}.asset");
            if (!File.Exists(imported)) continue;

            var resolved = _resolver
                .ResolveAsync(asset, imported, plan.Profile.Target, ct)
                .GetAwaiter().GetResult();

            if (resolved.Origin == VariantOrigin.Processed) processed++;
            else if (resolved.Origin == VariantOrigin.Cached) reused++;

            // Only a claimed asset has a key. The rest ship from the imported cache as they always did.
            if (resolved.Key != null)
                _variants[guid] = resolved;
        }

        if (processed + reused > 0)
            context.Log($"Asset variants: {processed} processed, {reused} reused from cache.");

        // After resolving, so everything this build touched is the most recently used and survives.
        int pruned = cache.PruneAsync().GetAwaiter().GetResult();
        if (pruned > 0)
            context.Log($"Variant cache: discarded {pruned} entry(s) no build has asked for.");
    }

    private Task PrepareEmbeddedAssets(IBuildContext context, CancellationToken ct)
    {
        var plan = context.GetOutput<DesktopPlan>();
        var collected = context.GetOutput<CollectedAssets>();

        context.Log("Preparing embedded assets...");
        string embeddedDir = Path.Combine(plan.BuildTempDir, "Assets");
        Directory.CreateDirectory(embeddedDir);

        var embeddedAssets = CopyLooseAssets(collected.Collection.AllAssets, embeddedDir, _progress, ct);
        ReportMissingAssets(context, PrepareEmbedded, collected.Collection.AllAssets, embeddedAssets);

        GenerateManifest(Path.Combine(embeddedDir, "asset_manifest.bin"),
            embeddedAssets, collected.Collection.ResourcesMap, plan.DefaultScene);

        // Sorted because the filesystem does not promise an enumeration order, and these become
        // EmbeddedResource items, so their order lands in the assembly.
        var paths = Directory.EnumerateFiles(embeddedDir, "*.*", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        context.SetOutput(new EmbeddedAssetPaths(paths));
        return Task.CompletedTask;
    }

    private Task GeneratePlayer(IBuildContext context, CancellationToken ct)
    {
        var plan = context.GetOutput<DesktopPlan>();
        List<string>? embedded = context.TryGetOutput<EmbeddedAssetPaths>(out var e) ? e?.Paths : null;

        GeneratePlayerSource(plan.BuildTempDir);
        GeneratePlayerCsproj(_project, _settings, plan.Profile, plan.BuildTempDir, embedded);

        context.Log("Generated player source and project.");
        return Task.CompletedTask;
    }

    private async Task PublishPlayer(IBuildContext context, CancellationToken ct)
    {
        var plan = context.GetOutput<DesktopPlan>();

        context.Log("Compiling player...");
        string csprojPath = Path.Combine(plan.BuildTempDir, $"{context.Request.ProjectName}.Player.csproj");

        var args = new StringBuilder();
        args.Append($"publish \"{csprojPath}\"");
        args.Append($" -c {context.Request.Configuration}");
        args.Append($" -r {plan.Profile.RuntimeIdentifier}");
        args.Append($" -o \"{plan.OutputDirectory}\"");
        args.Append($" --self-contained {plan.Profile.SelfContained.ToString().ToLowerInvariant()}");

        // Diagnostics land in the report with their code, file and line, so a caller can group and
        // navigate them instead of re-reading the log.
        int errors = 0;
        var (exitCode, stdout, stderr) = await RunDotnetAsync(args.ToString(), _progress, ct,
            onDiagnostic: diagnostic =>
            {
                if (diagnostic.Severity == BuildSeverity.Error) errors++;
                context.Report(diagnostic);
            },
            stage: BuildStage.CompilePlayer).ConfigureAwait(false);

        if (exitCode != 0)
        {
            ScriptCompiler.LogBuildOutput(stdout, stderr);

            // The reported diagnostics already say what went wrong; this only says the step failed.
            throw new InvalidOperationException(errors > 0
                ? $"Player compilation failed with {errors} error(s)."
                : $"Player compilation failed (dotnet publish exited {exitCode}).");
        }
    }

    private Task CopyGameAssemblies(IBuildContext context, CancellationToken ct)
    {
        var plan = context.GetOutput<DesktopPlan>();
        var copied = new List<string>();

        foreach (var asm in plan.Assemblies)
        {
            ct.ThrowIfCancellationRequested();

            if (!File.Exists(asm.DllPath))
            {
                Runtime.Debug.LogWarning($"[Build] Expected user assembly missing: {asm.DllPath}");
                continue;
            }

            File.Copy(asm.DllPath, Path.Combine(plan.OutputDirectory, Path.GetFileName(asm.DllPath)), true);
            copied.Add(Path.GetFileName(asm.DllPath));

            string pdb = Path.ChangeExtension(asm.DllPath, ".pdb");
            if (File.Exists(pdb))
            {
                File.Copy(pdb, Path.Combine(plan.OutputDirectory, Path.GetFileName(pdb)), true);
                copied.Add(Path.GetFileName(pdb));
            }

            context.Log($"Copied game assembly: {Path.GetFileName(asm.DllPath)}");
        }

        context.SetOutput(new CopiedAssemblies(copied));
        return Task.CompletedTask;
    }

    private Task CopyPluginsStage(IBuildContext context, CancellationToken ct)
    {
        var plan = context.GetOutput<DesktopPlan>();
        CopyPlugins(_project, plan.OutputDirectory, plan.TargetPlatform, _progress, ct);
        return Task.CompletedTask;
    }

    private Task OrganizeOutputStage(IBuildContext context, CancellationToken ct)
    {
        var plan = context.GetOutput<DesktopPlan>();
        var copied = context.GetOutput<CopiedAssemblies>();
        OrganizePublishOutput(plan.OutputDirectory, context.Request.ProjectName, copied.FileNames);
        return Task.CompletedTask;
    }

    private Task WritePlayerManifest(IBuildContext context, CancellationToken ct)
    {
        var plan = context.GetOutput<DesktopPlan>();
        BuildPlayerManifest(context.Request, plan).Save(plan.OutputDirectory);
        context.Log($"Wrote {PlayerManifest.FileName}.");
        return Task.CompletedTask;
    }

    private Task PackageAssets(IBuildContext context, CancellationToken ct)
    {
        var plan = context.GetOutput<DesktopPlan>();
        var collected = context.GetOutput<CollectedAssets>();

        var request = context.Request;

        context.Log("Packaging assets...");
        var all = collected.Collection.AllAssets;
        int assetCount = all.Count;

        // Embedded assets were baked into the assembly at compile time, so there is nothing to place.
        if (request.Packaging != AssetPackagingMode.Embedded)
        {
            Directory.CreateDirectory(plan.ContentDir);

            var shipped = request.Packaging switch
            {
                AssetPackagingMode.ProwlPak => PackAssets(PlanChunks(request, collected), plan.ContentDir, request.MaxPackSizeMB, _progress, ct),
                AssetPackagingMode.LooseFiles => CopyLooseAssets(all, plan.ContentDir, _progress, ct),
                _ => all,
            };

            ReportMissingAssets(context, BuildStage.PackAssets, all, shipped);
            assetCount = shipped.Count;

            GenerateManifest(Path.Combine(plan.ContentDir, "asset_manifest.bin"),
                shipped, collected.Collection.ResourcesMap, plan.DefaultScene);
        }

        context.Log($"Packaged {assetCount} assets ({request.Packaging}).");
        context.SetOutput(new PackagedAssets(assetCount));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Warns about assets that were collected but had nothing to ship, which means an import that never
    /// produced a cache file. They are left out of the manifest rather than named as a missing file.
    /// </summary>
    private static void ReportMissingAssets(IBuildContext context, BuildStage stage, HashSet<Guid> collected, HashSet<Guid> shipped)
    {
        if (shipped.Count >= collected.Count) return;

        var missing = collected.Where(g => !shipped.Contains(g)).OrderBy(g => g).ToList();

        context.Report(new BuildIssue
        {
            Severity = BuildSeverity.Warning,
            Code = "PB2001",
            Message = $"{missing.Count} collected asset(s) had no imported data and were left out of the build: " +
                      string.Join(", ", missing.Take(5)) + (missing.Count > 5 ? ", ..." : ""),
            Stage = stage,
        });
    }

    /// <summary>
    /// Groups the shipped assets by which scene pulls them in. Falls back to one chunk when there is no
    /// asset source, which keeps packing working rather than shipping nothing.
    /// </summary>
    private static IReadOnlyList<AssetChunk> PlanChunks(BuildRequest request, CollectedAssets collected)
    {
        var source = EditorAssetBackend.Instance;
        if (source == null)
            return [new AssetChunk(ChunkPlanner.CommonChunk, collected.Collection.AllAssets.OrderBy(g => g).ToList())];

        var subAssets = new Dictionary<Guid, IReadOnlyList<Guid>>();
        foreach (var entry in source.GetAllEntries())
            if (entry.SubAssets.Length > 0)
                subAssets[entry.Guid] = entry.SubAssets.Select(s => s.Guid).ToList();

        return ChunkPlanner.Plan(source.Dependencies, request.Scenes,
            collected.Collection.ResourcesMap.Values.ToList(), collected.Collection.AllAssets, subAssets);
    }

    private Task ExportSettingsStage(IBuildContext context, CancellationToken ct)
    {
        var plan = context.GetOutput<DesktopPlan>();
        ExportSettings(context, plan.SettingsDir, _progress);
        context.Log("Exported project settings.");
        return Task.CompletedTask;
    }

    private Task FinalizeBuild(IBuildContext context, CancellationToken ct)
    {
        var plan = context.GetOutput<DesktopPlan>();

        // Engine-custom natives (e.g. miniaudioex) that NuGet does not provide. The NuGet ones
        // (glfw3, soft_oal, Magick.Native) are already handled by dotnet publish.
        context.Log("Copying native libraries...");
        CopyEngineNatives(plan.OutputDirectory, plan.Profile.Target, ct);

        // macOS ships as a real .app bundle, not a bare folder of files.
        if (plan.Profile.Target.AssemblyPlatform == BuildPlatforms.MacOS)
        {
            context.Log("Bundling macOS .app...");
            var general = TryGetGeneralSettings();
            BundleMacApp(plan.OutputDirectory, context.Request.ProjectName,
                ProductNameFor(context.Request.ProjectName), general?.CompanyName ?? "", general?.Version ?? "0.0.0");
        }

        if (Directory.Exists(plan.BuildTempDir))
            try { Directory.Delete(plan.BuildTempDir, true); } catch { }

        context.Log("Build complete!");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Copies the engine's native libraries for this target's runtime identifiers only.
    /// </summary>
    /// <remarks>
    /// The engine ships natives for every identifier it supports. Copying the lot would put Android and
    /// macOS binaries inside a Windows build, so the folders are filtered by name against the target.
    /// </remarks>
    private static void CopyEngineNatives(string outputDirectory, PlatformTarget target, CancellationToken ct)
    {
        string engineDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        string engineRuntimes = Path.Combine(engineDir, "runtimes");
        if (!Directory.Exists(engineRuntimes)) return;

        string destinationRoot = Path.Combine(outputDirectory, "runtimes");

        // Files at the root belong to no identifier. They are the third party licence texts, and they
        // ship with every build.
        Copy(Directory.EnumerateFiles(engineRuntimes), engineRuntimes);

        foreach (string rid in target.RuntimeIdentifiers)
        {
            string source = Path.Combine(engineRuntimes, rid);
            if (Directory.Exists(source))
                Copy(Directory.EnumerateFiles(source, "*.*", SearchOption.AllDirectories), engineRuntimes);
        }

        void Copy(IEnumerable<string> files, string relativeTo)
        {
            foreach (var file in files.OrderBy(f => f, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();

                string dest = Path.Combine(destinationRoot, Path.GetRelativePath(relativeTo, file));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(file, dest, true);
            }
        }
    }

    /// <summary>
    /// Writes the player's entry program.
    /// </summary>
    /// <remarks>
    /// One line, because the player is a real compiled project now. Everything that used to be emitted
    /// here as text lives in Prowl.Player, and everything that varies per build travels in the manifest
    /// instead of being interpolated into source.
    /// </remarks>
    private void GeneratePlayerSource(string outputDir)
    {
        File.WriteAllText(Path.Combine(outputDir, "Program.cs"),
            "return Prowl.Player.PlayerEntryPoint.Main(System.Environment.GetCommandLineArgs()[1..]);" + Environment.NewLine);
    }

    /// <summary>Describes the build to the player. Written after publish, which clears the output directory.</summary>
    private static PlayerManifest BuildPlayerManifest(BuildRequest request, DesktopPlan plan)
    {
        var general = TryGetGeneralSettings();

        return new PlayerManifest
        {
            ProductName = general?.ProductName ?? "Prowl Game",
            CompanyName = general?.CompanyName ?? "",
            Version = general?.Version ?? "0.0.0",
            TargetId = plan.Profile.TargetId,
            BuildDateUtcTicks = BuildTimestampTicks(),
            DefaultScene = plan.DefaultScene.ToString(),
            AssemblyLoadOrder = plan.Assemblies.Select(a => a.Name).ToList(),
            Packaging = request.Packaging,
            WindowWidth = plan.Profile.WindowWidth,
            WindowHeight = plan.Profile.WindowHeight,
        };
    }

    /// <summary>
    /// Now, unless SOURCE_DATE_EPOCH says otherwise. It is the only value written into a build that
    /// would otherwise differ between two runs over identical content, and honouring the convention is
    /// what lets a release build be reproduced and compared byte for byte.
    /// </summary>
    private static long BuildTimestampTicks()
    {
        const long MaxUnixSeconds = 253402300799;

        if (long.TryParse(Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH"),
                NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds)
            && seconds >= 0 && seconds <= MaxUnixSeconds)
            return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime.Ticks;

        return DateTime.UtcNow.Ticks;
    }

    private static GeneralSettings? TryGetGeneralSettings()
    {
        try { return EditorRegistries.GetSettings<GeneralSettings>(); }
        catch { return null; }
    }

    /// <summary>
    /// Describes the player project, then lets <see cref="MSBuildProjectSpec"/> render it.
    /// </summary>
    /// <remarks>
    /// Filling in a spec rather than assembling XML is the difference between adding a platform and
    /// reimplementing this pipeline. It also gets ordering and escaping for free, so a path containing
    /// an ampersand no longer produces an invalid project file.
    /// </remarks>
    private void GeneratePlayerCsproj(Project project, BuildSettings settings, DesktopBuildProfile desktopProfile,
        string outputDir, List<string>? embeddedAssetPaths = null)
    {
        string engineDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;

        var properties = new Dictionary<string, string>
        {
            ["OutputType"] = "Exe",
            ["EnableDefaultCompileItems"] = "false",
            ["AllowUnsafeBlocks"] = "true",
            ["Nullable"] = "annotations",
            ["AssemblyName"] = project.Name,
            // Deliberately no PROWL_EDITOR: this is the shipped game, not the editor.
            ["DefineConstants"] = $"PROWL;{ScriptCompiler.GetVersionDefine()};{FinalizeDefineString(settings, this)}",
            ["IncludeNativeLibrariesForSelfExtract"] = "true",
        };

        if (desktopProfile.SelfContained)
            properties["SelfContained"] = "true";

        // Trimming is still experimental here. Partial mode plus rooting the runtime keeps the trimmer
        // away from assemblies it cannot see through reflection.
        var trimmerRoots = new List<string>();
        if (desktopProfile.PublishTrimmed)
        {
            properties["PublishTrimmed"] = "true";
            properties["TrimMode"] = "partial";
            trimmerRoots.Add("Prowl.Runtime");
        }

        var spec = new MSBuildProjectSpec
        {
            TargetFramework = "net10.0",
            RuntimeIdentifiers = desktopProfile.Target.RuntimeIdentifiers,
            Properties = properties,
            References =
            [
                new AssemblyRef("Prowl.Runtime", Path.Combine(engineDir, "Prowl.Runtime.dll")),
                new AssemblyRef(PlayerAssembly, Path.Combine(engineDir, PlayerAssembly + ".dll")),
            ],
            Packages = GetRuntimePackageReferences().Select(p => new PackageRef(p.Name, p.Version)).ToList(),
            TrimmerRootAssemblies = trimmerRoots,

            // Only the generated entry program: user scripts are separate, precompiled assemblies.
            Compile = ["Program.cs"],
            EmbeddedResources = BuildEmbeddedResources(outputDir, embeddedAssetPaths),
        };

        // User NuGet packages and project references come from the project's Directory.Build.props. This
        // csproj lives under Temp/ inside the project root, so MSBuild imports that file automatically.
        File.WriteAllText(Path.Combine(outputDir, $"{project.Name}.Player.csproj"), spec.ToXml());
    }

    private static List<EmbeddedResourceRef> BuildEmbeddedResources(string outputDir, List<string>? assetPaths)
    {
        if (assetPaths == null || assetPaths.Count == 0) return [];

        var resources = new List<EmbeddedResourceRef>(assetPaths.Count);

        foreach (string assetPath in assetPaths)
        {
            string relative = Path.GetRelativePath(outputDir, assetPath).Replace('\\', '/');

            // The logical name is what Assembly.GetManifestResourceStream is given at runtime.
            string logicalName = assetPath.EndsWith("asset_manifest.bin", StringComparison.Ordinal)
                ? "Assets._manifest.bin"
                : "Assets." + Path.GetFileName(assetPath);

            resources.Add(new EmbeddedResourceRef(relative, logicalName));
        }

        return resources;
    }

    public void ListDependencies(StringBuilder sb)
    {
        // Read PackageReferences from assembly metadata embedded by the MSBuild
        // EmbedPackageReferences target in Prowl.Runtime.csproj. This works regardless
        // of whether the source tree is present - the data lives in the compiled DLL.
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

    /// <summary>
    /// Where the finished build's executable ended up. A macOS build is bundled by
    /// <see cref="BundleMacApp"/> after everything else, so the answer is inside the bundle and not
    /// beside it, which is what makes Build and Run work there.
    /// </summary>
    public override string GetExecutablePath(string outputPath, BuildSettings settings)
    {
        var profile = settings.GetProfile<DesktopBuildProfile>(GetType());
        string platform = profile.Target.AssemblyPlatform ?? BuildPlatforms.Windows;
        string name = Project.Current!.Name;

        if (platform == BuildPlatforms.MacOS)
            return Path.Combine(outputPath, MacBundleName(ProductNameFor(name)), "Contents", "MacOS", name);

        return Path.Combine(outputPath, name + (platform == BuildPlatforms.Windows ? ".exe" : ""));
    }

    public override bool CanRunOnHost(BuildSettings settings)
    {
        string platform = settings.GetProfile<DesktopBuildProfile>(GetType()).Target.AssemblyPlatform
            ?? BuildPlatforms.Windows;

        return platform switch
        {
            BuildPlatforms.Linux => OperatingSystem.IsLinux(),
            BuildPlatforms.MacOS => OperatingSystem.IsMacOS(),
            _ => OperatingSystem.IsWindows(),
        };
    }

    /// <summary>The name the bundle is given, which has to match what <see cref="BundleMacApp"/> used.</summary>
    private static string ProductNameFor(string fallback)
    {
        string? product = TryGetGeneralSettings()?.ProductName;
        return string.IsNullOrWhiteSpace(product) ? fallback : product;
    }

    /// <summary>
    /// Whether builds may be placed inside the chosen directory.
    /// </summary>
    /// <remarks>
    /// Each build gets its own new folder in here, so the directory itself may hold as many previous
    /// builds as the user likes and nothing existing is ever touched. What is refused is a place that
    /// would poison the project: the project root, or anywhere under Assets, which the asset database
    /// scans and would import a whole player into.
    /// </remarks>
    public static bool IsUsableOutputRoot(string rawOutput, string resolvedOutput, string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
            return false;

        string output = Normalize(resolvedOutput);
        string root = Normalize(projectRoot);
        string assets = Path.Combine(root, "Assets");

        // Case insensitive everywhere, which on Linux can refuse a path that would have been fine.
        // Refusing too much is the safe direction here.
        return !output.Equals(root, StringComparison.OrdinalIgnoreCase)
            && !output.Equals(assets, StringComparison.OrdinalIgnoreCase)
            && !output.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>
    /// Creates a new folder inside the chosen directory for this build to fill.
    /// </summary>
    /// <remarks>
    /// One folder per build, so a build never writes into another's output and the previous one stays
    /// runnable. Numbered rather than stamped with the time, because the names then sort in build order
    /// and are short enough to say out loud.
    /// </remarks>
    private static string CreateBuildDirectory(BuildRequest request)
    {
        const int Limit = 10000;

        string parent = ResolveOutputDirectory(request);
        Directory.CreateDirectory(parent);

        string name = EditorUtils.SafeFileName(request.ProjectName, "Build");

        for (int index = 0; index < Limit; index++)
        {
            string candidate = Path.Combine(parent, index == 0 ? name : $"{name} ({index})");
            if (Directory.Exists(candidate) || File.Exists(candidate)) continue;

            // Created here rather than later, because creating it is what claims the name.
            Directory.CreateDirectory(candidate);
            return candidate;
        }

        throw new InvalidOperationException(
            $"'{parent}' already holds {Limit} builds. Clear some out before building again.");
    }


    /// <summary>
    /// Copies project plugins into the player output: managed plugins go to <c>runtimes/</c> (probed
    /// by the managed assembly resolver), native plugins go to <c>runtimes/{rid}/native</c> (probed by
    /// the native resolver). Editor-only plugins and plugins not targeting this platform are skipped.
    /// </summary>
    private static void CopyPlugins(Project project, string outputDir, string platform, BuildProgress? progress, CancellationToken ct)
    {
        string runtimesDir = Path.Combine(outputDir, "runtimes");
        int managed = 0, native = 0;
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var plugin in PluginScanner.ScanAll(project))
        {
            ct.ThrowIfCancellationRequested();

            if (!plugin.AppliesToBuild(platform)) continue;

            string dest;
            if (plugin.IsManaged)
            {
                // The player's managed resolver probes runtimes/{assemblyName}.dll - the CLR identity
                // recorded in referencing assemblies, which is not necessarily the file name on disk.
                string asmName;
                try { asmName = AssemblyName.GetAssemblyName(plugin.AbsolutePath).Name ?? Path.GetFileNameWithoutExtension(plugin.FileName); }
                catch { asmName = Path.GetFileNameWithoutExtension(plugin.FileName); }

                Directory.CreateDirectory(runtimesDir);
                dest = Path.Combine(runtimesDir, asmName + ".dll");
                managed++;
            }
            else
            {
                string nativeDir = Path.Combine(runtimesDir, plugin.RuntimeIdentifierFor(platform), "native");
                Directory.CreateDirectory(nativeDir);
                dest = Path.Combine(nativeDir, plugin.FileName);
                native++;
            }

            if (!written.Add(dest))
                Runtime.Debug.LogWarning($"[Build] Plugin '{plugin.FileName}' overwrites '{Path.GetFileName(dest)}' (another plugin maps to the same destination).");
            File.Copy(plugin.AbsolutePath, dest, true);
        }

        if (managed + native > 0)
            progress?.Log($"Copied {managed} managed and {native} native plugin(s).");
    }

    /// <summary>
    /// Moves the third party dependencies out of the publish root into runtimes/, so a shipped player is
    /// its executable and little else.
    /// </summary>
    /// <remarks>
    /// Framework assemblies never move. The runtime resolves those itself, before any of our code has
    /// run and therefore before the player's resolver exists to redirect them, so one moved out of the
    /// root is a self contained build that dies on startup with a missing assembly. This was a list of
    /// the specific ones somebody had watched fail, which is a list that is always one short.
    /// </remarks>
    private static void OrganizePublishOutput(string outputDir, string projectName, IEnumerable<string> userAssemblies)
    {
        string libsDir = Path.Combine(outputDir, "runtimes");
        Directory.CreateDirectory(libsDir);

        var keepInRoot = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"{projectName}.dll",
            $"{projectName}.exe",
            $"{projectName}.pdb",
            "Prowl.Runtime.dll",
            "Prowl.Runtime.pdb",

            // The entry program calls straight into this, and the resolver that searches runtimes/ is
            // itself inside it. Move it there and nothing can load it in the first place.
            $"{PlayerAssembly}.dll",
            $"{PlayerAssembly}.pdb",
        };
        foreach (var name in userAssemblies)
            keepInRoot.Add(name);

        foreach (var file in Directory.GetFiles(outputDir, "*.dll"))
        {
            string fileName = Path.GetFileName(file);
            if (keepInRoot.Contains(fileName) || IsFrameworkAssembly(fileName)) continue;

            // Skip native (unmanaged) DLLs - only move managed assemblies. Anything else that stops the
            // header being read (a lock, a permission) means leaving the file where publish put it, which
            // is far better than failing a build that is otherwise finished.
            try { AssemblyName.GetAssemblyName(file); }
            catch (BadImageFormatException) { continue; }
            catch (Exception e)
            {
                Runtime.Debug.LogWarning($"[Build] Left '{fileName}' in the output root: {e.Message}");
                continue;
            }

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

    /// <summary>
    /// Whether the .NET runtime, rather than the player's own resolver, is responsible for finding this
    /// assembly. Matched on the name because it has to be answered from a file, before anything is loaded.
    /// </summary>
    public static bool IsFrameworkAssembly(string fileName)
        => fileName.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
        || fileName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("netstandard.dll", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("mscorlib.dll", StringComparison.OrdinalIgnoreCase)
        || fileName.Equals("WindowsBase.dll", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Wraps a finished publish output into a real "ProductName.app" bundle: everything moves under
    /// Contents/MacOS unchanged (so relative lookups like "Content/Settings" keep working), plus a
    /// minimal Contents/Info.plist. Does NOT code-sign or notarize - Gatekeeper will still show an
    /// "unidentified developer" warning until the user right-clicks Open (or runs `xattr -cr`).
    /// </summary>
    private static void BundleMacApp(string outputDir, string executableName, string productName, string companyName, string version)
    {
        // Snapshot BEFORE creating the bundle folder so it never sweeps up itself.
        var existingEntries = Directory.GetFileSystemEntries(outputDir).ToList();

        string appDir = Path.Combine(outputDir, MacBundleName(productName));
        string macOsDir = Path.Combine(appDir, "Contents", "MacOS");
        Directory.CreateDirectory(macOsDir);

        foreach (var entry in existingEntries)
        {
            string dest = Path.Combine(macOsDir, Path.GetFileName(entry));
            if (Directory.Exists(entry))
                Directory.Move(entry, dest);
            else
                File.Move(entry, dest);
        }

        // Windows can't hold POSIX exec bits - a .app built there will need `chmod +x` once it
        // actually reaches a Mac.
        string exePath = Path.Combine(macOsDir, executableName);
        if (!OperatingSystem.IsWindows() && File.Exists(exePath))
        {
            try
            {
                UnixFileMode mode = File.GetUnixFileMode(exePath);
                File.SetUnixFileMode(exePath, mode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
            }
            catch { /* best-effort */ }
        }

        string bundleId = $"com.{SanitizeBundleIdSegment(companyName)}.{SanitizeBundleIdSegment(productName)}";

        // A plist is XML, so a product name containing an ampersand makes one macOS refuses to read.
        string name = SecurityElement.Escape(productName) ?? productName;
        string exe = SecurityElement.Escape(executableName) ?? executableName;
        string ver = SecurityElement.Escape(version) ?? version;

        string plist = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>CFBundleName</key><string>{name}</string>
                <key>CFBundleDisplayName</key><string>{name}</string>
                <key>CFBundleExecutable</key><string>{exe}</string>
                <key>CFBundleIdentifier</key><string>{bundleId}</string>
                <key>CFBundlePackageType</key><string>APPL</string>
                <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
                <key>CFBundleVersion</key><string>{ver}</string>
                <key>CFBundleShortVersionString</key><string>{ver}</string>
                <key>NSHighResolutionCapable</key><true/>
            </dict>
            </plist>
            """;
        File.WriteAllText(Path.Combine(appDir, "Contents", "Info.plist"), plist);
    }

    /// <summary>The bundle directory name. Shared with <see cref="GetExecutablePath"/>, which has to agree.</summary>
    private static string MacBundleName(string productName)
        => EditorUtils.SafeFileName(productName, "Game") + ".app";

    // Bundle identifier segments are restricted to alphanumerics, dots and hyphens.
    private static string SanitizeBundleIdSegment(string s)
    {
        string cleaned = new string([.. s.Where(c => char.IsLetterOrDigit(c) || c is '-' or '.')]);
        return cleaned.Length > 0 ? cleaned : "prowlgame";
    }
}
