// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Scribe;
using Prowl.Vector;
using Prowl.Vector.Spatial;

using Color = System.Drawing.Color;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.OrigamiUI;

/// <summary>Direction the chat bubble tail points toward.</summary>
public enum BubbleTailDirection { Left, Right, Top, Bottom }

/// <summary>
/// Fluent builder for a chat bubble widget. Renders a speech-bubble shape with an
/// optional avatar, header, footer, and caller-provided content body.
/// The bubble shape (including the tail) is drawn via Quill's path API.
/// </summary>
public sealed class ChatBubbleBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private readonly Action<Paper> _content;

    private BubbleTailDirection _tail = BubbleTailDirection.Left;
    private float _maxWidth = 350f;

    // Avatar
    private object? _avatarTexture;
    private string? _avatarInitials;
    private Color? _avatarColor;
    private float _avatarSize = 32f;

    // Header / Footer
    private string? _header;
    private string? _footer;
    private Color? _headerColor;

    // Colors
    private Color? _bgColor;
    private Color? _textColor;
    private OrigamiVariant _variant = OrigamiVariant.Default;

    // Tail
    private float _tailSize = 8f;
    private bool _showTail = true;

    internal ChatBubbleBuilder(Paper paper, string id, Action<Paper> content, OrigamiTheme theme)
    {
        _paper = paper;
        _id = id;
        _content = content;
        _theme = theme;
    }

    // ── Direction ──────────────────────────────────────────────

    public ChatBubbleBuilder Tail(BubbleTailDirection dir) { _tail = dir; return this; }
    public ChatBubbleBuilder TailLeft() => Tail(BubbleTailDirection.Left);
    public ChatBubbleBuilder TailRight() => Tail(BubbleTailDirection.Right);
    public ChatBubbleBuilder TailTop() => Tail(BubbleTailDirection.Top);
    public ChatBubbleBuilder TailBottom() => Tail(BubbleTailDirection.Bottom);
    public ChatBubbleBuilder NoTail() { _showTail = false; return this; }
    public ChatBubbleBuilder TailSize(float size) { _tailSize = MathF.Max(4, size); return this; }

    // ── Sizing ─────────────────────────────────────────────────

    public ChatBubbleBuilder MaxWidth(float maxW) { _maxWidth = maxW; return this; }

    // ── Avatar ─────────────────────────────────────────────────

    public ChatBubbleBuilder Avatar(object texture, float size = 32f)
    {
        _avatarTexture = texture; _avatarSize = size; return this;
    }

    public ChatBubbleBuilder Avatar(string initials, Color color, float size = 32f)
    {
        _avatarInitials = initials; _avatarColor = color; _avatarSize = size; return this;
    }

    // ── Header / Footer ────────────────────────────────────────

    public ChatBubbleBuilder Header(string text, Color? color = null)
    {
        _header = text; _headerColor = color; return this;
    }

    public ChatBubbleBuilder Footer(string text) { _footer = text; return this; }

    // ── Colors ─────────────────────────────────────────────────

    public ChatBubbleBuilder Background(Color color) { _bgColor = color; return this; }
    public ChatBubbleBuilder TextColor(Color color) { _textColor = color; return this; }
    public ChatBubbleBuilder Variant(OrigamiVariant variant) { _variant = variant; return this; }
    public ChatBubbleBuilder Primary() => Variant(OrigamiVariant.Primary);
    public ChatBubbleBuilder Success() => Variant(OrigamiVariant.Success);
    public ChatBubbleBuilder Info() => Variant(OrigamiVariant.Info);
    public ChatBubbleBuilder Warning() => Variant(OrigamiVariant.Warning);
    public ChatBubbleBuilder Danger() => Variant(OrigamiVariant.Danger);

    // ── Terminator ─────────────────────────────────────────────

    public void Show()
    {
        var m = _theme.Metrics;
        var font = _theme.Font;
        var ink = _theme.Ink;
        var ramp = _theme.Get(_variant);

        Color bubbleBg = _bgColor ?? (_variant == OrigamiVariant.Default
            ? _theme.Neutral.C400 : ramp.C300);
        Color headerCol = _headerColor ?? (_variant == OrigamiVariant.Default ? ink.C400 : ramp.C600);
        float rounding = m.ContainerRounding;
        float tail = _showTail ? _tailSize : 0;
        bool hasAvatar = _avatarTexture != null || _avatarInitials != null;
        bool avatarOnLeft = hasAvatar && _tail == BubbleTailDirection.Left;
        bool avatarOnRight = hasAvatar && _tail == BubbleTailDirection.Right;

        // Outer row: [avatar] [bubble] or [bubble] [avatar]
        using (_paper.Row($"{_id}_row").Width(UnitValue.Auto).Height(UnitValue.Auto)
            .RowBetween(m.SpacingLarge).Enter())
        {
            if (avatarOnLeft)
                DrawAvatar(font, m);

            // The bubble: a column that sizes to content, with canvas-drawn background
            float marginL = _showTail && _tail == BubbleTailDirection.Left ? tail : 0;
            float marginR = _showTail && _tail == BubbleTailDirection.Right ? tail : 0;
            float marginT = _showTail && _tail == BubbleTailDirection.Top ? tail : 0;
            float marginB = _showTail && _tail == BubbleTailDirection.Bottom ? tail : 0;

            // Capture for lambda
            var capturedBg = bubbleBg;
            var capturedTail = _tail;
            var capturedShowTail = _showTail;
            var capturedTailSize = _tailSize;
            var capturedRounding = rounding;

            using (_paper.Column($"{_id}_wrap")
                .Width(UnitValue.Auto).MaxWidth(_maxWidth)
                .Height(UnitValue.Auto)
                .Margin(marginL, marginR, marginT, marginB)
                .Padding(m.PaddingLarge, m.PaddingLarge, m.Padding + 2, m.Padding + 2)
                .ColBetween(m.SpacingSmall)
                .OnPostLayout((handle, rect) => _paper.Draw(ref handle, (canvas, r) =>
                {
                    // Draw bubble shape behind all content
                    float bx = (float)r.Min.X - marginL;
                    float by = (float)r.Min.Y - marginT;
                    float bw = (float)r.Size.X + marginL + marginR;
                    float bh = (float)r.Size.Y + marginT + marginB;
                    DrawBubbleShape(canvas, bx + marginL, by + marginT,
                        bw - marginL - marginR, bh - marginT - marginB,
                        capturedRounding, capturedBg, capturedTail, capturedShowTail, capturedTailSize);
                }))
                .Enter())
            {
                // Header
                if (!string.IsNullOrEmpty(_header) && font != null)
                {
                    _paper.Box($"{_id}_hdr")
                        .Width(UnitValue.Auto).Height(UnitValue.Auto)
                        .IsNotInteractable()
                        .Text(_header, font).TextColor(headerCol)
                        .FontSize(m.FontSizeSmall)
                        .Alignment(TextAlignment.Left);
                }

                // User content
                _content(_paper);

                // Footer
                if (!string.IsNullOrEmpty(_footer) && font != null)
                {
                    _paper.Box($"{_id}_ftr")
                        .Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                        .IsNotInteractable()
                        .Text(_footer, font).TextColor(ink.C300)
                        .FontSize(m.FontSizeSmall)
                        .Alignment(TextAlignment.Right);
                }
            }

            if (avatarOnRight)
                DrawAvatar(font, m);
        }
    }

    private void DrawAvatar(FontFile? font, OrigamiMetrics m)
    {
        float size = _avatarSize;

        if (_avatarTexture != null)
        {
            var capturedTex = _avatarTexture;
            _paper.Box($"{_id}_av")
                .Width(size).Height(size)
                .IsNotInteractable()
                .OnPostLayout((handle, rect) => _paper.Draw(ref handle, (canvas, r) =>
                {
                    float ax = (float)r.Min.X, ay = (float)r.Min.Y, aw = (float)r.Size.X;
                    float rad = aw * 0.5f;
                    canvas.SetBrushTexture(capturedTex);
                    canvas.SetBrushTextureTransform(
                        Transform2D.CreateTranslation(ax, ay) * Transform2D.CreateScale(aw, aw));
                    canvas.CircleFilled(ax + rad, ay + rad, rad, Color32.FromArgb(255, 255, 255, 255));
                    canvas.ClearBrushTexture();
                }));
        }
        else if (_avatarInitials != null && font != null)
        {
            var col = _avatarColor ?? _theme.Primary.C400;
            var initials = _avatarInitials;
            var fontSize = m.FontSize;
            _paper.Box($"{_id}_av")
                .Width(size).Height(size)
                .IsNotInteractable()
                .OnPostLayout((handle, rect) => _paper.Draw(ref handle, (canvas, r) =>
                {
                    float ax = (float)r.Min.X, ay = (float)r.Min.Y, aw = (float)r.Size.X;
                    float rad = aw * 0.5f, cx = ax + rad, cy = ay + rad;
                    canvas.CircleFilled(cx, cy, rad,
                        Color32.FromArgb(255, (byte)col.R, (byte)col.G, (byte)col.B));
                    var ts = canvas.MeasureText(initials, fontSize, font);
                    canvas.DrawText(initials, cx - (float)ts.X * 0.5f, cy - (float)ts.Y * 0.5f,
                        Color32.FromArgb(255, 255, 255, 255), fontSize, font);
                }));
        }
    }

    private static void DrawBubbleShape(Canvas canvas, float x, float y, float w, float h,
        float rounding, Color bg, BubbleTailDirection tail, bool showTail, float tailSize)
    {
        float r = MathF.Min(rounding, MathF.Min(w, h) * 0.4f);
        float k = 0.5522847498f;

        canvas.SetFillColor(Color32.FromArgb((byte)bg.A, (byte)bg.R, (byte)bg.G, (byte)bg.B));
        canvas.BeginPath();

        canvas.MoveTo(x + r, y);

        // Top edge
        if (showTail && tail == BubbleTailDirection.Top)
        {
            float tx = x + w * 0.5f - tailSize * 0.5f;
            canvas.LineTo(tx, y);
            canvas.LineTo(tx + tailSize * 0.5f, y - tailSize);
            canvas.LineTo(tx + tailSize, y);
        }
        canvas.LineTo(x + w - r, y);

        // Top-right corner
        canvas.BezierCurveTo(x + w - r * (1 - k), y, x + w, y + r * (1 - k), x + w, y + r);

        // Right edge
        if (showTail && tail == BubbleTailDirection.Right)
        {
            float ty = y + h * 0.3f;
            canvas.LineTo(x + w, ty);
            canvas.LineTo(x + w + tailSize, ty + tailSize * 0.5f);
            canvas.LineTo(x + w, ty + tailSize);
        }
        canvas.LineTo(x + w, y + h - r);

        // Bottom-right corner
        canvas.BezierCurveTo(x + w, y + h - r * (1 - k), x + w - r * (1 - k), y + h, x + w - r, y + h);

        // Bottom edge
        if (showTail && tail == BubbleTailDirection.Bottom)
        {
            float tx = x + w * 0.5f - tailSize * 0.5f;
            canvas.LineTo(tx + tailSize, y + h);
            canvas.LineTo(tx + tailSize * 0.5f, y + h + tailSize);
            canvas.LineTo(tx, y + h);
        }
        canvas.LineTo(x + r, y + h);

        // Bottom-left corner
        canvas.BezierCurveTo(x + r * (1 - k), y + h, x, y + h - r * (1 - k), x, y + h - r);

        // Left edge
        if (showTail && tail == BubbleTailDirection.Left)
        {
            float ty = y + h * 0.3f + tailSize;
            canvas.LineTo(x, ty);
            canvas.LineTo(x - tailSize, ty - tailSize * 0.5f);
            canvas.LineTo(x, ty - tailSize);
        }
        canvas.LineTo(x, y + r);

        // Top-left corner
        canvas.BezierCurveTo(x, y + r * (1 - k), x + r * (1 - k), y, x + r, y);

        canvas.ClosePath();
        canvas.FillComplexAA();
    }
}
