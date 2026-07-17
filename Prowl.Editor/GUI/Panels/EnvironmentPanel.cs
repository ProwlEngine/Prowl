// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using static Prowl.Editor.GUI.EditorGUI;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Inspector;
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

    private string _cat = "sky";

    // label holds a localization key, resolved via Loc.Get at render (see the Sidebar call).
    private static readonly (string id, string label, string icon)[] Cats =
    {
        ("sky",      "env.tab_sky",      EditorIcons.Sun),
        ("fog",      "env.tab_fog",      EditorIcons.Cloud),
        ("ambient",  "env.tab_ambient",  EditorIcons.Lightbulb),
    };

    private static VColor ToColor(Float4 f) => new((float)f.X, (float)f.Y, (float)f.Z, (float)f.W);
    private static Float4 ToF4(VColor c) => new(c.R, c.G, c.B, c.A);

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

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

}
