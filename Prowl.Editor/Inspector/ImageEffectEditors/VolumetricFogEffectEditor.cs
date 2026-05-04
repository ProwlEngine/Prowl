using Prowl.Editor.Widgets;
using Prowl.OrigamiUI;
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
        SliderRow(paper, $"{id}_dens", "Global Density", fx.GlobalDensity, 0, 0.2f, v => fx.GlobalDensity = v);
        EditorGUI.ColorField(paper, $"{id}_tint", "Color Tint", fx.GlobalColorTint)
            .OnValueChanged(v => fx.GlobalColorTint = v);
        SliderRow(paper, $"{id}_scat", "Scattering (Anisotropy)", fx.Scattering, -0.99f, 0.99f, v => fx.Scattering = v, bipolar: true);
        SliderRow(paper, $"{id}_ext", "Extinction", fx.Extinction, 0, 5, v => fx.Extinction = v);
        SliderRow(paper, $"{id}_dith", "Dithering", fx.Dithering, 0, 0.2f, v => fx.Dithering = v);

        // ── Ambient ──
        EditorGUI.Header(paper, $"{id}_h_amb", "Ambient");
        EditorGUI.Separator(paper, $"{id}_sep_amb");
        EditorGUI.ColorField(paper, $"{id}_amb_col", "Ambient Color", fx.AmbientColor)
            .OnValueChanged(v => fx.AmbientColor = v);
        SliderRow(paper, $"{id}_amb_int", "Ambient Intensity", fx.AmbientIntensity, 0, 5, v => fx.AmbientIntensity = v);

        // ── Lights ──
        EditorGUI.Header(paper, $"{id}_h_lights", "Light Types");
        EditorGUI.Separator(paper, $"{id}_sep_lights");

        Origami.Checkbox(paper, $"{id}_dir", fx.EnableDirectional, v => fx.EnableDirectional = v)
            .LabelRight("Directional Light").Show();
        Origami.Checkbox(paper, $"{id}_dir_sh", fx.EnableDirectionalShadows, v => fx.EnableDirectionalShadows = v)
            .LabelRight("  └ Directional Shadows").Show();

        Origami.Checkbox(paper, $"{id}_pt", fx.EnablePointLights, v => fx.EnablePointLights = v)
            .LabelRight("Point Lights").Show();
        Origami.Checkbox(paper, $"{id}_pt_sh", fx.EnablePointLightShadows, v => fx.EnablePointLightShadows = v)
            .LabelRight("  └ Point Shadows").Show();

        Origami.Checkbox(paper, $"{id}_sp", fx.EnableSpotLights, v => fx.EnableSpotLights = v)
            .LabelRight("Spot Lights").Show();
        Origami.Checkbox(paper, $"{id}_sp_sh", fx.EnableSpotLightShadows, v => fx.EnableSpotLightShadows = v)
            .LabelRight("  └ Spot Shadows").Show();

        // ── Performance ──
        EditorGUI.Header(paper, $"{id}_h_perf", "Performance");
        EditorGUI.Separator(paper, $"{id}_sep_perf");

        SliderRow(paper, $"{id}_maxd", "Max Distance", fx.MaxDistance, 1, 500, v => fx.MaxDistance = v);
        IntSliderRow(paper, $"{id}_steps", "Steps", fx.Steps, 8, 128, v => fx.Steps = v);
        IntSliderRow(paper, $"{id}_down", "Downsample (1=full,2=half,4=quarter)", fx.DownsampleScale, 1, 4, v => fx.DownsampleScale = v);
        SliderRow(paper, $"{id}_upthr", "Upsample Depth Threshold", fx.UpsampleDepthThreshold, 0.001f, 1f, v => fx.UpsampleDepthThreshold = v);
    }

    private static void SliderRow(Paper paper, string id, string label, float value, float min, float max, System.Action<float> setter, bool bipolar = false)
        => InspectorRow.Draw(paper, id, label, () =>
        {
            var s = Origami.Slider(paper, $"{id}_v", value, setter, min, max).Format("F3");
            if (bipolar) s.Bipolar();
            s.Show();
        });

    private static void IntSliderRow(Paper paper, string id, string label, int value, int min, int max, System.Action<int> setter)
        => InspectorRow.Draw(paper, id, label, () =>
            Origami.IntSlider(paper, $"{id}_v", value, setter, min, max).Show());
}
