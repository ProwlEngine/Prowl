using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Prowl.Echo;
using Prowl.Editor.Projects;
using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Projects.Settings;

namespace Prowl.Editor.Build;

/// <summary>
/// Abstract base for platform-specific build pipelines.
/// Subclass for Desktop, Android, Web, Console, etc.
/// </summary>
public abstract class BuildPipeline
{
    public abstract string DisplayName { get; }

    /// <summary>Glyph shown on the platform card in the Build window.</summary>
    public virtual string Icon => Prowl.Editor.Theming.EditorIcons.Desktop;

    // ================================================================
    //  Staged plan, driven by BuildExecutor
    // ================================================================

    /// <summary>
    /// The stages for this request and their ordering. Derived from the request, because the shape
    /// genuinely changes with it: embedding assets into the assembly and packing them beside it impose
    /// opposite orderings on the same two stages.
    /// </summary>
    public abstract StageGraph CreateStageGraph(BuildRequest request);

    /// <summary>
    /// The work for one stage, streamed so a large project never materialises every operation at once.
    /// Must yield in a stable order for identical inputs, or the build stops being reproducible.
    /// </summary>
    /// <remarks>
    /// Planning is interleaved with execution rather than done once up front. A single plan can only
    /// describe a build that copies files it already knows about; the moment assets are processed, a
    /// later stage cannot be planned until an earlier one has run, because it does not yet know what
    /// was produced.
    /// </remarks>
    public abstract IAsyncEnumerable<BuildOperation> PlanStageAsync(
        BuildStage stage, IBuildContext context, CancellationToken ct);

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
        progress?.Log("Collecting assets...");

        var sceneGuids = settings.Scenes
            .Where(s => s.Enabled)
            .Select(s => s.SceneGuid)
            .Where(g => g != Guid.Empty)
            .ToList();

        bool depsOnly = settings.AssetMode == AssetExportMode.DependenciesOnly;

        var source = EditorAssetBackend.Instance;
        if (source == null)
            return new AssetCollector.CollectionResult { AllAssets = new(), ResourcesMap = new() };

        return AssetCollector.Collect(source, sceneGuids, depsOnly);
    }

    /// <summary>
    /// A stable order for anything written into a build. A <see cref="HashSet{T}"/> does not promise an
    /// iteration order, so without this the same content lays out differently on every build and a patch
    /// has to ship bytes that never changed.
    /// </summary>
    private protected static IEnumerable<Guid> InBuildOrder(IEnumerable<Guid> assets)
        => assets.OrderBy(g => g);

    /// <summary>
    /// Opens the bytes to ship for one asset, or null when nothing was imported for it.
    /// </summary>
    /// <remarks>
    /// The single point every packaging path reads through, so a pipeline that processes its assets into
    /// a target specific form ships that form by overriding this, rather than every packaging mode having
    /// to know the processed variant exists.
    /// </remarks>
    protected virtual Stream? OpenShippedAsset(Guid guid)
    {
        string path = Path.Combine(Project.Current!.CachePath, $"{guid}.asset");
        return File.Exists(path) ? File.OpenRead(path) : null;
    }

    /// <summary>Copies shipped assets to output as loose files, returning the ones actually written.</summary>
    protected HashSet<Guid> CopyLooseAssets(HashSet<Guid> assets, string outputAssetsDir, BuildProgress? progress,
        CancellationToken ct)
    {
        Directory.CreateDirectory(outputAssetsDir);
        var written = new HashSet<Guid>();

        foreach (var guid in InBuildOrder(assets))
        {
            // Per asset, because a stage is one operation and this loop is most of a large build.
            ct.ThrowIfCancellationRequested();

            using (var source = OpenShippedAsset(guid))
            {
                if (source != null)
                {
                    using var destination = File.Create(Path.Combine(outputAssetsDir, $"{guid}.asset"));
                    source.CopyTo(destination);
                    written.Add(guid);
                }
            }

            if (written.Count % 50 == 0)
                progress?.Log($"Copying assets... ({written.Count}/{assets.Count})");
        }

        return written;
    }

    /// <summary>
    /// Zip stores a modified time per entry, and by default that is the source file's. Two machines with
    /// identical content would then produce different archives, so every entry is stamped with the same
    /// instant. It is the zip format's own minimum, chosen only because it is a constant.
    /// </summary>
    private static readonly DateTimeOffset DeterministicTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Packs each chunk into its own archive, splitting one that exceeds <paramref name="maxSizeMB"/>.
    /// </summary>
    /// <remarks>
    /// One archive per chunk rather than a flat split at a byte ceiling, so an archive corresponds to
    /// something real: a scene, or what several scenes share. That is what a streaming or downloadable
    /// content story needs, and it costs nothing to produce now.
    /// </remarks>
    protected HashSet<Guid> PackAssets(IReadOnlyList<AssetChunk> chunks, string outputAssetsDir, int maxSizeMB,
        BuildProgress? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(outputAssetsDir);

        long maxBytes = (long)maxSizeMB * 1024 * 1024;
        int total = chunks.Sum(c => c.Assets.Count);
        var packed = new HashSet<Guid>();

        foreach (var chunk in chunks)
        {
            int part = 0;
            long size = 0;
            ZipArchive? archive = null;

            try
            {
                foreach (var guid in chunk.Assets)
                {
                    ct.ThrowIfCancellationRequested();

                    using var source = OpenShippedAsset(guid);
                    if (source == null) continue;

                    long length = source.CanSeek ? source.Length : 0;

                    if (archive == null || size + length > maxBytes)
                    {
                        archive?.Dispose();
                        archive = ZipFile.Open(PakPath(outputAssetsDir, chunk.Name, part), ZipArchiveMode.Create);
                        part++;
                        size = 0;
                    }

                    WriteEntry(archive, source, $"{guid}.asset");
                    size += length;
                    packed.Add(guid);

                    if (packed.Count % 50 == 0)
                        progress?.Log($"Packing assets... ({packed.Count}/{total})");
                }
            }
            finally
            {
                archive?.Dispose();
            }
        }

        return packed;
    }

    /// <summary>A single part keeps the plain chunk name, so the common case reads well.</summary>
    private static string PakPath(string directory, string chunk, int part)
        => Path.Combine(directory, part == 0 ? $"{chunk}.prowlpak" : $"{chunk}_{part}.prowlpak");

    private static void WriteEntry(ZipArchive archive, Stream source, string entryName)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        entry.LastWriteTime = DeterministicTimestamp;

        using var destination = entry.Open();
        source.CopyTo(destination);
    }

    public abstract string GetExecutablePath(string outputPath, BuildSettings settings);

    /// <summary>
    /// Whether the machine running the editor can execute what this build produced. Build and Run asks
    /// before launching, because handing a Mach-O binary to a Windows shell only produces a confusing
    /// error, and a cross platform build is a perfectly normal thing to want.
    /// </summary>
    public virtual bool CanRunOnHost(BuildSettings settings) => true;

    internal static string FinalizeDefineString(BuildSettings settings, BuildPipeline pipeline)
    {
        var profile = settings.GetOrCreateProfile(pipeline.GetType());
        var symbols = new List<string>(profile.ScriptingDefineSymbols);

        profile.ModifyDefines(symbols);

        var config = settings.Config;

        // For when profiling will be implemented
        if (config == BuildConfiguration.Debug)
            symbols.Add("PROWL_PROFILING");

        return string.Join(";", symbols.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    /// <summary>
    /// Generate the asset manifest as Echo binary. Only what was actually written is listed, since a
    /// manifest naming a file the build does not contain turns a build problem into a runtime one.
    /// </summary>
    protected void GenerateManifest(string outputPath, HashSet<Guid> assets,
        Dictionary<string, Guid> resourcesMap, Guid defaultSceneGuid)
    {
        var root = EchoObject.NewCompound();
        root["defaultScene"] = new EchoObject(defaultSceneGuid.ToString());

        var assetsTag = EchoObject.NewCompound();
        foreach (var guid in InBuildOrder(assets))
            assetsTag[guid.ToString()] = new EchoObject($"{guid}.asset");
        root["assets"] = assetsTag;

        var resTag = EchoObject.NewCompound();
        foreach (var (path, guid) in resourcesMap.Where(kv => assets.Contains(kv.Value))
                     .OrderBy(kv => kv.Key, StringComparer.Ordinal))
            resTag[path] = new EchoObject(guid.ToString());
        root["resources"] = resTag;

        root.WriteToBinary(new FileInfo(outputPath));
    }

    /// <summary>Export only build-relevant project settings as Echo YAML files.</summary>
    /// <remarks>
    /// Named after the settings type, not the category's display label: see
    /// <see cref="PlayerSettingsFiles"/> for why, and for the list this checks itself against.
    /// </remarks>
    protected void ExportSettings(IBuildContext context, string outputSettingsDir, BuildProgress? progress)
    {
        progress?.Log("Exporting settings...");
        Directory.CreateDirectory(outputSettingsDir);

        var written = new HashSet<string>(StringComparer.Ordinal);

        // Serialize the live (in-memory) settings instances. The in-memory registry is the source
        // of truth at build time. TypeMode.None keeps the output a flat compound keyed by field name
        // so the player (PlayerSettingsLoader) can read it without referencing the settings types.
        foreach (var entry in EditorRegistries.SettingsEntries)
        {
            if (!entry.ExportToBuild) continue;

            try
            {
                EchoObject echo = Serializer.Serialize(entry.Type, entry.Instance, TypeMode.None);
                File.WriteAllText(Path.Combine(outputSettingsDir, $"{entry.Type.Name}.yaml"), echo.WriteToYaml());
                written.Add(entry.Type.Name);
            }
            catch (Exception ex)
            {
                Report(context, "PB2002", $"Failed to export setting '{entry.Name}': {ex.Message}");
            }
        }

        foreach (string expected in Runtime.PlayerSettingsFiles.All.Where(name => !written.Contains(name)))
            Report(context, "PB2003",
                $"The player reads '{expected}.yaml' and nothing exported it. Those settings will fall back to defaults.");
    }

    private static void Report(IBuildContext context, string code, string message)
        => context.Report(new BuildIssue
        {
            Severity = BuildSeverity.Warning,
            Code = code,
            Message = message,
            Stage = BuildStage.ExportSettings,
        });

    /// <summary>
    /// Runs the dotnet CLI, streaming its output and reporting any diagnostic it emits.
    /// </summary>
    /// <param name="onDiagnostic">
    /// Receives each parsed diagnostic with its code, file and line. Diagnostics are recognised by
    /// MSBuild's canonical format rather than by searching for a substring, which is why the process is
    /// pinned to an invariant UI language: the severity words are localised otherwise.
    /// </param>
    protected static async Task<(int exitCode, string stdout, string stderr)> RunDotnetAsync(
        string arguments,
        BuildProgress? progress = null,
        CancellationToken cancellation = default,
        Action<BuildIssue>? onDiagnostic = null,
        BuildStage stage = default)
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

        foreach (var (key, value) in MSBuildDiagnostics.InvariantEnvironment)
            psi.Environment[key] = value;

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

                if (MSBuildDiagnostics.TryParse(line, stage, out var diagnostic))
                {
                    onDiagnostic?.Invoke(diagnostic);
                    progress?.Log(line, diagnostic.Severity == BuildSeverity.Error
                        ? Runtime.LogSeverity.Error
                        : Runtime.LogSeverity.Warning);
                }
                else
                {
                    progress?.Log(line, line.Contains("Build succeeded", StringComparison.Ordinal)
                        ? Runtime.LogSeverity.Success
                        : Runtime.LogSeverity.Normal);
                }
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

        try
        {
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellation).ConfigureAwait(false);
        }
        finally
        {
            // On cancellation the awaits above throw before we get here, so the kill must live in a
            // finally - otherwise the dotnet/MSBuild process tree is orphaned and keeps running.
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
        }

        return (process.ExitCode, stdoutBuilder.ToString(), stderrBuilder.ToString());
    }

}
