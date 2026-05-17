using System;
using System.Linq;

using Prowl.Editor.Docking;
using Prowl.Editor.GUI;
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

namespace Prowl.Editor.Panels;

[EditorWindow("General/Game")]
public class GameViewPanel : DockPanel
{
    public override string Title => Loc.Get("panel.game");
    public override string Icon => EditorIcons.Gamepad;

    private RenderTexture? _rt;
    private int _resolutionIndex = 0;
    private bool _showStats;

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
            DrawToolbar(paper, font);
            DrawGameView(paper, font, width, height - EditorTheme.RowHeight);
        }
    }

    private void DrawToolbar(Paper paper, Prowl.Scribe.FontFile font)
    {
        using (paper.Row("gv_toolbar")
            .Height(EditorTheme.RowHeight)
            .ChildLeft(EditorTheme.Padding).ChildRight(EditorTheme.Padding)
            .RowBetween(EditorTheme.Spacing * 2)
            .Enter())
        {
            var resNames = Resolutions.Select(r => r.name).ToArray();
            Origami.Dropdown(paper, "gv_res", _resolutionIndex,
                v => { _resolutionIndex = v; InvalidateRT(); }, resNames)
                .Width(100).Show();

            paper.Box("gv_spacer");

            paper.Box("gv_stats_btn")
                .Width(EditorTheme.RowHeight - 4).Height(EditorTheme.RowHeight - 4)
                .Rounded(EditorTheme.Roundness * 0.5f)
                .BackgroundColor(_showStats ? EditorTheme.Purple400 : Color.Transparent)
                .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                .Text(EditorIcons.ChartBar, font).TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize - 4).Alignment(TextAlignment.MiddleCenter)
                .OnClick(_ => _showStats = !_showStats);
        }
    }

    private void DrawGameView(Paper paper, Prowl.Scribe.FontFile font, float width, float height)
    {
        using (paper.Box("gv_game").Enter())
        {
            var scene = Scene.Current;
            if (scene == null)
            {
                paper.Box("gv_no_scene")
                    .Size(width, height)
                    .BackgroundColor(EditorTheme.Neutral100)
                    .Text(Loc.Get("panel.game") + " - No scene loaded", font)
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

            // Render game UI into the RT
            if (Application.IsPlaying && _rt != null)
            {
                EnsureGamePaper(rtW, rtH);
                _gamePaperRenderer!.UpdateProjection(rtW, rtH);

                Graphics.BindFramebuffer(_rt.frameBuffer);
                Graphics.Viewport(0, 0, (uint)rtW, (uint)rtH);

                _gamePaper!.BeginFrame(Time.DeltaTime, -1f);
                scene.OnGui(_gamePaper);
                _gamePaper.EndFrame();

                Graphics.UnbindFramebuffer();
                Graphics.Viewport(0, 0, (uint)Window.InternalWindow.FramebufferSize.X, (uint)Window.InternalWindow.FramebufferSize.Y);
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
                    .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                    {
                        if (capturedRT?.MainTexture == null) return;
                        float rx = (float)r.Min.X;
                        float ry = (float)r.Min.Y;
                        float rw = (float)r.Size.X;
                        float rh = (float)r.Size.Y;
                        float round = EditorTheme.Roundness;

                        // Draw RT with rounded corners and flipped Y
                        canvas.SetBrushTexture(capturedRT.MainTexture);
                        canvas.SetBrushTextureTransform(
                            Transform2D.CreateTranslation(rx, ry + rh) *
                            Transform2D.CreateScale(rw, -rh));
                        canvas.RoundedRectFilled(rx, ry, rw, rh, round, round, round, round, Color.White);
                        canvas.ClearBrushTexture();

                        // Purple border stroke
                        canvas.RoundedRect(rx, ry, rw, rh, round, round, round, round);
                        canvas.SetStrokeColor(playing ? EditorTheme.Purple500 : EditorTheme.Ink200);
                        canvas.SetStrokeWidth(playing ? 2f : 1f);
                        canvas.Stroke();
                    }));

                // Route game input when hovered
                GameViewInputHandler.IsGameViewFocused = paper.IsParentHovered;

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

    private void DrawStats(Paper paper, Prowl.Scribe.FontFile font, float rightEdge, float top, float displayW)
    {
        const float panelW = 250f;
        float pad = EditorTheme.Padding;
        float margin = EditorTheme.Padding * 2f;
        float x = rightEdge - panelW - margin;
        float y = top + margin;

        // Background card anchored top-right of the game viewport
        using (paper.Column("gv_stats")
            .PositionType(PositionType.SelfDirected)
            .Position(x, y)
            .Width(panelW).Height(UnitValue.Auto)
            .BackgroundColor(Color.FromArgb(220, EditorTheme.Neutral200.R, EditorTheme.Neutral200.G, EditorTheme.Neutral200.B))
            .Rounded(EditorTheme.Roundness)
            .Padding(pad + 2, pad + 2, pad + 2, pad + 2)
            .ColBetween(EditorTheme.Spacing)
            .Enter())
        {
            // Header
            paper.Box("gv_stats_hdr")
                .Height(EditorTheme.RowHeight - 4)
                .Text("Statistics", font)
                .TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize - 2)
                .Alignment(TextAlignment.MiddleLeft);

            paper.Box("gv_stats_sep")
                .Height(1).BackgroundColor(EditorTheme.Ink200);

            // Frame timing
            float fps = 1f / MathF.Max(0.0001f, (float)Time.UnscaledDeltaTime);
            float ms = (float)Time.UnscaledDeltaTime * 1000f;
            DrawStatRow(paper, font, "gv_st_fps", "FPS", $"{fps:F0}  ({ms:F2} ms)", EditorTheme.Ink500);

            if (_rt != null)
                DrawStatRow(paper, font, "gv_st_res", "Resolution", $"{_rt.Width} x {_rt.Height}");

            var s = RenderStats.Last;

            DrawStatHeader(paper, font, "gv_st_hdr_draw", "Render");
            DrawStatRow(paper, font, "gv_st_draws", "Draw Calls", s.DrawCalls.ToString());
            DrawStatRow(paper, font, "gv_st_inst", "Instanced", s.InstancedDrawCalls.ToString());
            DrawStatRow(paper, font, "gv_st_batch", "Batches", s.Batches.ToString());
            DrawStatRow(paper, font, "gv_st_tris", "Triangles", FormatCount(s.Triangles));
            DrawStatRow(paper, font, "gv_st_verts", "Vertices", FormatCount(s.Vertices));

            DrawStatHeader(paper, font, "gv_st_hdr_cull", "Culling");
            DrawStatRow(paper, font, "gv_st_coll", "Collected", s.RenderablesCollected.ToString());
            DrawStatRow(paper, font, "gv_st_drawn", "Drawn", s.RenderablesDrawn.ToString());
            DrawStatRow(paper, font, "gv_st_culled", "Culled", s.RenderablesCulled.ToString());

            DrawStatHeader(paper, font, "gv_st_hdr_light", "Lighting");
            DrawStatRow(paper, font, "gv_st_lights",
                "Lights",
                $"{s.Lights}  (D:{s.DirectionalLights} P:{s.PointLights} S:{s.SpotLights})");
            DrawStatRow(paper, font, "gv_st_shadow_casters", "Shadow Casters", s.ShadowCasters.ToString());
            DrawStatRow(paper, font, "gv_st_shadow_draws", "Shadow Draws", s.ShadowDrawCalls.ToString());
        }
    }

    private static void DrawStatHeader(Paper paper, Prowl.Scribe.FontFile font, string id, string label)
    {
        paper.Box($"{id}_sp").Height(EditorTheme.Spacing);
        paper.Box(id)
            .Height(EditorTheme.RowHeight - 6)
            .Text(label, font)
            .TextColor(EditorTheme.Purple600)
            .FontSize(EditorTheme.FontSize - 4)
            .Alignment(TextAlignment.MiddleLeft);
    }

    private static void DrawStatRow(Paper paper, Prowl.Scribe.FontFile font, string id, string label, string value, Color? valueColor = null)
    {
        using (paper.Row(id).Height(EditorTheme.RowHeight - 6).Enter())
        {
            paper.Box($"{id}_l")
                .Text(label, font)
                .TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSize - 3)
                .Alignment(TextAlignment.MiddleLeft);

            paper.Box($"{id}_v")
                .Text(value, font)
                .TextColor(valueColor ?? EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize - 3)
                .Alignment(TextAlignment.MiddleRight);
        }
    }

    private static string FormatCount(long n)
    {
        if (n >= 1_000_000) return $"{n / 1_000_000.0:F2}M";
        if (n >= 1_000) return $"{n / 1_000.0:F2}K";
        return n.ToString();
    }

    private void EnsureGamePaper(int w, int h)
    {
        if (_gamePaperRenderer == null)
        {
            _gamePaperRenderer = new PaperRenderer();
            _gamePaperRenderer.Initialize(w, h);
        }

        if (_gamePaper == null)
            _gamePaper = new Paper(_gamePaperRenderer, w, h, new Prowl.Quill.FontAtlasSettings());
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
