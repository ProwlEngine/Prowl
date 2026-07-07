// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>Toast severity level, determines accent color and icon.</summary>
public enum ToastType { Info, Success, Warning, Error }

/// <summary>
/// Toast notification system for Origami. Manages the toast queue internally.
/// Use the fluent builder returned by <see cref="Origami.Toast(string)"/> to fire toasts.
/// Call <see cref="Draw"/> once per frame from the host's render loop.
/// </summary>
public sealed class Toasts
{
    // ── Instance (builder) state ─────────────────────────────

    private string _title;
    private string _message = "";
    private ToastType _type = ToastType.Info;
    private float _duration = 3f;

    internal Toasts(string title) => _title = title;

    /// <summary>Set the toast body message.</summary>
    public Toasts Message(string message) { _message = message; return this; }

    /// <summary>Set the toast type/severity (default Info).</summary>
    public Toasts Type(ToastType type) { _type = type; return this; }

    /// <summary>Set toast to Info style.</summary>
    public Toasts Info() { _type = ToastType.Info; return this; }

    /// <summary>Set toast to Success style.</summary>
    public Toasts Success() { _type = ToastType.Success; return this; }

    /// <summary>Set toast to Warning style.</summary>
    public Toasts Warning() { _type = ToastType.Warning; return this; }

    /// <summary>Set toast to Error style (default 5s duration).</summary>
    public Toasts Error() { _type = ToastType.Error; _duration = 5f; return this; }

    /// <summary>Set display duration in seconds (default 3).</summary>
    public Toasts Duration(float seconds) { _duration = seconds; return this; }

    /// <summary>Fire the toast notification.</summary>
    public void Show()
        => s_toasts.Add(new ToastEntry { Title = _title, Message = _message, Type = _type, Duration = _duration, Elapsed = 0 });

    // ── Static convenience shortcuts ────────────────────────

    /// <summary>Fire a toast immediately with the given parameters.</summary>
    public static void Show(string title, string message, ToastType type = ToastType.Info, float duration = 3f)
        => new Toasts(title) { _message = message, _type = type, _duration = duration }.Show();

    /// <summary>Fire an Info toast.</summary>
    public static void Info(string title, string message) => Show(title, message, ToastType.Info);

    /// <summary>Fire a Success toast.</summary>
    public static void Success(string title, string message) => Show(title, message, ToastType.Success);

    /// <summary>Fire a Warning toast.</summary>
    public static void Warning(string title, string message) => Show(title, message, ToastType.Warning);

    /// <summary>Fire an Error toast (5s duration).</summary>
    public static void Error(string title, string message) => Show(title, message, ToastType.Error, 5f);

    // ── Static system (queue + rendering) ────────────────────

    private sealed class ToastEntry
    {
        public string Title = "";
        public string Message = "";
        public ToastType Type;
        public float Duration;
        public float Elapsed;
    }

    private static readonly List<ToastEntry> s_toasts = [];

    // Prototype .w2toast geometry (literal px, matching the Nebula HTML/CSS).
    private const float ToastWidth = 300f;   // >= min-width 240
    private const float BadgeSize = 26f;     // .tic 26x26
    private const float BadgeRadius = 7f;
    private const float CloseSize = 14f;
    private const float PadX = 13f;          // padding 11px 13px
    private const float PadY = 11f;
    private const float Gap = 11f;           // flex gap
    private const float CardRadius = 10f;    // border-radius 10
    private const float StackGap = 9f;       // .w2toast-stack gap 9

    private const float FadeInTime = 0.3f;
    private const float FadeOutTime = 0.5f;
    private const int ToastLayer = Layer.Overlay + 100000;

    /// <summary>Draw all active toasts. Call once per frame from the host render loop.</summary>
    public static void Draw(Paper paper)
    {
        if (s_toasts.Count == 0) return;

        var theme = Origami.Current;
        var metrics = theme.Metrics;
        var font = theme.Font;
        if (font == null) return;
        var titleFace = theme.Medium ?? font;   // .w2toast title is weight 500

        float dt = paper.DeltaTime;
        float screenW = (float)paper.ScreenRect.Size.X;
        float screenH = (float)paper.ScreenRect.Size.Y;
        float yOffset = screenH - metrics.PaddingLarge;

        for (int i = s_toasts.Count - 1; i >= 0; i--)
        {
            var toast = s_toasts[i];
            toast.Elapsed += dt;

            if (toast.Elapsed >= toast.Duration)
            {
                s_toasts.RemoveAt(i);
                continue;
            }

            float fade = 1f;
            if (toast.Elapsed < FadeInTime)
                fade = toast.Elapsed / FadeInTime;
            else if (toast.Elapsed > toast.Duration - FadeOutTime)
                fade = (toast.Duration - toast.Elapsed) / FadeOutTime;

            float slideX = (1f - MathF.Min(1f, toast.Elapsed / 0.2f)) * 40f;

            byte Fa(int a) => (byte)Math.Clamp((int)(a * fade), 0, 255);

            var semantic = GetSemantic(toast.Type, theme);
            bool hasSub = !string.IsNullOrEmpty(toast.Message);

            var bg         = Color.FromArgb(Fa(247), 28, 23, 42);                         // rgba(28,23,42,0.97)
            var border     = Color.FromArgb(Fa(66), 190, 150, 255);                       // bd-strong
            var shadow     = Color.FromArgb(Fa(150), 0, 0, 0);                            // 0 14 40 rgba(0,0,0,.6)
            var titleColor = Color.FromArgb(Fa(255), theme.Ink.C500.R, theme.Ink.C500.G, theme.Ink.C500.B); // t-hi
            var subColor   = Color.FromArgb(Fa(255), theme.Ink.C200.R, theme.Ink.C200.G, theme.Ink.C200.B); // t-lo
            var badgeBg    = Color.FromArgb(Fa(38), semantic.R, semantic.G, semantic.B);  // ~15% alpha
            var iconColor  = Color.FromArgb(Fa(255), semantic.R, semantic.G, semantic.B);

            float mainH = metrics.FontSize + 2f;
            float subH = metrics.FontSizeSmall;
            float textH = mainH + (hasSub ? 2f + subH : 0f);
            float contentH = MathF.Max(BadgeSize, textH);
            float toastH = contentH + PadY * 2f;
            yOffset -= toastH + StackGap;

            float x = screenW - ToastWidth - metrics.PaddingLarge + slideX;

            ToastType capType = toast.Type;
            Color capIcon = iconColor, capClose = subColor;

            using (paper.Row($"toast_{i}")
                .PositionType(PositionType.SelfDirected)
                .Position(x, yOffset)
                .Size(ToastWidth, toastH)
                .BackgroundColor(bg)
                .BorderColor(border).BorderWidth(1)
                .Rounded(CardRadius)
                .DropShadow(0, 14, 40, 0, shadow)
                .Layer(ToastLayer)
                .IsNotInteractable()
                .Padding(PadX, PadX, PadY, PadY).RowBetween(Gap)
                .Enter())
            {
                using (paper.Box($"toast_ico_{i}")
                    .Width(BadgeSize).Height(BadgeSize)
                    .Margin(UnitValue.Auto, UnitValue.Stretch())
                    .Rounded(BadgeRadius)
                    .BackgroundColor(badgeBg)
                    .IsNotInteractable()
                    .Enter())
                {
                    paper.Draw((canvas, rect) => DrawToastGlyph(canvas, rect, capType, capIcon));
                }

                using (paper.Column($"toast_txt_{i}")
                    .Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                    .Margin(UnitValue.Auto, UnitValue.Stretch())
                    .ColBetween(2)
                    .Enter())
                {
                    paper.Box($"toast_t_{i}")
                        .Height(mainH)
                        .Text(toast.Title, titleFace).TextColor(titleColor)
                        .FontSize(metrics.FontSize)
                        .Alignment(TextAlignment.MiddleLeft)
                        .IsNotInteractable();

                    if (hasSub)
                        paper.Box($"toast_m_{i}")
                            .Height(subH)
                            .Text(toast.Message, font).TextColor(subColor)
                            .FontSize(metrics.FontSizeSmall)
                            .Alignment(TextAlignment.MiddleLeft)
                            .IsNotInteractable();
                }

                using (paper.Box($"toast_x_{i}")
                    .Width(CloseSize).Height(CloseSize)
                    .Margin(UnitValue.Auto, UnitValue.Stretch())
                    .IsNotInteractable()
                    .Enter())
                {
                    paper.Draw((canvas, rect) => DrawCloseX(canvas, rect, capClose));
                }
            }
        }
    }

    /// <summary>
    /// Render a static, non-interactive <c>.w2toast</c> card inline in the current layout flow
    /// (for showcases / documentation). Unlike <see cref="Show"/> this does not queue or auto-dismiss.
    /// </summary>
    public static void Preview(Paper paper, string id, string title, ToastType type, string message = "")
    {
        var theme = Origami.Current;
        var metrics = theme.Metrics;
        var font = theme.Font;
        if (font == null) return;
        var titleFace = theme.Medium ?? font;

        var semantic = GetSemantic(type, theme);
        bool hasSub = !string.IsNullOrEmpty(message);

        var bg         = theme.Popover;
        var border     = theme.BorderStrong;
        var shadow     = theme.Shadow;
        var titleColor = theme.Ink.C500;
        var subColor   = theme.Ink.C200;
        var badgeBg    = Color.FromArgb(38, semantic.R, semantic.G, semantic.B);
        var iconColor  = semantic;

        float mainH = metrics.FontSize + 2f;
        float subH = metrics.FontSizeSmall;
        float textH = mainH + (hasSub ? 2f + subH : 0f);
        float contentH = MathF.Max(BadgeSize, textH);
        float toastH = contentH + PadY * 2f;

        using (paper.Row(id)
            .Width(UnitValue.Stretch()).Height(toastH)
            .BackgroundColor(bg)
            .BorderColor(border).BorderWidth(1)
            .Rounded(CardRadius)
            .DropShadow(0, 14, 40, 0, shadow)
            .IsNotInteractable()
            .Padding(PadX, PadX, PadY, PadY).RowBetween(Gap)
            .Enter())
        {
            using (paper.Box($"{id}_ico")
                .Width(BadgeSize).Height(BadgeSize)
                .Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
                .Rounded(BadgeRadius)
                .BackgroundColor(badgeBg)
                .IsNotInteractable()
                .Enter())
            {
                paper.Draw((canvas, rect) => DrawToastGlyph(canvas, rect, type, iconColor));
            }

            using (paper.Column($"{id}_txt")
                .Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                .Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
                .ColBetween(2)
                .Enter())
            {
                paper.Box($"{id}_t")
                    .Height(mainH)
                    .Text(title, titleFace).TextColor(titleColor)
                    .FontSize(metrics.FontSize)
                    .Alignment(TextAlignment.MiddleLeft)
                    .IsNotInteractable();

                if (hasSub)
                    paper.Box($"{id}_m")
                        .Height(subH)
                        .Text(message, font).TextColor(subColor)
                        .FontSize(metrics.FontSizeSmall)
                        .Alignment(TextAlignment.MiddleLeft)
                        .IsNotInteractable();
            }

            using (paper.Box($"{id}_x")
                .Width(CloseSize).Height(CloseSize)
                .Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
                .IsNotInteractable()
                .Enter())
            {
                paper.Draw((canvas, rect) => DrawCloseX(canvas, rect, subColor));
            }
        }
    }

    /// <summary>Full-strength semantic colour for a toast type (#4ade80 / #fbbf24 / #fb7185 / #60a5fa).</summary>
    private static Color GetSemantic(ToastType type, OrigamiTheme theme) => type switch
    {
        ToastType.Success => theme.Green.C500,
        ToastType.Warning => theme.Amber.C500,
        ToastType.Error   => theme.Red.C500,
        _                 => theme.Blue.C500,
    };

    /// <summary>Draw the .tic vector glyph (check / x / info-dot / bang) centred in the badge.</summary>
    private static void DrawToastGlyph(Canvas canvas, Rect rect, ToastType type, Color color)
    {
        float cx = (float)(rect.Min.X + rect.Size.X * 0.5);
        float cy = (float)(rect.Min.Y + rect.Size.Y * 0.5);

        canvas.SaveState();
        canvas.SetStrokeColor(color);
        canvas.SetStrokeWidth(1.9f);
        canvas.SetStrokeCap(EndCapStyle.Round);

        switch (type)
        {
            case ToastType.Success:
                canvas.BeginPath();
                canvas.MoveTo(cx - 3.5f, cy);
                canvas.LineTo(cx - 1f, cy + 2.7f);
                canvas.LineTo(cx + 3.9f, cy - 2.9f);
                canvas.Stroke();
                break;

            case ToastType.Error:
                canvas.BeginPath();
                canvas.MoveTo(cx - 3.3f, cy - 3.3f); canvas.LineTo(cx + 3.3f, cy + 3.3f);
                canvas.MoveTo(cx + 3.3f, cy - 3.3f); canvas.LineTo(cx - 3.3f, cy + 3.3f);
                canvas.Stroke();
                break;

            case ToastType.Warning:                    // "!" — stem then dot
                canvas.BeginPath();
                canvas.MoveTo(cx, cy - 4.2f); canvas.LineTo(cx, cy + 1.2f);
                canvas.Stroke();
                canvas.CircleFilled(cx, cy + 3.9f, 1.3f, color);
                break;

            default:                                   // Info "i" — dot then stem
                canvas.CircleFilled(cx, cy - 3.7f, 1.3f, color);
                canvas.BeginPath();
                canvas.MoveTo(cx, cy - 0.6f); canvas.LineTo(cx, cy + 4.2f);
                canvas.Stroke();
                break;
        }

        canvas.RestoreState();
    }

    /// <summary>Draw the trailing close 'x' as two crossing strokes in t-lo.</summary>
    private static void DrawCloseX(Canvas canvas, Rect rect, Color color)
    {
        float cx = (float)(rect.Min.X + rect.Size.X * 0.5);
        float cy = (float)(rect.Min.Y + rect.Size.Y * 0.5);

        canvas.SaveState();
        canvas.SetStrokeColor(color);
        canvas.SetStrokeWidth(1.5f);
        canvas.SetStrokeCap(EndCapStyle.Round);
        canvas.BeginPath();
        canvas.MoveTo(cx - 3.2f, cy - 3.2f); canvas.LineTo(cx + 3.2f, cy + 3.2f);
        canvas.MoveTo(cx + 3.2f, cy - 3.2f); canvas.LineTo(cx - 3.2f, cy + 3.2f);
        canvas.Stroke();
        canvas.RestoreState();
    }
}
