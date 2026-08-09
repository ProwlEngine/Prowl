// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Editor.Projects;
using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;

using Prowl.Editor.GUI;
namespace Prowl.Editor.Inspector;

/// <summary>
/// Inspector for assembly definition (.asmdef) files. Edits the definition stored in the file itself
/// (the source of truth) and triggers a recompile on save.
/// </summary>
[CustomAssetEditor(typeof(AssemblyDefinitionAsset))]
public class AssemblyDefinitionAssetEditor : AssetImporterEditor
{
    /// <summary>The definition being edited, per asset. Static so switching selection and coming back
    /// keeps the edits - a single slot would re-read the file and discard them.</summary>
    private static readonly Dictionary<Guid, AssemblyDefinition> s_edits = new();

    private AssemblyDefinition Edits(AssetEntry entry, string absPath)
    {
        if (s_edits.TryGetValue(entry.Guid, out AssemblyDefinition? existing)) return existing;

        AssemblyDefinition loaded;
        try { loaded = AssemblyDefinition.ReadFromFile(absPath); }
        catch { loaded = new AssemblyDefinition { Name = Path.GetFileNameWithoutExtension(absPath) }; }

        s_edits[entry.Guid] = loaded;
        Rebaseline(entry, null); // the file as loaded is the clean state
        return loaded;
    }

    protected override EchoObject? CaptureState(AssetEntry entry, EngineObject? asset)
        => s_edits.TryGetValue(entry.Guid, out AssemblyDefinition? def)
            ? Serializer.Serialize(typeof(AssemblyDefinition), def)
            : null;

    protected override bool ApplyState(AssetEntry entry, EngineObject? asset)
    {
        if (Project.Current == null) return false;
        if (!s_edits.TryGetValue(entry.Guid, out AssemblyDefinition? def)) return false;

        try
        {
            def.WriteToFile(Path.Combine(Project.Current.AssetsPath, entry.Path));
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"Failed to save assembly definition '{entry.Path}': {ex.Message}");
            return false;
        }

        EditorAssetBackend.Instance?.Reimport(entry.Guid);
        ScriptAssemblyManager.RequestRecompile();
        return true;
    }

    protected override void RevertState(AssetEntry entry, EngineObject? asset, EchoObject baseline)
    {
        if (s_edits.TryGetValue(entry.Guid, out AssemblyDefinition? def))
            Serializer.DeserializeInto(baseline, def);
    }

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        if (Project.Current == null) return;

        string absPath = Path.Combine(Project.Current.AssetsPath, entry.Path);
        if (!File.Exists(absPath)) return;

        AssemblyDefinition def = Edits(entry, absPath);

        Origami.Header(paper, $"{id}_hdr", $"{EditorIcons.FileLines}  Assembly Definition").Show();

        EditorGUI.Row(paper, $"{id}_name", "Name", () =>
            Origami.TextField(paper, $"{id}_name_v", def.Name, v => def.Name = v).Show());

        Origami.Checkbox(paper, $"{id}_unsafe", def.AllowUnsafeCode, v => def.AllowUnsafeCode = v)
            .LabelRight("Allow Unsafe Code").Show();
        Origami.Checkbox(paper, $"{id}_auto", def.AutoReferenced, v => def.AutoReferenced = v)
            .LabelRight("Auto Referenced").Show();
        Origami.Checkbox(paper, $"{id}_noeng", def.NoEngineReferences, v => def.NoEngineReferences = v)
            .LabelRight("No Engine References").Show();

        // References to other assembly definitions (checkbox per discovered asmdef).
        paper.Box($"{id}_sp1").Height(6);
        Origami.Header(paper, $"{id}_refs_hdr", "Assembly References").Show();

        var others = AssemblyDefinitionDatabase.LoadAll(Project.Current)
            .Select(a => a.Name)
            .Where(n => !n.Equals(def.Name, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();

        if (others.Count == 0)
            Origami.Label(paper, $"{id}_refs_none", "No other assembly definitions in project.").Show();

        foreach (var name in others)
        {
            bool referenced = def.References.Any(r => r.Equals(name, StringComparison.OrdinalIgnoreCase));
            Origami.Checkbox(paper, $"{id}_ref_{name}", referenced, v =>
                {
                    def.References.RemoveAll(r => r.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (v) def.References.Add(name);
                })
                .LabelRight(name).Show();
        }

        // Platform inclusion. "Any Platform" = no include/exclude lists; otherwise the checked set
        // becomes the include list.
        paper.Box($"{id}_sp2").Height(6);
        Origami.Header(paper, $"{id}_plat_hdr", "Platforms").Show();

        bool anyPlatform = def.IncludePlatforms.Count == 0 && def.ExcludePlatforms.Count == 0;
        Origami.Checkbox(paper, $"{id}_any", anyPlatform, v =>
            {
                def.ExcludePlatforms.Clear();
                if (v) def.IncludePlatforms.Clear();
                else if (def.IncludePlatforms.Count == 0) def.IncludePlatforms.AddRange(BuildPlatforms.All);
            })
            .LabelRight("Any Platform").Show();

        if (!anyPlatform)
        {
            foreach (var platform in BuildPlatforms.All)
                IncludeToggle(paper, $"{id}_inc_{platform}", platform, def);
        }

        paper.Box($"{id}_sp3").Height(8);
        DrawApplyRevertBar(paper, id, entry, asset);
    }

    private static void IncludeToggle(Paper paper, string id, string platform, AssemblyDefinition def)
    {
        bool included = def.IncludePlatforms.Any(p => p.Equals(platform, StringComparison.OrdinalIgnoreCase));
        Origami.Checkbox(paper, id, included, v =>
            {
                def.IncludePlatforms.RemoveAll(p => p.Equals(platform, StringComparison.OrdinalIgnoreCase));
                if (v) def.IncludePlatforms.Add(platform);
            })
            .LabelRight(platform).Show();
    }
}
