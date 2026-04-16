using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Editor.Inspector;

[CustomEditor(typeof(CinematicEffects))]
public class CinematicEffectsEditor : CustomEditor
{
    public override void OnGUI(Paper paper, string id, object target)
    {
        var fx = (CinematicEffects)target;

        // ── Vignette ──────────────────────────────────────
        DrawSection(paper, $"{id}_vignette", EditorIcons.Eye, "Vignette",
            fx.EnableVignette, v => fx.EnableVignette = v, () =>
        {
            EditorGUI.Slider(paper, $"{id}_vig_int", "Intensity", fx.VignetteIntensity, 0, 1)
                .OnValueChanged(v => fx.VignetteIntensity = v);
            EditorGUI.Slider(paper, $"{id}_vig_sm", "Smoothness", fx.VignetteSmoothness, 0.01f, 1)
                .OnValueChanged(v => fx.VignetteSmoothness = v);
            EditorGUI.Slider(paper, $"{id}_vig_rn", "Roundness", fx.VignetteRoundness, 0, 1)
                .OnValueChanged(v => fx.VignetteRoundness = v);
        });

        // ── Chromatic Aberration ──────────────────────────
        DrawSection(paper, $"{id}_chroma", EditorIcons.Droplet, "Chromatic Aberration",
            fx.EnableChromaticAberration, v => fx.EnableChromaticAberration = v, () =>
        {
            EditorGUI.Slider(paper, $"{id}_chr_int", "Intensity", fx.ChromaticIntensity, 0, 20)
                .OnValueChanged(v => fx.ChromaticIntensity = v);
            EditorGUI.Slider(paper, $"{id}_chr_dist", "Distortion", fx.ChromaticDistortion, 0, 2)
                .OnValueChanged(v => fx.ChromaticDistortion = v);
        });

        // ── Film Grain ────────────────────────────────────
        DrawSection(paper, $"{id}_grain", EditorIcons.Film, "Film Grain",
            fx.EnableFilmGrain, v => fx.EnableFilmGrain = v, () =>
        {
            EditorGUI.Slider(paper, $"{id}_grn_int", "Intensity", fx.GrainIntensity, 0, 1)
                .OnValueChanged(v => fx.GrainIntensity = v);
            EditorGUI.Slider(paper, $"{id}_grn_rsp", "Response", fx.GrainResponse, 0, 1)
                .OnValueChanged(v => fx.GrainResponse = v);
        });

        // ── Color Grading ─────────────────────────────────
        DrawSection(paper, $"{id}_cgrade", EditorIcons.Palette, "Color Grading",
            fx.EnableColorGrading, v => fx.EnableColorGrading = v, () =>
        {
            EditorGUI.Slider(paper, $"{id}_cg_exp", "Post Exposure (EV)", fx.PostExposure, -5, 5)
                .OnValueChanged(v => fx.PostExposure = v);
            EditorGUI.Slider(paper, $"{id}_cg_con", "Contrast", fx.Contrast, -1, 1)
                .OnValueChanged(v => fx.Contrast = v);
            EditorGUI.Slider(paper, $"{id}_cg_sat", "Saturation", fx.Saturation, -1, 1)
                .OnValueChanged(v => fx.Saturation = v);
            EditorGUI.Slider(paper, $"{id}_cg_tmp", "Temperature", fx.Temperature, -1, 1)
                .OnValueChanged(v => fx.Temperature = v);

            EditorGUI.Separator(paper, $"{id}_cg_sep_lgg");
            EditorGUI.Label(paper, $"{id}_cg_lgg_lbl", "Lift / Gamma / Gain");

            EditorGUI.ColorField(paper, $"{id}_cg_lift", "Lift (Shadows)", fx.Lift)
                .OnValueChanged(v => fx.Lift = v);
            EditorGUI.ColorField(paper, $"{id}_cg_gamma", "Gamma (Midtones)", fx.Gamma)
                .OnValueChanged(v => fx.Gamma = v);
            EditorGUI.ColorField(paper, $"{id}_cg_gain", "Gain (Highlights)", fx.Gain)
                .OnValueChanged(v => fx.Gain = v);
        });

        // ── LUT ───────────────────────────────────────────
        DrawSection(paper, $"{id}_lut", EditorIcons.TableCells, "LUT Color Grading",
            fx.EnableLUT, v => fx.EnableLUT = v, () =>
        {
            EngineObjectPropertyEditor.SetFieldType(typeof(Texture2D));
            PropertyGrid.DrawField(paper, $"{id}_lut_tex", "LUT Texture", typeof(Texture2D), fx.LUTTexture.Res,
                newVal =>
                {
                    fx.LUTTexture = new AssetRef<Texture2D>(newVal as Texture2D);
                }, 0);
            EditorGUI.Slider(paper, $"{id}_lut_cont", "Contribution", fx.LUTContribution, 0, 1)
                .OnValueChanged(v => fx.LUTContribution = v);
        });

        // ── Sharpen (CAS) ─────────────────────────────────
        DrawSection(paper, $"{id}_sharp", EditorIcons.Diamond, "Sharpen (CAS)",
            fx.EnableSharpen, v => fx.EnableSharpen = v, () =>
        {
            EditorGUI.Slider(paper, $"{id}_shp_amt", "Amount", fx.SharpenAmount, 0, 1)
                .OnValueChanged(v => fx.SharpenAmount = v);
            EditorGUI.Slider(paper, $"{id}_shp_rad", "Radius", fx.SharpenRadius, 1, 4)
                .OnValueChanged(v => fx.SharpenRadius = v);
        });

        // ── Edge Detection ────────────────────────────────
        DrawSection(paper, $"{id}_edge", EditorIcons.BorderAll, "Edge Detection",
            fx.EnableEdgeDetection, v => fx.EnableEdgeDetection = v, () =>
        {
            EditorGUI.Slider(paper, $"{id}_edg_int", "Intensity", fx.EdgeIntensity, 0, 5)
                .OnValueChanged(v => fx.EdgeIntensity = v);
            EditorGUI.ColorField(paper, $"{id}_edg_col", "Edge Color", fx.EdgeColor)
                .OnValueChanged(v => fx.EdgeColor = v);
            EditorGUI.Slider(paper, $"{id}_edg_bg", "Background Fade", fx.EdgeBackgroundFade, 0, 1)
                .OnValueChanged(v => fx.EdgeBackgroundFade = v);
        });

        // ── Pixelation ────────────────────────────────────
        DrawSection(paper, $"{id}_pixel", EditorIcons.TableCellsLarge, "Pixelation",
            fx.EnablePixelation, v => fx.EnablePixelation = v, () =>
        {
            EditorGUI.Slider(paper, $"{id}_pxl_sz", "Pixel Size", fx.PixelSize, 1, 32)
                .OnValueChanged(v => fx.PixelSize = v);
        });

        // ── God Rays ──────────────────────────────────────
        DrawSection(paper, $"{id}_godrays", EditorIcons.Sun, "God Rays",
            fx.EnableGodRays, v => fx.EnableGodRays = v, () =>
        {
            EditorGUI.Slider(paper, $"{id}_gr_int", "Intensity", fx.GodRayIntensity, 0, 2)
                .OnValueChanged(v => fx.GodRayIntensity = v);
            EditorGUI.Slider(paper, $"{id}_gr_dec", "Decay", fx.GodRayDecay, 0.9f, 1.0f)
                .OnValueChanged(v => fx.GodRayDecay = v);
            EditorGUI.Slider(paper, $"{id}_gr_dns", "Density", fx.GodRayDensity, 0.1f, 2.0f)
                .OnValueChanged(v => fx.GodRayDensity = v);
            EditorGUI.Slider(paper, $"{id}_gr_wgt", "Weight", fx.GodRayWeight, 0, 1)
                .OnValueChanged(v => fx.GodRayWeight = v);
            EditorGUI.Slider(paper, $"{id}_gr_thr", "Threshold", fx.GodRayThreshold, 0, 1)
                .OnValueChanged(v => fx.GodRayThreshold = v);
            EditorGUI.IntField(paper, $"{id}_gr_smp", fx.GodRaySamples, "Samples")
                .OnValueChanged(v => fx.GodRaySamples = System.Math.Clamp(v, 8, 128));
        });
    }

    private static void DrawSection(Paper paper, string id, string icon, string title,
        bool enabled, System.Action<bool> setEnabled, System.Action drawContent)
    {
        EditorGUI.Separator(paper, $"{id}_sep");

        EditorGUI.Toggle(paper, $"{id}_tog", $"{icon}  {title}", enabled)
            .OnValueChanged(v => setEnabled(v));

        if (enabled)
        {
            using (paper.Column($"{id}_body").Height(UnitValue.Auto).Enter())
            {
                drawContent();
            }
        }
    }
}

