// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;

using Color = System.Drawing.Color;
using VColor = Prowl.Vector.Color;
using TextAlign = Prowl.Runtime.UI.TextAlignment;

namespace Prowl.Editor.GUI;

/// <summary>
/// Shared immediate-mode building blocks for the editor's own panels and inspectors
/// </summary>
public static class EditorGUI
{
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
        using (paper.Column(id).Height(UnitValue.Auto).Margin(0, 0, 0, m.Spacing).Enter())
        {
            DrawRowLine(paper, $"{id}_r", label, drawControl, m, labelWidth, compact, minHeight,
                vpad: m.PaddingSmall, bottomMargin: 0);
            paper.Box($"{id}_d").Height(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();
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
        using (paper.Row(id).Height(UnitValue.Auto).MinHeight(rh)
            .Padding(hpad, hpad, vpad, vpad).RowBetween(m.Padding)
            .Margin(0, 0, 0, bottomMargin).Enter())
        {
            if (!string.IsNullOrEmpty(label) && font != null)
                paper.Box($"{id}_l").Width(labelWidth ?? m.LabelWidth).Height(rh)
                    .Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).IsNotInteractable()
                    .Text(label, font).TextColor(labelColor).FontSize(labelSize)
                    .Alignment(TextAlignment.MiddleLeft).TextTruncate();

            // The control sizes to its widget and centers via stretch margins. (A forced MinHeight here
            // used to top-align the widget while the label stayed centered, so they didn't line up.)
            using (paper.Box($"{id}_c").Height(UnitValue.Auto).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
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
            using (paper.Row($"{id}_tw").Height(Origami.Current.Metrics.RowHeight)
                .Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
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
    //  Inspector row helpers
    // =====================================================================

    /// <summary>A label + float-slider inspector row.</summary>
    public static void SliderRow(Paper paper, string id, string label, float value, float min, float max,
        Action<float> setter, string format = "F2", bool bipolar = false)
        => Row(paper, id, label, () =>
        {
            var s = Origami.Slider(paper, $"{id}_v", value, setter, min, max).Format(format);
            if (bipolar) s.Bipolar();
            s.Show();
        });

    /// <summary>A label + int-slider inspector row.</summary>
    public static void IntSliderRow(Paper paper, string id, string label, int value, int min, int max,
        Action<int> setter)
        => Row(paper, id, label, () =>
            Origami.IntSlider(paper, $"{id}_v", value, setter, min, max).Show());

    /// <summary>A foldout section with an enable toggle — shared by particle modules and image-effect sections.</summary>
    public static void ModuleSection(Paper paper, string id, string icon, string label,
        bool enabled, Action<bool> setEnabled, Action draw)
        => Origami.Foldout(paper, id, $"{icon}  {label}")
            .Toggle(enabled, setEnabled)
            .Body(draw);

    /// <summary>A two-axis text-alignment selector row (horizontal + vertical ButtonGroups).</summary>
    public static void TextAlignmentRow(Paper paper, string id, string label, TextAlign value, Action<TextAlign> setter)
    {
        var font = EditorTheme.DefaultFont;
        using (paper.Row(id).Height(EditorTheme.RowHeight).RowBetween(6).Enter())
        {
            if (font != null)
                paper.Box($"{id}_lbl")
                    .Width(EditorTheme.LabelWidth).Height(EditorTheme.RowHeight)
                    .ChildLeft(4).IsNotInteractable()
                    .Text(label, font).TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize);

            Origami.ButtonGroup(paper, $"{id}_h", TextAlignHIndex(value),
                    idx => setter(TextAlignVFlag(TextAlignVIndex(value)) | TextAlignHFlag(idx)))
                .Height(EditorTheme.RowHeight).FullWidth()
                .Item("", EditorIcons.AlignLeft, "Left")
                .Item("", EditorIcons.AlignCenter, "Center")
                .Item("", EditorIcons.AlignRight, "Right")
                .Show();

            Origami.ButtonGroup(paper, $"{id}_v", TextAlignVIndex(value),
                    idx => setter(TextAlignVFlag(idx) | TextAlignHFlag(TextAlignHIndex(value))))
                .Height(EditorTheme.RowHeight).FullWidth()
                .Item("Top", tooltip: "Top")
                .Item("Mid", tooltip: "Middle")
                .Item("Bot", tooltip: "Bottom")
                .Show();
        }
    }

    private static int TextAlignHIndex(TextAlign a)
        => (a & TextAlign.Right) != 0 ? 2 : (a & TextAlign.Middle) != 0 ? 1 : 0;

    private static int TextAlignVIndex(TextAlign a)
        => (a & TextAlign.Bottom) != 0 ? 2 : (a & TextAlign.Center) != 0 ? 1 : 0;

    private static TextAlign TextAlignHFlag(int i)
        => i == 2 ? TextAlign.Right : i == 1 ? TextAlign.Middle : TextAlign.Left;

    private static TextAlign TextAlignVFlag(int i)
        => i == 2 ? TextAlign.Bottom : i == 1 ? TextAlign.Center : TextAlign.Top;

    // =====================================================================
    //  Settings-window chrome
    // =====================================================================

    /// <summary>Left category rail. Each entry is (id, label, icon glyph). Returns the sidebar width
    /// so the caller can size its content area. <paramref name="footer"/> is called at the bottom of the
    /// item list, inside the same centered group, for adding a note card or other trailing widget.</summary>
    public static float Sidebar(Paper paper, string id, (string id, string label, string icon)[] cats,
        string active, Action<string> onSelect, float width = 148f, float rowHeight = 30f, Action? footer = null)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return width;

        using (paper.Column(id).Width(width).BackgroundColor(Color.FromArgb(36, 0, 0, 0)).Enter())
        using (paper.Column($"{id}_grp").Height(UnitValue.Auto).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
            .Padding(8, 8, 10, 10).ColBetween(2).BackgroundColor(Color.FromArgb(36, 0, 0, 0)).Enter())
        {
            foreach (var (cid, label, icon) in cats)
            {
                bool on = cid == active;
                using (paper.Row($"{id}_{cid}").Height(rowHeight).Rounded(8).Padding(10, 10, 0, 0)
                    .BackgroundColor(on ? EditorTheme.Selected : Color.Transparent)
                    .Hovered.BackgroundColor(on ? EditorTheme.Selected : EditorTheme.Hover).End()
                    .OnClick(cid, (c, _) => onSelect(c))
                    .Enter())
                {
                    paper.Box($"{id}_{cid}_i").Width(16).Height(rowHeight).IsNotInteractable()
                        .Text(icon, font).TextColor(on ? EditorTheme.Ink500 : EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
                    paper.Box($"{id}_{cid}_l").Height(rowHeight).Margin(9, 0, 0, 0).IsNotInteractable()
                        .Text(label, font).TextColor(on ? EditorTheme.Ink500 : EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                }
            }
            footer?.Invoke();
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
        paper.Box(id).Height(h).Margin(leftPad, 0, topGap, botGap).IsNotInteractable()
            .Text(text.ToUpperInvariant(), font).TextColor(EditorTheme.AccentText)
            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
    }

    /// <summary>Full-width 1px separator. <paramref name="verticalMargin"/> adds equal top and bottom gap.</summary>
    public static void Divider(Paper paper, string id, float verticalMargin = 0f)
    {
        var b = paper.Box(id).Height(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();
        if (verticalMargin > 0f) b.Margin(0, 0, verticalMargin, verticalMargin);
    }

    /// <summary>
    /// A grouped section panel that visually nests its content: a soft rounded inset card with a clean
    /// title strip (icon optional) + divider and a padded body. Use to group related rows/controls.
    /// </summary>
    public static void Group(Paper paper, string id, string? title, Action body, string? icon = null)
    {
        var m = Origami.Current.Metrics;
        using (paper.Column(id).Height(UnitValue.Auto)
            .Margin(m.PaddingLarge, m.PaddingLarge, 0, m.SpacingLarge)
            .Rounded(10).Clip()
            .BackgroundColor(Color.FromArgb(38, 0, 0, 0)).BorderColor(EditorTheme.BorderSoft).BorderWidth(1).Enter())
        {
            var font = EditorTheme.FontSemiBold ?? EditorTheme.DefaultFont;
            if (!string.IsNullOrEmpty(title) && font != null)
            {
                using (paper.Row($"{id}_gh").Height(32).Padding(m.PaddingLarge, m.PaddingLarge, 0, 0)
                    .RowBetween(m.SpacingMedium).IsNotInteractable().Enter())
                {
                    if (!string.IsNullOrEmpty(icon))
                        paper.Box($"{id}_gi").Width(16).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).IsNotInteractable()
                            .Text(icon, EditorTheme.DefaultFont).TextColor(EditorTheme.AccentText)
                            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
                    paper.Box($"{id}_gt").Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).IsNotInteractable()
                        .Text(title, font).TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSizeSmall)
                        .Alignment(TextAlignment.MiddleLeft);
                }
                paper.Box($"{id}_gd").Height(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();
            }

            using (paper.Column($"{id}_gb").Height(UnitValue.Auto)
                .Padding(m.SpacingSmall, m.SpacingSmall, m.SpacingMedium, m.SpacingMedium).Enter())
                body();
        }
    }

    /// <summary>A glass pill button (label + click). <paramref name="leftGap"/> adds explicit spacing
    /// from a previous chip.</summary>
    public static void Chip(Paper paper, string id, string label, Action onClick, float leftGap = 0f)
    {
        var font = EditorTheme.DefaultFont;
        paper.Box(id).Width(UnitValue.Auto).Height(28).Margin(leftGap, 0, UnitValue.StretchOne, UnitValue.StretchOne).Rounded(8).Padding(11, 11, 0, 0)
            .BackgroundColor(EditorTheme.Neutral400).BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .Hovered.BorderColor(EditorTheme.BorderStrong).End()
            .Text(label, font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
            .Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) => onClick());
    }

    /// <summary>A colored call-to-action button. Pass <paramref name="grow"/> = true to stretch width.</summary>
    public static void CtaButton(Paper paper, string id, string label, Color bg, Action onClick, bool grow = false, float height = 28f)
    {
        var font = EditorTheme.FontSemiBold ?? EditorTheme.DefaultFont;
        paper.Box(id).Width(grow ? UnitValue.StretchOne : UnitValue.Auto).Height(height).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Rounded(8).Padding(16, 16, 0, 0)
            .BackgroundColor(bg)
            .Hovered.BackgroundColor(Color.FromArgb(230, bg.R, bg.G, bg.B)).End()
            .Text(label, font).TextColor(Color.White).FontSize(EditorTheme.FontSizeSmall)
            .Alignment(TextAlignment.MiddleCenter)
            .OnClick(0, (_, _) => onClick());
    }

    /// <summary>A small 24x24 icon button used in panel tab-bar headers.</summary>
    public static void HeaderIconButton(Paper paper, string id, string icon, Action onClick)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;
        paper.Box(id).Width(24).Height(24).Rounded(6).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .Text(icon, font).TextColor(EditorTheme.Ink300).FontSize(13f).Alignment(TextAlignment.MiddleCenter)
            .OnClick(_ => onClick());
    }

    /// <summary>A 26x26 icon button used in panel toolbars. Highlights with accent when active.</summary>
    public static void ToolbarIconBtn(Paper paper, string id, string glyph, bool active, Action onClick)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;
        paper.Box(id).Width(26).Height(26).Rounded(7).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
            .BackgroundColor(active ? EditorTheme.Selected : Color.Transparent)
            .Transition(GuiProp.BackgroundColor, 0.15f)
            .Hovered.BackgroundColor(active ? EditorTheme.Selected : EditorTheme.Hover).End()
            .Text(glyph, font).TextColor(active ? EditorTheme.Accent : EditorTheme.Ink300).FontSize(14f).Alignment(TextAlignment.MiddleCenter)
            .OnClick(_ => onClick());
    }

    /// <summary>A centered "nothing here" placeholder for empty lists and panels.</summary>
    public static void EmptyState(Paper paper, string id, string message, Scribe.FontFile font)
    {
        paper.Box(id).Height(60)
            .Text(message, font).TextColor(EditorTheme.Ink300)
            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
            .IsNotInteractable();
    }

    /// <summary>A read-only info chip for displaying asset statistics inline.</summary>
    public static void StatChip(Paper paper, string id, string text, Scribe.FontFile font)
    {
        paper.Box(id).Width(UnitValue.Auto).Height(22).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Rounded(6).Padding(9, 9, 0, 0)
            .BackgroundColor(EditorTheme.Glass).BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .IsNotInteractable()
            .Text(text, font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
            .Alignment(TextAlignment.MiddleCenter);
    }

    /// <summary>A purple-tinted banner shown when a drag payload can be dropped in the current area.</summary>
    public static void DropBanner(Paper paper, string id, string text)
    {
        var font = EditorTheme.DefaultFont;
        paper.Box(id).Height(24)
            .BackgroundColor(Color.FromArgb(40, EditorTheme.Purple400))
            .Rounded(3)
            .Text(text, font)
            .TextColor(EditorTheme.Purple400)
            .FontSize(EditorTheme.FontSizeSmall)
            .Alignment(TextAlignment.MiddleCenter);
    }

    // =====================================================================
    //  Drag-drop utilities
    // =====================================================================

    /// <summary>Returns true when a currently active drag payload is compatible with the given field type.</summary>
    public static bool IsCompatibleDragTarget(Type fieldType)
    {
        if (!DragDrop.IsDragging) return false;
        if (DragDrop.Payload is AssetDragPayload adp && adp.AssetType != null && fieldType.IsAssignableFrom(adp.AssetType))
            return true;
        if (DragDrop.Payload is GameObjectDragPayload &&
            (typeof(GameObject).IsAssignableFrom(fieldType) || typeof(MonoBehaviour).IsAssignableFrom(fieldType)))
            return true;
        if (DragDrop.Payload is ComponentDragPayload cdp && fieldType.IsAssignableFrom(cdp.Component.GetType()))
            return true;
        return false;
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
            paper.Box($"{id}_p_{hex}").Width(28).Height(28).Margin(0, sp * 2, UnitValue.StretchOne, UnitValue.StretchOne)
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
                using (paper.Row($"{id}_r").Height(UnitValue.Auto).MinHeight(30).Enter())
                {
                    using (paper.Box($"{id}_cb").Width(130).Height(28).Margin(0, pad * 2, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
                        ColorBox();
                    using (paper.Row($"{id}_pal").Height(28).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
                        Palette();
                }
            else
                using (paper.Column($"{id}_r").Height(UnitValue.Auto).Enter())
                {
                    using (paper.Box($"{id}_cb").Width(130).Height(28).Margin(0, 0, 0, sp * 2).Enter())
                        ColorBox();
                    using (paper.Row($"{id}_pal").Height(28).Enter())
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
