// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.OrigamiUI.Gizmo;
using Prowl.PaperUI;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Runtime.UI;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Editor;

[SceneViewEditorFor(typeof(UIBehaviour))]
public sealed class UISceneEditor : ISceneViewEditor
{
    private Texture2D _handleTexture = Texture2D.ParseDefault(DefaultTexture.Handle);

    /// <summary>The grabbable handles, plus <see cref="Move"/> for the rect body.</summary>
    private enum Handle
    {
        None, Move,
        ResizeL, ResizeR, ResizeB, ResizeT,
        ResizeBL, ResizeBR, ResizeTR, ResizeTL,
        Pivot,
        AnchorBL, AnchorBR, AnchorTR, AnchorTL,
    }

    /// <summary>Snapshot of the layout fields this editor mutates — used for undo.</summary>
    private struct LayoutState
    {
        public Float2 AnchorMin, AnchorMax, Pivot, SizeDelta, AnchoredPosition;

        public static LayoutState Capture(RectTransform rt) => new()
        {
            AnchorMin = rt.AnchorMin,
            AnchorMax = rt.AnchorMax,
            Pivot = rt.Pivot,
            SizeDelta = rt.SizeDelta,
            AnchoredPosition = rt.AnchoredPosition,
        };

        public readonly void ApplyTo(RectTransform rt)
        {
            rt.AnchorMin = AnchorMin;
            rt.AnchorMax = AnchorMax;
            rt.Pivot = Pivot;
            rt.SizeDelta = SizeDelta;
            rt.AnchoredPosition = AnchoredPosition;
        }

        public readonly bool Matches(LayoutState o) =>
            AnchorMin.Equals(o.AnchorMin) && AnchorMax.Equals(o.AnchorMax) &&
            Pivot.Equals(o.Pivot) && SizeDelta.Equals(o.SizeDelta) &&
            AnchoredPosition.Equals(o.AnchoredPosition);
    }

    // ================================================================
    //  Overlay Preview — temporary World-Space state
    // ================================================================

    /// <summary>
    /// Captures the original state of an Overlay canvas before the temporary
    /// World-Space preview is activated. Used to cleanly revert when the editor
    /// deactivates (deselect, play mode, save, shutdown, etc.).
    /// </summary>
    private struct OverlayPreviewState
    {
        public bool IsActive;
        public GameCanvas Canvas;

        // Original canvas properties
        public RenderMode OriginalRenderMode;

        // Original transform properties
        public Float3 OriginalPosition;
        public Quaternion OriginalRotation;
        public Float3 OriginalScale;
    }

    /// <summary>
    /// World-space Y position for the first Overlay preview canvas.
    /// Subsequent canvases are offset by <see cref="OverlayPreviewSpacing"/>.
    /// Placed far from typical scene content to avoid visual interference.
    /// </summary>
    private const float OverlayPreviewBaseY = 2000f;

    /// <summary>Y spacing between multiple simultaneously previewed Overlay canvases.</summary>
    private const float OverlayPreviewSpacing = 500f;

    /// <summary>Global counter for assigning non-overlapping preview positions.</summary>
    private static int s_overlayPreviewCount;

    /// <summary>Index of this editor's preview slot (used for Y offset).</summary>
    private int _overlayPreviewIndex;

    private OverlayPreviewState _overlayPreview;

    // ================================================================
    //  Constants / colors
    // ================================================================

    private static readonly Color ResizeColor = new(0.80f, 0.75f, 0.80f, 1.00f);  // green
    private static readonly Color PivotColor  = new(0.20f, 0.80f, 1.00f, 1.00f);  // cyan
    private static readonly Color AnchorColor = new(1.00f, 0.85f, 0.20f, 1.00f);  // amber
    private static readonly Color MoveColor   = new(0.40f, 1.00f, 0.60f, 1.00f);  // bright green
    private static readonly Color HotColor    = new(1.00f, 1.00f, 1.00f, 1.00f);  // hovered / active

    private const float MinRectSize = 1f;

    private UIBehaviour? _target;

    private Handle _hover = Handle.None;
    private Handle _active = Handle.None;

    // Drag anchoring — captured the frame a handle is grabbed.
    private Float2 _dragStartDesign;
    private Rect _dragStartRect;
    private LayoutState _dragStartState;

    // Save hook subscription tracking
    private bool _subscribedToSaveHook;

    public int Priority => 0;

    public void OnActivate(GameObject target)
    {
        _target = target.GetComponent<UIBehaviour>();

        if (_target == null) return;

        GameCanvas? canvas = _target.GetCanvas();
        if (canvas == null) return;


        // If the canvas is Overlay, activate the temporary World-Space preview.
        //if (canvas.RenderMode == RenderMode.ScreenSpaceOverlay)
        //    ActivateOverlayPreview(canvas);
    }

    public void OnDeactivate()
    {
        // Revert any active Overlay preview before releasing the target.
        //RevertOverlayPreview();

        _target = null;
        _hover = Handle.None;
        _active = Handle.None;
    }

    // The transform gizmo is meaningless for a RectTransform (its layout is driven by
    // anchors / pivot / size-delta, not Transform.Position), so this editor takes over
    // input entirely for world-space UI. The default toolbar buttons are left visible
    // but inert; the scene handles are the tool.
    public bool DrawToolbar(Paper paper, string id, Prowl.Scribe.FontFile font) => false;

    public bool OnSceneInput(Camera camera, Scene scene, Ray mouseRay, Float2 mousePos, bool viewportHovered)
    {
        if (_target == null)
            return false;

        // Revert overlay preview if play mode starts while we're active.
        if (Application.IsPlaying)
        {
            return false;
        }

        RectTransform? rt = _target.GameObject.RectTransform;
        GameCanvas? canvas = _target.GetCanvas();
        if (rt == null || canvas == null)
        {
            _hover = Handle.None;
            _active = Handle.None;
            return false;
        }

        RenderTexture? sceneRT = camera.Target;
        Float2? prevScreenOverride = GameCanvas.ScreenSizeOverride;
        if (sceneRT != null && sceneRT.Width > 0 && sceneRT.Height > 0)
            GameCanvas.ScreenSizeOverride = new Float2(sceneRT.Width, sceneRT.Height);

        try
        {
            // Pick up any edits applied on previous frames so ComputedRect is current.
            canvas.RebuildIfDirty();

            bool camNav = Input.GetMouseButton(1) || Input.GetMouseButton(2);
            if (camNav && _active == Handle.None)
            {
                _hover = Handle.None;
                return false;
            }

            Float4x4 designToWorld = canvas.CanvasToWorld;
            Float4x4 worldToDesign = designToWorld.Invert();

            Float3 originW = Float4x4.TransformPoint(Float3.Zero, designToWorld);
            Float3 rightW = Float4x4.TransformPoint(Float3.UnitX, designToWorld) - originW;
            Float3 upW = Float4x4.TransformPoint(Float3.UnitY, designToWorld) - originW;
            float worldPerPixel = Float3.Length(rightW);
            if (worldPerPixel < 1e-9f)
                return _active != Handle.None;
            rightW /= worldPerPixel;
            upW = Float3.Normalize(upW);
            Float3 normalW = Float3.Normalize(Float3.Cross(rightW, upW));

            if (!GizmoUtils.IntersectPlane(normalW, originW, mouseRay.Origin, mouseRay.Direction, out float tHit))
            {
                if (_active != Handle.None) return true;
                _hover = Handle.None;
                return false;
            }
            Float3 worldHit = mouseRay.Origin + mouseRay.Direction * tHit;
            Float3 designHit3 = Float4x4.TransformPoint(worldHit, worldToDesign);
            Float2 designHit = new(designHit3.X, designHit3.Y);

            Rect cr = rt.ComputedRect;
            if (cr.Size.X <= 0 || cr.Size.Y <= 0)
            {
                _hover = Handle.None;
                _active = Handle.None;
                return false;
            }

            Rect parentRect = ResolveParentRect(rt);

            Float3 camPos = camera.GameObject.Transform.Position;
            Float3 centerW = Float4x4.TransformPoint(
                new Float3(cr.Min.X + cr.Size.X * 0.5f, cr.Min.Y + cr.Size.Y * 0.5f, 0), designToWorld);
            float handleWorld = Maths.Max(Float3.Distance(camPos, centerW) * 0.018f, worldPerPixel * 4f);
            float pickRadius = handleWorld / worldPerPixel; // design pixels

            bool leftDown = Input.GetMouseButton(0);
            bool leftPressed = Input.GetMouseButtonDown(0);

            // ---- Begin a drag ----
            if (_active == Handle.None)
            {
                _hover = HitTest(rt, cr, parentRect, designHit, pickRadius);

                if (leftPressed && viewportHovered && !camNav)
                {
                    // Resize / pivot / anchor handles on the current target win first — that's
                    // the active edit affordance. For Handle.Move (clicked inside the rect body)
                    // and Handle.None we re-pick across every canvas so a click on a sibling /
                    // child UI element switches the selection instead of starting a drag on the
                    // currently-selected ancestor.
                    if (_hover != Handle.None && _hover != Handle.Move)
                    {
                        _active = _hover;
                        _dragStartDesign = designHit;
                        _dragStartRect = cr;
                        _dragStartState = LayoutState.Capture(rt);
                    }
                    else
                    {
                        GameObject? uiHit = UIPicker.Pick(scene, mouseRay);

                        if (uiHit == _target.GameObject)
                        {
                            // Topmost UI under cursor IS the current target — start a Move drag.
                            _active = Handle.Move;
                            _dragStartDesign = designHit;
                            _dragStartRect = cr;
                            _dragStartState = LayoutState.Capture(rt);
                        }
                        else if (uiHit != null)
                        {
                            // Different UI element under cursor — switch selection. The registry
                            // reactivates this editor against the new target on the next frame.
                            if (Input.IsCtrlPressed) Selection.ToggleSelection(uiHit);
                            else Selection.Select(uiHit);
                        }
                        else if (!Input.IsCtrlPressed && !Input.IsShiftPressed)
                        {
                            Selection.Clear();
                        }
                    }
                }
            }

            if (_active != Handle.None)
            {
                if (leftDown)
                {
                    ApplyDrag(rt, parentRect, designHit);
                    canvas.RebuildIfDirty();
                    EditorSceneManager.IsDirty = true;
                }
                else
                {
                    RegisterUndo(_target.GameObject, _dragStartState, LayoutState.Capture(rt));
                    _active = Handle.None;
                }
            }

            DrawHandles(rt, designToWorld, rightW, upW, handleWorld);
            return true;
        }
        finally
        {
            GameCanvas.ScreenSizeOverride = prevScreenOverride;
        }
    }

    // ================================================================
    //  Hit testing
    // ================================================================

    private static Handle HitTest(RectTransform rt, Rect cr, Rect parentRect, Float2 p, float radius)
    {
        Float2 min = cr.Min, max = cr.Max;
        Float2 midBottom = new((min.X + max.X) * 0.5f, min.Y);
        Float2 midTop = new((min.X + max.X) * 0.5f, max.Y);
        Float2 midLeft = new(min.X, (min.Y + max.Y) * 0.5f);
        Float2 midRight = new(max.X, (min.Y + max.Y) * 0.5f);

        Float2 pivot = new(min.X + rt.Pivot.X * cr.Size.X, min.Y + rt.Pivot.Y * cr.Size.Y);

        float aMinX = parentRect.Min.X + rt.AnchorMin.X * parentRect.Size.X;
        float aMaxX = parentRect.Min.X + rt.AnchorMax.X * parentRect.Size.X;
        float aMinY = parentRect.Min.Y + rt.AnchorMin.Y * parentRect.Size.Y;
        float aMaxY = parentRect.Min.Y + rt.AnchorMax.Y * parentRect.Size.Y;

        Handle best = Handle.None;
        float bestDist = radius;

        void Test(Handle h, Float2 c)
        {
            float d = Distance(p, c);
            if (d < bestDist)
            {
                bestDist = d;
                best = h;
            }
        }

        Test(Handle.ResizeBL, min);
        Test(Handle.ResizeBR, new Float2(max.X, min.Y));
        Test(Handle.ResizeTR, max);
        Test(Handle.ResizeTL, new Float2(min.X, max.Y));
        Test(Handle.ResizeB, midBottom);
        Test(Handle.ResizeT, midTop);
        Test(Handle.ResizeL, midLeft);
        Test(Handle.ResizeR, midRight);
        Test(Handle.Pivot, pivot);
        Test(Handle.AnchorBL, new Float2(aMinX, aMinY));
        Test(Handle.AnchorBR, new Float2(aMaxX, aMinY));
        Test(Handle.AnchorTR, new Float2(aMaxX, aMaxY));
        Test(Handle.AnchorTL, new Float2(aMinX, aMaxY));

        if (best != Handle.None)
            return best;

        // Inside the rect body — move.
        if (p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y)
            return Handle.Move;

        return Handle.None;
    }

    // ================================================================
    //  Drag application
    // ================================================================

    private void ApplyDrag(RectTransform rt, Rect parentRect, Float2 designHit)
    {
        Float2 delta = designHit - _dragStartDesign;
        Rect s = _dragStartRect;
        float minX = s.Min.X, minY = s.Min.Y, maxX = s.Max.X, maxY = s.Max.Y;

        switch (_active)
        {
            case Handle.Move:
                minX += delta.X; maxX += delta.X;
                minY += delta.Y; maxY += delta.Y;
                break;

            case Handle.ResizeL: minX += delta.X; break;
            case Handle.ResizeR: maxX += delta.X; break;
            case Handle.ResizeB: minY += delta.Y; break;
            case Handle.ResizeT: maxY += delta.Y; break;

            case Handle.ResizeBL: minX += delta.X; minY += delta.Y; break;
            case Handle.ResizeBR: maxX += delta.X; minY += delta.Y; break;
            case Handle.ResizeTR: maxX += delta.X; maxY += delta.Y; break;
            case Handle.ResizeTL: minX += delta.X; maxY += delta.Y; break;

            case Handle.Pivot:
            {
                Float2 newPivot = new(
                    Maths.Clamp((designHit.X - s.Min.X) / Maths.Max(s.Size.X, 1e-4f), 0f, 1f),
                    Maths.Clamp((designHit.Y - s.Min.Y) / Maths.Max(s.Size.Y, 1e-4f), 0f, 1f));
                rt.Pivot = newPivot;
                // Re-pivoting keeps the element where it is on screen.
                ApplyDesiredRect(rt, parentRect, s.Min.X, s.Min.Y, s.Size.X, s.Size.Y);
                return;
            }

            case Handle.AnchorBL:
            case Handle.AnchorBR:
            case Handle.AnchorTR:
            case Handle.AnchorTL:
                ApplyAnchorDrag(rt, parentRect, designHit);
                return;
        }

        // Move / resize — snap to whole pixels and keep a minimum size.
        minX = MathF.Round(minX); maxX = MathF.Round(maxX);
        minY = MathF.Round(minY); maxY = MathF.Round(maxY);

        if (maxX - minX < MinRectSize)
        {
            if (_active is Handle.ResizeL or Handle.ResizeBL or Handle.ResizeTL)
                minX = maxX - MinRectSize;
            else
                maxX = minX + MinRectSize;
        }
        if (maxY - minY < MinRectSize)
        {
            if (_active is Handle.ResizeB or Handle.ResizeBL or Handle.ResizeBR)
                minY = maxY - MinRectSize;
            else
                maxY = minY + MinRectSize;
        }

        ApplyDesiredRect(rt, parentRect, minX, minY, maxX - minX, maxY - minY);
    }

    private void ApplyAnchorDrag(RectTransform rt, Rect parentRect, Float2 designHit)
    {
        if (parentRect.Size.X <= 1e-4f || parentRect.Size.Y <= 1e-4f)
            return;

        float nx = Maths.Clamp((designHit.X - parentRect.Min.X) / parentRect.Size.X, 0f, 1f);
        float ny = Maths.Clamp((designHit.Y - parentRect.Min.Y) / parentRect.Size.Y, 0f, 1f);

        // Hold Ctrl to snap anchors to quarter steps
        if (Input.GetKey(KeyCode.ControlLeft) || Input.GetKey(KeyCode.ControlRight))
        {
            nx = MathF.Round(nx * 4f) / 4f;
            ny = MathF.Round(ny * 4f) / 4f;
        }

        Float2 min = rt.AnchorMin, max = rt.AnchorMax;
        switch (_active)
        {
            case Handle.AnchorBL: min.X = nx; min.Y = ny; break;
            case Handle.AnchorBR: max.X = nx; min.Y = ny; break;
            case Handle.AnchorTR: max.X = nx; max.Y = ny; break;
            case Handle.AnchorTL: min.X = nx; max.Y = ny; break;
        }

        // Keep min <= max: clamp the edge that was just dragged.
        if (min.X > max.X)
        {
            if (_active is Handle.AnchorBL or Handle.AnchorTL) min.X = max.X;
            else max.X = min.X;
        }
        if (min.Y > max.Y)
        {
            if (_active is Handle.AnchorBL or Handle.AnchorBR) min.Y = max.Y;
            else max.Y = min.Y;
        }

        rt.AnchorMin = min;
        rt.AnchorMax = max;

        ApplyDesiredRect(rt, parentRect,
            _dragStartRect.Min.X, _dragStartRect.Min.Y,
            _dragStartRect.Size.X, _dragStartRect.Size.Y);
    }

    private static void ApplyDesiredRect(RectTransform rt, Rect parentRect, float posX, float posY, float width, float height)
    {
        float pX = parentRect.Min.X, pY = parentRect.Min.Y;
        float pW = parentRect.Size.X, pH = parentRect.Size.Y;

        float aMinX = pX + rt.AnchorMin.X * pW;
        float aMaxX = pX + rt.AnchorMax.X * pW;
        float aMinY = pY + rt.AnchorMin.Y * pH;
        float aMaxY = pY + rt.AnchorMax.Y * pH;

        Float2 sizeDelta = rt.SizeDelta;
        Float2 anchored = rt.AnchoredPosition;

        // Horizontal — fixed width when the X anchors coincide, otherwise stretch.
        if (Maths.Abs(rt.AnchorMin.X - rt.AnchorMax.X) < 1e-6f)
        {
            sizeDelta.X = width;
            anchored.X = posX + rt.Pivot.X * width - aMinX;
        }
        else
        {
            sizeDelta.X = (aMaxX - aMinX) - width;
            anchored.X = posX - aMinX - sizeDelta.X * 0.5f;
        }

        // Vertical — same rule on Y.
        if (Maths.Abs(rt.AnchorMin.Y - rt.AnchorMax.Y) < 1e-6f)
        {
            sizeDelta.Y = height;
            anchored.Y = posY + rt.Pivot.Y * height - aMinY;
        }
        else
        {
            sizeDelta.Y = (aMaxY - aMinY) - height;
            anchored.Y = posY - aMinY - sizeDelta.Y * 0.5f;
        }

        rt.SizeDelta = sizeDelta;
        rt.AnchoredPosition = anchored;
    }

    // ================================================================
    //  Drawing
    // ================================================================

    private void DrawHandles(RectTransform rt, Float4x4 designToWorld, Float3 rightW, Float3 upW, float handleWorld)
    {
        Rect cr = rt.ComputedRect;
        Rect parentRect = ResolveParentRect(rt);
        float half = handleWorld * 0.5f;

        Float3 ToWorld(Float2 d) => Float4x4.TransformPoint(new Float3(d.X, d.Y, 0), designToWorld);

        // Rect outline — brightened while moving.
        bool moving = _active == Handle.Move || (_active == Handle.None && _hover == Handle.Move);
        Color outline = moving ? MoveColor : ResizeColor;
        Float3 bl = ToWorld(cr.Min);
        Float3 br = ToWorld(new Float2(cr.Max.X, cr.Min.Y));
        Float3 tr = ToWorld(cr.Max);
        Float3 tl = ToWorld(new Float2(cr.Min.X, cr.Max.Y));
        Debug.DrawLine(bl, br, outline);
        Debug.DrawLine(br, tr, outline);
        Debug.DrawLine(tr, tl, outline);
        Debug.DrawLine(tl, bl, outline);

        // Resize handles — corners full size, edge mid-points slightly smaller.
        Float2 mid = (cr.Min + cr.Max) * 0.5f;
        DrawHandle(Handle.ResizeBL, bl, rightW, upW, half);
        DrawHandle(Handle.ResizeBR, br, rightW, upW, half);
        DrawHandle(Handle.ResizeTR, tr, rightW, upW, half);
        DrawHandle(Handle.ResizeTL, tl, rightW, upW, half);
        DrawHandle(Handle.ResizeB, ToWorld(new Float2(mid.X, cr.Min.Y)), rightW, upW, half * 0.8f);
        DrawHandle(Handle.ResizeT, ToWorld(new Float2(mid.X, cr.Max.Y)), rightW, upW, half * 0.8f);
        DrawHandle(Handle.ResizeL, ToWorld(new Float2(cr.Min.X, mid.Y)), rightW, upW, half * 0.8f);
        DrawHandle(Handle.ResizeR, ToWorld(new Float2(cr.Max.X, mid.Y)), rightW, upW, half * 0.8f);

        // Pivot.
        Float2 pivot = new(cr.Min.X + rt.Pivot.X * cr.Size.X, cr.Min.Y + rt.Pivot.Y * cr.Size.Y);
        DrawHandle(Handle.Pivot, ToWorld(pivot), rightW, upW, half * 0.9f);

        // Anchor handles, in the parent's design space.
        float aMinX = parentRect.Min.X + rt.AnchorMin.X * parentRect.Size.X;
        float aMaxX = parentRect.Min.X + rt.AnchorMax.X * parentRect.Size.X;
        float aMinY = parentRect.Min.Y + rt.AnchorMin.Y * parentRect.Size.Y;
        float aMaxY = parentRect.Min.Y + rt.AnchorMax.Y * parentRect.Size.Y;
        DrawHandle(Handle.AnchorBL, ToWorld(new Float2(aMinX, aMinY)), rightW, upW, half * 0.85f);
        DrawHandle(Handle.AnchorBR, ToWorld(new Float2(aMaxX, aMinY)), rightW, upW, half * 0.85f);
        DrawHandle(Handle.AnchorTR, ToWorld(new Float2(aMaxX, aMaxY)), rightW, upW, half * 0.85f);
        DrawHandle(Handle.AnchorTL, ToWorld(new Float2(aMinX, aMaxY)), rightW, upW, half * 0.85f);
    }

    private void DrawHandle(Handle handle, Float3 centerW, Float3 rightW, Float3 upW, float half)
    {
        Color baseColor = handle switch
        {
            Handle.Pivot => PivotColor,
            Handle.AnchorBL or Handle.AnchorBR or Handle.AnchorTR or Handle.AnchorTL => AnchorColor,
            _ => ResizeColor,
        };
        bool hot = _active == handle || (_active == Handle.None && _hover == handle);
        Color color = hot ? HotColor : baseColor;
        float s = hot ? half * 1.3f : half;

        Float3 r = rightW * s;
        Float3 u = upW * s;
        Float3 a = centerW - r - u;
        Float3 b = centerW + r - u;
        Float3 c = centerW + r + u;
        Float3 d = centerW - r + u;


        Debug.DrawIcon(_handleTexture, centerW, 7.5f, color);
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private static Rect ResolveParentRect(RectTransform rt)
    {
        RectTransform? parent = rt.GameObject.Parent?.RectTransform;
        // A properly parented UI element always has a RectTransform parent (the canvas
        // ensures this). Fall back to the element's own rect so anchors degenerate to
        // a no-op rather than throwing.
        return parent != null ? parent.ComputedRect : rt.ComputedRect;
    }

    private void RegisterUndo(GameObject go, LayoutState before, LayoutState after)
    {
        if (before.Matches(after))
            return;

        Guid id = go.Identifier;
        Undo.RegisterAction("Modify UI Rect",
            () => { RectTransform? rt = Undo.FindGO(id)?.RectTransform; if (rt != null) before.ApplyTo(rt); },
            () => { RectTransform? rt = Undo.FindGO(id)?.RectTransform; if (rt != null) after.ApplyTo(rt); });
    }

    private static float Distance(Float2 a, Float2 b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
