// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Core;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Inspector;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using VColor = Prowl.Vector.Color;
namespace Prowl.Editor.GUI.Panels;

[EditorWindow("General/Environment")]
public class EnvironmentPanel : DockPanel
{
    public override string Title => Loc.Get("panel.environment");
    public override string Icon => EditorIcons.Sun;

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        var scene = Scene.Current;
        if (scene == null)
        {
            paper.Box("env_noscene").Height(30)
                .Text("No scene loaded.", font).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter);
            return;
        }

        using (paper.Column("env_root").Width(width).Height(height).Padding(8, 8, 8, 0).ColBetween(4).Clip().Enter())
        {
            DrawSkyboxSection(paper, "env_sky", font, scene);
            paper.Box("env_sp1").Height(8);
            DrawFogSection(paper, "env_fog", font, scene);
            paper.Box("env_sp2").Height(8);
            DrawAmbientSection(paper, "env_amb", font, scene);
        }
    }

    private void DrawSkyboxSection(Paper paper, string id, Scribe.FontFile font, Scene scene)
    {
        Origami.Foldout(paper, $"{id}_fold", $"{EditorIcons.Sun}  Skybox").Body(() =>
        {
            var sky = scene.Skybox;

            InspectorRow.Draw(paper, $"{id}_mode", "Mode", () =>
                Origami.EnumDropdown(paper, $"{id}_mode_v", sky.Mode,
                    v => { sky.Mode = v; scene.Skybox = sky; EditorSceneManager.IsDirty = true; }).Show());

            switch (sky.Mode)
            {
                case Scene.SkyboxMode.SolidColor:
                    PropertyGridUtils.DrawField(paper, $"{id}_solid", "Color", typeof(VColor), sky.SolidColor,
                        v => { sky.SolidColor = (VColor)v!; scene.Skybox = sky; EditorSceneManager.IsDirty = true; }, 0);
                    break;

                case Scene.SkyboxMode.Gradient:
                    PropertyGridUtils.DrawField(paper, $"{id}_top", "Top Color", typeof(VColor), sky.GradientTop,
                        v => { sky.GradientTop = (VColor)v!; scene.Skybox = sky; EditorSceneManager.IsDirty = true; }, 0);
                    PropertyGridUtils.DrawField(paper, $"{id}_bot", "Bottom Color", typeof(VColor), sky.GradientBottom,
                        v => { sky.GradientBottom = (VColor)v!; scene.Skybox = sky; EditorSceneManager.IsDirty = true; }, 0);
                    InspectorRow.Draw(paper, $"{id}_exp", "Exponent", () =>
                        Origami.Slider(paper, $"{id}_exp_v", sky.GradientExponent,
                            v => { sky.GradientExponent = v; scene.Skybox = sky; EditorSceneManager.IsDirty = true; },
                            0.1f, 5f).Format("F2").Show());
                    break;

                case Scene.SkyboxMode.Material:
                    PropertyGridUtils.DrawField(paper, $"{id}_mat", "Material", typeof(AssetRef<Material>), sky.CustomMaterial,
                        v => { sky.CustomMaterial = (AssetRef<Material>)v!; scene.Skybox = sky; EditorSceneManager.IsDirty = true; }, 0);
                    break;

                case Scene.SkyboxMode.Procedural:
                    paper.Box($"{id}_hint").Height(20)
                        .Text("Sun direction set automatically from Directional Light.", font)
                        .TextColor(EditorTheme.Ink300).FontSize(9f).Alignment(TextAlignment.MiddleLeft);
                    break;
            }
        });
    }

    private void DrawFogSection(Paper paper, string id, Scribe.FontFile font, Scene scene)
    {
        Origami.Foldout(paper, $"{id}_fold", $"{EditorIcons.Cloud}  Fog").Body(() =>
        {
            var fog = scene.Fog;

            InspectorRow.Draw(paper, $"{id}_mode", "Mode", () =>
                Origami.EnumDropdown(paper, $"{id}_mode_v", fog.Mode,
                    v => { fog.Mode = v; scene.Fog = fog; EditorSceneManager.IsDirty = true; }).Show());

            if (fog.Mode != Scene.FogParams.FogMode.Off)
            {
                PropertyGridUtils.DrawField(paper, $"{id}_color", "Color", typeof(VColor), fog.Color,
                    v => { fog.Color = (VColor)v!; scene.Fog = fog; EditorSceneManager.IsDirty = true; }, 0);

                if (fog.Mode == Scene.FogParams.FogMode.Linear)
                {
                    InspectorRow.Draw(paper, $"{id}_start", "Start Distance", () =>
                        Origami.NumericField<float>(paper, $"{id}_start_v", fog.Start,
                            v => { fog.Start = v; scene.Fog = fog; EditorSceneManager.IsDirty = true; }).Show());
                    InspectorRow.Draw(paper, $"{id}_end", "End Distance", () =>
                        Origami.NumericField<float>(paper, $"{id}_end_v", fog.End,
                            v => { fog.End = v; scene.Fog = fog; EditorSceneManager.IsDirty = true; }).Show());
                }
                else
                {
                    InspectorRow.Draw(paper, $"{id}_density", "Density", () =>
                        Origami.Slider(paper, $"{id}_density_v", fog.Density,
                            v => { fog.Density = v; scene.Fog = fog; EditorSceneManager.IsDirty = true; },
                            0f, 0.1f).Format("F4").Show());
                }
            }
        });
    }

    private void DrawAmbientSection(Paper paper, string id, Scribe.FontFile font, Scene scene)
    {
        Origami.Foldout(paper, $"{id}_fold", $"{EditorIcons.Lightbulb}  Ambient Lighting").Body(() =>
        {
            var ambient = scene.Ambient;

            InspectorRow.Draw(paper, $"{id}_mode", "Mode", () =>
                Origami.EnumDropdown(paper, $"{id}_mode_v", ambient.Mode,
                    v => { ambient.Mode = v; scene.Ambient = ambient; EditorSceneManager.IsDirty = true; }).Show());

            InspectorRow.Draw(paper, $"{id}_str", "Strength", () =>
                Origami.Slider(paper, $"{id}_str_v", ambient.Strength,
                    v => { ambient.Strength = v; scene.Ambient = ambient; EditorSceneManager.IsDirty = true; },
                    0f, 5f).Format("F2").Show());

            if (ambient.Mode == Scene.AmbientLightParams.AmbientMode.Uniform)
            {
                PropertyGridUtils.DrawField(paper, $"{id}_color", "Color", typeof(Float4), ambient.Color,
                    v => { ambient.Color = (Float4)v!; scene.Ambient = ambient; EditorSceneManager.IsDirty = true; }, 0);
            }
            else
            {
                PropertyGridUtils.DrawField(paper, $"{id}_sky", "Sky Color", typeof(Float4), ambient.SkyColor,
                    v => { ambient.SkyColor = (Float4)v!; scene.Ambient = ambient; EditorSceneManager.IsDirty = true; }, 0);
                PropertyGridUtils.DrawField(paper, $"{id}_gnd", "Ground Color", typeof(Float4), ambient.GroundColor,
                    v => { ambient.GroundColor = (Float4)v!; scene.Ambient = ambient; EditorSceneManager.IsDirty = true; }, 0);
            }
        });
    }
}
