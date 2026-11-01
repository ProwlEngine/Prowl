// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Globalization;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.GUI.SceneView;
using Prowl.Editor.Prefabs;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using DColor = System.Drawing.Color;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Inspector for every <see cref="Light"/>: a preset, intensity in the unit of choice with an exposure readout against the
/// camera, colour as a temperature or RGB, the shape rows of the light's kind, its reach and its shadows, help in the tooltips.
/// </summary>
[CustomEditor(typeof(Light))]
public class LightEditor : CustomEditor
{
    private static readonly string[] s_filterNames = ["Hard", "Soft", "Contact"];
    private static readonly ShadowResolution[] s_resolutions = Enum.GetValues<ShadowResolution>();

    private const string KelvinHelp = "Candle 1900, bulb 2700, halogen 3200, daylight 5600, overcast 6500";
    private const string BakeHelp = "Realtime lights every frame. Mixed bakes bounced light only. Baked lives only in lightmaps and probes.";
    private const string BiasHelp = "In shadow map texels. Raise if surfaces stripe, lower if shadows lift off contact points.";
    private const float MinKelvin = 1000f, MaxKelvin = 12000f;

    // The unit each kind of light is edited in, kept for the session
    private readonly Dictionary<Type, LightUnit> _units = [];
    private bool _shadowSettings;

    public override void OnGUI(Paper paper, string id, object target)
    {
        var light = (Light)target;
        if (EditorTheme.DefaultFont == null) return;

        Undo.Snapshot(light);

        PresetRow(paper, id, light);
        IntensityRow(paper, id, light);
        ReadoutRow(paper, id, light);
        ColorRow(paper, id, light);
        if (light is SpotLight spot) ConeRow(paper, id, spot);
        ShapeRows(paper, id, light);
        if (light is LocalLight local) ReachRow(paper, id, local);

        Row(paper, $"{id}_bake", "Bake", BakeHelp, () =>
            Origami.EnumDropdown(paper, $"{id}_bake_v", light.BakeMode, v => Edit(light, () => light.BakeMode = v)).Show(), "bakeMode");

        if (light.CanCastShadows) ShadowRows(paper, id, light);
        LayerAndProfileRows(paper, id, light);
    }

    private static void PresetRow(Paper paper, string id, Light light)
    {
        IReadOnlyList<LightPreset> presets = light.Presets;
        int current = -1;
        for (int i = 0; i < presets.Count && current < 0; i++)
            if (light.IsPreset(presets[i])) current = i;
        string[] names = new string[presets.Count];
        for (int i = 0; i < names.Length; i++) names[i] = presets[i].Name;
        Row(paper, $"{id}_preset", "Preset", "Sets the colour temperature, shape and brightness of a real light", () =>
            Origami.Dropdown(paper, $"{id}_preset_v", current, i => Edit(light, () => light.ApplyPreset(presets[i])), names)
                .Placeholder("Custom").Show());
    }

    private void IntensityRow(Paper paper, string id, Light light)
    {
        LightUnit unit = UnitOf(light);
        IReadOnlyList<LightUnit> units = light.Units;
        Row(paper, $"{id}_int", "Intensity", UnitHelp(unit), () =>
        {
            Origami.NumericField<float>(paper, $"{id}_int_v", light.FromStored(unit, light.Intensity),
                v => Edit(light, () => light.Intensity = MathF.Max(0f, light.ToStored(unit, v))))
                .Format("0.###").Show();
            string[] names = new string[units.Count];
            for (int i = 0; i < names.Length; i++) names[i] = UnitName(units[i]);
            int selected = 0;
            for (int i = 0; i < units.Count; i++)
                if (units[i] == unit) selected = i;
            Origami.Dropdown(paper, $"{id}_unit_v", selected, i => _units[light.GetType()] = units[i], names).Width(80).Show();
        }, "intensity");
    }

    private void ReadoutRow(Paper paper, string id, Light light)
    {
        LightUnit unit = UnitOf(light);
        LightUnit other = unit;
        foreach (LightUnit u in light.Units)
            if (u != unit) other = u;
        bool local = light is LocalLight;
        float lux = light is LocalLight localLight ? localLight.AxisCandela : light.Intensity;
        float ev = MathF.Log2(MathF.Max(lux, 1e-6f) / 2.5f);
        Camera? camera = Camera.Main;
        float cameraEv = camera.IsValid() ? camera.EV100 : 14f;
        float diff = ev - cameraEv;
        string stops = MathF.Abs(diff).ToString("F1", CultureInfo.InvariantCulture);

        (DColor tone, string verdict) = diff switch
        {
            < -6f => (EditorTheme.InkDim, $"{stops} stops under"),
            < -2f => (EditorTheme.Ink400, $"{stops} stops under"),
            <= 2f => (EditorTheme.Green400, "well exposed"),
            _ => (EditorTheme.Amber400, $"{stops} stops over"),
        };
        string evText = ev.ToString("F1", CultureInfo.InvariantCulture);
        string text = $"{Format(light.FromStored(other, light.Intensity))} {UnitName(other)}, {(local ? "at 1 m" : "lit")} EV {evText}, {verdict}";
        string help = $"A white surface {(local ? "1 m away facing it" : "facing it")} gets {Format(lux)} lux and needs EV100 {evText}. "
            + $"The camera is at EV100 {cameraEv.ToString("0.#", CultureInfo.InvariantCulture)}, so it reads {verdict}.";

        Row(paper, $"{id}_read", "", help, () =>
        {
            paper.Box($"{id}_read_dot").Width(6).Height(6).Rounded(3).Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
                .BackgroundColor(tone).IsNotInteractable();
            paper.Box($"{id}_read_t").Height(EditorTheme.RowHeight).IsNotInteractable()
                .Text(text, EditorTheme.DefaultFont).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleLeft).TextTruncate();
        });
    }

    private static void ColorRow(Paper paper, string id, Light light)
    {
        Row(paper, $"{id}_col", "Colour", light.UseTemperature ? KelvinHelp : "Linear colour", () =>
        {
            if (light.UseTemperature)
            {
                paper.Box($"{id}_col_sw").Width(22).Height(22).Rounded(4).Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
                    .BackgroundColor(Display(light.FinalColor)).BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
                    .Tooltip("The colour the light shines");
                Origami.Slider(paper, $"{id}_col_k", Maths.Clamp(light.Temperature, MinKelvin, MaxKelvin),
                        v => Edit(light, () => light.Temperature = v), MinKelvin, MaxKelvin)
                    .Step(50f).ShowValue(false).CustomTrack(KelvinTrack).Show();
                Origami.NumericField<float>(paper, $"{id}_col_kv", light.Temperature,
                        v => Edit(light, () => light.Temperature = Maths.Clamp(v, ColorTemperature.Min, ColorTemperature.Max)))
                    .Format("0").Suffix("K").Width(72).Show();
            }
            else
            {
                Origami.ColorField(paper, $"{id}_col_rgb", light.Color, v => Edit(light, () => light.Color = v)).Show();
            }

            Origami.Button(paper, $"{id}_col_mode", light.UseTemperature ? "RGB" : "K", () => Edit(light, () =>
            {
                if (light.UseTemperature) light.Color = light.FinalColor;
                else light.Color = Color.White;
                light.UseTemperature = !light.UseTemperature;
            })).Small().Width(40).Tooltip("Switch between Kelvin and RGB").Show();
        }, "useTemperature", "temperature", "color");
    }

    // The slider's track as the colours of the temperatures under it
    private static void KelvinTrack(Quill.Canvas canvas, SliderTrackContext ctx)
    {
        const int segments = 44;
        const float thumb = 6f, thickness = 6f;
        float x = (float)ctx.Rect.Min.X, width = (float)ctx.Rect.Size.X;
        float y = (float)(ctx.Rect.Min.Y + ctx.Rect.Size.Y * 0.5f) - thickness * 0.5f;
        float step = width / segments;
        for (int i = 0; i < segments; i++)
        {
            float centre = x + step * (i + 0.5f);
            float t = Maths.Clamp((centre - x - thumb) / MathF.Max(width - 2f * thumb, 1f), 0f, 1f);
            DColor c = Display(ColorTemperature.ToLinear(MinKelvin + t * (MaxKelvin - MinKelvin)));
            canvas.RectFilled(x + step * i, y, step + 0.5f, thickness, Color32.FromArgb(255, c.R, c.G, c.B));
        }
    }

    private void ConeRow(Paper paper, string id, SpotLight light)
    {
        Row(paper, $"{id}_cone", "Cone", "The whole cone angle in degrees, then how much of the cone fades out at the edge", () =>
        {
            Origami.Slider(paper, $"{id}_cone_a", light.SpotAngle, v => KeepLumens(light, () => light.SpotAngle = v), 1f, 179f)
                .Step(1f).Format("0").ValueWidth(44).Show();
            Origami.Slider(paper, $"{id}_cone_s", light.SpotSoftness, v => Edit(light, () => light.SpotSoftness = v), 0f, 1f)
                .Format("0.00").ValueWidth(44).Show();
        }, "spotAngle", "spotSoftness");
    }

    private void ShapeRows(Paper paper, string id, Light light)
    {
        string keep = UnitOf(light) == LightUnit.Lumen ? "Keeps lumens, so bigger spreads the same light thinner." : "Keeps nits, so bigger puts out more light.";
        const string radiusHelp = "Bulb radius in metres. Softens highlights and contact shadows.";
        switch (light)
        {
            case DirectionalLight sun:
                ShapeSlider(paper, $"{id}_sun", "Sun size", "Angular radius of the sun disk in degrees, about 0.27 for the real sun. Softens highlights and contact shadows.",
                    "angularRadius", sun.AngularRadius, 0f, 5f, v => Edit(sun, () => sun.AngularRadius = v));
                break;
            case PunctualLight punctual:
                ShapeSlider(paper, $"{id}_rad", "Radius", radiusHelp, "sourceRadius", punctual.SourceRadius, 0f, 0.5f, v => Edit(punctual, () => punctual.SourceRadius = v));
                break;
            case RectLight rect:
                ShapeSlider(paper, $"{id}_w", "Width", keep, "width", rect.Width, 0.05f, 5f, v => KeepLumens(rect, () => rect.Width = v));
                ShapeSlider(paper, $"{id}_h", "Height", keep, "height", rect.Height, 0.05f, 5f, v => KeepLumens(rect, () => rect.Height = v));
                break;
            case DiskLight disk:
                ShapeSlider(paper, $"{id}_w", "Diameter", keep, "diameter", disk.Diameter, 0.05f, 5f, v => KeepLumens(disk, () => disk.Diameter = v));
                break;
            case TubeLight tube:
                ShapeSlider(paper, $"{id}_len", "Length", keep, "length", tube.Length, 0.05f, 5f, v => KeepLumens(tube, () => tube.Length = v));
                ShapeSlider(paper, $"{id}_rad", "Radius", keep, "radius", tube.Radius, 0.001f, 0.2f, v => KeepLumens(tube, () => tube.Radius = v));
                break;
        }
    }

    private static void ShapeSlider(Paper paper, string id, string label, string help, string field, float value, float min, float max, Action<float> set)
        => Row(paper, id, label, help, () => Origami.Slider(paper, $"{id}_v", value, set, min, max).Format("0.###").ValueWidth(64).Show(), field);

    private static void ReachRow(Paper paper, string id, LocalLight light)
    {
        string cutoff = QualitySettings.Active.LightCutoff.ToString("0.###", CultureInfo.InvariantCulture);
        string help = light.OverrideRange
            ? "Set by hand. The light stops at this distance."
            : $"Worked out from intensity: where the light falls below {cutoff} lux, the light cutoff in Quality settings.";
        Row(paper, $"{id}_reach", "Reach", help, () =>
        {
            if (light.OverrideRange)
            {
                Origami.NumericField<float>(paper, $"{id}_reach_v", light.Range, v => Edit(light, () => light.Range = MathF.Max(0.01f, v)))
                    .Format("0.##").Suffix("m").Show();
                Origami.Button(paper, $"{id}_reach_auto", "Auto", () => Edit(light, () => light.OverrideRange = false))
                    .Small().Width(56).Tooltip("Work the reach out from the intensity again").Show();
            }
            else
            {
                paper.Box($"{id}_reach_t").Height(EditorTheme.RowHeight).IsNotInteractable()
                    .Text($"{Format(light.Range)} m automatic", EditorTheme.DefaultFont).TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                Origami.Button(paper, $"{id}_reach_set", "Set", () => Edit(light, () => light.Range = light.AutomaticRange))
                    .Small().Width(56).Tooltip("Set the reach by hand").Show();
            }
        }, "overrideRange", "range");
    }

    private void ShadowRows(Paper paper, string id, Light light)
    {
        Row(paper, $"{id}_sh", "Shadows", null, () =>
        {
            Origami.Checkbox(paper, $"{id}_sh_on", light.CastShadows, v => Edit(light, () => light.CastShadows = v)).NoLabel().Show();
            if (!light.CastShadows) return;
            using (paper.Box($"{id}_sh_tip").Height(UnitValue.Auto).Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch()).Tooltip("Shadow strength").Enter())
                Origami.Slider(paper, $"{id}_sh_str", light.ShadowStrength, v => Edit(light, () => light.ShadowStrength = v), 0f, 1f)
                    .Format("0.00").ValueWidth(44).Show();
            ButtonBuilder settings = Origami.IconButton(paper, $"{id}_sh_set", EditorIcons.Sliders, () => _shadowSettings = !_shadowSettings)
                .Small().Tooltip("Shadow settings");
            if (_shadowSettings) settings.Primary(); else settings.Ghost();
            settings.Show();
        }, "castShadows", "shadowStrength");

        if (!light.CastShadows || !_shadowSettings) return;

        string resHelp = light is DirectionalLight
            ? "Largest shadow tile, capped by the quality level"
            : "Largest shadow tile. Local lights size theirs from screen size up to this.";
        Row(paper, $"{id}_map", "Map", resHelp, () =>
        {
            Origami.Dropdown(paper, $"{id}_map_res", light.ShadowResolution, v => Edit(light, () => light.ShadowResolution = v), s_resolutions)
                .Display(r => ((int)r).ToString(CultureInfo.InvariantCulture)).Show();
            Origami.Dropdown(paper, $"{id}_map_f", (int)light.ShadowFilter, i => Edit(light, () => light.ShadowFilter = (ShadowFilter)i), s_filterNames).Show();
        }, "shadowResolution", "shadowFilter");

        Row(paper, $"{id}_bias", "Bias", BiasHelp, () =>
        {
            Origami.NumericField<float>(paper, $"{id}_bias_d", light.DepthBias, v => Edit(light, () => light.DepthBias = v))
                .Format("0.###").Prefix("depth", EditorTheme.InkDim).Show();
            Origami.NumericField<float>(paper, $"{id}_bias_n", light.NormalBias, v => Edit(light, () => light.NormalBias = v))
                .Format("0.###").Prefix("normal", EditorTheme.InkDim).Show();
        }, "depthBias", "normalBias");

        if (light is DirectionalLight sun)
            Row(paper, $"{id}_blend", "Blend", "How much of each cascade blends into the next. Cascade count and distance come from Quality settings.", () =>
                Origami.Slider(paper, $"{id}_blend_v", sun.CascadeBlend, v => Edit(sun, () => sun.CascadeBlend = v), 0f, 1f)
                    .Format("0.00").ValueWidth(44).Show(), "cascadeBlend");
    }

    private static void LayerAndProfileRows(Paper paper, string id, Light light)
    {
        PropertyGridUtils.DrawField(paper, $"{id}_layers", "Layers", typeof(LayerMask), light.LayerMask,
            v => Edit(light, () => light.LayerMask = (LayerMask)v!));
        if (light is not PunctualLight punctual) return;
        PropertyGridUtils.DrawField(paper, $"{id}_cookie", "Cookie", typeof(Texture), punctual.Cookie,
            v => Edit(punctual, () => punctual.Cookie = v as Texture));
        PropertyGridUtils.DrawField(paper, $"{id}_ies", "IES Profile", typeof(Texture2D), punctual.IesProfile,
            v => Edit(punctual, () => punctual.IesProfile = v as Texture2D));
    }

    // A labelled row with its help on the label, marked like the property grid when one of its fields is a prefab override
    private static void Row(Paper paper, string id, string label, string? help, Action draw, params string[] fields)
    {
        OrigamiTheme theme = Origami.Current;
        OrigamiMetrics m = theme.Metrics;
        bool overridden = false;
        foreach (string field in fields)
            overridden |= PropertyGridUtils.OverriddenFields?.Contains(field) == true;

        using (paper.Row(id).Height(UnitValue.Auto).MinHeight(m.RowHeight)
            .Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).Gap(m.Padding).Margin(0, 0, 0, m.SpacingLarge).Enter())
        {
            if (overridden)
                using (paper.Box($"{id}_ov").Width(8).Height(m.RowHeight).IsNotInteractable().Enter())
                    paper.Box($"{id}_ovd").Width(5).Height(5).Rounded(3)
                        .Margin(1, UnitValue.Stretch(), UnitValue.Stretch(), UnitValue.Stretch())
                        .BackgroundColor(theme.Primary.C500).IsNotInteractable();

            ElementBuilder labelBox = paper.Box($"{id}_l").Width(m.LabelWidth).Height(m.RowHeight)
                .Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
                .Text(label, EditorTheme.DefaultFont).TextColor(overridden ? theme.Primary.C700 : theme.Ink.C300)
                .FontSize(m.FontSize).Alignment(TextAlignment.MiddleLeft).TextTruncate();
            if (help != null) labelBox.Tooltip(help);
            else labelBox.IsNotInteractable();

            ElementBuilder controls = paper.Row($"{id}_c").Height(UnitValue.Auto).Gap(m.Padding).Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch());
            if (help != null && label.Length == 0) controls.Tooltip(help);
            using (controls.Enter())
                draw();
        }
    }

    private static void Edit(Light light, Action change)
    {
        change();
        PrefabUtility.NotifyEdited(light);
        EditorSceneManager.MarkDirty();
    }

    // A change to the cone or the size that keeps the light's lumens while it is edited in lumens
    private void KeepLumens(Light light, Action change)
    {
        bool lumens = UnitOf(light) == LightUnit.Lumen;
        float amount = light.FromStored(LightUnit.Lumen, light.Intensity);
        Edit(light, () =>
        {
            change();
            if (lumens) light.Intensity = light.ToStored(LightUnit.Lumen, amount);
        });
    }

    private LightUnit UnitOf(Light light) => _units.TryGetValue(light.GetType(), out LightUnit unit) ? unit : light.Units[0];

    private static string UnitName(LightUnit unit) => unit switch
    {
        LightUnit.Lux => "lux",
        LightUnit.Ev100 => "EV100",
        LightUnit.Lumen => "lm",
        LightUnit.Candela => "cd",
        _ => "nits",
    };

    private static string UnitHelp(LightUnit unit) => unit switch
    {
        LightUnit.Lux => "Lux: light landing on a surface facing it. Midday sun is about 100000, an office about 500.",
        LightUnit.Ev100 => "EV100: the camera exposure a white card in this light needs. Sunny days are about 15.",
        LightUnit.Lumen => "Lumens: all the light it puts out, the number on a bulb box. A 60 W bulb is about 800.",
        LightUnit.Candela => "Candela: brightness in its brightest direction. One candela is about one candle.",
        _ => "Nits: how bright its surface looks. A monitor is about 250.",
    };

    private static string Format(float value)
    {
        float a = MathF.Abs(value);
        if (!float.IsFinite(value)) return "-";
        if (a >= 1000f) return value.ToString("N0", CultureInfo.InvariantCulture);
        if (a >= 100f) return value.ToString("F0", CultureInfo.InvariantCulture);
        if (a >= 10f) return value.ToString("F1", CultureInfo.InvariantCulture);
        if (a >= 1f) return value.ToString("F2", CultureInfo.InvariantCulture);
        return value.ToString("G2", CultureInfo.InvariantCulture);
    }

    // A linear colour as the screen shows it, scaled so its brightest channel is full
    private static DColor Display(Color linear)
    {
        float peak = MathF.Max(linear.R, MathF.Max(linear.G, linear.B));
        if (peak <= 0f) return DColor.Black;
        Color gamma = Color.LinearToGamma(new Color(linear.R / peak, linear.G / peak, linear.B / peak, 1f));
        return DColor.FromArgb(255, (int)(Maths.Clamp(gamma.R, 0f, 1f) * 255f), (int)(Maths.Clamp(gamma.G, 0f, 1f) * 255f), (int)(Maths.Clamp(gamma.B, 0f, 1f) * 255f));
    }
}
