// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Docking;
using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using VColor = Prowl.Vector.Color;

namespace Prowl.Editor.Panels;

[EditorWindow("General/Environment")]
public class EnvironmentPanel : DockPanel
{
    public override string Title => "Environment";
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

        using (paper.Column("env_root").Width(width).Height(height).ChildLeft(8).ChildRight(8).ChildTop(8).ColBetween(4).Clip().Enter())
        {
            DrawSkyboxSection(paper, "env_sky", font, scene);
            paper.Box("env_sp1").Height(8);
            DrawFogSection(paper, "env_fog", font, scene);
            paper.Box("env_sp2").Height(8);
            DrawAmbientSection(paper, "env_amb", font, scene);
        }
    }

    private void DrawSkyboxSection(Paper paper, string id, Prowl.Scribe.FontFile font, Scene scene)
    {
        EditorGUI.Foldout(paper, $"{id}_fold", $"{EditorIcons.Sun}  Skybox", () =>
        {
            var sky = scene.Skybox;

            EditorGUI.EnumDropdown(paper, $"{id}_mode", "Mode", sky.Mode)
                .OnValueChanged(v => { sky.Mode = v; scene.Skybox = sky; EditorSceneManager.IsDirty = true; });

            switch (sky.Mode)
            {
                case Scene.SkyboxMode.SolidColor:
                    PropertyGrid.DrawField(paper, $"{id}_solid", "Color", typeof(VColor), sky.SolidColor,
                        v => { sky.SolidColor = (VColor)v!; scene.Skybox = sky; EditorSceneManager.IsDirty = true; }, 0);
                    break;

                case Scene.SkyboxMode.Gradient:
                    PropertyGrid.DrawField(paper, $"{id}_top", "Top Color", typeof(VColor), sky.GradientTop,
                        v => { sky.GradientTop = (VColor)v!; scene.Skybox = sky; EditorSceneManager.IsDirty = true; }, 0);
                    PropertyGrid.DrawField(paper, $"{id}_bot", "Bottom Color", typeof(VColor), sky.GradientBottom,
                        v => { sky.GradientBottom = (VColor)v!; scene.Skybox = sky; EditorSceneManager.IsDirty = true; }, 0);
                    EditorGUI.Slider(paper, $"{id}_exp", "Exponent", sky.GradientExponent, 0.1f, 5f)
                        .OnValueChanged(v => { sky.GradientExponent = v; scene.Skybox = sky; EditorSceneManager.IsDirty = true; });
                    break;

                case Scene.SkyboxMode.Material:
                    PropertyGrid.DrawField(paper, $"{id}_mat", "Material", typeof(AssetRef<Material>), sky.CustomMaterial,
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

    private void DrawFogSection(Paper paper, string id, Prowl.Scribe.FontFile font, Scene scene)
    {
        EditorGUI.Foldout(paper, $"{id}_fold", $"{EditorIcons.Cloud}  Fog", () =>
        {
            var fog = scene.Fog;

            EditorGUI.EnumDropdown(paper, $"{id}_mode", "Mode", fog.Mode)
                .OnValueChanged(v => { fog.Mode = v; scene.Fog = fog; EditorSceneManager.IsDirty = true; });

            if (fog.Mode != Scene.FogParams.FogMode.Off)
            {
                PropertyGrid.DrawField(paper, $"{id}_color", "Color", typeof(VColor), fog.Color,
                    v => { fog.Color = (VColor)v!; scene.Fog = fog; EditorSceneManager.IsDirty = true; }, 0);

                if (fog.Mode == Scene.FogParams.FogMode.Linear)
                {
                    EditorGUI.FloatField(paper, $"{id}_start", fog.Start, "Start Distance")
                        .OnValueChanged(v => { fog.Start = v; scene.Fog = fog; EditorSceneManager.IsDirty = true; });
                    EditorGUI.FloatField(paper, $"{id}_end", fog.End, "End Distance")
                        .OnValueChanged(v => { fog.End = v; scene.Fog = fog; EditorSceneManager.IsDirty = true; });
                }
                else
                {
                    EditorGUI.Slider(paper, $"{id}_density", "Density", fog.Density, 0f, 0.1f)
                        .OnValueChanged(v => { fog.Density = v; scene.Fog = fog; EditorSceneManager.IsDirty = true; });
                }
            }
        });
    }

    private void DrawAmbientSection(Paper paper, string id, Prowl.Scribe.FontFile font, Scene scene)
    {
        EditorGUI.Foldout(paper, $"{id}_fold", $"{EditorIcons.Lightbulb}  Ambient Lighting", () =>
        {
            var ambient = scene.Ambient;

            EditorGUI.EnumDropdown(paper, $"{id}_mode", "Mode", ambient.Mode)
                .OnValueChanged(v => { ambient.Mode = v; scene.Ambient = ambient; EditorSceneManager.IsDirty = true; });

            EditorGUI.Slider(paper, $"{id}_str", "Strength", ambient.Strength, 0f, 5f)
                .OnValueChanged(v => { ambient.Strength = v; scene.Ambient = ambient; EditorSceneManager.IsDirty = true; });

            if (ambient.Mode == Scene.AmbientLightParams.AmbientMode.Uniform)
            {
                PropertyGrid.DrawField(paper, $"{id}_color", "Color", typeof(Float4), ambient.Color,
                    v => { ambient.Color = (Float4)v!; scene.Ambient = ambient; EditorSceneManager.IsDirty = true; }, 0);
            }
            else
            {
                PropertyGrid.DrawField(paper, $"{id}_sky", "Sky Color", typeof(Float4), ambient.SkyColor,
                    v => { ambient.SkyColor = (Float4)v!; scene.Ambient = ambient; EditorSceneManager.IsDirty = true; }, 0);
                PropertyGrid.DrawField(paper, $"{id}_gnd", "Ground Color", typeof(Float4), ambient.GroundColor,
                    v => { ambient.GroundColor = (Float4)v!; scene.Ambient = ambient; EditorSceneManager.IsDirty = true; }, 0);
            }
        });
    }
}
