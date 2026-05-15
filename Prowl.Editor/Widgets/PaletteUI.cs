using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor.Widgets;

/// <summary>
/// Shared UI for drawing color and curve palettes.
/// Used by ColorPicker popup, CurveEditor popup, and Project Settings.
/// </summary>
public static class PaletteUI
{
   
    /// <summary>
    /// Draw a row of curve preset thumbnails.
    /// </summary>
    public static void DrawCurvePresets(Paper paper, string id, List<ProjectsEditorSettings.CurvePreset> presets,
        float availWidth, float presetW = 52f, float presetH = 32f, int maxRows = 2,
        Action<ProjectsEditorSettings.CurvePreset>? onSelect = null)
    {
        float gap = 3f;
        int cols = Math.Max(1, (int)((availWidth + gap) / (presetW + gap)));
        int rowCount = (presets.Count + cols - 1) / cols;
        rowCount = Math.Min(rowCount, maxRows);

        for (int row = 0; row < rowCount; row++)
        {
            using (paper.Row($"{id}_r{row}").Height(presetH).RowBetween(gap).Enter())
            {
                for (int col = 0; col < cols; col++)
                {
                    int itemIdx = row * cols + col;
                    if (itemIdx >= presets.Count) break;

                    int idx = itemIdx;
                    var preset = presets[idx];

                    var tempCurve = new AnimationCurve();
                    foreach (var kd in preset.Keys) tempCurve.Keys.Add(kd.ToKeyFrame());

                    using (paper.Box($"{id}_p{idx}")
                        .Size(presetW, presetH)
                        .BackgroundColor(EditorTheme.Neutral300)
                        .Rounded(3)
                        .BorderColor(EditorTheme.Ink200).BorderWidth(1)
                        .Hovered.BorderColor(EditorTheme.Purple400).End()
                        .Tooltip(preset.Name)
                        .OnClick(idx, (ci, _) => onSelect?.Invoke(presets[ci]))
                        .OnRightClick(idx, (ci, _) =>
                        {
                            presets.RemoveAt(ci);
                            ProjectSettingsRegistry.SaveAll();
                        })
                        .Enter())
                    {
                        paper.Box($"{id}_pv{idx}")
                            .Size(presetW, presetH)
                            .IsNotInteractable()
                            .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                                CurveEditor.DrawCurvePreviewStatic(canvas, r, tempCurve)));
                    }
                }
            }
        }
    }
}
