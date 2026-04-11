using System;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Inspector;

[CustomAssetEditor(typeof(Model))]
public class ModelAssetEditor : AssetImporterEditor
{
    private PreviewRenderer? _preview;
    private EngineObject? _lastPreviewAsset;

    // Cached settings
    private bool _generateNormals = true;
    private bool _generateSmoothNormals;
    private bool _calculateTangents = true;
    private bool _flipUVs;
    private bool _globalScale;
    private float _unitScale = 1f;
    private bool _settingsLoaded;
    private bool _settingsDirty;

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;
        var model = asset as Model;

        if (!_settingsLoaded)
        {
            LoadSettingsFromMeta(entry);
            _settingsLoaded = true;
        }

        EditorGUI.Header(paper, $"{id}_h_info", "Model Info");

        EditorGUI.Label(paper, $"{id}_path", $"Path: {entry.Path}");
        EditorGUI.Label(paper, $"{id}_guid", $"GUID: {entry.Guid}");

        if (model != null)
        {
            EditorGUI.Label(paper, $"{id}_subs", $"Sub-Assets: {entry.SubAssets.Length}");
        }

        // Sub-assets
        if (entry.SubAssets.Length > 0)
        {
            EditorGUI.Separator(paper, $"{id}_sep_subs");
            EditorGUI.Header(paper, $"{id}_h_subs", $"Sub-Assets ({entry.SubAssets.Length})");

            for (int i = 0; i < entry.SubAssets.Length && i < 30; i++)
            {
                var sub = entry.SubAssets[i];
                string typeName = sub.Type?.Name ?? "Unknown";
                EditorGUI.Label(paper, $"{id}_sub_{i}", $"  {sub.Name} ({typeName})");
            }
            if (entry.SubAssets.Length > 30)
                EditorGUI.Label(paper, $"{id}_sub_more", $"  ... and {entry.SubAssets.Length - 30} more");
        }

        // Import settings
        EditorGUI.Separator(paper, $"{id}_sep_settings");
        EditorGUI.Header(paper, $"{id}_h_settings", "Import Settings");

        EditorGUI.Toggle(paper, $"{id}_genNormals", "Generate Normals", _generateNormals)
            .OnValueChanged(v => { _generateNormals = v; _settingsDirty = true; });

        EditorGUI.Toggle(paper, $"{id}_smoothNormals", "Smooth Normals", _generateSmoothNormals)
            .OnValueChanged(v => { _generateSmoothNormals = v; _settingsDirty = true; });

        EditorGUI.Toggle(paper, $"{id}_tangents", "Calculate Tangents", _calculateTangents)
            .OnValueChanged(v => { _calculateTangents = v; _settingsDirty = true; });

        EditorGUI.Toggle(paper, $"{id}_flipUV", "Flip UVs", _flipUVs)
            .OnValueChanged(v => { _flipUVs = v; _settingsDirty = true; });

        EditorGUI.Toggle(paper, $"{id}_globalScale", "Global Scale", _globalScale)
            .OnValueChanged(v => { _globalScale = v; _settingsDirty = true; });

        EditorGUI.FloatField(paper, $"{id}_unitScale", _unitScale, label: "Unit Scale")
            .OnValueChanged(v => { _unitScale = v; _settingsDirty = true; });

        // Save / Revert buttons
        EditorGUI.Separator(paper, $"{id}_sep_btns");
        if (_settingsDirty)
        {
            using (paper.Row($"{id}_btn_row").Height(28).RowBetween(8).ChildLeft(8).ChildRight(8).Enter())
            {
                paper.Box($"{id}_btn_spacer");

                EditorGUI.Button(paper, $"{id}_revert", "Revert", width: 80)
                    .OnValueChanged(_ =>
                    {
                        LoadSettingsFromMeta(entry);
                        _settingsDirty = false;
                    });

                EditorGUI.Button(paper, $"{id}_save", "Save & Reimport", width: 120)
                    .OnValueChanged(_ =>
                    {
                        SaveSettingsToMeta(entry);
                        _settingsDirty = false;
                        _lastPreviewAsset = null; // Force preview refresh
                        _settingsLoaded = false;
                        EditorAssetDatabase.Instance?.Reimport(entry.Guid);
                    });
            }
        }
        else
        {
            EditorGUI.Button(paper, $"{id}_reimport", $"{EditorIcons.ArrowsRotate}  Reimport")
                .OnValueChanged(_ =>
                {
                    _lastPreviewAsset = null;
                    EditorAssetDatabase.Instance?.Reimport(entry.Guid);
                });
        }

        // 3D Preview
        EditorGUI.Separator(paper, $"{id}_sep_preview");
        EditorGUI.Header(paper, $"{id}_h_preview", "Preview");

        if (model != null)
        {
            _preview ??= new PreviewRenderer(256, 256) { ShowGrid = true };

            if (_lastPreviewAsset != model)
            {
                _lastPreviewAsset = model;
                _preview.SetupForModel(model);
            }

            _preview.DrawPreview(paper, $"{id}_preview", 256, 256);
        }
    }

    private void LoadSettingsFromMeta(AssetEntry entry)
    {
        if (Project.Current == null) return;
        string absPath = Path.Combine(Project.Current.AssetsPath, entry.Path);
        string metaPath = MetaFile.GetMetaPath(absPath);
        if (!File.Exists(metaPath)) return;

        try
        {
            var meta = MetaFile.Read(metaPath);
            var s = meta.Settings;
            if (s == null) return;

            _generateNormals = !s.TryGet("generateNormals", out var gn) || gn.BoolValue; // default true
            _generateSmoothNormals = s.TryGet("generateSmoothNormals", out var gsn) && gsn.BoolValue;
            _calculateTangents = !s.TryGet("calculateTangents", out var ct) || ct.BoolValue; // default true
            _flipUVs = s.TryGet("flipUVs", out var fu) && fu.BoolValue;
            _globalScale = s.TryGet("globalScale", out var gs) && gs.BoolValue;
            _unitScale = s.TryGet("unitScale", out var us) ? us.FloatValue : 1f;
        }
        catch { }
    }

    private void SaveSettingsToMeta(AssetEntry entry)
    {
        if (Project.Current == null) return;
        string absPath = Path.Combine(Project.Current.AssetsPath, entry.Path);
        string metaPath = MetaFile.GetMetaPath(absPath);

        MetaFileData meta;
        try { meta = File.Exists(metaPath) ? MetaFile.Read(metaPath) : MetaFile.CreateNew(entry.ImporterType); }
        catch { meta = MetaFile.CreateNew(entry.ImporterType); }

        var s = EchoObject.NewCompound();
        s["generateNormals"] = new EchoObject(_generateNormals);
        s["generateSmoothNormals"] = new EchoObject(_generateSmoothNormals);
        s["calculateTangents"] = new EchoObject(_calculateTangents);
        s["flipUVs"] = new EchoObject(_flipUVs);
        s["globalScale"] = new EchoObject(_globalScale);
        s["unitScale"] = new EchoObject(_unitScale);
        meta.Settings = s;

        MetaFile.Write(metaPath, meta);
    }
}
