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

// Targets the importer, not the asset type: a model imports into a PrefabAsset like any prefab,
// so keying on the asset would hand these files the generic prefab editor and drop the import settings.
[CustomAssetEditor(typeof(Importers.EditorModelImporter))]
public class ModelAssetEditor : ImportSettingsEditor
{

    protected override bool ApplyState(AssetEntry entry, EngineObject? asset)
    {
        if (!base.ApplyState(entry, asset)) return false;

        // Reimporting rebuilds every mesh this model owns, so the cached previews are stale.
        PreviewWidget.For(entry.Guid, showGrid: true).Invalidate();
        MeshAssetEditor.InvalidateCachedPreviews();
        return true;
    }

    protected override void RevertState(AssetEntry entry, EngineObject? asset, EchoObject baseline)
    {
        base.RevertState(entry, asset, baseline);
        PreviewWidget.For(entry.Guid, showGrid: true).Invalidate();
    }

    // Settings live in the compound rather than in fields, so there is one copy of each value and
    // nothing to keep in sync. Reads never create anything - materialising the SDF block just by
    // looking at it would register as an edit and ask to apply a change nobody made.
    private static bool Bool(EchoObject? s, string key, bool fallback)
        => s != null && s.TryGet(key, out EchoObject t) ? t.BoolValue : fallback;

    private static int Int(EchoObject? s, string key, int fallback)
        => s != null && s.TryGet(key, out EchoObject t) ? t.IntValue : fallback;

    private static float Float(EchoObject? s, string key, float fallback)
        => s != null && s.TryGet(key, out EchoObject t) ? t.FloatValue : fallback;

    private static EchoObject? SdfBlock(EchoObject s)
        => s.TryGet(SDFFeatureSpec.KeyRoot, out EchoObject sdf) ? sdf : null;

    private static EchoObject SdfBlockForWrite(EchoObject s)
    {
        if (s.TryGet(SDFFeatureSpec.KeyRoot, out EchoObject sdf)) return sdf;
        var created = EchoObject.NewCompound();
        s[SDFFeatureSpec.KeyRoot] = created;
        return created;
    }

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        // Include the GUID in element IDs so Paper UI state is unique per asset
        id = $"{id}_{entry.Guid:N}";

        var font = EditorTheme.DefaultFont;
        if (font == null) return;
        var m = Origami.Current.Metrics;
        var model = asset as PrefabAsset;

        EchoObject settings = Settings(entry);
        EchoObject? sdf = SdfBlock(settings);

        if (model != null)
        {
            var pr = PreviewWidget.For(entry.Guid, showGrid: true).Get(model, p => p.SetupForPrefab(model));
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

        EditorGUI.SettingsToggle(paper, $"{id}_genNormals", "Generate Normals", Bool(settings, "generateNormals", true),
            v => settings["generateNormals"] = new EchoObject(v), separator: false);

        EditorGUI.SettingsToggle(paper, $"{id}_smoothNormals", "Smooth Normals", Bool(settings, "generateSmoothNormals", false),
            v => settings["generateSmoothNormals"] = new EchoObject(v), separator: false);

        EditorGUI.SettingsToggle(paper, $"{id}_tangents", "Calculate Tangents", Bool(settings, "calculateTangents", true),
            v => settings["calculateTangents"] = new EchoObject(v), separator: false);

        EditorGUI.SettingsToggle(paper, $"{id}_flipUV", "Flip UVs", Bool(settings, "flipUVs", false),
            v => settings["flipUVs"] = new EchoObject(v), separator: false);

        EditorGUI.SettingsToggle(paper, $"{id}_globalScale", "Global Scale", Bool(settings, "globalScale", false),
            v => settings["globalScale"] = new EchoObject(v), separator: false);

        EditorGUI.Row(paper, $"{id}_unitScale", "Unit Scale", () =>
            Origami.NumericField<float>(paper, $"{id}_unitScale_v", Float(settings, "unitScale", 1f),
                v => settings["unitScale"] = new EchoObject(v)).Show());

        // Lightmapping generates a UV2 atlas for every mesh via Prowl.Unwrapper. Off by default:
        // it's slow (a full unwrap per mesh) and some models already ship their own UV2.
        EditorGUI.SettingsToggle(paper, $"{id}_lightmapUVs", "Generate Lightmap UVs (slow)", Bool(settings, "generateLightmapUVs", false),
            v => settings["generateLightmapUVs"] = new EchoObject(v), separator: false);

        // Mesh features produces an SDF sub-asset alongside every imported mesh.
        EditorGUI.SectionHeader(paper, $"{id}_h_features", "Mesh Features");

        bool generateSDF = Bool(sdf, SDFFeatureSpec.Key_Enabled, false);
        EditorGUI.SettingsToggle(paper, $"{id}_genSDF", "Generate SDF (all meshes)", generateSDF,
            v => SdfBlockForWrite(settings)[SDFFeatureSpec.Key_Enabled] = new EchoObject(v), separator: false);

        if (generateSDF)
        {
            EditorGUI.Row(paper, $"{id}_sdfRes", "SDF Resolution", () =>
                Origami.NumericField<int>(paper, $"{id}_sdfRes_v", Int(sdf, SDFFeatureSpec.Key_Resolution, 64),
                    v => SdfBlockForWrite(settings)[SDFFeatureSpec.Key_Resolution] = new EchoObject(System.Math.Clamp(v, 8, 256)))
                    .Min(8).Max(256).Show());

            EditorGUI.Row(paper, $"{id}_sdfPad", "SDF Padding", () =>
                Origami.NumericField<float>(paper, $"{id}_sdfPad_v", Float(sdf, SDFFeatureSpec.Key_Padding, 0.1f),
                    v => SdfBlockForWrite(settings)[SDFFeatureSpec.Key_Padding] = new EchoObject(v)).Show());

            EditorGUI.Row(paper, $"{id}_sdfMax", "SDF Max Distance", () =>
                Origami.NumericField<float>(paper, $"{id}_sdfMax_v", Float(sdf, SDFFeatureSpec.Key_MaxDistance, 0.25f),
                    v => SdfBlockForWrite(settings)[SDFFeatureSpec.Key_MaxDistance] = new EchoObject(v)).Show());
        }

        DrawApplyRevertBar(paper, id, entry, asset);

        // Reimport stays available when there is nothing pending, for re-running the import as-is.
        if (!HasPendingChanges(entry, asset))
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
                    PreviewWidget.For(entry.Guid, showGrid: true).Invalidate();
                    EditorAssetBackend.Instance?.Reimport(entry.Guid);
                });
        }
    }
}
