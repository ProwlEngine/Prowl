using System;
using System.Linq;

using Prowl.Editor.Docking;
using Prowl.Editor.Widgets;
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
    private const float ToolbarHeight = 26f;

    // Separate Paper instance for in-game UI so it renders into the game RT,
    // not on top of the editor.
    private PaperRenderer? _gamePaperRenderer;
    private Paper? _gamePaper;

    private static readonly (string name, int w, int h)[] Resolutions =
    {
        ("Free", 0, 0),
        ("1920x1080 (1080p)", 1920, 1080),
        ("1280x720 (720p)", 1280, 720),
        ("960x540 (540p)", 960, 540),
        ("640x480 (VGA)", 640, 480),
        ("800x600", 800, 600),
        ("1024x768", 1024, 768),
    };

    public override void OnGUI(Paper paper, float width, float height)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        using (paper.Column("gv_root").Size(width, height).Enter())
        {
            DrawToolbar(paper, font, width);
            DrawGameView(paper, font, width, height - ToolbarHeight);
        }
    }

    private void DrawToolbar(Paper paper, Prowl.Scribe.FontFile font, float width)
    {
        var resNames = Resolutions.Select(r => r.name).ToArray();

        using (paper.Row("gv_toolbar")
            .Height(ToolbarHeight).ChildLeft(6).ChildRight(6).RowBetween(6)
            .ChildTop(2).ChildBottom(2)
            .Enter())
        {
            Origami.Dropdown(paper, "gv_res", _resolutionIndex,
                v => { _resolutionIndex = v; InvalidateRT(); }, resNames).Show();

            paper.Box("gv_spacer");

            if (Scene.Current != null)
            {
                int camCount = Scene.Current.ActiveObjects
                    .SelectMany(go => go.GetComponents<Camera>())
                    .Where(c => !c.GameObject.HideFlags.HasFlag(HideFlags.HideAndDontSave))
                    .Count();
                paper.Box("gv_cam_count")
                    .Width(UnitValue.Auto).Height(22).ChildLeft(4).ChildRight(4)
                    .Text($"{EditorIcons.Camera} {camCount} camera(s)", font)
                    .TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSize - 3).Alignment(TextAlignment.MiddleRight);
            }
        }
    }

    private void DrawGameView(Paper paper, Prowl.Scribe.FontFile font, float width, float height)
    {
        var scene = Scene.Current;
        if (scene == null)
        {
            paper.Box("gv_no_scene")
                .Size(width, height)
                .BackgroundColor(Color.FromArgb(255, 20, 20, 22))
                .Text("No scene loaded", font)
                .TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter);
            return;
        }

        // Determine render size
        var (_, targetW, targetH) = Resolutions[_resolutionIndex];
        int rtW, rtH;
        if (targetW == 0 || targetH == 0)
        {
            // Free match panel size
            rtW = (int)MathF.Max(1, width);
            rtH = (int)MathF.Max(1, height);
        }
        else
        {
            rtW = targetW;
            rtH = targetH;
        }

        EnsureRT(rtW, rtH);

        // Renderables are now collected inside pipeline.Render() per-camera with context
        var cameras = scene.ActiveObjects
            .SelectMany(go => go.GetComponentsInChildren<Camera>())
            .Where(c => !c.GameObject.HideFlags.HasFlag(HideFlags.HideAndDontSave))
            .OrderBy(c => c.Depth)
            .ToList();

        foreach (var cam in cameras)
        {
            var origTarget = cam.Target;
            cam.Target = _rt;
            cam.UpdateRenderData();

            var pipeline = cam.Pipeline ?? DefaultRenderPipeline.Default;
            pipeline.Render(cam, new RenderingData());

            cam.Target = origTarget;
        }

        // Render game OnGui into the game RT using a separate Paper instance
        // so it doesn't overlay the editor UI.
        if (Application.IsPlaying && _rt != null)
        {
            EnsureGamePaper(rtW, rtH);
            _gamePaperRenderer!.UpdateProjection(rtW, rtH);

            // Bind the game RT BEFORE EndFrame so the Paper renderer draws into it
            Graphics.BindFramebuffer(_rt.frameBuffer);
            Graphics.Viewport(0, 0, (uint)rtW, (uint)rtH);

            _gamePaper!.BeginFrame(Time.DeltaTime, -1f);
            scene.OnGui(_gamePaper);
            _gamePaper.EndFrame(); // Layout + render draw calls go to the bound RT

            // Restore default framebuffer for editor rendering
            Graphics.UnbindFramebuffer();
            Graphics.Viewport(0, 0, (uint)Window.InternalWindow.FramebufferSize.X, (uint)Window.InternalWindow.FramebufferSize.Y);
        }

        // Display the RT centered with letterboxing
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

            // Game view
            paper.Box("gv_display")
                .PositionType(PositionType.SelfDirected)
                .Position(offsetX, offsetY).Size(displayW, displayH)
                .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                {
                    float rx = (float)r.Min.X;
                    float ry = (float)r.Min.Y;
                    float rw = (float)r.Size.X;
                    float rh = (float)r.Size.Y;

                    // Flip Y
                    canvas.SetBrushTexture(_rt.MainTexture);
                    canvas.SetBrushTextureTransform(
                        Transform2D.CreateTranslation(rx, ry + rh) *
                        Transform2D.CreateScale(rw, -rh));
                    canvas.RectFilled(rx, ry, rw, rh, new Prowl.Vector.Color32(255, 255, 255, 255));
                    canvas.ClearBrushTexture();
                }));

            // Game view is focused when hovered play-mode input is routed here
            GameViewInputHandler.IsGameViewFocused = paper.IsParentHovered;
        }
        else
        {
            paper.Box("gv_black")
                .Size(width, height)
                .BackgroundColor(Color.Black);
        }
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
