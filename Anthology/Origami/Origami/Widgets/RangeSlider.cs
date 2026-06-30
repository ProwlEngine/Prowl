// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Numerics;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// Fluent builder for a two-thumb range slider. The user picks a low/high pair within an
/// outer min/max range. Construct via <c>Origami.RangeSlider</c> / <c>Origami.IntRangeSlider</c>
/// (or the generic <c>Origami.RangeSlider&lt;T&gt;</c>).
/// </summary>
/// <remarks>
/// Mirrors <see cref="SliderBuilder{T}"/> for everything except the value model: two thumbs,
/// fill <em>between</em> them, and a setter that receives <c>(low, high)</c> on every change.
/// </remarks>
public sealed class RangeSliderBuilder<T> where T : struct, INumber<T>
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private readonly T _low;
    private readonly T _high;
    private readonly Action<T, T> _setter;

    private OrigamiVariant _variant = OrigamiVariant.Default;
    private UnitValue _width = UnitValue.Stretch();
    private float _height = 24f;
    private float _thumbDiameter = 15f;
    private float _trackThickness = 6f;
    private bool _vertical;

    private T _min;
    private T _max;
    private T? _step;
    private bool _logarithmic;

    private bool _showValue = true;
    private float _valueWidth = 64f;
    private string _format = "G";
    private bool _showTooltip = true;
    private int _tickCount;
    private Func<int, T, string>? _tickLabel;

    private bool _readOnly;
    private bool _disabled;
    private bool _allowSwap = true;
    private T? _minDistance;
    private bool _snapWhileDragging = true;

    private string? _error;
    private string? _helperText;

    private Action? _onDragStart;
    private Action? _onDragEnd;

    // Storage keys.
    private const string KeyDragging  = "rs_drag";       // 0=none, 1=low, 2=high
    private const string KeyDragOrigin = "rs_origin";

    internal RangeSliderBuilder(Paper paper, string id, T low, T high, Action<T, T> setter, T min, T max, OrigamiTheme theme)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _setter = setter ?? throw new ArgumentNullException(nameof(setter));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _low = low;
        _high = high;
        _min = min;
        _max = max;
    }

    // ── Variant ────────────────────────────────────────────────────────

    public RangeSliderBuilder<T> Variant(OrigamiVariant v) { _variant = v; return this; }
    public RangeSliderBuilder<T> Primary() => Variant(OrigamiVariant.Primary);
    public RangeSliderBuilder<T> Success() => Variant(OrigamiVariant.Success);
    public RangeSliderBuilder<T> Warning() => Variant(OrigamiVariant.Warning);
    public RangeSliderBuilder<T> Danger()  => Variant(OrigamiVariant.Danger);
    public RangeSliderBuilder<T> Info()    => Variant(OrigamiVariant.Info);
    public RangeSliderBuilder<T> Subtle()  => Variant(OrigamiVariant.Subtle);

    // ── Sizing / orientation ───────────────────────────────────────────

    public RangeSliderBuilder<T> Width(UnitValue w) { _width = w; return this; }
    public RangeSliderBuilder<T> Height(float h) { _height = MathF.Max(16, h); return this; }
    public RangeSliderBuilder<T> ThumbSize(float diameter) { _thumbDiameter = MathF.Max(8, diameter); return this; }
    public RangeSliderBuilder<T> TrackThickness(float px) { _trackThickness = MathF.Max(2, px); return this; }
    public RangeSliderBuilder<T> Small()  { _height = 20; _thumbDiameter = 11; _trackThickness = 4; return this; }
    public RangeSliderBuilder<T> Medium() { _height = 24; _thumbDiameter = 15; _trackThickness = 6; return this; }
    public RangeSliderBuilder<T> Large()  { _height = 32; _thumbDiameter = 22; _trackThickness = 8; return this; }
    public RangeSliderBuilder<T> Vertical(bool vertical = true) { _vertical = vertical; return this; }

    // ── Range / scale ──────────────────────────────────────────────────

    public RangeSliderBuilder<T> Range(T min, T max) { _min = min; _max = max; return this; }
    public RangeSliderBuilder<T> Step(T step) { _step = step; return this; }
    public RangeSliderBuilder<T> Logarithmic(bool log = true) { _logarithmic = log; return this; }

    /// <summary>
    /// Whether dragging the low thumb past the high thumb (or vice versa) swaps their roles
    /// (default true). When false, the dragged thumb is clamped to its sibling's position.
    /// </summary>
    public RangeSliderBuilder<T> AllowSwap(bool allow = true) { _allowSwap = allow; return this; }

    /// <summary>Minimum distance between the two thumbs.</summary>
    public RangeSliderBuilder<T> MinDistance(T distance) { _minDistance = distance; return this; }

    // ── Display ────────────────────────────────────────────────────────

    public RangeSliderBuilder<T> ShowValue(bool show = true) { _showValue = show; return this; }
    public RangeSliderBuilder<T> ValueWidth(float width) { _valueWidth = MathF.Max(40, width); return this; }
    public RangeSliderBuilder<T> Format(string format) { _format = format ?? "G"; return this; }
    public RangeSliderBuilder<T> ShowTooltip(bool show = true) { _showTooltip = show; return this; }
    public RangeSliderBuilder<T> Ticks(int count) { _tickCount = Math.Max(0, count); return this; }
    public RangeSliderBuilder<T> TickLabels(Func<int, T, string> labels) { _tickLabel = labels; return this; }

    // ── Behaviour ──────────────────────────────────────────────────────

    public RangeSliderBuilder<T> ReadOnly(bool readOnly = true) { _readOnly = readOnly; return this; }
    public RangeSliderBuilder<T> Disabled(bool disabled = true) { _disabled = disabled; return this; }
    public RangeSliderBuilder<T> SnapWhileDragging(bool snap = true) { _snapWhileDragging = snap; return this; }

    // ── Validation ─────────────────────────────────────────────────────

    public RangeSliderBuilder<T> Error(string? message) { _error = message; return this; }
    public RangeSliderBuilder<T> HelperText(string? text) { _helperText = text; return this; }

    // ── Drag lifecycle hooks ──────────────────────────────────────────

    public RangeSliderBuilder<T> OnDragStart(Action handler) { _onDragStart = handler; return this; }
    public RangeSliderBuilder<T> OnDragEnd(Action handler) { _onDragEnd = handler; return this; }

    // ── Terminator ─────────────────────────────────────────────────────

    public void Show()
    {
        if (Origami.IsReadOnly) _disabled = true;
        var ramp = _theme.Get(_variant);
        var ink = _theme.Ink;
        var font = _theme.Font;
        var metrics = _theme.Metrics;

        bool interactive = !_disabled && !_readOnly;

        // Normalise: low <= high.
        T lo = _low; T hi = _high;
        if (lo > hi) (lo, hi) = (hi, lo);
        lo = SliderInternal.Clamp(lo, _min, _max);
        hi = SliderInternal.Clamp(hi, _min, _max);

        double tLo = SliderInternal.ValueToT(lo, _min, _max, _logarithmic);
        double tHi = SliderInternal.ValueToT(hi, _min, _max, _logarithmic);

        bool hasError = !string.IsNullOrEmpty(_error);
        string? helpLine = hasError ? _error : _helperText;
        float helperH = !string.IsNullOrEmpty(helpLine) ? 16f : 0f;
        bool drawTickLabels = _tickCount > 0 && _tickLabel != null && font != null;
        float tickLabelH = drawTickLabels ? metrics.FontSize : 0f;

        using (_paper.Column(_id).Width(_width).Height(UnitValue.Auto).Enter())
        {
            float trackRowH = _height;

            using (_paper.Row($"{_id}_row").Width(UnitValue.Stretch()).Height(trackRowH).RowBetween(8).Enter())
            {
                ElementHandle trackHandle = default;
                var trackBuilder = _paper.Box($"{_id}_track")
                    .Width(UnitValue.Stretch())
                    .Height(trackRowH);

                if (interactive)
                {
                    trackBuilder.TabIndex(0);
                    trackBuilder.OnDragStart(e =>
                    {
                        // Decide which thumb to drag based on the closer thumb to the pointer.
                        var r = trackHandle.Data.LayoutRect;
                        double t = PointerToT(e.PointerPosition, r, _vertical);
                        int which = (Math.Abs(t - tLo) < Math.Abs(t - tHi)) ? 1 : 2;
                        _paper.SetElementStorage(trackHandle, KeyDragging, which);
                        _paper.SetElementStorage(trackHandle, KeyDragOrigin, t);
                        _onDragStart?.Invoke();
                        ApplyDrag(e, trackHandle, lo, hi, which);
                    });
                    trackBuilder.OnDragging(e =>
                    {
                        int which = _paper.GetElementStorage(trackHandle, KeyDragging, 0);
                        if (which == 0) return;
                        ApplyDrag(e, trackHandle, lo, hi, which);
                    });
                    trackBuilder.OnDragEnd(e =>
                    {
                        if (_step.HasValue && !_snapWhileDragging)
                        {
                            T sLo = SliderInternal.Clamp(SliderInternal.Snap(lo, _min, _step.Value), _min, _max);
                            T sHi = SliderInternal.Clamp(SliderInternal.Snap(hi, _min, _step.Value), _min, _max);
                            EmitOrdered(sLo, sHi);
                        }
                        _paper.SetElementStorage(trackHandle, KeyDragging, 0);
                        _onDragEnd?.Invoke();
                    });
                }

                using (trackBuilder.Enter())
                {
                    trackHandle = _paper.CurrentParent;

                    int dragging = interactive
                        ? _paper.GetElementStorage(trackHandle, KeyDragging, 0)
                        : 0;
                    bool isDragging = dragging != 0;
                    bool isHovered = _paper.IsParentHovered;

                    PaintTrack(tLo, tHi, lo, hi, dragging, isHovered, interactive,
                        ramp, ink, metrics, font);
                }

                if (_showValue)
                {
                    // Two compact numeric fields side by side.
                    using (_paper.Row($"{_id}_nfs").Width(_valueWidth * 2 + 4).Height(trackRowH).RowBetween(4).Enter())
                    {
                        var nfLo = new NumericFieldBuilder<T>(_paper, $"{_id}_nf_lo", lo, v =>
                            EmitOrdered(SliderInternal.Clamp(v, _min, _max), hi),
                            _theme).Variant(_variant).Width(_valueWidth).Height(trackRowH)
                            .Min(_min).Max(_max).Format(_format);
                        if (_step.HasValue) nfLo.Step(_step.Value);
                        if (_disabled || _readOnly) nfLo.ReadOnly();
                        nfLo.Show();

                        var nfHi = new NumericFieldBuilder<T>(_paper, $"{_id}_nf_hi", hi, v =>
                            EmitOrdered(lo, SliderInternal.Clamp(v, _min, _max)),
                            _theme).Variant(_variant).Width(_valueWidth).Height(trackRowH)
                            .Min(_min).Max(_max).Format(_format);
                        if (_step.HasValue) nfHi.Step(_step.Value);
                        if (_disabled || _readOnly) nfHi.ReadOnly();
                        nfHi.Show();
                    }
                }
            }

            if (drawTickLabels)
            {
                using (_paper.Row($"{_id}_ticks_outer").Width(UnitValue.Stretch()).Height(tickLabelH).RowBetween(8).Enter())
                {
                    using (_paper.Row($"{_id}_ticks").Width(UnitValue.Stretch()).Height(tickLabelH).Enter())
                    {
                        for (int i = 0; i < _tickCount; i++)
                        {
                            double tt = (_tickCount == 1) ? 0.5 : (double)i / (_tickCount - 1);
                            T tv = SliderInternal.TToValue<T>(tt, _min, _max, _logarithmic);
                            string label = _tickLabel!.Invoke(i, tv);
                            if (string.IsNullOrEmpty(label)) continue;

                            _paper.Box($"{_id}_tk_{i}")
                                .Width(UnitValue.Stretch()).Height(tickLabelH)
                                .Alignment(TextAlignment.MiddleCenter)
                                .IsNotInteractable()
                                .Text(label, font!).TextColor(ink.C300).FontSize(metrics.FontSize - 2);
                        }
                    }

                    // Spacer matching the two-numeric-field width so labels align under the track.
                    if (_showValue)
                        _paper.Box($"{_id}_ticks_pad").Width(_valueWidth * 2 + 4).Height(tickLabelH).IsNotInteractable();
                }
            }

            if (helperH > 0f && font != null)
            {
                Color color = hasError ? _theme.Red.C600 : ink.C300;
                _paper.Box($"{_id}_help")
                    .Width(UnitValue.Stretch()).Height(helperH)
                    .Margin(2, 2, 2, 0)
                    .Alignment(TextAlignment.MiddleLeft)
                    .IsNotInteractable()
                    .Text(helpLine!, font).TextColor(color).FontSize(metrics.FontSize - 2);
            }
        }
    }

    // ── Painting ───────────────────────────────────────────────────────

    private void PaintTrack(double tLo, double tHi, T lo, T hi, int dragging, bool isHovered, bool interactive,
        OrigamiRamp ramp, OrigamiRamp ink, OrigamiMetrics metrics, Prowl.Scribe.FontFile? font)
    {
        bool vertical = _vertical;
        var onRamp = OnRamp();
        Color trackBg  = _theme.Neutral.C300;
        Color fill     = _variant == OrigamiVariant.Subtle ? _theme.Neutral.C500 : onRamp.C500;
        Color thumbCol = _theme.Ink.C500;
        Color tickCol  = ink.C300;
        if (_disabled)
        {
            fill     = OrigamiRamp.LerpColor(fill, _theme.Neutral.C400, 0.6f);
            thumbCol = ink.C300;
        }

        bool isDragging = dragging != 0;
        bool showTooltip = _showTooltip && (isDragging || isHovered) && font != null;
        // Always compute the text so the fade-out animation keeps showing the value.
        string tooltipText = (font != null) ? $"{Format(lo)} – {Format(hi)}" : string.Empty;
        float fontSize = metrics.FontSize;
        int tickCount = _tickCount;
        float thumbBaseR = _thumbDiameter * 0.5f;
        float trackThicknessLocal = _trackThickness;

        float hoverAnim  = _paper.AnimateBool(isHovered && interactive, 0.12f, id: $"{_id}_hov");
        float activeAnim = _paper.AnimateBool(isDragging, 0.10f, id: $"{_id}_act");

        _paper.Draw((canvas, rect) =>
        {
            float rx = (float)rect.Min.X;
            float ry = (float)rect.Min.Y;
            float rw = (float)rect.Size.X;
            float rh = (float)rect.Size.Y;

            float trackThickness = trackThicknessLocal;
            float trackR = trackThickness * 0.5f;
            float thumbR = thumbBaseR * (1f + 0.10f * hoverAnim + 0.05f * activeAnim);

            float tx, ty, tw, th;
            float thumbLoCx, thumbLoCy, thumbHiCx, thumbHiCy;

            if (!vertical)
            {
                tx = rx; ty = ry + (rh - trackThickness) * 0.5f; tw = rw; th = trackThickness;
                thumbLoCx = rx + rw * (float)tLo; thumbLoCy = ry + rh * 0.5f;
                thumbHiCx = rx + rw * (float)tHi; thumbHiCy = thumbLoCy;
            }
            else
            {
                tx = rx + (rw - trackThickness) * 0.5f; ty = ry; tw = trackThickness; th = rh;
                thumbLoCx = rx + rw * 0.5f; thumbLoCy = ry + rh * (1f - (float)tLo);
                thumbHiCx = thumbLoCx;     thumbHiCy = ry + rh * (1f - (float)tHi);
            }

            // Track background.
            canvas.RoundedRectFilled(tx, ty, tw, th, trackR, trackBg);

            // Selection fill between low and high.
            if (!vertical)
            {
                float fx = tx + tw * (float)tLo;
                float fw = tw * (float)(tHi - tLo);
                if (fw > 0.0001f) canvas.RoundedRectFilled(fx, ty, fw, th, trackR, fill);
            }
            else
            {
                float fy = ty + th * (1f - (float)tHi);
                float fh = th * (float)(tHi - tLo);
                if (fh > 0.0001f) canvas.RoundedRectFilled(tx, fy, tw, fh, trackR, fill);
            }

            // Tick marks.
            if (tickCount > 0)
            {
                float tickW = MathF.Max(2f, trackThickness * 0.35f);
                float tickH = trackThickness + 4f;
                for (int i = 0; i < tickCount; i++)
                {
                    float tt = (tickCount == 1) ? 0.5f : (float)i / (tickCount - 1);
                    if (!vertical)
                    {
                        float mx = tx + tw * tt - tickW * 0.5f;
                        float my = ty + th * 0.5f - tickH * 0.5f;
                        canvas.RectFilled(mx, my, tickW, tickH, tickCol);
                    }
                    else
                    {
                        float mx = tx + tw * 0.5f - tickH * 0.5f;
                        float my = ty + th * (1f - tt) - tickW * 0.5f;
                        canvas.RectFilled(mx, my, tickH, tickW, tickCol);
                    }
                }
            }

            // Thumbs (low first, then high so high renders on top when they overlap).
            DrawThumb(canvas, thumbLoCx, thumbLoCy, thumbR, fill, thumbCol, ink, dragging == 1, activeAnim);
            DrawThumb(canvas, thumbHiCx, thumbHiCy, thumbR, fill, thumbCol, ink, dragging == 2, activeAnim);
        });

        // ── Tooltip overlay (Layer.Topmost so it can never be clipped) ────
        // Always create the element so AnimateBool storage persists across show/hide.
        if (font != null)
        {
            var thandle = _paper.CurrentParent;
            string tt = tooltipText;
            bool wantTooltip = showTooltip;
            bool vert = vertical;
            double tLoLocal = tLo, tHiLocal = tHi;
            int dragLocal = dragging;
            float thumbBaseRLocal = thumbBaseR;
            Color ttBg = _theme.Neutral.C500;
            Color ttFg = ink.C500;
            string ttId = _id;

            using (_paper.Box($"{_id}_tt")
                .PositionType(PositionType.SelfDirected)
                .Position(0, 0)
                .Width(1).Height(1)
                .Layer(Layer.Topmost)
                .HookToParent()
                .IsNotInteractable()
                .Enter())
            {
                float ttAnim = _paper.AnimateBool(wantTooltip, 0.14f, id: $"{ttId}_ttf");

                if (ttAnim > 0.01f)
                {
                    _paper.Draw((canvas, _) =>
                    {
                        var tr = thandle.Data.LayoutRect;
                        float trX = (float)tr.Min.X;
                        float trY = (float)tr.Min.Y;
                        float trW = (float)tr.Size.X;
                        float trH = (float)tr.Size.Y;
                        float thR = thumbBaseRLocal;

                        float loCx, loCy, hiCx, hiCy;
                        if (!vert)
                        {
                            loCx = trX + trW * (float)tLoLocal; loCy = trY + trH * 0.5f;
                            hiCx = trX + trW * (float)tHiLocal; hiCy = loCy;
                        }
                        else
                        {
                            loCx = trX + trW * 0.5f; loCy = trY + trH * (1f - (float)tLoLocal);
                            hiCx = loCx;             hiCy = trY + trH * (1f - (float)tHiLocal);
                        }

                        var ts = canvas.MeasureText(tt, fontSize, font);
                        float padX = 6f, padY = 2f;
                        float bw = (float)ts.X + padX * 2f;
                        float bh = (float)ts.Y + padY * 2f;

                        float anchorX = dragLocal == 1 ? loCx
                                      : dragLocal == 2 ? hiCx
                                      : (loCx + hiCx) * 0.5f;
                        float slide = (1f - ttAnim) * 4f;
                        float bx = anchorX - bw * 0.5f;
                        float by = loCy - thR - bh - 4f + slide;

                        byte aShadow = (byte)Math.Clamp((int)(80 * ttAnim), 0, 255);
                        byte aBody   = (byte)Math.Clamp((int)(255 * ttAnim), 0, 255);
                        byte aText   = (byte)Math.Clamp((int)(255 * ttAnim), 0, 255);

                        canvas.RoundedRectFilled(bx + 1f, by + 2f, bw, bh, 3f,
                            Color.FromArgb(aShadow, 0, 0, 0));
                        canvas.RoundedRectFilled(bx, by, bw, bh, 3f,
                            Color.FromArgb(aBody, ttBg.R, ttBg.G, ttBg.B));
                        canvas.DrawText(tt, bx + padX, by + padY,
                            Color.FromArgb(aText, ttFg.R, ttFg.G, ttFg.B),
                            fontSize, font);
                    });
                }
            }
        }
    }

    private void DrawThumb(Canvas canvas, float cx, float cy, float r, Color fill, Color body, OrigamiRamp ink,
        bool isActive, float activeAnim)
    {
        if (isActive && activeAnim > 0.01f && !_disabled)
        {
            Color halo = OrigamiRamp.LerpColor(Color.Transparent, fill, activeAnim * 0.35f);
            canvas.CircleFilled(cx, cy, r + 4f, halo);
        }
        Color rim = _disabled ? _theme.Neutral.C400
                  : isActive ? fill : ink.C400;
        canvas.CircleFilled(cx, cy, r, rim);
        canvas.CircleFilled(cx, cy, MathF.Max(0, r - 1.5f), body);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private OrigamiRamp OnRamp() => _variant switch
    {
        OrigamiVariant.Default => _theme.Primary,
        OrigamiVariant.Subtle  => _theme.Neutral,
        _ => _theme.Get(_variant),
    };

    private static double PointerToT(Float2 pointer, Rect rect, bool vertical)
    {
        if (!vertical)
        {
            double w = (double)rect.Size.X;
            return w > 0 ? Math.Clamp(((double)pointer.X - (double)rect.Min.X) / w, 0.0, 1.0) : 0.0;
        }
        double h = (double)rect.Size.Y;
        return h > 0 ? Math.Clamp(1.0 - ((double)pointer.Y - (double)rect.Min.Y) / h, 0.0, 1.0) : 0.0;
    }

    private void ApplyDrag(PaperUI.Events.DragEvent e, ElementHandle trackHandle, T curLo, T curHi, int which)
    {
        var r = trackHandle.Data.LayoutRect;
        double t = PointerToT(e.PointerPosition, r, _vertical);
        T newV = SliderInternal.TToValue<T>(t, _min, _max, _logarithmic);
        if (_step.HasValue && _snapWhileDragging) newV = SliderInternal.Snap(newV, _min, _step.Value);
        newV = SliderInternal.Clamp(newV, _min, _max);

        if (which == 1) // dragging low
        {
            T newLo = newV;
            T newHi = curHi;
            if (_minDistance.HasValue && (newHi - newLo) < _minDistance.Value)
                newLo = newHi - _minDistance.Value;

            if (newLo > newHi)
            {
                if (_allowSwap)
                {
                    // Crossed the high thumb > swap values AND swap dragging-role storage.
                    // Without the role flip, the dragging-thumb logic next frame would think
                    // it's still moving the low side and re-swap, producing flicker.
                    (newLo, newHi) = (newHi, newLo);
                    _paper.SetElementStorage(trackHandle, KeyDragging, 2);
                }
                else newLo = newHi;
            }
            EmitOrdered(SliderInternal.Clamp(newLo, _min, _max), newHi);
        }
        else // dragging high
        {
            T newLo = curLo;
            T newHi = newV;
            if (_minDistance.HasValue && (newHi - newLo) < _minDistance.Value)
                newHi = newLo + _minDistance.Value;

            if (newHi < newLo)
            {
                if (_allowSwap)
                {
                    (newLo, newHi) = (newHi, newLo);
                    _paper.SetElementStorage(trackHandle, KeyDragging, 1);
                }
                else newHi = newLo;
            }
            EmitOrdered(newLo, SliderInternal.Clamp(newHi, _min, _max));
        }
    }

    private void EmitOrdered(T lo, T hi)
    {
        if (lo > hi) (lo, hi) = (hi, lo);
        _setter(lo, hi);
    }

    private string Format(T v)
    {
        try { return v.ToString(_format, System.Globalization.CultureInfo.CurrentCulture); }
        catch { return v.ToString(); }
    }
}
