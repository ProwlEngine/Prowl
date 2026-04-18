using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;

namespace Prowl.Editor.Inspector;

[CustomEditor(typeof(VolumetricFogEffect))]
public class VolumetricFogEffectEditor : CustomEditor
{
    public override void OnGUI(Paper paper, string id, object target)
    {
        var fx = (VolumetricFogEffect)target;

        EditorGUI.Header(paper, $"{id}_h_main", $"{EditorIcons.Cloud}  Volumetric Fog");
        EditorGUI.Separator(paper, $"{id}_sep_main");

        // ── Global ──
        EditorGUI.Header(paper, $"{id}_h_global", "Global");
        EditorGUI.Slider(paper, $"{id}_dens", "Global Density", fx.GlobalDensity, 0, 0.2f)
            .OnValueChanged(v => fx.GlobalDensity = v);
        EditorGUI.ColorField(paper, $"{id}_tint", "Color Tint", fx.GlobalColorTint)
            .OnValueChanged(v => fx.GlobalColorTint = v);
        EditorGUI.Slider(paper, $"{id}_scat", "Scattering (Anisotropy)", fx.Scattering, -0.99f, 0.99f)
            .OnValueChanged(v => fx.Scattering = v);
        EditorGUI.Slider(paper, $"{id}_ext", "Extinction", fx.Extinction, 0, 5)
            .OnValueChanged(v => fx.Extinction = v);
        EditorGUI.Slider(paper, $"{id}_dith", "Dithering", fx.Dithering, 0, 0.2f)
            .OnValueChanged(v => fx.Dithering = v);

        // ── Ambient ──
        EditorGUI.Header(paper, $"{id}_h_amb", "Ambient");
        EditorGUI.Separator(paper, $"{id}_sep_amb");
        EditorGUI.ColorField(paper, $"{id}_amb_col", "Ambient Color", fx.AmbientColor)
            .OnValueChanged(v => fx.AmbientColor = v);
        EditorGUI.Slider(paper, $"{id}_amb_int", "Ambient Intensity", fx.AmbientIntensity, 0, 5)
            .OnValueChanged(v => fx.AmbientIntensity = v);

        // ── Lights ──
        EditorGUI.Header(paper, $"{id}_h_lights", "Light Types");
        EditorGUI.Separator(paper, $"{id}_sep_lights");

        EditorGUI.Toggle(paper, $"{id}_dir", "Directional Light", fx.EnableDirectional)
            .OnValueChanged(v => fx.EnableDirectional = v);
        EditorGUI.Toggle(paper, $"{id}_dir_sh", "  └ Directional Shadows", fx.EnableDirectionalShadows)
            .OnValueChanged(v => fx.EnableDirectionalShadows = v);

        EditorGUI.Toggle(paper, $"{id}_pt", "Point Lights", fx.EnablePointLights)
            .OnValueChanged(v => fx.EnablePointLights = v);
        EditorGUI.Toggle(paper, $"{id}_pt_sh", "  └ Point Shadows", fx.EnablePointLightShadows)
            .OnValueChanged(v => fx.EnablePointLightShadows = v);

        EditorGUI.Toggle(paper, $"{id}_sp", "Spot Lights", fx.EnableSpotLights)
            .OnValueChanged(v => fx.EnableSpotLights = v);
        EditorGUI.Toggle(paper, $"{id}_sp_sh", "  └ Spot Shadows", fx.EnableSpotLightShadows)
            .OnValueChanged(v => fx.EnableSpotLightShadows = v);

        // ── Performance ──
        EditorGUI.Header(paper, $"{id}_h_perf", "Performance");
        EditorGUI.Separator(paper, $"{id}_sep_perf");

        EditorGUI.Slider(paper, $"{id}_maxd", "Max Distance", fx.MaxDistance, 1, 500)
            .OnValueChanged(v => fx.MaxDistance = v);
        EditorGUI.IntSlider(paper, $"{id}_steps", "Steps", fx.Steps, 8, 128)
            .OnValueChanged(v => fx.Steps = v);
        EditorGUI.IntSlider(paper, $"{id}_down", "Downsample (1=full,2=half,4=quarter)", fx.DownsampleScale, 1, 4)
            .OnValueChanged(v => fx.DownsampleScale = v);
        EditorGUI.Slider(paper, $"{id}_upthr", "Upsample Depth Threshold", fx.UpsampleDepthThreshold, 0.001f, 1f)
            .OnValueChanged(v => fx.UpsampleDepthThreshold = v);
    }
}
