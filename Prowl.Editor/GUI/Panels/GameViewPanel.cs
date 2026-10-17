using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.Projects.Settings;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.Diagnostics;
using Prowl.Runtime.GUI;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Spatial;

using Color = System.Drawing.Color;
// Aliased to avoid clashing with Prowl.PaperUI types used elsewhere in this file.
using RuntimeEventSystem = Prowl.Runtime.UI.EventSystem;

namespace Prowl.Editor.GUI.Panels;

public class GameViewPanel : DockPanel
{
    [MenuItem("Window/General/Game", priority: 1)]
    static void Open() => EditorApplication.Instance?.OpenPanel(typeof(GameViewPanel));

    public override string Title => Loc.Get("panel.game");
    public override string Icon => EditorIcons.Gamepad;

    private RenderTexture? _rt;

    /// <summary> What the game view last rendered into, or null before it has drawn. </summary>
    internal RenderTexture? RenderTarget => _rt;
    private int _resolutionIndex = -1; // read from the project on first use, see ResolutionIndex
    private bool _showStats;

    /// <summary> Whether play mode fills the dock area with this view. Saved with the layout, and read at the next play. </summary>
    internal bool MaximizeOnPlay { get; private set; }

    private RenderStats.Frame _gameStats; // snapshot from last game render (persists when paused)
    private Rect _displayAbsRect; // game-view rect in paper coords, cached for routing UI input next frame

    // Separate Paper instance for in-game UI
    private PaperRenderer? _gamePaperRenderer;
    private Paper? _gamePaper;

    private readonly PanelLockContext _lockContext = new();
    private bool _lockContextPushed;

    private static readonly (string name, int w, int h)[] Resolutions =
    [
        ("Free", 0, 0),
        ("16:9", -16, -9),
        ("16:10", -16, -10),
        ("4:3", -4, -3),
        ("5:4", -5, -4),
        ("21:9", -21, -9),
        ("1:1", -1, -1),
        ("1920x1080", 1920, 1080),
        ("1280x720", 1280, 720),
        ("960x540", 960, 540),
        ("640x480", 640, 480),
        ("800x600", 800, 600),
    ];

    public override void OnGUI(Paper paper, float width, float height)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        using (paper.Column("gv_root").Size(width, height).Enter())
        {
            // Hover-scoped like the other panels' shortcuts.
            if (paper.IsParentHovered && ShortcutManager.TryConsume("Game/ToggleMaximize"))
                EditorApplication.Instance?.Maximizer.RequestToggle(this);

            DrawGameView(paper, font, width, height);
        }
    }

    public override bool SerializeState(System.Text.Json.Nodes.JsonObject state)
    {
        if (!MaximizeOnPlay) return false;
        state["maximizeOnPlay"] = true;
        return true;
    }

    public override void RestoreState(System.Text.Json.Nodes.JsonObject state)
        => MaximizeOnPlay = state["maximizeOnPlay"]?.GetValue<bool>() == true;

    // The layout is otherwise only written on save, so the choice is written straight away. Not during play, where
    // the other panels' state is the play session's and must not be kept.
    private void ToggleMaximizeOnPlay()
    {
        MaximizeOnPlay = !MaximizeOnPlay;
        if (!Application.IsPlaying)
            EditorApplication.Instance?.SaveDockLayout();
    }

    public override float HeaderWidth => 28f;
    public override void OnHeaderContent(Paper paper, float width, float height)
    {
        EditorGUI.HeaderIconButton(paper, "gv_hdr_settings", EditorIcons.Gear, Loc.Get("game.settings"), () =>
            Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b =>
            {
                b.Header(Loc.Get("panel.game"));
                b.Submenu(Loc.Get("game.resolution"), sub =>
                {
                    for (int i = 0; i < Resolutions.Length; i++)
                    {
                        int idx = i;
                        sub.Item(Resolutions[i].name, () => SetResolution(idx), on: idx == ResolutionIndex);
                    }
                }, EditorIcons.Expand);
                b.Toggle(Loc.Get("game.show_stats"), () => _showStats = !_showStats, () => _showStats);
                b.Toggle(Loc.Get("game.maximize_on_play"), ToggleMaximizeOnPlay, () => MaximizeOnPlay);
            }));
    }

    // The project remembers the last resolution picked, so the next session (or a newly opened Game view)
    // starts on it. Read on first use rather than at construction, since the default layout builds its
    // panels before any project's settings are loaded.
    internal int ResolutionIndex
    {
        get
        {
            if (_resolutionIndex < 0)
                _resolutionIndex = Math.Clamp(EditorRegistries.GetSettings<ProjectsEditorSettings>().SelectedResolutionIndex, 0, Resolutions.Length - 1);
            return _resolutionIndex;
        }
    }

    internal void SetResolution(int index)
    {
        _resolutionIndex = index;
        InvalidateRT();

        ProjectsEditorSettings settings = EditorRegistries.GetSettings<ProjectsEditorSettings>();
        if (settings.SelectedResolutionIndex == index) return;
        settings.SelectedResolutionIndex = index;
        EditorRegistries.SaveSettings();
    }

    private void DrawGameView(Paper paper, Scribe.FontFile font, float width, float height)
    {
        using (paper.Box("gv_game").Enter())
        {
            Scene scene = Scene.Current;
            if (scene == null)
            {
                paper.Box("gv_no_scene")
                    .Size(width, height)
                    .BackgroundColor(EditorTheme.Neutral100)
                    .Text(Loc.Get("panel.game") + " - " + Loc.Get("selector.no_scene"), font)
                    .TextColor(EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter);
                return;
            }

            // Determine render size from resolution/aspect setting
            (string _, int targetW, int targetH) = Resolutions[ResolutionIndex];
            int rtW, rtH;
            if (targetW == 0 && targetH == 0)
            {
                // Free - match panel
                rtW = (int)MathF.Max(1, width);
                rtH = (int)MathF.Max(1, height);
            }
            else if (targetW < 0 && targetH < 0)
            {
                // Aspect ratio mode - fit panel with given ratio
                float aspect = (float)(-targetW) / (-targetH);
                float panelAspect = width / height;
                if (aspect > panelAspect)
                {
                    rtW = (int)MathF.Max(1, width);
                    rtH = (int)MathF.Max(1, width / aspect);
                }
                else
                {
                    rtH = (int)MathF.Max(1, height);
                    rtW = (int)MathF.Max(1, height * aspect);
                }
            }
            else
            {
                // Fixed resolution
                rtW = targetW;
                rtH = targetH;
            }

            EnsureRT(rtW, rtH);

            // Render all cameras.
            List<Camera> cameras = scene.GatherActiveCameras();
            cameras.RemoveAll(c => c.GameObject.HideFlags.HasFlag(HideFlags.HideAndDontSave));

            foreach (Camera cam in cameras)
            {
                try
                {
                    cam.Render(new CameraRenderOptions { FallbackTarget = _rt, Kind = Prowl.Graphite.Rendering.ViewKind.Game });
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[GameView] Camera '{cam.GameObject.Name}' render threw and was skipped: {ex.Message}");
                }
            }
            RenderStats.EndFrame();
            _gameStats = RenderStats.Last; // snapshot for stats overlay (persists when paused/stepped)

            bool hovered = paper.IsParentHovered;
            GameViewInputHandler.IsGameViewFocused = hovered;

            Float2 pointerInRT = Float2.Zero;
            bool pointerInside = false;

            if (_displayAbsRect.Size.X > 0 && _displayAbsRect.Size.Y > 0)
            {
                Float2 origin = new((float)_displayAbsRect.Min.X, (float)_displayAbsRect.Min.Y);
                Float2 size = new((float)_displayAbsRect.Size.X, (float)_displayAbsRect.Size.Y);
                Float2 local = paper.PointerPos - origin;
                pointerInRT = new(local.X * (rtW / size.X), local.Y * (rtH / size.Y));
                pointerInside = local.X >= 0 && local.Y >= 0 && local.X <= size.X && local.Y <= size.Y;

                // A click the CLI simulates stands in for the real pointer, for scripts, Paper and GameObject UI alike.
                if (SimulatedInput.Pointer is { } simulated)
                {
                    pointerInRT = new Float2(simulated.X, simulated.Y);
                    pointerInside = true;
                    hovered = true;
                }

                _lockContext.PanelOrigin = origin;
                _lockContext.PanelSize = size;
                if (!_lockContextPushed)
                {
                    Input.PushLockContext(_lockContext);
                    _lockContextPushed = true;
                }

                // Gameplay scripts read Input.MousePosition in render-target pixel space (matching
                // Camera.PixelWidth/Height), so picking works from the editor like a standalone build.
                GameViewInputHandler.Viewport = new GameViewInputHandler.GameViewport(origin, size, new Int2(rtW, rtH));

                if (RuntimeEventSystem.Current is { } es)
                    es.Viewport = new RuntimeEventSystem.HostViewport
                    {
                        ReferenceSize = new Float2(rtW, rtH),
                        PointerPosition = pointerInRT,
                        ReceivesInput = hovered && pointerInside,
                    };
            }
            else
            {
                GameViewInputHandler.Viewport = null;
                if (RuntimeEventSystem.Current is { } es)
                    es.Viewport = null;
            }

            // Render game UI into the RT
            if (Application.IsPlaying && _rt != null)
            {
                EnsureGamePaper(rtW, rtH);
                _gamePaperRenderer!.UpdateProjection(rtW, rtH);
                _gamePaperRenderer.Target = _rt;

                // The game's UI is gameplay, so it reads input the way the scene update does, simulated input included.
                bool wasGameplay = Application.IsGameplayExecuting;
                Application.IsGameplayExecuting = Application.ShouldRunGameplay;
                try
                {
                    PaperInputBridge.Pump(_gamePaper!, pointerInRT, hovered && pointerInside);

                    // Counted as UI, the same as in a build, not as the editor drawing it.
                    using (FrameStats.UI.Scope())
                    {
                        _gamePaper!.BeginFrame(Time.UnscaledDeltaTime, -1f);
                        scene.OnGui(_gamePaper);
                        _gamePaper.EndFrame();
                    }
                }
                finally
                {
                    Application.IsGameplayExecuting = wasGameplay;
                }
            }

            // Display with letterboxing
            if (_rt != null && _rt.MainTexture != null)
            {
                float aspect = rtW / (float)rtH;
                float panelAspect = width / height;
                float displayW, displayH, offsetX, offsetY;

                if (aspect > panelAspect)
                {
                    displayW = width;
                    displayH = width / aspect;
                    offsetX = 0;
                    offsetY = (height - displayH) / 2;
                }
                else
                {
                    displayH = height;
                    displayW = height * aspect;
                    offsetX = (width - displayW) / 2;
                    offsetY = 0;
                }

                // Letterbox bars
                if (offsetY > 0)
                {
                    paper.Box("gv_bar_top")
                        .PositionType(PositionType.SelfDirected).Position(0, 0).Size(width, offsetY)
                        .BackgroundColor(Color.Black);
                    paper.Box("gv_bar_bot")
                        .PositionType(PositionType.SelfDirected).Position(0, offsetY + displayH).Size(width, offsetY)
                        .BackgroundColor(Color.Black);
                }
                if (offsetX > 0)
                {
                    paper.Box("gv_bar_left")
                        .PositionType(PositionType.SelfDirected).Position(0, 0).Size(offsetX, height)
                        .BackgroundColor(Color.Black);
                    paper.Box("gv_bar_right")
                        .PositionType(PositionType.SelfDirected).Position(offsetX + displayW, 0).Size(offsetX, height)
                        .BackgroundColor(Color.Black);
                }

                // Game viewport - rounded rect with purple border (matches SceneView)
                bool playing = Application.IsPlaying;
                RenderTexture capturedRT = _rt;
                paper.Box("gv_display")
                    .PositionType(PositionType.SelfDirected)
                    .Position(offsetX, offsetY).Size(displayW, displayH)
                    .OnPostLayout((handle, rect) =>
                    {
                        // Cache the absolute paper-coord rect so next frame's UI input maps into RT space.
                        _displayAbsRect = rect;
                        paper.Draw(ref handle, (canvas, r) =>
                    {
                        if (capturedRT.IsNotValid() || capturedRT.MainTexture == null) return;
                        float rx = (float)r.Min.X;
                        float ry = (float)r.Min.Y;
                        float rw = (float)r.Size.X;
                        float rh = (float)r.Size.Y;
                        float round = EditorTheme.Roundness;

                        // When the image fills to the top (no letterbox bar above), keep the top corners
                        // square so it butts flush against the panel header; when letterboxed, round them.
                        float topRound = offsetY > 0.5f ? round : 0f;

                        canvas.SetBrushTexture(capturedRT.MainTexture);
                        canvas.SetBrushTextureTransform(
                            Transform2D.CreateTranslation(rx, ry + rh) *
                            Transform2D.CreateScale(rw, -rh));
                        canvas.RoundedRectFilled(rx, ry, rw, rh, topRound, topRound, round, round, Color.White);
                        canvas.ClearBrushTexture();

                        // Border inset by half its width so the full stroke sits on top of the texture edge.
                        float bw = playing ? 2f : 1f;
                        float half = bw * 0.5f;
                        canvas.RoundedRect(rx + half, ry + half, rw - bw, rh - bw, topRound, topRound, round, round);
                        canvas.SetStrokeColor(playing ? EditorTheme.Purple500 : EditorTheme.Ink200);
                        canvas.SetStrokeWidth(bw);
                        canvas.Stroke();
                    });
                    });

                // Stats overlay (top-right of viewport, theme sized)
                if (_showStats)
                    DrawStats(paper, font, offsetX + displayW, offsetY, displayW);
            }
            else
            {
                paper.Box("gv_black")
                    .Size(width, height)
                    .BackgroundColor(Color.Black);
            }
        }
    }

    private void DrawStats(Paper paper, Scribe.FontFile font, float rightEdge, float top, float displayW)
    {
        const float panelW = 220f;
        const float graphH = 50f;
        const float rowH = 13f;
        float pad = 7f;
        float x = rightEdge - panelW - 6;
        float y = top + 6;
        float fs = EditorTheme.FontSizeSmall;

        RenderStats.Frame s = _gameStats;
        float fps = s.FrameTimeMs > 0 ? 1000f / s.FrameTimeMs : 0;

        using (paper.Column("gv_stats")
            .PositionType(PositionType.SelfDirected)
            .Position(x, y)
            .Width(panelW).Height(UnitValue.Auto)
            .BackgroundColor(EditorTheme.Neutral200)
            .Rounded(Origami.Current.Metrics.ContainerRounding).Padding(pad, pad, pad, pad).Gap(1)
            .Enter())
        {
            // FPS + frame time
            using (paper.Row("gv_st_fps").Height(16).Enter())
            {
                paper.Box("gv_st_fps_v")
                    .Text($"{fps:F0} FPS", font).TextColor(FpsColor(fps))
                    .FontSize(fs + 3).Alignment(TextAlignment.MiddleLeft);
                paper.Box("gv_st_fps_ms")
                    .Text(FormatMs(s.FrameTimeMs), font).TextColor(Dim)
                    .FontSize(fs).Alignment(TextAlignment.MiddleRight);
            }

            // Graph
            paper.Box("gv_st_graph")
                .Width(UnitValue.Stretch()).Height(graphH)
                .IsNotInteractable()
                .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                    DrawFrameTimeGraph(canvas, r, font)));

            // General info
            if (_rt != null)
                Row(paper, font, "gv_r", $"{_rt.Width}x{_rt.Height}", $"{s.Views} views", fs, rowH);

            Section(paper, font, "gv_ht", Loc.Get("gameview.sec_target"), "", fs);
            Row(paper, font, "gv_tdc", Loc.Get("gameview.draws"), $"{s.DrawCalls} ({s.DrawCommands} meshes)", fs, rowH,
                "Indirect draw calls issued for the views this frame, and the mesh pieces drawn by them. Renderables sharing a shader and mesh page draw in one call.");
            Row(paper, font, "gv_twa", Loc.Get("gameview.waited"), FormatMs((float)Graphics.LastFrameWaitMs), fs, rowH,
                "Time the main thread spent blocked at end of frame waiting for the render thread. " +
                "High values mean rendering is the bottleneck; near zero means the main thread takes longer than rendering.");
            if (s.ShaderCompiles > 0)
                Row(paper, font, "gv_tsc", "Compiling", $"{s.ShaderCompiles} shaders", fs, rowH,
                    "Shader variants compiling in the background. What needs them draws once they are ready.");

            if (s.ShadowDrawCalls > 0)
            {
                Section(paper, font, "gv_hs", Loc.Get("gameview.sec_shadows"), "", fs);
                Row(paper, font, "gv_sdc", Loc.Get("gameview.draws"), $"{s.ShadowDrawCalls} ({s.ShadowDrawCommands} meshes)", fs, rowH,
                    "Draw calls rendering shadow maps. Each shadowed light draws the casters it sees into its part of the shadow atlas.");
            }
        }
    }

    private static Color Dim => EditorTheme.Ink300;
    private static Color Val => EditorTheme.Ink500;
    private static Color Hdr => EditorTheme.Purple400;

    private static void DrawFrameTimeGraph(Quill.Canvas canvas, Rect r, Scribe.FontFile? font)
    {
        float x = (float)r.Min.X, y = (float)r.Min.Y;
        float w = (float)r.Size.X, h = (float)r.Size.Y;
        float[] history = RenderStats.FrameTimeHistory;
        int head = RenderStats.FrameTimeIndex;
        int len = history.Length;

        float round = EditorTheme.Roundness * 0.5f;
        canvas.RoundedRectFilled(x, y, w, h, round, round, round, round,
            Prowl.Vector.Color32.FromArgb(255, 10, 10, 14));

        float maxMs = 8f;
        for (int i = 0; i < len; i++)
            if (history[i] > maxMs) maxMs = history[i];
        maxMs = MathF.Ceiling(maxMs / 8f) * 8f;

        float gx = x + 1, gy = y + 1, gw = w - 2, gh = h - 2;

        // Target lines
        DrawTargetLine(canvas, font, gx, gy, gw, gh, maxMs, 16.67f, "60", EditorTheme.ToColor32(EditorTheme.Green400, 0.14f));
        DrawTargetLine(canvas, font, gx, gy, gw, gh, maxMs, 33.33f, "30", EditorTheme.ToColor32(EditorTheme.Amber400, 0.14f));

        // Bars
        float barW = gw / len;
        for (int i = 0; i < len; i++)
        {
            float ms = history[(head + i) % len];
            if (ms <= 0) continue;
            float barH = MathF.Min((ms / maxMs) * gh, gh);
            Vector.Color col = EditorTheme.ToColor32(ms < 16.67f ? EditorTheme.Green400
                : ms < 33.33f ? EditorTheme.Amber400 : EditorTheme.Red400, 0.78f);
            canvas.RectFilled(gx + i * barW, gy + gh - barH, MathF.Max(1, barW - 0.5f), barH, col);
        }
    }

    private static void DrawTargetLine(Quill.Canvas canvas, Scribe.FontFile? font,
        float gx, float gy, float gw, float gh, float maxMs, float targetMs, string label,
        Color32 color)
    {
        float ly = gy + gh - (targetMs / maxMs) * gh;
        if (ly <= gy || ly >= gy + gh) return;
        canvas.SetStrokeColor(color); canvas.SetStrokeWidth(0.5f);
        canvas.BeginPath(); canvas.MoveTo(gx, ly); canvas.LineTo(gx + gw, ly); canvas.Stroke();
        if (font != null)
            canvas.DrawText(label, gx + 1, ly - 8, color, 7, font, 0);
    }

    private static void Section(Paper paper, Scribe.FontFile font, string id, string label, float fs)
        => Section(paper, font, id, label, null, fs);

    private static void Section(Paper paper, Scribe.FontFile font, string id, string label, string? timing, float fs)
    {
        paper.Box($"{id}_s").Height(2);
        if (timing != null)
        {
            using (paper.Row(id).Height(11).Enter())
            {
                paper.Box($"{id}_l").Text(label, font).TextColor(Hdr)
                    .FontSize(fs - 1).Alignment(TextAlignment.MiddleLeft);
                paper.Box($"{id}_t").Text(timing, font).TextColor(Dim)
                    .FontSize(fs - 1).Alignment(TextAlignment.MiddleRight);
            }
        }
        else
        {
            paper.Box(id).Height(11).Text(label, font).TextColor(Hdr)
                .FontSize(fs - 1).Alignment(TextAlignment.MiddleLeft);
        }
    }

    private static void Row(Paper paper, Scribe.FontFile font, string id,
        string left, string right, float fs, float h, string? tooltip = null)
    {
        using (paper.Row(id).Height(h).Enter())
        {
            ElementBuilder lbl = paper.Box($"{id}_l").Text(left, font).TextColor(Dim).FontSize(fs).Alignment(TextAlignment.MiddleLeft);
            if (tooltip != null) lbl.Tooltip(tooltip);
            paper.Box($"{id}_r").Text(right, font).TextColor(Val).FontSize(fs).Alignment(TextAlignment.MiddleRight);
        }
    }

    // Fixed-width "XX.XXms" so changing values don't reflow the row.
    private static string FormatMs(float ms) => $"{ms,5:0.00}ms";

    private static Color FpsColor(float fps) =>
        fps >= 55 ? EditorTheme.Green400 :
        fps >= 28 ? EditorTheme.Amber400 :
        EditorTheme.Red400;

    private void EnsureGamePaper(int w, int h)
    {
        if (_gamePaperRenderer == null)
        {
            _gamePaperRenderer = new PaperRenderer();
            _gamePaperRenderer.Initialize(w, h);
        }

        if (_gamePaper == null)
            _gamePaper = new Paper(_gamePaperRenderer, w, h, new Quill.FontAtlasSettings());
        else
            _gamePaper.SetResolution(w, h);
    }

    private void EnsureRT(int w, int h)
    {
        if (_rt != null && _rt.Width == w && _rt.Height == h) return;
        if (_rt.IsValid()) _rt.Dispose();
        _rt = new RenderTexture(w, h, true, [TextureFormat.RGBA8]);
    }

    private void InvalidateRT()
    {
        if (_rt.IsValid()) _rt.Dispose();
        _rt = null;
    }

    public override void OnClosed()
    {
        if (_lockContextPushed)
        {
            Input.PopLockContext();
            _lockContextPushed = false;
        }

        InvalidateRT();
        _gamePaperRenderer?.Dispose();
        _gamePaperRenderer = null;
        _gamePaper = null;
    }
}
