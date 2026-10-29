// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI;

/// <summary>
/// While the game runs, slightly dims the editor around the Game view and gives it a glowing ring,
/// so play mode is obvious without washing the whole editor in colour. Nothing here takes input.
/// </summary>
public static class PlayModeSpotlight
{
    // Kept from the last frame the Game view showed, so the spotlight can fade out where it was.
    private static (float x, float y, float w, float h) s_rect;
    private static bool s_gameViewDrawn;
    private static float s_alpha;

    /// <summary> Called by the Game view each frame it draws, so a Game view behind another tab isn't spotlit. </summary>
    public static void MarkGameViewDrawn() => s_gameViewDrawn = true;

    /// <summary>
    /// Draws the dim and the ring. Call after the dock space is drawn: both sit on the base layer so they
    /// cover the panels but stay under any popup a panel or Origami opens.
    /// </summary>
    public static void Draw(Paper paper, float dt, Vector.Rect? gameView, float headerHeight)
    {
        bool visible = s_gameViewDrawn && gameView is { } g && g.Size.X > 0 && g.Size.Y > 0;
        s_gameViewDrawn = false;
        if (visible)
            s_rect = ((float)gameView!.Value.Min.X, (float)gameView.Value.Min.Y, (float)gameView.Value.Size.X, (float)gameView.Value.Size.Y);

        PlayModePreferences prefs = Preferences.Get<PlayModePreferences>();
        bool on = Runtime.Application.IsPlaying && visible && prefs.SpotlightGameView;
        float step = EditorTheme.AnimationScale > 0f ? dt * 6f / EditorTheme.AnimationScale : 1f;
        s_alpha = Math.Clamp(s_alpha + (on ? step : -step), 0f, 1f);
        if (s_alpha <= 0f) return;

        float W = (float)paper.ScreenRect.Size.X, H = (float)paper.ScreenRect.Size.Y;
        float appear = 1f - MathF.Pow(1f - s_alpha, 3f);
        var (hx, hy, hw, hh) = s_rect;

        // Four rects around the hole. The header stays undimmed so Stop / Pause stay easy to find.
        Color back = Color.FromArgb((int)(Math.Clamp(prefs.SpotlightDim, 0f, 1f) * 255 * appear), 4, 3, 9);
        float top = Math.Min(headerHeight, hy);
        Dim(paper, "pms_t", 0, top, W, hy - top, back);
        Dim(paper, "pms_b", 0, hy + hh, W, H - (hy + hh), back);
        Dim(paper, "pms_l", 0, hy, hx, hh, back);
        Dim(paper, "pms_r", hx + hw, hy, W - (hx + hw), hh, back);

        // Accent ring (amber while paused) on the window's own edge.
        Color ringC = Runtime.Application.IsPaused ? EditorTheme.Amber400 : EditorTheme.Accent;
        paper.Box("pms_ring").PositionType(PositionType.SelfDirected).Position(hx, hy).Size(hw, hh)
            .Rounded(Origami.Current.Metrics.ContainerRounding).IsNotInteractable()
            .BorderColor(Color.FromArgb((int)(230 * appear), ringC)).BorderWidth(2)
            .Glow(0, 0, 22, 2, Color.FromArgb((int)(110 * appear), ringC));
    }

    private static void Dim(Paper paper, string id, float x, float y, float w, float h, Color c)
    {
        if (w <= 0 || h <= 0) return;
        paper.Box(id).PositionType(PositionType.SelfDirected).Position(x, y).Size(w, h)
            .BackgroundColor(c).IsNotInteractable();
    }
}
