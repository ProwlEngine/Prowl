// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;
using System.Numerics;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Inspector;
using Prowl.Editor.Lightmapping;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using static Prowl.Graphite.Rendering.PropertyIDText;

using static Prowl.Editor.GUI.EditorGUI;

using Color = System.Drawing.Color;
using Material = Prowl.Runtime.Resources.Material;
using MaterialProperty = Prowl.Graphite.Rendering.MaterialProperty;
using Shader = Prowl.Runtime.Resources.Shader;
namespace Prowl.Editor.GUI.Panels;

public class EnvironmentPanel : DockPanel
{
    [MenuItem("Window/General/Environment", priority: 6)]
    static void Open() => EditorApplication.Instance?.OpenPanel<EnvironmentPanel>();

    public override string Title => Loc.Get("panel.environment");
    public override string Icon => EditorIcons.Sun;

    private readonly LightmapBakeService _bake = new();
    private string _cat = "sky";

    // label holds a localization key, resolved via Loc.Get at render (see the Sidebar call).
    private static readonly (string id, string label, string icon)[] Cats =
    [
        ("sky",      "env.tab_sky",      EditorIcons.Sun),
        ("fog",      "env.tab_fog",      EditorIcons.Cloud),
        ("ambient",  "env.tab_ambient",  EditorIcons.Lightbulb),
        ("lightmap", "env.tab_lightmap", EditorIcons.TableCellsLarge),
    ];

    public override void OnGUI(Paper paper, float width, float height)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        _bake.Poll();

        SceneInstance scene = EditorSceneManager.EditedWorld;
        if (scene.IsNotValid())
        {
            paper.Box("env_noscene").Height(30)
                .Text(Loc.Get("selector.no_scene"), font).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter);
            return;
        }

        using (paper.Row("env_root").Width(width).Height(height).Clip().Enter())
        {
            (string id, string, string icon)[] cats = [.. Cats.Select(c => (c.id, Loc.Get(c.label), c.icon))];
            float side = EditorGUI.Sidebar(paper, "env_side", cats, _cat, c => _cat = c);
            paper.Box("env_vdiv").Width(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

            Origami.ScrollView(paper, "env_scroll", width - side - 1, height).Body(() =>
            {
                using (paper.Column("env_content").Height(UnitValue.Auto).Padding(0, 0, 8, 12).Enter())
                {
                    switch (_cat)
                    {
                        case "fog":      DrawFogSection(paper, "env_fog", font, scene); DrawVolumetricFogSection(paper, "env_vfog", scene); break;
                        case "ambient":  DrawAmbientSection(paper, "env_amb", font, scene); break;
                        case "lightmap": DrawLightmappingSection(paper, "env_lm", font, scene); break;
                        default:         DrawSkySection(paper, "env_sky", font, scene); DrawCloudsSection(paper, "env_clouds", scene); break;
                    }
                }
            });
        }
    }

    private static readonly (BuiltInSky Sky, string Label)[] s_newSkies =
    [
        (BuiltInSky.Procedural, "env.sky_procedural"),
        (BuiltInSky.Gradient, "env.sky_gradient"),
        (BuiltInSky.Cubemap, "env.sky_cubemap"),
        (BuiltInSky.Panorama, "env.sky_panorama"),
    ];

    private void DrawSkySection(Paper paper, string id, Scribe.FontFile font, SceneInstance scene)
    {
        SceneEnvironment env = scene.Environment;

        EditorGUI.SectionHeader(paper, $"{id}_h", Loc.Get("env.sky"), first: true);

        EditorGUI.SettingsRow(paper, $"{id}_mat", Loc.Get("env.sky_material"), () =>
            PropertyGridUtils.DrawField(paper, $"{id}_mat_v", "", typeof(Material), env.SkyMaterial,
                v => { RecordEnvironment("Sky Material"); env.SkyMaterial = v as Material; }, 0));

        EditorGUI.SettingsRow(paper, $"{id}_new", Loc.Get("env.new_sky"), () =>
        {
            using (paper.Row($"{id}_new_row").Height(28).Gap(6).Enter())
                foreach ((BuiltInSky sky, string label) in s_newSkies)
                    SmallButton(paper, $"{id}_new_{sky}", Loc.Get(label), () =>
                    {
                        RecordEnvironment("Sky Material");
                        env.SkyMaterial = SceneEnvironment.CreateSkyMaterial(sky);
                    });
        });

        EditorGUI.SettingsRow(paper, $"{id}_bg", Loc.Get("env.background_color"), () =>
            Origami.ColorField(paper, $"{id}_bg_v", env.SkyColor, v => { RecordEnvironment("Background Color"); env.SkyColor = v; }).Show());

        Material? material = env.SkyMaterial;
        if (material.IsNotValid())
        {
            Hint(paper, $"{id}_none", font, Loc.Get("env.no_sky_hint"));
        }
        else if (material.AssetID != Guid.Empty)
        {
            Hint(paper, $"{id}_asset", font, Loc.Get("env.sky_asset_hint"));
            EditorGUI.SettingsRow(paper, $"{id}_embed", "", () =>
                SmallButton(paper, $"{id}_embed_b", Loc.Get("env.make_embedded"), () =>
                {
                    RecordEnvironment("Sky Material");
                    env.SkyMaterial = material.Clone();
                }));
        }
        else
        {
            EditorGUI.SectionHeader(paper, $"{id}_h_props", Loc.Get("env.sky_properties"));
            Shader? shader = material.Shader;
            if (shader.IsValid())
            {
                Undo.InspectorBaseline(material);
                foreach (MaterialProperty prop in shader.Properties)
                    MaterialPropertyDrawer.DrawPropertyRow(paper, $"{id}_p_{prop.Name.Text()}", material, prop, () => Undo.InspectorChanged(material, ActiveSceneOwner));
                if (shader.Name == "Graphite/Sky/Procedural")
                    Hint(paper, $"{id}_sun", font, Loc.Get("env.sun_auto"));
            }
        }

        EditorGUI.SectionHeader(paper, $"{id}_h_cap", Loc.Get("env.sky_capture"));
        SceneEnvironment.SkyCaptureSettings capture = env.SkyCapture;
        void Capture() { RecordEnvironment("Sky Capture"); env.SkyCapture = capture; }
        EditorGUI.SettingsRow(paper, $"{id}_cap_mode", Loc.Get("env.mode"), () =>
            Origami.EnumDropdown(paper, $"{id}_cap_mode_v", capture.Mode, v => { capture.Mode = v; Capture(); }).Show());
        EditorGUI.SettingsRow(paper, $"{id}_cap_res", Loc.Get("env.capture_resolution"), () =>
            Origami.IntSlider(paper, $"{id}_cap_res_v", capture.Resolution, v => { capture.Resolution = (int)BitOperations.RoundUpToPowerOf2((uint)Math.Clamp(v, 16, 2048)); Capture(); }, 16, 2048).Show());
        if (capture.Mode == SkyCaptureMode.OnChange)
            EditorGUI.SettingsRow(paper, $"{id}_cap_int", Loc.Get("env.capture_interval"), () =>
                Origami.IntSlider(paper, $"{id}_cap_int_v", capture.Interval, v => { capture.Interval = v; Capture(); }, 1, 30).Show());
    }

    private void DrawFogSection(Paper paper, string id, Scribe.FontFile font, SceneInstance scene)
    {
        EditorGUI.SectionHeader(paper, $"{id}_h", Loc.Get("env.fog"), first: true);

        SceneEnvironment.FogSettings fog = scene.Environment.Fog;
        void Apply() { RecordEnvironment("Fog"); scene.Environment.Fog = fog; }

        EditorGUI.SettingsRow(paper, $"{id}_mode", Loc.Get("env.mode"), () =>
            Origami.EnumDropdown(paper, $"{id}_mode_v", fog.Mode, v => { fog.Mode = v; Apply(); }).Show());

        if (fog.Mode == FogMode.Off) return;

        EditorGUI.SettingsToggle(paper, $"{id}_sky", Loc.Get("env.fog_use_sky"), fog.UseSky, v => { fog.UseSky = v; Apply(); });
        if (!fog.UseSky)
        {
            EditorGUI.SettingsRow(paper, $"{id}_color", Loc.Get("env.color"), () =>
                Origami.ColorField(paper, $"{id}_color_v", fog.Color, v => { fog.Color = v; Apply(); }).Show());
            EditorGUI.SettingsRow(paper, $"{id}_int", Loc.Get("env.intensity") + " (nits)", () =>
                Origami.NumericField<float>(paper, $"{id}_int_v", fog.Intensity, v => { fog.Intensity = MathF.Max(0f, v); Apply(); }).Show());
        }

        if (fog.Mode == FogMode.Linear)
        {
            EditorGUI.SettingsRow(paper, $"{id}_start", Loc.Get("env.start_distance"), () =>
                Origami.NumericField<float>(paper, $"{id}_start_v", fog.Start, v => { fog.Start = v; Apply(); }).Show());
            EditorGUI.SettingsRow(paper, $"{id}_end", Loc.Get("env.end_distance"), () =>
                Origami.NumericField<float>(paper, $"{id}_end_v", fog.End, v => { fog.End = v; Apply(); }).Show());
        }
        else
        {
            EditorGUI.SettingsRow(paper, $"{id}_density", Loc.Get("env.density"), () =>
                Origami.Slider(paper, $"{id}_density_v", fog.Density, v => { fog.Density = v; Apply(); }, 0f, 0.1f).Format("F4").Show());
        }
    }

    private void DrawCloudsSection(Paper paper, string id, SceneInstance scene)
    {
        EditorGUI.SectionHeader(paper, $"{id}_h", Loc.Get("env.volumetric_clouds"));

        SceneEnvironment.VolumetricCloudsSettings clouds = scene.Environment.VolumetricClouds;
        static void Record() => RecordEnvironment("Clouds");

        EditorGUI.SettingsToggle(paper, $"{id}_on", Loc.Get("env.enabled"), clouds.Enabled, v => { Record(); clouds.Enabled = v; });
        if (!clouds.Enabled) return;

        EditorGUI.SettingsRow(paper, $"{id}_coverage", Loc.Get("env.coverage"), () =>
            Origami.Slider(paper, $"{id}_coverage_v", clouds.Coverage, v => { Record(); clouds.Coverage = v; }, 0f, 1f).Show());
        EditorGUI.SettingsRow(paper, $"{id}_density", Loc.Get("env.density"), () =>
            Origami.Slider(paper, $"{id}_density_v", clouds.Density, v => { Record(); clouds.Density = v; }, 0f, 4f).Show());
        EditorGUI.SettingsRow(paper, $"{id}_altitude", Loc.Get("env.altitude"), () =>
            Origami.NumericField<float>(paper, $"{id}_altitude_v", clouds.Altitude, v => { Record(); clouds.Altitude = v; }).Show());
        EditorGUI.SettingsRow(paper, $"{id}_thickness", Loc.Get("env.thickness"), () =>
            Origami.NumericField<float>(paper, $"{id}_thickness_v", clouds.Thickness, v => { Record(); clouds.Thickness = MathF.Max(1f, v); }).Show());
        EditorGUI.SettingsRow(paper, $"{id}_wind", Loc.Get("env.wind_speed"), () =>
            Origami.NumericField<float>(paper, $"{id}_wind_v", clouds.WindSpeed, v => { Record(); clouds.WindSpeed = v; }).Show());
        EditorGUI.SettingsRow(paper, $"{id}_winddir", Loc.Get("env.wind_direction"), () =>
            Origami.Slider(paper, $"{id}_winddir_v", clouds.WindDirection, v => { Record(); clouds.WindDirection = v; }, 0f, 360f).Show());
        EditorGUI.SettingsRow(paper, $"{id}_sun", Loc.Get("env.sun_brightness"), () =>
            Origami.Slider(paper, $"{id}_sun_v", clouds.SunBrightness, v => { Record(); clouds.SunBrightness = v; }, 0f, 6f).Show());
        EditorGUI.SettingsRow(paper, $"{id}_ambient", Loc.Get("env.ambient_brightness"), () =>
            Origami.Slider(paper, $"{id}_ambient_v", clouds.AmbientBrightness, v => { Record(); clouds.AmbientBrightness = v; }, 0f, 4f).Show());
        EditorGUI.SettingsToggle(paper, $"{id}_temporal", Loc.Get("env.temporal"), clouds.Temporal, v => { Record(); clouds.Temporal = v; });
        EditorGUI.SettingsRow(paper, $"{id}_shadow", Loc.Get("env.shadow_resolution"), () =>
            Origami.NumericField<int>(paper, $"{id}_shadow_v", clouds.ShadowResolution, v => { Record(); clouds.ShadowResolution = Math.Clamp(v, 64, 2048); }).Show());
        EditorGUI.SettingsToggle(paper, $"{id}_high", Loc.Get("env.high_layer"), clouds.HighLayer, v => { Record(); clouds.HighLayer = v; });
    }

    private void DrawVolumetricFogSection(Paper paper, string id, SceneInstance scene)
    {
        EditorGUI.SectionHeader(paper, $"{id}_h", Loc.Get("env.volumetric_fog"));

        SceneEnvironment.VolumetricFogSettings fog = scene.Environment.VolumetricFog;
        void Apply() { RecordEnvironment("Volumetric Fog"); scene.Environment.VolumetricFog = fog; }

        EditorGUI.SettingsToggle(paper, $"{id}_on", Loc.Get("env.enabled"), fog.Enabled, v => { fog.Enabled = v; Apply(); });
        if (!fog.Enabled) return;

        EditorGUI.SettingsRow(paper, $"{id}_density", Loc.Get("env.density"), () =>
            Origami.Slider(paper, $"{id}_density_v", fog.Density, v => { fog.Density = v; Apply(); }, 0f, 0.2f).Format("F4").Show());
        EditorGUI.SettingsRow(paper, $"{id}_base", Loc.Get("env.base_height"), () =>
            Origami.NumericField<float>(paper, $"{id}_base_v", fog.BaseHeight, v => { fog.BaseHeight = v; Apply(); }).Show());
        EditorGUI.SettingsRow(paper, $"{id}_falloff", Loc.Get("env.height_falloff"), () =>
            Origami.Slider(paper, $"{id}_falloff_v", fog.HeightFalloff, v => { fog.HeightFalloff = v; Apply(); }, 0f, 1f).Format("F3").Show());
        EditorGUI.SettingsRow(paper, $"{id}_albedo", Loc.Get("env.albedo"), () =>
            Origami.ColorField(paper, $"{id}_albedo_v", fog.Albedo, v => { fog.Albedo = v; Apply(); }).Show());
        EditorGUI.SettingsRow(paper, $"{id}_aniso", Loc.Get("env.anisotropy"), () =>
            Origami.Slider(paper, $"{id}_aniso_v", fog.Anisotropy, v => { fog.Anisotropy = v; Apply(); }, -0.95f, 0.95f).Show());
        EditorGUI.SettingsRow(paper, $"{id}_ambient", Loc.Get("env.ambient_scale"), () =>
            Origami.Slider(paper, $"{id}_ambient_v", fog.AmbientScale, v => { fog.AmbientScale = v; Apply(); }, 0f, 4f).Show());
        EditorGUI.SettingsRow(paper, $"{id}_distance", Loc.Get("env.distance"), () =>
            Origami.NumericField<float>(paper, $"{id}_distance_v", fog.Distance, v => { fog.Distance = MathF.Max(1f, v); Apply(); }).Show());
        EditorGUI.SettingsRow(paper, $"{id}_slices", Loc.Get("env.slices"), () =>
            Origami.NumericField<int>(paper, $"{id}_slices_v", fog.Slices, v => { fog.Slices = Math.Clamp(v, 16, 256); Apply(); }).Show());
        EditorGUI.SettingsRow(paper, $"{id}_temporal", Loc.Get("env.temporal_blend"), () =>
            Origami.Slider(paper, $"{id}_temporal_v", fog.TemporalBlend, v => { fog.TemporalBlend = v; Apply(); }, 0f, 0.98f).Show());
    }

    private void DrawAmbientSection(Paper paper, string id, Scribe.FontFile font, SceneInstance scene)
    {
        EditorGUI.SectionHeader(paper, $"{id}_h", Loc.Get("env.ambient_lighting"), first: true);

        SceneEnvironment.AmbientSettings ambient = scene.Environment.Ambient;
        void Apply() { RecordEnvironment("Ambient"); scene.Environment.Ambient = ambient; }

        EditorGUI.SettingsRow(paper, $"{id}_mode", Loc.Get("env.mode"), () =>
            Origami.EnumDropdown(paper, $"{id}_mode_v", ambient.Mode, v => { ambient.Mode = v; Apply(); }).Show());

        if (ambient.Mode == AmbientMode.Sky)
            EditorGUI.SettingsRow(paper, $"{id}_int", Loc.Get("env.intensity"), () =>
                Origami.Slider(paper, $"{id}_int_v", ambient.Intensity, v => { ambient.Intensity = v; Apply(); }, 0f, 4f).Show());
        else
            EditorGUI.SettingsRow(paper, $"{id}_int", Loc.Get("env.intensity") + " (nits)", () =>
                Origami.NumericField<float>(paper, $"{id}_int_v", ambient.Intensity, v => { ambient.Intensity = MathF.Max(0f, v); Apply(); }).Show());

        switch (ambient.Mode)
        {
            case AmbientMode.Uniform:
                EditorGUI.SettingsRow(paper, $"{id}_color", Loc.Get("env.color"), () =>
                    Origami.ColorField(paper, $"{id}_color_v", ambient.Color, v => { ambient.Color = v; Apply(); }).Show());
                break;
            case AmbientMode.Hemisphere:
                EditorGUI.SettingsRow(paper, $"{id}_sky", Loc.Get("env.sky_color"), () =>
                    Origami.ColorField(paper, $"{id}_sky_v", ambient.SkyColor, v => { ambient.SkyColor = v; Apply(); }).Show());
                EditorGUI.SettingsRow(paper, $"{id}_gnd", Loc.Get("env.ground_color"), () =>
                    Origami.ColorField(paper, $"{id}_gnd_v", ambient.GroundColor, v => { ambient.GroundColor = v; Apply(); }).Show());
                break;
            default:
                Hint(paper, $"{id}_hint", font, Loc.Get("env.sky_ambient_hint"));
                break;
        }
    }

    // Settings are the active section's, recorded against that section so an undo after the active scene changed still
    // reaches and marks the right one.
    private static UndoOwner ActiveSceneOwner => UndoOwner.OfScene(EditorSceneManager.SceneOf(EditorSceneManager.EditedWorld.ActiveSection));

    private static void RecordEnvironment(string name)
    {
        SceneSection section = EditorSceneManager.EditedWorld.ActiveSection;
        Undo.RecordObject(() => section.IsLoaded ? section.Environment : null, ActiveSceneOwner, name);
    }

    private static void Hint(Paper paper, string id, Scribe.FontFile font, string text)
        => paper.Box(id).Height(20).Margin(Origami.Current.Metrics.PaddingLarge, 0, 0, 0)
            .Text(text, font).TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

    private static void SmallButton(Paper paper, string id, string label, Action onClick)
        => Origami.Button(paper, id, label, onClick).Small().Show();

    private void DrawLightmappingSection(Paper paper, string id, Scribe.FontFile font, SceneInstance scene)
    {
        // IsBaking alone doesn't say WHICH scene it's for - only one bake can run at a time, so
        // switching to a different scene while it's in flight must not show this scene as baking too.
        bool baking = _bake.IsBaking && _bake.TargetScene == scene;
        LightmapBakeSettings s = scene.LightmapBake;
        SceneSection section = scene.ActiveSection;
        void Record() => Undo.RecordObject(() => section.IsLoaded ? section.LightmapBake : null, ActiveSceneOwner, "Lightmap Settings");

        EditorGUI.SectionHeader(paper, $"{id}_h_res", Loc.Get("game.resolution"), first: true);

        EditorGUI.SettingsRow(paper, $"{id}_size", Loc.Get("env.atlas_size"), () =>
            Origami.IntSlider(paper, $"{id}_size_v", s.AtlasSize, v => { if (!baking) { Record(); s.AtlasSize = v; } }, 256, 4096).Show());
        EditorGUI.SettingsRow(paper, $"{id}_tpu", Loc.Get("env.texels_unit"), () =>
            Origami.Slider(paper, $"{id}_tpu_v", s.TexelsPerUnit, v => { if (!baking) { Record(); s.TexelsPerUnit = v; } }, 1f, 100f).Format("F0").Show());
        EditorGUI.SettingsRow(paper, $"{id}_dil", Loc.Get("env.padding_dilate"), () =>
            Origami.IntSlider(paper, $"{id}_dil_v", s.DilatePixels, v => { if (!baking) { Record(); s.DilatePixels = v; } }, 0, 16).Show());

        EditorGUI.SectionHeader(paper, $"{id}_h_qual", Loc.Get("env.quality"));

        EditorGUI.SettingsRow(paper, $"{id}_bnc", Loc.Get("env.bounces"), () =>
            Origami.IntSlider(paper, $"{id}_bnc_v", s.Bounces, v => { if (!baking) { Record(); s.Bounces = v; } }, 0, 8).Show());
        EditorGUI.SettingsRow(paper, $"{id}_smp", Loc.Get("env.indirect_samples"), () =>
            Origami.IntSlider(paper, $"{id}_smp_v", s.Samples, v => { if (!baking) { Record(); s.Samples = v; } }, 1, 1024).Show());
        EditorGUI.SettingsRow(paper, $"{id}_psmp", Loc.Get("env.probe_samples"), () =>
            Origami.IntSlider(paper, $"{id}_psmp_v", s.ProbeSamples, v => { if (!baking) { Record(); s.ProbeSamples = v; } }, 16, 2048).Show());
        EditorGUI.SettingsToggle(paper, $"{id}_cull", Loc.Get("env.backface_cull"), s.DoBackfaceCull, v => { if (!baking) { Record(); s.DoBackfaceCull = v; } });
        EditorGUI.SettingsRow(paper, $"{id}_spr", Loc.Get("env.sparse_stride"), () =>
            Origami.IntSlider(paper, $"{id}_spr_v", s.SparseStride, v => { if (!baking) { Record(); s.SparseStride = v; } }, 1, 16).Show());

        EditorGUI.SectionHeader(paper, $"{id}_h_env", Loc.Get("panel.environment"));
        EditorGUI.SettingsToggle(paper, $"{id}_sky", Loc.Get("env.bake_sky_gi"), s.BakeSkyLighting, v => { if (!baking) { Record(); s.BakeSkyLighting = v; } });

        EditorGUI.SectionHeader(paper, $"{id}_h_adv", Loc.Get("env.advanced"));
        EditorGUI.SettingsToggle(paper, $"{id}_alb", Loc.Get("env.ignore_albedo"), s.IgnoreAlbedo, v => { if (!baking) { Record(); s.IgnoreAlbedo = v; } });

        DrawBakeCard(paper, id, font, scene, s, baking);
    }

    private void DrawBakeCard(Paper paper, string id, Scribe.FontFile font, SceneInstance scene,
        LightmapBakeSettings s, bool baking)
    {
        OrigamiMetrics m = Origami.Current.Metrics;
        Scribe.FontFile semi = EditorTheme.FontSemiBold ?? font;
        bool hasBaked = LightmapBakeService.HasBakedData(scene);

        using (paper.Column($"{id}_card").Height(UnitValue.Auto).Margin(m.PaddingLarge, m.PaddingLarge, 16, 0)
            .Padding(12, 12, 12, 12).Rounded(m.ContainerRounding).BackgroundColor(EditorTheme.Glass)
            .BorderColor(EditorTheme.BorderSoft).BorderWidth(1).Gap(10).Enter())
        {
            if (baking)
            {
                Origami.ProgressBar(paper, $"{id}_pb", _bake.Progress).Show();
                paper.Box($"{id}_status").Height(18)
                    .Text($"{_bake.Status}  ({_bake.Progress * 100f:F0}%)", font)
                    .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                EditorGUI.CtaButton(paper, $"{id}_cancel", $"{EditorIcons.Xmark}  {Loc.Get("common.cancel")}", EditorTheme.Red400, () => _bake.Cancel(), height: 34f);
            }
            else
            {
                using (paper.Row($"{id}_info").Height(UnitValue.Auto).MinHeight(18).Gap(8).Enter())
                {
                    paper.Box($"{id}_info_i").Width(14).Height(18).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).IsNotInteractable()
                        .Text(hasBaked ? EditorIcons.Check : EditorIcons.Sun, font)
                        .TextColor(hasBaked ? EditorTheme.Green400 : EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
                    paper.Box($"{id}_info_t").Height(18).IsNotInteractable()
                        .Text($"{s.AtlasSize}px atlas  ·  {s.Bounces} bounce{(s.Bounces == 1 ? "" : "s")}  ·  {s.TexelsPerUnit:F0} texels/unit", font)
                        .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                }

                using (paper.Row($"{id}_btns").Height(34).Gap(8).Enter())
                {
                    EditorGUI.CtaButton(paper, $"{id}_bake", $"{EditorIcons.Sun}  {Loc.Get("env.generate_lighting")}", EditorTheme.Accent,
                        () =>
                        {
                            if (_bake.IsBaking)
                            {
                                Toasts.Warning(Loc.Get("env.toast_bake_busy"), Loc.Get("env.toast_bake_busy_msg"));
                                return;
                            }
                            _bake.Start(scene, scene.LightmapBake);
                        }, grow: true, height: 34f);
                    if (hasBaked)
                        ChipButton(paper, $"{id}_clear", Loc.Get("console.clear"), () => _bake.Clear(scene));
                }
            }
        }
    }

    private static void ChipButton(Paper paper, string id, string label, Action onClick)
        => Origami.Button(paper, id, label, onClick).Height(34).Show();
}
