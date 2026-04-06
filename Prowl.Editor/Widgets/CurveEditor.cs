using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Runtime;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Widgets;

public static class CurveEditor
{
    private const int PreviewSteps = 40;
    private const float RulerSize = 24f;
    private const float TangentHandleLength = 40f;

    // ================================================================
    //  Inline Field
    // ================================================================

    public static WidgetResult<AnimationCurve> CurveField(Paper paper, string id, string label, AnimationCurve curve)
    {
        Action<AnimationCurve>? userCallback = null;

        using (paper.Row(id).Height(40).RowBetween(6).Enter())
        {
            if (EditorTheme.DefaultFont != null && !string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl")
                    .Width(EditorTheme.LabelWidth).Height(40).ChildLeft(4)
                    .IsNotInteractable()
                    .Text(label, EditorTheme.DefaultFont)
                    .TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize);

            using (paper.Box($"{id}_preview")
                .Height(40).Width(UnitValue.Stretch())
                .BackgroundColor(EditorTheme.Neutral300)
                .Rounded(3).BorderColor(EditorTheme.Ink200).BorderWidth(1)
                .Hovered.BorderColor(EditorTheme.Purple400).End()
                .Enter())
            {
                bool isOpen = paper.IsParentFocusWithin;

                paper.Box($"{id}_prev_line")
                    .Width(UnitValue.Stretch()).Height(40).IsNotInteractable()
                    .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                        DrawCurvePreview(canvas, r, curve)));

                if (isOpen)
                {
                    using (paper.Box($"{id}_ed_wrap")
                        .PositionType(PositionType.SelfDirected)
                        .Position(0, 44).Width(UnitValue.Auto).Height(UnitValue.Auto)
                        .Enter())
                    {
                        DrawFullEditor(paper, $"{id}_ed", curve, c => userCallback?.Invoke(c));
                    }
                }
            }
        }

        return new WidgetResult<AnimationCurve>(cb => userCallback = cb);
    }

    // ================================================================
    //  Preview
    // ================================================================

    static void DrawCurvePreview(Canvas canvas, Rect r, AnimationCurve curve)
    {
        if (curve.Keys.Count == 0) return;
        float x = (float)r.Min.X + 2, y = (float)r.Min.Y + 2;
        float w = (float)r.Size.X - 4, h = (float)r.Size.Y - 4;
        GetCurveBounds(curve, out float minT, out float maxT, out float minV, out float maxV);

        canvas.SetStrokeColor(Color32.FromArgb(255, 51, 180, 100));
        canvas.SetStrokeWidth(1.5f);
        canvas.BeginPath();
        for (int i = 0; i <= PreviewSteps; i++)
        {
            float t = minT + (maxT - minT) * i / PreviewSteps;
            float v = curve.Evaluate(t);
            float px = x + (t - minT) / (maxT - minT) * w;
            float py = y + h - (v - minV) / (maxV - minV) * h;
            if (i == 0) canvas.MoveTo(px, py); else canvas.LineTo(px, py);
        }
        canvas.Stroke();
    }

    // ================================================================
    //  Full Editor
    // ================================================================

    static void DrawFullEditor(Paper paper, string id, AnimationCurve curve, Action<AnimationCurve> onChange)
    {
        float editorW = 420;
        float graphX = RulerSize, graphY = RulerSize;
        float graphW = editorW - RulerSize - 8, graphH = 220;
        float barY = graphY + graphH + 4;
        float paletteY = barY + 28;
        float editorH = paletteY + 76; // 2 rows of presets + padding

        using (paper.Column(id)
            .Size(editorW, editorH)
            .BackgroundColor(EditorTheme.Neutral300)
            .BorderColor(EditorTheme.Ink200).BorderWidth(1).Rounded(6)
            .Layer(Layer.Topmost)
            .ClampToScreen()
            .Enter())
        {
            var el = paper.CurrentParent;

            // Initialize view
            bool initialized = paper.GetElementStorage(el, "init", false);
            if (!initialized)
            {
                GetCurveBounds(curve, out float fMinT, out float fMaxT, out float fMinV, out float fMaxV);
                paper.SetElementStorage(el, "vMinT", fMinT);
                paper.SetElementStorage(el, "vMaxT", fMaxT);
                paper.SetElementStorage(el, "vMinV", fMinV);
                paper.SetElementStorage(el, "vMaxV", fMaxV);
                paper.SetElementStorage(el, "sel", -1);
                paper.SetElementStorage(el, "init", true);
            }

            float vMinT = paper.GetElementStorage(el, "vMinT", 0f);
            float vMaxT = paper.GetElementStorage(el, "vMaxT", 1f);
            float vMinV = paper.GetElementStorage(el, "vMinV", 0f);
            float vMaxV = paper.GetElementStorage(el, "vMaxV", 1f);
            float rngT = vMaxT - vMinT; if (rngT < 0.001f) rngT = 1;
            float rngV = vMaxV - vMinV; if (rngV < 0.001f) rngV = 1;
            int selectedIdx = paper.GetElementStorage(el, "sel", -1);

            float tToX = graphW / rngT;
            float vToY = graphH / rngV;

            // Graph area
            paper.Box($"{id}_graph")
                .PositionType(PositionType.SelfDirected)
                .Position(graphX, graphY).Size(graphW, graphH)
                .BackgroundColor(Color.FromArgb(255, 28, 28, 30)).Rounded(2)
                .Clip()
                .StopEventPropagation()
                .OnScroll(e =>
                {
                    float zf = (float)e.Delta > 0 ? 0.9f : 1.1f;
                    float nX = (float)e.NormalizedPosition.X;
                    float nY = 1f - (float)e.NormalizedPosition.Y;
                    float cT = vMinT + nX * rngT, cV = vMinV + nY * rngV;
                    float nrT = rngT * zf, nrV = rngV * zf;
                    paper.SetElementStorage(el, "vMinT", cT - nX * nrT);
                    paper.SetElementStorage(el, "vMaxT", cT + (1 - nX) * nrT);
                    paper.SetElementStorage(el, "vMinV", cV - nY * nrV);
                    paper.SetElementStorage(el, "vMaxV", cV + (1 - nY) * nrV);
                })
                .OnDragging(e =>
                {
                    float dx = -(float)e.Delta.X / graphW * rngT;
                    float dy = (float)e.Delta.Y / graphH * rngV;
                    paper.SetElementStorage(el, "vMinT", vMinT + dx);
                    paper.SetElementStorage(el, "vMaxT", vMaxT + dx);
                    paper.SetElementStorage(el, "vMinV", vMinV + dy);
                    paper.SetElementStorage(el, "vMaxV", vMaxV + dy);
                })
                .OnDoubleClick(curve, (c, e) =>
                {
                    float t = vMinT + (float)e.NormalizedPosition.X * rngT;
                    float v = vMinV + (1f - (float)e.NormalizedPosition.Y) * rngV;
                    c.Keys.Add(new KeyFrame(t, v));
                    c.SmoothTangents(CurveTangent.Smooth);
                    onChange(c);
                })
                .OnClick(e =>
                {
                    // Deselect when clicking empty space
                    paper.SetElementStorage(el, "sel", -1);
                })
                .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                {
                    DrawGraph(canvas, r, curve, vMinT, vMaxT, vMinV, vMaxV);

                    // Draw tangent lines for selected keyframe inside the graph canvas
                    if (selectedIdx >= 0 && selectedIdx < curve.Keys.Count)
                    {
                        var selKey = curve.Keys[selectedIdx];
                        float skx = (float)r.Min.X + (selKey.Position - vMinT) * tToX;
                        float sky = (float)r.Min.Y + graphH - (selKey.Value - vMinV) * vToY;

                        canvas.SetStrokeColor(Color32.FromArgb(180, 200, 200, 60));
                        canvas.SetStrokeWidth(1.5f);

                        if (selectedIdx > 0)
                        {
                            var (tinX, tinY) = GetTangentScreenPos(skx, sky, selKey.TangentIn, -1f, tToX, vToY);
                            canvas.BeginPath(); canvas.MoveTo(skx, sky); canvas.LineTo(tinX, tinY); canvas.Stroke();
                        }
                        if (selectedIdx < curve.Keys.Count - 1)
                        {
                            var (toutX, toutY) = GetTangentScreenPos(skx, sky, selKey.TangentOut, 1f, tToX, vToY);
                            canvas.BeginPath(); canvas.MoveTo(skx, sky); canvas.LineTo(toutX, toutY); canvas.Stroke();
                        }
                    }
                }));

            // Rulers
            paper.Box($"{id}_rulerT")
                .PositionType(PositionType.SelfDirected)
                .Position(graphX, 0).Size(graphW, RulerSize)
                .IsNotInteractable()
                .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                    DrawTimeRuler(canvas, r, vMinT, vMaxT)));

            paper.Box($"{id}_rulerV")
                .PositionType(PositionType.SelfDirected)
                .Position(0, graphY).Size(RulerSize, graphH)
                .IsNotInteractable()
                .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                    DrawValueRuler(canvas, r, vMinV, vMaxV)));

            // Keyframe handles + tangent handles

            for (int i = 0; i < curve.Keys.Count; i++)
            {
                var key = curve.Keys[i];
                float kx = graphX + (key.Position - vMinT) * tToX;
                float ky = graphY + graphH - (key.Value - vMinV) * vToY;
                if (kx < graphX - 15 || kx > graphX + graphW + 15 || ky < graphY - 15 || ky > graphY + graphH + 15)
                    continue;

                bool isSelected = i == selectedIdx;
                int idx = i;

                // Tangent handles (only for selected keyframe, skip out-of-bounds tangents)
                if (isSelected)
                {
                    // Only show TangentIn if not the first key
                    if (i > 0)
                        DrawTangentHandle(paper, id, i, "In", curve, el, onChange,
                            kx, ky, key.TangentIn, -1f, tToX, vToY, graphX, graphY, graphW, graphH, rngT, rngV);

                    // Only show TangentOut if not the last key
                    if (i < curve.Keys.Count - 1)
                        DrawTangentHandle(paper, id, i, "Out", curve, el, onChange,
                            kx, ky, key.TangentOut, 1f, tToX, vToY, graphX, graphY, graphW, graphH, rngT, rngV);
                }

                // Keyframe dot
                paper.Box($"{id}_key_{i}")
                    .PositionType(PositionType.SelfDirected)
                    .Position(kx - 5, ky - 5).Size(10, 10)
                    .BackgroundColor(isSelected ? Color.FromArgb(255, 255, 200, 50) : Color.FromArgb(255, 255, 255, 255))
                    .Hovered.BackgroundColor(EditorTheme.Purple400).End()
                    .Rounded(5)
                    .StopEventPropagation()
                    .Tooltip($"({key.Position:F2}, {key.Value:F2})")
                    .OnClick(idx, (ci, e) =>
                    {
                        paper.SetElementStorage(el, "sel", ci);
                    })
                    .OnDragStart(idx, (ci, e) =>
                    {
                        // Select on drag start too
                        paper.SetElementStorage(el, "sel", ci);
                    })
                    .OnDragging(idx, (ci, e) =>
                    {
                        float dx = (float)e.Delta.X / graphW * rngT;
                        float dy = -(float)e.Delta.Y / graphH * rngV;
                        var k = curve.Keys[ci];
                        curve.Keys.RemoveAt(ci);
                        curve.Keys.Add(new KeyFrame(k.Position + dx, k.Value + dy, k.TangentIn, k.TangentOut, k.Continuity));
                        onChange(curve);
                    })
                    .OnRightClick(idx, (ci, e) =>
                    {
                        if (curve.Keys.Count > 2)
                        {
                            curve.Keys.RemoveAt(ci);
                            curve.SmoothTangents(CurveTangent.Smooth);
                            paper.SetElementStorage(el, "sel", -1);
                            onChange(curve);
                        }
                    });
            }

            // Bottom bar — actions
            using (paper.Row($"{id}_bar")
                .PositionType(PositionType.SelfDirected)
                .Position(4, barY).Size(editorW - 8, 26)
                .RowBetween(4).Enter())
            {
                var fitBtn = paper.Box($"{id}_fit")
                    .Height(22).ChildLeft(6).ChildRight(6)
                    .BackgroundColor(EditorTheme.ButtonNormal)
                    .Hovered.BackgroundColor(EditorTheme.ButtonHovered).End()
                    .Rounded(3)
                    .OnClick(e =>
                    {
                        GetCurveBounds(curve, out float fMinT, out float fMaxT, out float fMinV, out float fMaxV);
                        paper.SetElementStorage(el, "vMinT", fMinT);
                        paper.SetElementStorage(el, "vMaxT", fMaxT);
                        paper.SetElementStorage(el, "vMinV", fMinV);
                        paper.SetElementStorage(el, "vMaxV", fMaxV);
                    });
                if (EditorTheme.DefaultFont != null)
                    fitBtn.Text("Fit", EditorTheme.DefaultFont)
                        .TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize - 2);

                // Save current curve to palette
                var saveBtn = paper.Box($"{id}_save_preset")
                    .Height(22).ChildLeft(6).ChildRight(6)
                    .BackgroundColor(EditorTheme.ButtonNormal)
                    .Hovered.BackgroundColor(EditorTheme.ButtonHovered).End()
                    .Rounded(3)
                    .OnClick(e =>
                    {
                        try
                        {
                            var pal = ProjectSettingsRegistry.Get<EditorPaletteSettings>();
                            var preset = new EditorPaletteSettings.CurvePreset
                            {
                                Name = $"Custom {pal.CurvePalette.Count + 1}",
                                Keys = curve.Keys.Select(EditorPaletteSettings.KeyFrameData.FromKeyFrame).ToList()
                            };
                            pal.CurvePalette.Add(preset);
                            ProjectSettingsRegistry.SaveAll();
                        }
                        catch { }
                    });
                if (EditorTheme.DefaultFont != null)
                    saveBtn.Text($"{EditorIcons.Plus} Save", EditorTheme.DefaultFont)
                        .TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize - 2);
            }

            // Curve palette row
            DrawCurvePalette(paper, $"{id}_cpal", curve, el, onChange, editorW, paletteY);
        }
    }

    static void DrawCurvePalette(Paper paper, string id, AnimationCurve curve,
        ElementHandle editorEl, Action<AnimationCurve> onChange, float editorW, float paletteY)
    {
        List<EditorPaletteSettings.CurvePreset>? presets = null;
        try { presets = ProjectSettingsRegistry.Get<EditorPaletteSettings>().CurvePalette; }
        catch { }
        if (presets == null || presets.Count == 0) return;

        float presetW = 52f, presetH = 32f;
        float gap = 3f;
        float rowW = editorW - 8;
        int cols = Math.Max(1, (int)((rowW + gap) / (presetW + gap)));
        int rowCount = (presets.Count + cols - 1) / cols;
        int maxRows = 2;
        float totalH = Math.Min(rowCount, maxRows) * (presetH + gap);

        // Use SelfDirected columns of rows
        using (paper.Column($"{id}_col")
            .PositionType(PositionType.SelfDirected)
            .Position(4, paletteY).Size(rowW, totalH)
            .RowBetween(gap)
            .Clip()
            .Enter())
        {
            for (int row = 0; row < Math.Min(rowCount, maxRows); row++)
            {
                using (paper.Row($"{id}_r{row}").Height(presetH).RowBetween(gap).Enter())
                {
                    for (int col = 0; col < cols; col++)
                    {
                        int itemIdx = row * cols + col;
                        if (itemIdx >= presets.Count) break;

                        int idx = itemIdx;
                        var preset = presets[idx];

                        // Build temporary curve for preview
                        var tempCurve = new AnimationCurve();
                        foreach (var kd in preset.Keys) tempCurve.Keys.Add(kd.ToKeyFrame());

                        using (paper.Box($"{id}_p{idx}")
                            .Size(presetW, presetH)
                            .BackgroundColor(EditorTheme.Neutral300)
                            .Rounded(3)
                            .BorderColor(EditorTheme.Ink200).BorderWidth(1)
                            .Hovered.BorderColor(EditorTheme.Purple400).End()
                            .Tooltip(preset.Name)
                            .OnClick(idx, (ci, _) =>
                            {
                                var p = presets[ci];
                                curve.Keys.Clear();
                                foreach (var kd in p.Keys) curve.Keys.Add(kd.ToKeyFrame());
                                onChange(curve);
                                GetCurveBounds(curve, out float fMinT, out float fMaxT, out float fMinV, out float fMaxV);
                                paper.SetElementStorage(editorEl, "vMinT", fMinT);
                                paper.SetElementStorage(editorEl, "vMaxT", fMaxT);
                                paper.SetElementStorage(editorEl, "vMinV", fMinV);
                                paper.SetElementStorage(editorEl, "vMaxV", fMaxV);
                                paper.SetElementStorage(editorEl, "sel", -1);
                            })
                            .OnRightClick(idx, (ci, _) =>
                            {
                                presets.RemoveAt(ci);
                                ProjectSettingsRegistry.SaveAll();
                            })
                            .Enter())
                        {
                            // Draw preview inside the box using full size
                            paper.Box($"{id}_pv{idx}")
                                .Size(presetW, presetH)
                                .IsNotInteractable()
                                .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                                    DrawCurvePreview(canvas, r, tempCurve)));
                        }
                    }
                }
            }
        }
    }

    // ================================================================
    //  Rulers
    // ================================================================

    static void DrawTimeRuler(Canvas canvas, Rect r, float minT, float maxT)
    {
        float x = (float)r.Min.X, y = (float)r.Min.Y;
        float w = (float)r.Size.X, h = (float)r.Size.Y;
        float range = maxT - minT;
        var rulerFont = EditorTheme.DefaultFont;

        canvas.SetStrokeColor(Color32.FromArgb(60, 255, 255, 255));
        canvas.SetStrokeWidth(0.5f);

        float step = GetNiceStep(range, w, 50);
        float start = MathF.Ceiling(minT / step) * step;

        for (float t = start; t <= maxT; t += step)
        {
            float px = x + (t - minT) / range * w;
            canvas.BeginPath(); canvas.MoveTo(px, y + h - 6); canvas.LineTo(px, y + h); canvas.Stroke();

            if (rulerFont != null)
                canvas.DrawText(t.ToString("G3"), px + 2, y + 2, Color32.FromArgb(120, 200, 200, 200), 9, rulerFont, 0);
        }
    }

    static void DrawValueRuler(Canvas canvas, Rect r, float minV, float maxV)
    {
        float x = (float)r.Min.X, y = (float)r.Min.Y;
        float w = (float)r.Size.X, h = (float)r.Size.Y;
        float range = maxV - minV;
        var rulerFont = EditorTheme.DefaultFont;

        canvas.SetStrokeColor(Color32.FromArgb(60, 255, 255, 255));
        canvas.SetStrokeWidth(0.5f);

        float step = GetNiceStep(range, h, 40);
        float start = MathF.Ceiling(minV / step) * step;

        for (float v = start; v <= maxV; v += step)
        {
            float py = y + h - (v - minV) / range * h;
            canvas.BeginPath(); canvas.MoveTo(x + w - 6, py); canvas.LineTo(x + w, py); canvas.Stroke();

            if (rulerFont != null)
                canvas.DrawText(v.ToString("G3"), x + 1, py - 4, Color32.FromArgb(120, 200, 200, 200), 9, rulerFont, 0);
        }
    }

    static float GetNiceStep(float range, float pixels, float minPixelStep)
    {
        float rawStep = range * minPixelStep / pixels;
        float magnitude = MathF.Pow(10, MathF.Floor(MathF.Log10(rawStep)));
        float residual = rawStep / magnitude;

        if (residual <= 1) return magnitude;
        if (residual <= 2) return 2 * magnitude;
        if (residual <= 5) return 5 * magnitude;
        return 10 * magnitude;
    }

    // ================================================================
    //  Graph Drawing
    // ================================================================

    static void DrawGraph(Canvas canvas, Rect r, AnimationCurve curve,
        float vMinT, float vMaxT, float vMinV, float vMaxV)
    {
        if (curve.Keys.Count == 0) return;
        float x = (float)r.Min.X, y = (float)r.Min.Y;
        float w = (float)r.Size.X, h = (float)r.Size.Y;
        float rngT = vMaxT - vMinT, rngV = vMaxV - vMinV;

        // Grid
        float stepT = GetNiceStep(rngT, w, 50);
        float stepV = GetNiceStep(rngV, h, 40);

        canvas.SetStrokeColor(Color32.FromArgb(25, 255, 255, 255));
        canvas.SetStrokeWidth(0.5f);

        for (float t = MathF.Ceiling(vMinT / stepT) * stepT; t <= vMaxT; t += stepT)
        {
            float gx = x + (t - vMinT) / rngT * w;
            canvas.BeginPath(); canvas.MoveTo(gx, y); canvas.LineTo(gx, y + h); canvas.Stroke();
        }
        for (float v = MathF.Ceiling(vMinV / stepV) * stepV; v <= vMaxV; v += stepV)
        {
            float gy = y + h - (v - vMinV) / rngV * h;
            canvas.BeginPath(); canvas.MoveTo(x, gy); canvas.LineTo(x + w, gy); canvas.Stroke();
        }

        // Zero lines
        canvas.SetStrokeColor(Color32.FromArgb(45, 255, 255, 255));
        canvas.SetStrokeWidth(1f);
        if (vMinV < 0 && vMaxV > 0)
        {
            float zy = y + h - (-vMinV) / rngV * h;
            canvas.BeginPath(); canvas.MoveTo(x, zy); canvas.LineTo(x + w, zy); canvas.Stroke();
        }
        if (vMinT < 0 && vMaxT > 0)
        {
            float zx = x + (-vMinT) / rngT * w;
            canvas.BeginPath(); canvas.MoveTo(zx, y); canvas.LineTo(zx, y + h); canvas.Stroke();
        }

        // Curve
        int steps = 120;
        canvas.SetStrokeColor(Color32.FromArgb(255, 51, 180, 100));
        canvas.SetStrokeWidth(2f);
        canvas.BeginPath();
        for (int i = 0; i <= steps; i++)
        {
            float t = vMinT + rngT * i / steps;
            float v = curve.Evaluate(t);
            float px = x + (t - vMinT) / rngT * w;
            float py = y + h - (v - vMinV) / rngV * h;
            if (i == 0) canvas.MoveTo(px, py); else canvas.LineTo(px, py);
        }
        canvas.Stroke();

        // Keyframe dots (drawn in canvas for the base layer)
        for (int i = 0; i < curve.Keys.Count; i++)
        {
            var key = curve.Keys[i];
            float kx = x + (key.Position - vMinT) / rngT * w;
            float ky = y + h - (key.Value - vMinV) / rngV * h;
            if (kx < x - 5 || kx > x + w + 5 || ky < y - 5 || ky > y + h + 5) continue;
            canvas.SetFillColor(Color32.FromArgb(255, 255, 255, 255));
            canvas.BeginPath(); canvas.Circle(kx, ky, 3, 12); canvas.Fill();
        }
    }

    // ================================================================
    //  Helpers
    // ================================================================

    static void DrawTangentHandle(Paper paper, string id, int keyIdx, string side,
        AnimationCurve curve, ElementHandle el, Action<AnimationCurve> onChange,
        float kx, float ky, float tangent, float dir, float tToX, float vToY,
        float graphX, float graphY, float graphW, float graphH, float rngT, float rngV)
    {
        var (hx, hy) = GetTangentScreenPos(kx, ky, tangent, dir, tToX, vToY);

        paper.Box($"{id}_tan{side}_{keyIdx}")
            .PositionType(PositionType.SelfDirected)
            .Position(hx - 4, hy - 4).Size(8, 8)
            .BackgroundColor(Color.FromArgb(255, 200, 200, 60))
            .Rounded(4)
            .StopEventPropagation()
            .Hovered.BackgroundColor(Color.FromArgb(255, 255, 255, 80)).End()
            .OnDragging(keyIdx, (ci, e) =>
            {
                var k = curve.Keys[ci];
                // Convert mouse delta to time/value delta
                float dt = (float)e.Delta.X / tToX;  // pixel to time
                float dv = -(float)e.Delta.Y / vToY;  // pixel to value (inverted Y)

                // The tangent is the slope: dv/dt
                // Compute new handle position relative to keyframe
                float curHandleDt = dir * TangentHandleLength / tToX;
                float curTangent = side == "In" ? k.TangentIn : k.TangentOut;
                float curHandleDv = curTangent * curHandleDt;

                // Add the mouse delta
                curHandleDt += dt;
                curHandleDv += dv;

                // New tangent = new slope
                if (MathF.Abs(curHandleDt) > 0.0001f)
                {
                    float newTangent = curHandleDv / curHandleDt;
                    if (side == "In") k.TangentIn = newTangent;
                    else k.TangentOut = newTangent;
                }
                onChange(curve);
            });
    }

    static (float x, float y) GetTangentScreenPos(float kx, float ky, float tangent, float dir, float tToX, float vToY)
    {
        // Handle direction in screen space:
        // A step of 'dir' in time = dir * tToX pixels in X
        // The corresponding value change = tangent * dir, which is -tangent * dir * vToY pixels in Y (inverted)
        float dx = dir * TangentHandleLength;
        float dy = -tangent * dir * (TangentHandleLength / tToX) * vToY;

        // Normalize to fixed handle length
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len > 0) { dx = dx / len * TangentHandleLength; dy = dy / len * TangentHandleLength; }

        return (kx + dx, ky + dy);
    }

    static void GetCurveBounds(AnimationCurve curve, out float minT, out float maxT, out float minV, out float maxV)
    {
        if (curve.Keys.Count == 0) { minT = 0; maxT = 1; minV = 0; maxV = 1; return; }
        minT = curve.Keys[0].Position;
        maxT = curve.Keys[curve.Keys.Count - 1].Position;
        if (maxT <= minT) maxT = minT + 1;
        minV = float.MaxValue; maxV = float.MinValue;
        for (int i = 0; i <= PreviewSteps; i++)
        {
            float t = minT + (maxT - minT) * i / PreviewSteps;
            float v = curve.Evaluate(t);
            minV = MathF.Min(minV, v); maxV = MathF.Max(maxV, v);
        }
        if (maxV <= minV) { minV -= 0.5f; maxV += 0.5f; }
        float padT = (maxT - minT) * 0.1f, padV = (maxV - minV) * 0.1f;
        minT -= padT; maxT += padT; minV -= padV; maxV += padV;
    }
}
