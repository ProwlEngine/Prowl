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
    private float _height = 256f;
    private float _splitPos = 0.5f;
    private float _barWidth = 3f;
    private float _handleSize = 24f;
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

        var container = _paper.Box($"{_id}_box")
            .Width(_width).Height(_height)
            .Rounded(m.Rounding)
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

                    // Vertical bar
                    float barHalf = capturedBarW * 0.5f;
                    canvas.RectFilled(splitX - barHalf, y, capturedBarW, h,
                        Color32.FromArgb(220, 255, 255, 255));

                    // Handle circle in center of bar
                    float handleR = capturedHandleSize * 0.5f;
                    float cy = y + h * 0.5f;
                    var priCol = Color32.FromArgb(255, (byte)primary.C400.R, (byte)primary.C400.G, (byte)primary.C400.B);
                    canvas.CircleFilled(splitX, cy, handleR, priCol);

                    // Arrow glyphs on handle
                    canvas.SetStrokeColor(Color32.FromArgb(255, 255, 255, 255));
                    canvas.SetStrokeWidth(2f);
                    float arrowH = handleR * 0.4f;
                    float arrowW = handleR * 0.3f;

                    // Left arrow
                    canvas.BeginPath();
                    canvas.MoveTo(splitX - arrowW * 0.3f, cy - arrowH);
                    canvas.LineTo(splitX - arrowW * 1.3f, cy);
                    canvas.LineTo(splitX - arrowW * 0.3f, cy + arrowH);
                    canvas.Stroke();

                    // Right arrow
                    canvas.BeginPath();
                    canvas.MoveTo(splitX + arrowW * 0.3f, cy - arrowH);
                    canvas.LineTo(splitX + arrowW * 1.3f, cy);
                    canvas.LineTo(splitX + arrowW * 0.3f, cy + arrowH);
                    canvas.Stroke();
                }));
        }
    }
}
