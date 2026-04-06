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
    private bool _dirty;

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
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

        // Shader properties — one field per property defined in the shader
        var shader = material.Shader;
        if (shader != null)
        {
            EditorGUI.Separator(paper, $"{id}_sep_props");
            EditorGUI.Header(paper, $"{id}_h_props", "Properties");

            foreach (var prop in shader.Properties)
            {
                DrawShaderProperty(paper, $"{id}_p_{prop.Name}", material, prop);
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

        bool hovered = _preview.DrawPreview(paper, $"{id}_preview", 256, 256);
        _preview.ProcessOrbitInput(hovered);
    }

    private void DrawShaderProperty(Paper paper, string id, Material material, ShaderProperty prop)
    {
        string label = !string.IsNullOrEmpty(prop.DisplayName) ? prop.DisplayName : prop.Name;

        switch (prop.PropertyType)
        {
            case ShaderPropertyType.Float:
            {
                float val = material._properties.GetFloat(prop.Name);
                EditorGUI.FloatField(paper, id, val, label)
                    .OnValueChanged(v => { material.SetFloat(prop.Name, v); _dirty = true; });
                break;
            }

            case ShaderPropertyType.Color:
            {
                var val = material._properties.GetColor(prop.Name);
                var vc = new VColor(val.R, val.G, val.B, val.A);
                EditorGUI.ColorField(paper, id, label, vc)
                    .OnValueChanged(v =>
                    {
                        material.SetColor(prop.Name, new Prowl.Vector.Color(v.R, v.G, v.B, v.A));
                        _dirty = true;
                        _lastPreviewAsset = null; // refresh preview
                    });
                break;
            }

            case ShaderPropertyType.Vector2:
            {
                var val = material._properties.GetVector2(prop.Name);
                EditorGUI.Vector2Field(paper, id, label, val)
                    .OnValueChanged(v => { material.SetVector(prop.Name, v); _dirty = true; });
                break;
            }

            case ShaderPropertyType.Vector3:
            {
                var val = material._properties.GetVector3(prop.Name);
                EditorGUI.Vector3Field(paper, id, label, val)
                    .OnValueChanged(v => { material.SetVector(prop.Name, v); _dirty = true; });
                break;
            }

            case ShaderPropertyType.Vector4:
            {
                var val = material._properties.GetVector4(prop.Name);
                EditorGUI.Vector4Field(paper, id, label, val)
                    .OnValueChanged(v => { material.SetVector(prop.Name, v); _dirty = true; });
                break;
            }

            case ShaderPropertyType.Texture2D:
            {
                var val = material._properties.GetTexture(prop.Name);
                EngineObjectPropertyEditor.SetFieldType(typeof(Texture2D));
                PropertyGrid.DrawField(paper, id, label, typeof(Texture2D), val,
                    newVal =>
                    {
                        material.SetTexture(prop.Name, newVal as Texture2D);
                        _dirty = true;
                        _lastPreviewAsset = null;
                    }, 0);
                break;
            }

            case ShaderPropertyType.Texture3D:
            {
                var val = material._properties.GetTexture3D(prop.Name);
                EngineObjectPropertyEditor.SetFieldType(typeof(Texture3D));
                PropertyGrid.DrawField(paper, id, label, typeof(Texture3D), val,
                    newVal =>
                    {
                        material.SetTexture3D(prop.Name, newVal as Texture3D);
                        _dirty = true;
                    }, 0);
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

            var ctx = new SerializationContext();
            Runtime.AssetDatabase.ConfigureContext(ctx);

            var echo = Serializer.Serialize(material, ctx);
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
