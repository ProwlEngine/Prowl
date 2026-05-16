// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>Toast severity level, determines accent color and icon.</summary>
public enum ToastType { Info, Success, Warning, Error }

/// <summary>
/// Static toast notification system for Origami. Toasts stack in the bottom-right
/// corner, fade in/out, and auto-dismiss after a configurable duration.
/// Renders on top of everything (Layer.Overlay + 100000) with no interaction.
/// </summary>
public static class Toasts
{
    private sealed class ToastEntry
    {
        public string Title;
        public string Message;
        public ToastType Type;
        public string Icon;
        public float Duration;
        public float Elapsed;
    }

    private static readonly List<ToastEntry> _toasts = [];
    private const float ToastWidth = 300f;
    private const float Spacing = 6f;
    private const float FadeInTime = 0.3f;
    private const float FadeOutTime = 0.5f;
    private const int ToastLayer = Layer.Overlay + 100000;

    // ── Show ─────────────────────────────────────────────────

    /// <summary>Show a toast notification.</summary>
    public static void Show(string title, string message, ToastType type = ToastType.Info, float duration = 3f)
    {
        var icons = Origami.Current.Icons;
        string icon = type switch
        {
            ToastType.Success => icons.Success,
            ToastType.Warning => icons.Warning,
            ToastType.Error   => icons.Danger,
            _                 => icons.Info,
        };
        _toasts.Add(new ToastEntry { Title = title, Message = message, Type = type, Icon = icon, Duration = duration, Elapsed = 0 });
    }

    public static void Info(string title, string message) => Show(title, message, ToastType.Info);
    public static void Success(string title, string message) => Show(title, message, ToastType.Success);
    public static void Warning(string title, string message) => Show(title, message, ToastType.Warning);
    public static void Error(string title, string message) => Show(title, message, ToastType.Error, 5f);

    // ── Draw ─────────────────────────────────────────────────

    /// <summary>Draw all active toasts. Call once per frame.</summary>
    public static void Draw(Paper paper)
    {
        if (_toasts.Count == 0) return;

        var theme = Origami.Current;
        var font = theme.Font;
        if (font == null) return;

        float dt = paper.DeltaTime;
        float screenW = (float)paper.ScreenRect.Size.X;
        float screenH = (float)paper.ScreenRect.Size.Y;
        float yOffset = screenH - 36f;

        for (int i = _toasts.Count - 1; i >= 0; i--)
        {
            var toast = _toasts[i];
            toast.Elapsed += dt;

            if (toast.Elapsed >= toast.Duration)
            {
                _toasts.RemoveAt(i);
                continue;
            }

            // Fade progress (0..1)
            float fade = 1f;
            if (toast.Elapsed < FadeInTime)
                fade = toast.Elapsed / FadeInTime;
            else if (toast.Elapsed > toast.Duration - FadeOutTime)
                fade = (toast.Duration - toast.Elapsed) / FadeOutTime;

            // Slide in from right
            float slideX = (1f - MathF.Min(1f, toast.Elapsed / 0.2f)) * 40f;

            byte alpha = (byte)(fade * 240);
            var accent = GetAccent(toast.Type, theme);
            var bg = Color.FromArgb(alpha, theme.Neutral.C300.R, theme.Neutral.C300.G, theme.Neutral.C300.B);
            var border = Color.FromArgb(alpha, accent.R, accent.G, accent.B);
            var titleColor = Color.FromArgb(alpha, theme.Ink.C500.R, theme.Ink.C500.G, theme.Ink.C500.B);
            var msgColor = Color.FromArgb(alpha, theme.Ink.C400.R, theme.Ink.C400.G, theme.Ink.C400.B);
            var iconColor = Color.FromArgb(alpha, accent.R, accent.G, accent.B);

            // Size
            int msgLines = CountLines(toast.Message);
            float msgH = MathF.Max(18f, msgLines * 16f);
            float toastH = 34f + msgH;
            yOffset -= toastH + Spacing;

            float x = screenW - ToastWidth - 16f + slideX;

            float pill = toastH * 0.5f;

            using (paper.Row($"toast_{i}")
                .PositionType(PositionType.SelfDirected)
                .Position(x, yOffset)
                .Size(ToastWidth, toastH)
                .BackgroundColor(bg)
                .BorderColor(border).BorderWidth(1)
                .Rounded(pill)
                .BoxShadow(0, 3, 16, -2, Color.FromArgb((int)(fade * 80), 0, 0, 0))
                .Layer(ToastLayer)
                .IsNotInteractable()
                .Padding(4, 12, 4, 4).RowBetween(8)
                .Enter())
            {
                // Circle icon badge
                if (!string.IsNullOrEmpty(toast.Icon))
                {
                    float circleSize = toastH - 8;
                    paper.Box($"toast_ico_{i}")
                        .Width(circleSize).Height(circleSize)
                        .Rounded(circleSize * 0.5f)
                        .BackgroundColor(accent)
                        .Text(toast.Icon, font)
                        .TextColor(Color.FromArgb(alpha, 255, 255, 255))
                        .FontSize(theme.Metrics.FontSize)
                        .Alignment(TextAlignment.MiddleCenter);
                }

                // Text
                using (paper.Column($"toast_txt_{i}")
                    .Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
                    .ChildTop(6).ChildBottom(6)
                    .ColBetween(1)
                    .Enter())
                {
                    paper.Box($"toast_t_{i}")
                        .Height(18)
                        .Text(toast.Title, font).TextColor(titleColor)
                        .FontSize(theme.Metrics.FontSize)
                        .Alignment(TextAlignment.MiddleLeft);

                    if (!string.IsNullOrEmpty(toast.Message))
                    {
                        paper.Box($"toast_m_{i}")
                            .Height(msgH)
                            .Text(toast.Message, font).TextColor(msgColor)
                            .FontSize(theme.Metrics.FontSize - 2)
                            .Alignment(TextAlignment.MiddleLeft);
                    }
                }
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────

    private static int CountLines(string s)
    {
        if (string.IsNullOrEmpty(s)) return 1;
        int n = 1;
        for (int i = 0; i < s.Length; i++)
            if (s[i] == '\n') n++;
        return n;
    }

    private static Color GetAccent(ToastType type, OrigamiTheme theme) => type switch
    {
        ToastType.Success => Color.FromArgb(255, theme.Green.C400.R, theme.Green.C400.G, theme.Green.C400.B),
        ToastType.Warning => Color.FromArgb(255, theme.Amber.C400.R, theme.Amber.C400.G, theme.Amber.C400.B),
        ToastType.Error   => Color.FromArgb(255, theme.Red.C400.R, theme.Red.C400.G, theme.Red.C400.B),
        _                 => Color.FromArgb(255, theme.Blue.C400.R, theme.Blue.C400.G, theme.Blue.C400.B),
    };
}
