using System;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.Editor.GUI;
using Prowl.Editor.Theming;
using Prowl.Graphite;
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

public class GameViewPanel : DockPanel
{
    [MenuItem("Window/General/Game", priority: 1)]
    static void Open() => EditorApplication.Instance?.OpenPanel(typeof(GameViewPanel));

    public override string Title => Loc.Get("panel.game");
    public override string Icon => EditorIcons.Gamepad;

    private RenderTexture? _rt;
    private int _resolutionIndex = 0;
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
        EditorGUI.HeaderIconButton(paper, "gv_hdr_settings", EditorIcons.Gear, () =>
            Origami.ContextMenu((float)paper.PointerPos.X, (float)paper.PointerPos.Y, b =>
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

            // Render the scene's main camera into our own RT (game view shows no gizmos/grid).
            EnsureRT(rtW, rtH);
            Camera? mainCam = FindSceneCamera(scene);
            if (mainCam != null && _rt != null)
            {
                var prevTarget = mainCam.Target;
                mainCam.Target = _rt;
                mainCam.Render(new RenderingData { DisplayGizmos = false, DisplayGrid = false });
                mainCam.Target = prevTarget;
            }

            var smokeRT = _rt;

            // Display with letterboxing
            if (smokeRT != null && smokeRT.MainTexture != null)
            {
                rtW = smokeRT.Width;
                rtH = smokeRT.Height;
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
                var capturedRT = smokeRT;
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

            }
        }
    }

    private void EnsureGamePaper(int w, int h)
    {
        if (_gamePaperRenderer == null)
        {
            _gamePaperRenderer = new PaperRenderer();
            _gamePaperRenderer.Initialize(w, h);
            _gamePaperRenderer.PresentTarget = _rt?.frameBuffer;
        }

        if (_gamePaper == null)
            _gamePaper = new Paper(_gamePaperRenderer, w, h, new Quill.FontAtlasSettings());
        else
            _gamePaper.SetResolution(w, h);
    }

    private static Camera? FindSceneCamera(Scene scene)
    {
        foreach (var go in scene.AllObjects)
        {
            if (go.HideFlags.HasFlag(HideFlags.HideAndDontSave)) continue;
            if (!go.CompareTag("Main Camera")) continue;
            var cam = go.GetComponent<Camera>();
            if (cam != null) return cam;
        }

        foreach (var go in scene.AllObjects)
        {
            if (go.HideFlags.HasFlag(HideFlags.HideAndDontSave)) continue;
            var cam = go.GetComponent<Camera>();
            if (cam != null) return cam;
        }

        return null;
    }

    private void EnsureRT(int w, int h)
    {
        if (_rt != null && _rt.Width == w && _rt.Height == h) return;
        _rt?.Dispose();
        _rt = new RenderTexture(w, h, true, new[] { PixelFormat.R8_G8_B8_A8_UNorm });
        if (_gamePaperRenderer != null)
            _gamePaperRenderer.PresentTarget = _rt.frameBuffer;
    }

    private void InvalidateRT()
    {
        _rt?.Dispose();
        _rt = null;
    }

    public override void OnClosed()
    {
        InvalidateRT();
        _gamePaperRenderer?.Dispose();
        _gamePaperRenderer = null;
        _gamePaper = null;
    }
}
