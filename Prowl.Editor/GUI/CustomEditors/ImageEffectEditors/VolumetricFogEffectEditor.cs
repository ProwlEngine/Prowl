using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime.Rendering;

namespace Prowl.Editor.Inspector;

[CustomEditor(typeof(VolumetricFogEffect))]
public class VolumetricFogEffectEditor : CustomEditor
{
    public override void OnGUI(Paper paper, string id, object target)
    {
        var fx = (VolumetricFogEffect)target;

        // Pre-snapshot: captures entire component state before any widget mutates it
        Undo.Snapshot(fx);

        Origami.Header(paper, $"{id}_h_main", $"{EditorIcons.Cloud}  Volumetric Fog").Underline().Show();

        // -- Global --
        Origami.Header(paper, $"{id}_h_global", "Global").Show();
        EditorGUI.SliderRow(paper, $"{id}_dens", "Global Density", fx.GlobalDensity, 0, 0.2f, v => fx.GlobalDensity = v, "F3");
        EditorGUI.Row(paper, $"{id}_tint", "Color Tint", () =>
                Origami.ColorField(paper, $"{id}_tint_cf", fx.GlobalColorTint, v => fx.GlobalColorTint = v).Show());
        EditorGUI.SliderRow(paper, $"{id}_scat", "Scattering (Anisotropy)", fx.Scattering, -0.99f, 0.99f, v => fx.Scattering = v, "F3", bipolar: true);
        EditorGUI.SliderRow(paper, $"{id}_ext", "Extinction", fx.Extinction, 0, 5, v => fx.Extinction = v, "F3");
        EditorGUI.SliderRow(paper, $"{id}_dith", "Dithering", fx.Dithering, 0, 0.2f, v => fx.Dithering = v, "F3");

        // -- Ambient --
        Origami.Header(paper, $"{id}_h_amb", "Ambient").Underline().Show();
        EditorGUI.Row(paper, $"{id}_amb_col", "Ambient Color", () =>
                Origami.ColorField(paper, $"{id}_amb_col_cf", fx.AmbientColor, v => fx.AmbientColor = v).Show());
        EditorGUI.SliderRow(paper, $"{id}_amb_int", "Ambient Intensity", fx.AmbientIntensity, 0, 5, v => fx.AmbientIntensity = v, "F3");

        // -- Lights --
        Origami.Header(paper, $"{id}_h_lights", "Light Types").Underline().Show();

        Origami.Checkbox(paper, $"{id}_dir", fx.EnableDirectional, v => fx.EnableDirectional = v)
            .LabelRight("Directional Light").Show();
        Origami.Checkbox(paper, $"{id}_dir_sh", fx.EnableDirectionalShadows, v => fx.EnableDirectionalShadows = v)
            .LabelRight("  + Directional Shadows").Show();

        Origami.Checkbox(paper, $"{id}_pt", fx.EnablePointLights, v => fx.EnablePointLights = v)
            .LabelRight("Point Lights").Show();
        Origami.Checkbox(paper, $"{id}_pt_sh", fx.EnablePointLightShadows, v => fx.EnablePointLightShadows = v)
            .LabelRight("  + Point Shadows").Show();

        Origami.Checkbox(paper, $"{id}_sp", fx.EnableSpotLights, v => fx.EnableSpotLights = v)
            .LabelRight("Spot Lights").Show();
        Origami.Checkbox(paper, $"{id}_sp_sh", fx.EnableSpotLightShadows, v => fx.EnableSpotLightShadows = v)
            .LabelRight("  + Spot Shadows").Show();

        // -- Performance --
        Origami.Header(paper, $"{id}_h_perf", "Performance").Underline().Show();

        EditorGUI.SliderRow(paper, $"{id}_maxd", "Max Distance", fx.MaxDistance, 1, 500, v => fx.MaxDistance = v, "F3");
        EditorGUI.IntSliderRow(paper, $"{id}_steps", "Steps", fx.Steps, 8, 128, v => fx.Steps = v);
        EditorGUI.IntSliderRow(paper, $"{id}_down", "Downsample (1=full,2=half,4=quarter)", fx.DownsampleScale, 1, 4, v => fx.DownsampleScale = v);
        EditorGUI.SliderRow(paper, $"{id}_upthr", "Upsample Depth Threshold", fx.UpsampleDepthThreshold, 0.001f, 1f, v => fx.UpsampleDepthThreshold = v, "F3");
    }
}
