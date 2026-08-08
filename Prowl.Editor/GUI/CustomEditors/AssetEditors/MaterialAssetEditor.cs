using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Editor.GUI;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using PropertyGridUtils = Prowl.Editor.GUI.PropertyGridUtils;
using Prowl.Editor.GUI.PropertyEditors;
using Prowl.Editor.Theming;
using Prowl.Editor.Projects;
namespace Prowl.Editor.Inspector;

[CustomAssetEditor(typeof(Material))]
public class MaterialAssetEditor : AssetImporterEditor
{
    private readonly PreviewWidget _preview = new();
    private Guid _currentGuid;

    // Materials with edits not yet written to disk, keyed by asset GUID. Static rather than
    // per-instance to have a behaviour more coherent with user expectations
    private static readonly Dictionary<Guid, (Material Material, AssetEntry Entry)> _pending = new();

    /// <summary>Used only to reach the instance members from the global save hook.</summary>
    private static readonly MaterialAssetEditor s_saveHook = new();

    static MaterialAssetEditor()
    {
        SaveManager.OnSave += () => s_saveHook.SavePending(showToast: false);
    }

    // ============================================================
    // Pending changes
    // ============================================================
    // A material is edited live, so the scene shows every tweak as it happens. What the base class
    // measures is whether the live object still matches the .mat file on disk.

    protected override EchoObject? CaptureState(AssetEntry entry, EngineObject? asset)
        => asset is Material material && material.IsValid()
            ? Serializer.Serialize(typeof(Material), material)
            : null;

    protected override bool ApplyState(AssetEntry entry, EngineObject? asset)
    {
        if (asset is not Material material || material.IsNotValid()) return false;
        if (!Write(material, entry)) return false; // a failed write was logged and stays pending

        _pending.Remove(entry.Guid);
        EditorAssetBackend.Instance?.Reimport(entry.Guid);
        return true;
    }

    protected override void RevertState(AssetEntry entry, EngineObject? asset, EchoObject baseline)
    {
        if (asset is not Material material || material.IsNotValid()) return;

        // Restored onto the live instance rather than swapped for a fresh one, so everything already
        // referencing this material shows the revert immediately and keeps its GPU state.
        Serializer.DeserializeInto(baseline, material);

        _pending.Remove(entry.Guid);
        _preview.Invalidate();
    }

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        // Detect asset change and force a preview refresh.
        if (_currentGuid != entry.Guid)
        {
            _currentGuid = entry.Guid;
            _preview.Invalidate();
        }

        // Include the GUID in element IDs so Paper UI state is unique per asset
        id = $"{id}_{entry.Guid:N}";

        var material = asset as Material;

        Origami.Header(paper, $"{id}_h_info", $"{EditorIcons.Palette}  Material").Show();
        Origami.Label(paper, $"{id}_path", $"Path: {entry.Path}").Show();

        if (material == null) return;

        // Shader reference
        Origami.Separator(paper, $"{id}_sep_shader").Show();
        PropertyGridUtils.DrawField(paper, $"{id}_shader", "Shader", typeof(AssetRef<Shader>), material.ShaderRef,
            newVal =>
            {
                material.ShaderRef = (AssetRef<Shader>)newVal!;
                MarkDirty(material, entry);
            }, 0);

        // Shader properties one field per property declared by the shader. Values
        // are read live from the shader for non-overridden entries (see
        // DrawShaderProperty), so changes to defaults in the shader graph propagate
        // immediately no SyncShaderDefaults call needed.
        var shader = material.Shader;
        if (shader != null)
        {
            Origami.Header(paper, $"{id}_h_props", "Properties").Underline().Show();

            foreach (var prop in shader.Properties)
            {
                MaterialPropertyDrawer.DrawPropertyRow(paper, $"{id}_p_{prop.Name}", material, prop,
                    onChanged: () => MarkDirty(material, entry));
            }

        }

        // Save button writes material to disk then reimports. Shown when the live material actually
        // differs from its file, not merely because it was touched at some point.
        if (HasPendingChanges(entry, asset))
        {
            Origami.Separator(paper, $"{id}_sep_save").Show();
            Origami.Button(paper, $"{id}_save", $"{EditorIcons.FloppyDisk}  Save Material",
                () => SavePending(showToast: true)).Show();
        }

        // 3D Preview
                Origami.Header(paper, $"{id}_h_preview", "Preview").Underline().Show();

        _preview.Get(material, p => p.SetupForMaterial(material)).DrawPreview(paper, $"{id}_preview", 256, 256);
    }

    /// <summary>Record that <paramref name="material"/> has edits not yet written to disk.</summary>
    private void MarkDirty(Material material, AssetEntry entry)
    {
        _pending[entry.Guid] = (material, entry);
        _preview.Invalidate();
    }

    /// <summary>
    /// Write every pending material to disk.
    /// </summary>
    private string? SavePending(bool showToast)
    {
        if (_pending.Count == 0) return null;

        var db = EditorAssetBackend.Instance;
        if (db == null || Project.Current == null) return null;

        var names = new List<string>();

        foreach (var (guid, pending) in _pending.ToArray())
        {
            var (material, entry) = pending;

            // Pending edits that can never be written- drop them instead.
            if (material.IsNotValid() || !ReferenceEquals(db.GetEntry(entry.Guid), entry))
            {
                _pending.Remove(guid);
                continue;
            }

            // Touched at some point but since put back to what the file already holds.
            if (!HasPendingChanges(entry, material))
            {
                _pending.Remove(guid);
                continue;
            }

            ApplyPendingChanges(entry, material);

            // A failed write stays pending (and was logged) so the next save retries it rather than
            // silently discarding the user's edits.
            if (_pending.ContainsKey(guid)) continue;

            names.Add(Path.GetFileNameWithoutExtension(entry.Path));
        }

        if (names.Count == 0) return null;

        names.Sort(StringComparer.OrdinalIgnoreCase);
        string label = string.Join(", ", names);

        if (showToast)
            Toasts.Success(Prowl.Rosetta.Loc.Get("save.saved"), label);

        return label;
    }

    /// <summary>Serialize one material over its .mat file. Returns false (and logs) on failure.</summary>
    private static bool Write(Material material, AssetEntry entry)
    {
        string absolutePath = Path.Combine(Project.Current!.AssetsPath, entry.Path);
        try
        {
            EchoObject? echo;

            // Temporarily clear AssetID so the serializer writes the full object
            // instead of just an $assetId reference
            var savedId = material.AssetID;
            material.AssetID = Guid.Empty;
            try { echo = Serializer.Serialize(typeof(object), material); }
            finally { material.AssetID = savedId; }

            if (echo == null) return false;
            File.WriteAllText(absolutePath, echo.WriteToString());
            return true;
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"Failed to save material '{entry.Path}': {ex.Message}");
            return false;
        }
    }
}
