// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime.UI;

/// <summary>
/// A scrollable view. Drag (or scroll-wheel over) the view to pan <see cref="Content"/> within
/// <see cref="Viewport"/>, clamped so the content always covers the viewport. Pair the viewport with a
/// <see cref="RectMask"/> to clip the overflow. Panning is done in the canvas's design-pixel space via
/// <see cref="PointerEventData.DesignPosition"/>, so no screen/scale conversion is needed.
/// </summary>
[AddComponentMenu("UI/Scroll View")]
[ComponentIcon("")] // arrows
[RequireComponent(typeof(RectTransform))]
public class UIScrollRect : UIBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
{
    [SerializeField] private RectTransform? _content;
    /// <summary>The moved element. Its children scroll inside the viewport.</summary>
    public RectTransform? Content { get => _content; set => _content = value; }

    [SerializeField] private RectTransform? _viewport;
    /// <summary>The clipping window; defaults to this element's own rect when unset.</summary>
    public RectTransform? Viewport { get => _viewport; set => _viewport = value; }

    [SerializeField] private UIScrollbar? _horizontalScrollbar;
    /// <summary>Optional horizontal scrollbar, kept in two-way sync with the content position.</summary>
    public UIScrollbar? HorizontalScrollbar { get => _horizontalScrollbar; set => _horizontalScrollbar = value; }

    [SerializeField] private UIScrollbar? _verticalScrollbar;
    /// <summary>Optional vertical scrollbar, kept in two-way sync with the content position.</summary>
    public UIScrollbar? VerticalScrollbar { get => _verticalScrollbar; set => _verticalScrollbar = value; }

    /// <summary>How a scrollbar shows/hides relative to whether its axis actually needs scrolling.</summary>
    public enum ScrollbarVisibilityMode
    {
        /// <summary>Always visible; the viewport always reserves room for it.</summary>
        Permanent,
        /// <summary>Hidden when the content fits, but the viewport keeps the reserved room (no expand).</summary>
        AutoHide,
        /// <summary>Hidden when the content fits, and the viewport expands to fill the freed room.</summary>
        AutoHideAndExpandViewport,
    }

    [SerializeField] private ScrollbarVisibilityMode _horizontalScrollbarVisibility = ScrollbarVisibilityMode.AutoHideAndExpandViewport;
    public ScrollbarVisibilityMode HorizontalScrollbarVisibility { get => _horizontalScrollbarVisibility; set => _horizontalScrollbarVisibility = value; }

    [SerializeField] private ScrollbarVisibilityMode _verticalScrollbarVisibility = ScrollbarVisibilityMode.AutoHideAndExpandViewport;
    public ScrollbarVisibilityMode VerticalScrollbarVisibility { get => _verticalScrollbarVisibility; set => _verticalScrollbarVisibility = value; }

    /// <summary>Extra gap (design pixels) between the viewport and a visible horizontal scrollbar.</summary>
    [SerializeField] private float _horizontalScrollbarSpacing = 0f;
    public float HorizontalScrollbarSpacing { get => _horizontalScrollbarSpacing; set => _horizontalScrollbarSpacing = value; }

    /// <summary>Extra gap (design pixels) between the viewport and a visible vertical scrollbar.</summary>
    [SerializeField] private float _verticalScrollbarSpacing = 0f;
    public float VerticalScrollbarSpacing { get => _verticalScrollbarSpacing; set => _verticalScrollbarSpacing = value; }

    [SerializeField] private bool _horizontal = true;
    public bool Horizontal { get => _horizontal; set => _horizontal = value; }

    [SerializeField] private bool _vertical = true;
    public bool Vertical { get => _vertical; set => _vertical = value; }

    /// <summary>Design pixels moved per unit of scroll-wheel delta.</summary>
    [SerializeField] private float _scrollSensitivity = 30f;
    public float ScrollSensitivity { get => _scrollSensitivity; set => _scrollSensitivity = value; }

    /// <summary>Keep coasting after a flick.</summary>
    [SerializeField] private bool _inertia = true;
    public bool Inertia { get => _inertia; set => _inertia = value; }

    /// <summary>Fraction of velocity retained per second while coasting (0..1).</summary>
    [SerializeField] private float _decelerationRate = 0.135f;
    public float DecelerationRate { get => _decelerationRate; set => _decelerationRate = Maths.Clamp(value, 0f, 1f); }

    /// <summary>Fires when the content position changes (dragging, wheel, or coasting).</summary>
    public event Action? OnValueChanged;

    [SerializeIgnore] private bool _dragging;
    [SerializeIgnore] private Float2 _dragStartContent;
    [SerializeIgnore] private Float2 _dragStartPointer;
    [SerializeIgnore] private Float2 _velocity;
    [SerializeIgnore] private Float2 _prevContentPos;

    // No geometry of its own; the background/mask/content draw themselves.
    public override void GenerateMesh(UIMeshBuilder builder, in UIContext context) { }

    private Rect ViewportRect() => (_viewport ?? GameObject.RectTransform)!.ComputedRect;

    public void OnBeginDrag(PointerEventData e)
    {
        if (_content == null) return;
        _dragging = true;
        _velocity = Float2.Zero;
        _dragStartContent = _content.AnchoredPosition;
        _dragStartPointer = e.DesignPosition;
        _prevContentPos = _content.AnchoredPosition;
    }

    public void OnDrag(PointerEventData e)
    {
        if (_content == null || !_dragging) return;

        Float2 delta = e.DesignPosition - _dragStartPointer;
        Float2 target = _dragStartContent;
        if (_horizontal) target.X += delta.X;
        if (_vertical) target.Y += delta.Y;

        SetContentPosition(ClampContent(target));
        e.Use();
    }

    public void OnEndDrag(PointerEventData e)
    {
        _dragging = false;
    }

    public void OnScroll(PointerEventData e)
    {
        if (_content == null) return;

        Float2 pos = _content.AnchoredPosition;
        // Wheel scrolls the vertical axis when it exists, otherwise the horizontal one.
        if (_vertical) pos.Y += e.ScrollDelta * _scrollSensitivity;      // +Y up: wheel-up moves content up, revealing lower content
        else if (_horizontal) pos.X += e.ScrollDelta * _scrollSensitivity;

        _velocity = Float2.Zero;
        SetContentPosition(ClampContent(pos));
        e.Use();
    }

    public override void Update()
    {
        if (!Application.IsPlaying || _content == null) return;

        // Auto-hide / expand scrollbars based on whether each axis needs to scroll (play mode only; in
        // edit mode the authored layout with both bars shown is kept).
        UpdateScrollbarVisibility();

        // Two-way scrollbar binding: while a handle is held it drives the content, otherwise the content
        // position drives the scrollbar (and its handle size).
        SyncScrollbar(_horizontalScrollbar, horizontal: true);
        SyncScrollbar(_verticalScrollbar, horizontal: false);

        if (_dragging)
        {
            // Track velocity from the drag so a release can coast.
            float dt = Maths.Max(Time.DeltaTime, 1e-4f);
            Float2 cur = _content.AnchoredPosition;
            _velocity = (cur - _prevContentPos) / dt;
            _prevContentPos = cur;
            return;
        }

        if (!_inertia || Maths.Abs(_velocity.X) + Maths.Abs(_velocity.Y) < 1f) { _velocity = Float2.Zero; return; }

        float decay = MathF.Pow(_decelerationRate, Time.DeltaTime);
        _velocity *= decay;

        Float2 target = _content.AnchoredPosition + _velocity * Time.DeltaTime;
        Float2 clamped = ClampContent(target);
        // Kill velocity on the axis that hit a clamp edge so it doesn't fight the wall.
        if (clamped.X != target.X) _velocity.X = 0f;
        if (clamped.Y != target.Y) _velocity.Y = 0f;
        SetContentPosition(clamped);
    }

    private void SetContentPosition(Float2 pos)
    {
        if (_content == null) return;
        if (_content.AnchoredPosition == pos) return;
        _content.AnchoredPosition = pos;
        try { OnValueChanged?.Invoke(); }
        catch (Exception ex) { Debug.LogError($"[UIScrollRect] OnValueChanged on '{Name}' threw: {ex.Message}\n{ex.StackTrace}"); }
    }

    /// <summary>
    /// Clamps a candidate content <see cref="RectTransform.AnchoredPosition"/> so the content keeps
    /// covering the viewport (no empty gutters). A unit change in AnchoredPosition moves the content's
    /// ComputedRect by the same amount in design pixels, so the valid range is expressed relative to the
    /// current position. Rects are from the last layout, which is fine for per-frame panning.
    /// </summary>
    private Float2 ClampContent(Float2 candidate)
    {
        if (_content == null) return candidate;

        Rect vp = ViewportRect();
        Rect ct = _content.ComputedRect;
        Float2 cur = _content.AnchoredPosition;

        if (!_horizontal) candidate.X = cur.X;
        else candidate.X = ClampAxis(candidate.X, cur.X, ct.Min.X, ct.Max.X, ct.Size.X, vp.Min.X, vp.Max.X, vp.Size.X, pinToMax: false);

        if (!_vertical) candidate.Y = cur.Y;
        else candidate.Y = ClampAxis(candidate.Y, cur.Y, ct.Min.Y, ct.Max.Y, ct.Size.Y, vp.Min.Y, vp.Max.Y, vp.Size.Y, pinToMax: true);

        return candidate;
    }

    // ============================================================
    // Scrollbar binding
    // ============================================================

    /// <summary>
    /// Shows/hides each scrollbar based on whether its axis needs to scroll, and (for
    /// <see cref="ScrollbarVisibilityMode.AutoHideAndExpandViewport"/>) drives the viewport rect and the
    /// scrollbar lengths so the viewport expands into the room a hidden bar would have taken. Mirrors
    /// Unity's ScrollRect: the viewport and both scrollbars must be children of this root.
    /// </summary>
    private void UpdateScrollbarVisibility()
    {
        if (_content == null) return;

        Rect rootRect = GameObject.RectTransform!.ComputedRect;
        Rect ct = _content.ComputedRect;
        float fullW = rootRect.Size.X, fullH = rootRect.Size.Y;
        float contentW = ct.Size.X, contentH = ct.Size.Y;

        RectTransform? vRt = _verticalScrollbar?.GameObject.RectTransform;
        RectTransform? hRt = _horizontalScrollbar?.GameObject.RectTransform;
        float vbarW = vRt != null ? MathF.Abs(vRt.SizeDelta.X) : 0f; // fixed thickness (line-anchored axis)
        float hbarH = hRt != null ? MathF.Abs(hRt.SizeDelta.Y) : 0f;

        bool vExpand = _verticalScrollbarVisibility == ScrollbarVisibilityMode.AutoHideAndExpandViewport;
        bool hExpand = _horizontalScrollbarVisibility == ScrollbarVisibilityMode.AutoHideAndExpandViewport;

        // A needed bar on one axis (in expand mode) shrinks the opposite viewport dimension, which can in
        // turn make the other axis need a bar - resolve with a couple of passes.
        bool hNeeded = false, vNeeded = false;
        for (int i = 0; i < 2; i++)
        {
            float rw = (vExpand && vNeeded) ? vbarW + _verticalScrollbarSpacing : 0f;
            float rh = (hExpand && hNeeded) ? hbarH + _horizontalScrollbarSpacing : 0f;
            hNeeded = _horizontal && _horizontalScrollbar != null && contentW > (fullW - rw) + 0.01f;
            vNeeded = _vertical && _verticalScrollbar != null && contentH > (fullH - rh) + 0.01f;
        }

        bool vVisible = _verticalScrollbar != null && (_verticalScrollbarVisibility == ScrollbarVisibilityMode.Permanent || vNeeded);
        bool hVisible = _horizontalScrollbar != null && (_horizontalScrollbarVisibility == ScrollbarVisibilityMode.Permanent || hNeeded);

        // Expand mode frees the reserved room when the bar hides; the other modes keep it reserved.
        bool vReserve = _verticalScrollbar != null && _vertical && (!vExpand || vNeeded);
        bool hReserve = _horizontalScrollbar != null && _horizontal && (!hExpand || hNeeded);
        float reserveW = vReserve ? vbarW + _verticalScrollbarSpacing : 0f;
        float reserveH = hReserve ? hbarH + _horizontalScrollbarSpacing : 0f;

        if (_verticalScrollbar != null && _verticalScrollbar.GameObject.Enabled != vVisible)
            _verticalScrollbar.GameObject.Enabled = vVisible;
        if (_horizontalScrollbar != null && _horizontalScrollbar.GameObject.Enabled != hVisible)
            _horizontalScrollbar.GameObject.Enabled = hVisible;

        // Viewport fills the root, inset by the reserved gutters on the right and bottom.
        if (_viewport != null)
        {
            _viewport.AnchorMin = Float2.Zero;
            _viewport.AnchorMax = Float2.One;
            _viewport.Pivot = new Float2(0.5f, 0.5f);
            _viewport.SizeDelta = new Float2(-reserveW, -reserveH);
            _viewport.AnchoredPosition = new Float2(-reserveW * 0.5f, reserveH * 0.5f);
        }

        // Each bar is shortened by the other's reserved gutter so they leave the corner free.
        if (vRt != null)
        {
            vRt.SizeDelta = new Float2(vbarW, -reserveH);
            vRt.AnchoredPosition = new Float2(0f, reserveH * 0.5f);
        }
        if (hRt != null)
        {
            hRt.SizeDelta = new Float2(-reserveW, hbarH);
            hRt.AnchoredPosition = new Float2(-reserveW * 0.5f, 0f);
        }
    }

    private void SyncScrollbar(UIScrollbar? bar, bool horizontal)
    {
        if (bar == null || _content == null) return;

        if (bar.IsPressed)
        {
            // The user is dragging the handle - it drives the content position.
            if (horizontal) SetHorizontalNormalized(bar.Value);
            else SetVerticalNormalized(bar.Value);
        }
        else
        {
            bar.SetValueWithoutNotify(horizontal ? HorizontalNormalized() : VerticalNormalized());
        }

        bar.Size = horizontal ? HorizontalVisibleFraction() : VerticalVisibleFraction();
    }

    /// <summary>0 = content's left edge at the viewport's left, 1 = fully scrolled right.</summary>
    public float HorizontalNormalized()
    {
        Rect vp = ViewportRect(); Rect ct = _content!.ComputedRect;
        float over = ct.Size.X - vp.Size.X;
        return over > 1e-4f ? Maths.Clamp((vp.Min.X - ct.Min.X) / over, 0f, 1f) : 0f;
    }

    /// <summary>0 = content's top edge at the viewport's top, 1 = fully scrolled down.</summary>
    public float VerticalNormalized()
    {
        Rect vp = ViewportRect(); Rect ct = _content!.ComputedRect;
        float over = ct.Size.Y - vp.Size.Y;
        return over > 1e-4f ? Maths.Clamp((ct.Max.Y - vp.Max.Y) / over, 0f, 1f) : 0f;
    }

    private float HorizontalVisibleFraction()
    {
        Rect vp = ViewportRect(); Rect ct = _content!.ComputedRect;
        return ct.Size.X > 1e-4f ? Maths.Clamp(vp.Size.X / ct.Size.X, 0.05f, 1f) : 1f;
    }

    private float VerticalVisibleFraction()
    {
        Rect vp = ViewportRect(); Rect ct = _content!.ComputedRect;
        return ct.Size.Y > 1e-4f ? Maths.Clamp(vp.Size.Y / ct.Size.Y, 0.05f, 1f) : 1f;
    }

    public void SetHorizontalNormalized(float f)
    {
        if (_content == null) return;
        Rect vp = ViewportRect(); Rect ct = _content.ComputedRect;
        float over = ct.Size.X - vp.Size.X;
        if (over <= 1e-4f) return;
        float dx = (vp.Min.X - Maths.Clamp(f, 0f, 1f) * over) - ct.Min.X;
        if (Maths.Abs(dx) < 1e-4f) return;
        _velocity = Float2.Zero;
        SetContentPosition(_content.AnchoredPosition + new Float2(dx, 0f));
    }

    public void SetVerticalNormalized(float f)
    {
        if (_content == null) return;
        Rect vp = ViewportRect(); Rect ct = _content.ComputedRect;
        float over = ct.Size.Y - vp.Size.Y;
        if (over <= 1e-4f) return;
        float dy = (vp.Max.Y + Maths.Clamp(f, 0f, 1f) * over) - ct.Max.Y;
        if (Maths.Abs(dy) < 1e-4f) return;
        _velocity = Float2.Zero;
        SetContentPosition(_content.AnchoredPosition + new Float2(0f, dy));
    }

    // pinToMax: when the content fits inside the viewport, pin which edge stays aligned - top for the
    // vertical axis (+Y up), left for horizontal.
    private static float ClampAxis(float candidate, float cur, float ctMin, float ctMax, float ctSize,
        float vpMin, float vpMax, float vpSize, bool pinToMax)
    {
        if (ctSize <= vpSize)
        {
            // Content fits: single valid position.
            return pinToMax ? cur + (vpMax - ctMax) : cur + (vpMin - ctMin);
        }
        float a = cur + (vpMax - ctMax); // content's high edge reaches viewport's high edge
        float b = cur + (vpMin - ctMin); // content's low edge reaches viewport's low edge
        return Maths.Clamp(candidate, Maths.Min(a, b), Maths.Max(a, b));
    }
}
