// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Runtime;
using Prowl.Vector;

using Color = System.Drawing.Color;
using VColor = Prowl.Vector.Color;
using Gradient = Prowl.Runtime.Gradient;
using Prowl.Editor.Theming;

namespace Prowl.Editor.GUI.Widgets;

/// <summary>
/// Fluent builder for a Gradient editor field.
/// Renders an inline gradient preview bar that opens a full gradient editor modal on click.
/// </summary>
public sealed class GradientFieldBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly Gradient _value;
    private readonly Action<Gradient> _setter;

    private UnitValue _width = UnitValue.Stretch();
    private float _previewHeight = 28f;
    private bool _readOnly;

    internal GradientFieldBuilder(Paper paper, string id, Gradient value, Action<Gradient> setter)
    {
        _paper = paper;
        _id = id;
        _value = value;
        _setter = setter;
    }

    public GradientFieldBuilder Width(UnitValue width) { _width = width; return this; }
    public GradientFieldBuilder PreviewHeight(float height) { _previewHeight = MathF.Max(16, height); return this; }
    public GradientFieldBuilder ReadOnly(bool readOnly = true) { _readOnly = readOnly; return this; }

    public void Show()
    {
        if (Origami.IsReadOnly) _readOnly = true;
        var theme = Origami.Current;
        var m = theme.Metrics;

        var swatch = _paper.Box($"{_id}_swatch")
            .Width(_width).Height(_previewHeight)
            .BorderColor(theme.Neutral.C400).BorderWidth(1)
            .Hovered.BorderColor(theme.Primary.C400).End()
            .Rounded(m.Rounding);

        if (!_readOnly)
        {
            var gradient = _value;
            var setter = _setter;
            var id = _id;
            swatch.OnClick(e =>
            {
                float anchorX = (float)e.ElementRect.Min.X;
                float anchorY = (float)e.ElementRect.Max.Y + 2;
                Modal.Push(new GradientEditorModal(id, gradient, setter, anchorX, anchorY));
            });
        }

        using (swatch.Enter())
        {
            var gradient = _value;
            _paper.Box($"{_id}_preview")
                .Width(UnitValue.Stretch()).Height(_previewHeight)
                .IsNotInteractable()
                .OnPostLayout((handle, rect) => _paper.Draw(ref handle, (canvas, r) =>
                    GradientRenderer.DrawPreview(canvas, r, gradient, theme)));
        }
    }
}

/// <summary>Static entry point for GradientField builder.</summary>
public static class GradientField
{
    public static GradientFieldBuilder Create(Paper paper, string id, Gradient value, Action<Gradient> setter)
        => new GradientFieldBuilder(paper, id, value, setter);
}

// ════════════════════════════════════════════════════════════════
//  Gradient Renderer
// ════════════════════════════════════════════════════════════════

public static class GradientRenderer
{
    public static void DrawPreview(Canvas canvas, Rect r, Gradient gradient, OrigamiTheme theme)
    {
        float x = (float)r.Min.X, y = (float)r.Min.Y;
        float w = (float)r.Size.X, h = (float)r.Size.Y;
        float round = theme.Metrics.Rounding;

        // Checkerboard behind (for alpha visibility)
        float checkSize = 5f;
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
                    light ? Color32.FromArgb(255, 180, 180, 180) : Color32.FromArgb(255, 120, 120, 120));
            }
        }

        // Gradient fill (per-pixel column)
        int steps = Math.Max(1, (int)w);
        for (int i = 0; i < steps; i++)
        {
            float t = i / (float)steps;
            var col = gradient.Evaluate(t);
            canvas.RectFilled(x + i, y, 1.5f, h,
                Color32.FromArgb((byte)(col.A * 255), (byte)(col.R * 255), (byte)(col.G * 255), (byte)(col.B * 255)));
        }
    }
}

// ════════════════════════════════════════════════════════════════
//  Gradient Editor Modal
// ════════════════════════════════════════════════════════════════

internal sealed class GradientEditorModal : IModal
{
    private readonly string _id;
    private readonly Gradient _gradient;
    private readonly Action<Gradient> _setter;
    private readonly float _anchorX;
    private readonly float _anchorY;

    private bool _selectedIsColor = true;
    private int _selectedKey = -1;

    public bool CloseOnBackdrop => true;
    public bool CloseOnEscape => true;

    private const float EditorW = 380f;
    private const float BarHeight = 32f;
    private const float MarkerH = 14f;

    public GradientEditorModal(string id, Gradient gradient, Action<Gradient> setter, float anchorX, float anchorY)
    {
        _id = id;
        _gradient = gradient;
        _setter = setter;
        _anchorX = anchorX;
        _anchorY = anchorY;
    }

    public void Draw(Paper paper, int layer, int stackIndex)
    {
        var theme = Origami.Current;
        var m = theme.Metrics;
        var font = theme.Font;
        var ink = theme.Ink;

        using (paper.Column($"{_id}_gmod")
            .PositionType(PositionType.SelfDirected)
            .Position(_anchorX, _anchorY)
            .Width(EditorW).Height(UnitValue.Auto)
            .BackgroundColor(theme.Neutral.C300)
            .BorderColor(ink.C200).BorderWidth(1)
            .Rounded(m.ContainerRounding)
            .BoxShadow(0, 4, 24, 0, Color.FromArgb(100, 0, 0, 0))
            .Padding(m.PaddingLarge, m.PaddingLarge, m.PaddingLarge, m.PaddingLarge)
            .ColBetween(m.SpacingMedium)
            .Layer(layer)
            .ClampToScreen()
            .StopEventPropagation()
            .Enter())
        {
            float barW = EditorW - m.PaddingLarge * 2;

            // Color markers (top) + Gradient bar + Alpha markers (bottom)
            DrawGradientArea(paper, font, ink, theme, barW);

            // Selected key editor
            DrawSelectedKeyEditor(paper, font, theme);
        }
    }

    private void DrawGradientArea(Paper paper, Scribe.FontFile? font, OrigamiRamp ink, OrigamiTheme theme, float barW)
    {
        float totalH = MarkerH + BarHeight + MarkerH;
        var gradient = _gradient;
        var id = _id;
        int selKey = _selectedKey;
        bool selIsColor = _selectedIsColor;

        paper.Box($"{id}_gbar")
            .Width(UnitValue.Stretch()).Height(totalH)
            .OnClick(e =>
            {
                float relX = (float)e.RelativePosition.X;
                float relY = (float)e.RelativePosition.Y;
                float w = (float)e.ElementRect.Size.X;
                float t = MathF.Max(0, MathF.Min(1, relX / w));
                float threshold = 10f / w;

                if (relY < MarkerH)
                {
                    // Color marker area - select or add
                    int hit = FindKey(gradient.ColorKeys, k => k.Time, t, threshold);
                    if (hit >= 0)
                    {
                        _selectedIsColor = true;
                        _selectedKey = hit;
                    }
                    else
                    {
                        var col = gradient.Evaluate(t);
                        gradient.ColorKeys.Add(new GradientColorKey(new VColor(col.R, col.G, col.B, 1), t));
                        gradient.ColorKeys.Sort((a, b) => a.Time.CompareTo(b.Time));
                        _selectedIsColor = true;
                        _selectedKey = gradient.ColorKeys.FindIndex(k => MathF.Abs(k.Time - t) < 0.001f);
                        _setter(gradient);
                    }
                }
                else if (relY > MarkerH + BarHeight)
                {
                    // Alpha marker area - select or add
                    int hit = FindKey(gradient.AlphaKeys, k => k.Time, t, threshold);
                    if (hit >= 0)
                    {
                        _selectedIsColor = false;
                        _selectedKey = hit;
                    }
                    else
                    {
                        var col = gradient.Evaluate(t);
                        gradient.AlphaKeys.Add(new GradientAlphaKey(col.A, t));
                        gradient.AlphaKeys.Sort((a, b) => a.Time.CompareTo(b.Time));
                        _selectedIsColor = false;
                        _selectedKey = gradient.AlphaKeys.FindIndex(k => MathF.Abs(k.Time - t) < 0.001f);
                        _setter(gradient);
                    }
                }
                else
                {
                    _selectedKey = -1;
                }
            })
            .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
            {
                float x = (float)r.Min.X, y = (float)r.Min.Y;
                float w = (float)r.Size.X;
                float barY = y + MarkerH;

                // Checkerboard
                float checkSize = 5f;
                for (float cx = 0; cx < w; cx += checkSize)
                    for (float cy = 0; cy < BarHeight; cy += checkSize)
                    {
                        bool light = ((int)(cx / checkSize) + (int)(cy / checkSize)) % 2 == 0;
                        canvas.RectFilled(x + cx, barY + cy,
                            MathF.Min(checkSize, w - cx), MathF.Min(checkSize, BarHeight - cy),
                            light ? Color32.FromArgb(255, 180, 180, 180) : Color32.FromArgb(255, 120, 120, 120));
                    }

                // Gradient fill
                int steps = Math.Max(1, (int)w);
                for (int i = 0; i < steps; i++)
                {
                    float t = i / (float)steps;
                    var col = gradient.Evaluate(t);
                    canvas.RectFilled(x + i, barY, 1.5f, BarHeight,
                        Color32.FromArgb((byte)(col.A * 255), (byte)(col.R * 255), (byte)(col.G * 255), (byte)(col.B * 255)));
                }

                // Border
                var borderCol = Color32.FromArgb(255, (byte)ink.C300.R, (byte)ink.C300.G, (byte)ink.C300.B);
                canvas.SetStrokeColor(borderCol);
                canvas.SetStrokeWidth(1);
                canvas.BeginPath();
                canvas.RoundedRect(x, barY, w, BarHeight, 2, 2, 2, 2);
                canvas.Stroke();

                // Color markers (top)
                var primary = theme.Primary;
                for (int i = 0; i < gradient.ColorKeys.Count; i++)
                {
                    var key = gradient.ColorKeys[i];
                    float mx = x + key.Time * w;
                    float my = y + MarkerH - 2;
                    bool sel = selIsColor && selKey == i;

                    var kc = Color32.FromArgb(255, (byte)(key.Color.R * 255), (byte)(key.Color.G * 255), (byte)(key.Color.B * 255));
                    var ring = sel ? Color32.FromArgb(255, (byte)primary.C400.R, (byte)primary.C400.G, (byte)primary.C400.B) : borderCol;
                    canvas.CircleFilled(mx, my - 2, sel ? 5.5f : 4.5f, ring);
                    canvas.CircleFilled(mx, my - 2, sel ? 3.5f : 2.5f, kc);
                }

                // Alpha markers (bottom)
                for (int i = 0; i < gradient.AlphaKeys.Count; i++)
                {
                    var key = gradient.AlphaKeys[i];
                    float mx = x + key.Time * w;
                    float my = barY + BarHeight + 2;
                    bool sel = !selIsColor && selKey == i;

                    byte g = (byte)(key.Alpha * 255);
                    var kc = Color32.FromArgb(255, g, g, g);
                    var ring = sel ? Color32.FromArgb(255, (byte)primary.C400.R, (byte)primary.C400.G, (byte)primary.C400.B) : borderCol;
                    canvas.CircleFilled(mx, my + 2, sel ? 5.5f : 4.5f, ring);
                    canvas.CircleFilled(mx, my + 2, sel ? 3.5f : 2.5f, kc);
                }
            }));
    }

    private void DrawSelectedKeyEditor(Paper paper, Scribe.FontFile? font, OrigamiTheme theme)
    {
        if (_selectedKey < 0) return;
        var m = theme.Metrics;
        var gradient = _gradient;

        if (_selectedIsColor && _selectedKey < gradient.ColorKeys.Count)
        {
            var key = gradient.ColorKeys[_selectedKey];
            int idx = _selectedKey;

            // Time slider
            Origami.Slider(paper, $"{_id}_gt", key.Time, v =>
            {
                var k = gradient.ColorKeys[idx];
                k.Time = v;
                gradient.ColorKeys[idx] = k;
                gradient.ColorKeys.Sort((a, b) => a.Time.CompareTo(b.Time));
                _selectedKey = gradient.ColorKeys.FindIndex(k2 => MathF.Abs(k2.Time - v) < 0.001f);
                _setter(gradient);
            }, 0f, 1f).Format("F3").Show();

            // Color field
            Origami.ColorField(paper, $"{_id}_gc", key.Color, v =>
            {
                var k = gradient.ColorKeys[idx];
                k.Color = v;
                gradient.ColorKeys[idx] = k;
                _setter(gradient);
            }).Show();

            // Delete button
            if (gradient.ColorKeys.Count > 1)
            {
                Origami.Button(paper, $"{_id}_gdel", $"{EditorIcons.Trash} Remove", () =>
                {
                    gradient.ColorKeys.RemoveAt(idx);
                    _selectedKey = -1;
                    _setter(gradient);
                }).Danger().Show();
            }
        }
        else if (!_selectedIsColor && _selectedKey < gradient.AlphaKeys.Count)
        {
            var key = gradient.AlphaKeys[_selectedKey];
            int idx = _selectedKey;

            // Time slider
            Origami.Slider(paper, $"{_id}_at", key.Time, v =>
            {
                var k = gradient.AlphaKeys[idx];
                k.Time = v;
                gradient.AlphaKeys[idx] = k;
                gradient.AlphaKeys.Sort((a, b) => a.Time.CompareTo(b.Time));
                _selectedKey = gradient.AlphaKeys.FindIndex(k2 => MathF.Abs(k2.Time - v) < 0.001f);
                _setter(gradient);
            }, 0f, 1f).Format("F3").Show();

            // Alpha slider
            Origami.Slider(paper, $"{_id}_aa", key.Alpha, v =>
            {
                var k = gradient.AlphaKeys[idx];
                k.Alpha = v;
                gradient.AlphaKeys[idx] = k;
                _setter(gradient);
            }, 0f, 1f).Format("F2").Show();

            // Delete button
            if (gradient.AlphaKeys.Count > 1)
            {
                Origami.Button(paper, $"{_id}_adel", $"{EditorIcons.Trash} Remove", () =>
                {
                    gradient.AlphaKeys.RemoveAt(idx);
                    _selectedKey = -1;
                    _setter(gradient);
                }).Danger().Show();
            }
        }
    }

    private static int FindKey<T>(List<T> keys, Func<T, float> getTime, float t, float threshold)
    {
        for (int i = 0; i < keys.Count; i++)
            if (MathF.Abs(getTime(keys[i]) - t) < threshold)
                return i;
        return -1;
    }
}
