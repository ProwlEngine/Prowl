// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;
using VColor = Prowl.Vector.Color;

namespace Prowl.Editor.GUI;

/// <summary>
/// Shared immediate-mode building blocks for the editor's own panels and inspectors
/// </summary>
public static class EditorGUI
{
    private static UnitValue ST => UnitValue.StretchOne;
    private static UnitValue STV => UnitValue.Stretch();

    // =====================================================================
    //  Rows
    // =====================================================================

    /// <summary>
    /// A fixed-width label on the left and the caller's control filling the remainder. When
    /// <paramref name="separator"/> is false (the inspector default) rows are spaced with a
    /// bottom gap; when true a 1px divider is drawn beneath the row. An
    /// empty <paramref name="label"/> gives the control the full width.
    /// </summary>
    public static void Row(Paper paper, string id, string label, Action drawControl,
        bool separator = false, float? labelWidth = null, bool compact = false, float minHeight = 0f)
    {
        var m = Origami.Current.Metrics;

        if (!separator)
        {
            // Inspector rhythm: the between-row gap is a Spacing-driven bottom margin (not vertical
            // padding) so hand-drawn rows line up with the reflection-drawn PropertyGrid. Compact rows
            // rely on their host column's ColBetween instead, so they take no bottom margin.
            DrawRowLine(paper, id, label, drawControl, m, labelWidth, compact, minHeight,
                vpad: 0, bottomMargin: compact ? 0f : m.SpacingLarge);
            return;
        }

        // A Spacing-driven gap after the divider, so between-setting spacing tracks the theme's Spacing.
        using (paper.Column(id).Width(ST).Height(UnitValue.Auto).Margin(0, 0, 0, m.Spacing).Enter())
        {
            DrawRowLine(paper, $"{id}_r", label, drawControl, m, labelWidth, compact, minHeight,
                vpad: m.PaddingSmall, bottomMargin: 0);
            paper.Box($"{id}_d").Width(ST).Height(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();
        }
    }

    private static void DrawRowLine(Paper paper, string id, string label, Action drawControl,
        OrigamiMetrics m, float? labelWidth, bool compact, float minHeight, float vpad, float bottomMargin)
    {
        var font = EditorTheme.DefaultFont;
        float rh = minHeight > 0f ? minHeight : m.RowHeight;
        // Compact rows (theme editor) use a brighter, smaller label and no horizontal padding since the
        // hosting column already pads; standard rows use the muted label + a PaddingLarge gutter.
        float hpad = compact ? 0f : m.PaddingLarge;
        var labelColor = compact ? EditorTheme.Ink500 : Origami.Current.Ink.C300;
        float labelSize = compact ? EditorTheme.FontSizeSmall : m.FontSize;
        using (paper.Row(id).Width(ST).Height(UnitValue.Auto).MinHeight(rh)
            .Padding(hpad, hpad, vpad, vpad).RowBetween(m.Padding)
            .Margin(0, 0, 0, bottomMargin).Enter())
        {
            if (!string.IsNullOrEmpty(label) && font != null)
                paper.Box($"{id}_l").Width(labelWidth ?? m.LabelWidth).Height(rh)
                    .Margin(0, 0, ST, ST).IsNotInteractable()
                    .Text(label, font).TextColor(labelColor).FontSize(labelSize)
                    .Alignment(TextAlignment.MiddleLeft).TextTruncate();

            // The control sizes to its widget and centers via stretch margins. (A forced MinHeight here
            // used to top-align the widget while the label stayed centered, so they didn't line up.)
            using (paper.Box($"{id}_c").Width(ST).Height(UnitValue.Auto).Margin(0, 0, ST, ST).Enter())
                drawControl();
        }
    }

    /// <summary>A settings-window row (label + control with a bottom divider by default).</summary>
    public static void SettingsRow(Paper paper, string id, string label, Action drawControl,
        bool separator = true, float? labelWidth = null, bool compact = false, float minHeight = 0f)
        => Row(paper, id, label, drawControl, separator, labelWidth, compact, minHeight);

    /// <summary>A label + pill-toggle settings row (respects the label gutter).</summary>
    public static void SettingsToggle(Paper paper, string id, string label, bool value,
        Action<bool> setter, bool separator = true, bool compact = false)
        => SettingsRow(paper, id, label, () =>
        {
            using (paper.Row($"{id}_tw").Width(ST).Height(Origami.Current.Metrics.RowHeight)
                .Margin(0, 0, ST, ST).Enter())
                Origami.Switch(paper, $"{id}_sw", value, setter).NoLabel().Show();
        }, separator, compact: compact);

    /// <summary>A label + slider settings row. The value is rounded to 2 decimals before
    /// <paramref name="setter"/> is called; <paramref name="format"/> controls the readout.</summary>
    public static void SettingsSlider(Paper paper, string id, string label, float value, float min, float max,
        Action<float> setter, string format = "F2", bool separator = true, bool compact = false)
        => SettingsRow(paper, id, label, () =>
            Origami.Slider(paper, $"{id}_v", value, v => setter(MathF.Round(v, 2)), min, max)
                .Format(format).Show(), separator, compact: compact);

    /// <summary>A label + colour-field settings row editing a "#RRGGBB" hex string.</summary>
    public static void SettingsColorField(Paper paper, string id, string label, Func<string> get,
        Action<string> setter, bool separator = true, bool compact = false)
        => SettingsRow(paper, id, label, () =>
            Origami.ColorField(paper, $"{id}_v", HexToVColor(get()), v => setter(VColorToHex(v)))
                .Width(130f).Show(), separator, compact: compact);

    // =====================================================================
    //  Settings-window chrome
    // =====================================================================

    /// <summary>Left category rail. Each entry is (id, label, icon glyph). Returns the sidebar width
    /// so the caller can size its content area.</summary>
    public static float Sidebar(Paper paper, string id, (string id, string label, string icon)[] cats,
        string active, Action<string> onSelect, float width = 148f)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return width;

        // Dark sidebar; the centered band re-uses the same colour so its overlap reads a touch darker.
        using (paper.Column(id).Width(width).Height(ST).BackgroundColor(Color.FromArgb(36, 0, 0, 0)).Enter())
        using (paper.Column($"{id}_grp").Width(ST).Height(UnitValue.Auto).Margin(0, 0, STV, STV)
            .Padding(8, 8, 10, 10).ColBetween(2).BackgroundColor(Color.FromArgb(36, 0, 0, 0)).Enter())
        {
            foreach (var (cid, label, icon) in cats)
            {
                bool on = cid == active;
                using (paper.Row($"{id}_{cid}").Width(ST).Height(30).Rounded(8).Padding(10, 10, 0, 0)
                    .BackgroundColor(on ? EditorTheme.Selected : Color.Transparent)
                    .Hovered.BackgroundColor(on ? EditorTheme.Selected : EditorTheme.Hover).End()
                    .OnClick(cid, (c, _) => onSelect(c))
                    .Enter())
                {
                    paper.Box($"{id}_{cid}_i").Width(16).Height(30).IsNotInteractable()
                        .Text(icon, font).TextColor(on ? EditorTheme.Ink500 : EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
                    paper.Box($"{id}_{cid}_l").Width(ST).Height(30).Margin(9, 0, 0, 0).IsNotInteractable()
                        .Text(label, font).TextColor(on ? EditorTheme.Ink500 : EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                }
            }
        }
        return width;
    }

    /// <summary>Uppercase accent section header, left-aligned to the row labels. The <paramref name="compact"/>
    /// variant (theme editor) is shorter and sits flush-left since its host column already pads.</summary>
    public static void SectionHeader(Paper paper, string id, string text, bool first = false, bool compact = false)
    {
        var font = EditorTheme.FontSemiBold ?? EditorTheme.DefaultFont;
        if (font == null) return;
        var m = Origami.Current.Metrics;
        float h = compact ? 18f : 22f;
        float leftPad = compact ? 0f : m.PaddingLarge;
        float topGap = compact ? (first ? 0f : EditorTheme.Spacing * 2f) : (first ? 2f : 14f);
        float botGap = compact ? EditorTheme.Spacing : 4f;
        paper.Box(id).Width(ST).Height(h).Margin(leftPad, 0, topGap, botGap).IsNotInteractable()
            .Text(text.ToUpperInvariant(), font).TextColor(EditorTheme.AccentText)
            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
    }

    /// <summary>Full-width 1px separator (for rows a caller draws itself, e.g. color fields).</summary>
    public static void Divider(Paper paper, string id)
        => paper.Box(id).Width(ST).Height(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

    /// <summary>
    /// A grouped section panel that visually nests its content: a soft rounded inset card with a clean
    /// title strip (icon optional) + divider and a padded body. Use to group related rows/controls.
    /// </summary>
    public static void Group(Paper paper, string id, string? title, Action body, string? icon = null)
    {
        var m = Origami.Current.Metrics;
        using (paper.Column(id).Width(ST).Height(UnitValue.Auto)
            .Margin(m.PaddingLarge, m.PaddingLarge, 0, m.SpacingLarge)
            .Rounded(10).Clip()
            .BackgroundColor(Color.FromArgb(38, 0, 0, 0)).BorderColor(EditorTheme.BorderSoft).BorderWidth(1).Enter())
        {
            var font = EditorTheme.FontSemiBold ?? EditorTheme.DefaultFont;
            if (!string.IsNullOrEmpty(title) && font != null)
            {
                using (paper.Row($"{id}_gh").Width(ST).Height(32).Padding(m.PaddingLarge, m.PaddingLarge, 0, 0)
                    .RowBetween(m.SpacingMedium).IsNotInteractable().Enter())
                {
                    if (!string.IsNullOrEmpty(icon))
                        paper.Box($"{id}_gi").Width(16).Height(ST).Margin(0, 0, STV, STV).IsNotInteractable()
                            .Text(icon, EditorTheme.DefaultFont).TextColor(EditorTheme.AccentText)
                            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
                    paper.Box($"{id}_gt").Width(ST).Height(ST).Margin(0, 0, STV, STV).IsNotInteractable()
                        .Text(title, font).TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSizeSmall)
                        .Alignment(TextAlignment.MiddleLeft);
                }
                paper.Box($"{id}_gd").Width(ST).Height(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();
            }

            using (paper.Column($"{id}_gb").Width(ST).Height(UnitValue.Auto)
                .Padding(m.SpacingSmall, m.SpacingSmall, m.SpacingMedium, m.SpacingMedium).Enter())
                body();
        }
    }

    /// <summary>A glass pill button (label + click). <paramref name="leftGap"/> adds explicit spacing
    /// from a previous chip.</summary>
    public static void Chip(Paper paper, string id, string label, Action onClick, float leftGap = 0f)
    {
        var font = EditorTheme.DefaultFont;
        paper.Box(id).Width(UnitValue.Auto).Height(28).Margin(leftGap, 0, ST, ST).Rounded(8).Padding(11, 11, 0, 0)
            .BackgroundColor(EditorTheme.Neutral400).BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .Hovered.BorderColor(EditorTheme.BorderStrong).End()
            .Text(label, font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
            .Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) => onClick());
    }

    // =====================================================================
    //  Colour editing (theme ramps)
    // =====================================================================

    /// <summary>A colour row: a colour box (opens the picker) + a quick-pick palette of swatches, editing
    /// a <see cref="ColorRamp"/>'s primary. Small palettes sit inline; larger ones drop to a second line.
    /// Each selected swatch draws a 2px accent ring just outside the fill so it reads even when the swatch
    /// colour equals the accent.</summary>
    public static void SwatchRow(Paper paper, EditorSettings s, string id, string label, ColorRamp ramp, string[] palette)
    {
        float sp = EditorTheme.Spacing, pad = EditorTheme.Padding;
        bool inline = palette.Length <= 6;

        void ColorBox() =>
            Origami.ColorField(paper, $"{id}_cf", HexToVColor(ramp.Primary), v =>
            { ramp.Primary = VColorToHex(v); ramp.OverrideAll = false; s.ApplyTheme(); s.Save(); }).Width(130f).Show();

        void Swatch(string hex)
        {
            var col = ColorRamp.ParseHex(hex);
            bool on = string.Equals(ramp.Primary, hex, StringComparison.OrdinalIgnoreCase);
            paper.Box($"{id}_p_{hex}").Width(28).Height(28).Margin(0, sp * 2, ST, ST)
                .OnClick(hex, (h, _) => { ramp.Primary = h; ramp.OverrideAll = false; s.ApplyTheme(); s.Save(); })
                .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                {
                    float cx = (float)(r.Min.X + r.Size.X / 2), cy = (float)(r.Min.Y + r.Size.Y / 2);
                    const float sw = 20f;
                    canvas.RoundedRectFilled(cx - sw / 2, cy - sw / 2, sw, sw, 6f,
                        Prowl.Vector.Color32.FromArgb(255, col.R, col.G, col.B));
                    canvas.RoundedRect(cx - sw / 2, cy - sw / 2, sw, sw, 6f);
                    canvas.SetStrokeColor(Prowl.Vector.Color32.FromArgb(30, 255, 255, 255));
                    canvas.SetStrokeWidth(1f);
                    canvas.Stroke();
                    if (on)
                    {
                        var acc = EditorTheme.Accent;
                        const float ring = 27f;
                        canvas.RoundedRect(cx - ring / 2, cy - ring / 2, ring, ring, 8f);
                        canvas.SetStrokeColor(Prowl.Vector.Color32.FromArgb(255, acc.R, acc.G, acc.B));
                        canvas.SetStrokeWidth(2f);
                        canvas.Stroke();
                    }
                }));
        }

        void Palette() { foreach (var hex in palette) Swatch(hex); }

        Row(paper, id, label, () =>
        {
            if (inline)
                using (paper.Row($"{id}_r").Width(ST).Height(UnitValue.Auto).MinHeight(30).Enter())
                {
                    using (paper.Box($"{id}_cb").Width(130).Height(28).Margin(0, pad * 2, ST, ST).Enter())
                        ColorBox();
                    using (paper.Row($"{id}_pal").Width(ST).Height(28).Margin(0, 0, ST, ST).Enter())
                        Palette();
                }
            else
                using (paper.Column($"{id}_r").Width(ST).Height(UnitValue.Auto).Enter())
                {
                    using (paper.Box($"{id}_cb").Width(130).Height(28).Margin(0, 0, 0, sp * 2).Enter())
                        ColorBox();
                    using (paper.Row($"{id}_pal").Width(ST).Height(28).Enter())
                        Palette();
                }
        }, compact: true, minHeight: inline ? 36f : 68f);
    }

    /// <summary>Hex string ("#RRGGBB") to a <see cref="VColor"/> (alpha 1).</summary>
    public static VColor HexToVColor(string hex)
    {
        var c = ColorRamp.ParseHex(hex);
        return new VColor(c.R / 255f, c.G / 255f, c.B / 255f, 1f);
    }

    /// <summary>A <see cref="VColor"/> back to a "#RRGGBB" hex string (alpha dropped).</summary>
    public static string VColorToHex(VColor c)
    {
        int r = Math.Clamp((int)(c.R * 255), 0, 255);
        int g = Math.Clamp((int)(c.G * 255), 0, 255);
        int b = Math.Clamp((int)(c.B * 255), 0, 255);
        return $"#{r:X2}{g:X2}{b:X2}";
    }
}
