using System;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.Panels;
using Prowl.Editor.Theming;
using Prowl.Graphite.Rendering;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Rosetta;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Vector.Spatial;

using Color = System.Drawing.Color;

namespace Prowl.Editor.GUI.SceneView;

/// <summary>
/// The scene view's bottom-right preview of what the selected camera sees, effects and world-space UI included. The camera draws
/// into a view the preview owns (<see cref="CameraRenderOptions.View"/>), so its screen size, aspect and the Game
/// view's effect history are left alone.
/// </summary>
internal sealed class CameraPreview
{
    private const float Margin = 12f, MinWidth = 120f, MaxFraction = 0.8f, Corner = 22f, Button = 22f;

    // Shared by every scene view, for the session.
    private static float s_width = 280f;
    private static bool s_minimized;

    private RenderView? _view;
    private RenderTexture? _rt;
    private Camera? _source, _lastSource;
    private int _lastCutCount;
    private float _widthAtDragStart;

    /// <summary>Renders the selected camera, if any. Call once per frame before <see cref="Draw"/>.</summary>
    public void Render(float viewWidth, float viewHeight)
    {
        _source = null;
        GameObject? go = Selection.GetActiveAs<GameObject>();
        Camera? cam = go.IsValid() ? go.GetComponent<Camera>() : null;
        // A camera outside a loaded scene has no world to draw, and would leave the last frame up as its own.
        if (cam.IsNotValid() || cam.GameObject.Scene.IsNotValid()) { _lastSource = null; return; }
        _source = cam;
        if (s_minimized) { _lastSource = null; return; }

        (float w, float h) = Size(cam, viewWidth, viewHeight);
        // Too small to show: drop the source too, so Draw doesn't put the last image up at its old size.
        if (w <= 0f) { _source = _lastSource = null; return; }

        try
        {
            RenderWorld? world = cam.GameObject.Scene.Rendering.World;
            if (Graphics.Renderer == null || world == null) { _source = null; return; }
            EnsureTarget((int)w, (int)h);
            // The preview's own view: its effect history stays apart from the Game view's render of the same camera.
            _view ??= Graphics.Renderer.CreateView(world, "Camera Preview");
            // A different camera, a gap or a jump, or the camera itself was cut (a teleport): don't blend in the old history.
            if (!ReferenceEquals(cam, _lastSource) || cam.CutCount != _lastCutCount) _view.Cut();
            _lastSource = cam;
            _lastCutCount = cam.CutCount;

            // Preview, not RenderTexture: screen-space canvases would lay out again at the preview's pixel size, every
            // frame alongside the Game view's. World-space UI still draws.
            cam.Render(new CameraRenderOptions { FallbackTarget = _rt, View = _view, Kind = ViewKind.Preview });
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogException($"[SceneView] Camera preview of '{cam.GameObject.Name}' threw and was skipped", ex);
            _source = null;
        }
    }

    /// <param name="view">The scene view drawing this preview, which Align With View copies.</param>
    public void Draw(Paper paper, Scribe.FontFile font, float viewWidth, float viewHeight, EditorCamera view)
    {
        Camera? cam = _source;
        if (cam.IsNotValid()) return;

        if (s_minimized)
        {
            IconButton(paper, font, "sv_campv_show", EditorIcons.Camera, Loc.Get("scene.camera_preview_show"),
                    () => s_minimized = false)
                .PositionType(PositionType.SelfDirected)
                .Position(viewWidth - Button - 6 - Margin, viewHeight - Button - 6 - Margin)
                .Size(Button + 6, Button + 6)
                .BackgroundColor(EditorTheme.Glass).BorderColor(EditorTheme.BorderSoft).BorderWidth(1);
            return;
        }

        RenderTexture? rt = _rt;
        if (rt.IsNotValid() || rt.MainTexture.IsNotValid()) return;
        // The size this frame's image was rendered at, so a resize drag never stretches it for a frame.
        float w = rt.Width, h = rt.Height;
        float round = Origami.Current.Metrics.ContainerRounding;

        using (paper.Box("sv_campv")
            .PositionType(PositionType.SelfDirected)
            .Position(viewWidth - w - Margin, viewHeight - h - Margin)
            .Size(w, h)
            // Interactable, so a click on the preview does not pick through it into the scene.
            .StopEventPropagation()
            .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
            {
                float rx = (float)r.Min.X, ry = (float)r.Min.Y, rw = (float)r.Size.X, rh = (float)r.Size.Y;
                canvas.SetBrushTexture(rt.MainTexture);
                canvas.SetBrushTextureTransform(Transform2D.CreateTranslation(rx, ry + rh) * Transform2D.CreateScale(rw, -rh));
                canvas.RoundedRectFilled(rx, ry, rw, rh, round, round, round, round, Color.White);
                canvas.ClearBrushTexture();

                canvas.RoundedRect(rx + 0.5f, ry + 0.5f, rw - 1f, rh - 1f, round, round, round, round);
                canvas.SetStrokeColor(EditorTheme.BorderSoft);
                canvas.SetStrokeWidth(1f);
                canvas.Stroke();
            }))
            .Enter())
        {
            // Top-left grip: dragging up and left grows the preview, which stays anchored bottom-right.
            paper.Box("sv_campv_grip")
                .PositionType(PositionType.SelfDirected).Position(4, 4).Size(Corner, Corner)
                .BackgroundColor(EditorTheme.Glass)
                .Rounded(round)
                .Hovered.BackgroundColor(EditorTheme.Hover).End()
                .Cursor(PaperCursor.ResizeNWSE)
                .Tooltip(Loc.Get("scene.camera_preview_resize"))
                .OnDragStart(_ => _widthAtDragStart = w)
                .OnDragging(e =>
                {
                    float aspect = w / h;
                    float dx = (float)e.TotalDelta.X, dy = (float)e.TotalDelta.Y * aspect;
                    // Whichever axis moved further leads, so a diagonal drag tracks the cursor.
                    float maxW = MathF.Min(viewWidth, viewHeight * aspect) * MaxFraction;
                    s_width = Math.Clamp(_widthAtDragStart - (MathF.Abs(dx) > MathF.Abs(dy) ? dx : dy), MinWidth, MathF.Max(MinWidth, maxW));
                })
                .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                {
                    // Three diagonal grip lines tucked into the corner, shortest at the very corner.
                    float x = (float)r.Min.X + 5, y = (float)r.Min.Y + 5;
                    canvas.SetStrokeColor(EditorTheme.Ink400);
                    canvas.SetStrokeWidth(1.25f);
                    for (int i = 1; i <= 3; i++)
                    {
                        float len = i * 4f;
                        canvas.BeginPath();
                        canvas.MoveTo(x, y + len); canvas.LineTo(x + len, y);
                        canvas.Stroke();
                    }
                }));

            // Top-right: put the camera where the scene view is looking, then hide the preview.
            using (paper.Row("sv_campv_btns")
                .PositionType(PositionType.SelfDirected).Position(w - 2 * Button - 6, 4)
                .Size(2 * Button + 2, Button).Gap(2)
                .Enter())
            {
                IconButton(paper, font, "sv_campv_align", EditorIcons.ArrowsToEye, Loc.Get("hierarchy.align_with_view"),
                    () =>
                    {
                        MoveTo(cam.GameObject, view.Position, view.Rotation);
                        _lastSource = null; // a jump: start the effect history over
                    });
                IconButton(paper, font, "sv_campv_hide", EditorIcons.Minimize, Loc.Get("scene.camera_preview_hide"),
                    () => s_minimized = true);
            }
        }
    }

    public void Dispose()
    {
        if (_rt.IsValid()) _rt.Dispose();
        _rt = null;
        _view?.Dispose();
        _view = null;
    }

    private static ElementBuilder IconButton(Paper paper, Scribe.FontFile font, string id, string glyph, string tooltip, Action onClick)
        => paper.Box(id).Size(Button, Button).Rounded(EditorTheme.Roundness)
            .BackgroundColor(EditorTheme.Glass)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .Text(glyph, font).TextColor(EditorTheme.Ink500).FontSize(11f).Alignment(TextAlignment.MiddleCenter)
            .Cursor(PaperCursor.Pointer)
            .Tooltip(tooltip)
            .IsNotFocusable()
            .OnClick(_ => onClick());

    /// <summary>Moves <paramref name="go"/> to a pose as one undo step.</summary>
    internal static void MoveTo(GameObject go, Float3 position, Quaternion rotation)
    {
        Undo.RecordObject(go, "Align With View");
        go.Transform.SetPositionAndRotation(position, rotation);
    }

    /// <summary>
    /// The preview's size: the user's width at the aspect the game shows the camera at, kept inside the viewport.
    /// Zero when the viewport is too small to hold even the smallest preview.
    /// </summary>
    internal static (float W, float H) Size(Camera cam, float viewWidth, float viewHeight)
    {
        float aspect;
        if (cam.HasCustomAspect && cam.Aspect > 0f) aspect = cam.Aspect;
        else
        {
            RenderTexture? target = cam.Target;
            aspect = target.IsValid() ? target.Width / (float)Math.Max(1, target.Height) : GameViewPanel.LastAspect;
            // A split-screen camera draws into its share of the target, clamped as the game clamps it.
            Float4 rect = Camera.ClampViewport(cam.ViewportRect);
            aspect *= rect.Z / rect.W;
        }

        float maxW = MathF.Min(viewWidth * MaxFraction, viewHeight * MaxFraction * aspect);
        if (maxW < MinWidth) return (0f, 0f);
        // Clamped here, not stored: each scene view has its own limit, and the width the user chose stays as it was.
        float w = Math.Clamp(s_width, MinWidth, maxW);
        return (w, w / aspect);
    }

    private void EnsureTarget(int w, int h)
    {
        w = Math.Max(1, w); h = Math.Max(1, h);
        if (_rt.IsValid() && _rt.Width == w && _rt.Height == h) return;
        if (_rt.IsValid()) _rt.Dispose();
        _rt = new RenderTexture(w, h, true, [TextureFormat.RGBA8]);
    }
}
