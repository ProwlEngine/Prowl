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
    {
        var icons = Origami.Current.Icons;
        string icon = _type switch
        {
            ToastType.Success => icons.Success,
            ToastType.Warning => icons.Warning,
            ToastType.Error   => icons.Danger,
            _                 => icons.Info,
        };
        s_toasts.Add(new ToastEntry { Title = _title, Message = _message, Type = _type, Icon = icon, Duration = _duration, Elapsed = 0 });
    }

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
        public string Icon = "";
        public float Duration;
        public float Elapsed;
    }

    private static readonly List<ToastEntry> s_toasts = [];
    private const float ToastWidth = 300f;
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

        float dt = paper.DeltaTime;
        float screenW = (float)paper.ScreenRect.Size.X;
        float screenH = (float)paper.ScreenRect.Size.Y;
        float yOffset = screenH - metrics.PaddingLarge * 3f;

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

            byte alpha = (byte)(fade * 240);
            var accent = GetAccent(toast.Type, theme);
            var bg = Color.FromArgb(alpha, theme.Neutral.C300.R, theme.Neutral.C300.G, theme.Neutral.C300.B);
            var border = Color.FromArgb(alpha, accent.R, accent.G, accent.B);
            var titleColor = Color.FromArgb(alpha, theme.Ink.C500.R, theme.Ink.C500.G, theme.Ink.C500.B);
            var msgColor = Color.FromArgb(alpha, theme.Ink.C400.R, theme.Ink.C400.G, theme.Ink.C400.B);

            int msgLines = CountLines(toast.Message);
            float lineH = metrics.FontSize + metrics.Spacing;
            float msgH = MathF.Max(metrics.FontSize + metrics.SpacingMedium, msgLines * lineH);
            float toastH = metrics.RowHeight + metrics.Padding + metrics.Spacing + msgH;
            yOffset -= toastH + metrics.SpacingMedium;

            float x = screenW - ToastWidth - metrics.PaddingLarge + slideX;
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
                .Padding(metrics.PaddingSmall, metrics.PaddingLarge, metrics.PaddingSmall, metrics.PaddingSmall).RowBetween(metrics.SpacingLarge)
                .Enter())
            {
                if (!string.IsNullOrEmpty(toast.Icon))
                {
                    float circleSize = toastH - metrics.SpacingLarge;
                    paper.Box($"toast_ico_{i}")
                        .Width(circleSize).Height(circleSize)
                        .Rounded(circleSize * 0.5f)
                        .BackgroundColor(accent)
                        .Text(toast.Icon, font)
                        .TextColor(Color.FromArgb(alpha, 255, 255, 255))
                        .FontSize(metrics.FontSize)
                        .Alignment(TextAlignment.MiddleCenter);
                }

                using (paper.Column($"toast_txt_{i}")
                    .Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
                    .ChildTop(metrics.Padding).ChildBottom(metrics.Padding)
                    .ColBetween(1)
                    .Enter())
                {
                    paper.Box($"toast_t_{i}")
                        .Height(metrics.FontSize + metrics.SpacingMedium)
                        .Text(toast.Title, font).TextColor(titleColor)
                        .FontSize(metrics.FontSize)
                        .Alignment(TextAlignment.MiddleLeft);

                    if (!string.IsNullOrEmpty(toast.Message))
                    {
                        paper.Box($"toast_m_{i}")
                            .Height(msgH)
                            .Text(toast.Message, font).TextColor(msgColor)
                            .FontSize(metrics.FontSizeSmall)
                            .Alignment(TextAlignment.MiddleLeft);
                    }
                }
            }
        }
    }

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
