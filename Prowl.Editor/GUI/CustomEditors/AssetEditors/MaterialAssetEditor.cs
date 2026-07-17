using System;
using System.IO;

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
    private bool _dirty;

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        // Detect asset change reset dirty flag and force preview refresh
        if (_currentGuid != entry.Guid)
        {
            _currentGuid = entry.Guid;
            _dirty = false;
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
                _dirty = true;
                _preview.Invalidate();
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
                    onChanged: () => { _dirty = true; _preview.Invalidate(); });
            }

        }

        // Save button writes material to disk then reimports
        if (_dirty)
        {
            Origami.Separator(paper, $"{id}_sep_save").Show();
            Origami.Button(paper, $"{id}_save", $"{EditorIcons.FloppyDisk}  Save Material", () => { SaveMaterial(material, entry); }).Show();
        }

        // 3D Preview
                Origami.Header(paper, $"{id}_h_preview", "Preview").Underline().Show();

        _preview.Get(material, p => p.SetupForMaterial(material)).DrawPreview(paper, $"{id}_preview", 256, 256);
    }

    private void SaveMaterial(Material material, AssetEntry entry)
    {
        if (Project.Current == null) return;

        string absolutePath = Path.Combine(Project.Current.AssetsPath, entry.Path);
        try
        {
            // Temporarily clear AssetID so the serializer writes the full object
            // instead of just an $assetId reference
            var savedId = material.AssetID;
            material.AssetID = Guid.Empty;

            var echo = Serializer.Serialize(typeof(object), material);
            material.AssetID = savedId;

            if (echo != null)
            {
                File.WriteAllText(absolutePath, echo.WriteToString());
                _dirty = false;
                _preview.Invalidate();

                // Reimport to update cache + thumbnail
                EditorAssetBackend.Instance?.Reimport(entry.Guid);
            }
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"Failed to save material: {ex.Message}");
        }
    }
}
