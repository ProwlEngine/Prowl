// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Editor.Core;
using Prowl.Editor.GUI.SceneView;
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

    /// <summary>Snapshot of the layout fields this editor mutates - used for undo.</summary>
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

    // Drag anchoring - captured the frame a handle is grabbed.
    private Float2 _dragStartDesign;
    private Rect _dragStartRect;
    private LayoutState _dragStartState;

    // Move click-vs-drag: a press inside the rect body tentatively grabs Move; if the pointer doesn't
    // travel past ClickThreshold before release, it's treated as a plain selection click instead of a move.
    private bool _moveIsClick;
    private Float2 _dragStartMouse;
    private const float ClickThreshold = 4f;

    // Coordinate frame frozen at grab. The element frame (BuildItemModel) depends on ComputedRect,
    // which changes as we resize/move, so the drag must keep a fixed frame or the size jitters as the
    // frame shifts under the cursor each frame (a feedback loop).
    private Float4x4 _dragWorldToFrame;
    private Float2 _dragFramePivot;
    private Float3 _dragPlaneNormal;
    private Float3 _dragPlaneOrigin;

    public int Priority => 0;

    public void OnActivate(GameObject target)
    {
        _target = target.GetComponent<UIBehaviour>();
    }

    public void OnDeactivate()
    {
        _target = null;
        _hover = Handle.None;
        _active = Handle.None;
    }

    // The transform gizmo is meaningless for a RectTransform (its layout is driven by
    // anchors / pivot / size-delta, not Transform.Position), so this editor takes over
    // input entirely for world-space UI. The default toolbar buttons are left visible
    // but inert; the scene handles are the tool.
    public bool DrawToolbar(Paper paper, string id, Prowl.Scribe.FontFile font) => false;

    public bool OnSceneInput(Camera camera, Scene scene, Rect viewport, Ray mouseRay, Float2 mousePos, bool viewportHovered)
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

            Rect cr = rt.ComputedRect;
            if (cr.Size.X <= 0 || cr.Size.Y <= 0)
            {
                _hover = Handle.None;
                _active = Handle.None;
                return false;
            }

            // Work in the element's own model frame (BuildItemModel = CanvasToWorld * BuildRectModel),
            // so the outline, handles and drag follow the element's (and its parents') rotation and
            // scale rather than the flat canvas plane. The element's pivot-centered local space maps to
            // the canvas layout rect by a translation of the pivot position, so the mouse hit is shifted
            // by that pivot and the existing canvas-space hit-test / drag math is reused unchanged.
            Float4x4 frameToWorld = canvas.BuildItemModel(_target);
            Float4x4 worldToFrame = frameToWorld.Invert();
            Float2 pivotCanvasPos = new(cr.Min.X + rt.Pivot.X * cr.Size.X, cr.Min.Y + rt.Pivot.Y * cr.Size.Y);

            Float3 originW = Float4x4.TransformPoint(Float3.Zero, frameToWorld);
            Float3 rightW = Float4x4.TransformPoint(Float3.UnitX, frameToWorld) - originW;
            Float3 upW = Float4x4.TransformPoint(Float3.UnitY, frameToWorld) - originW;
            float worldPerPixel = Float3.Length(rightW);
            if (worldPerPixel < 1e-9f)
                return _active != Handle.None;
            rightW /= worldPerPixel;
            upW = Float3.Normalize(upW);
            Float3 normalW = Float3.Normalize(Float3.Cross(rightW, upW));

            Rect parentRect = ResolveParentRect(rt, canvas.RootRect);

            Float3 camPos = camera.GameObject.Transform.Position;
            Float3 centerW = Float4x4.TransformPoint(
                new Float3(cr.Min.X + cr.Size.X * 0.5f - pivotCanvasPos.X, cr.Min.Y + cr.Size.Y * 0.5f - pivotCanvasPos.Y, 0), frameToWorld);
            float handleWorld = Maths.Max(Float3.Distance(camPos, centerW) * 0.018f, worldPerPixel * 4f);
            float pickRadius = handleWorld / worldPerPixel; // design pixels

            // Draw the rect outline + handles every frame, BEFORE any input-related early-out, so they
            // never flicker off while navigating the camera or when the pointer leaves the canvas plane.
            DrawHandles(rt, parentRect, frameToWorld, pivotCanvasPos, rightW, upW, handleWorld);

            // Camera navigation (RMB/MMB) takes over: don't hit-test or drag while orbiting / panning.
            bool camNav = Input.GetMouseButton(1) || Input.GetMouseButton(2);
            if (camNav && _active == Handle.None)
            {
                _hover = Handle.None;
                return false;
            }

            // While dragging, use the frame captured at grab so resizing the element doesn't shift the
            // frame under the cursor; while only hovering, the live frame tracks the current rect.
            bool dragging = _active != Handle.None;
            Float3 planeNormal = dragging ? _dragPlaneNormal : normalW;
            Float3 planeOrigin = dragging ? _dragPlaneOrigin : originW;
            Float4x4 hitWorldToFrame = dragging ? _dragWorldToFrame : worldToFrame;
            Float2 hitPivot = dragging ? _dragFramePivot : pivotCanvasPos;

            if (!GizmoUtils.IntersectPlane(planeNormal, planeOrigin, mouseRay.Origin, mouseRay.Direction, out float tHit))
            {
                if (_active != Handle.None) return true;
                _hover = Handle.None;
                return false;
            }
            Float3 worldHit = mouseRay.Origin + mouseRay.Direction * tHit;
            // Map the world hit into the element's local frame, then shift by the pivot so it lands in
            // the same canvas layout space HitTest / ApplyDrag operate in (un-rotated, un-scaled).
            Float3 localHit = Float4x4.TransformPoint(worldHit, hitWorldToFrame);
            Float2 designHit = new(localHit.X + hitPivot.X, localHit.Y + hitPivot.Y);

            bool leftDown = Input.GetMouseButton(0);
            bool leftPressed = Input.GetMouseButtonDown(0);
            Handle activeBefore = _active;

            // ---- Begin a drag ----
            if (_active == Handle.None)
            {
                _hover = HitTest(rt, cr, parentRect, designHit, pickRadius);

                if (leftPressed && viewportHovered && !camNav)
                {
                    if (_hover != Handle.None && _hover != Handle.Move)
                    {
                        // A resize/pivot/anchor handle on the selected element always wins - clicking it
                        // edits the element (e.g. resizing something behind a panel), never re-selects.
                        BeginDrag(_hover, designHit, cr, rt);
                        _moveIsClick = false;
                    }
                    else if (_hover == Handle.Move)
                    {
                        // Inside the selected element's body (Move covers the whole rect): tentatively
                        // grab Move. Release decides - a drag past ClickThreshold moves the element, a
                        // click in place re-selects whatever is under the cursor. This lets you drag-move
                        // a selected element even when another one is drawn in front of it.
                        BeginDrag(Handle.Move, designHit, cr, rt);
                        _moveIsClick = true;
                        _dragStartMouse = mousePos;
                    }
                    else
                    {
                        // Outside the selected rect: select the top-most element under the cursor.
                        GameObject? topMost = UIPicker.Pick(scene, mouseRay);
                        if (topMost != null && !ReferenceEquals(topMost, _target.GameObject))
                        {
                            if (Input.IsCtrlPressed) Selection.ToggleSelection(topMost);
                            else Selection.Select(topMost);
                        }
                        else if (topMost == null && !Input.IsCtrlPressed && !Input.IsShiftPressed)
                        {
                            Selection.Clear();
                        }
                    }
                }
            }

            // A drag just began this frame: freeze the coordinate frame for the rest of it.
            if (activeBefore == Handle.None && _active != Handle.None)
            {
                _dragWorldToFrame = worldToFrame;
                _dragFramePivot = pivotCanvasPos;
                _dragPlaneNormal = normalW;
                _dragPlaneOrigin = originW;
            }

            if (_active != Handle.None)
            {
                if (leftDown)
                {
                    // A pending Move stays put until the pointer travels past the click threshold; once
                    // it does, it becomes a real drag and starts moving the element.
                    if (_active == Handle.Move && _moveIsClick && Distance(mousePos, _dragStartMouse) > ClickThreshold)
                        _moveIsClick = false;

                    if (!(_active == Handle.Move && _moveIsClick))
                    {
                        ApplyDrag(rt, parentRect, designHit);
                        canvas.RebuildIfDirty();
                        EditorSceneManager.IsDirty = true;
                    }
                }
                else if (_active == Handle.Move && _moveIsClick)
                {
                    // Released without dragging: treat as a selection click, not a move -> select the
                    // top-most element under the cursor (the one in front), leaving the rect untouched.
                    GameObject? topMost = UIPicker.Pick(scene, mouseRay);
                    if (topMost != null && !ReferenceEquals(topMost, _target.GameObject))
                    {
                        if (Input.IsCtrlPressed) Selection.ToggleSelection(topMost);
                        else Selection.Select(topMost);
                    }
                    _active = Handle.None;
                    _moveIsClick = false;
                }
                else
                {
                    RegisterUndo(_target.GameObject, _dragStartState, LayoutState.Capture(rt));
                    _active = Handle.None;
                }
            }

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

        // Inside the rect body - move.
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

        // Move / resize - keep a minimum size. (No per-frame pixel rounding: it made the size flicker
        // as the cursor moved sub-pixel; the drag tracks the cursor smoothly instead.)
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

        // Inverse of RectTransform.ComputeRect - one formula for fixed and stretched anchors:
        //   sizeDelta   = desiredSize - anchorSpan
        //   anchoredPos = desiredMin - anchorMin + pivot * sizeDelta
        Float2 sizeDelta, anchored;
        sizeDelta.X = width - (aMaxX - aMinX);
        anchored.X = posX - aMinX + rt.Pivot.X * sizeDelta.X;
        sizeDelta.Y = height - (aMaxY - aMinY);
        anchored.Y = posY - aMinY + rt.Pivot.Y * sizeDelta.Y;

        rt.SizeDelta = sizeDelta;
        rt.AnchoredPosition = anchored;
    }

    // ================================================================
    //  Drawing
    // ================================================================

    private void DrawHandles(RectTransform rt, Rect parentRect, Float4x4 frameToWorld, Float2 pivotCanvasPos, Float3 rightW, Float3 upW, float handleWorld)
    {
        Rect cr = rt.ComputedRect;
        float half = handleWorld * 0.5f;

        // Canvas layout point -> element local (subtract pivot) -> world (element model). Follows rotation/scale.
        Float3 ToWorld(Float2 d) => Float4x4.TransformPoint(new Float3(d.X - pivotCanvasPos.X, d.Y - pivotCanvasPos.Y, 0), frameToWorld);

        // Rect outline - brightened while moving.
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

        // Resize handles - corners full size, edge mid-points slightly smaller.
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

    private void BeginDrag(Handle handle, Float2 designHit, Rect cr, RectTransform rt)
    {
        _active = handle;
        _dragStartDesign = designHit;
        _dragStartRect = cr;
        _dragStartState = LayoutState.Capture(rt);
    }

    private static Rect ResolveParentRect(RectTransform rt, Rect canvasRootRect)
    {
        RectTransform? parent = rt.GameObject.Parent?.RectTransform;
        // A top-level element's parent is the canvas, which has no RectTransform. Anchor against the
        // canvas ROOT rect - never the element's own rect, which would move/resize with the element
        // and feed back into layout (the anchor reference shifting each frame = the drag jitter).
        return parent != null ? parent.ComputedRect : canvasRootRect;
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
