// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI;

/// <summary>A rect to spotlight, in screen space; null means "no target" (a centered welcome card).</summary>
public delegate (float x, float y, float w, float h)? GuideTarget();

/// <summary>One phase of a <see cref="Guide"/>. Title/body are localization keys.</summary>
public sealed class GuideStep
{
    public string TitleKey = "";
    public string BodyKey = "";
    public string TipKey = "";          // optional migration hint (Unreal/Godot equivalent)
    public string Icon = "";            // an EditorIcons glyph shown in the callout bubble
    public GuideTarget? Target;         // null -> centered card; otherwise spotlight this rect
    public Func<bool>? WaitUntil;       // gate "Next" until true (interactive/in-depth steps)
    public Action? OnEnter;             // set editor state when the step begins (future guides)
}

/// <summary>An ordered set of <see cref="GuideStep"/>s with a stable id (its "seen once" key).</summary>
public sealed class Guide
{
    public readonly string Id;
    public readonly List<GuideStep> Steps = new();
    public Guide(string id) { Id = id; }
    public Guide Add(GuideStep step) { Steps.Add(step); return this; }
}

/// <summary>
/// Lightweight, self-contained framework for on-boarding guides. A <see cref="Guide"/> is a list of
/// steps that either show a centered welcome card or spotlight a target (a docked panel, a header
/// button, or any custom rect). Steps can gate "Next" on a predicate and run an action on entry, so
/// this scales from the first-run UI tour to future per-window / interactive tutorials.
/// </summary>
public static class EditorGuide
{
    private static Guide? _active;
    private static int _index;
    private static float _time;      // global, for the pulse
    private static float _stepTime;  // per-step, for the entrance ease

    // ---- target sources ------------------------------------------------
    private static DockSpace? _dock;
    private static (float x, float y, float w, float h)? _themeButton;

    public static void SetDockSpace(DockSpace dock) => _dock = dock;
    public static void RegisterThemeButton(float x, float y, float w, float h) => _themeButton = (x, y, w, h);

    /// <summary>Target the docked panel of the given type (searches tabs; works after the layout shifts).</summary>
    public static GuideTarget Panel(Type panelType) => () =>
        _dock != null && _dock.TryGetPanelRect(panelType, out var r)
            ? ((float)r.Min.X, (float)r.Min.Y, (float)r.Size.X, (float)r.Size.Y)
            : (( float, float, float, float)?)null;

    /// <summary>Target the header's Theme quick-access button.</summary>
    public static GuideTarget ThemeButton() => () => _themeButton;

    // ---- lifecycle -----------------------------------------------------

    public static bool IsActive => _active != null;

    public static void Start(Guide guide)
    {
        _active = guide;
        _index = 0;
        _stepTime = 0f;
        Cur?.OnEnter?.Invoke();
    }

    /// <summary>Start a guide only if it has never been completed/skipped and none is running.</summary>
    public static void StartOnce(Guide guide)
    {
        if (_active != null) return;
        if (EditorSettings.Instance.SeenGuides.Contains(guide.Id)) return;
        Start(guide);
    }

    // One-shot auto-start: evaluated at most once per project-open, so clearing the "seen" state
    // mid-session (e.g. from Clear Cache) resets the tour for next launch without popping it up now.
    private static bool _autoStartConsumed;

    /// <summary>Re-arm the auto-start (call when a project finishes opening).</summary>
    public static void ArmAutoStart() => _autoStartConsumed = false;

    /// <summary>Auto-start the guide at most once per arm (once the editor is settled into a project).</summary>
    public static void TryAutoStart(Guide guide)
    {
        if (_autoStartConsumed || _active != null) return;
        _autoStartConsumed = true;
        StartOnce(guide);
    }

    private static GuideStep? Cur => _active != null && _index >= 0 && _index < _active.Steps.Count ? _active.Steps[_index] : null;

    private static void Next()
    {
        if (_active == null) return;
        if (_index >= _active.Steps.Count - 1) { Finish(false); return; }
        _index++; _stepTime = 0f; Cur?.OnEnter?.Invoke();
    }

    private static void Back()
    {
        if (_active == null || _index == 0) return;
        _index--; _stepTime = 0f;
    }

    private static void Finish(bool skipped)
    {
        if (_active != null && !EditorSettings.Instance.SeenGuides.Contains(_active.Id))
        {
            EditorSettings.Instance.SeenGuides.Add(_active.Id);
            EditorSettings.Instance.Save();
        }
        _active = null;
    }

    // ---- rendering -----------------------------------------------------

    private static float Ease(float t) => 1f - MathF.Pow(1f - Math.Clamp(t, 0f, 1f), 3f); // easeOutCubic

    public static void Draw(Paper paper, float dt)
    {
        if (_active == null) return;
        var step = Cur;
        if (step == null) { Finish(false); return; }

        _time += dt;
        _stepTime += dt;

        float W = (float)paper.ScreenRect.Size.X, H = (float)paper.ScreenRect.Size.Y;
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        float appear = Ease(_stepTime / 0.32f);
        var hole = step.Target?.Invoke();

        // ---- dark backdrop (spotlight cut-out via 4 side rects) ----
        int backA = (int)(190 * appear);
        Color back = Color.FromArgb(backA, 4, 3, 9);
        if (hole is { } r)
        {
            float hx = r.x, hy = r.y, hw = r.w, hh = r.h;
            Rect2(paper, "grd_t", 0, 0, W, hy, back);
            Rect2(paper, "grd_b", 0, hy + hh, W, H - (hy + hh), back);
            Rect2(paper, "grd_l", 0, hy, hx, hh, back);
            Rect2(paper, "grd_r", hx + hw, hy, W - (hx + hw), hh, back);

            // Glowing, gently pulsing ring around the spotlighted region.
            float pulse = 0.55f + 0.45f * MathF.Sin(_time * 3.2f);
            paper.Box("grd_ring").PositionType(PositionType.SelfDirected).Position(hx - 3, hy - 3).Size(hw + 6, hh + 6)
                .Layer(Layer.Overlay).Rounded(8).IsNotInteractable()
                .BorderColor(Color.FromArgb((int)(220 * appear), EditorTheme.Accent.R, EditorTheme.Accent.G, EditorTheme.Accent.B)).BorderWidth(2)
                .Glow(0, 0, 22, 2, Color.FromArgb((int)(150 * pulse * appear), EditorTheme.Accent.R, EditorTheme.Accent.G, EditorTheme.Accent.B));

            DrawCallout(paper, font, step, hx, hy, hw, hh, W, H, appear, centered: false);
        }
        else
        {
            // Full backdrop + a centered welcome card.
            paper.Box("grd_full").PositionType(PositionType.SelfDirected).Position(0, 0).Size(W, H)
                .Layer(Layer.Overlay).BackgroundColor(back)
                .OnClick(0, (_, _) => { }).StopEventPropagation();
            DrawCallout(paper, font, step, 0, 0, 0, 0, W, H, appear, centered: true);
        }
    }

    private static void Rect2(Paper paper, string id, float x, float y, float w, float h, Color c)
    {
        if (w <= 0 || h <= 0) return;
        paper.Box(id).PositionType(PositionType.SelfDirected).Position(x, y).Size(w, h)
            .Layer(Layer.Overlay).BackgroundColor(c)
            .OnClick(0, (_, _) => { }).StopEventPropagation();
    }

    private static void DrawCallout(Paper paper, Scribe.FontFile font, GuideStep step,
        float hx, float hy, float hw, float hh, float W, float H, float appear, bool centered)
    {
        float cardW = centered ? 460f : 350f;
        float pad = 18f;

        // Position: centered for welcome; otherwise below the target if there's room, else above/side.
        float cx, cy;
        if (centered)
        {
            cx = (W - cardW) / 2f;
            cy = H * 0.30f;
        }
        else
        {
            cx = Math.Clamp(hx + hw * 0.5f - cardW / 2f, 12f, W - cardW - 12f);
            float below = hy + hh + 14f;
            cy = (below + 210f < H) ? below : Math.Max(12f, hy - 210f - 14f);
        }
        cy += (1f - appear) * 14f; // slide-up entrance

        bool last = _active != null && _index >= _active.Steps.Count - 1;
        bool first = _index == 0;
        bool gated = step.WaitUntil != null && !step.WaitUntil();
        var semi = EditorTheme.FontSemiBold ?? font;
        var display = EditorTheme.FontDisplay ?? EditorTheme.DefaultBoldFont ?? semi;

        float heroH = centered ? 66 : 52;
        using (paper.Column("grd_card").PositionType(PositionType.SelfDirected).Position(cx, cy)
            .Width(cardW).Height(UnitValue.Auto).Layer(Layer.Overlay).Rounded(14).Clip()
            .BackgroundColor(Color.FromArgb(252, 24, 20, 36))
            .BorderColor(EditorTheme.BorderStrong).BorderWidth(1)
            .Glow(0, 14, 40, -12, Color.FromArgb(85, EditorTheme.Accent.R, EditorTheme.Accent.G, EditorTheme.Accent.B))
            .Enter())
        {
            // Accent gradient hero strip (top corners rounded to match the card).
            using (paper.Row("grd_hero").Width(UnitValue.StretchOne).Height(heroH)
                .Padding(pad, pad, 0, 0).RowBetween(13).RoundedTop(13)
                .BackgroundLinearGradient(0, 0, 1, 1, EditorTheme.Accent, EditorTheme.AccentBright).Enter())
            {
                if (!string.IsNullOrEmpty(step.Icon))
                    paper.Box("grd_icon").Width(centered ? 40 : 30).Height(centered ? 40 : 30)
                        .Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch()).Rounded(9)
                        .BackgroundColor(Color.FromArgb(60, 255, 255, 255)).IsNotInteractable()
                        .Text(step.Icon, semi).TextColor(Color.White).FontSize(centered ? 20f : 15f)
                        .Alignment(TextAlignment.MiddleCenter);

                paper.Box("grd_title").Width(UnitValue.StretchOne).Height(UnitValue.StretchOne)
                    .Margin(2, 0, UnitValue.Stretch(), UnitValue.Stretch()).IsNotInteractable()
                    .Text(Loc.Get(step.TitleKey), display).TextColor(Color.White)
                    .FontSize(centered ? 22f : 17f).Alignment(TextAlignment.MiddleLeft);
            }

            // Body copy (regular font, consistent padding).
            paper.Box("grd_body").Width(UnitValue.StretchOne).Height(UnitValue.Auto)
                .Padding(pad, pad, pad, string.IsNullOrEmpty(step.TipKey) ? pad : 12).IsNotInteractable()
                .Text(Loc.Get(step.BodyKey), font).Wrap(Scribe.TextWrapMode.Wrap)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

            // Optional migration tip (Unreal / Godot equivalent).
            if (!string.IsNullOrEmpty(step.TipKey))
                using (paper.Row("grd_tip").Width(UnitValue.StretchOne).Height(UnitValue.Auto)
                    .Margin(pad, pad, 0, 4).Rounded(8).Padding(10, 10, 8, 8).RowBetween(8)
                    .BackgroundColor(EditorTheme.Selected).Enter())
                {
                    paper.Box("grd_tip_i").Width(16).Height(UnitValue.StretchOne).Margin(0, 0, UnitValue.Stretch(), 0).IsNotInteractable()
                        .Text(EditorIcons.Lightbulb, font).TextColor(EditorTheme.AccentText)
                        .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
                    paper.Box("grd_tip_t").Width(UnitValue.StretchOne).Height(UnitValue.Auto).IsNotInteractable()
                        .Text(Loc.Get(step.TipKey), font).Wrap(Scribe.TextWrapMode.Wrap)
                        .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
                }

            // Footer: progress dots + Back / Skip / Next (Origami buttons).
            using (paper.Row("grd_foot").Width(UnitValue.StretchOne).Height(56).Padding(pad, pad, 10, 12).RowBetween(8).Enter())
            {
                int n = _active!.Steps.Count;
                using (paper.Row("grd_dots").Width(UnitValue.Auto).Height(UnitValue.StretchOne).Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch()).RowBetween(5).Enter())
                    for (int i = 0; i < n; i++)
                    {
                        bool on = i == _index;
                        paper.Box($"grd_dot{i}").Width(on ? 16 : 6).Height(6)
                            .Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch()).Rounded(3)
                            .BackgroundColor(on ? EditorTheme.Accent : EditorTheme.Neutral500).IsNotInteractable();
                    }

                paper.Box("grd_spc").Width(UnitValue.StretchOne).Height(1).IsNotInteractable();

                using (paper.Row("grd_btns").Width(UnitValue.Auto).Height(UnitValue.StretchOne)
                    .Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch()).RowBetween(8).Enter())
                {
                    if (!first)
                        Origami.Button(paper, "grd_back", Loc.Get("guide.back"), () => Back()).Subtle().Show();
                    Origami.Button(paper, "grd_skip", Loc.Get("guide.skip"), () => Finish(true)).Subtle().Show();
                    Origami.Button(paper, "grd_next", last ? Loc.Get("guide.finish") : Loc.Get("guide.next"),
                        () => { if (!gated) Next(); }).Primary().Disabled(gated).Show();
                }
            }
        }
    }

    // ---- the built-in first-run UI tour --------------------------------

    public static Guide WelcomeTour() => new Guide("welcome")
        .Add(new GuideStep { TitleKey = "guide.welcome.title", BodyKey = "guide.welcome.body", Icon = EditorIcons.WandMagicSparkles })
        .Add(new GuideStep { TitleKey = "guide.hierarchy.title", BodyKey = "guide.hierarchy.body", TipKey = "guide.hierarchy.tip", Icon = EditorIcons.Sitemap, Target = Panel(typeof(Panels.HierarchyPanel)) })
        .Add(new GuideStep { TitleKey = "guide.scene.title", BodyKey = "guide.scene.body", TipKey = "guide.scene.tip", Icon = EditorIcons.Cube, Target = Panel(typeof(Panels.SceneViewPanel)) })
        .Add(new GuideStep { TitleKey = "guide.inspector.title", BodyKey = "guide.inspector.body", TipKey = "guide.inspector.tip", Icon = EditorIcons.Sliders, Target = Panel(typeof(Panels.InspectorPanel)) })
        .Add(new GuideStep { TitleKey = "guide.project.title", BodyKey = "guide.project.body", TipKey = "guide.project.tip", Icon = EditorIcons.FolderOpen, Target = Panel(typeof(Panels.ProjectPanel)) })
        .Add(new GuideStep { TitleKey = "guide.console.title", BodyKey = "guide.console.body", TipKey = "guide.console.tip", Icon = EditorIcons.Terminal, Target = Panel(typeof(Panels.ConsolePanel)) })
        .Add(new GuideStep { TitleKey = "guide.theme.title", BodyKey = "guide.theme.body", Icon = EditorIcons.Palette, Target = ThemeButton() });
}
