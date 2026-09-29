// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Prowl.Echo;
using Prowl.Editor.Projects;
using Prowl.Runtime;

namespace Prowl.Editor.Migration;

/// <summary>
/// Brings a project saved with an older version up to <see cref="Project.CurrentVersion"/>. Everything that
/// converts assets from an old format lives here, and all of it goes away at 1.0.
/// </summary>
public static class ProjectMigration
{
    // Importers whose source file is Echo text, so its references can be rewritten.
    private static readonly HashSet<string> s_textImporters =
    [
        "MaterialImporter", "PrefabImporter", "SceneImporter", "CustomAssetImporter", "InputActionMapImporter",
        "AudioMixerImporter", "TerrainDataImporter", "RenderTextureImporter", "MeshImporter",
    ];

    private const string BinaryImporter = "NavMeshDataImporter";

    /// <summary> Converts every asset file and import setting, drops the import caches so everything reimports, and marks the project current. </summary>
    public static void Migrate(Project project)
    {
        int converted = 0;
        foreach (string metaPath in Directory.EnumerateFiles(project.AssetsPath, "*.meta", SearchOption.AllDirectories).ToList())
        {
            MetaFileData meta = MetaFile.Read(metaPath);
            if (meta.Settings != null && Convert(meta.Settings) is { } settings)
            {
                meta.Settings = settings;
                MetaFile.Write(metaPath, meta);
                converted++;
            }

            string assetPath = metaPath[..^".meta".Length];
            if (File.Exists(assetPath) && MigrateAsset(assetPath, meta.ImporterType))
                converted++;
        }

        foreach (string path in Directory.EnumerateFiles(project.AssetsPath, "*.*", SearchOption.AllDirectories))
            if ((path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".glsl", StringComparison.OrdinalIgnoreCase))
                && MigrateShader(path))
                converted++;

        DeleteDirectory(project.CachePath);
        DeleteDirectory(project.ThumbnailsPath);
        if (File.Exists(project.MetadataDbPath)) File.Delete(project.MetadataDbPath);

        project.MarkCurrent();
        Debug.Log($"Migrated '{project.Name}' to {Project.CurrentVersion}, {converted} files converted.");
    }

    private static readonly Regex s_fragmentInclude = new(@"#include\s+""Fragment""");

    // The engine's Fragment include was renamed to ProwlCG.
    private static bool MigrateShader(string path)
    {
        string source = File.ReadAllText(path);
        string result = s_fragmentInclude.Replace(source, "#include \"ProwlCG\"");
        if (result == source) return false;
        File.WriteAllText(path, result);
        return true;
    }

    private static bool MigrateAsset(string path, string importer)
    {
        // Older meta files name the importer with its namespace.
        importer = importer[(importer.LastIndexOf('.') + 1)..];
        bool binary = importer == BinaryImporter;
        if (!binary && !s_textImporters.Contains(importer)) return false;

        EchoObject echo = binary ? EchoObject.ReadFromBinary(new FileInfo(path)) : EchoObject.ReadFromString(File.ReadAllText(path));
        if (Convert(echo) is not { } result) return false;

        if (binary) result.WriteToBinary(new FileInfo(path));
        else File.WriteAllText(path, result.WriteToString());
        return true;
    }

    /// <summary> The converted tree, or null when nothing in it is in an old format. </summary>
    public static EchoObject? Convert(EchoObject echo)
    {
        bool changed = false;
        EchoObject result = ConvertTag(echo, ref changed);
        return changed ? result : null;
    }

    // An asset reference used to be {"AssetID": guid}, with the asset inline under "Instance" when it had no guid.
    // It is now {"$asset": guid}. An asset copied inline used to carry its AssetID and AssetPath, which it no longer does.
    private static EchoObject ConvertTag(EchoObject tag, ref bool changed)
    {
        if (tag.TagType == EchoType.List)
        {
            for (int i = 0; i < tag.List.Count; i++)
            {
                EchoObject item = tag.List[i];
                EchoObject converted = ConvertTag(item, ref changed);
                if (!ReferenceEquals(item, converted)) tag[i] = converted;
            }
            return tag;
        }

        if (tag.TagType != EchoType.Compound) return tag;

        foreach (string name in tag.GetNames().ToList())
        {
            EchoObject child = tag[name];
            EchoObject converted = ConvertTag(child, ref changed);
            if (!ReferenceEquals(child, converted)) tag[name] = converted;
        }

        if (!tag.TryGet("AssetID", out EchoObject? id)) return tag;
        changed = true;

        List<string> content = tag.GetNames().Where(n => n is not ("AssetID" or "$type" or "$id")).ToList();
        if (content.Count == 0 || (content.Count == 1 && content[0] == "Instance"))
        {
            if (Guid.TryParse(id!.StringValue, out Guid guid) && guid != Guid.Empty)
            {
                var stub = EchoObject.NewCompound();
                stub["$asset"] = new EchoObject(guid.ToString());
                return stub;
            }

            if (!tag.TryGet("Instance", out EchoObject? instance)) return new EchoObject(EchoType.Null, null);
            tag.Remove("Instance");
            return instance!;
        }

        tag.Remove("AssetID");
        tag.Remove("AssetPath");
        return tag;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }
}
