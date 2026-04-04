using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Widgets;

public enum ToastType { Info, Success, Warning, Error }

public class ToastEntry
{
    public string Title;
    public string Message;
    public ToastType Type;
    public float Duration;
    public float Elapsed;

    public ToastEntry(string title, string message, ToastType type, float duration)
    {
        Title = title;
        Message = message;
        Type = type;
        Duration = duration;
        Elapsed = 0;
    }
}

/// <summary>
/// Toast notification system. Toasts stack in the bottom-right corner and fade out.
/// </summary>
public static class Toasts
{
    private static readonly List<ToastEntry> _toasts = new();
    private const float ToastWidth = 280f;
    private const float ToastSpacing = 6f;
    private const float FadeTime = 0.5f;

    public static void Show(string title, string message, ToastType type = ToastType.Info, float duration = 3f)
    {
        _toasts.Add(new ToastEntry(title, message, type, duration));
    }

    public static void Info(string title, string message) => Show(title, message, ToastType.Info);
    public static void Success(string title, string message) => Show(title, message, ToastType.Success);
    public static void Warning(string title, string message) => Show(title, message, ToastType.Warning);
    public static void Error(string title, string message) => Show(title, message, ToastType.Error, 5f);

    /// <summary>
    /// Draw all active toasts. Call from EditorApplication.EndGui.
    /// </summary>
    public static void Draw(Paper paper, float deltaTime)
    {
        if (_toasts.Count == 0) return;
        var font = EditorTheme.DefaultFont;

        int w = Prowl.Runtime.Window.InternalWindow.Size.X;
        int h = Prowl.Runtime.Window.InternalWindow.Size.Y;

        float yOffset = h - 30f; // Start from bottom

        for (int i = _toasts.Count - 1; i >= 0; i--)
        {
            var toast = _toasts[i];
            toast.Elapsed += deltaTime;

            if (toast.Elapsed >= toast.Duration)
            {
                _toasts.RemoveAt(i);
                continue;
            }

            // Fade
            float fadeProgress = 1f;
            if (toast.Elapsed < 0.3f)
                fadeProgress = toast.Elapsed / 0.3f; // Fade in
            else if (toast.Elapsed > toast.Duration - FadeTime)
                fadeProgress = (toast.Duration - toast.Elapsed) / FadeTime; // Fade out

            int alpha = (int)(fadeProgress * 230);
            var accentColor = GetAccentColor(toast.Type);
            var bgColor = Color.FromArgb(alpha, 35, 35, 38);
            var borderColor = Color.FromArgb(alpha, accentColor.R, accentColor.G, accentColor.B);

            float toastH = 52f;
            yOffset -= toastH + ToastSpacing;

            using (paper.Row($"toast_{i}")
                .PositionType(PositionType.SelfDirected)
                .Position(w - ToastWidth - 16, yOffset)
                .Size(ToastWidth, toastH)
                .BackgroundColor(bgColor)
                .BorderColor(borderColor).BorderWidth(1)
                .Rounded(6)
                .Layer(Layer.Overlay)
                .Enter())
            {
                // Accent bar
                paper.Box($"toast_accent_{i}")
                    .Width(4).Height(UnitValue.Stretch())
                    .BackgroundColor(accentColor)
                    .RoundedLeft(6);

                // Text content
                if (font != null)
                {
                    using (paper.Column($"toast_text_{i}")
                        .Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
                        .ChildLeft(8).ChildTop(6).ChildBottom(6)
                        .RowBetween(2)
                        .IsNotInteractable()
                        .Enter())
                    {
                        var titleColor = Color.FromArgb(alpha, 220, 220, 220);
                        var msgColor = Color.FromArgb(alpha, 160, 160, 160);

                        paper.Box($"toast_title_{i}")
                            .Height(18)
                            .Text(toast.Title, font)
                            .TextColor(titleColor)
                            .FontSize(EditorTheme.FontSize);

                        paper.Box($"toast_msg_{i}")
                            .Height(18)
                            .Text(toast.Message, font)
                            .TextColor(msgColor)
                            .FontSize(EditorTheme.FontSize - 2);
                    }
                }
            }
        }
    }

    private static Color GetAccentColor(ToastType type) => type switch
    {
        ToastType.Success => Color.FromArgb(255, 60, 180, 75),
        ToastType.Warning => Color.FromArgb(255, 220, 180, 50),
        ToastType.Error => Color.FromArgb(255, 200, 60, 60),
        _ => Color.FromArgb(255, 51, 122, 183),
    };
}
