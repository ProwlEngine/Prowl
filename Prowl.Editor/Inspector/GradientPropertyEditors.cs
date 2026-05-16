using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Widgets;
using Prowl.Editor.Widgets.PropertyEditors;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Vector;

using Color = System.Drawing.Color;
using Gradient = Prowl.Runtime.Gradient;

using PropertyGrid = Prowl.Editor.Widgets.PropertyGrid;
namespace Prowl.Editor.Inspector;

// ================================================================
//  Gradient Property Editor visual bar with keyframe markers
// ================================================================

[CustomPropertyEditor(typeof(Gradient))]
public class GradientPropertyEditor : PropertyEditor
{
    private const float BarHeight = 28f;
    private const float MarkerSize = 10f;
    private const float MarkerAreaHeight = 14f;
    private const float TotalHeight = MarkerAreaHeight + BarHeight + MarkerAreaHeight;

    // Selection state keyed by editor id
    private static string? _selectedEditorId;
    private static bool _selectedIsColor; // true = color key, false = alpha key
    private static int _selectedKeyIndex = -1;

    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        var gradient = value as Gradient ?? new Gradient();
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        Origami.Foldout(paper, $"{id}_fold", label).Body(() =>
        {
            DrawGradientBar(paper, id, gradient, font, onChange);
        });
    }

    private void DrawGradientBar(Paper paper, string id, Gradient gradient, Prowl.Scribe.FontFile font, Action<object?> onChange)
    {
        // Main gradient area: top markers + bar + bottom markers
        paper.Box($"{id}_area")
            .Width(UnitValue.Stretch()).Height(TotalHeight)
            .Rounded(3)
            .OnClick(id, (edId, e) =>
            {
                float relX = (float)e.RelativePosition.X;
                float relY = (float)e.RelativePosition.Y;
                float barWidth = (float)e.ElementRect.Size.X;

                float t = MathF.Max(0, MathF.Min(1, relX / barWidth));

                if (relY < MarkerAreaHeight)
                {
                    // Clicked in color marker area find or add
                    int hit = FindColorKeyAt(gradient, t, barWidth);
                    if (hit >= 0)
                    {
                        _selectedEditorId = edId;
                        _selectedIsColor = true;
                        _selectedKeyIndex = hit;
                    }
                    else
                    {
                        // Add new color key at this position
                        var newColor = gradient.Evaluate(t);
                        gradient.ColorKeys.Add(new GradientColorKey(new Prowl.Vector.Color(newColor.R, newColor.G, newColor.B, 1), t));
                        gradient.ColorKeys.Sort((a, b) => a.Time.CompareTo(b.Time));
                        _selectedEditorId = edId;
                        _selectedIsColor = true;
                        _selectedKeyIndex = gradient.ColorKeys.FindIndex(k => MathF.Abs(k.Time - t) < 0.001f);
                        onChange(gradient);
                    }
                }
                else if (relY > MarkerAreaHeight + BarHeight)
                {
                    // Clicked in alpha marker area find or add
                    int hit = FindAlphaKeyAt(gradient, t, barWidth);
                    if (hit >= 0)
                    {
                        _selectedEditorId = edId;
                        _selectedIsColor = false;
                        _selectedKeyIndex = hit;
                    }
                    else
                    {
                        // Add new alpha key
                        var evalColor = gradient.Evaluate(t);
                        gradient.AlphaKeys.Add(new GradientAlphaKey(evalColor.A, t));
                        gradient.AlphaKeys.Sort((a, b) => a.Time.CompareTo(b.Time));
                        _selectedEditorId = edId;
                        _selectedIsColor = false;
                        _selectedKeyIndex = gradient.AlphaKeys.FindIndex(k => MathF.Abs(k.Time - t) < 0.001f);
                        onChange(gradient);
                    }
                }
                else
                {
                    // Clicked on the bar itself deselect
                    _selectedEditorId = null;
                    _selectedKeyIndex = -1;
                }
            })
            .OnPostLayout((handle, rect) =>
            {
                paper.Draw(ref handle, (canvas, r) =>
                {
                    float x = (float)r.Min.X;
                    float y = (float)r.Min.Y;
                    float w = (float)r.Size.X;
                    float barY = y + MarkerAreaHeight;

                    // Checkerboard behind bar (for alpha)
                    float checkSize = 6f;
                    for (float cx = 0; cx < w; cx += checkSize)
                    {
                        for (float cy = 0; cy < BarHeight; cy += checkSize)
                        {
                            int ix = (int)(cx / checkSize);
                            int iy = (int)(cy / checkSize);
                            bool light = (ix + iy) % 2 == 0;
                            float cw = MathF.Min(checkSize, w - cx);
                            float ch = MathF.Min(checkSize, BarHeight - cy);
                            canvas.RectFilled(x + cx, barY + cy, cw, ch,
                                light ? Color.FromArgb(255, 180, 180, 180) : Color.FromArgb(255, 120, 120, 120));
                        }
                    }

                    // Gradient fill
                    int steps = Math.Max(1, (int)w);
                    for (int i = 0; i < steps; i++)
                    {
                        float t = i / (float)steps;
                        var col = gradient.Evaluate(t);
                        canvas.RectFilled(x + i, barY, 1.5f, BarHeight,
                            Color.FromArgb((int)(col.A * 255), (int)(col.R * 255), (int)(col.G * 255), (int)(col.B * 255)));
                    }

                    // Bar border
                    canvas.RectFilled(x, barY, w, 1, EditorTheme.Ink200);
                    canvas.RectFilled(x, barY + BarHeight - 1, w, 1, EditorTheme.Ink200);
                    canvas.RectFilled(x, barY, 1, BarHeight, EditorTheme.Ink200);
                    canvas.RectFilled(x + w - 1, barY, 1, BarHeight, EditorTheme.Ink200);

                    // Color key markers (top)
                    for (int i = 0; i < gradient.ColorKeys.Count; i++)
                    {
                        var key = gradient.ColorKeys[i];
                        float mx = x + key.Time * w;
                        float my = y + MarkerAreaHeight - 2;
                        bool selected = _selectedEditorId == id && _selectedIsColor && _selectedKeyIndex == i;

                        // Triangle pointing down
                        var keyColor = Color.FromArgb(255, (int)(key.Color.R * 255), (int)(key.Color.G * 255), (int)(key.Color.B * 255));
                        canvas.CircleFilled(mx, my - 3, selected ? 5f : 4f, selected ? EditorTheme.Purple400 : EditorTheme.Ink300);
                        canvas.CircleFilled(mx, my - 3, selected ? 3.5f : 2.5f, keyColor);
                    }

                    // Alpha key markers (bottom)
                    for (int i = 0; i < gradient.AlphaKeys.Count; i++)
                    {
                        var key = gradient.AlphaKeys[i];
                        float mx = x + key.Time * w;
                        float my = barY + BarHeight + 2;
                        bool selected = _selectedEditorId == id && !_selectedIsColor && _selectedKeyIndex == i;

                        int gray = (int)(key.Alpha * 255);
                        canvas.CircleFilled(mx, my + 3, selected ? 5f : 4f, selected ? EditorTheme.Purple400 : EditorTheme.Ink300);
                        canvas.CircleFilled(mx, my + 3, selected ? 3.5f : 2.5f, Color.FromArgb(255, gray, gray, gray));
                    }
                });
            });

        // Selected key properties
        if (_selectedEditorId == id && _selectedKeyIndex >= 0)
        {
            paper.Box($"{id}_sp").Height(4);

            if (_selectedIsColor && _selectedKeyIndex < gradient.ColorKeys.Count)
            {
                var key = gradient.ColorKeys[_selectedKeyIndex];
                int idx = _selectedKeyIndex;

                using (paper.Column($"{id}_sel").Height(UnitValue.Auto)
                    .BackgroundColor(Color.FromArgb(20, EditorTheme.Purple400))
                    .Rounded(3).Padding(4, 4, 4, 4)
                    .Enter())
                {
                    using (paper.Row($"{id}_sel_hdr").Height(EditorTheme.RowHeight).RowBetween(4).Enter())
                    {
                        paper.Box($"{id}_sel_lbl")
                            .Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight)
                            .Text($"Color Key {idx}", font).TextColor(EditorTheme.Purple400)
                            .FontSize(EditorTheme.FontSize - 1).Alignment(TextAlignment.MiddleLeft);

                        if (gradient.ColorKeys.Count > 1)
                        {
                            Origami.Button(paper, $"{id}_sel_del", $"{EditorIcons.Trash}", () =>
                            {
                                gradient.ColorKeys.RemoveAt(idx);
                                _selectedKeyIndex = -1;
                                _selectedEditorId = null;
                                onChange(gradient);
                            }).Width(24).Show();
                        }
                    }

                    InspectorRow.Draw(paper, $"{id}_sel_time", "Time", () =>
                        Origami.Slider(paper, $"{id}_sel_time_v", key.Time, v =>
                        {
                            var k = gradient.ColorKeys[idx];
                            k.Time = v;
                            gradient.ColorKeys[idx] = k;
                            gradient.ColorKeys.Sort((a, b) => a.Time.CompareTo(b.Time));
                            _selectedKeyIndex = gradient.ColorKeys.FindIndex(k2 => MathF.Abs(k2.Time - v) < 0.001f);
                            onChange(gradient);
                        }, 0f, 1f).Format("F3").Show());

                    InspectorRow.Draw(paper, $"{id}_sel_color", "Color", () =>
                        Origami.ColorField(paper, $"{id}_sel_color_cf", key.Color, v =>
                        {
                            var k = gradient.ColorKeys[idx];
                            k.Color = v;
                            gradient.ColorKeys[idx] = k;
                            onChange(gradient);
                        }).Show());
                }
            }
            else if (!_selectedIsColor && _selectedKeyIndex < gradient.AlphaKeys.Count)
            {
                var key = gradient.AlphaKeys[_selectedKeyIndex];
                int idx = _selectedKeyIndex;

                using (paper.Column($"{id}_sel").Height(UnitValue.Auto)
                    .BackgroundColor(Color.FromArgb(20, EditorTheme.Purple400))
                    .Rounded(3).Padding(4, 4, 4, 4)
                    .Enter())
                {
                    using (paper.Row($"{id}_sel_hdr").Height(EditorTheme.RowHeight).RowBetween(4).Enter())
                    {
                        paper.Box($"{id}_sel_lbl")
                            .Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight)
                            .Text($"Alpha Key {idx}", font).TextColor(EditorTheme.Purple400)
                            .FontSize(EditorTheme.FontSize - 1).Alignment(TextAlignment.MiddleLeft);

                        if (gradient.AlphaKeys.Count > 1)
                        {
                            Origami.Button(paper, $"{id}_sel_del", $"{EditorIcons.Trash}", () =>
                            {
                                gradient.AlphaKeys.RemoveAt(idx);
                                _selectedKeyIndex = -1;
                                _selectedEditorId = null;
                                onChange(gradient);
                            }).Width(24).Show();
                        }
                    }

                    InspectorRow.Draw(paper, $"{id}_sel_time", "Time", () =>
                        Origami.NumericField<float>(paper, $"{id}_sel_time_v", key.Time, v =>
                        {
                            var k = gradient.AlphaKeys[idx];
                            k.Time = v;
                            gradient.AlphaKeys[idx] = k;
                            gradient.AlphaKeys.Sort((a, b) => a.Time.CompareTo(b.Time));
                            _selectedKeyIndex = gradient.AlphaKeys.FindIndex(k2 => MathF.Abs(k2.Time - v) < 0.001f);
                            onChange(gradient);
                        }).Min(0f).Max(1f).Show());

                    InspectorRow.Draw(paper, $"{id}_sel_alpha", "Alpha", () =>
                        Origami.Slider(paper, $"{id}_sel_alpha_v", key.Alpha, v =>
                        {
                            var k = gradient.AlphaKeys[idx];
                            k.Alpha = v;
                            gradient.AlphaKeys[idx] = k;
                            onChange(gradient);
                        }, 0f, 1f).Format("F2").Show());
                }
            }
        }
    }

    private static int FindColorKeyAt(Gradient gradient, float t, float barWidth)
    {
        float threshold = MarkerSize / barWidth;
        for (int i = 0; i < gradient.ColorKeys.Count; i++)
            if (MathF.Abs(gradient.ColorKeys[i].Time - t) < threshold)
                return i;
        return -1;
    }

    private static int FindAlphaKeyAt(Gradient gradient, float t, float barWidth)
    {
        float threshold = MarkerSize / barWidth;
        for (int i = 0; i < gradient.AlphaKeys.Count; i++)
            if (MathF.Abs(gradient.AlphaKeys[i].Time - t) < threshold)
                return i;
        return -1;
    }
}

// ================================================================
//  MinMaxCurve Property Editor
// ================================================================

[CustomPropertyEditor(typeof(MinMaxCurve))]
public class MinMaxCurvePropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        var curve = value as MinMaxCurve ?? new MinMaxCurve();

        using (paper.Column(id).Height(UnitValue.Auto).Enter())
        {
            InspectorRow.Draw(paper, $"{id}_mode", label, () =>
                Origami.EnumDropdown(paper, $"{id}_mode_v", curve.Mode,
                    v => { curve.Mode = v; onChange(curve); }).Show());

            switch (curve.Mode)
            {
                case MinMaxCurveMode.Constant:
                    InspectorRow.Draw(paper, $"{id}_val", "Value", () =>
                        Origami.NumericField<float>(paper, $"{id}_val_v", curve.ConstantValue,
                            v => { curve.ConstantValue = v; onChange(curve); }).Show());
                    break;

                case MinMaxCurveMode.Curve:
                    InspectorRow.Draw(paper, $"{id}_curve", "Curve", () =>
                        CurveField.Create(paper, $"{id}_curve_cf", curve.Curve,
                            v => { curve.Curve = v; onChange(curve); }).Show());
                    break;

                case MinMaxCurveMode.Random:
                    InspectorRow.Draw(paper, $"{id}_min", "Min", () =>
                        Origami.NumericField<float>(paper, $"{id}_min_v", curve.MinValue,
                            v => { curve.MinValue = v; onChange(curve); }).Show());
                    InspectorRow.Draw(paper, $"{id}_max", "Max", () =>
                        Origami.NumericField<float>(paper, $"{id}_max_v", curve.MaxValue,
                            v => { curve.MaxValue = v; onChange(curve); }).Show());
                    break;
            }
        }
    }
}

// ================================================================
//  MinMaxGradient Property Editor
// ================================================================

[CustomPropertyEditor(typeof(MinMaxGradient))]
public class MinMaxGradientPropertyEditor : PropertyEditor
{
    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        var gradient = value as MinMaxGradient ?? new MinMaxGradient();

        using (paper.Column(id).Height(UnitValue.Auto).Enter())
        {
            InspectorRow.Draw(paper, $"{id}_mode", label, () =>
                Origami.EnumDropdown(paper, $"{id}_mode_v", gradient.Mode,
                    v => { gradient.Mode = v; onChange(gradient); }).Show());

            switch (gradient.Mode)
            {
                case MinMaxGradientMode.Color:
                    InspectorRow.Draw(paper, $"{id}_color", "Color", () =>
                        Origami.ColorField(paper, $"{id}_color_cf", gradient.ConstantColor, v => { gradient.ConstantColor = v; onChange(gradient); }).Show());
                    break;

                case MinMaxGradientMode.Gradient:
                    PropertyGrid.DrawField(paper, $"{id}_grad", "Gradient", typeof(Gradient), gradient.Gradient,
                        v => { gradient.Gradient = v as Gradient ?? new Gradient(); onChange(gradient); }, depth + 1);
                    break;

                case MinMaxGradientMode.RandomBetweenTwoColors:
                    InspectorRow.Draw(paper, $"{id}_minc", "Min Color", () =>
                        Origami.ColorField(paper, $"{id}_minc_cf", gradient.MinColor, v => { gradient.MinColor = v; onChange(gradient); }).Show());
                    InspectorRow.Draw(paper, $"{id}_maxc", "Max Color", () =>
                        Origami.ColorField(paper, $"{id}_maxc_cf", gradient.MaxColor, v => { gradient.MaxColor = v; onChange(gradient); }).Show());
                    break;

                case MinMaxGradientMode.RandomBetweenTwoGradients:
                    PropertyGrid.DrawField(paper, $"{id}_ming", "Min Gradient", typeof(Gradient), gradient.MinGradient,
                        v => { gradient.MinGradient = v as Gradient ?? new Gradient(); onChange(gradient); }, depth + 1);
                    PropertyGrid.DrawField(paper, $"{id}_maxg", "Max Gradient", typeof(Gradient), gradient.MaxGradient,
                        v => { gradient.MaxGradient = v as Gradient ?? new Gradient(); onChange(gradient); }, depth + 1);
                    break;
            }
        }
    }
}
