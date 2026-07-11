using System;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Spatial;

using Color = System.Drawing.Color;
// Aliased to avoid clashing with Prowl.PaperUI types used elsewhere in this file.
using RuntimeEventSystem = Prowl.Runtime.UI.EventSystem;

namespace Prowl.Editor.GUI.Panels;

[EditorWindow("General/Game")]
public class GameViewPanel : DockPanel
{
    public override string Title => Loc.Get("panel.game");
    public override string Icon => EditorIcons.Gamepad;

    private RenderTexture? _rt;
    private int _resolutionIndex = 0;
    private bool _showStats;
    private RenderStats.Frame _gameStats; // snapshot from last game render (persists when paused)
    private Rect _displayAbsRect; // game-view rect in paper coords, cached for routing UI input next frame

    // Separate Paper instance for in-game UI
    private PaperRenderer? _gamePaperRenderer;
    private Paper? _gamePaper;

    private static readonly (string name, int w, int h)[] Resolutions =
    {
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
    };

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        using (paper.Column("gv_root").Size(width, height).Enter())
        {
            DrawGameView(paper, font, width, height);
        }
    }

    public override float HeaderWidth => 28f;
    public override void OnHeaderContent(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;
        paper.Box("gv_hdr_settings").Width(24).Height(24).Rounded(6)
            .Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .Text(EditorIcons.Gear, font).TextColor(EditorTheme.Ink300).FontSize(13f).Alignment(TextAlignment.MiddleCenter)
            .OnClick(_ => Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b =>
            {
                b.Header(Loc.Get("panel.game"));
                b.Submenu(Loc.Get("game.resolution"), sub =>
                {
                    for (int i = 0; i < Resolutions.Length; i++)
                    {
                        int idx = i;
                        sub.Item(Resolutions[i].name, () => { _resolutionIndex = idx; InvalidateRT(); }, on: idx == _resolutionIndex);
                    }
                }, EditorIcons.Expand);
                b.Toggle(Loc.Get("game.show_stats"), () => _showStats = !_showStats, () => _showStats);
            }));
    }

    private void DrawGameView(Paper paper, Scribe.FontFile font, float width, float height)
    {
        using (paper.Box("gv_game").Enter())
        {
            var scene = Scene.Current;
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
            var (_, targetW, targetH) = Resolutions[_resolutionIndex];
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

            // Render all cameras
            var cameras = scene.ActiveObjects
                .SelectMany(go => go.GetComponentsInChildren<Camera>())
                .Where(c => !c.GameObject.HideFlags.HasFlag(HideFlags.HideAndDontSave))
                .OrderBy(c => c.Depth)
                .ToList();

            RenderStats.BeginFrame();
            foreach (var cam in cameras)
            {
                var origTarget = cam.Target;
                cam.Target = _rt;
                cam.UpdateRenderData();

                var pipeline = cam.Pipeline ?? DefaultRenderPipeline.Default;
                pipeline.Render(cam, new RenderingData());

                cam.Target = origTarget;
            }
            RenderStats.EndFrame();
            _gameStats = RenderStats.Last; // snapshot for stats overlay (persists when paused/stepped)

            // Render game UI into the RT
            if (Application.IsPlaying && _rt != null)
            {
                EnsureGamePaper(rtW, rtH);
                _gamePaperRenderer!.UpdateProjection(rtW, rtH);

                {
                    using var bind = Graphics.GetCommandBuffer("GameViewPanel.GUI Bind");
                    bind.SetRenderTarget(_rt.frameBuffer);
                    bind.SetViewport(0, 0, (uint)rtW, (uint)rtH);
                    Graphics.Submit(bind);
                }

                _gamePaper!.BeginFrame(Time.DeltaTime, -1f);
                scene.OnGui(_gamePaper);
                _gamePaper.EndFrame();

                {
                    using var unbind = Graphics.GetCommandBuffer("GameViewPanel.GUI Unbind");
                    unbind.SetRenderTarget(null);
                    unbind.SetViewport(0, 0, (uint)Window.InternalWindow.FramebufferSize.X, (uint)Window.InternalWindow.FramebufferSize.Y);
                    Graphics.Submit(unbind);
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
                var capturedRT = _rt;
                paper.Box("gv_display")
                    .PositionType(PositionType.SelfDirected)
                    .Position(offsetX, offsetY).Size(displayW, displayH)
                    .OnPostLayout((handle, rect) =>
                    {
                        // Cache the absolute paper-coord rect so next frame's UI input maps into RT space.
                        _displayAbsRect = rect;
                        paper.Draw(ref handle, (canvas, r) =>
                    {
                        if (capturedRT?.MainTexture == null) return;
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

                // Route game input into the new UI system. The cursor is rescaled from the letterboxed
                // display rect into the RT's pixel space so clicks map to the same coords the canvas was
                // laid out at (the pipeline pushed the RT size as GameCanvas.ScreenSizeOverride).
                bool hovered = paper.IsParentHovered;
                GameViewInputHandler.IsGameViewFocused = hovered;

                if (_displayAbsRect.Size.X > 0 && _displayAbsRect.Size.Y > 0)
                {
                    Float2 origin = new((float)_displayAbsRect.Min.X, (float)_displayAbsRect.Min.Y);
                    Float2 size = new((float)_displayAbsRect.Size.X, (float)_displayAbsRect.Size.Y);
                    Float2 local = paper.PointerPos - origin;
                    Float2 inRT = new(local.X * (rtW / size.X), local.Y * (rtH / size.Y));
                    bool inside = local.X >= 0 && local.Y >= 0 && local.X <= size.X && local.Y <= size.Y;

                    // Gameplay scripts read Input.MousePosition in render-target pixel space (matching
                    // Camera.PixelWidth/Height), so picking works from the editor like a standalone build.
                    // Published unconditionally (unlike the UI viewport below, which needs an EventSystem).
                    GameViewInputHandler.Viewport = new GameViewInputHandler.GameViewport(origin, size, new Int2(rtW, rtH));

                    if (RuntimeEventSystem.Current is { } es)
                        es.Viewport = new RuntimeEventSystem.HostViewport
                        {
                            ReferenceSize = new Float2(rtW, rtH),
                            PointerPosition = inRT,
                            ReceivesInput = hovered && inside,
                        };
                }
                else
                {
                    GameViewInputHandler.Viewport = null;
                    if (RuntimeEventSystem.Current is { } es)
                        es.Viewport = null;
                }

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

        var s = _gameStats;
        float fps = s.FrameTimeMs > 0 ? 1000f / s.FrameTimeMs : 0;

        using (paper.Column("gv_stats")
            .PositionType(PositionType.SelfDirected)
            .Position(x, y)
            .Width(panelW).Height(UnitValue.Auto)
            .BackgroundColor(EditorTheme.Neutral200)
            .Rounded(5).Padding(pad, pad, pad, pad).ColBetween(1)
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
                Row(paper, font, "gv_r", $"{_rt.Width}x{_rt.Height}", $"{s.Cameras} cam", fs, rowH);

            // Target (color pass)
            int cullPct = s.RenderablesCollected > 0 ? (int)(s.RenderablesCulled * 100f / s.RenderablesCollected) : 0;
            long colorTris = s.Triangles - s.ShadowTriangles;
            long colorVerts = s.Vertices - s.ShadowVertices;

            Section(paper, font, "gv_ht", Loc.Get("gameview.sec_target"), FormatMainThread(s.ColorPassMs), fs);
            Row(paper, font, "gv_tdc", Loc.Get("gameview.draws"), $"{s.DrawCalls} ({s.InstancedDrawCalls} inst)", fs, rowH,
                "Individual GPU draw commands issued this frame. Each draw sends geometry to the GPU. Instanced draws render multiple copies in a single call.");
            Row(paper, font, "gv_tba", Loc.Get("gameview.batches"), s.Batches.ToString(), fs, rowH,
                "Groups of draw calls sharing the same material and render state. Fewer batches means less CPU overhead from state changes between draws.");
            Row(paper, font, "gv_ttr", Loc.Get("gameview.tris_verts"), $"{FormatCount(colorTris)} / {FormatCount(colorVerts)}", fs, rowH,
                "Total triangles and vertices submitted to the GPU for the color pass. High counts impact GPU fill rate and vertex processing.");
            Row(paper, font, "gv_tcu", Loc.Get("gameview.culled"), $"{s.RenderablesCulled}/{s.RenderablesCollected} ({cullPct}%)", fs, rowH,
                "Objects removed by frustum culling before rendering. Higher percentage means more objects are outside the camera view and skipped.");
            Row(paper, font, "gv_tlt", Loc.Get("gameview.lights"), $"D:{s.DirectionalLights} P:{s.PointLights} S:{s.SpotLights}", fs, rowH,
                "Active lights this frame. D = Directional (sun), P = Point (omni), S = Spot. Each light adds a lighting pass over affected geometry.");
            Row(paper, font, "gv_twa", Loc.Get("gameview.waited"), FormatMs(Graphics.LastFrameWaitMs), fs, rowH,
                "Time the main thread spent blocked at end of frame waiting for the render thread to finish executing this frame's command buffers. " +
                "High values mean the render thread is the bottleneck (GPU work or CB execution dominates the frame). " +
                "Near-zero values mean the main thread (update + encoding) takes longer than rendering, so the render thread was already idle when we asked.");

            // Shadows
            if (s.ShadowPasses > 0 || s.ShadowCasters > 0)
            {
                int shCullPct = s.ShadowRenderablesCollected > 0
                    ? (int)(s.ShadowRenderablesCulled * 100f / s.ShadowRenderablesCollected) : 0;

                Section(paper, font, "gv_hs", Loc.Get("gameview.sec_shadows"), FormatMainThread(s.ShadowPassMs), fs);
                Row(paper, font, "gv_sdc", Loc.Get("gameview.draws"), $"{s.ShadowDrawCalls} ({s.ShadowInstancedDrawCalls} inst)", fs, rowH,
                    "Draw calls for shadow map rendering. Each shadow-casting light renders the scene from its perspective to build depth maps.");
                Row(paper, font, "gv_spa", Loc.Get("gameview.passes"), s.ShadowPasses.ToString(), fs, rowH,
                    "Number of shadow map renders. Directional lights use cascaded shadow maps (multiple passes per light). Point lights use 6 passes (cube map).");
                Row(paper, font, "gv_str", Loc.Get("gameview.tris_verts"), $"{FormatCount(s.ShadowTriangles)} / {FormatCount(s.ShadowVertices)}", fs, rowH,
                    "Geometry rendered into shadow maps. This is additional to the color pass geometry and can be a major cost with many shadow casters.");
                Row(paper, font, "gv_scu", Loc.Get("gameview.culled"), $"{s.ShadowRenderablesCulled}/{s.ShadowRenderablesCollected} ({shCullPct}%)", fs, rowH,
                    "Objects culled from shadow map rendering. Each shadow pass has its own frustum so culling rates differ from the camera.");
                Row(paper, font, "gv_sca", Loc.Get("gameview.casters"), s.ShadowCasters.ToString(), fs, rowH,
                    "Lights with shadow mapping enabled. Each shadow caster adds one or more shadow passes to the frame.");
            }

            // Post FX (only when active)
            if (s.ImageEffects > 0)
            {
                Section(paper, font, "gv_hf", Loc.Get("gameview.sec_postfx"), FormatMainThread(s.PostFxMs), fs);
                Row(paper, font, "gv_fx", $"{s.ImageEffects} effects", $"{s.ImageEffectPasses} passes", fs, rowH,
                    "Post-processing effects applied after rendering (bloom, tone mapping, SSAO, etc). Each effect may use multiple full-screen passes.");
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
        var history = RenderStats.FrameTimeHistory;
        int head = RenderStats.FrameTimeIndex;
        int len = history.Length;

        canvas.RoundedRectFilled(x, y, w, h, 3, 3, 3, 3,
            Prowl.Vector.Color32.FromArgb(255, 10, 10, 14));

        float maxMs = 8f;
        for (int i = 0; i < len; i++)
            if (history[i] > maxMs) maxMs = history[i];
        maxMs = MathF.Ceiling(maxMs / 8f) * 8f;

        float gx = x + 1, gy = y + 1, gw = w - 2, gh = h - 2;

        // Target lines
        DrawTargetLine(canvas, font, gx, gy, gw, gh, maxMs, 16.67f, "60",
            Prowl.Vector.Color32.FromArgb(35, 80, 200, 100));
        DrawTargetLine(canvas, font, gx, gy, gw, gh, maxMs, 33.33f, "30",
            Prowl.Vector.Color32.FromArgb(35, 220, 180, 50));

        // Bars
        float barW = gw / len;
        for (int i = 0; i < len; i++)
        {
            float ms = history[(head + i) % len];
            if (ms <= 0) continue;
            float barH = MathF.Min((ms / maxMs) * gh, gh);
            var col = ms < 16.67f ? Prowl.Vector.Color32.FromArgb(200, 70, 190, 110)
                : ms < 33.33f ? Prowl.Vector.Color32.FromArgb(200, 210, 170, 50)
                : Prowl.Vector.Color32.FromArgb(200, 210, 55, 55);
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
            var lbl = paper.Box($"{id}_l").Text(left, font).TextColor(Dim).FontSize(fs).Alignment(TextAlignment.MiddleLeft);
            if (tooltip != null) lbl.Tooltip(tooltip);
            paper.Box($"{id}_r").Text(right, font).TextColor(Val).FontSize(fs).Alignment(TextAlignment.MiddleRight);
        }
    }

    // Fixed-width "XX.XXms" so changing values don't reflow the row.
    private static string FormatMs(float ms) => $"{ms,5:0.00}ms";
    private static string FormatMainThread(float ms) => $"{Loc.Get("gameview.main_thread")} {ms,5:0.00}ms";

    private static Color FpsColor(float fps) =>
        fps >= 55 ? EditorTheme.Green400 :
        fps >= 28 ? EditorTheme.Amber400 :
        EditorTheme.Red400;

    private static string FormatCount(long n) =>
        n >= 1_000_000 ? $"{n / 1_000_000.0:F1}M" :
        n >= 1_000 ? $"{n / 1_000.0:F1}K" :
        n.ToString();

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
        _rt?.Dispose();
        _rt = new RenderTexture(w, h, true, new[] { TextureImageFormat.Color4b });
    }

    private void InvalidateRT()
    {
        _rt?.Dispose();
        _rt = null;
    }
}
