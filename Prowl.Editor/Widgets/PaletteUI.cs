using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Runtime;
using Prowl.Vector;

namespace Prowl.Editor.Widgets;

/// <summary>
/// Shared UI for drawing color and curve palettes.
/// Used by ColorPicker popup, CurveEditor popup, and Project Settings.
/// </summary>
public static class PaletteUI
{
    /// <summary>
    /// Draw a grid of color swatches from a palette list.
    /// </summary>
    /// <param name="paper">The Paper instance</param>
    /// <param name="id">Unique element ID prefix</param>
    /// <param name="palette">The color palette (list of hex strings)</param>
    /// <param name="availWidth">Available width for the grid</param>
    /// <param name="swatchSize">Size of each swatch</param>
    /// <param name="maxRows">Maximum visible rows (0 = unlimited)</param>
    /// <param name="onSelect">Called when a swatch is clicked (receives the color)</param>
    /// <param name="onAdd">Called when the + button is clicked (add current color). Null to hide the button.</param>
    public static void DrawColorPalette(Paper paper, string id, List<string> palette,
        float availWidth, float swatchSize = 16f, int maxRows = 0,
        Action<Prowl.Vector.Color>? onSelect = null, Func<string>? onAdd = null)
    {
        float gap = 2f;
        int cols = Math.Max(1, (int)((availWidth + gap) / (swatchSize + gap)));
        int totalItems = palette.Count + (onAdd != null ? 1 : 0);
        int rowCount = (totalItems + cols - 1) / cols;
        if (maxRows > 0) rowCount = Math.Min(rowCount, maxRows);

        for (int row = 0; row < rowCount; row++)
        {
            using (paper.Row($"{id}_r{row}").Height(swatchSize).RowBetween(gap).Enter())
            {
                for (int col = 0; col < cols; col++)
                {
                    int itemIdx = row * cols + col;
                    if (itemIdx >= totalItems) break;

                    if (itemIdx < palette.Count)
                    {
                        int idx = itemIdx;
                        var sc = ColorRamp.ParseHex(palette[idx]);
                        paper.Box($"{id}_s{idx}")
                            .Size(swatchSize, swatchSize)
                            .BackgroundColor(sc)
                            .Rounded(2)
                            .BorderColor(EditorTheme.Ink200).BorderWidth(1)
                            .Hovered.BorderColor(EditorTheme.Purple400).End()
                            .OnClick(idx, (ci, _) =>
                            {
                                var c = ColorRamp.ParseHex(palette[ci]);
                                onSelect?.Invoke(new Prowl.Vector.Color(c.R / 255f, c.G / 255f, c.B / 255f, 1f));
                            })
                            .OnRightClick(idx, (ci, _) =>
                            {
                                palette.RemoveAt(ci);
                                ProjectSettingsRegistry.SaveAll();
                            });
                    }
                    else
                    {
                        // Add button
                        paper.Box($"{id}_add")
                            .Size(swatchSize, swatchSize)
                            .BackgroundColor(EditorTheme.Ink100)
                            .Rounded(2)
                            .BorderColor(EditorTheme.Ink200).BorderWidth(1)
                            .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                            .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                            {
                                float cx = (float)r.Min.X + (float)r.Size.X / 2f;
                                float cy = (float)r.Min.Y + (float)r.Size.Y / 2f;
                                canvas.SetStrokeColor(Color32.FromArgb(180, 200, 200, 200));
                                canvas.SetStrokeWidth(1.5f);
                                canvas.BeginPath(); canvas.MoveTo(cx - 4, cy); canvas.LineTo(cx + 4, cy); canvas.Stroke();
                                canvas.BeginPath(); canvas.MoveTo(cx, cy - 4); canvas.LineTo(cx, cy + 4); canvas.Stroke();
                            }))
                            .OnClick(e =>
                            {
                                if (onAdd != null)
                                {
                                    string hex = onAdd();
                                    if (!string.IsNullOrEmpty(hex) && !palette.Contains(hex))
                                    {
                                        palette.Add(hex);
                                        ProjectSettingsRegistry.SaveAll();
                                    }
                                }
                            });
                    }
                }
            }
        }
    }

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
