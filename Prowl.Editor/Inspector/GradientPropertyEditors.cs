using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Widgets;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Vector;

using Color = System.Drawing.Color;
using Gradient = Prowl.Runtime.Gradient;

namespace Prowl.Editor.Inspector;

// ================================================================
//  Gradient Property Editor — with visual preview and key editing
// ================================================================

[CustomPropertyEditor(typeof(Gradient))]
public class GradientPropertyEditor : PropertyEditor
{
    private const float PreviewHeight = 24f;
    private const float KeySize = 8f;

    public override void OnGUI(Paper paper, string id, string label, object? value, Action<object?> onChange, int depth)
    {
        var gradient = value as Gradient ?? new Gradient();
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        EditorGUI.Foldout(paper, $"{id}_fold", label, () =>
        {
            using (paper.Column($"{id}_body").Height(UnitValue.Auto).ChildLeft(4).ChildRight(4).Enter())
            {
                // Gradient preview bar with checkerboard background for alpha
                paper.Box($"{id}_preview")
                    .Width(UnitValue.Stretch()).Height(PreviewHeight)
                    .Rounded(3)
                    .BorderColor(EditorTheme.Ink200).BorderWidth(1)
                    .OnPostLayout((handle, rect) =>
                    {
                        paper.Draw(ref handle, (canvas, r) =>
                        {
                            float x = (float)r.Min.X;
                            float y = (float)r.Min.Y;
                            float w = (float)r.Size.X;
                            float h = (float)r.Size.Y;

                            // Checkerboard background (for alpha visibility)
                            float checkSize = 6f;
                            for (float cx = 0; cx < w; cx += checkSize)
                            {
                                for (float cy = 0; cy < h; cy += checkSize)
                                {
                                    int ix = (int)(cx / checkSize);
                                    int iy = (int)(cy / checkSize);
                                    bool light = (ix + iy) % 2 == 0;
                                    float cw = MathF.Min(checkSize, w - cx);
                                    float ch = MathF.Min(checkSize, h - cy);
                                    canvas.RectFilled(x + cx, y + cy, cw, ch,
                                        light ? Color.FromArgb(255, 180, 180, 180) : Color.FromArgb(255, 120, 120, 120));
                                }
                            }

                            // Draw gradient using narrow vertical strips
                            int steps = (int)w;
                            for (int i = 0; i < steps; i++)
                            {
                                float t = i / (float)steps;
                                var col = gradient.Evaluate(t);
                                canvas.RectFilled(x + i, y, 1.5f, h,
                                    Color.FromArgb((int)(col.A * 255), (int)(col.R * 255), (int)(col.G * 255), (int)(col.B * 255)));
                            }
                        });
                    });

                paper.Box($"{id}_sp1").Height(4);

                // Color keys
                EditorGUI.Header(paper, $"{id}_ck_h", "Color Keys");
                for (int i = 0; i < gradient.ColorKeys.Count; i++)
                {
                    int idx = i;
                    var key = gradient.ColorKeys[i];

                    using (paper.Row($"{id}_ck_{i}").Height(EditorTheme.RowHeight).RowBetween(4).Enter())
                    {
                        EditorGUI.FloatField(paper, $"{id}_ckt_{i}", key.Time, "Time")
                            .OnValueChanged(v =>
                            {
                                var k = gradient.ColorKeys[idx];
                                k.Time = MathF.Max(0, MathF.Min(1, v));
                                gradient.ColorKeys[idx] = k;
                                SortKeys(gradient);
                                onChange(gradient);
                            });

                        EditorGUI.ColorField(paper, $"{id}_ckc_{i}", "", key.Color)
                            .OnValueChanged(v =>
                            {
                                var k = gradient.ColorKeys[idx];
                                k.Color = v;
                                gradient.ColorKeys[idx] = k;
                                onChange(gradient);
                            });

                        paper.Box($"{id}_ckx_{i}")
                            .Width(18).Height(EditorTheme.RowHeight).Rounded(3)
                            .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                            .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                            .FontSize(9f).Alignment(TextAlignment.MiddleCenter)
                            .OnClick(idx, (ci, _) =>
                            {
                                if (gradient.ColorKeys.Count > 1)
                                {
                                    gradient.ColorKeys.RemoveAt(ci);
                                    onChange(gradient);
                                }
                            });
                    }
                }

                EditorGUI.Button(paper, $"{id}_ck_add", $"{EditorIcons.Plus}  Add Color Key", width: 140)
                    .OnValueChanged(_ =>
                    {
                        gradient.ColorKeys.Add(new GradientColorKey(new Prowl.Vector.Color(1, 1, 1, 1), 0.5f));
                        SortKeys(gradient);
                        onChange(gradient);
                    });

                paper.Box($"{id}_sp2").Height(4);

                // Alpha keys
                EditorGUI.Header(paper, $"{id}_ak_h", "Alpha Keys");
                for (int i = 0; i < gradient.AlphaKeys.Count; i++)
                {
                    int idx = i;
                    var key = gradient.AlphaKeys[i];

                    using (paper.Row($"{id}_ak_{i}").Height(EditorTheme.RowHeight).RowBetween(4).Enter())
                    {
                        EditorGUI.FloatField(paper, $"{id}_akt_{i}", key.Time, "Time")
                            .OnValueChanged(v =>
                            {
                                var k = gradient.AlphaKeys[idx];
                                k.Time = MathF.Max(0, MathF.Min(1, v));
                                gradient.AlphaKeys[idx] = k;
                                SortAlphaKeys(gradient);
                                onChange(gradient);
                            });

                        EditorGUI.Slider(paper, $"{id}_aka_{i}", "Alpha", key.Alpha, 0f, 1f)
                            .OnValueChanged(v =>
                            {
                                var k = gradient.AlphaKeys[idx];
                                k.Alpha = v;
                                gradient.AlphaKeys[idx] = k;
                                onChange(gradient);
                            });

                        paper.Box($"{id}_akx_{i}")
                            .Width(18).Height(EditorTheme.RowHeight).Rounded(3)
                            .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                            .Text(EditorIcons.Xmark, font).TextColor(EditorTheme.Ink400)
                            .FontSize(9f).Alignment(TextAlignment.MiddleCenter)
                            .OnClick(idx, (ci, _) =>
                            {
                                if (gradient.AlphaKeys.Count > 1)
                                {
                                    gradient.AlphaKeys.RemoveAt(ci);
                                    onChange(gradient);
                                }
                            });
                    }
                }

                EditorGUI.Button(paper, $"{id}_ak_add", $"{EditorIcons.Plus}  Add Alpha Key", width: 140)
                    .OnValueChanged(_ =>
                    {
                        gradient.AlphaKeys.Add(new GradientAlphaKey(1f, 0.5f));
                        SortAlphaKeys(gradient);
                        onChange(gradient);
                    });
            }
        });
    }

    private static void SortKeys(Gradient g)
    {
        g.ColorKeys.Sort((a, b) => a.Time.CompareTo(b.Time));
    }

    private static void SortAlphaKeys(Gradient g)
    {
        g.AlphaKeys.Sort((a, b) => a.Time.CompareTo(b.Time));
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
            EditorGUI.EnumDropdown(paper, $"{id}_mode", label, curve.Mode)
                .OnValueChanged(v => { curve.Mode = v; onChange(curve); });

            switch (curve.Mode)
            {
                case MinMaxCurveMode.Constant:
                    EditorGUI.FloatField(paper, $"{id}_val", curve.ConstantValue, "Value")
                        .OnValueChanged(v => { curve.ConstantValue = v; onChange(curve); });
                    break;

                case MinMaxCurveMode.Curve:
                    CurveEditor.CurveField(paper, $"{id}_curve", "Curve", curve.Curve)
                        .OnValueChanged(v => { curve.Curve = v; onChange(curve); });
                    break;

                case MinMaxCurveMode.Random:
                    EditorGUI.FloatField(paper, $"{id}_min", curve.MinValue, "Min")
                        .OnValueChanged(v => { curve.MinValue = v; onChange(curve); });
                    EditorGUI.FloatField(paper, $"{id}_max", curve.MaxValue, "Max")
                        .OnValueChanged(v => { curve.MaxValue = v; onChange(curve); });
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
            EditorGUI.EnumDropdown(paper, $"{id}_mode", label, gradient.Mode)
                .OnValueChanged(v => { gradient.Mode = v; onChange(gradient); });

            switch (gradient.Mode)
            {
                case MinMaxGradientMode.Color:
                    EditorGUI.ColorField(paper, $"{id}_color", "Color", gradient.ConstantColor)
                        .OnValueChanged(v => { gradient.ConstantColor = v; onChange(gradient); });
                    break;

                case MinMaxGradientMode.Gradient:
                    PropertyGrid.DrawField(paper, $"{id}_grad", "Gradient", typeof(Gradient), gradient.Gradient,
                        v => { gradient.Gradient = v as Gradient ?? new Gradient(); onChange(gradient); }, depth + 1);
                    break;

                case MinMaxGradientMode.RandomBetweenTwoColors:
                    EditorGUI.ColorField(paper, $"{id}_minc", "Min Color", gradient.MinColor)
                        .OnValueChanged(v => { gradient.MinColor = v; onChange(gradient); });
                    EditorGUI.ColorField(paper, $"{id}_maxc", "Max Color", gradient.MaxColor)
                        .OnValueChanged(v => { gradient.MaxColor = v; onChange(gradient); });
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
