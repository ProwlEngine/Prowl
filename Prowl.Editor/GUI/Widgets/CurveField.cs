// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Runtime;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI.Widgets;

/// <summary>
/// Fluent builder for an AnimationCurve editor field.
/// Renders an inline preview swatch that opens a full curve editor popover on click.
/// Depends on Prowl.Runtime (AnimationCurve) so lives outside Origami.
///
/// Usage:
///   CurveField.Create(paper, id, curve, v => myCurve = v).Show();
/// </summary>
public sealed class CurveFieldBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly AnimationCurve _value;
    private readonly Action<AnimationCurve> _setter;

    private UnitValue _width = UnitValue.Stretch();
    private float _previewHeight = 40f;
    private bool _readOnly;

    internal CurveFieldBuilder(Paper paper, string id, AnimationCurve value, Action<AnimationCurve> setter)
    {
        _paper = paper;
        _id = id;
        _value = value;
        _setter = setter;
    }

    /// <summary>Override the field width (default Stretch).</summary>
    public CurveFieldBuilder Width(UnitValue width) { _width = width; return this; }

    /// <summary>Override the inline preview height (default 40).</summary>
    public CurveFieldBuilder PreviewHeight(float height) { _previewHeight = MathF.Max(20, height); return this; }

    /// <summary>Make the field read-only (no popover).</summary>
    public CurveFieldBuilder ReadOnly(bool readOnly = true) { _readOnly = readOnly; return this; }

    /// <summary>Render the curve field.</summary>
    public void Show()
    {
        if (Origami.IsReadOnly) _readOnly = true;
        var theme = Origami.Current;
        var m = theme.Metrics;

        var swatch = _paper.Box($"{_id}_swatch")
            .Width(_width).Height(_previewHeight)
            .BackgroundColor(theme.Neutral.C200)
            .BorderColor(theme.Neutral.C400).BorderWidth(1)
            .Hovered.BorderColor(theme.Primary.C400).End()
            .Rounded(m.Rounding);

        if (!_readOnly)
        {
            var curve = _value;
            var setter = _setter;
            var id = _id;
            swatch.OnClick(e =>
            {
                float anchorX = (float)e.ElementRect.Min.X;
                float anchorY = (float)e.ElementRect.Max.Y + 2;
                Modal.Push(new CurveEditorModal(id, curve, setter, anchorX, anchorY));
            });
        }

        using (swatch.Enter())
        {
            _paper.Box($"{_id}_preview")
                .Width(UnitValue.Stretch()).Height(_previewHeight)
                .IsNotInteractable()
                .OnPostLayout((handle, rect) => _paper.Draw(ref handle, (canvas, r) =>
                    CurveRenderer.DrawPreview(canvas, r, _value, theme)));
        }
    }
}

/// <summary>Static entry point for the CurveField builder.</summary>
public static class CurveField
{
    /// <summary>Create a curve field builder.</summary>
    public static CurveFieldBuilder Create(Paper paper, string id, AnimationCurve value, Action<AnimationCurve> setter)
        => new CurveFieldBuilder(paper, id, value, setter);
}

// ================================================================
//  Curve Renderer - canvas drawing for preview and full editor
// ================================================================

public static class CurveRenderer
{
    private const int PreviewSteps = 40;
    private const int EditorSteps = 120;

    public static void DrawPreview(Canvas canvas, Rect r, AnimationCurve curve, OrigamiTheme theme)
    {
        if (curve.Keys.Count == 0) return;
        float x = (float)r.Min.X + 2, y = (float)r.Min.Y + 2;
        float w = (float)r.Size.X - 4, h = (float)r.Size.Y - 4;
        GetBounds(curve, out float minT, out float maxT, out float minV, out float maxV);

        var curveColor = Color32.FromArgb(255, (byte)theme.Green.C500.R, (byte)theme.Green.C500.G, (byte)theme.Green.C500.B);
        canvas.SetStrokeColor(curveColor);
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

    public static void DrawGraph(Canvas canvas, Rect r, AnimationCurve curve,
        float vMinT, float vMaxT, float vMinV, float vMaxV, OrigamiTheme theme, int selectedIdx)
    {
        if (curve.Keys.Count == 0) return;
        float x = (float)r.Min.X, y = (float)r.Min.Y;
        float w = (float)r.Size.X, h = (float)r.Size.Y;
        float rngT = vMaxT - vMinT, rngV = vMaxV - vMinV;

        // Grid
        float stepT = GetNiceStep(rngT, w, 50);
        float stepV = GetNiceStep(rngV, h, 40);

        canvas.SetStrokeColor(Color32.FromArgb(20, 255, 255, 255));
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

        // Zero axes
        canvas.SetStrokeColor(Color32.FromArgb(40, 255, 255, 255));
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

        // Curve line
        var curveColor = Color32.FromArgb(255, (byte)theme.Green.C500.R, (byte)theme.Green.C500.G, (byte)theme.Green.C500.B);
        canvas.SetStrokeColor(curveColor);
        canvas.SetStrokeWidth(2f);
        canvas.BeginPath();
        for (int i = 0; i <= EditorSteps; i++)
        {
            float t = vMinT + rngT * i / EditorSteps;
            float v = curve.Evaluate(t);
            float px = x + (t - vMinT) / rngT * w;
            float py = y + h - (v - vMinV) / rngV * h;
            if (i == 0) canvas.MoveTo(px, py); else canvas.LineTo(px, py);
        }
        canvas.Stroke();

        // Keyframe dots
        float tToX = w / rngT, vToY = h / rngV;
        for (int i = 0; i < curve.Keys.Count; i++)
        {
            var key = curve.Keys[i];
            float kx = x + (key.Position - vMinT) * tToX;
            float ky = y + h - (key.Value - vMinV) * vToY;
            if (kx < x - 6 || kx > x + w + 6 || ky < y - 6 || ky > y + h + 6) continue;

            bool isSel = i == selectedIdx;
            var dotColor = isSel
                ? Color32.FromArgb(255, 255, 210, 60)
                : Color32.FromArgb(255, 240, 240, 240);
            canvas.SetFillColor(dotColor);
            canvas.BeginPath(); canvas.Circle(kx, ky, isSel ? 4.5f : 3.5f, 12); canvas.Fill();

            // Tangent lines for selected key
            if (isSel)
            {
                canvas.SetStrokeColor(Color32.FromArgb(160, 200, 200, 60));
                canvas.SetStrokeWidth(1.5f);
                if (i > 0)
                {
                    var (tinX, tinY) = GetTangentScreenPos(kx, ky, key.TangentIn, -1f, tToX, vToY);
                    canvas.BeginPath(); canvas.MoveTo(kx, ky); canvas.LineTo(tinX, tinY); canvas.Stroke();
                    canvas.SetFillColor(Color32.FromArgb(255, 200, 200, 60));
                    canvas.BeginPath(); canvas.Circle(tinX, tinY, 3f, 8); canvas.Fill();
                }
                if (i < curve.Keys.Count - 1)
                {
                    var (toutX, toutY) = GetTangentScreenPos(kx, ky, key.TangentOut, 1f, tToX, vToY);
                    canvas.BeginPath(); canvas.MoveTo(kx, ky); canvas.LineTo(toutX, toutY); canvas.Stroke();
                    canvas.SetFillColor(Color32.FromArgb(255, 200, 200, 60));
                    canvas.BeginPath(); canvas.Circle(toutX, toutY, 3f, 8); canvas.Fill();
                }
            }
        }
    }

    public static void DrawRulerH(Canvas canvas, Rect r, float minT, float maxT, Scribe.FontFile? font)
    {
        float x = (float)r.Min.X, y = (float)r.Min.Y;
        float w = (float)r.Size.X, h = (float)r.Size.Y;
        float range = maxT - minT;

        canvas.SetStrokeColor(Color32.FromArgb(50, 255, 255, 255));
        canvas.SetStrokeWidth(0.5f);
        float step = GetNiceStep(range, w, 50);
        for (float t = MathF.Ceiling(minT / step) * step; t <= maxT; t += step)
        {
            float px = x + (t - minT) / range * w;
            canvas.BeginPath(); canvas.MoveTo(px, y + h - 5); canvas.LineTo(px, y + h); canvas.Stroke();
            if (font != null)
                canvas.DrawText(t.ToString("G3"), px + 2, y + 2, Color32.FromArgb(100, 200, 200, 200), 9, font, 0);
        }
    }

    public static void DrawRulerV(Canvas canvas, Rect r, float minV, float maxV, Scribe.FontFile? font)
    {
        float x = (float)r.Min.X, y = (float)r.Min.Y;
        float w = (float)r.Size.X, h = (float)r.Size.Y;
        float range = maxV - minV;

        canvas.SetStrokeColor(Color32.FromArgb(50, 255, 255, 255));
        canvas.SetStrokeWidth(0.5f);
        float step = GetNiceStep(range, h, 40);
        for (float v = MathF.Ceiling(minV / step) * step; v <= maxV; v += step)
        {
            float py = y + h - (v - minV) / range * h;
            canvas.BeginPath(); canvas.MoveTo(x + w - 5, py); canvas.LineTo(x + w, py); canvas.Stroke();
            if (font != null)
                canvas.DrawText(v.ToString("G3"), x + 1, py - 4, Color32.FromArgb(100, 200, 200, 200), 9, font, 0);
        }
    }

    public static (float x, float y) GetTangentScreenPos(float kx, float ky, float tangent, float dir, float tToX, float vToY)
    {
        const float HandleLen = 40f;
        float dx = dir * HandleLen;
        float dy = -tangent * dir * (HandleLen / tToX) * vToY;
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len > 0) { dx = dx / len * HandleLen; dy = dy / len * HandleLen; }
        return (kx + dx, ky + dy);
    }

    public static void GetBounds(AnimationCurve curve, out float minT, out float maxT, out float minV, out float maxV)
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

    public static float GetNiceStep(float range, float pixels, float minPixelStep)
    {
        float rawStep = range * minPixelStep / pixels;
        float magnitude = MathF.Pow(10, MathF.Floor(MathF.Log10(rawStep)));
        float residual = rawStep / magnitude;
        if (residual <= 1) return magnitude;
        if (residual <= 2) return 2 * magnitude;
        if (residual <= 5) return 5 * magnitude;
        return 10 * magnitude;
    }
}

// ================================================================
//  Curve Popover - the full editor panel
// ================================================================

internal static class CurvePopover
{
    private const float EditorW = 440f;
    private const float GraphH = 240f;
    private const float RulerSize = 24f;
    private const float TangentHandleLen = 40f;

    public static float EditorWidth => EditorW;
    public static float EditorHeight
    {
        get
        {
            var m = Origami.Current.Metrics;
            return RulerSize + GraphH + m.Spacing + m.RowHeight + m.Spacing;
        }
    }

    /// <summary>Draw the curve editor content (no container). Used by the modal.</summary>
    public static void DrawContent(Paper paper, string id, AnimationCurve curve, Action<AnimationCurve> onChange, OrigamiTheme theme)
    {
        var m = theme.Metrics;
        float graphX = RulerSize, graphY = RulerSize;
        float graphW = EditorW - RulerSize - m.SpacingLarge;
        float barY = graphY + GraphH + m.Spacing;

        {
            var el = paper.CurrentParent;

            // Initialize view bounds
            bool initialized = paper.GetElementStorage(el, "init", false);
            if (!initialized)
            {
                CurveRenderer.GetBounds(curve, out float fMinT, out float fMaxT, out float fMinV, out float fMaxV);
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
            float tToX = graphW / rngT, vToY = GraphH / rngV;

            // Graph area
            paper.Box($"{id}_graph")
                .PositionType(PositionType.SelfDirected)
                .Position(graphX, graphY).Size(graphW, GraphH)
                .BackgroundColor(Color.FromArgb(255, 22, 22, 26))
                .Rounded(m.SmallRounding).Clip()
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
                    float dy = (float)e.Delta.Y / GraphH * rngV;
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
                .OnClick(e => paper.SetElementStorage(el, "sel", -1))
                .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                    CurveRenderer.DrawGraph(canvas, r, curve, vMinT, vMaxT, vMinV, vMaxV, theme, selectedIdx)));

            // Rulers
            paper.Box($"{id}_rulerT")
                .PositionType(PositionType.SelfDirected)
                .Position(graphX, 0).Size(graphW, RulerSize)
                .IsNotInteractable()
                .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                    CurveRenderer.DrawRulerH(canvas, r, vMinT, vMaxT, theme.Font)));

            paper.Box($"{id}_rulerV")
                .PositionType(PositionType.SelfDirected)
                .Position(0, graphY).Size(RulerSize, GraphH)
                .IsNotInteractable()
                .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                    CurveRenderer.DrawRulerV(canvas, r, vMinV, vMaxV, theme.Font)));

            // Interactive keyframe handles
            for (int i = 0; i < curve.Keys.Count; i++)
            {
                var key = curve.Keys[i];
                float kx = graphX + (key.Position - vMinT) * tToX;
                float ky = graphY + GraphH - (key.Value - vMinV) * vToY;
                if (kx < graphX - 12 || kx > graphX + graphW + 12 || ky < graphY - 12 || ky > graphY + GraphH + 12)
                    continue;

                bool isSelected = i == selectedIdx;
                int idx = i;

                // Tangent handles for selected key
                if (isSelected)
                {
                    if (i > 0)
                        DrawTangentHandle(paper, id, i, "In", curve, el, onChange, kx, ky, key.TangentIn, -1f, tToX, vToY, theme);
                    if (i < curve.Keys.Count - 1)
                        DrawTangentHandle(paper, id, i, "Out", curve, el, onChange, kx, ky, key.TangentOut, 1f, tToX, vToY, theme);
                }

                // Keyframe handle
                float dotSize = isSelected ? 10 : 8;
                paper.Box($"{id}_key_{i}")
                    .PositionType(PositionType.SelfDirected)
                    .Position(kx - dotSize * 0.5f, ky - dotSize * 0.5f).Size(dotSize, dotSize)
                    .BackgroundColor(isSelected ? Color.FromArgb(255, 255, 210, 60) : Color.White)
                    .Hovered.BackgroundColor(Color.FromArgb(255, theme.Primary.C400.R, theme.Primary.C400.G, theme.Primary.C400.B)).End()
                    .Rounded(dotSize * 0.5f)
                    .StopEventPropagation()
                    .OnClick(idx, (ci, _) => paper.SetElementStorage(el, "sel", ci))
                    .OnDragStart(idx, (ci, _) => paper.SetElementStorage(el, "sel", ci))
                    .OnDragging(idx, (ci, e) =>
                    {
                        float dx = (float)e.Delta.X / graphW * rngT;
                        float dy = -(float)e.Delta.Y / GraphH * rngV;
                        var k = curve.Keys[ci];
                        curve.Keys.RemoveAt(ci);
                        curve.Keys.Add(new KeyFrame(k.Position + dx, k.Value + dy, k.TangentIn, k.TangentOut, k.Continuity));
                        onChange(curve);
                    })
                    .OnRightClick(idx, (ci, _) =>
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

            // Bottom toolbar
            using (paper.Row($"{id}_bar")
                .PositionType(PositionType.SelfDirected)
                .Position(m.Spacing, barY).Size(EditorW - m.SpacingLarge, m.RowHeight)
                .RowBetween(m.Spacing).Enter())
            {
                Origami.Button(paper, $"{id}_fit", "Fit", () =>
                {
                    CurveRenderer.GetBounds(curve, out float fMinT, out float fMaxT, out float fMinV, out float fMaxV);
                    paper.SetElementStorage(el, "vMinT", fMinT);
                    paper.SetElementStorage(el, "vMaxT", fMaxT);
                    paper.SetElementStorage(el, "vMinV", fMinV);
                    paper.SetElementStorage(el, "vMaxV", fMaxV);
                }).Height(m.CompactHeight).Show();

                Origami.Button(paper, $"{id}_smooth", "Smooth All", () =>
                {
                    curve.SmoothTangents(CurveTangent.Smooth);
                    onChange(curve);
                }).Height(m.CompactHeight).Show();

                Origami.Button(paper, $"{id}_linear", "Linear All", () =>
                {
                    curve.SmoothTangents(CurveTangent.Linear);
                    onChange(curve);
                }).Height(m.CompactHeight).Show();

                Origami.Button(paper, $"{id}_flat", "Flat All", () =>
                {
                    curve.SmoothTangents(CurveTangent.Flat);
                    onChange(curve);
                }).Height(m.CompactHeight).Show();
            }
        }
    }

    private static void DrawTangentHandle(Paper paper, string id, int keyIdx, string side,
        AnimationCurve curve, ElementHandle el, Action<AnimationCurve> onChange,
        float kx, float ky, float tangent, float dir, float tToX, float vToY, OrigamiTheme theme)
    {
        var (hx, hy) = CurveRenderer.GetTangentScreenPos(kx, ky, tangent, dir, tToX, vToY);
        var m = theme.Metrics;

        paper.Box($"{id}_tan{side}_{keyIdx}")
            .PositionType(PositionType.SelfDirected)
            .Position(hx - 4, hy - 4).Size(8, 8)
            .BackgroundColor(Color.FromArgb(255, 200, 200, 60))
            .Hovered.BackgroundColor(Color.FromArgb(255, 255, 255, 80)).End()
            .Rounded(4)
            .StopEventPropagation()
            .OnDragging(keyIdx, (ci, e) =>
            {
                var k = curve.Keys[ci];
                float dt = (float)e.Delta.X / tToX;
                float dv = -(float)e.Delta.Y / vToY;

                float curHandleDt = dir * TangentHandleLen / tToX;
                float curTangent = side == "In" ? k.TangentIn : k.TangentOut;
                float curHandleDv = curTangent * curHandleDt;

                curHandleDt += dt;
                curHandleDv += dv;

                if (MathF.Abs(curHandleDt) > 0.0001f)
                {
                    float newTangent = curHandleDv / curHandleDt;
                    if (side == "In") k.TangentIn = newTangent;
                    else k.TangentOut = newTangent;
                }
                onChange(curve);
            });
    }
}

// ================================================================
//  Curve Editor Modal - wraps the popover in the modal stack
// ================================================================

internal sealed class CurveEditorModal : IModal
{
    private readonly string _id;
    private readonly AnimationCurve _curve;
    private readonly Action<AnimationCurve> _setter;
    private readonly float _anchorX;
    private readonly float _anchorY;

    public bool CloseOnBackdrop => true;
    public bool CloseOnEscape => true;

    public CurveEditorModal(string id, AnimationCurve curve, Action<AnimationCurve> setter, float anchorX, float anchorY)
    {
        _id = id;
        _curve = curve;
        _setter = setter;
        _anchorX = anchorX;
        _anchorY = anchorY;
    }

    public void Draw(Paper paper, int layer, int stackIndex)
    {
        var theme = Origami.Current;
        var m = theme.Metrics;

        using (paper.Column($"{_id}_modal")
            .PositionType(PositionType.SelfDirected)
            .Position(_anchorX, _anchorY)
            .Size(CurvePopover.EditorWidth, CurvePopover.EditorHeight)
            .BackgroundColor(theme.Neutral.C300)
            .BorderColor(theme.Ink.C200).BorderWidth(1)
            .Rounded(m.ContainerRounding)
            .BoxShadow(0, 4, 24, 0, Color.FromArgb(100, 0, 0, 0))
            .Layer(layer)
            .ClampToScreen()
            .StopEventPropagation()
            .Enter())
        {
            CurvePopover.DrawContent(paper, _id, _curve, _setter, theme);
        }
    }
}
