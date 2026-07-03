// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;
using Prowl.Vector.Spatial;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// Fluent builder for an image diff slider widget. Two images are overlaid with a
/// draggable vertical split bar so the user can scrub between them.
/// Construct via <see cref="Origami.ImageDiff(Paper, string, object, object)"/>.
/// </summary>
public sealed class ImageDiffBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private readonly object _imageA;
    private readonly object _imageB;

    private UnitValue _width = UnitValue.Stretch();
    private float _height = 156f;
    private float _splitPos = 0.5f;
    private float _barWidth = 2f;
    private float _handleSize = 26f;
    private float _edgePad = 16f;

    internal ImageDiffBuilder(Paper paper, string id, object imageA, object imageB, OrigamiTheme theme)
    {
        _paper = paper;
        _id = id;
        _imageA = imageA;
        _imageB = imageB;
        _theme = theme;
    }

    public ImageDiffBuilder Width(UnitValue width) { _width = width; return this; }
    public ImageDiffBuilder Height(float height) { _height = MathF.Max(32, height); return this; }
    public ImageDiffBuilder SplitPosition(float pos) { _splitPos = Math.Clamp(pos, 0, 1); return this; }
    public ImageDiffBuilder BarWidth(float w) { _barWidth = MathF.Max(1, w); return this; }
    public ImageDiffBuilder HandleSize(float s) { _handleSize = MathF.Max(8, s); return this; }

    public void Show()
    {
        var m = _theme.Metrics;
        var capturedA = _imageA;
        var capturedB = _imageB;
        var capturedBarW = _barWidth;
        var capturedHandleSize = _handleSize;
        var capturedEdgePad = _edgePad;
        var primary = _theme.Primary;
        var font = _theme.Font;

        // .w2diff: 9px radius, 1px soft border, clipped, dark fallback behind the images.
        var container = _paper.Box($"{_id}_box")
            .Width(_width).Height(_height)
            .Rounded(9f)
            .BorderColor(_theme.BorderSoft).BorderWidth(1)
            .Clip()
            .BackgroundColor(Color.FromArgb(255, 20, 20, 24));

        using (container.Enter())
        {
            var el = _paper.CurrentParent;

            // Read/write split position from element storage so dragging persists
            float split = _paper.GetElementStorage(el, "split", _splitPos);

            // Drag the whole container to adjust split
            container.OnDragging(e =>
            {
                float w = (float)e.ElementRect.Size.X;
                if (w <= 0) return;
                float localX = (float)e.RelativePosition.X;
                float minX = capturedEdgePad / w;
                float maxX = 1f - capturedEdgePad / w;
                _paper.SetElementStorage(el, "split", Math.Clamp(localX / w, minX, maxX));
            });

            // Also allow click to set position
            container.OnClick(e =>
            {
                float w = (float)e.ElementRect.Size.X;
                if (w <= 0) return;
                float localX = (float)e.RelativePosition.X;
                float minX = capturedEdgePad / w;
                float maxX = 1f - capturedEdgePad / w;
                _paper.SetElementStorage(el, "split", Math.Clamp(localX / w, minX, maxX));
            });

            // Draw both images + bar via canvas
            _paper.Box($"{_id}_canvas")
                .PositionType(PositionType.SelfDirected)
                .Position(0, 0).Size(UnitValue.Stretch(), UnitValue.Stretch())
                .IsNotInteractable()
                .OnPostLayout((handle, rect) => _paper.Draw(ref handle, (canvas, r) =>
                {
                    float x = (float)r.Min.X, y = (float)r.Min.Y;
                    float w = (float)r.Size.X, h = (float)r.Size.Y;
                    float splitX = x + split * w;

                    // Image B (full background)
                    canvas.SetBrushTexture(capturedB);
                    canvas.SetBrushTextureTransform(
                        Transform2D.CreateTranslation(x, y) * Transform2D.CreateScale(w, h));
                    canvas.RectFilled(x, y, w, h, Color32.FromArgb(255, 255, 255, 255));
                    canvas.ClearBrushTexture();

                    // Image A (left portion only - clip by drawing a narrower rect)
                    float leftW = splitX - x;
                    if (leftW > 0)
                    {
                        canvas.SetBrushTexture(capturedA);
                        canvas.SetBrushTextureTransform(
                            Transform2D.CreateTranslation(x, y) * Transform2D.CreateScale(w, h));
                        canvas.RectFilled(x, y, leftW, h, Color32.FromArgb(255, 255, 255, 255));
                        canvas.ClearBrushTexture();
                    }

                    float cy = y + h * 0.5f;

                    // .w2diff-handle glow — box-shadow 0 0 10px acc-glow behind the white line.
                    canvas.SaveState();
                    Color accGlow = Color.FromArgb(128, 168, 85, 247);
                    canvas.SetBoxBrush(splitX, cy, capturedBarW, h - 4f, 1f, 10f,
                        accGlow, Color.FromArgb(0, 168, 85, 247));
                    canvas.BeginPath();
                    canvas.Rect(splitX - 16f, y, 32f, h);
                    canvas.Fill();
                    canvas.RestoreState();

                    // .w2diff-handle — 2px white vertical line at the split.
                    float barHalf = capturedBarW * 0.5f;
                    canvas.RectFilled(splitX - barHalf, y, capturedBarW, h,
                        Color32.FromArgb(255, 255, 255, 255));

                    // .w2diff-grip — soft drop shadow (box-shadow 0 2px 12px rgba(0,0,0,0.55)).
                    float handleR = capturedHandleSize * 0.5f;
                    canvas.SaveState();
                    canvas.SetBoxBrush(splitX, cy + 2f, capturedHandleSize, capturedHandleSize,
                        handleR, 12f, Color.FromArgb(140, 0, 0, 0), Color.FromArgb(0, 0, 0, 0));
                    canvas.BeginPath();
                    float shPad = handleR + 20f;
                    canvas.Rect(splitX - shPad, cy + 2f - shPad, shPad * 2f, shPad * 2f);
                    canvas.Fill();
                    canvas.RestoreState();

                    // .w2diff-grip — 26px accent circle (#A855F7).
                    var accCol = Color32.FromArgb(255, (byte)primary.C500.R, (byte)primary.C500.G, (byte)primary.C500.B);
                    canvas.CircleFilled(splitX, cy, handleR, accCol);

                    // White left-right drag glyph ("<->") on the grip.
                    canvas.SetStrokeColor(Color32.FromArgb(255, 255, 255, 255));
                    canvas.SetStrokeWidth(1.6f);
                    float lineLen = handleR * 0.42f;
                    float ah = handleR * 0.26f;

                    // Shaft
                    canvas.BeginPath();
                    canvas.MoveTo(splitX - lineLen, cy);
                    canvas.LineTo(splitX + lineLen, cy);
                    canvas.Stroke();

                    // Left arrowhead
                    canvas.BeginPath();
                    canvas.MoveTo(splitX - lineLen + ah, cy - ah);
                    canvas.LineTo(splitX - lineLen, cy);
                    canvas.LineTo(splitX - lineLen + ah, cy + ah);
                    canvas.Stroke();

                    // Right arrowhead
                    canvas.BeginPath();
                    canvas.MoveTo(splitX + lineLen - ah, cy - ah);
                    canvas.LineTo(splitX + lineLen, cy);
                    canvas.LineTo(splitX + lineLen - ah, cy + ah);
                    canvas.Stroke();

                    // .w2diff-tag — "Before" bottom-left, "After" bottom-right pills.
                    if (font != null)
                    {
                        const float tagFont = 10f;
                        const float padTX = 8f, padTY = 2f, margin = 8f;
                        Color tagBg = Color.FromArgb(140, 0, 0, 0);
                        Color tagFg = Color.FromArgb(255, 255, 255, 255);

                        var bs = canvas.MeasureText("Before", tagFont, font);
                        float bpw = (float)bs.X + padTX * 2f;
                        float bph = (float)bs.Y + padTY * 2f;
                        float bpx = x + margin;
                        float bpy = y + h - margin - bph;
                        canvas.RoundedRectFilled(bpx, bpy, bpw, bph, 6f, tagBg);
                        canvas.DrawText("Before", bpx + padTX, bpy + padTY, tagFg, tagFont, font);

                        var as_ = canvas.MeasureText("After", tagFont, font);
                        float apw = (float)as_.X + padTX * 2f;
                        float aph = (float)as_.Y + padTY * 2f;
                        float apx = x + w - margin - apw;
                        float apy = y + h - margin - aph;
                        canvas.RoundedRectFilled(apx, apy, apw, aph, 6f, tagBg);
                        canvas.DrawText("After", apx + padTX, apy + padTY, tagFg, tagFont, font);
                    }
                }));
        }
    }
}
