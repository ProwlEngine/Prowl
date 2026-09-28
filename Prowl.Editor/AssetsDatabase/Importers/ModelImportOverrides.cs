// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.AssetImporting;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Importers;

/// <summary>The import settings blocks the model inspector writes and the importer reads back.</summary>
public static class ModelImportKeys
{
    /// <summary>Per clip settings, keyed by the clip's name in the file.</summary>
    public const string Clips = "clips";

    /// <summary>Material name to the GUID of the asset it was extracted into.</summary>
    public const string MaterialRemap = "materialRemap";

    /// <summary>Humanoid bone name to the skeleton bone playing it, as set in the Avatar editor.</summary>
    public const string HumanoidMap = "humanoidMap";

    public const string ClipName = "name";
    public const string ClipLoop = "loop";
    public const string ClipTrimStart = "trimStart";
    public const string ClipTrimEnd = "trimEnd";
    public const string ClipRootTravel = "rootTravel";
    public const string ClipRootTurn = "rootTurn";
    public const string ClipRootHeight = "rootHeight";
    public const string ClipEvents = "events";
}

/// <summary>
/// Points a material slot at an asset extracted out of the model. A reference that no longer resolves
/// falls back to the embedded material.
/// </summary>
public sealed class ExtractedMaterialResolver : IModelMaterialResolver
{
    private readonly Dictionary<string, Guid> _remap = new();

    public ExtractedMaterialResolver(EchoObject settings)
    {
        if (!settings.TryGet(ModelImportKeys.MaterialRemap, out EchoObject remap)) return;

        foreach (KeyValuePair<string, EchoObject> entry in remap.Tags)
            if (Guid.TryParse(entry.Value.StringValue, out Guid guid) && guid != Guid.Empty)
                _remap[entry.Key] = guid;
    }

    public AssetRef<Material> Resolve(string materialName)
    {
        if (!_remap.TryGetValue(materialName, out Guid guid)) return default;
        if (EditorAssetBackend.Instance?.GuidToPath(guid) == null)
        {
            Debug.LogWarning($"[Model] The extracted material '{materialName}' is missing, so the import rebuilt it. Extract it again to point at an asset.");
            return default;
        }
        return new AssetRef<Material>(guid);
    }
}

/// <summary>Reads and writes the per clip and per material blocks of a model's import settings.</summary>
public static class ModelImportOverrides
{
    public static Dictionary<string, ModelClipSettings>? ReadClips(EchoObject settings)
    {
        if (!settings.TryGet(ModelImportKeys.Clips, out EchoObject clips) || clips.Tags.Count == 0) return null;

        var result = new Dictionary<string, ModelClipSettings>(clips.Tags.Count);
        foreach (KeyValuePair<string, EchoObject> entry in clips.Tags)
        {
            EchoObject value = entry.Value;
            result[entry.Key] = new ModelClipSettings
            {
                Name = value.TryGet(ModelImportKeys.ClipName, out EchoObject n) && !string.IsNullOrWhiteSpace(n.StringValue) ? n.StringValue : null,
                Loop = value.TryGet(ModelImportKeys.ClipLoop, out EchoObject l) ? l.BoolValue : null,
                TrimStart = value.TryGet(ModelImportKeys.ClipTrimStart, out EchoObject ts) ? ts.FloatValue : 0f,
                TrimEnd = value.TryGet(ModelImportKeys.ClipTrimEnd, out EchoObject te) ? te.FloatValue : 0f,
                RootTravel = value.TryGet(ModelImportKeys.ClipRootTravel, out EchoObject rt) ? rt.BoolValue : null,
                RootTurn = value.TryGet(ModelImportKeys.ClipRootTurn, out EchoObject rr) ? rr.BoolValue : null,
                RootHeight = value.TryGet(ModelImportKeys.ClipRootHeight, out EchoObject rh) ? rh.BoolValue : null,
                Events = ReadEvents(value) is { Count: > 0 } events ? events : null,
            };
        }
        return result;
    }

    /// <summary>The block holding one clip's settings, created on first write.</summary>
    public static EchoObject ClipBlock(EchoObject settings, string clipName)
    {
        if (!settings.TryGet(ModelImportKeys.Clips, out EchoObject clips))
        {
            clips = EchoObject.NewCompound();
            settings[ModelImportKeys.Clips] = clips;
        }
        if (!clips.TryGet(clipName, out EchoObject clip))
        {
            clip = EchoObject.NewCompound();
            clips[clipName] = clip;
        }
        return clip;
    }

    /// <summary>A clip's events, timed in seconds of the take. The kind is stored by name.</summary>
    public static List<ClipEvent> ReadEvents(EchoObject? clipBlock)
    {
        var events = new List<ClipEvent>();
        if (clipBlock == null || !clipBlock.TryGet(ModelImportKeys.ClipEvents, out EchoObject list) || list.TagType != EchoType.List)
            return events;

        foreach (EchoObject entry in list.List)
        {
            if (!entry.TryGet("kind", out EchoObject kind) || !Enum.TryParse(kind.StringValue, out ClipEventKind parsed)) continue;
            events.Add(new ClipEvent
            {
                Kind = parsed,
                Time = entry.TryGet("time", out EchoObject t) ? t.FloatValue : 0f,
                Length = entry.TryGet("length", out EchoObject l) ? l.FloatValue : 0f,
                Name = entry.TryGet("name", out EchoObject n) ? n.StringValue : string.Empty,
                Option = entry.TryGet("option", out EchoObject o) ? o.IntValue : 0,
                BlendTime = entry.TryGet("blend", out EchoObject b) ? b.FloatValue : 0.1f,
            });
        }
        return events;
    }

    public static void WriteEvents(EchoObject settings, string clipName, IReadOnlyList<ClipEvent> events)
    {
        EchoObject list = EchoObject.NewList();
        foreach (ClipEvent e in events)
        {
            EchoObject entry = EchoObject.NewCompound();
            entry["kind"] = new EchoObject(e.Kind.ToString());
            entry["time"] = new EchoObject(e.Time);
            entry["length"] = new EchoObject(e.Length);
            entry["name"] = new EchoObject(e.Name);
            entry["option"] = new EchoObject(e.Option);
            entry["blend"] = new EchoObject(e.BlendTime);
            list.ListAdd(entry);
        }
        ClipBlock(settings, clipName)[ModelImportKeys.ClipEvents] = list;
    }

    public static EchoObject? ReadClipBlock(EchoObject settings, string clipName)
        => settings.TryGet(ModelImportKeys.Clips, out EchoObject clips) && clips.TryGet(clipName, out EchoObject clip) ? clip : null;

    public static Dictionary<string, string>? ReadHumanoidMap(EchoObject settings)
    {
        if (!settings.TryGet(ModelImportKeys.HumanoidMap, out EchoObject map) || map.Tags.Count == 0) return null;

        var result = new Dictionary<string, string>(map.Tags.Count);
        foreach (KeyValuePair<string, EchoObject> entry in map.Tags)
            result[entry.Key] = entry.Value.StringValue ?? string.Empty;
        return result;
    }

    /// <summary>Replaces the stored humanoid map. An empty map goes back to the auto mapper.</summary>
    public static void WriteHumanoidMap(EchoObject settings, IReadOnlyDictionary<string, string> map)
    {
        var block = EchoObject.NewCompound();
        foreach (KeyValuePair<string, string> entry in map)
            block[entry.Key] = new EchoObject(entry.Value);
        settings[ModelImportKeys.HumanoidMap] = block;
    }

    public static Guid ExtractedMaterial(EchoObject settings, string materialName)
    {
        if (!settings.TryGet(ModelImportKeys.MaterialRemap, out EchoObject remap)) return Guid.Empty;
        if (!remap.TryGet(materialName, out EchoObject entry)) return Guid.Empty;
        return Guid.TryParse(entry.StringValue, out Guid guid) ? guid : Guid.Empty;
    }

    public static void SetExtractedMaterial(EchoObject settings, string materialName, Guid guid)
    {
        if (!settings.TryGet(ModelImportKeys.MaterialRemap, out EchoObject remap))
        {
            remap = EchoObject.NewCompound();
            settings[ModelImportKeys.MaterialRemap] = remap;
        }

        if (guid == Guid.Empty) remap.Remove(materialName);
        else remap[materialName] = new EchoObject(guid.ToString());
    }

    /// <summary>Writes a material out to a Materials folder beside the model and returns its GUID, or empty on failure.</summary>
    public static Guid ExtractMaterial(Material material, string modelRelativePath, string materialName)
    {
        EditorAssetBackend? backend = EditorAssetBackend.Instance;
        if (backend == null || Projects.Project.Current == null) return Guid.Empty;

        try
        {
            string modelFolder = Path.GetDirectoryName(modelRelativePath)?.Replace('\\', '/') ?? "";
            string relativeFolder = string.IsNullOrEmpty(modelFolder) ? "Materials" : modelFolder + "/Materials";
            string absoluteFolder = Path.Combine(Projects.Project.Current.AssetsPath, relativeFolder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(absoluteFolder);

            string fileName = Utils.UniqueNames.ForFile(absoluteFolder, Sanitize(materialName), ".mat");
            EchoObject echo = Serializer.Serialize(typeof(object), material);
            File.WriteAllText(Path.Combine(absoluteFolder, fileName), echo.WriteToString());

            backend.InvalidateFolderIndex();
            return backend.ImportFile(relativeFolder + "/" + fileName);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Model] Could not extract the material '{materialName}': {ex.Message}");
            return Guid.Empty;
        }
    }

    private static string Sanitize(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(name) ? "Material" : name;
    }
}
