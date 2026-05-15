using System;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Editor.Widgets;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.MeshFeatures.Generation;
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
    // Mesh feature settings applies to every imported sub-mesh.
    private bool _generateSDF;
    private int _sdfResolution = 64;
    private float _sdfPadding = 0.1f;
    private float _sdfMaxDistance = 0.25f;
    private bool _settingsLoaded;
    private bool _settingsDirty;
    private Guid _currentGuid;

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        // Detect asset change reload settings and reset state
        if (_currentGuid != entry.Guid)
        {
            _currentGuid = entry.Guid;
            _settingsLoaded = false;
            _settingsDirty = false;
            _lastPreviewAsset = null;
        }

        // Include the GUID in element IDs so Paper UI state is unique per asset
        id = $"{id}_{entry.Guid:N}";

        var font = EditorTheme.DefaultFont;
        if (font == null) return;
        var model = asset as Model;

        if (!_settingsLoaded)
        {
            LoadSettingsFromMeta(entry);
            _settingsLoaded = true;
        }

        Origami.Header(paper, $"{id}_h_info", "Model Info").Show();

        EditorGUI.Label(paper, $"{id}_path", $"Path: {entry.Path}");
        EditorGUI.Label(paper, $"{id}_guid", $"GUID: {entry.Guid}");

        if (model != null)
        {
            EditorGUI.Label(paper, $"{id}_subs", $"Sub-Assets: {entry.SubAssets.Length}");
        }

        // Sub-assets
        if (entry.SubAssets.Length > 0)
        {
            Origami.Separator(paper, $"{id}_sep_subs").Show();
            Origami.Header(paper, $"{id}_h_subs", $"Sub-Assets ({entry.SubAssets.Length})").Show();

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
                Origami.Header(paper, $"{id}_h_settings", "Import Settings").Underline().Show();

        Origami.Checkbox(paper, $"{id}_genNormals", _generateNormals,
                v => { _generateNormals = v; _settingsDirty = true; })
            .LabelRight("Generate Normals").Show();

        Origami.Checkbox(paper, $"{id}_smoothNormals", _generateSmoothNormals,
                v => { _generateSmoothNormals = v; _settingsDirty = true; })
            .LabelRight("Smooth Normals").Show();

        Origami.Checkbox(paper, $"{id}_tangents", _calculateTangents,
                v => { _calculateTangents = v; _settingsDirty = true; })
            .LabelRight("Calculate Tangents").Show();

        Origami.Checkbox(paper, $"{id}_flipUV", _flipUVs,
                v => { _flipUVs = v; _settingsDirty = true; })
            .LabelRight("Flip UVs").Show();

        Origami.Checkbox(paper, $"{id}_globalScale", _globalScale,
                v => { _globalScale = v; _settingsDirty = true; })
            .LabelRight("Global Scale").Show();

        InspectorRow.Draw(paper, $"{id}_unitScale", "Unit Scale", () =>
            Origami.NumericField<float>(paper, $"{id}_unitScale_v", _unitScale,
                v => { _unitScale = v; _settingsDirty = true; }).Show());

        // Mesh features produces an SDF sub-asset alongside every imported mesh.
                Origami.Header(paper, $"{id}_h_features", "Mesh Features").Underline().Show();

        Origami.Checkbox(paper, $"{id}_genSDF", _generateSDF,
                v => { _generateSDF = v; _settingsDirty = true; })
            .LabelRight("Generate SDF (all meshes)").Show();

        if (_generateSDF)
        {
            InspectorRow.Draw(paper, $"{id}_sdfRes", "SDF Resolution", () =>
                Origami.NumericField<int>(paper, $"{id}_sdfRes_v", _sdfResolution,
                    v => { _sdfResolution = System.Math.Clamp(v, 8, 256); _settingsDirty = true; })
                    .Min(8).Max(256).Show());

            InspectorRow.Draw(paper, $"{id}_sdfPad", "SDF Padding", () =>
                Origami.NumericField<float>(paper, $"{id}_sdfPad_v", _sdfPadding,
                    v => { _sdfPadding = v; _settingsDirty = true; }).Show());

            InspectorRow.Draw(paper, $"{id}_sdfMax", "SDF Max Distance", () =>
                Origami.NumericField<float>(paper, $"{id}_sdfMax_v", _sdfMaxDistance,
                    v => { _sdfMaxDistance = v; _settingsDirty = true; }).Show());
        }

        // Save / Revert buttons
        Origami.Separator(paper, $"{id}_sep_btns").Show();
        if (_settingsDirty)
        {
            using (paper.Row($"{id}_btn_row").Height(28).RowBetween(8).ChildLeft(8).ChildRight(8).Enter())
            {
                paper.Box($"{id}_btn_spacer");

                Origami.Button(paper, $"{id}_revert", "Revert", () =>
                {
                    LoadSettingsFromMeta(entry);
                    _settingsDirty = false;
                }).Width(80).Show();

                Origami.Button(paper, $"{id}_save", "Save & Reimport", () =>
                {
                    SaveSettingsToMeta(entry);
                    _settingsDirty = false;
                    _lastPreviewAsset = null; // Force preview refresh
                    _settingsLoaded = false;
                    EditorAssetDatabase.Instance?.Reimport(entry.Guid);
                    MeshAssetEditor.InvalidateCachedPreviews();
                }).Width(120).Show();
            }
        }
        else
        {
            Origami.Button(paper, $"{id}_reimport", $"{EditorIcons.ArrowsRotate}  Reimport", () =>
            {
                _lastPreviewAsset = null;
                EditorAssetDatabase.Instance?.Reimport(entry.Guid);
            }).Show();
        }

        // 3D Preview
                Origami.Header(paper, $"{id}_h_preview", "Preview").Underline().Show();

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

            if (s.TryGet(SDFFeatureSpec.KeyRoot, out var sdf) && sdf != null)
            {
                _generateSDF = sdf.TryGet(SDFFeatureSpec.Key_Enabled, out var en) && en.BoolValue;
                _sdfResolution = sdf.TryGet(SDFFeatureSpec.Key_Resolution, out var sr) ? sr.IntValue : 64;
                _sdfPadding = sdf.TryGet(SDFFeatureSpec.Key_Padding, out var sp) ? sp.FloatValue : 0.1f;
                _sdfMaxDistance = sdf.TryGet(SDFFeatureSpec.Key_MaxDistance, out var sm) ? sm.FloatValue : 0.25f;
            }
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

        var sdf = EchoObject.NewCompound();
        sdf[SDFFeatureSpec.Key_Enabled] = new EchoObject(_generateSDF);
        sdf[SDFFeatureSpec.Key_Resolution] = new EchoObject(_sdfResolution);
        sdf[SDFFeatureSpec.Key_Padding] = new EchoObject(_sdfPadding);
        sdf[SDFFeatureSpec.Key_MaxDistance] = new EchoObject(_sdfMaxDistance);
        s[SDFFeatureSpec.KeyRoot] = sdf;

        meta.Settings = s;

        MetaFile.Write(metaPath, meta);
    }
}
