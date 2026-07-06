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

        bool me = _variant != OrigamiVariant.Default;          // saturated variant = own message
        Color solidBg = _bgColor ?? _theme.Neutral.C500;       // "them": raised surface
        Color gradTop = ramp.C500, gradBot = ramp.C600;        // "me": accent gradient
        Color border = _theme.Neutral.C200;
        bool drawBorder = !me && _bgColor == null;
        Color headerCol = _headerColor ?? (me ? ramp.C700 : ink.C400);
        Color footerCol = me ? Color.FromArgb(180, ink.C600.R, ink.C600.G, ink.C600.B) : ink.C300;

        bool hasAvatar = _avatarTexture != null || _avatarInitials != null;
        bool avatarOnLeft = hasAvatar && _tail == BubbleTailDirection.Left;
        bool avatarOnRight = hasAvatar && _tail == BubbleTailDirection.Right;

        // Corner radii: one corner is cut short to read as the "tail" (prototype has no pointer triangle).
        const float radius = 14f, tailR = 5f;
        float tl = radius, tr = radius, br = radius, bl = radius;
        if (_showTail)
        {
            if (_tail == BubbleTailDirection.Left) bl = tailR;
            else if (_tail == BubbleTailDirection.Right) br = tailR;
            else if (_tail == BubbleTailDirection.Top) tl = tailR;
            else br = tailR;
        }

        // Outer row: [avatar] [bubble] (them) or right-aligned [bubble] (me). Avatar carries the gap.
        var row = _paper.Row($"{_id}_row").Width(UnitValue.Auto).Height(UnitValue.Auto);
        if (_tail == BubbleTailDirection.Right) row.Margin(UnitValue.Stretch(), 0, 0, 0);
        using (row.Enter())
        {
            if (avatarOnLeft)
                DrawAvatar(font, m);

            bool capMe = me; bool capBorder = drawBorder;
            Color capBg = solidBg, capTop = gradTop, capBot = gradBot, capBd = border;
            float cTl = tl, cTr = tr, cBr = br, cBl = bl;

            using (_paper.Column($"{_id}_wrap")
                .Width(UnitValue.Auto).MaxWidth(_maxWidth)
                .Height(UnitValue.Auto)
                .Padding(13, 13, 9, 9)
                .ColBetween(m.SpacingSmall)
                .OnPostLayout((handle, rect) => _paper.Draw(ref handle, (canvas, r) =>
                {
                    float x = (float)r.Min.X, y = (float)r.Min.Y, w = (float)r.Size.X, h = (float)r.Size.Y;
                    if (capMe)
                    {
                        canvas.SaveState();
                        canvas.SetLinearBrush(x, y, x + w, y + h, capTop, capBot);
                        canvas.BeginPath();
                        canvas.RoundedRect(x, y, w, h, cTl, cTr, cBr, cBl);
                        canvas.Fill();
                        canvas.RestoreState();
                    }
                    else
                    {
                        canvas.RoundedRectFilled(x, y, w, h, cTl, cTr, cBr, cBl, capBg);
                        if (capBorder)
                        {
                            canvas.SaveState();
                            canvas.SetStrokeColor(capBd);
                            canvas.SetStrokeWidth(1f);
                            canvas.BeginPath();
                            canvas.RoundedRect(x + 0.5f, y + 0.5f, w - 1f, h - 1f, cTl, cTr, cBr, cBl);
                            canvas.Stroke();
                            canvas.RestoreState();
                        }
                    }
                }))
                .Enter())
            {
                if (!string.IsNullOrEmpty(_header) && font != null)
                {
                    _paper.Box($"{_id}_hdr")
                        .Width(UnitValue.Auto).Height(UnitValue.Auto)
                        .IsNotInteractable()
                        .Text(_header, font).TextColor(headerCol)
                        .FontSize(m.FontSizeSmall)
                        .Alignment(TextAlignment.Left);
                }

                _content(_paper);

                if (!string.IsNullOrEmpty(_footer) && font != null)
                {
                    _paper.Box($"{_id}_ftr")
                        .Width(UnitValue.Stretch()).Height(UnitValue.Auto).Margin(0, 0, 2, 0)
                        .IsNotInteractable()
                        .Text(_footer, font).TextColor(footerCol)
                        .FontSize(m.FontSizeSmall - 1f)
                        .Alignment(TextAlignment.Left);
                }
            }

            if (avatarOnRight)
                DrawAvatar(font, m);
        }
    }

    private void DrawAvatar(FontFile? font, OrigamiMetrics m)
    {
        float size = _avatarSize;
        // Gap between the avatar and the bubble (RowBetween is overridden by the stretch margins below).
        float gap = m.SpacingLarge;
        float aml = _tail == BubbleTailDirection.Right ? gap : 0f;
        float amr = _tail == BubbleTailDirection.Left ? gap : 0f;

        if (_avatarTexture != null)
        {
            var capturedTex = _avatarTexture;
            _paper.Box($"{_id}_av")
                .Width(size).Height(size)
                .Margin(aml, amr, UnitValue.Stretch(), 0) // bottom-align + gap to the bubble
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
                .Margin(aml, amr, UnitValue.Stretch(), 0) // bottom-align + gap to the bubble
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
}
