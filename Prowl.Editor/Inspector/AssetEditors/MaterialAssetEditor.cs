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

        _preview.DrawPreview(paper, $"{id}_preview", 256, 256);
    }

    private void DrawShaderProperty(Paper paper, string id, Material material, ShaderProperty prop)
    {
        string label = !string.IsNullOrEmpty(prop.DisplayName) ? prop.DisplayName : prop.Name;

        switch (prop.PropertyType)
        {
            case ShaderPropertyType.Float:
            {
                float val = prop.Value.X;
                EditorGUI.FloatField(paper, id, val, label)
                    .OnValueChanged(v =>
                    {
                        prop.Value = new Float4(v, 0, 0, 0);
                        material.SetFloat(prop.Name, v);
                        _dirty = true;
                    });
                break;
            }

            case ShaderPropertyType.Int:
            {
                int val = (int)prop.Value.X;
                EditorGUI.IntField(paper, id, val, label)
                    .OnValueChanged(v =>
                    {
                        prop.Value = new Float4(v, 0, 0, 0);
                        material.SetInt(prop.Name, v);
                        _dirty = true;
                    });
                break;
            }

            case ShaderPropertyType.Color:
            {
                var val = (VColor)prop;
                EditorGUI.ColorField(paper, id, label, val)
                    .OnValueChanged(v =>
                    {
                        prop.Value = new Float4(v.R, v.G, v.B, v.A);
                        material.SetColor(prop.Name, new Prowl.Vector.Color(v.R, v.G, v.B, v.A));
                        _dirty = true;
                        _lastPreviewAsset = null; // refresh preview
                    });
                break;
            }

            case ShaderPropertyType.Vector2:
            {
                var val = new Float2(prop.Value.X, prop.Value.Y);
                EditorGUI.Vector2Field(paper, id, label, val)
                    .OnValueChanged(v =>
                    {
                        prop.Value = new Float4(v.X, v.Y, 0, 0);
                        material.SetVector(prop.Name, v);
                        _dirty = true;
                    });
                break;
            }

            case ShaderPropertyType.Vector3:
            {
                var val = new Float3(prop.Value.X, prop.Value.Y, prop.Value.Z);
                EditorGUI.Vector3Field(paper, id, label, val)
                    .OnValueChanged(v =>
                    {
                        prop.Value = new Float4(v.X, v.Y, v.Z, 0);
                        material.SetVector(prop.Name, v);
                        _dirty = true;
                    });
                break;
            }

            case ShaderPropertyType.Vector4:
            {
                var val = prop.Value;
                EditorGUI.Vector4Field(paper, id, label, val)
                    .OnValueChanged(v =>
                    {
                        prop.Value = v;
                        material.SetVector(prop.Name, v);
                        _dirty = true;
                    });
                break;
            }

            case ShaderPropertyType.Texture2D:
            {
                var val = prop.Texture2DValue;
                EngineObjectPropertyEditor.SetFieldType(typeof(Texture2D));
                PropertyGrid.DrawField(paper, id, label, typeof(Texture2D), val,
                    newVal =>
                    {
                        var tex = newVal as Texture2D;
                        prop.Texture2DValue = tex;
                        material.SetTexture(prop.Name, tex);
                        _dirty = true;
                        _lastPreviewAsset = null;
                    }, 0);
                break;
            }

            case ShaderPropertyType.Texture3D:
            {
                var val = prop.Texture3DValue;
                EngineObjectPropertyEditor.SetFieldType(typeof(Texture3D));
                PropertyGrid.DrawField(paper, id, label, typeof(Texture3D), val,
                    newVal =>
                    {
                        var tex = newVal as Texture3D;
                        prop.Texture3DValue = tex;
                        material.SetTexture3D(prop.Name, tex);
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
