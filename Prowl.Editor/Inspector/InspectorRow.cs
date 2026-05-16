// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Editor.Inspector;

/// <summary>
/// Layout helper for the inspector's standard "label-left, control-right" row pattern.
/// Property editors wrap their Origami widget calls in <see cref="Draw"/> so every row in
/// the inspector aligns its label gutter consistently.
/// </summary>
internal static class InspectorRow
{
    /// <summary>
    /// Render a fixed-height row with a fixed-width label on the left and the caller's
    /// control filling the remainder. When <paramref name="label"/> is empty/null the label
    /// gutter is skipped and the control owns the full width.
    /// </summary>
    public static void Draw(Paper paper, string id, string label, Action drawControl, float? labelWidth = null)
    {
        var font = EditorTheme.DefaultFont;
        using (paper.Row(id).Height(EditorTheme.RowHeight).RowBetween(6).Margin(0, 0, 0, EditorTheme.Spacing).Enter())
        {
            if (!string.IsNullOrEmpty(label) && font != null)
            {
                paper.Box($"{id}_lbl")
                    .Width(labelWidth ?? EditorTheme.LabelWidth).Height(EditorTheme.RowHeight)
                    .ChildLeft(4)
                    .IsNotInteractable()
                    .Text(label, font).TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize);
            }

            using (paper.Box($"{id}_ctl")
                .Width(UnitValue.Stretch())
                .Height(EditorTheme.RowHeight)
                .Enter())
            {
                drawControl();
            }
        }
    }
}
