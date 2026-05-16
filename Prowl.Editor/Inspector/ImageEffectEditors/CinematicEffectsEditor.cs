using Prowl.Editor.Widgets;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using PropertyGrid = Prowl.Editor.Widgets.PropertyGrid;
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
            SliderRow(paper, $"{id}_vig_int", "Intensity", fx.VignetteIntensity, 0, 1, v => fx.VignetteIntensity = v);
            SliderRow(paper, $"{id}_vig_sm", "Smoothness", fx.VignetteSmoothness, 0.01f, 1, v => fx.VignetteSmoothness = v);
            SliderRow(paper, $"{id}_vig_rn", "Roundness", fx.VignetteRoundness, 0, 1, v => fx.VignetteRoundness = v);
        });

        // ── Chromatic Aberration ──────────────────────────
        DrawSection(paper, $"{id}_chroma", EditorIcons.Droplet, "Chromatic Aberration",
            fx.EnableChromaticAberration, v => fx.EnableChromaticAberration = v, () =>
        {
            SliderRow(paper, $"{id}_chr_int", "Intensity", fx.ChromaticIntensity, 0, 20, v => fx.ChromaticIntensity = v);
            SliderRow(paper, $"{id}_chr_dist", "Distortion", fx.ChromaticDistortion, 0, 2, v => fx.ChromaticDistortion = v);
        });

        // ── Film Grain ────────────────────────────────────
        DrawSection(paper, $"{id}_grain", EditorIcons.Film, "Film Grain",
            fx.EnableFilmGrain, v => fx.EnableFilmGrain = v, () =>
        {
            SliderRow(paper, $"{id}_grn_int", "Intensity", fx.GrainIntensity, 0, 1, v => fx.GrainIntensity = v);
            SliderRow(paper, $"{id}_grn_rsp", "Response", fx.GrainResponse, 0, 1, v => fx.GrainResponse = v);
        });

        // ── Color Grading ─────────────────────────────────
        DrawSection(paper, $"{id}_cgrade", EditorIcons.Palette, "Color Grading",
            fx.EnableColorGrading, v => fx.EnableColorGrading = v, () =>
        {
            SliderRow(paper, $"{id}_cg_exp", "Post Exposure (EV)", fx.PostExposure, -5, 5, v => fx.PostExposure = v, bipolar: true);
            SliderRow(paper, $"{id}_cg_con", "Contrast", fx.Contrast, -1, 1, v => fx.Contrast = v, bipolar: true);
            SliderRow(paper, $"{id}_cg_sat", "Saturation", fx.Saturation, -1, 1, v => fx.Saturation = v, bipolar: true);
            SliderRow(paper, $"{id}_cg_tmp", "Temperature", fx.Temperature, -1, 1, v => fx.Temperature = v, bipolar: true);

            Origami.Separator(paper, $"{id}_cg_sep_lgg").Show();
            Origami.Label(paper, $"{id}_cg_lgg_lbl", "Lift / Gamma / Gain").Show();

            InspectorRow.Draw(paper, $"{id}_cg_lift", "Lift (Shadows)", () =>
                Origami.ColorField(paper, $"{id}_cg_lift_cf", fx.Lift, v => fx.Lift = v).Show());
            InspectorRow.Draw(paper, $"{id}_cg_gamma", "Gamma (Midtones)", () =>
                Origami.ColorField(paper, $"{id}_cg_gamma_cf", fx.Gamma, v => fx.Gamma = v).Show());
            InspectorRow.Draw(paper, $"{id}_cg_gain", "Gain (Highlights)", () =>
                Origami.ColorField(paper, $"{id}_cg_gain_cf", fx.Gain, v => fx.Gain = v).Show());
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
            SliderRow(paper, $"{id}_lut_cont", "Contribution", fx.LUTContribution, 0, 1, v => fx.LUTContribution = v);
        });

        // ── Sharpen (CAS) ─────────────────────────────────
        DrawSection(paper, $"{id}_sharp", EditorIcons.Diamond, "Sharpen (CAS)",
            fx.EnableSharpen, v => fx.EnableSharpen = v, () =>
        {
            SliderRow(paper, $"{id}_shp_amt", "Amount", fx.SharpenAmount, 0, 1, v => fx.SharpenAmount = v);
            SliderRow(paper, $"{id}_shp_rad", "Radius", fx.SharpenRadius, 1, 4, v => fx.SharpenRadius = v);
        });

        // ── Edge Detection ────────────────────────────────
        DrawSection(paper, $"{id}_edge", EditorIcons.BorderAll, "Edge Detection",
            fx.EnableEdgeDetection, v => fx.EnableEdgeDetection = v, () =>
        {
            SliderRow(paper, $"{id}_edg_int", "Intensity", fx.EdgeIntensity, 0, 5, v => fx.EdgeIntensity = v);
            InspectorRow.Draw(paper, $"{id}_edg_col", "Edge Color", () =>
                Origami.ColorField(paper, $"{id}_edg_col_cf", fx.EdgeColor, v => fx.EdgeColor = v).Show());
            SliderRow(paper, $"{id}_edg_bg", "Background Fade", fx.EdgeBackgroundFade, 0, 1, v => fx.EdgeBackgroundFade = v);
        });

        // ── Pixelation ────────────────────────────────────
        DrawSection(paper, $"{id}_pixel", EditorIcons.TableCellsLarge, "Pixelation",
            fx.EnablePixelation, v => fx.EnablePixelation = v, () =>
        {
            SliderRow(paper, $"{id}_pxl_sz", "Pixel Size", fx.PixelSize, 1, 32, v => fx.PixelSize = v);
        });

        // ── God Rays ──────────────────────────────────────
        DrawSection(paper, $"{id}_godrays", EditorIcons.Sun, "God Rays",
            fx.EnableGodRays, v => fx.EnableGodRays = v, () =>
        {
            SliderRow(paper, $"{id}_gr_int", "Intensity", fx.GodRayIntensity, 0, 2, v => fx.GodRayIntensity = v);
            SliderRow(paper, $"{id}_gr_dec", "Decay", fx.GodRayDecay, 0.9f, 1.0f, v => fx.GodRayDecay = v);
            SliderRow(paper, $"{id}_gr_dns", "Density", fx.GodRayDensity, 0.1f, 2.0f, v => fx.GodRayDensity = v);
            SliderRow(paper, $"{id}_gr_wgt", "Weight", fx.GodRayWeight, 0, 1, v => fx.GodRayWeight = v);
            SliderRow(paper, $"{id}_gr_thr", "Threshold", fx.GodRayThreshold, 0, 1, v => fx.GodRayThreshold = v);
            InspectorRow.Draw(paper, $"{id}_gr_smp", "Samples", () =>
                Origami.IntSlider(paper, $"{id}_gr_smp_v", fx.GodRaySamples,
                    v => fx.GodRaySamples = System.Math.Clamp(v, 8, 128), 8, 128).Show());
        });
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private static void SliderRow(Paper paper, string id, string label, float value, float min, float max, System.Action<float> setter, bool bipolar = false)
        => InspectorRow.Draw(paper, id, label, () =>
        {
            var s = Origami.Slider(paper, $"{id}_v", value, setter, min, max).Format("F2");
            if (bipolar) s.Bipolar();
            s.Show();
        });

    private static void DrawSection(Paper paper, string id, string icon, string title,
        bool enabled, System.Action<bool> setEnabled, System.Action drawContent)
    {
        Origami.Foldout(paper, id, $"{icon}  {title}")
            .Toggle(enabled, setEnabled)
            .Body(drawContent);
    }
}
