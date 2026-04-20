using System;
using System.IO;

using Prowl.Echo;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Rendering.Shaders;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Color = System.Drawing.Color;
using VColor = Prowl.Vector.Color;

namespace Prowl.Editor.Inspector;

[CustomAssetEditor(typeof(Material))]
public class MaterialAssetEditor : AssetImporterEditor
{
    private PreviewRenderer? _preview;
    private EngineObject? _lastPreviewAsset;
    private Guid _currentGuid;
    private bool _dirty;

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        // Detect asset change — reset dirty flag and force preview refresh
        if (_currentGuid != entry.Guid)
        {
            _currentGuid = entry.Guid;
            _dirty = false;
            _lastPreviewAsset = null;
        }

        // Include the GUID in element IDs so Paper UI state is unique per asset
        id = $"{id}_{entry.Guid:N}";

        var font = EditorTheme.DefaultFont;
        if (font == null) return;
        var material = asset as Material;

        EditorGUI.Header(paper, $"{id}_h_info", $"{EditorIcons.Palette}  Material");
        EditorGUI.Label(paper, $"{id}_path", $"Path: {entry.Path}");

        if (material == null) return;

        // Shader reference
        EditorGUI.Separator(paper, $"{id}_sep_shader");
        EngineObjectPropertyEditor.SetFieldType(typeof(Shader));
        PropertyGrid.DrawField(paper, $"{id}_shader", "Shader", typeof(Shader), material.Shader,
            newVal =>
            {
                if (newVal is Shader s)
                {
                    material.Shader = s;
                    _dirty = true;
                    _lastPreviewAsset = null; // force preview refresh
                }
            }, 0);

        // Shader properties — one field per property declared by the shader. Values
        // are read live from the shader for non-overridden entries (see
        // DrawShaderProperty), so changes to defaults in the shader graph propagate
        // immediately — no SyncShaderDefaults call needed.
        var shader = material.Shader;
        if (shader != null)
        {
            EditorGUI.Separator(paper, $"{id}_sep_props");
            EditorGUI.Header(paper, $"{id}_h_props", "Properties");

            foreach (var prop in shader.Properties)
            {
                DrawShaderPropertyRow(paper, $"{id}_p_{prop.Name}", material, prop);
            }

        }

        // Save button — writes material to disk then reimports
        if (_dirty)
        {
            EditorGUI.Separator(paper, $"{id}_sep_save");
            EditorGUI.Button(paper, $"{id}_save", $"{EditorIcons.FloppyDisk}  Save Material")
                .OnValueChanged(_ => SaveMaterial(material, entry));
        }

        // 3D Preview
        EditorGUI.Separator(paper, $"{id}_sep_preview");
        EditorGUI.Header(paper, $"{id}_h_preview", "Preview");

        _preview ??= new PreviewRenderer(256, 256);
        if (_lastPreviewAsset != material)
        {
            _lastPreviewAsset = material;
            _preview.SetupForMaterial(material);
        }

        _preview.DrawPreview(paper, $"{id}_preview", 256, 256);
    }

    /// <summary>Wrap a property in a row that adds an override indicator on the left
    /// and a revert-to-default button on the right when the field has been overridden.
    /// The actual field draws inside via <see cref="DrawShaderProperty"/>.</summary>
    private void DrawShaderPropertyRow(Paper paper, string id, Material material, ShaderProperty prop)
    {
        bool overridden = material.IsOverridden(prop.Name);

        using (paper.Row($"{id}_row")
            .Height(EditorTheme.RowHeight)
            .Margin(0, EditorTheme.Spacing)
            .Enter())
        {
            // Left marker: thin vertical bar in the purple theme color when overridden.
            // Always present (transparent when not overridden) so widths line up.
            paper.Box($"{id}_marker")
                .Width(3).Height(EditorTheme.RowHeight - 4)
                .Margin(2, 4, 2, 2)
                .BackgroundColor(overridden ? EditorTheme.Purple400 : System.Drawing.Color.Transparent)
                .Rounded(1.5f);

            // The actual field — fills the remaining row width.
            using (paper.Box($"{id}_field").Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight).Enter())
            {
                DrawShaderProperty(paper, id, material, prop);
            }

            // Right-side revert button — only shown when the property is overridden.
            if (overridden)
            {
                EditorGUI.Button(paper, $"{id}_revert", EditorIcons.ArrowRotateLeft, width: 24)
                    .OnValueChanged(_ =>
                    {
                        material.RevertProperty(prop.Name);
                        // Drop the cached entry so the live shader default takes over
                        // immediately. Without this, the override flag is gone but the
                        // stored value is still in _properties (and would be re-uploaded
                        // by ApplyMaterialUniformsWithDefaults).
                        material._properties.RemoveProperty(prop.Name);
                        _dirty = true;
                        _lastPreviewAsset = null;
                    });
            }
        }
    }

    private void DrawShaderProperty(Paper paper, string id, Material material, ShaderProperty prop)
    {
        string label = !string.IsNullOrEmpty(prop.DisplayName) ? prop.DisplayName : prop.Name;

        // For each value: read from the material if the property is overridden,
        // otherwise from the shader's CURRENT default. The material only stores
        // user-set overrides — defaults are always live.
        var ps = material._properties;
        switch (prop.PropertyType)
        {
            case ShaderPropertyType.Float:
            {
                float val = ps.HasFloat(prop.Name) ? ps.GetFloat(prop.Name) : (float)prop.Value.X;
                EditorGUI.FloatField(paper, id, val, label)
                    .OnValueChanged(v => { material.SetFloat(prop.Name, v); _dirty = true; });
                break;
            }

            case ShaderPropertyType.Int:
            {
                int val = ps.HasInt(prop.Name) ? ps.GetInt(prop.Name) : (int)prop.Value.X;
                EditorGUI.IntField(paper, id, val, label)
                    .OnValueChanged(v => { material.SetInt(prop.Name, v); _dirty = true; });
                break;
            }

            case ShaderPropertyType.Color:
            {
                var val = ps.HasColor(prop.Name)
                    ? ps.GetColor(prop.Name)
                    : new Prowl.Vector.Color((float)prop.Value.X, (float)prop.Value.Y, (float)prop.Value.Z, (float)prop.Value.W);
                EditorGUI.ColorField(paper, id, label, val)
                    .OnValueChanged(v => { material.SetColor(prop.Name, new Prowl.Vector.Color(v.R, v.G, v.B, v.A)); _dirty = true; _lastPreviewAsset = null; });
                break;
            }

            case ShaderPropertyType.Vector2:
            {
                var val = ps.HasVector2(prop.Name) ? ps.GetVector2(prop.Name) : new Float2((float)prop.Value.X, (float)prop.Value.Y);
                EditorGUI.Vector2Field(paper, id, label, val)
                    .OnValueChanged(v => { material.SetVector(prop.Name, v); _dirty = true; });
                break;
            }

            case ShaderPropertyType.Vector3:
            {
                var val = ps.HasVector3(prop.Name) ? ps.GetVector3(prop.Name) : new Float3((float)prop.Value.X, (float)prop.Value.Y, (float)prop.Value.Z);
                EditorGUI.Vector3Field(paper, id, label, val)
                    .OnValueChanged(v => { material.SetVector(prop.Name, v); _dirty = true; });
                break;
            }

            case ShaderPropertyType.Vector4:
            {
                var val = ps.HasVector4(prop.Name) ? ps.GetVector4(prop.Name) : prop.Value;
                EditorGUI.Vector4Field(paper, id, label, val)
                    .OnValueChanged(v => { material.SetVector(prop.Name, v); _dirty = true; });
                break;
            }

            case ShaderPropertyType.Texture2D:
            {
                var val = ps.HasTexture(prop.Name) ? ps.GetTexture(prop.Name) : prop.Texture2DValue;
                EngineObjectPropertyEditor.SetFieldType(typeof(Texture2D));
                PropertyGrid.DrawField(paper, id, label, typeof(Texture2D), val,
                    newVal => { material.SetTexture(prop.Name, newVal as Texture2D); _dirty = true; _lastPreviewAsset = null; }, 0);
                break;
            }

            case ShaderPropertyType.Texture3D:
            {
                var val = ps.HasTexture3D(prop.Name) ? ps.GetTexture3D(prop.Name) : prop.Texture3DValue;
                EngineObjectPropertyEditor.SetFieldType(typeof(Texture3D));
                PropertyGrid.DrawField(paper, id, label, typeof(Texture3D), val,
                    newVal => { material.SetTexture3D(prop.Name, newVal as Texture3D); _dirty = true; }, 0);
                break;
            }
        }
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
                _lastPreviewAsset = null;

                // Reimport to update cache + thumbnail
                EditorAssetDatabase.Instance?.Reimport(entry.Guid);
            }
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError($"Failed to save material: {ex.Message}");
        }
    }
}
