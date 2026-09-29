// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

using Prowl.Echo;
using Prowl.Editor.Projects;
using Prowl.Runtime;

namespace Prowl.Editor.Migration;

/// <summary>
/// Brings a project saved with an older engine version up to <see cref="Project.CurrentVersion"/>.
/// Every format change is a <see cref="MigrationStep"/> listed by the <see cref="MigrationRelease"/> it shipped in,
/// and the steps run in version order, so a project from any older version walks up one step at a time.
/// </summary>
public static class ProjectMigration
{
    private static MigrationRelease[]? s_releases;

    /// <summary> Every release with migration steps, oldest first. </summary>
    public static IReadOnlyList<MigrationRelease> Releases => s_releases ??= typeof(MigrationRelease).Assembly.GetTypes()
        .Where(t => t.IsSubclassOf(typeof(MigrationRelease)) && !t.IsAbstract)
        .Select(t => (MigrationRelease)Activator.CreateInstance(t)!)
        .OrderBy(r => r.Version)
        .ToArray();

    /// <summary> How many steps the release for this exact version has, zero when it has none. </summary>
    public static int StepCount(EngineVersion version) => Releases.FirstOrDefault(r => r.Version == version)?.Steps.Length ?? 0;

    /// <summary> The steps a project still needs, in the order they run. </summary>
    public static List<(MigrationRelease Release, int Index)> PendingSteps(Project project)
    {
        var pending = new List<(MigrationRelease, int)>();
        foreach (MigrationRelease release in Releases)
        {
            if (release.Version < project.Version || release.Version > Project.CurrentVersion) continue;
            int start = release.Version == project.Version ? project.AppliedSteps : 0;
            for (int i = start; i < release.Steps.Length; i++)
                pending.Add((release, i));
        }
        return pending;
    }

    /// <summary>
    /// Backs the project up, runs every pending step, then drops the import caches so everything reimports.
    /// The project records each step as it finishes, so a failed migration resumes from the step that failed.
    /// </summary>
    public static void Migrate(Project project)
    {
        if (project.IsFromNewerEngine)
            throw new InvalidOperationException($"'{project.Name}' was saved with {project.Version}, which is newer than {Project.CurrentVersion}.");

        List<(MigrationRelease Release, int Index)> pending = PendingSteps(project);
        if (pending.Count > 0)
        {
            string backup = Backup(project);
            Debug.Log($"Backed up '{project.Name}' to {backup}");

            var context = new MigrationContext(project);
            foreach ((MigrationRelease release, int index) in pending)
            {
                MigrationStep step = release.Steps[index];
                int before = context.ChangedFiles.Count;
                step.Run(context);
                project.MarkMigrated(release.Version, index + 1);
                Debug.Log($"Migration {release.Version} '{step.Name}': {context.ChangedFiles.Count - before} files changed.");
            }

            DeleteDirectory(project.CachePath);
            DeleteDirectory(project.ThumbnailsPath);
            if (File.Exists(project.MetadataDbPath)) File.Delete(project.MetadataDbPath);
        }

        project.MarkCurrent();
        Debug.Log($"Migrated '{project.Name}' to {Project.CurrentVersion}, {pending.Count} steps.");
    }

    /// <summary> Zips everything a migration can touch into the project's Backups folder and returns the zip path. </summary>
    public static string Backup(Project project)
    {
        Directory.CreateDirectory(project.BackupsPath);
        string version = project.Version.IsUnknown ? "unknown" : project.Version.ToString();
        string zipPath = Path.Combine(project.BackupsPath, $"{project.Name}_{version}_{DateTime.Now:yyyyMMdd_HHmmss}.zip");

        using ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (string folder in new[] { project.AssetsPath, project.ProjectSettingsPath, project.PackagesPath })
            if (Directory.Exists(folder))
                foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                    zip.CreateEntryFromFile(file, Path.GetRelativePath(project.RootPath, file).Replace('\\', '/'));

        foreach (string file in Directory.EnumerateFiles(project.RootPath, "*.prowl").Append(Path.Combine(project.RootPath, "Directory.Build.props")))
            if (File.Exists(file))
                zip.CreateEntryFromFile(file, Path.GetFileName(file));

        return zipPath;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
}

/// <summary> The format changes one engine release made, in the order they were made. </summary>
public abstract class MigrationRelease
{
    private EngineVersion? _version;

    /// <summary> The release these steps shipped in, like "1.0" or "1.1a". </summary>
    public abstract string Name { get; }
    public EngineVersion Version => _version ??= EngineVersion.Parse(Name);

    /// <summary> Append only. A step that has shipped is never edited, reordered or removed. </summary>
    public abstract MigrationStep[] Steps { get; }
}

/// <summary>
/// One format change. It works on raw files and Echo trees, never on engine types, since those keep changing after
/// the step is written. It may run again on files it already converted when a migration is resumed.
/// </summary>
public sealed record MigrationStep(string Name, Action<MigrationContext> Run);

/// <summary> What a step works on, and helpers that walk the project's files for it. </summary>
public sealed class MigrationContext
{
    // Importers whose source file is Echo text or binary. Old meta files name them with their namespace.
    private static readonly HashSet<string> s_echoTextImporters =
    [
        "MaterialImporter", "PrefabImporter", "SceneImporter", "CustomAssetImporter", "InputActionMapImporter",
        "AudioMixerImporter", "TerrainDataImporter", "RenderTextureImporter", "MeshImporter",
    ];
    private static readonly HashSet<string> s_echoBinaryImporters = ["NavMeshDataImporter"];

    public Project Project { get; }
    public HashSet<string> ChangedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public MigrationContext(Project project) => Project = project;

    /// <summary> Runs convert on every Echo asset file, every meta file's import settings and every project settings file. Convert returns null to leave one alone. </summary>
    public void RewriteEcho(Func<EchoObject, EchoObject?> convert)
    {
        RewriteEchoAssets(convert);
        RewriteMetaSettings(convert);
        RewriteProjectSettings(convert);
    }

    public void RewriteEchoAssets(Func<EchoObject, EchoObject?> convert)
    {
        foreach (string metaPath in MetaFiles())
        {
            string importer = ShortName(MetaFile.Read(metaPath).ImporterType);
            bool binary = s_echoBinaryImporters.Contains(importer);
            if (!binary && !s_echoTextImporters.Contains(importer)) continue;

            string path = metaPath[..^".meta".Length];
            if (!File.Exists(path)) continue;

            EchoObject echo = binary ? EchoObject.ReadFromBinary(new FileInfo(path)) : EchoObject.ReadFromString(File.ReadAllText(path));
            if (convert(echo) is not { } result) continue;

            if (binary) result.WriteToBinary(new FileInfo(path));
            else File.WriteAllText(path, result.WriteToString());
            ChangedFiles.Add(path);
        }
    }

    public void RewriteMetaSettings(Func<EchoObject, EchoObject?> convert)
    {
        foreach (string metaPath in MetaFiles())
        {
            MetaFileData meta = MetaFile.Read(metaPath);
            if (meta.Settings == null || convert(meta.Settings) is not { } settings) continue;
            meta.Settings = settings;
            MetaFile.Write(metaPath, meta);
            ChangedFiles.Add(metaPath);
        }
    }

    public void RewriteProjectSettings(Func<EchoObject, EchoObject?> convert)
    {
        if (!Directory.Exists(Project.ProjectSettingsPath)) return;
        foreach (string path in Directory.EnumerateFiles(Project.ProjectSettingsPath, "*.yaml"))
        {
            if (convert(EchoObject.ReadFromYaml(File.ReadAllText(path))) is not { } result) continue;
            File.WriteAllText(path, result.WriteToYaml());
            ChangedFiles.Add(path);
        }
    }

    /// <summary> Runs convert on the text of every asset with one of the extensions, like ".shader". </summary>
    public void RewriteText(Func<string, string> convert, params string[] extensions)
    {
        foreach (string path in Directory.EnumerateFiles(Project.AssetsPath, "*.*", SearchOption.AllDirectories))
        {
            if (!extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase))) continue;
            string source = File.ReadAllText(path);
            string result = convert(source);
            if (result == source) continue;
            File.WriteAllText(path, result);
            ChangedFiles.Add(path);
        }
    }

    private IEnumerable<string> MetaFiles() => Directory.EnumerateFiles(Project.AssetsPath, "*.meta", SearchOption.AllDirectories);

    private static string ShortName(string typeName) => typeName[(typeName.LastIndexOf('.') + 1)..];
}

/// <summary>
/// An engine release like "1.0-preview.5", "1.0", "1.1" or the hotfix "1.1a". Previews sort before their release,
/// hotfixes after it. The default value is the unknown version of a project saved before versions were written.
/// </summary>
public readonly record struct EngineVersion : IComparable<EngineVersion>
{
    private static readonly Regex s_pattern = new(@"^(\d+)\.(\d+)([a-z]*)(?:-preview\.([1-9]\d*))?$");
    private static readonly Regex s_legacyPreview = new(@"^preview-([1-9]\d*)$");

    private readonly string? _hotfix;

    public int Major { get; }
    public int Minor { get; }
    /// <summary> The hotfix letters, empty for the release itself. </summary>
    public string Hotfix => _hotfix ?? "";
    /// <summary> The preview number, zero for the release itself. </summary>
    public int Preview { get; }

    public EngineVersion(int major, int minor, string hotfix = "", int preview = 0)
    {
        Major = major;
        Minor = minor;
        _hotfix = hotfix.Length > 0 ? hotfix : null;
        Preview = preview;
    }

    public bool IsUnknown => this == default;

    public static EngineVersion Parse(string text)
        => TryParse(text, out EngineVersion version) ? version : throw new FormatException($"'{text}' is not an engine version.");

    /// <summary> Empty text and the "0.0.1" older editors always wrote are the unknown version. The old "preview-5" style reads as "1.0-preview.5". </summary>
    public static bool TryParse(string? text, out EngineVersion version)
    {
        version = default;
        if (string.IsNullOrEmpty(text) || text == "0.0.1") return true;

        if (s_legacyPreview.Match(text) is { Success: true } legacy)
        {
            version = new EngineVersion(1, 0, "", int.Parse(legacy.Groups[1].Value));
            return true;
        }

        Match match = s_pattern.Match(text);
        if (!match.Success) return false;
        version = new EngineVersion(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), match.Groups[3].Value,
            match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 0);
        return true;
    }

    public int CompareTo(EngineVersion other)
    {
        if (Major != other.Major) return Major.CompareTo(other.Major);
        if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
        if (Hotfix.Length != other.Hotfix.Length) return Hotfix.Length.CompareTo(other.Hotfix.Length);
        int hotfix = string.CompareOrdinal(Hotfix, other.Hotfix);
        if (hotfix != 0) return hotfix;
        return PreviewRank.CompareTo(other.PreviewRank);
    }

    private int PreviewRank => Preview == 0 ? int.MaxValue : Preview;

    public static bool operator <(EngineVersion a, EngineVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(EngineVersion a, EngineVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(EngineVersion a, EngineVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(EngineVersion a, EngineVersion b) => a.CompareTo(b) >= 0;

    public override string ToString() => IsUnknown ? "" : $"{Major}.{Minor}{Hotfix}" + (Preview > 0 ? $"-preview.{Preview}" : "");
}
