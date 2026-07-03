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

/// <summary>Per-frame data passed to a custom track renderer.</summary>
public readonly struct SliderTrackContext
{
    /// <summary>The track rectangle in screen space (top-left origin, full track row).</summary>
    public readonly Rect Rect;
    /// <summary>0..1 fraction of the track filled by the current value.</summary>
    public readonly float FilledT;
    /// <summary>True when the slider is currently being dragged.</summary>
    public readonly bool IsActive;
    /// <summary>True when the pointer is over the track row.</summary>
    public readonly bool IsHovered;
    /// <summary>True when the slider is interactable.</summary>
    public readonly bool Interactive;
    /// <summary>Slider's resolved variant ramp.</summary>
    public readonly OrigamiRamp Surface;
    /// <summary>Active ink ramp.</summary>
    public readonly OrigamiRamp Ink;
    /// <summary>Active theme — for callers that want metrics / fonts.</summary>
    public readonly OrigamiTheme Theme;

    internal SliderTrackContext(Rect rect, float filledT, bool isActive, bool isHovered, bool interactive,
        OrigamiRamp surface, OrigamiRamp ink, OrigamiTheme theme)
    {
        Rect = rect; FilledT = filledT; IsActive = isActive; IsHovered = isHovered;
        Interactive = interactive; Surface = surface; Ink = ink; Theme = theme;
    }
}

/// <summary>Per-frame data passed to a custom thumb renderer.</summary>
public readonly struct SliderThumbContext
{
    /// <summary>Thumb center in screen space.</summary>
    public readonly Float2 Center;
    /// <summary>Suggested thumb radius (driven by the slider's <see cref="SliderBuilder{T}.Size"/>).</summary>
    public readonly float Radius;
    /// <summary>0..1 fraction of the track filled by the current value.</summary>
    public readonly float FilledT;
    /// <summary>True when the slider is currently being dragged.</summary>
    public readonly bool IsActive;
    /// <summary>True when the pointer is over the track row.</summary>
    public readonly bool IsHovered;
    /// <summary>True when the slider is interactable.</summary>
    public readonly bool Interactive;
    /// <summary>Slider's resolved variant ramp.</summary>
    public readonly OrigamiRamp Surface;
    /// <summary>Active ink ramp.</summary>
    public readonly OrigamiRamp Ink;
    /// <summary>Active theme — for callers that want metrics / fonts.</summary>
    public readonly OrigamiTheme Theme;

    internal SliderThumbContext(Float2 center, float radius, float filledT, bool isActive, bool isHovered,
        bool interactive, OrigamiRamp surface, OrigamiRamp ink, OrigamiTheme theme)
    {
        Center = center; Radius = radius; FilledT = filledT; IsActive = isActive; IsHovered = isHovered;
        Interactive = interactive; Surface = surface; Ink = ink; Theme = theme;
    }
}

/// <summary>
/// Fluent builder for a single-thumb slider. Construct via the <c>Origami.Slider</c> /
/// <c>Origami.IntSlider</c> factories (or the generic <c>Origami.Slider&lt;T&gt;</c>) and
/// call <see cref="Show"/> to render.
/// </summary>
/// <remarks>
/// <para>Generic on <typeparamref name="T"/> — works with any <see cref="INumber{T}"/>:
/// float, double, decimal, int, long, etc. Math goes through <see cref="double"/> internally
/// (or <see cref="decimal"/> for <c>decimal</c>) to keep the value mapping precise across
/// the full numeric range.</para>
/// <para>One layout box for the track (with all canvas drawing), one optional inline
/// <see cref="NumericFieldBuilder{T}"/> on the right. Tooltip during drag floats above the
/// thumb on <see cref="Layer.Topmost"/>.</para>
/// </remarks>
public sealed class SliderBuilder<T> where T : struct, INumber<T>
{
    private static readonly bool s_isDecimal = typeof(T) == typeof(decimal);

    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private readonly T _value;
    private readonly Action<T> _setter;

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
    private bool _bipolar;
    private T _bipolarCenter;

    private bool _showValue = true;
    private float _valueWidth = 64f;
    private string _format = "G";
    private bool _showTooltip = true;
    private int _tickCount;
    private Func<int, T, string>? _tickLabel;

    private bool _readOnly;
    private bool _disabled;
    private bool _snapWhileDragging = true;
    private T? _wheelStep;
    private T? _keyboardStep;
    private T? _keyboardPageStep;

    private string? _error;
    private string? _helperText;
    private Func<T, (bool ok, string? message)>? _validator;

    private Action? _onDragStart;
    private Action? _onDragEnd;

    private Action<Canvas, SliderTrackContext>? _customTrack;
    private Action<Canvas, SliderThumbContext>? _customThumb;

    // Storage keys for per-element persistent state.
    private const string KeyDragging   = "sl_drag";
    private const string KeyDragStartV = "sl_drag_v";

    internal SliderBuilder(Paper paper, string id, T value, Action<T> setter, T min, T max, OrigamiTheme theme)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _setter = setter ?? throw new ArgumentNullException(nameof(setter));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _value = value;
        _min = min;
        _max = max;
        _bipolarCenter = T.Zero;
    }

    // ── Variant ────────────────────────────────────────────────────────

    public SliderBuilder<T> Variant(OrigamiVariant v) { _variant = v; return this; }
    public SliderBuilder<T> Primary() => Variant(OrigamiVariant.Primary);
    public SliderBuilder<T> Success() => Variant(OrigamiVariant.Success);
    public SliderBuilder<T> Warning() => Variant(OrigamiVariant.Warning);
    public SliderBuilder<T> Danger()  => Variant(OrigamiVariant.Danger);
    public SliderBuilder<T> Info()    => Variant(OrigamiVariant.Info);
    public SliderBuilder<T> Subtle()  => Variant(OrigamiVariant.Subtle);

    // ── Sizing / orientation ───────────────────────────────────────────

    public SliderBuilder<T> Width(UnitValue w) { _width = w; return this; }
    public SliderBuilder<T> Height(float h) { _height = MathF.Max(16, h); return this; }

    /// <summary>Thumb diameter in pixels (default 19, set by Small/Medium/Large).</summary>
    public SliderBuilder<T> ThumbSize(float diameter) { _thumbDiameter = MathF.Max(8, diameter); return this; }

    /// <summary>Track thickness in pixels (default 6, set by Small/Medium/Large).</summary>
    public SliderBuilder<T> TrackThickness(float px) { _trackThickness = MathF.Max(2, px); return this; }

    public SliderBuilder<T> Small()  { _height = 20; _thumbDiameter = 11; _trackThickness = 4; return this; }
    public SliderBuilder<T> Medium() { _height = 24; _thumbDiameter = 15; _trackThickness = 6; return this; }
    public SliderBuilder<T> Large()  { _height = 32; _thumbDiameter = 22; _trackThickness = 8; return this; }

    /// <summary>Vertical track instead of horizontal. Min sits at the bottom, max at the top.</summary>
    public SliderBuilder<T> Vertical(bool vertical = true) { _vertical = vertical; return this; }

    // ── Range / scale ──────────────────────────────────────────────────

    public SliderBuilder<T> Range(T min, T max) { _min = min; _max = max; return this; }
    public SliderBuilder<T> Min(T min) { _min = min; return this; }
    public SliderBuilder<T> Max(T max) { _max = max; return this; }

    /// <summary>Snap the value to multiples of <paramref name="step"/>. Disabled when null/zero.</summary>
    public SliderBuilder<T> Step(T step) { _step = step; return this; }

    /// <summary>
    /// Log-scale value mapping. Useful for frequency, density, exposure — perceptually
    /// linear control over wide ranges. Requires <c>min &gt; 0</c>; falls back to linear
    /// if the range crosses zero.
    /// </summary>
    public SliderBuilder<T> Logarithmic(bool log = true) { _logarithmic = log; return this; }

    /// <summary>
    /// Track fills outward from <paramref name="center"/> (defaults to 0). Use for signed
    /// signals like saturation, contrast, pan.
    /// </summary>
    public SliderBuilder<T> Bipolar(T? center = null)
    {
        _bipolar = true;
        if (center.HasValue) _bipolarCenter = center.Value;
        return this;
    }

    // ── Display ────────────────────────────────────────────────────────

    /// <summary>Show an inline numeric field on the right (default true). Typing the value snaps the thumb.</summary>
    public SliderBuilder<T> ShowValue(bool show = true) { _showValue = show; return this; }
    public SliderBuilder<T> ValueWidth(float width) { _valueWidth = MathF.Max(40, width); return this; }
    public SliderBuilder<T> Format(string format) { _format = format ?? "G"; return this; }

    /// <summary>Floating value bubble above the thumb during drag/hover (default true).</summary>
    public SliderBuilder<T> ShowTooltip(bool show = true) { _showTooltip = show; return this; }

    /// <summary>Tick marks at <paramref name="count"/> evenly spaced positions. <c>0</c> disables ticks.</summary>
    public SliderBuilder<T> Ticks(int count) { _tickCount = Math.Max(0, count); return this; }

    /// <summary>Optional label for each tick. Receives <c>(tickIndex, tickValue)</c>; return empty for unlabeled ticks.</summary>
    public SliderBuilder<T> TickLabels(Func<int, T, string> labels) { _tickLabel = labels; return this; }

    // ── Behaviour ──────────────────────────────────────────────────────

    public SliderBuilder<T> ReadOnly(bool readOnly = true) { _readOnly = readOnly; return this; }
    public SliderBuilder<T> Disabled(bool disabled = true) { _disabled = disabled; return this; }

    /// <summary>If true (default), drag updates snap to <see cref="Step"/> live; if false, value snaps only on release.</summary>
    public SliderBuilder<T> SnapWhileDragging(bool snap = true) { _snapWhileDragging = snap; return this; }

    public SliderBuilder<T> WheelStep(T step) { _wheelStep = step; return this; }
    public SliderBuilder<T> KeyboardStep(T step) { _keyboardStep = step; return this; }
    public SliderBuilder<T> KeyboardPageStep(T step) { _keyboardPageStep = step; return this; }

    // ── Validation ─────────────────────────────────────────────────────

    public SliderBuilder<T> Error(string? message) { _error = message; return this; }
    public SliderBuilder<T> HelperText(string? text) { _helperText = text; return this; }
    public SliderBuilder<T> Validator(Func<T, (bool ok, string? message)> validator) { _validator = validator; return this; }

    // ── Drag lifecycle hooks (for undo grouping etc.) ─────────────────

    public SliderBuilder<T> OnDragStart(Action handler) { _onDragStart = handler; return this; }
    public SliderBuilder<T> OnDragEnd(Action handler) { _onDragEnd = handler; return this; }

    // ── Custom rendering ───────────────────────────────────────────────

    public SliderBuilder<T> CustomTrack(Action<Canvas, SliderTrackContext> render) { _customTrack = render; return this; }
    public SliderBuilder<T> CustomThumb(Action<Canvas, SliderThumbContext> render) { _customThumb = render; return this; }

    // ── Terminator ─────────────────────────────────────────────────────

    public void Show()
    {
        if (Origami.IsReadOnly) _disabled = true;
        var ramp = _theme.Get(_variant);
        var ink = _theme.Ink;
        var font = _theme.Font;
        var metrics = _theme.Metrics;

        bool interactive = !_disabled && !_readOnly;
        T clampedValue = SliderInternal.Clamp(_value, _min, _max);
        double t = SliderInternal.ValueToT(clampedValue, _min, _max, _logarithmic);

        // Validation runs on the present (clamped) value.
        bool hasError = !string.IsNullOrEmpty(_error);
        if (!hasError && _validator != null)
        {
            var (ok, msg) = _validator(clampedValue);
            if (!ok) { hasError = true; _error = msg ?? "Invalid"; }
        }
        string? helpLine = hasError ? _error : _helperText;
        float helperH = !string.IsNullOrEmpty(helpLine) ? 16f : 0f;
        bool drawTickLabels = _tickCount > 0 && _tickLabel != null && font != null;
        float tickLabelH = drawTickLabels ? metrics.FontSize : 0f;

        using (_paper.Column(_id).Width(_width).Height(UnitValue.Auto).Enter())
        {
            float trackRowH = _height;

            // ── Track + optional inline numeric field row ─────────
            using (_paper.Row($"{_id}_row").Width(UnitValue.Stretch()).Height(trackRowH).RowBetween(8).Enter())
            {
                // Track box owns all interaction + the canvas-drawn track + thumb.
                ElementHandle trackHandle = default;
                var trackBuilder = _paper.Box($"{_id}_track")
                    .Width(UnitValue.Stretch())
                    .Height(trackRowH);

                if (interactive)
                {
                    trackBuilder.TabIndex(0);

                    // Click anywhere on track > set value to that position.
                    trackBuilder.OnClick(e =>
                    {
                        double newT = ClickToT(e, _vertical);
                        T newV = SliderInternal.TToValue<T>(newT, _min, _max, _logarithmic);
                        if (_step.HasValue && _snapWhileDragging) newV = SliderInternal.Snap(newV, _min, _step.Value);
                        newV = SliderInternal.Clamp(newV, _min, _max);
                        _setter(newV);
                    });

                    trackBuilder.OnDragStart(e =>
                    {
                        _paper.SetElementStorage(trackHandle, KeyDragging, true);
                        _paper.SetElementStorage(trackHandle, KeyDragStartV, clampedValue);
                        _onDragStart?.Invoke();
                        ApplyDrag(e, trackHandle);
                    });
                    trackBuilder.OnDragging(e => ApplyDrag(e, trackHandle));
                    trackBuilder.OnDragEnd(e =>
                    {
                        // On release, ensure the value is finally snapped (if SnapWhileDragging was off).
                        if (_step.HasValue && !_snapWhileDragging)
                        {
                            T finalV = SliderInternal.Snap(clampedValue, _min, _step.Value);
                            finalV = SliderInternal.Clamp(finalV, _min, _max);
                            _setter(finalV);
                        }
                        _paper.SetElementStorage(trackHandle, KeyDragging, false);
                        _onDragEnd?.Invoke();
                    });

                    // Note: no mouse-wheel handler — it fights with an enclosing ScrollView.
                }

                using (trackBuilder.Enter())
                {
                    trackHandle = _paper.CurrentParent;

                    // Keyboard while focused.
                    if (interactive && _paper.IsElementFocused(trackHandle.Data.ID))
                        HandleKeyboard(clampedValue);

                    bool isDragging = interactive && _paper.GetElementStorage(trackHandle, KeyDragging, false);
                    // We can't read hover state here without another query; we'll wire it in the Draw closure
                    // where the rect is concrete. For now, treat hover as "any pointer-over the track box".
                    bool isHovered = _paper.IsParentHovered;

                    PaintTrack(t, trackRowH, isDragging, isHovered, interactive, ramp, ink, metrics, font);
                }

                // Inline numeric field on the right.
                if (_showValue)
                {
                    var nf = new NumericFieldBuilder<T>(_paper, $"{_id}_nf", clampedValue, v =>
                        {
                            T snapped = _step.HasValue ? SliderInternal.Snap(v, _min, _step.Value) : v;
                            _setter(SliderInternal.Clamp(snapped, _min, _max));
                        }, _theme)
                        .Variant(_variant)
                        .Width(_valueWidth)
                        .Height(trackRowH)
                        .Min(_min).Max(_max)
                        .Format(_format);
                    if (_step.HasValue) nf.Step(_step.Value);
                    // NumericField doesn't have a Disabled state today; use ReadOnly as the closest
                    // gate so disabled sliders also can't be retyped via the inline field.
                    if (_disabled || _readOnly) nf.ReadOnly();
                    nf.Show();
                }
            }

            // ── Tick labels row ────────────────────────────────────────
            // Mirror the track-row gutters so labels align under the track only — without this
            // the labels would stretch under the inline numeric field too and skew the spacing.
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

                    // Spacer that matches the inline numeric field's footprint so tick labels
                    // align with the track section above, not the full row.
                    if (_showValue)
                        _paper.Box($"{_id}_ticks_pad").Width(_valueWidth).Height(tickLabelH).IsNotInteractable();
                }
            }

            // ── Helper / error line ───────────────────────────────────
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

    private void PaintTrack(double valueT, float rowH, bool isDragging, bool isHovered, bool interactive,
        OrigamiRamp ramp, OrigamiRamp ink, OrigamiMetrics metrics, Prowl.Scribe.FontFile? font)
    {
        // Snapshot per-frame state for the closure.
        bool vertical = _vertical;
        bool bipolar = _bipolar;
        double centerT = _bipolar ? SliderInternal.ValueToT(_bipolarCenter, _min, _max, _logarithmic) : 0.0;

        OrigamiRamp onRamp = OnRamp();
        Color trackBg   = Color.FromArgb(20, 255, 255, 255); // light translucent groove
        Color fill      = _variant == OrigamiVariant.Subtle ? _theme.Neutral.C500 : onRamp.C500;
        Color thumbCol  = _theme.Ink.C600;
        Color tickCol   = ink.C300;
        if (_disabled)
        {
            trackBg  = Color.FromArgb(14, 255, 255, 255);
            fill     = OrigamiRamp.LerpColor(fill, _theme.Neutral.C400, 0.6f);
            thumbCol = ink.C300;
        }

        int tickCount = _tickCount;
        float thumbBaseR = _thumbDiameter * 0.5f;
        float trackThicknessLocal = _trackThickness;
        bool showCustomTrack = _customTrack != null;
        bool showCustomThumb = _customThumb != null;
        var customTrack = _customTrack;
        var customThumb = _customThumb;

        bool showTooltip = _showTooltip && (isDragging || isHovered) && font != null;
        // Always compute the formatted value when we have a font, so the fade-out animation
        // keeps showing the last value rather than emptying the bubble mid-fade.
        string tooltipText = (font != null) ? FormatValue(valueT) : string.Empty;
        float fontSize = metrics.FontSize;
        Color tooltipBg = _theme.Neutral.C500;
        Color tooltipFg = ink.C500;

        // Animation hooks for hover/active.
        float hoverAnim  = _paper.AnimateBool(isHovered && interactive, 0.12f, id: $"{_id}_hov");
        float activeAnim = _paper.AnimateBool(isDragging, 0.10f, id: $"{_id}_act");

        _paper.Draw((canvas, rect) =>
        {
            float rx = (float)rect.Min.X;
            float ry = (float)rect.Min.Y;
            float rw = (float)rect.Size.X;
            float rh = (float)rect.Size.Y;

            // Both track thickness and thumb diameter are fixed per size category (set by
            // Small / Medium / Large or ThumbSize / TrackThickness). Avoiding rect-derived
            // sizing means vertical sliders, narrow boxes and tall boxes all paint the same.
            float trackThickness = trackThicknessLocal;
            float trackR = trackThickness * 0.5f;
            float thumbR = thumbBaseR * (1f + 0.10f * hoverAnim + 0.05f * activeAnim);

            float tx, ty, tw, th;
            float thumbCx, thumbCy;
            float fillStartFrac, fillEndFrac;

            if (!vertical)
            {
                tx = rx;
                ty = ry + (rh - trackThickness) * 0.5f;
                tw = rw;
                th = trackThickness;

                thumbCx = rx + rw * (float)valueT;
                thumbCy = ry + rh * 0.5f;

                if (bipolar)
                {
                    fillStartFrac = MathF.Min((float)valueT, (float)centerT);
                    fillEndFrac   = MathF.Max((float)valueT, (float)centerT);
                }
                else
                {
                    fillStartFrac = 0f;
                    fillEndFrac   = (float)valueT;
                }
            }
            else
            {
                tx = rx + (rw - trackThickness) * 0.5f;
                ty = ry;
                tw = trackThickness;
                th = rh;

                // Vertical: 0 at the bottom, 1 at the top.
                thumbCx = rx + rw * 0.5f;
                thumbCy = ry + rh * (1f - (float)valueT);

                if (bipolar)
                {
                    fillStartFrac = MathF.Min((float)valueT, (float)centerT);
                    fillEndFrac   = MathF.Max((float)valueT, (float)centerT);
                }
                else
                {
                    fillStartFrac = 0f;
                    fillEndFrac   = (float)valueT;
                }
            }

            // Custom track wins over default chrome.
            if (showCustomTrack)
            {
                var ctx = new SliderTrackContext(rect, (float)valueT, isDragging, isHovered, interactive,
                    ramp, ink, _theme);
                customTrack!(canvas, ctx);
            }
            else
            {
                // Track background.
                canvas.RoundedRectFilled(tx, ty, tw, th, trackR, trackBg);

                // Track filled portion — bipolar fills from center to thumb, otherwise 0 to thumb.
                if (fillEndFrac - fillStartFrac > 0.0001f)
                {
                    if (!vertical)
                    {
                        float fx = tx + tw * fillStartFrac;
                        float fw = tw * (fillEndFrac - fillStartFrac);
                        canvas.RoundedRectFilled(fx, ty, fw, th, trackR, fill);
                    }
                    else
                    {
                        // Inverted fill — origin at bottom.
                        float fy = ty + th * (1f - fillEndFrac);
                        float fh = th * (fillEndFrac - fillStartFrac);
                        canvas.RoundedRectFilled(tx, fy, tw, fh, trackR, fill);
                    }
                }

                // Tick marks (between min and max, inclusive).
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
            }

            // Thumb.
            if (showCustomThumb)
            {
                var ctx = new SliderThumbContext(new Float2(thumbCx, thumbCy), thumbR, (float)valueT,
                    isDragging, isHovered, interactive, ramp, ink, _theme);
                customThumb!(canvas, ctx);
            }
            else
            {
                // Purple glow ring around a white thumb (prototype box-shadow 0 0 0 3px acc/0.3).
                if (!_disabled)
                {
                    float ringA = 0.30f + 0.30f * MathF.Max(hoverAnim, activeAnim);
                    canvas.CircleFilled(thumbCx, thumbCy, thumbR + 3f, Color.FromArgb((int)(ringA * 255), fill.R, fill.G, fill.B));
                }
                canvas.CircleFilled(thumbCx, thumbCy, thumbR, _disabled ? _theme.Neutral.C400 : thumbCol);
            }

        });

        // ── Tooltip overlay ──────────────────────────────────────
        // Lives on its own element on Layer.Topmost so nothing can clip or occlude it.
        // The element is created every frame regardless of visibility — that way AnimateBool
        // storage persists across show/hide cycles and we get a clean fade in/out instead of
        // an instant pop. The Draw callback short-circuits when the animation is fully out.
        if (font != null)
        {
            var thandle = _paper.CurrentParent;
            string tt = tooltipText;
            bool wantTooltip = _showTooltip && (isDragging || isHovered);
            bool vert = vertical;
            double t = valueT;
            float thumbBaseRLocal = thumbBaseR;
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
                // 0 > 1 fade. Pinned to this element so it persists across visibility flips.
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

                        float thumbCx, thumbCy;
                        if (!vert)
                        {
                            thumbCx = trX + trW * (float)t;
                            thumbCy = trY + trH * 0.5f;
                        }
                        else
                        {
                            thumbCx = trX + trW * 0.5f;
                            thumbCy = trY + trH * (1f - (float)t);
                        }

                        var ts = canvas.MeasureText(tt, fontSize, font);
                        float padX = 6f, padY = 2f;
                        float bw = (float)ts.X + padX * 2f;
                        float bh = (float)ts.Y + padY * 2f;
                        // Slide up into place: 4px below resting position when fading in.
                        float slide = (1f - ttAnim) * 4f;
                        float bx = thumbCx - bw * 0.5f;
                        float by = thumbCy - thR - bh - 4f + slide;

                        // Multiply each color's alpha by the animation fraction so the bubble
                        // truly fades rather than appearing instantly.
                        byte aShadow = (byte)Math.Clamp((int)(80 * ttAnim), 0, 255);
                        byte aBody   = (byte)Math.Clamp((int)(255 * ttAnim), 0, 255);
                        byte aText   = (byte)Math.Clamp((int)(255 * ttAnim), 0, 255);

                        canvas.RoundedRectFilled(bx + 1f, by + 2f, bw, bh, 3f,
                            Color.FromArgb(aShadow, 0, 0, 0));
                        canvas.RoundedRectFilled(bx, by, bw, bh, 3f,
                            Color.FromArgb(aBody, tooltipBg.R, tooltipBg.G, tooltipBg.B));
                        canvas.DrawText(tt, bx + padX, by + padY,
                            Color.FromArgb(aText, tooltipFg.R, tooltipFg.G, tooltipFg.B),
                            fontSize, font);
                    });
                }
            }
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────

    /// <summary>On-state ramp — Default / Subtle don't have one of their own, so we promote them.</summary>
    private OrigamiRamp OnRamp() => _variant switch
    {
        OrigamiVariant.Default => _theme.Primary,
        OrigamiVariant.Subtle  => _theme.Neutral,
        _ => _theme.Get(_variant),
    };

    private double ClickToT(PaperUI.Events.ClickEvent e, bool vertical)
    {
        // NormalizedPosition is 0..1 across the element rect; Y is top-down so invert for vertical.
        double t = vertical ? (1.0 - (double)e.NormalizedPosition.Y) : (double)e.NormalizedPosition.X;
        return Math.Clamp(t, 0.0, 1.0);
    }

    private void ApplyDrag(PaperUI.Events.DragEvent e, ElementHandle trackHandle)
    {
        // Map pointer position back to a value along the track. Pointer position is in screen
        // space; we pull the rect from the layout and project onto it.
        var r = trackHandle.Data.LayoutRect;
        double t;
        if (!_vertical)
        {
            double w = (double)r.Size.X;
            t = w > 0 ? ((double)e.PointerPosition.X - (double)r.Min.X) / w : 0;
        }
        else
        {
            double h = (double)r.Size.Y;
            t = h > 0 ? 1.0 - ((double)e.PointerPosition.Y - (double)r.Min.Y) / h : 0;
        }
        t = Math.Clamp(t, 0.0, 1.0);

        T newV = SliderInternal.TToValue<T>(t, _min, _max, _logarithmic);
        if (_step.HasValue && _snapWhileDragging) newV = SliderInternal.Snap(newV, _min, _step.Value);
        newV = SliderInternal.Clamp(newV, _min, _max);
        _setter(newV);
    }

    private void HandleKeyboard(T currentValue)
    {
        T step = _keyboardStep ?? _step ?? DefaultKeyStep();
        T pageStep = _keyboardPageStep ?? SliderInternal.MultiplyByDouble(step, 5.0); // ~5x small step
        T applied = ApplyModifiers(step);
        T appliedPage = ApplyModifiers(pageStep);

        // Arrow keys
        if (_paper.IsKeyPressedOrRepeating(PaperKey.Right) || (_vertical && _paper.IsKeyPressedOrRepeating(PaperKey.Up)))
            _setter(StepValue(currentValue, applied));
        else if (_paper.IsKeyPressedOrRepeating(PaperKey.Left) || (_vertical && _paper.IsKeyPressedOrRepeating(PaperKey.Down)))
            _setter(StepValue(currentValue, -applied));
        else if (!_vertical && _paper.IsKeyPressedOrRepeating(PaperKey.Up))
            _setter(StepValue(currentValue, applied));
        else if (!_vertical && _paper.IsKeyPressedOrRepeating(PaperKey.Down))
            _setter(StepValue(currentValue, -applied));
        else if (_paper.IsKeyPressedOrRepeating(PaperKey.PageUp))
            _setter(StepValue(currentValue, appliedPage));
        else if (_paper.IsKeyPressedOrRepeating(PaperKey.PageDown))
            _setter(StepValue(currentValue, -appliedPage));
        else if (_paper.IsKeyPressed(PaperKey.Home))
            _setter(_min);
        else if (_paper.IsKeyPressed(PaperKey.End))
            _setter(_max);
    }

    private T StepValue(T cur, T delta)
    {
        T v = cur + delta;
        if (_step.HasValue) v = SliderInternal.Snap(v, _min, _step.Value);
        return SliderInternal.Clamp(v, _min, _max);
    }

    private T ApplyModifiers(T step)
    {
        if (_paper.IsKeyDown(PaperKey.LeftControl) || _paper.IsKeyDown(PaperKey.RightControl))
            return SliderInternal.MultiplyByDouble(step, 10.0);
        if (_paper.IsKeyDown(PaperKey.LeftShift) || _paper.IsKeyDown(PaperKey.RightShift))
            return SliderInternal.MultiplyByDouble(step, 0.1);
        return step;
    }

    /// <summary>Reasonable default keyboard / wheel step when the caller didn't supply one.</summary>
    private T DefaultKeyStep()
    {
        // 1% of the range, falling back to T.One for integer-only types (where 1% of a small
        // integer range often rounds to 0).
        double range = SliderInternal.ToDouble(_max) - SliderInternal.ToDouble(_min);
        double s = range / 100.0;
        if (s <= 0) return T.One;

        // For integer types, the smallest meaningful step is T.One.
        T candidate = SliderInternal.FromDouble<T>(s);
        if (SliderInternal.IsIntegerType<T>() && candidate == T.Zero) return T.One;
        return candidate;
    }

    private string FormatValue(double valueT)
    {
        T v = SliderInternal.TToValue<T>(valueT, _min, _max, _logarithmic);
        if (_step.HasValue) v = SliderInternal.Snap(v, _min, _step.Value);
        try { return v.ToString(_format, System.Globalization.CultureInfo.CurrentCulture); }
        catch { return v.ToString(); }
    }
}
