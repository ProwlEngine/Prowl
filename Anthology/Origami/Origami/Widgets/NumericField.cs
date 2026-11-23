// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// Generic numeric input field for any <see cref="INumber{T}"/> — float, double, decimal,
/// int, uint, long, short, byte, sbyte, etc. Culture-aware (defaults to
/// <see cref="CultureInfo.CurrentCulture"/>); supports min/max clamping, step quantisation,
/// formatting, validation, and full <see cref="TextFieldBuilder"/> chrome (placeholder,
/// helper text, error state, leading/trailing slots).
/// </summary>
public sealed class NumericFieldBuilder<T> where T : struct, INumber<T>
{
    private static readonly bool s_isFloatingPoint
        = typeof(T) == typeof(float)
        || typeof(T) == typeof(double)
        || typeof(T) == typeof(decimal)
        || typeof(T) == typeof(Half);

    private static readonly bool s_isSigned
        = T.IsNegative(T.Zero - T.One); // detect signed by trying -1; unsigned types don't change sign here

    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private readonly T _value;
    private readonly Action<T> _setter;

    private OrigamiVariant _variant = OrigamiVariant.Default;
    private UnitValue _width = UnitValue.Stretch();
    private float _height = 24f;
    private bool _readOnly;
    private string _placeholder = "";
    private bool _selectAllOnFocus = true; // default to true
    private bool _autoFocus;

    private T? _min;
    private T? _max;
    private T? _step;
    private CultureInfo _culture = CultureInfo.CurrentCulture;
    private string _format = "G";

    private string? _error;
    private string? _helperText;
    private Func<T, (bool ok, string? message)>? _validator;

    private string? _leadingIconGlyph;
    private string? _trailingIconGlyph;
    private Action? _trailingIconClick;
    private string? _prefixText;
    private Color? _prefixColor;
    private string? _suffixText;
    private bool _stepper;
    private bool _dragLabel;
    private int _dragDecimalPlaces;
    private bool _prefixCompact;
    private bool _dragHandle = true; // type-tag scrub handle shown when no other prefix is set
    private int _dragHandleDecimals = 2;
    private Action? _onCommit;

    internal NumericFieldBuilder(Paper paper, string id, T value, Action<T> setter, OrigamiTheme theme)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _setter = setter ?? throw new ArgumentNullException(nameof(setter));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _value = value;
        _height = _theme.Metrics.RowHeight;
    }

    // ── Variant + sizing (delegated to TextField behind the scenes) ─

    public NumericFieldBuilder<T> Variant(OrigamiVariant variant) { _variant = variant; return this; }
    public NumericFieldBuilder<T> Primary() => Variant(OrigamiVariant.Primary);
    public NumericFieldBuilder<T> Success() => Variant(OrigamiVariant.Success);
    public NumericFieldBuilder<T> Warning() => Variant(OrigamiVariant.Warning);
    public NumericFieldBuilder<T> Danger() => Variant(OrigamiVariant.Danger);
    public NumericFieldBuilder<T> Info() => Variant(OrigamiVariant.Info);
    public NumericFieldBuilder<T> Subtle() => Variant(OrigamiVariant.Subtle);

    public NumericFieldBuilder<T> Width(UnitValue width) { _width = width; return this; }
    public NumericFieldBuilder<T> Height(float height) { _height = MathF.Max(20, height); return this; }
    public NumericFieldBuilder<T> ReadOnly(bool readOnly = true) { _readOnly = readOnly; return this; }
    public NumericFieldBuilder<T> Placeholder(string text) { _placeholder = text ?? string.Empty; return this; }
    public NumericFieldBuilder<T> SelectAllOnFocus(bool select = true) { _selectAllOnFocus = select; return this; }
    /// <summary>Focus the field on the first frame it appears, and again each time it reappears.</summary>
    public NumericFieldBuilder<T> AutoFocus(bool autoFocus = true) { _autoFocus = autoFocus; return this; }

    // ── Numeric range / step / format ───────────────────────────────

    /// <summary>Inclusive minimum. Values from the user are clamped before being passed to the setter.</summary>
    public NumericFieldBuilder<T> Min(T min) { _min = min; return this; }

    /// <summary>Inclusive maximum.</summary>
    public NumericFieldBuilder<T> Max(T max) { _max = max; return this; }

    /// <summary>
    /// Round / quantise the parsed value to the nearest multiple of <paramref name="step"/>.
    /// Useful for editor sliders or when you want clean increments.
    /// </summary>
    public NumericFieldBuilder<T> Step(T step) { _step = step; return this; }

    /// <summary>Override the format string used to render the value (default <c>"G"</c>). Examples: <c>"F2"</c>, <c>"N0"</c>.</summary>
    public NumericFieldBuilder<T> Format(string format) { _format = format ?? "G"; return this; }

    /// <summary>
    /// Override the culture used for parse + format. Defaults to
    /// <see cref="CultureInfo.CurrentCulture"/>; pass <see cref="CultureInfo.InvariantCulture"/>
    /// for code-facing fields (e.g. asset settings) where the decimal separator must be <c>.</c>.
    /// </summary>
    public NumericFieldBuilder<T> Culture(CultureInfo culture) { _culture = culture ?? CultureInfo.CurrentCulture; return this; }

    // ── Validation / helper / slots ─────────────────────────────────

    public NumericFieldBuilder<T> Error(string? message) { _error = message; return this; }
    public NumericFieldBuilder<T> HelperText(string? text) { _helperText = text; return this; }
    public NumericFieldBuilder<T> Validator(Func<T, (bool ok, string? message)> validator) { _validator = validator; return this; }

    public NumericFieldBuilder<T> LeadingIcon(string glyph) { _leadingIconGlyph = glyph; return this; }

    /// <summary>
    /// Show a small colored label badge inside the field, before the text input.
    /// Useful for channel labels like "H", "S", "V" or "R", "G", "B".
    /// </summary>
    /// <remarks>Replaces any <see cref="DraggableLabel"/> / <see cref="DragHandle"/>: a plain label never scrubs.</remarks>
    public NumericFieldBuilder<T> Prefix(string text, Color color)
    {
        _prefixText = text;
        _prefixColor = color;
        _dragLabel = false;
        _prefixCompact = false;
        return this;
    }
    public NumericFieldBuilder<T> TrailingIcon(string glyph, Action? onClick = null)
    {
        _trailingIconGlyph = glyph;
        _trailingIconClick = onClick;
        return this;
    }

    /// <summary>Muted mono unit suffix inside the field (e.g. "kg", "%").</summary>
    public NumericFieldBuilder<T> Suffix(string text) { _suffixText = text; return this; }

    /// <summary>Show up/down stepper buttons on the right that increment/decrement by the step.</summary>
    public NumericFieldBuilder<T> Stepper(bool enabled = true) { _stepper = enabled; return this; }

    /// <summary>Make the <see cref="Prefix"/> label a horizontal scrub handle (drag to change the value).
    /// <paramref name="compact"/> tightens the label padding (for vector axis tags).</summary>
    public NumericFieldBuilder<T> DraggableLabel(string text, Color color, int dragDecimalPlaces = 2, bool compact = false)
    {
        _prefixText = text;
        _prefixColor = color;
        _dragLabel = true;
        _dragDecimalPlaces = dragDecimalPlaces;
        _prefixCompact = compact;
        return this;
    }

    /// <summary>
    /// Configure the type tag shown before the value that scrubs it when dragged: <c>f</c> in
    /// <see cref="OrigamiTheme.FloatTag"/> for floating-point fields, <c>i</c> in
    /// <see cref="OrigamiTheme.IntTag"/> for integer ones. Shift drags faster, Ctrl slower.
    /// On by default; pass <c>false</c> to hide it. Ignored when <see cref="Prefix"/> or
    /// <see cref="DraggableLabel"/> sets a label.
    /// </summary>
    public NumericFieldBuilder<T> DragHandle(bool enabled = true, int dragDecimalPlaces = 2)
    {
        _dragHandle = enabled;
        _dragHandleDecimals = dragDecimalPlaces;
        return this;
    }

    /// <summary>
    /// Called once a change is final: after a typed or stepper edit, and when a scrub is released
    /// (not on each scrub frame). Put expensive follow-up work here - saving to disk, rebuilding
    /// resources - rather than in the setter, which runs every frame while scrubbing.
    /// </summary>
    public NumericFieldBuilder<T> OnCommit(Action onCommit) { _onCommit = onCommit; return this; }

    // A field shows the same number most frames, so its text is kept per value, format and culture.
    [ThreadStatic] private static List<(string Format, CultureInfo Culture, Dictionary<T, string> Texts)>? s_texts;
    private const int MaxCachedTexts = 2048;

    private static string FormatCached(T value, string format, CultureInfo culture)
    {
        // Positive and negative zero compare equal but print differently, so zero is never cached.
        if (T.IsZero(value)) return FormatValue(value, format, culture);

        var all = s_texts ??= new();
        Dictionary<T, string>? texts = null;
        for (int i = 0; i < all.Count; i++)
        {
            if (!ReferenceEquals(all[i].Culture, culture) || all[i].Format != format) continue;
            texts = all[i].Texts;
            break;
        }
        if (texts == null) all.Add((format, culture, texts = new Dictionary<T, string>()));

        if (!texts.TryGetValue(value, out var text))
        {
            if (texts.Count >= MaxCachedTexts) texts.Clear();
            texts[value] = text = FormatValue(value, format, culture);
        }
        return text;
    }

    private static string FormatValue(T value, string format, CultureInfo culture)
    {
        try { return value.ToString(format, culture); }
        catch { return value.ToString() ?? ""; }
    }

    // ── Terminator ──────────────────────────────────────────────────

    public void Show()
    {
        if (Origami.IsReadOnly) _readOnly = true;
        // Format current value to a string for the underlying TextField. The text reflects
        // the live value every frame, which means a parse failure (e.g. user typed a stray
        // letter) gracefully reverts to the last good value as soon as we re-render.
        string text = FormatCached(_value, _format, _culture);

        // Translate validator on T into validator on string for the underlying TextField.
        Func<string, (bool, string?)>? stringValidator = null;
        if (_validator != null)
        {
            var captured = _validator;
            stringValidator = s =>
            {
                if (!TryParse(s, out T parsed)) return (false, "Invalid number");
                return captured(parsed);
            };
        }

        var tb = new TextFieldBuilder(_paper, _id, text, raw => HandleChange(raw), _theme)
            .Variant(_variant)
            .Width(_width)
            .Height(_height)
            .ReadOnly(_readOnly)
            .Placeholder(_placeholder)
            .SelectAllOnFocus(_selectAllOnFocus)
            .AutoFocus(_autoFocus)
            .SubmitOnEnter()
            .Mono()
            .CharFilter(NumericCharFilter);

        if (!string.IsNullOrEmpty(_leadingIconGlyph))
            tb.LeadingIcon(_leadingIconGlyph!);
        if (!string.IsNullOrEmpty(_trailingIconGlyph))
            tb.TrailingIcon(_trailingIconGlyph!, _trailingIconClick);
        if (string.IsNullOrEmpty(_prefixText) && _dragHandle)
            DraggableLabel(s_isFloatingPoint ? "f" : "i", s_isFloatingPoint ? _theme.FloatTag : _theme.IntTag, _dragHandleDecimals);
        if (!string.IsNullOrEmpty(_prefixText))
        {
            tb.Prefix(_prefixText!, _prefixColor ?? _theme.Ink.C400);
            if (_dragLabel) tb.PrefixDrag(AddDelta, BeginScrub, EndScrub, square: !_prefixCompact);
            if (_prefixCompact) tb.PrefixPad(6, 6);
        }
        if (!string.IsNullOrEmpty(_suffixText)) tb.Suffix(_suffixText!);
        if (_stepper) tb.TrailingContent(DrawStepper);
        if (_onCommit != null) tb.OnEditEnd(EndTypedEdit);

        if (_error != null) tb.Error(_error);
        if (_helperText != null) tb.HelperText(_helperText);
        if (stringValidator != null) tb.Validator(stringValidator);

        tb.Show();
    }

    // ── Stepper / scrub ─────────────────────────────────────────────

    private void DrawStepper()
    {
        var ink = _theme.Ink;
        Color bdSoft = _theme.BorderSoft;
        Color hover = _theme.Hover;
        float h = _height;

        _paper.Box($"{_id}_stepdiv").Width(1).Height(h).BackgroundColor(bdSoft).IsNotInteractable();
        using (_paper.Column($"{_id}_step").Width(22).Height(h).Enter())
        {
            StepBtn($"{_id}_stup", true, h * 0.5f, hover, ink.C200);
            StepBtn($"{_id}_stdn", false, h - h * 0.5f, hover, ink.C200);
        }
    }

    private void StepBtn(string bid, bool up, float bh, Color hover, Color icon)
    {
        float r = MathF.Max(0f, _theme.Metrics.Rounding - 1f);
        var b = _paper.Box(bid).Width(22).Height(bh)
            .Rounded(0f, up ? r : 0f, up ? 0f : r, 0f)   // round the outer corner against the field
            .Hovered.BackgroundColor(hover).End()
            .OnClick(0, (_, _) => Increment(up ? 1 : -1))
            .Cursor(PaperCursor.Pointer);
        b.OnPostLayout((hh, r) => _paper.Draw(ref hh, (canvas, rr) =>
        {
            float cx = (float)(rr.Min.X + rr.Size.X * 0.5f);
            float cy = (float)(rr.Min.Y + rr.Size.Y * 0.5f);
            canvas.SaveState();
            canvas.SetStrokeColor(icon);
            canvas.SetStrokeWidth(1.3f);
            canvas.SetStrokeCap(EndCapStyle.Round);
            canvas.SetStrokeJoint(JointStyle.Round);
            canvas.BeginPath();
            if (up) { canvas.MoveTo(cx - 3f, cy + 1.5f); canvas.LineTo(cx, cy - 1.6f); canvas.LineTo(cx + 3f, cy + 1.5f); }
            else { canvas.MoveTo(cx - 3f, cy - 1.5f); canvas.LineTo(cx, cy + 1.6f); canvas.LineTo(cx + 3f, cy - 1.5f); }
            canvas.Stroke();
            canvas.RestoreState();
        }));
    }

    private void Increment(int dir)
    {
        T s = _step ?? T.One;
        T nv = _value + s * T.CreateChecked(dir);
        if (_min is T mn && nv < mn) nv = mn;
        if (_max is T mx && nv > mx) nv = mx;
        _setter(nv);
        _onCommit?.Invoke();
    }

    // Per-scrub state, kept in the field's root storage between frames and dropped on release.
    private sealed class ScrubState
    {
        public double PerPixel;   // value change per pixel, fixed for the whole scrub
        public double Remainder;  // what rounding dropped, carried so slow drags still move
        public bool Changed;
    }

    private string ScrubKey => $"{_id}_scrub";

    private void BeginScrub() => _paper.SetRootStorage<ScrubState?>(ScrubKey, new ScrubState { PerPixel = ScrubRate() });

    private void EndScrub()
    {
        var state = _paper.GetRootStorage<ScrubState?>(ScrubKey);
        _paper.SetRootStorage<ScrubState?>(ScrubKey, null);
        if (state is { Changed: true }) _onCommit?.Invoke();
    }

    // Value change per pixel. An explicit Step sets it; otherwise it scales with the value's size at
    // the start of the scrub (1% per pixel), so 0.0167 and 6500 both scrub at a usable rate.
    private double ScrubRate()
    {
        if (_step is T s && s != T.Zero) return double.CreateSaturating(T.Abs(s)) * 0.5;
        double mag = Math.Abs(double.CreateSaturating(_value)) * 0.01;
        return s_isFloatingPoint
            ? (mag > 0 ? mag : 0.01)
            : Math.Max(mag, 0.25);
    }

    private void AddDelta(float dx)
    {
        // Normally created by BeginScrub; if the drag-start event hasn't fired yet, start one now.
        var state = _paper.GetRootStorage<ScrubState?>(ScrubKey);
        if (state == null)
        {
            state = new ScrubState { PerPixel = ScrubRate() };
            _paper.SetRootStorage<ScrubState?>(ScrubKey, state);
        }

        // Shift - faster (x4), Ctrl - finer (x0.1).
        double mult = 1;
        if (_paper.IsKeyDown(PaperKey.LeftShift) || _paper.IsKeyDown(PaperKey.RightShift)) mult = 4;
        else if (_paper.IsKeyDown(PaperKey.LeftControl) || _paper.IsKeyDown(PaperKey.RightControl)) mult = 0.1;
        double perPixel = state.PerPixel * mult;

        // Snap to the precision one pixel can express, but never coarser than the caller asked for.
        int decimals = Math.Clamp(Math.Max(_dragDecimalPlaces, (int)Math.Ceiling(-Math.Log10(perPixel))), 0, 15);

        double cur = double.CreateSaturating(_value);
        double target = cur + state.Remainder + dx * perPixel;
        double next = s_isFloatingPoint ? Math.Round(target, decimals) : Math.Round(target);
        double rem = target - next;
        if (_min is T mn && next < double.CreateSaturating(mn)) { next = double.CreateSaturating(mn); rem = 0; }
        if (_max is T mx && next > double.CreateSaturating(mx)) { next = double.CreateSaturating(mx); rem = 0; }
        state.Remainder = rem;
        if (next == cur) return;
        state.Changed = true;
        _setter(T.CreateSaturating(next)); // saturate: an unsigned field scrubbed below 0 stops at 0
    }

    // ── Internals ───────────────────────────────────────────────────

    private bool NumericCharFilter(char c, string current)
    {
        var nf = _culture.NumberFormat;

        if (char.IsDigit(c)) return true;

        // Math expression characters: operators, parentheses, letters (for pi, e, tau)
        if (c == '*' || c == '/' || c == '^' || c == '(' || c == ')') return true;
        if (char.IsLetter(c)) return true; // allows pi, e, tau

        // Sign: + and - allowed anywhere (they're also math operators)
        if (c == '-' || c == '+') return true;

        // Decimal separator
        if (s_isFloatingPoint)
        {
            string dec = nf.NumberDecimalSeparator;
            if (dec.Length == 1 && c == dec[0]) return true;
        }

        // Space (ignored by math parser)
        if (c == ' ') return true;

        return false;
    }

    private bool TryParse(string s, out T result)
    {
        if (string.IsNullOrWhiteSpace(s)) { result = default; return false; }

        var styles = s_isFloatingPoint
            ? NumberStyles.Float | NumberStyles.AllowThousands
            : NumberStyles.Integer | NumberStyles.AllowThousands;

        // Try direct parse first (fast path for plain numbers)
        if (T.TryParse(s, styles, _culture, out result))
            return true;

        // If direct parse fails, try evaluating as a math expression
        // This allows users to type things like "2*3+1", "360/16", "pi*2"
        if (MathParser.TryEvaluate(s, out double evaluated))
        {
            // Convert the double result to T
            string evalStr = evaluated.ToString("G15", System.Globalization.CultureInfo.InvariantCulture);
            return T.TryParse(evalStr, NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out result);
        }

        result = default;
        return false;
    }

    private void HandleChange(string raw)
    {
        if (!TryParse(raw, out T parsed)) return;

        // Apply step quantisation, then clamp.
        if (_step is T step && step != T.Zero)
        {
            // Round to nearest multiple. For floating-point we route through double (or
            // decimal) for proper half-away-from-zero rounding; for integers, the natural
            // (parsed / step) * step truncates and is exactly the snap behaviour we want.
            if (s_isFloatingPoint)
            {
                if (typeof(T) == typeof(decimal))
                {
                    decimal pd = (decimal)(object)parsed;
                    decimal sd = (decimal)(object)step;
                    decimal rounded = Math.Round(pd / sd, MidpointRounding.AwayFromZero) * sd;
                    parsed = (T)(object)rounded;
                }
                else
                {
                    double pd = Convert.ToDouble(parsed, _culture);
                    double sd = Convert.ToDouble(step, _culture);
                    double rounded = Math.Round(pd / sd, MidpointRounding.AwayFromZero) * sd;
                    parsed = T.CreateChecked(rounded);
                }
            }
            else
            {
                parsed = (parsed / step) * step;
            }
        }

        if (_min is T min && parsed < min) parsed = min;
        if (_max is T max && parsed > max) parsed = max;

        _setter(parsed);
        // Typing applies as it goes; the commit waits for the edit to end
        if (_onCommit != null) _paper.SetRootStorage(TypedKey, true);
    }

    private string TypedKey => $"{_id}_typed";

    private void EndTypedEdit()
    {
        if (!_paper.GetRootStorage<bool>(TypedKey)) return;
        _paper.SetRootStorage(TypedKey, false);
        _onCommit?.Invoke();
    }
}
