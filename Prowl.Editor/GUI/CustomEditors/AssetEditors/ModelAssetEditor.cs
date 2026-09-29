using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Editor.GUI;
using Prowl.Editor.Projects;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.AssetImporting;
using Prowl.Runtime.MeshFeatures.Generation;
using Prowl.Runtime.Resources;

using static Prowl.Editor.GUI.EditorGUI;

namespace Prowl.Editor.Inspector;

// Targets the importer, not the asset type: a model imports into a PrefabAsset like any prefab,
// so keying on the asset would hand these files the generic prefab editor and drop the import settings.
[CustomAssetEditor(typeof(Importers.EditorModelImporter))]
public class ModelAssetEditor : ImportSettingsEditor
{
    private enum Tab { Model, Animation, Materials }

    /// <summary>What the inspector is showing for one model, kept per asset: the tab and the preview's playhead.</summary>
    private sealed class ViewState
    {
        public Tab Tab;
        public int Clip = -1;
        public bool Playing = true;
        public float Time;
        public double LastFrame = -1;
        public readonly ClipEventTrack Events = new();
    }

    private static readonly Dictionary<Guid, ViewState> s_views = new();

    private static ViewState View(Guid guid)
    {
        if (!s_views.TryGetValue(guid, out ViewState? state)) s_views[guid] = state = new ViewState();
        return state;
    }

    protected override bool ApplyState(AssetEntry entry, EngineObject? asset)
    {
        if (!base.ApplyState(entry, asset)) return false;

        // Reimporting rebuilds every mesh this model owns, so the cached previews are stale.
        PreviewWidget.For(entry.Guid, showGrid: true).Invalidate();
        foreach (SubAssetEntry sub in entry.SubAssets)
            PreviewWidget.Invalidate(sub.Guid);
        s_views.Remove(entry.Guid);
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

    private static string Str(EchoObject? s, string key, string fallback)
        => s != null && s.TryGet(key, out EchoObject t) ? t.StringValue ?? fallback : fallback;

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
        ViewState view = View(entry.Guid);

        EchoObject settings = Settings(entry);

        PreviewRenderer? preview = model != null
            ? PreviewWidget.For(entry.Guid, showGrid: true).Get(model, p => p.SetupForPrefab(model))
            : null;

        if (preview != null)
        {
            DrawPreview(paper, id, m, preview, view);
            DrawStats(paper, id, m, font, entry);
        }

        DrawTabs(paper, id, view, entry);

        switch (view.Tab)
        {
            case Tab.Animation: DrawAnimationTab(paper, id, m, font, settings, entry, preview, view); break;
            case Tab.Materials: DrawMaterialsTab(paper, id, m, font, settings, entry); break;
            default: DrawModelTab(paper, id, m, settings); break;
        }

        DrawApplyRevertBar(paper, id, entry, asset);

        // Reimport stays available when there is nothing pending, for re-running the import as-is.
        if (!HasPendingChanges(entry, asset))
        {
            paper.Box($"{id}_reimport").Width(UnitValue.Auto).Height(30)
                .Margin(m.PaddingLarge, m.PaddingLarge, m.SpacingLarge, m.SpacingLarge).Rounded(m.Rounding).Padding(16, 16, 0, 0)
                .BackgroundColor(EditorTheme.Accent)
                .Hovered.BackgroundColor(EditorTheme.AccentBright).End()
                .Text($"{EditorIcons.ArrowsRotate}  Reimport", EditorTheme.FontSemiBold ?? font)
                .TextColor(System.Drawing.Color.White).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleCenter)
                .OnClick(0, (_, _) =>
                {
                    PreviewWidget.For(entry.Guid, showGrid: true).Invalidate();
                    s_views.Remove(entry.Guid);
                    EditorAssetBackend.Instance?.Reimport(entry.Guid);
                });
        }
    }

    // ---- preview ------------------------------------------------------------------------------

    private static void DrawPreview(Paper paper, string id, OrigamiMetrics m, PreviewRenderer preview, ViewState view)
    {
        AdvanceClip(paper, preview, view);

        using (paper.Box($"{id}_previewCard").Height(200)
            .Margin(m.PaddingLarge, m.PaddingLarge, m.PaddingLarge, m.Spacing)
            .Rounded(m.ContainerRounding).Clip()
            .BackgroundColor(EditorTheme.Neutral300)
            .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .JustifyContent(LayoutJustification.Center).AlignItems(LayoutAlignment.Center).Enter())
        {
            preview.DrawPreview(paper, $"{id}_preview", 184, 184);
        }
    }

    /// <summary>Moves the preview's playhead on the UI clock and poses the model.</summary>
    private static void AdvanceClip(Paper paper, PreviewRenderer preview, ViewState view)
    {
        if (!preview.CanAnimate) return;

        // The preview starts playing on its own.
        if (view.Clip < 0 && preview.Clips.Count > 0) view.Clip = 0;

        AnimationClip? clip = view.Clip >= 0 && view.Clip < preview.Clips.Count ? preview.Clips[view.Clip] : null;
        double now = paper.Time;
        double elapsed = view.LastFrame < 0 ? 0 : now - view.LastFrame;
        view.LastFrame = now;

        if (view.Playing && clip.IsValid() && clip!.Duration > 0f)
        {
            view.Time += (float)elapsed / clip.Duration;
            view.Time -= MathF.Floor(view.Time);
        }

        preview.PoseAt(clip, view.Time);
    }

    private static void DrawStats(Paper paper, string id, OrigamiMetrics m, Scribe.FontFile font, AssetEntry entry)
    {
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
            .Margin(m.PaddingLarge, m.PaddingLarge, 0, m.SpacingLarge).Gap(m.SpacingMedium).Enter())
        {
            EditorGUI.StatChip(paper, $"{id}_st_meshes", $"{meshCount} {(meshCount == 1 ? "Mesh" : "Meshes")}", font);
            EditorGUI.StatChip(paper, $"{id}_st_mats", $"{matCount} {(matCount == 1 ? "Material" : "Materials")}", font);
            if (animCount > 0)
                EditorGUI.StatChip(paper, $"{id}_st_anims", $"{animCount} {(animCount == 1 ? "Animation" : "Animations")}", font);
            EditorGUI.StatChip(paper, $"{id}_st_subs", $"{entry.SubAssets.Length} Sub-Assets", font);
            paper.Box($"{id}_st_pad").Height(1).IsNotInteractable();
        }
    }

    private static void DrawTabs(Paper paper, string id, ViewState view, AssetEntry entry)
    {
        int animations = CountOf(entry, typeof(AnimationClip));

        var m = Origami.Current.Metrics;
        using (paper.Box($"{id}_tabsRow").Height(UnitValue.Auto)
            .Margin(m.PaddingLarge, m.PaddingLarge, 0, m.Spacing).Enter())
        {
            Origami.Tabs(paper, $"{id}_tabs", (int)view.Tab, i => view.Tab = (Tab)i)
                .Underline()
                .Tab("Model")
                .Tab("Animation", null, animations > 0 ? animations.ToString() : null)
                .Tab("Materials")
                .Show();
        }
    }

    private static int CountOf(AssetEntry entry, Type type)
    {
        int count = 0;
        foreach (var sub in entry.SubAssets)
            if (sub.Type != null && type.IsAssignableFrom(sub.Type)) count++;
        return count;
    }

    // ---- model --------------------------------------------------------------------------------

    private static void DrawModelTab(Paper paper, string id, OrigamiMetrics m, EchoObject settings)
    {
        EchoObject? sdf = SdfBlock(settings);

        EditorGUI.SectionHeader(paper, $"{id}_h_geometry", "Geometry", first: true);

        EditorGUI.Row(paper, $"{id}_unitScale", "Unit Scale", () =>
            Origami.NumericField<float>(paper, $"{id}_unitScale_v", Float(settings, "unitScale", 1f),
                v => settings["unitScale"] = new EchoObject(v)).Show());

        EditorGUI.SettingsToggle(paper, $"{id}_genNormals", "Generate Normals", Bool(settings, "generateNormals", true),
            v => settings["generateNormals"] = new EchoObject(v), separator: false);

        EditorGUI.SettingsToggle(paper, $"{id}_smoothNormals", "Smooth Normals", Bool(settings, "generateSmoothNormals", false),
            v => settings["generateSmoothNormals"] = new EchoObject(v), separator: false);

        EditorGUI.SettingsToggle(paper, $"{id}_tangents", "Calculate Tangents", Bool(settings, "calculateTangents", true),
            v => settings["calculateTangents"] = new EchoObject(v), separator: false);

        EditorGUI.SettingsToggle(paper, $"{id}_impBlendShapes", "Import Blend Shapes", Bool(settings, "importBlendShapes", true),
            v => settings["importBlendShapes"] = new EchoObject(v), separator: false);

        // Lightmapping generates a UV2 atlas for every mesh via Prowl.Unwrapper. Off by default:
        // it's slow (a full unwrap per mesh) and some models already ship their own UV2.
        EditorGUI.SettingsToggle(paper, $"{id}_lightmapUVs", "Generate Lightmap UVs (slow)", Bool(settings, "generateLightmapUVs", false),
            v => settings["generateLightmapUVs"] = new EchoObject(v), separator: false);

        EditorGUI.SectionHeader(paper, $"{id}_h_scene", "Scene");

        EditorGUI.SettingsToggle(paper, $"{id}_impCameras", "Import Cameras", Bool(settings, "importCameras", true),
            v => settings["importCameras"] = new EchoObject(v), separator: false);

        EditorGUI.SettingsToggle(paper, $"{id}_impLights", "Import Lights", Bool(settings, "importLights", true),
            v => settings["importLights"] = new EchoObject(v), separator: false);

        EditorGUI.Row(paper, $"{id}_sceneIndex", "Scene Index (-1 = default)", () =>
            Origami.NumericField<int>(paper, $"{id}_sceneIndex_v", Int(settings, "sceneIndex", -1),
                v => settings["sceneIndex"] = new EchoObject(v)).Min(-1).Show());

        EditorGUI.SectionHeader(paper, $"{id}_h_optimize", "Optimization");

        EditorGUI.SettingsToggle(paper, $"{id}_optMeshes", "Merge Sibling Meshes", Bool(settings, "optimizeMeshes", false),
            v => settings["optimizeMeshes"] = new EchoObject(v), separator: false);

        bool optimizeHierarchy = Bool(settings, "optimizeHierarchy", false);
        EditorGUI.SettingsToggle(paper, $"{id}_optGraph", "Collapse Empty Nodes", optimizeHierarchy,
            v => settings["optimizeHierarchy"] = new EchoObject(v), separator: false);

        if (optimizeHierarchy)
            EditorGUI.Row(paper, $"{id}_preserveNodes", "Never Collapse (comma separated)", () =>
                Origami.TextField(paper, $"{id}_preserveNodes_v", Str(settings, "preserveNodeNames", ""),
                    v => settings["preserveNodeNames"] = new EchoObject(v)).Show());

        EditorGUI.SettingsToggle(paper, $"{id}_strict", "Fail On Validation Errors", Bool(settings, "strictValidation", false),
            v => settings["strictValidation"] = new EchoObject(v), separator: false);

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
    }

    // ---- rig ----------------------------------------------------------------------------------

    private static void DrawRigSection(Paper paper, string id, OrigamiMetrics m, Scribe.FontFile font, EchoObject settings, AssetEntry entry)
    {
        var rigType = (ModelRigType)Int(settings, "rigType", (int)ModelRigType.Generic);

        EditorGUI.SectionHeader(paper, $"{id}_h_rig", "Rig", first: true);

        // Humanoid maps the bones to the human body, which is what lets a clip play on another rig.
        EditorGUI.Row(paper, $"{id}_rigType", "Rig Type", () =>
            Origami.EnumDropdown(paper, $"{id}_rigType_v", rigType,
                v => settings["rigType"] = new EchoObject((int)v)).Show());

        if (rigType == ModelRigType.None)
        {
            EditorGUI.Note(paper, $"{id}_rigNone", "This model imports as geometry only: no skeleton, and no animation.");
            return;
        }

        Avatar? avatar = FindAvatar(entry);
        if (avatar.IsNotValid())
        {
            EditorGUI.Note(paper, $"{id}_rigNoAvatar", "No rig has been built yet. Reimport to build one.");
            return;
        }

        EditorGUI.Row(paper, $"{id}_rigBones", "Bones", () =>
            paper.Box($"{id}_rigBones_v").Height(m.RowHeight).IsNotInteractable()
                .Text((avatar!.Skeleton?.BoneCount ?? 0).ToString(), font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft));

        EditorGUI.Row(paper, $"{id}_rigChannels", "Blend Shape Channels", () =>
            paper.Box($"{id}_rigChannels_v").Height(m.RowHeight).IsNotInteractable()
                .Text((avatar!.Skeleton?.FloatChannelCount ?? 0).ToString(), font).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft));

        if (rigType != ModelRigType.Humanoid) return;

        EditorGUI.SectionHeader(paper, $"{id}_h_humanoid", "Humanoid");

        if (avatar!.IsHuman)
        {
            int mapped = CountMappedBones(avatar);
            EditorGUI.Note(paper, $"{id}_rigOk",
                $"Mapped to the human body: {mapped} of {Prowl.Motion.HumanTrait.BoneCount} humanoid bones. Clips from this model retarget onto any humanoid rig.");
        }
        else
        {
            EditorGUI.Note(paper, $"{id}_rigFail",
                "The auto mapper could not recognise a humanoid in this skeleton, so the model plays as a generic rig and its clips will not retarget. Map the bones by hand in the Avatar editor.");
        }

        Guid modelGuid = entry.Guid;
        paper.Box($"{id}_openAvatar").Width(UnitValue.Auto).Height(30)
            .Margin(m.PaddingLarge, m.PaddingLarge, m.SpacingLarge, 0).Rounded(m.Rounding).Padding(16, 16, 0, 0)
            .BackgroundColor(avatar.IsHuman ? EditorTheme.Neutral300 : EditorTheme.Accent)
            .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .Hovered.BackgroundColor(avatar.IsHuman ? EditorTheme.Neutral400 : EditorTheme.AccentBright).End()
            .Text($"{EditorIcons.Bone}  Open Avatar Editor", font)
            .TextColor(avatar.IsHuman ? EditorTheme.Ink400 : System.Drawing.Color.White)
            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) => AvatarEditorWindow.OpenFor(modelGuid));
    }


    private static Avatar? FindAvatar(AssetEntry entry)
    {
        foreach (var sub in entry.SubAssets)
        {
            if (sub.Type == null || !typeof(Avatar).IsAssignableFrom(sub.Type)) continue;
            Avatar? avatar = AssetDatabase.Get<Avatar>(sub.Guid);
            if (avatar.IsValid()) return avatar;
        }
        return null;
    }

    private static int CountMappedBones(Avatar avatar)
    {
        Prowl.Motion.HumanDescription? description = avatar.Description;
        if (description == null) return 0;

        int mapped = 0;
        for (int i = 0; i < Prowl.Motion.HumanTrait.BoneCount; i++)
            if (description.GetSkeletonBoneIndex((Prowl.Motion.HumanBodyBone)i) >= 0) mapped++;
        return mapped;
    }

    // ---- animation ----------------------------------------------------------------------------

    private static void DrawAnimationTab(Paper paper, string id, OrigamiMetrics m, Scribe.FontFile font,
        EchoObject settings, AssetEntry entry, PreviewRenderer? preview, ViewState view)
    {
        DrawRigSection(paper, id, m, font, settings, entry);

        EditorGUI.SectionHeader(paper, $"{id}_h_animSettings", "Import");

        bool importAnimations = Bool(settings, "importAnimations", true);
        EditorGUI.SettingsToggle(paper, $"{id}_impAnimations", "Import Animations", importAnimations,
            v => settings["importAnimations"] = new EchoObject(v), separator: false);

        // Model formats carry no looping flag, so the import decides.
        if (importAnimations)
        {
            EditorGUI.SettingsToggle(paper, $"{id}_loopAnims", "Loop Animations", Bool(settings, "loopAnimations", true),
                v => settings["loopAnimations"] = new EchoObject(v), separator: false);

            EditorGUI.Row(paper, $"{id}_sampleRate", "Sample Rate (fps)", () =>
                Origami.NumericField<float>(paper, $"{id}_sampleRate_v", Float(settings, "animationSampleRate", 30f),
                    v => settings["animationSampleRate"] = new EchoObject(v)).Min(1).Max(240).Show());
        }

        EditorGUI.SectionHeader(paper, $"{id}_h_clips", "Clips");

        if (preview == null || preview.Clips.Count == 0)
        {
            EditorGUI.Note(paper, $"{id}_noClips", "This model has no animation clips.");
            return;
        }

        DrawClipTable(paper, id, m, preview, view);
        DrawTransport(paper, id, m, font, preview, view);
        DrawClipSettings(paper, id, m, font, settings, preview, view);
    }

    /// <summary>The settings of the selected clip, keyed by its name in the file.</summary>
    private static void DrawClipSettings(Paper paper, string id, OrigamiMetrics m, Scribe.FontFile font,
        EchoObject settings, PreviewRenderer preview, ViewState view)
    {
        if (view.Clip < 0 || view.Clip >= preview.Clips.Count) return;

        AnimationClip clip = preview.Clips[view.Clip];
        string sourceName = clip.SourceName.Length > 0 ? clip.SourceName : clip.Name;

        EchoObject? block = Importers.ModelImportOverrides.ReadClipBlock(settings, sourceName);
        EchoObject Write() => Importers.ModelImportOverrides.ClipBlock(settings, sourceName);

        EditorGUI.SectionHeader(paper, $"{id}_h_clipSettings", $"Clip: {clip.Name}");

        EditorGUI.Row(paper, $"{id}_clipName", "Name", () =>
            Origami.TextField(paper, $"{id}_clipName_v", Str(block, Importers.ModelImportKeys.ClipName, sourceName),
                v => Write()[Importers.ModelImportKeys.ClipName] = new EchoObject(v)).Show());

        EditorGUI.SettingsToggle(paper, $"{id}_clipLoop", "Loop",
            Bool(block, Importers.ModelImportKeys.ClipLoop, Bool(settings, "loopAnimations", true)),
            v => Write()[Importers.ModelImportKeys.ClipLoop] = new EchoObject(v), separator: false);

        // Trimming is in seconds from the take's start.
        EditorGUI.Row(paper, $"{id}_clipTrimStart", "Trim Start (s)", () =>
            Origami.NumericField<float>(paper, $"{id}_clipTrimStart_v", Float(block, Importers.ModelImportKeys.ClipTrimStart, 0f),
                v => Write()[Importers.ModelImportKeys.ClipTrimStart] = new EchoObject(MathF.Max(0f, v))).Min(0f).Show());

        EditorGUI.Row(paper, $"{id}_clipTrimEnd", "Trim End (s, 0 = full)", () =>
            Origami.NumericField<float>(paper, $"{id}_clipTrimEnd_v", Float(block, Importers.ModelImportKeys.ClipTrimEnd, 0f),
                v => Write()[Importers.ModelImportKeys.ClipTrimEnd] = new EchoObject(MathF.Max(0f, v))).Min(0f).Show());

        // What the body does across the clip can move the character instead of staying in the pose.
        EditorGUI.SectionHeader(paper, $"{id}_h_clipRoot", "Root Motion");
        EditorGUI.SettingsToggle(paper, $"{id}_clipRootTravel", "Travel",
            Bool(block, Importers.ModelImportKeys.ClipRootTravel, true),
            v => Write()[Importers.ModelImportKeys.ClipRootTravel] = new EchoObject(v), separator: false);
        EditorGUI.SettingsToggle(paper, $"{id}_clipRootTurn", "Turn",
            Bool(block, Importers.ModelImportKeys.ClipRootTurn, true),
            v => Write()[Importers.ModelImportKeys.ClipRootTurn] = new EchoObject(v), separator: false);
        EditorGUI.SettingsToggle(paper, $"{id}_clipRootHeight", "Height",
            Bool(block, Importers.ModelImportKeys.ClipRootHeight, false),
            v => Write()[Importers.ModelImportKeys.ClipRootHeight] = new EchoObject(v), separator: false);

        // Markers the graph and gameplay read: footsteps, named moments, windows for warps and transitions.
        EditorGUI.SectionHeader(paper, $"{id}_h_clipEvents", "Events");
        view.Events.Draw(paper, $"{id}_clipEvents", font, settings, sourceName,
            clip.TakeStart, clip.Duration, view.Time, t =>
            {
                view.Time = Math.Clamp(t, 0f, 1f);
                view.Playing = false;
            });

        EditorGUI.Note(paper, $"{id}_clipTrimNote",
            $"The imported clip is {clip.Duration:0.00}s at {clip.FrameCount} frames. Trimming, renaming, root motion and events take effect on the next import. Event times are in seconds of the take, before trimming.");
    }

    private static void DrawClipTable(Paper paper, string id, OrigamiMetrics m, PreviewRenderer preview, ViewState view)
    {
        var clips = preview.Clips;

        using (paper.Box($"{id}_clipTableWrap").Height(UnitValue.Auto)
            .Margin(m.PaddingLarge, m.PaddingLarge, 0, m.Spacing).Enter())
        {
            // No internal scroll, the inspector scrolls instead.
            var table = Origami.Table(paper, $"{id}_clipTable", view.Clip, i => Select(view, i))
                .Bordered(false)
                .RowHeight(m.RowHeight)
                .Column("Name", 2.2f)
                .Column("Length", 0.8f, align: TextAlignment.MiddleRight)
                .Column("Frames", 0.7f, align: TextAlignment.MiddleRight)
                .Column("Kind", 0.9f)
                .IsSelected(i => i == view.Clip)
                .OnSelectModified((i, _, _) => Select(view, i))
                .OnRowActivate(i => { Select(view, i); view.Playing = true; });

            foreach (AnimationClip clip in clips)
            {
                table.Row()
                    .Cell(clip.Name, EditorTheme.Ink700)
                    .CellRight($"{clip.Duration:0.00}s", EditorTheme.Ink400)
                    .CellRight(clip.FrameCount.ToString(), EditorTheme.Ink400)
                    .Cell(clip.Kind == AnimationClipKind.Humanoid ? "Humanoid" : "Skeletal", EditorTheme.Ink400);
            }

            table.Show();
        }
    }

    private static void Select(ViewState view, int index)
    {
        if (view.Clip == index) return;
        view.Clip = index;
        view.Time = 0f;
        view.Events.Reset();
    }

    private static void DrawTransport(Paper paper, string id, OrigamiMetrics m, Scribe.FontFile font,
        PreviewRenderer preview, ViewState view)
    {
        if (!preview.CanAnimate)
        {
            EditorGUI.Note(paper, $"{id}_noRig", "The preview cannot play these clips: the model has no rig bound to it.");
            return;
        }

        AnimationClip? clip = view.Clip >= 0 && view.Clip < preview.Clips.Count ? preview.Clips[view.Clip] : null;
        if (clip.IsNotValid())
        {
            EditorGUI.Note(paper, $"{id}_pickClip", "Pick a clip to play it in the preview.");
            return;
        }

        using (paper.Row($"{id}_transport").Height(UnitValue.Auto)
            .Margin(m.PaddingLarge, m.PaddingLarge, m.Spacing, m.Spacing).Gap(m.SpacingMedium)
            .AlignItems(LayoutAlignment.Center).Enter())
        {
            bool playing = view.Playing;
            paper.Box($"{id}_playBtn").Width(34).Height(26).Rounded(m.Rounding)
                .BackgroundColor(playing ? EditorTheme.Accent : EditorTheme.Neutral300)
                .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
                .Text(playing ? EditorIcons.CirclePause : EditorIcons.CirclePlay, font)
                .TextColor(playing ? System.Drawing.Color.White : EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
                .OnClick(0, (_, _) => view.Playing = !view.Playing);

            using (paper.Box($"{id}_scrubWrap").Width(UnitValue.Stretch()).Height(26)
                .AlignItems(LayoutAlignment.Center).Enter())
            {
                // Scrubbing stops playback.
                Origami.Slider(paper, $"{id}_scrub", view.Time, v =>
                {
                    view.Time = Math.Clamp(v, 0f, 1f);
                    view.Playing = false;
                }, 0f, 1f).Show();
            }

            paper.Box($"{id}_timeLabel").Width(78).Height(26).IsNotInteractable()
                .Text($"{view.Time * clip!.Duration:0.00} / {clip.Duration:0.00}s", font)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleRight);
        }
    }

    // ---- materials and contents ----------------------------------------------------------------

    private static void DrawMaterialsTab(Paper paper, string id, OrigamiMetrics m, Scribe.FontFile font, EchoObject settings, AssetEntry entry)
    {
        EditorGUI.SectionHeader(paper, $"{id}_h_matSettings", "Import", first: true);

        EditorGUI.SettingsToggle(paper, $"{id}_impMaterials", "Import Materials", Bool(settings, "importMaterials", true),
            v => settings["importMaterials"] = new EchoObject(v), separator: false);

        EditorGUI.SectionHeader(paper, $"{id}_h_matList", "Materials");

        List<MaterialSlot> slots = CollectMaterials(settings, entry);
        if (slots.Count == 0)
        {
            EditorGUI.Note(paper, $"{id}_noMats", "This model defines no materials.");
            return;
        }

        EditorGUI.Note(paper, $"{id}_matNote",
            "An extracted material is a real asset in the project: edits to it survive a reimport, and the model points at it. Delete it and the next import rebuilds an embedded one.");

        for (int i = 0; i < slots.Count; i++)
            DrawMaterialSlot(paper, $"{id}_mat{i}", m, font, settings, entry, slots[i]);
    }

    /// <summary>One material the model defines, and where it currently comes from.</summary>
    private readonly struct MaterialSlot
    {
        public MaterialSlot(string name, Guid embedded, Guid extracted, bool missing)
        {
            Name = name; Embedded = embedded; Extracted = extracted; Missing = missing;
        }

        public string Name { get; }
        /// <summary>The sub-asset this import built, or empty when the slot is extracted.</summary>
        public Guid Embedded { get; }
        /// <summary>The asset the settings point at, or empty when nothing is extracted.</summary>
        public Guid Extracted { get; }
        /// <summary>The settings point somewhere, but nothing is there any more.</summary>
        public bool Missing { get; }
    }

    // The full set is the materials this import owns plus the ones that were extracted out of it,
    // since an extracted material leaves no sub-asset behind.
    private static List<MaterialSlot> CollectMaterials(EchoObject settings, AssetEntry entry)
    {
        var slots = new List<MaterialSlot>();
        var seen = new HashSet<string>();

        foreach (var sub in entry.SubAssets)
        {
            if (sub.Type == null || !typeof(Material).IsAssignableFrom(sub.Type)) continue;
            if (!seen.Add(sub.Name)) continue;
            slots.Add(new MaterialSlot(sub.Name, sub.Guid, Guid.Empty, false));
        }

        if (settings.TryGet(Importers.ModelImportKeys.MaterialRemap, out EchoObject remap))
            foreach (KeyValuePair<string, EchoObject> pair in remap.Tags)
            {
                if (!seen.Add(pair.Key)) continue;
                Guid guid = Guid.TryParse(pair.Value.StringValue, out Guid g) ? g : Guid.Empty;
                bool missing = guid == Guid.Empty || EditorAssetBackend.Instance?.GuidToPath(guid) == null;
                slots.Add(new MaterialSlot(pair.Key, Guid.Empty, guid, missing));
            }

        slots.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return slots;
    }

    private static void DrawMaterialSlot(Paper paper, string id, OrigamiMetrics m, Scribe.FontFile font,
        EchoObject settings, AssetEntry entry, MaterialSlot slot)
    {
        string state = slot.Extracted != Guid.Empty
            ? (slot.Missing ? "missing" : EditorAssetBackend.Instance?.GuidToPath(slot.Extracted) ?? "extracted")
            : "embedded";

        EditorGUI.Row(paper, $"{id}_row", slot.Name, () =>
        {
            using (paper.Row($"{id}_actions").Height(m.RowHeight).Gap(m.SpacingMedium).AlignItems(LayoutAlignment.Center).Enter())
            {
                paper.Box($"{id}_state").Width(UnitValue.Stretch()).Height(m.RowHeight).IsNotInteractable()
                    .Text(state, font)
                    .TextColor(slot.Missing ? EditorTheme.Red400 : EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

                if (slot.Extracted == Guid.Empty)
                    SmallButton(paper, $"{id}_extract", "Extract", () => Extract(settings, entry, slot));
                else
                {
                    SmallButton(paper, $"{id}_reextract", slot.Missing ? "Re-extract" : "Replace", () => Extract(settings, entry, slot));
                    SmallButton(paper, $"{id}_reset", "Reset",
                        () => Importers.ModelImportOverrides.SetExtractedMaterial(settings, slot.Name, Guid.Empty));
                }
            }
        });
    }

    private static void Extract(EchoObject settings, AssetEntry entry, MaterialSlot slot)
    {
        // An extracted slot has no sub-asset to copy, so a re-extract needs the model reimported
        // against the embedded material first. Resetting is the way back to one.
        Guid source = slot.Embedded;
        if (source == Guid.Empty)
        {
            Debug.LogWarning($"[Model] '{slot.Name}' is already pointing at an asset. Reset it and apply, then extract again.");
            return;
        }

        Material? material = AssetDatabase.Get<Material>(source);
        if (material.IsNotValid())
        {
            Debug.LogWarning($"[Model] '{slot.Name}' could not be loaded, so there is nothing to extract.");
            return;
        }

        string? path = EditorAssetBackend.Instance?.GuidToPath(entry.Guid);
        Guid guid = Importers.ModelImportOverrides.ExtractMaterial(material!, path ?? "", slot.Name);
        if (guid == Guid.Empty) return;

        Importers.ModelImportOverrides.SetExtractedMaterial(settings, slot.Name, guid);
    }

    private static void SmallButton(Paper paper, string id, string label, Action onClick)
        => EditorGUI.PillButton(paper, id, label, active: false, onClick).Height(Origami.Current.Metrics.RowHeight - 2).Padding(10, 10, 0, 0);
}
