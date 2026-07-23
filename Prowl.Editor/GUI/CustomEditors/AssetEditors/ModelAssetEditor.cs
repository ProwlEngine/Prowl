using System;
using System.IO;

using Prowl.Echo;
using Prowl.Editor.GUI;
using static Prowl.Editor.GUI.EditorGUI;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.MeshFeatures.Generation;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.Inspector;

[CustomAssetEditor(typeof(Model))]
public class ModelAssetEditor : AssetImporterEditor
{
    private readonly PreviewWidget _preview = new(showGrid: true);

    // Cached settings
    private bool _generateNormals = true;
    private bool _generateSmoothNormals;
    private bool _calculateTangents = true;
    private bool _flipUVs;
    private bool _globalScale;
    private float _unitScale = 1f;
    private bool _generateLightmapUVs;
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
            _preview.Invalidate();
        }

        // Include the GUID in element IDs so Paper UI state is unique per asset
        id = $"{id}_{entry.Guid:N}";

        var font = EditorTheme.DefaultFont;
        if (font == null) return;
        var m = Origami.Current.Metrics;
        var model = asset as Model;

        if (!_settingsLoaded)
        {
            LoadSettingsFromMeta(entry);
            _settingsLoaded = true;
        }

        if (model != null)
        {
            var pr = _preview.Get(model, p => p.SetupForModel(model));
            using (paper.Box($"{id}_previewCard").Height(200)
                .Margin(m.PaddingLarge, m.PaddingLarge, m.PaddingLarge, m.Spacing)
                .Rounded(8).Clip()
                .BackgroundColor(EditorTheme.Neutral300)
                .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
                .ChildLeft().ChildRight().ChildTop().ChildBottom().Enter())
            {
                pr.DrawPreview(paper, $"{id}_preview", 184, 184);
            }

            // Quick-facts chip strip.
            int meshCount = 0, matCount = 0, animCount = 0;
            foreach (var sub in entry.SubAssets)
            {
                var t = sub.Type;
                if (t == null) continue;
                if (typeof(Mesh).IsAssignableFrom(t)) meshCount++;
                else if (typeof(Material).IsAssignableFrom(t)) matCount++;
                else if (typeof(AnimationClip).IsAssignableFrom(t)) animCount++;
            }

            using (paper.Row($"{id}_stats").Height(UnitValue.Auto)
                .Margin(m.PaddingLarge, m.PaddingLarge, 0, m.SpacingLarge).RowBetween(m.SpacingMedium).Enter())
            {
                EditorGUI.StatChip(paper, $"{id}_st_meshes", $"{meshCount} {(meshCount == 1 ? "Mesh" : "Meshes")}", font);
                EditorGUI.StatChip(paper, $"{id}_st_mats", $"{matCount} {(matCount == 1 ? "Material" : "Materials")}", font);
                if (animCount > 0)
                    EditorGUI.StatChip(paper, $"{id}_st_anims", $"{animCount} {(animCount == 1 ? "Animation" : "Animations")}", font);
                EditorGUI.StatChip(paper, $"{id}_st_subs", $"{entry.SubAssets.Length} Sub-Assets", font);
                paper.Box($"{id}_st_pad").Height(1).IsNotInteractable();
            }
        }

        // Contents: read-only list of imported sub-assets.
        if (entry.SubAssets.Length > 0)
        {
            EditorGUI.SectionHeader(paper, $"{id}_h_contents", "Contents", first: model == null);
            int shown = Math.Min(entry.SubAssets.Length, 30);
            for (int i = 0; i < shown; i++)
            {
                var sub = entry.SubAssets[i];
                string typeName = sub.Type?.Name ?? "Unknown";
                EditorGUI.Row(paper, $"{id}_sub_{i}", sub.Name, () =>
                    paper.Box($"{id}_sub_{i}_v").Height(m.RowHeight).IsNotInteractable()
                        .Text(typeName, font).TextColor(EditorTheme.Ink400)
                        .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft));
            }
            if (entry.SubAssets.Length > shown)
                EditorGUI.Row(paper, $"{id}_sub_more", $"and {entry.SubAssets.Length - shown} more", () => { });
        }

        // Import settings
        EditorGUI.SectionHeader(paper, $"{id}_h_settings", "Import Settings",
            first: model == null && entry.SubAssets.Length == 0);

        EditorGUI.SettingsToggle(paper, $"{id}_genNormals", "Generate Normals", _generateNormals,
            v => { _generateNormals = v; _settingsDirty = true; }, separator: false);

        EditorGUI.SettingsToggle(paper, $"{id}_smoothNormals", "Smooth Normals", _generateSmoothNormals,
            v => { _generateSmoothNormals = v; _settingsDirty = true; }, separator: false);

        EditorGUI.SettingsToggle(paper, $"{id}_tangents", "Calculate Tangents", _calculateTangents,
            v => { _calculateTangents = v; _settingsDirty = true; }, separator: false);

        EditorGUI.SettingsToggle(paper, $"{id}_flipUV", "Flip UVs", _flipUVs,
            v => { _flipUVs = v; _settingsDirty = true; }, separator: false);

        EditorGUI.SettingsToggle(paper, $"{id}_globalScale", "Global Scale", _globalScale,
            v => { _globalScale = v; _settingsDirty = true; }, separator: false);

        EditorGUI.Row(paper, $"{id}_unitScale", "Unit Scale", () =>
            Origami.NumericField<float>(paper, $"{id}_unitScale_v", _unitScale,
                v => { _unitScale = v; _settingsDirty = true; }).Show());

        // Lightmapping generates a UV2 atlas for every mesh via Prowl.Unwrapper. Off by default:
        // it's slow (a full unwrap per mesh) and some models already ship their own UV2.
        EditorGUI.SettingsToggle(paper, $"{id}_lightmapUVs", "Generate Lightmap UVs (slow)", _generateLightmapUVs,
            v => { _generateLightmapUVs = v; _settingsDirty = true; }, separator: false);

        // Mesh features produces an SDF sub-asset alongside every imported mesh.
        EditorGUI.SectionHeader(paper, $"{id}_h_features", "Mesh Features");

        EditorGUI.SettingsToggle(paper, $"{id}_genSDF", "Generate SDF (all meshes)", _generateSDF,
            v => { _generateSDF = v; _settingsDirty = true; }, separator: false);

        if (_generateSDF)
        {
            EditorGUI.Row(paper, $"{id}_sdfRes", "SDF Resolution", () =>
                Origami.NumericField<int>(paper, $"{id}_sdfRes_v", _sdfResolution,
                    v => { _sdfResolution = System.Math.Clamp(v, 8, 256); _settingsDirty = true; })
                    .Min(8).Max(256).Show());

            EditorGUI.Row(paper, $"{id}_sdfPad", "SDF Padding", () =>
                Origami.NumericField<float>(paper, $"{id}_sdfPad_v", _sdfPadding,
                    v => { _sdfPadding = v; _settingsDirty = true; }).Show());

            EditorGUI.Row(paper, $"{id}_sdfMax", "SDF Max Distance", () =>
                Origami.NumericField<float>(paper, $"{id}_sdfMax_v", _sdfMaxDistance,
                    v => { _sdfMaxDistance = v; _settingsDirty = true; }).Show());
        }

        // Save / Reimport CTA
        if (_settingsDirty)
        {
            using (paper.Row($"{id}_btns").Height(UnitValue.Auto)
                .Margin(m.PaddingLarge, m.PaddingLarge, m.SpacingLarge, m.SpacingLarge)
                .RowBetween(m.SpacingMedium).Enter())
            {
                paper.Box($"{id}_btn_spacer").Height(1).IsNotInteractable();

                paper.Box($"{id}_revert").Width(UnitValue.Auto).Height(30).Rounded(8).Padding(16, 16, 0, 0)
                    .BackgroundColor(EditorTheme.Glass).BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
                    .Hovered.BackgroundColor(EditorTheme.Neutral300).End()
                    .Text("Revert", EditorTheme.FontSemiBold ?? font).TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
                    .OnClick(0, (_, _) =>
                    {
                        LoadSettingsFromMeta(entry);
                        _settingsDirty = false;
                    });

                paper.Box($"{id}_save").Width(UnitValue.Auto).Height(30).Rounded(8).Padding(16, 16, 0, 0)
                    .BackgroundColor(EditorTheme.Accent)
                    .Hovered.BackgroundColor(EditorTheme.AccentBright).End()
                    .Text($"{EditorIcons.FloppyDisk}  Save & Reimport", EditorTheme.FontSemiBold ?? font)
                    .TextColor(System.Drawing.Color.White).FontSize(EditorTheme.FontSizeSmall)
                    .Alignment(TextAlignment.MiddleCenter)
                    .OnClick(0, (_, _) =>
                    {
                        SaveSettingsToMeta(entry);
                        _settingsDirty = false;
                        _preview.Invalidate();
                        _settingsLoaded = false;
                        EditorAssetBackend.Instance?.Reimport(entry.Guid);
                        MeshAssetEditor.InvalidateCachedPreviews();
                    });
            }
        }
        else
        {
            paper.Box($"{id}_reimport").Width(UnitValue.Auto).Height(30)
                .Margin(m.PaddingLarge, m.PaddingLarge, m.SpacingLarge, m.SpacingLarge).Rounded(8).Padding(16, 16, 0, 0)
                .BackgroundColor(EditorTheme.Accent)
                .Hovered.BackgroundColor(EditorTheme.AccentBright).End()
                .Text($"{EditorIcons.ArrowsRotate}  Reimport", EditorTheme.FontSemiBold ?? font)
                .TextColor(System.Drawing.Color.White).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleCenter)
                .OnClick(0, (_, _) =>
                {
                    _preview.Invalidate();
                    EditorAssetBackend.Instance?.Reimport(entry.Guid);
                });
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
            _generateLightmapUVs = s.TryGet("generateLightmapUVs", out var glu) && glu.BoolValue;

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
        s["generateLightmapUVs"] = new EchoObject(_generateLightmapUVs);

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
