// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using static Prowl.Editor.GUI.EditorGUI;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Inspector;
using Prowl.Editor.Lightmapping;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Color = System.Drawing.Color;
using VColor = Prowl.Vector.Color;
namespace Prowl.Editor.GUI.Panels;

public class EnvironmentPanel : DockPanel
{
    [MenuItem("Window/General/Environment", priority: 6)]
    static void Open() => EditorApplication.Instance?.OpenPanel(typeof(EnvironmentPanel));

    public override string Title => Loc.Get("panel.environment");
    public override string Icon => EditorIcons.Sun;

    private readonly LightmapBakeService _bake = new();
    private string _cat = "sky";

    // label holds a localization key, resolved via Loc.Get at render (see the Sidebar call).
    private static readonly (string id, string label, string icon)[] Cats =
    {
        ("sky",      "env.tab_sky",      EditorIcons.Sun),
        ("fog",      "env.tab_fog",      EditorIcons.Cloud),
        ("ambient",  "env.tab_ambient",  EditorIcons.Lightbulb),
        ("lightmap", "env.tab_lightmap", EditorIcons.TableCellsLarge),
    };

    private static VColor ToColor(Float4 f) => new((float)f.X, (float)f.Y, (float)f.Z, (float)f.W);
    private static Float4 ToF4(VColor c) => new(c.R, c.G, c.B, c.A);

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        _bake.Poll();

        var scene = Scene.Current;
        if (scene == null)
        {
            paper.Box("env_noscene").Height(30)
                .Text(Loc.Get("selector.no_scene"), font).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter);
            return;
        }

        using (paper.Row("env_root").Width(width).Height(height).Clip().Enter())
        {
            var cats = Cats.Select(c => (c.id, Loc.Get(c.label), c.icon)).ToArray();
            float side = EditorGUI.Sidebar(paper, "env_side", cats, _cat, c => _cat = c);
            paper.Box("env_vdiv").Width(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

            Origami.ScrollView(paper, "env_scroll", width - side - 1, height).Body(() =>
            {
                using (paper.Column("env_content").Height(UnitValue.Auto).Padding(0, 0, 8, 12).Enter())
                {
                    switch (_cat)
                    {
                        case "fog":      DrawFogSection(paper, "env_fog", font, scene); break;
                        case "ambient":  DrawAmbientSection(paper, "env_amb", font, scene); break;
                        case "lightmap": DrawLightmappingSection(paper, "env_lm", font, scene); break;
                        default:         DrawSkyboxSection(paper, "env_sky", font, scene); break;
                    }
                }
            });
        }
    }

    private void DrawSkyboxSection(Paper paper, string id, Scribe.FontFile font, Scene scene)
    {
        EditorGUI.SectionHeader(paper, $"{id}_h", Loc.Get("env.skybox"), first: true);

        var sky = scene.Skybox;
        void Dirty() { scene.Skybox = sky; EditorSceneManager.IsDirty = true; }

        EditorGUI.SettingsRow(paper, $"{id}_mode", Loc.Get("env.mode"), () =>
            Origami.EnumDropdown(paper, $"{id}_mode_v", sky.Mode, v => { sky.Mode = v; Dirty(); }).Show());

        switch (sky.Mode)
        {
            case Scene.SkyboxMode.SolidColor:
                EditorGUI.SettingsRow(paper, $"{id}_solid", Loc.Get("env.color"), () =>
                    Origami.ColorField(paper, $"{id}_solid_v", sky.SolidColor, v => { sky.SolidColor = v; Dirty(); }).Show());
                break;

            case Scene.SkyboxMode.Gradient:
                EditorGUI.SettingsRow(paper, $"{id}_top", Loc.Get("env.top_color"), () =>
                    Origami.ColorField(paper, $"{id}_top_v", sky.GradientTop, v => { sky.GradientTop = v; Dirty(); }).Show());
                EditorGUI.SettingsRow(paper, $"{id}_bot", Loc.Get("env.bottom_color"), () =>
                    Origami.ColorField(paper, $"{id}_bot_v", sky.GradientBottom, v => { sky.GradientBottom = v; Dirty(); }).Show());
                EditorGUI.SettingsRow(paper, $"{id}_exp", Loc.Get("env.exponent"), () =>
                    Origami.Slider(paper, $"{id}_exp_v", sky.GradientExponent, v => { sky.GradientExponent = v; Dirty(); }, 0.1f, 5f).Show());
                break;

            case Scene.SkyboxMode.Material:
                EditorGUI.SettingsRow(paper, $"{id}_mat", Loc.Get("env.material"), () =>
                    PropertyGridUtils.DrawField(paper, $"{id}_mat_v", "", typeof(AssetRef<Material>), sky.CustomMaterial,
                        v => { sky.CustomMaterial = (AssetRef<Material>)v!; Dirty(); }, 0));
                break;

            case Scene.SkyboxMode.Procedural:
                paper.Box($"{id}_hint").Height(20).Margin(Origami.Current.Metrics.PaddingLarge, 0, 0, 0)
                    .Text(Loc.Get("env.sun_auto"), font)
                    .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                break;
        }
    }

    private void DrawFogSection(Paper paper, string id, Scribe.FontFile font, Scene scene)
    {
        EditorGUI.SectionHeader(paper, $"{id}_h", Loc.Get("env.fog"), first: true);

        var fog = scene.Fog;
        void Dirty() { scene.Fog = fog; EditorSceneManager.IsDirty = true; }

        EditorGUI.SettingsRow(paper, $"{id}_mode", Loc.Get("env.mode"), () =>
            Origami.EnumDropdown(paper, $"{id}_mode_v", fog.Mode, v => { fog.Mode = v; Dirty(); }).Show());

        if (fog.Mode != Scene.FogParams.FogMode.Off)
        {
            EditorGUI.SettingsRow(paper, $"{id}_color", Loc.Get("env.color"), () =>
                Origami.ColorField(paper, $"{id}_color_v", fog.Color, v => { fog.Color = v; Dirty(); }).Show());

            if (fog.Mode == Scene.FogParams.FogMode.Linear)
            {
                EditorGUI.SettingsRow(paper, $"{id}_start", Loc.Get("env.start_distance"), () =>
                    Origami.NumericField<float>(paper, $"{id}_start_v", fog.Start, v => { fog.Start = v; Dirty(); }).Show());
                EditorGUI.SettingsRow(paper, $"{id}_end", Loc.Get("env.end_distance"), () =>
                    Origami.NumericField<float>(paper, $"{id}_end_v", fog.End, v => { fog.End = v; Dirty(); }).Show());
            }
            else
            {
                EditorGUI.SettingsRow(paper, $"{id}_density", Loc.Get("env.density"), () =>
                    Origami.Slider(paper, $"{id}_density_v", fog.Density, v => { fog.Density = v; Dirty(); }, 0f, 0.1f).Format("F4").Show());
            }
        }
    }

    private void DrawAmbientSection(Paper paper, string id, Scribe.FontFile font, Scene scene)
    {
        EditorGUI.SectionHeader(paper, $"{id}_h", Loc.Get("env.ambient_lighting"), first: true);

        var ambient = scene.Ambient;
        void Dirty() { scene.Ambient = ambient; EditorSceneManager.IsDirty = true; }

        EditorGUI.SettingsRow(paper, $"{id}_mode", Loc.Get("env.mode"), () =>
            Origami.EnumDropdown(paper, $"{id}_mode_v", ambient.Mode, v => { ambient.Mode = v; Dirty(); }).Show());

        EditorGUI.SettingsRow(paper, $"{id}_str", Loc.Get("env.strength"), () =>
            Origami.Slider(paper, $"{id}_str_v", ambient.Strength, v => { ambient.Strength = v; Dirty(); }, 0f, 5f).Show());

        if (ambient.Mode == Scene.AmbientLightParams.AmbientMode.Uniform)
        {
            EditorGUI.SettingsRow(paper, $"{id}_color", Loc.Get("env.color"), () =>
                Origami.ColorField(paper, $"{id}_color_v", ToColor(ambient.Color), v => { ambient.Color = ToF4(v); Dirty(); }).Show());
        }
        else
        {
            EditorGUI.SettingsRow(paper, $"{id}_sky", Loc.Get("env.sky_color"), () =>
                Origami.ColorField(paper, $"{id}_sky_v", ToColor(ambient.SkyColor), v => { ambient.SkyColor = ToF4(v); Dirty(); }).Show());
            EditorGUI.SettingsRow(paper, $"{id}_gnd", Loc.Get("env.ground_color"), () =>
                Origami.ColorField(paper, $"{id}_gnd_v", ToColor(ambient.GroundColor), v => { ambient.GroundColor = ToF4(v); Dirty(); }).Show());
        }
    }

    private void DrawLightmappingSection(Paper paper, string id, Scribe.FontFile font, Scene scene)
    {
        // IsBaking alone doesn't say WHICH scene it's for - only one bake can run at a time, so
        // switching to a different scene while it's in flight must not show this scene as baking too.
        bool baking = _bake.IsBaking && _bake.TargetScene == scene;
        var s = scene.LightmapBake;
        void Touch() => EditorSceneManager.IsDirty = true;

        EditorGUI.SectionHeader(paper, $"{id}_h_res", Loc.Get("game.resolution"), first: true);

        EditorGUI.SettingsRow(paper, $"{id}_size", Loc.Get("env.atlas_size"), () =>
            Origami.IntSlider(paper, $"{id}_size_v", s.AtlasSize, v => { if (!baking) { s.AtlasSize = v; Touch(); } }, 256, 4096).Show());
        EditorGUI.SettingsRow(paper, $"{id}_tpu", Loc.Get("env.texels_unit"), () =>
            Origami.Slider(paper, $"{id}_tpu_v", s.TexelsPerUnit, v => { if (!baking) { s.TexelsPerUnit = v; Touch(); } }, 1f, 100f).Format("F0").Show());
        EditorGUI.SettingsRow(paper, $"{id}_dil", Loc.Get("env.padding_dilate"), () =>
            Origami.IntSlider(paper, $"{id}_dil_v", s.DilatePixels, v => { if (!baking) { s.DilatePixels = v; Touch(); } }, 0, 16).Show());

        EditorGUI.SectionHeader(paper, $"{id}_h_qual", Loc.Get("env.quality"));

        EditorGUI.SettingsRow(paper, $"{id}_bnc", Loc.Get("env.bounces"), () =>
            Origami.IntSlider(paper, $"{id}_bnc_v", s.Bounces, v => { if (!baking) { s.Bounces = v; Touch(); } }, 0, 8).Show());
        EditorGUI.SettingsRow(paper, $"{id}_smp", Loc.Get("env.indirect_samples"), () =>
            Origami.IntSlider(paper, $"{id}_smp_v", s.Samples, v => { if (!baking) { s.Samples = v; Touch(); } }, 1, 1024).Show());
        EditorGUI.SettingsRow(paper, $"{id}_psmp", Loc.Get("env.probe_samples"), () =>
            Origami.IntSlider(paper, $"{id}_psmp_v", s.ProbeSamples, v => { if (!baking) { s.ProbeSamples = v; Touch(); } }, 16, 2048).Show());
        EditorGUI.SettingsToggle(paper, $"{id}_cull", Loc.Get("env.backface_cull"), s.DoBackfaceCull, v => { if (!baking) { s.DoBackfaceCull = v; Touch(); } });
        EditorGUI.SettingsToggle(paper, $"{id}_dn", Loc.Get("env.denoise"), s.Denoise, v => { if (!baking) { s.Denoise = v; Touch(); } });
        if (s.Denoise)
            EditorGUI.SettingsRow(paper, $"{id}_dnr", Loc.Get("env.denoise_radius"), () =>
                Origami.IntSlider(paper, $"{id}_dnr_v", s.DenoiseRadius, v => { if (!baking) { s.DenoiseRadius = v; Touch(); } }, 1, 8).Show());

        EditorGUI.SectionHeader(paper, $"{id}_h_env", Loc.Get("panel.environment"));
        EditorGUI.SettingsToggle(paper, $"{id}_sky", Loc.Get("env.bake_sky_gi"), s.BakeSkyLighting, v => { if (!baking) { s.BakeSkyLighting = v; Touch(); } });

        EditorGUI.SectionHeader(paper, $"{id}_h_adv", Loc.Get("env.advanced"));
        EditorGUI.SettingsRow(paper, $"{id}_rr", Loc.Get("env.russian_roulette"), () =>
            Origami.Slider(paper, $"{id}_rr_v", s.RussianRoulette, v => { if (!baking) { s.RussianRoulette = v; Touch(); } }, 0f, 1f).Show());
        EditorGUI.SettingsToggle(paper, $"{id}_alb", Loc.Get("env.ignore_albedo"), s.IgnoreAlbedo, v => { if (!baking) { s.IgnoreAlbedo = v; Touch(); } });

        DrawBakeCard(paper, id, font, scene, s, baking);
    }

    private void DrawBakeCard(Paper paper, string id, Scribe.FontFile font, Scene scene,
        Scene.LightmapBakeSettings s, bool baking)
    {
        var m = Origami.Current.Metrics;
        var semi = EditorTheme.FontSemiBold ?? font;
        bool hasBaked = LightmapBakeService.HasBakedData(scene);

        using (paper.Column($"{id}_card").Height(UnitValue.Auto).Margin(m.PaddingLarge, m.PaddingLarge, 16, 0)
            .Padding(12, 12, 12, 12).Rounded(9).BackgroundColor(EditorTheme.Glass)
            .BorderColor(EditorTheme.BorderSoft).BorderWidth(1).ColBetween(10).Enter())
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
                using (paper.Row($"{id}_info").Height(UnitValue.Auto).MinHeight(18).RowBetween(8).Enter())
                {
                    paper.Box($"{id}_info_i").Width(14).Height(18).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).IsNotInteractable()
                        .Text(hasBaked ? EditorIcons.Check : EditorIcons.Sun, font)
                        .TextColor(hasBaked ? EditorTheme.Green400 : EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
                    paper.Box($"{id}_info_t").Height(18).IsNotInteractable()
                        .Text($"{s.AtlasSize}px atlas  ·  {s.Bounces} bounce{(s.Bounces == 1 ? "" : "s")}  ·  {s.TexelsPerUnit:F0} texels/unit", font)
                        .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                }

                using (paper.Row($"{id}_btns").Height(34).RowBetween(8).Enter())
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
    {
        var font = EditorTheme.DefaultFont;
        paper.Box(id).Width(UnitValue.Auto).Height(34).Rounded(9).Padding(14, 14, 0, 0)
            .BackgroundColor(EditorTheme.Glass).BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .Hovered.BorderColor(EditorTheme.BorderStrong).End()
            .Text(label, font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) => onClick());
    }
}
