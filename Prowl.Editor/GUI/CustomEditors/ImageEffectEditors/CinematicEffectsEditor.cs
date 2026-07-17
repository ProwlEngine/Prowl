using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.PropertyEditors;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;

using PropertyGridUtils = Prowl.Editor.GUI.PropertyGridUtils;
namespace Prowl.Editor.Inspector;

[CustomEditor(typeof(CinematicEffects))]
public class CinematicEffectsEditor : CustomEditor
{
    public override void OnGUI(Paper paper, string id, object target)
    {
        var fx = (CinematicEffects)target;

        // Pre-snapshot: captures entire component state before any widget mutates it
        Undo.Snapshot(fx);

        // -- Vignette --------------------------------------
        EditorGUI.ModuleSection(paper, $"{id}_vignette", EditorIcons.Eye, "Vignette",
            fx.EnableVignette, v => fx.EnableVignette = v, () =>
        {
            EditorGUI.SliderRow(paper, $"{id}_vig_int", "Intensity", fx.VignetteIntensity, 0, 1, v => fx.VignetteIntensity = v);
            EditorGUI.SliderRow(paper, $"{id}_vig_sm", "Smoothness", fx.VignetteSmoothness, 0.01f, 1, v => fx.VignetteSmoothness = v);
            EditorGUI.SliderRow(paper, $"{id}_vig_rn", "Roundness", fx.VignetteRoundness, 0, 1, v => fx.VignetteRoundness = v);
        });

        // -- Chromatic Aberration --------------------------
        EditorGUI.ModuleSection(paper, $"{id}_chroma", EditorIcons.Droplet, "Chromatic Aberration",
            fx.EnableChromaticAberration, v => fx.EnableChromaticAberration = v, () =>
        {
            EditorGUI.SliderRow(paper, $"{id}_chr_int", "Intensity", fx.ChromaticIntensity, 0, 20, v => fx.ChromaticIntensity = v);
            EditorGUI.SliderRow(paper, $"{id}_chr_dist", "Distortion", fx.ChromaticDistortion, 0, 2, v => fx.ChromaticDistortion = v);
        });

        // -- Film Grain ------------------------------------
        EditorGUI.ModuleSection(paper, $"{id}_grain", EditorIcons.Film, "Film Grain",
            fx.EnableFilmGrain, v => fx.EnableFilmGrain = v, () =>
        {
            EditorGUI.SliderRow(paper, $"{id}_grn_int", "Intensity", fx.GrainIntensity, 0, 1, v => fx.GrainIntensity = v);
            EditorGUI.SliderRow(paper, $"{id}_grn_rsp", "Response", fx.GrainResponse, 0, 1, v => fx.GrainResponse = v);
        });

        // -- Color Grading ---------------------------------
        EditorGUI.ModuleSection(paper, $"{id}_cgrade", EditorIcons.Palette, "Color Grading",
            fx.EnableColorGrading, v => fx.EnableColorGrading = v, () =>
        {
            EditorGUI.SliderRow(paper, $"{id}_cg_exp", "Post Exposure (EV)", fx.PostExposure, -5, 5, v => fx.PostExposure = v, bipolar: true);
            EditorGUI.SliderRow(paper, $"{id}_cg_con", "Contrast", fx.Contrast, -1, 1, v => fx.Contrast = v, bipolar: true);
            EditorGUI.SliderRow(paper, $"{id}_cg_sat", "Saturation", fx.Saturation, -1, 1, v => fx.Saturation = v, bipolar: true);
            EditorGUI.SliderRow(paper, $"{id}_cg_tmp", "Temperature", fx.Temperature, -1, 1, v => fx.Temperature = v, bipolar: true);

            Origami.Separator(paper, $"{id}_cg_sep_lgg").Show();
            Origami.Label(paper, $"{id}_cg_lgg_lbl", "Lift / Gamma / Gain").Show();

            EditorGUI.Row(paper, $"{id}_cg_lift", "Lift (Shadows)", () =>
                Origami.ColorField(paper, $"{id}_cg_lift_cf", fx.Lift, v => fx.Lift = v).Show());
            EditorGUI.Row(paper, $"{id}_cg_gamma", "Gamma (Midtones)", () =>
                Origami.ColorField(paper, $"{id}_cg_gamma_cf", fx.Gamma, v => fx.Gamma = v).Show());
            EditorGUI.Row(paper, $"{id}_cg_gain", "Gain (Highlights)", () =>
                Origami.ColorField(paper, $"{id}_cg_gain_cf", fx.Gain, v => fx.Gain = v).Show());
        });

        // -- LUT -------------------------------------------
        EditorGUI.ModuleSection(paper, $"{id}_lut", EditorIcons.TableCells, "LUT Color Grading",
            fx.EnableLUT, v => fx.EnableLUT = v, () =>
        {
            PropertyGridUtils.DrawField(paper, $"{id}_lut_tex", "LUT Texture", typeof(AssetRef<Texture2D>), fx.LUTTexture,
                newVal =>
                {
                    fx.LUTTexture = (AssetRef<Texture2D>)newVal!;
                }, 0);
            EditorGUI.SliderRow(paper, $"{id}_lut_cont", "Contribution", fx.LUTContribution, 0, 1, v => fx.LUTContribution = v);
        });

        // -- Sharpen (CAS) ---------------------------------
        EditorGUI.ModuleSection(paper, $"{id}_sharp", EditorIcons.Diamond, "Sharpen (CAS)",
            fx.EnableSharpen, v => fx.EnableSharpen = v, () =>
        {
            EditorGUI.SliderRow(paper, $"{id}_shp_amt", "Amount", fx.SharpenAmount, 0, 1, v => fx.SharpenAmount = v);
            EditorGUI.SliderRow(paper, $"{id}_shp_rad", "Radius", fx.SharpenRadius, 1, 4, v => fx.SharpenRadius = v);
        });

        // -- Edge Detection --------------------------------
        EditorGUI.ModuleSection(paper, $"{id}_edge", EditorIcons.BorderAll, "Edge Detection",
            fx.EnableEdgeDetection, v => fx.EnableEdgeDetection = v, () =>
        {
            EditorGUI.SliderRow(paper, $"{id}_edg_int", "Intensity", fx.EdgeIntensity, 0, 5, v => fx.EdgeIntensity = v);
            EditorGUI.Row(paper, $"{id}_edg_col", "Edge Color", () =>
                Origami.ColorField(paper, $"{id}_edg_col_cf", fx.EdgeColor, v => fx.EdgeColor = v).Show());
            EditorGUI.SliderRow(paper, $"{id}_edg_bg", "Background Fade", fx.EdgeBackgroundFade, 0, 1, v => fx.EdgeBackgroundFade = v);
        });

        // -- Pixelation ------------------------------------
        EditorGUI.ModuleSection(paper, $"{id}_pixel", EditorIcons.TableCellsLarge, "Pixelation",
            fx.EnablePixelation, v => fx.EnablePixelation = v, () =>
        {
            EditorGUI.SliderRow(paper, $"{id}_pxl_sz", "Pixel Size", fx.PixelSize, 1, 32, v => fx.PixelSize = v);
        });

        // -- God Rays --------------------------------------
        EditorGUI.ModuleSection(paper, $"{id}_godrays", EditorIcons.Sun, "God Rays",
            fx.EnableGodRays, v => fx.EnableGodRays = v, () =>
        {
            EditorGUI.SliderRow(paper, $"{id}_gr_int", "Intensity", fx.GodRayIntensity, 0, 2, v => fx.GodRayIntensity = v);
            EditorGUI.SliderRow(paper, $"{id}_gr_dec", "Decay", fx.GodRayDecay, 0.9f, 1.0f, v => fx.GodRayDecay = v);
            EditorGUI.SliderRow(paper, $"{id}_gr_dns", "Density", fx.GodRayDensity, 0.1f, 2.0f, v => fx.GodRayDensity = v);
            EditorGUI.SliderRow(paper, $"{id}_gr_wgt", "Weight", fx.GodRayWeight, 0, 1, v => fx.GodRayWeight = v);
            EditorGUI.SliderRow(paper, $"{id}_gr_thr", "Threshold", fx.GodRayThreshold, 0, 1, v => fx.GodRayThreshold = v);
            EditorGUI.Row(paper, $"{id}_gr_smp", "Samples", () =>
                Origami.IntSlider(paper, $"{id}_gr_smp_v", fx.GodRaySamples,
                    v => fx.GodRaySamples = System.Math.Clamp(v, 8, 128), 8, 128).Show());
        });
    }

}
