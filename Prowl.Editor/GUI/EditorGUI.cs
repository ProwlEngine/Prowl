// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

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
        bool separator = false, float? labelWidth = null)
    {
        var m = Origami.Current.Metrics;

        if (!separator)
        {
            // Inspector rhythm: the between-row gap is a Spacing-driven bottom margin (not vertical
            // padding) so hand-drawn rows line up with the reflection-drawn PropertyGrid.
            DrawRowLine(paper, id, label, drawControl, m, labelWidth,
                vpad: 0, bottomMargin: m.SpacingLarge);
            return;
        }

        // A Spacing-driven gap after the divider, so between-setting spacing tracks the theme's Spacing.
        using (paper.Column(id).Width(ST).Height(UnitValue.Auto).Margin(0, 0, 0, m.Spacing).Enter())
        {
            DrawRowLine(paper, $"{id}_r", label, drawControl, m, labelWidth,
                vpad: m.PaddingSmall, bottomMargin: 0);
            paper.Box($"{id}_d").Width(ST).Height(1).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();
        }
    }

    private static void DrawRowLine(Paper paper, string id, string label, Action drawControl,
        OrigamiMetrics m, float? labelWidth, float vpad, float bottomMargin)
    {
        var font = EditorTheme.DefaultFont;
        float rh = m.RowHeight;
        using (paper.Row(id).Width(ST).Height(UnitValue.Auto).MinHeight(rh)
            .Padding(m.PaddingLarge, m.PaddingLarge, vpad, vpad).RowBetween(m.Padding)
            .Margin(0, 0, 0, bottomMargin).Enter())
        {
            if (!string.IsNullOrEmpty(label) && font != null)
                paper.Box($"{id}_l").Width(labelWidth ?? m.LabelWidth).Height(rh)
                    .Margin(0, 0, ST, ST).IsNotInteractable()
                    .Text(label, font).TextColor(Origami.Current.Ink.C300).FontSize(m.FontSize)
                    .Alignment(TextAlignment.MiddleLeft).TextTruncate();

            using (paper.Box($"{id}_c").Width(ST).Height(UnitValue.Auto).MinHeight(rh).Enter())
                drawControl();
        }
    }

    /// <summary>A settings-window row (label + control with a bottom divider by default).</summary>
    public static void SettingsRow(Paper paper, string id, string label, Action drawControl,
        bool separator = true, float? labelWidth = null)
        => Row(paper, id, label, drawControl, separator, labelWidth);

    /// <summary>A label + pill-toggle settings row (respects the label gutter).</summary>
    public static void SettingsToggle(Paper paper, string id, string label, bool value,
        Action<bool> setter, bool separator = true)
        => SettingsRow(paper, id, label, () =>
        {
            using (paper.Row($"{id}_tw").Width(ST).Height(Origami.Current.Metrics.RowHeight)
                .Margin(0, 0, ST, ST).Enter())
                Origami.Switch(paper, $"{id}_sw", value, setter).NoLabel().Show();
        }, separator);

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

    /// <summary>Uppercase accent section header, left-aligned to the row labels.</summary>
    public static void SectionHeader(Paper paper, string id, string text, bool first = false)
    {
        var font = EditorTheme.FontSemiBold ?? EditorTheme.DefaultFont;
        if (font == null) return;
        var m = Origami.Current.Metrics;
        paper.Box(id).Width(ST).Height(22).Margin(m.PaddingLarge, 0, first ? 2 : 14, 4).IsNotInteractable()
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
}
