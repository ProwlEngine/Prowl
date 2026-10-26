// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI;

/// <summary>
/// While the game runs, dims the editor around the Game view and rings it, so play mode is obvious
/// without washing the whole editor in colour. A short callout next to the Game view explains what
/// play mode means each time play starts. The dim never takes input, so the rest of the editor stays usable.
/// </summary>
public static class PlayModeSpotlight
{
    private const float HintDuration = 6f;
    private const float CardW = 300f;
    private const float CardH = 150f;
    private const float Gap = 14f;

    // Kept from the last frame the Game view showed, so the spotlight can fade out where it was.
    private static (float x, float y, float w, float h) s_rect;
    private static bool s_gameViewDrawn;

    private static float s_alpha, s_time, s_hintTime;
    private static bool s_wasPlaying, s_hintDismissed;

    /// <summary> Called by the Game view each frame it draws, so a Game view behind another tab isn't spotlit. </summary>
    public static void MarkGameViewDrawn() => s_gameViewDrawn = true;

    private static float Ease(float t) => 1f - MathF.Pow(1f - Math.Clamp(t, 0f, 1f), 3f);

    /// <summary> Draws the dim, the ring and the callout. Call after the editor is drawn, before Origami's overlays. </summary>
    public static void Draw(Paper paper, float dt, Vector.Rect? gameView, float headerHeight)
    {
        bool visible = s_gameViewDrawn && gameView is { } g && g.Size.X > 0 && g.Size.Y > 0;
        s_gameViewDrawn = false;
        if (visible)
            s_rect = ((float)gameView!.Value.Min.X, (float)gameView.Value.Min.Y, (float)gameView.Value.Size.X, (float)gameView.Value.Size.Y);

        bool playing = Runtime.Application.IsPlaying;
        if (playing && !s_wasPlaying) { s_hintTime = 0f; s_hintDismissed = false; }
        s_wasPlaying = playing;

        PlayModePreferences prefs = Preferences.Get<PlayModePreferences>();
        bool on = playing && visible && prefs.SpotlightGameView;
        s_alpha = Math.Clamp(s_alpha + (on ? dt : -dt) * 6f, 0f, 1f);
        if (s_alpha <= 0f) return;

        s_time += dt;
        if (playing) s_hintTime += dt;

        float W = (float)paper.ScreenRect.Size.X, H = (float)paper.ScreenRect.Size.Y;
        float appear = Ease(s_alpha);
        var (hx, hy, hw, hh) = s_rect;

        // Four rects around the hole. The header stays undimmed so Stop / Pause stay easy to find.
        Color back = Color.FromArgb((int)(Math.Clamp(prefs.SpotlightDim, 0f, 1f) * 255 * appear), 4, 3, 9);
        float top = Math.Min(headerHeight, hy);
        Dim(paper, "pms_t", 0, top, W, hy - top, back);
        Dim(paper, "pms_b", 0, hy + hh, W, H - (hy + hh), back);
        Dim(paper, "pms_l", 0, hy, hx, hh, back);
        Dim(paper, "pms_r", hx + hw, hy, W - (hx + hw), hh, back);

        // Accent ring (amber while paused) on the window's own edge, with a slow pulse.
        Color ringC = Runtime.Application.IsPaused ? EditorTheme.Amber400 : EditorTheme.Accent;
        float pulse = 0.55f + 0.45f * MathF.Sin(s_time * 2.4f);
        paper.Box("pms_ring").PositionType(PositionType.SelfDirected).Position(hx, hy).Size(hw, hh)
            .Layer(Layer.Overlay).Rounded(Origami.Current.Metrics.ContainerRounding).IsNotInteractable()
            .BorderColor(Color.FromArgb((int)(230 * appear), ringC)).BorderWidth(2)
            .Glow(0, 0, 22, 2, Color.FromArgb((int)(140 * pulse * appear), ringC));

        if (prefs.ShowPlayModeHint && !s_hintDismissed && s_hintTime < HintDuration + 0.4f)
        {
            float fade = appear * Math.Clamp((HintDuration + 0.4f - s_hintTime) / 0.4f, 0f, 1f);
            DrawCallout(paper, hx, hy, hw, hh, W, H, headerHeight, fade);
        }
    }

    private static void Dim(Paper paper, string id, float x, float y, float w, float h, Color c)
    {
        if (w <= 0 || h <= 0) return;
        paper.Box(id).PositionType(PositionType.SelfDirected).Position(x, y).Size(w, h)
            .Layer(Layer.Overlay).BackgroundColor(c).IsNotInteractable();
    }

    private static void DrawCallout(Paper paper, float hx, float hy, float hw, float hh, float W, float H, float minY, float fade)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;
        Scribe.FontFile semi = EditorTheme.FontSemiBold ?? font;
        OrigamiMetrics m = Origami.Current.Metrics;

        // Beside the Game view where there's room (right, left, below, above), else over its top-right corner.
        float cx, cy;
        float midY = Math.Clamp(hy + hh * 0.5f - CardH * 0.5f, minY + 8f, H - CardH - 8f);
        if (hx + hw + Gap + CardW < W) { cx = hx + hw + Gap; cy = midY; }
        else if (hx - Gap - CardW > 0) { cx = hx - Gap - CardW; cy = midY; }
        else
        {
            cx = Math.Clamp(hx + hw * 0.5f - CardW * 0.5f, 8f, W - CardW - 8f);
            if (hy + hh + Gap + CardH < H) cy = hy + hh + Gap;
            else if (hy - Gap - CardH > minY) cy = hy - Gap - CardH;
            else { cx = Math.Max(8f, hx + hw - CardW - 12f); cy = hy + 12f; }
        }
        cy += (1f - fade) * 10f;

        int a = (int)(255 * fade);
        using (paper.Column("pms_card").PositionType(PositionType.SelfDirected).Position(cx, cy)
            .Width(CardW).Height(UnitValue.Auto).Layer(Layer.Overlay).Rounded(m.ContainerRounding)
            .Padding(16, 16, 14, 12).Gap(8)
            .BackgroundColor(Color.FromArgb((int)(250 * fade), 24, 20, 36))
            .BorderColor(Color.FromArgb(a, EditorTheme.Accent)).BorderWidth(1)
            .Glow(0, 12, 36, -12, Color.FromArgb((int)(85 * fade), EditorTheme.Accent))
            .Enter())
        {
            using (paper.Row("pms_head").Width(UnitValue.StretchOne).Height(22).Gap(8).Enter())
            {
                paper.Box("pms_icon").Width(18).Height(UnitValue.StretchOne).IsNotInteractable()
                    .Text(EditorIcons.Gamepad, font).TextColor(Color.FromArgb(a, EditorTheme.Accent))
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter);
                paper.Box("pms_title").Width(UnitValue.StretchOne).Height(UnitValue.StretchOne).IsNotInteractable()
                    .Text(Loc.Get("play.hint.title"), semi).TextColor(Color.FromArgb(a, Color.White))
                    .FontSize(EditorTheme.FontSize + 2f).Alignment(TextAlignment.MiddleLeft);
            }

            paper.Box("pms_body").Width(UnitValue.StretchOne).Height(UnitValue.Auto).IsNotInteractable()
                .Text(Loc.Get("play.hint.body", new { key = ShortcutManager.GetDisplayString(ShortcutManager.GetBinding("Global/TogglePlay")) }), font)
                .Wrap(Scribe.TextWrapMode.Wrap).TextColor(Color.FromArgb(a, EditorTheme.Ink400))
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

            using (paper.Row("pms_btns").Width(UnitValue.StretchOne).Height(UnitValue.Auto).Gap(8).Enter())
            {
                paper.Box("pms_spc").Width(UnitValue.StretchOne).Height(1).IsNotInteractable();
                Origami.Button(paper, "pms_never", Loc.Get("play.hint.dont_show"), () =>
                {
                    PlayModePreferences prefs = Preferences.Get<PlayModePreferences>();
                    prefs.ShowPlayModeHint = false;
                    Preferences.Changed(Preferences.SectionOf(prefs));
                }).Subtle().Show();
                Origami.Button(paper, "pms_ok", Loc.Get("play.hint.got_it"), () => s_hintDismissed = true).Primary().Show();
            }
        }
    }
}
