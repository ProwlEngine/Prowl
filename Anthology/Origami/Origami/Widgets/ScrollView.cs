// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Vector;

using SDColor = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// The scroll view's live viewport, handed to the <see cref="ScrollViewBuilder.Body(Action{ScrollViewport})"/>
/// callback. Content is offset by (<see cref="ScrollX"/>, <see cref="ScrollY"/>); <see cref="Width"/> /
/// <see cref="Height"/> are the visible inner area (padding and any scrollbar already subtracted).
/// Use these to virtualize long lists: render only the rows intersecting [ScrollY, ScrollY+Height].
/// </summary>
public readonly struct ScrollViewport
{
    public readonly float ScrollX;
    public readonly float ScrollY;
    public readonly float Width;
    public readonly float Height;

    public ScrollViewport(float scrollX, float scrollY, float width, float height)
    {
        ScrollX = scrollX; ScrollY = scrollY; Width = width; Height = height;
    }
}

/// <summary>
/// Fluent builder for an Origami scroll view. Construct via <see cref="Origami.ScrollView"/>;
/// chain modifiers; call <see cref="Body"/> to render.
/// </summary>
/// <remarks>
/// <para>Provides a clipped viewport that scrolls vertically (default) and/or horizontally.
/// Wheel scrolls the vertical axis; <c>Shift+wheel</c> scrolls horizontal when enabled.
/// Both scrollbar thumbs are draggable when their axis overflows.</para>
/// <para>Programmatic positioning: <see cref="Origami.ScrollTo"/> registers a target offset
/// keyed by ID; the next render of that scroll view applies it.</para>
/// </remarks>
public sealed class ScrollViewBuilder
{
    // ── Pending programmatic scroll requests ──────────────────────────────
    // Static so callers can request a scroll without holding a builder.
    // Cleared once consumed by the matching ScrollView render.
    internal static readonly Dictionary<string, Float2> s_pendingScrollTo = new();

    // Delta-based scroll nudges (added to current scroll, consumed each frame)
    internal static readonly Dictionary<string, Float2> s_pendingScrollBy = new();

    private readonly Paper _paper;
    private readonly string _id;
    private readonly float _width;
    private readonly float _height;
    private readonly OrigamiTheme _theme;

    private OrigamiVariant _variant = OrigamiVariant.Default;
    private float _padLeft, _padRight, _padTop, _padBottom;
    private float _colSpacing;
    private bool _vertical = true;
    private bool _horizontal;
    private bool _forceScrollbar;
    private float _scrollbarSize = 6f;
    private float _wheelStep = 30f;
    private bool _trapScroll = true;
    private bool _smoothScroll = true;
    private bool _overlayScrollbars = true;
    private bool _autoHideScrollbars = true;

    internal ScrollViewBuilder(Paper paper, string id, float width, float height, OrigamiTheme theme)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _width = width;
        _height = height;
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    // ── Variant ────────────────────────────────────────────────────────

    public ScrollViewBuilder Variant(OrigamiVariant variant) { _variant = variant; return this; }
    public ScrollViewBuilder Primary() => Variant(OrigamiVariant.Primary);
    public ScrollViewBuilder Success() => Variant(OrigamiVariant.Success);
    public ScrollViewBuilder Warning() => Variant(OrigamiVariant.Warning);
    public ScrollViewBuilder Danger()  => Variant(OrigamiVariant.Danger);
    public ScrollViewBuilder Info()    => Variant(OrigamiVariant.Info);
    public ScrollViewBuilder Subtle()  => Variant(OrigamiVariant.Subtle);

    // ── Padding ────────────────────────────────────────────────────────

    public ScrollViewBuilder Padding(float all)
    {
        _padLeft = _padRight = _padTop = _padBottom = all;
        return this;
    }
    public ScrollViewBuilder Padding(float horizontal, float vertical)
    {
        _padLeft = _padRight = horizontal;
        _padTop = _padBottom = vertical;
        return this;
    }
    public ScrollViewBuilder Padding(float left, float right, float top, float bottom)
    {
        _padLeft = left; _padRight = right;
        _padTop = top;   _padBottom = bottom;
        return this;
    }

    // ── Behaviour ──────────────────────────────────────────────────────

    /// <summary>Spacing between stacked children. Forwarded to the content column's <c>ColBetween</c>.</summary>
    public ScrollViewBuilder ColSpacing(float spacing) { _colSpacing = spacing; return this; }

    /// <summary>Enable vertical scrolling (default <c>true</c>).</summary>
    public ScrollViewBuilder Vertical(bool enabled = true) { _vertical = enabled; return this; }

    /// <summary>
    /// Enable horizontal scrolling (default <c>false</c>). When on, content can extend beyond
    /// the viewport's width; the user scrolls via the bottom thumb or <c>Shift+wheel</c>.
    /// </summary>
    public ScrollViewBuilder Horizontal(bool enabled = true) { _horizontal = enabled; return this; }

    /// <summary>Always show scrollbar tracks, even when content fits.</summary>
    public ScrollViewBuilder ForceScrollbar(bool force = true) { _forceScrollbar = force; return this; }

    /// <summary>Scrollbar thumb thickness (default 6px).</summary>
    public ScrollViewBuilder ScrollbarSize(float size) { _scrollbarSize = MathF.Max(2f, size); return this; }

    /// <summary>Scroll-wheel step in pixels per click (default 30).</summary>
    public ScrollViewBuilder WheelStep(float step) { _wheelStep = MathF.Max(1f, step); return this; }

    /// <summary>When true (default), a wheel scroll this view actually consumes stops propagating to
    /// ancestor scroll views (so scrolling a nested list/table doesn't also scroll the page behind it).</summary>
    public ScrollViewBuilder TrapScroll(bool trap = true) { _trapScroll = trap; return this; }

    /// <summary>
    /// When true (default) the content eases to the scroll offset (0.33s). Turn OFF for virtualized
    /// content: the visible window is computed from the target offset, so easing the content toward it
    /// would leave the rows mismatched (blank gaps) mid-animation. Virtualized lists snap instead.
    /// </summary>
    public ScrollViewBuilder SmoothScroll(bool smooth = true) { _smoothScroll = smooth; return this; }

    /// <summary>
    /// When true (default) the scrollbars float over the content and reserve no layout space, so the
    /// content keeps its full width/height whether or not it overflows. Turn OFF to carve an inline
    /// gutter that the bar sits in (content shrinks to make room).
    /// </summary>
    public ScrollViewBuilder OverlayScrollbars(bool overlay = true) { _overlayScrollbars = overlay; return this; }

    /// <summary>
    /// When true (default) the scrollbars are only shown while the pointer is over the scroll view (or a
    /// thumb is being dragged), easing in and out. Turn OFF to always show them.
    /// </summary>
    public ScrollViewBuilder AutoHideScrollbars(bool autoHide = true) { _autoHideScrollbars = autoHide; return this; }

    // ── Terminator ─────────────────────────────────────────────────────

    /// <summary>Render the scroll view. <paramref name="drawContents"/> draws inside the (scrollable) content area.</summary>
    public void Body(Action drawContents)
    {
        ArgumentNullException.ThrowIfNull(drawContents);
        Body(_ => drawContents());
    }

    /// <summary>
    /// Render the scroll view, handing the content callback the live <see cref="ScrollViewport"/>
    /// (scroll offset + visible size) so it can virtualize long content.
    /// </summary>
    public void Body(Action<ScrollViewport> drawContents)
    {
        ArgumentNullException.ThrowIfNull(drawContents);

        var ramp = _theme.Get(_variant);

        // Apply any pending programmatic scroll requests for this id (snap; smooth-scroll later).
        Float2? pending = null;
        if (s_pendingScrollTo.TryGetValue(_id, out var target))
        {
            pending = target;
            s_pendingScrollTo.Remove(_id);
        }

        ElementHandle outerHandle = default;
        var outer = _paper.Box(_id)
            .Width(_width).Height(_height)
            .Clip()
            .OnScroll(e =>
            {
                if (!outerHandle.IsValid) return;
                bool shift = _paper.IsKeyDown(PaperKey.LeftShift) || _paper.IsKeyDown(PaperKey.RightShift);

                if (_horizontal && (shift || !_vertical))
                {
                    float scroll = _paper.GetElementStorage(outerHandle, "scrollX", 0f);
                    float contentW = _paper.GetElementStorage(outerHandle, "contentW", _width);
                    float maxScroll = MathF.Max(0f, contentW - _width);
                    scroll -= (float)e.Delta * _wheelStep;
                    scroll = Clamp(scroll, 0f, maxScroll);
                    _paper.SetElementStorage(outerHandle, "scrollX", scroll);
                    // Don't let an ancestor scroll view also consume this wheel event.
                    if (_trapScroll && maxScroll > 0f) e.StopPropagation();
                }
                else if (_vertical)
                {
                    float scroll = _paper.GetElementStorage(outerHandle, "scrollY", 0f);
                    float contentH = _paper.GetElementStorage(outerHandle, "contentH", _height);
                    float maxScroll = MathF.Max(0f, contentH - _height);
                    scroll -= (float)e.Delta * _wheelStep;
                    scroll = Clamp(scroll, 0f, maxScroll);
                    _paper.SetElementStorage(outerHandle, "scrollY", scroll);
                    if (_trapScroll && maxScroll > 0f) e.StopPropagation();
                }
            });

        using (outer.Enter())
        {
            outerHandle = _paper.CurrentParent;

            // Apply pending programmatic scroll.
            if (pending.HasValue)
            {
                _paper.SetElementStorage(outerHandle, "scrollX", MathF.Max(0f, (float)pending.Value.X));
                _paper.SetElementStorage(outerHandle, "scrollY", MathF.Max(0f, (float)pending.Value.Y));
            }

            // Apply pending scroll-by (delta nudges).
            if (s_pendingScrollBy.TryGetValue(_id, out var delta))
            {
                s_pendingScrollBy.Remove(_id);
                float curX = _paper.GetElementStorage(outerHandle, "scrollX", 0f);
                float curY = _paper.GetElementStorage(outerHandle, "scrollY", 0f);
                _paper.SetElementStorage(outerHandle, "scrollX", MathF.Max(0f, curX + (float)delta.X));
                _paper.SetElementStorage(outerHandle, "scrollY", MathF.Max(0f, curY + (float)delta.Y));
            }

            float scrollX = _paper.GetElementStorage(outerHandle, "scrollX", 0f);
            float scrollY = _paper.GetElementStorage(outerHandle, "scrollY", 0f);
            float contentW = _paper.GetElementStorage(outerHandle, "contentW", _width);
            float contentH = _paper.GetElementStorage(outerHandle, "contentH", _height);

            bool needsV = _vertical && (contentH > _height || _forceScrollbar);
            bool needsH = _horizontal && (contentW > _width || _forceScrollbar);
            float vBarW = needsV ? _scrollbarSize : 0f;
            float hBarH = needsH ? _scrollbarSize : 0f;
            // Overlay bars float over the content and reserve no layout space; inline bars carve a gutter.
            float reserveV = _overlayScrollbars ? 0f : vBarW;
            float reserveH = _overlayScrollbars ? 0f : hBarH;

            // Re-clamp scroll in case content shrank since last frame.
            float maxScrollY = MathF.Max(0f, contentH - _height);
            float maxScrollX = MathF.Max(0f, contentW - _width);
            scrollY = Clamp(scrollY, 0f, maxScrollY);
            scrollX = Clamp(scrollX, 0f, maxScrollX);
            _paper.SetElementStorage(outerHandle, "scrollY", scrollY);
            _paper.SetElementStorage(outerHandle, "scrollX", scrollX);

            // Content area: SelfDirected so we can offset it by (-scrollX, -scrollY).
            // Width is Auto when horizontal scroll is enabled (content can extend), otherwise
            // shrunk to viewport width minus the vertical scrollbar.
            float viewportW = _width - _padLeft - _padRight - reserveV;
            var content = _paper.Column($"{_id}_content")
                .PositionType(PositionType.SelfDirected)
                .Position(_padLeft - scrollX, _padTop - scrollY)
                .Height(UnitValue.Auto)
                .ColBetween(_colSpacing);
            if (_smoothScroll)
                content.Transition(GuiProp.Top, 0.33f, Easing.EaseOut)
                       .Transition(GuiProp.Left, 0.33f, Easing.EaseOut);

            if (_horizontal) content.Width(UnitValue.Auto);
            else             content.Width(viewportW);

            var capturedHandle = outerHandle;
            content.OnPostLayout((handle, rect) =>
            {
                _paper.SetElementStorage(capturedHandle, "contentH", (float)rect.Size.Y + _padTop + _padBottom);
                _paper.SetElementStorage(capturedHandle, "contentW", (float)rect.Size.X + _padLeft + _padRight);
            });

            using (content.Enter())
            {
                float viewportH = _height - _padTop - _padBottom - reserveH;
                drawContents(new ScrollViewport(scrollX, scrollY, viewportW, viewportH));
            }

            // ── Scrollbar thumbs ─────────────────────────────────────
            // Auto-hide: bars are only visible while the pointer is over the scroll view (or a thumb is
            // mid-drag), easing in/out through a stored visibility factor.
            bool barDragging = _paper.GetElementStorage(outerHandle, "barDrag", 0f) > 0.5f;
            bool wantBars = !_autoHideScrollbars || barDragging || _paper.IsElementHovered(outerHandle.Data.ID);
            float visTarget = wantBars ? 1f : 0f;
            float vis = _paper.GetElementStorage(outerHandle, "barVis", visTarget);
            vis += (visTarget - vis) * MathF.Min(1f, (float)_paper.DeltaTime * 14f);
            if (MathF.Abs(visTarget - vis) < 0.004f) vis = visTarget;
            _paper.SetElementStorage(outerHandle, "barVis", vis);

            if (needsV) DrawVerticalScrollbar(outerHandle, ramp, scrollY, contentH, hBarH, vis);
            if (needsH) DrawHorizontalScrollbar(outerHandle, ramp, scrollX, contentW, vBarW, vis);

            // Corner spacer so inline (gutter) bars don't overlap where they meet. Overlay bars float and
            // need no spacer.
            if (needsV && needsH && !_overlayScrollbars)
            {
                _paper.Box($"{_id}_corner")
                    .PositionType(PositionType.SelfDirected)
                    .Position(_width - vBarW, _height - hBarH)
                    .Width(vBarW).Height(hBarH)
                    .BackgroundColor(ScrollTrackColor())
                    .IsNotInteractable();
            }

            // Auto-scroll when dragging near edges (DragDrop integration).
            // Runs inside OnPostLayout so it has the final screen rect and can
            // adjust scrollY directly - no cross-frame state needed.
            if (_vertical && DragDrop.IsDragging)
            {
                var capturedOuter2 = outerHandle;
                float capturedHeight = _height;
                string capturedId = _id;
                outer.OnPostLayout((handle, rect) =>
                {
                    float top = (float)rect.Min.Y;
                    float bottom = top + capturedHeight;
                    float my = (float)_paper.PointerPos.Y;
                    float edgeZone = 40f;
                    float speed = 300f * _paper.DeltaTime;

                    float nudge = 0;
                    if (my > top && my < top + edgeZone)
                        nudge = -speed * (1f - (my - top) / edgeZone);
                    else if (my < bottom && my > bottom - edgeZone)
                        nudge = speed * (1f - (bottom - my) / edgeZone);

                    if (nudge != 0)
                    {
                        float cur = _paper.GetElementStorage(capturedOuter2, "scrollY", 0f);
                        float cH = _paper.GetElementStorage(capturedOuter2, "contentH", capturedHeight);
                        float max = MathF.Max(0f, cH - capturedHeight);
                        _paper.SetElementStorage(capturedOuter2, "scrollY", Clamp(cur + nudge, 0f, max));
                    }
                });
            }
        }
    }

    private void DrawVerticalScrollbar(ElementHandle outerHandle, OrigamiRamp ramp, float scrollY, float contentH, float hBarH, float vis)
    {
        if (vis <= 0.001f) return;

        float trackH = _height - hBarH;
        float effContentH = MathF.Max(_height, contentH);
        float maxScroll = MathF.Max(0f, contentH - _height);
        float ratio = _height / effContentH;
        float thumbH = MathF.Max(20f, trackH * ratio);
        float thumbY = maxScroll > 0f ? (scrollY / maxScroll) * (trackH - thumbH) : 0f;

        var thumb = _paper.Box($"{_id}_vthumb")
            .PositionType(PositionType.SelfDirected)
            .Position(_width - _scrollbarSize, thumbY)
            .Transition(GuiProp.Top, 0.1f, Easing.EaseOut)
            .Width(_scrollbarSize).Height(thumbH)
            .BackgroundColor(WithScaledAlpha(ScrollThumbColor(), vis))
            .Hovered.BackgroundColor(WithScaledAlpha(ScrollThumbHoverColor(), vis)).End();

        // Drag the thumb to scroll. Snapshot both scrollY *and* the pointer Y at drag start;
        // during the drag we recompute scrollY from absolute pointer position rather than
        // TotalDelta. The thumb itself moves with scroll each frame, which can confuse
        // delta-based tracking — pointer-anchored math is robust against that.
        var capturedHandle = outerHandle;
        thumb.OnDragStart(e =>
        {
            _paper.SetElementStorage(capturedHandle, "vDragStartScroll", scrollY);
            _paper.SetElementStorage(capturedHandle, "vDragStartY", (float)e.PointerPosition.Y);
            _paper.SetElementStorage(capturedHandle, "barDrag", 1f);
        });
        thumb.OnDragging(e =>
        {
            float dragRange = trackH - thumbH;
            if (dragRange <= 0.001f) return;
            float startScroll = _paper.GetElementStorage(capturedHandle, "vDragStartScroll", 0f);
            float startY = _paper.GetElementStorage(capturedHandle, "vDragStartY", 0f);
            float deltaY = (float)e.PointerPosition.Y - startY;
            float ns = Clamp(startScroll + deltaY * (maxScroll / dragRange), 0f, maxScroll);
            _paper.SetElementStorage(capturedHandle, "scrollY", ns);
        });
        thumb.OnDragEnd(e => _paper.SetElementStorage(capturedHandle, "barDrag", 0f));
    }

    private void DrawHorizontalScrollbar(ElementHandle outerHandle, OrigamiRamp ramp, float scrollX, float contentW, float vBarW, float vis)
    {
        if (vis <= 0.001f) return;

        float trackW = _width - vBarW;
        float effContentW = MathF.Max(_width, contentW);
        float maxScroll = MathF.Max(0f, contentW - _width);
        float ratio = _width / effContentW;
        float thumbW = MathF.Max(20f, trackW * ratio);
        float thumbX = maxScroll > 0f ? (scrollX / maxScroll) * (trackW - thumbW) : 0f;

        var thumb = _paper.Box($"{_id}_hthumb")
            .PositionType(PositionType.SelfDirected)
            .Position(thumbX, _height - _scrollbarSize)
            .Transition(GuiProp.Left, 0.1f, Easing.EaseOut)
            .Width(thumbW).Height(_scrollbarSize)
            .BackgroundColor(WithScaledAlpha(ScrollThumbColor(), vis))
            .Hovered.BackgroundColor(WithScaledAlpha(ScrollThumbHoverColor(), vis)).End();

        var capturedHandle = outerHandle;
        thumb.OnDragStart(e =>
        {
            _paper.SetElementStorage(capturedHandle, "hDragStartScroll", scrollX);
            _paper.SetElementStorage(capturedHandle, "hDragStartX", (float)e.PointerPosition.X);
            _paper.SetElementStorage(capturedHandle, "barDrag", 1f);
        });
        thumb.OnDragging(e =>
        {
            float dragRange = trackW - thumbW;
            if (dragRange <= 0.001f) return;
            float startScroll = _paper.GetElementStorage(capturedHandle, "hDragStartScroll", 0f);
            float startX = _paper.GetElementStorage(capturedHandle, "hDragStartX", 0f);
            float deltaX = (float)e.PointerPosition.X - startX;
            float ns = Clamp(startScroll + deltaX * (maxScroll / dragRange), 0f, maxScroll);
            _paper.SetElementStorage(capturedHandle, "scrollX", ns);
        });
        thumb.OnDragEnd(e => _paper.SetElementStorage(capturedHandle, "barDrag", 0f));
    }

    private static float Clamp(float v, float lo, float hi) => MathF.Max(lo, MathF.Min(hi, v));

    // Scrollbar styling: square (unrounded) thumbs in a translucent light ink, so they stay legible
    // floating over the dark panel surfaces, lifting brighter/more opaque on hover. The corner spacer's
    // track colour is fully transparent so overlay bars float over the content.
    private SDColor ScrollTrackColor() => SDColor.FromArgb(0, 0, 0, 0);
    private SDColor ScrollThumbColor() => SDColor.FromArgb(112, _theme.Ink.C200);
    private SDColor ScrollThumbHoverColor() => SDColor.FromArgb(153, _theme.Ink.C300);

    private static SDColor WithScaledAlpha(SDColor c, float f) =>
        SDColor.FromArgb((int)Math.Clamp(c.A * f, 0f, 255f), c.R, c.G, c.B);
}
