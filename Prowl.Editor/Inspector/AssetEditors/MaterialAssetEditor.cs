using System;

using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Inspector;

[CustomAssetEditor(typeof(Material))]
public class MaterialAssetEditor : AssetImporterEditor
{
    private PreviewRenderer? _preview;
    private EngineObject? _lastPreviewAsset;

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;
        var material = asset as Material;

        EditorGUI.Header(paper, $"{id}_h_info", "Material");

        EditorGUI.Label(paper, $"{id}_path", $"Path: {entry.Path}");
        EditorGUI.Label(paper, $"{id}_guid", $"GUID: {entry.Guid}");

        if (material != null)
        {
            // Shader reference
            EditorGUI.Separator(paper, $"{id}_sep_shader");
            EditorGUI.Header(paper, $"{id}_h_shader", "Shader");
            PropertyGrid.DrawField(paper, $"{id}_shader", "Shader", typeof(Shader), material.Shader,
                newVal => { /* TODO: change shader on material */ }, 0);

            // Material properties via PropertyGrid
            EditorGUI.Separator(paper, $"{id}_sep_props");
            EditorGUI.Header(paper, $"{id}_h_props", "Properties");
            PropertyGrid.Draw(paper, $"{id}_props", material);

            // 3D Preview — sphere with material
            EditorGUI.Separator(paper, $"{id}_sep_preview");
            EditorGUI.Header(paper, $"{id}_h_preview", "Preview");

            _preview ??= new PreviewRenderer(200, 200);
            if (_lastPreviewAsset != material)
            {
                _lastPreviewAsset = material;
                _preview.SetupForMaterial(material);
            }

            bool hovered = _preview.DrawPreview(paper, $"{id}_preview", 200, 200);
            _preview.ProcessOrbitInput(hovered);
        }

        // Reimport
        EditorGUI.Separator(paper, $"{id}_sep_actions");
        EditorGUI.Button(paper, $"{id}_reimport", $"{EditorIcons.ArrowsRotate}  Reimport")
            .OnValueChanged(_ => EditorAssetDatabase.Instance?.Reimport(entry.Guid));
    }
}
