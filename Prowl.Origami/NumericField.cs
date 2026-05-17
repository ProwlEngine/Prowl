// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Globalization;
using System.Numerics;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

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
    private float _height = 26f;
    private bool _readOnly;
    private string _placeholder = "";
    private bool _selectAllOnFocus;

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

    internal NumericFieldBuilder(Paper paper, string id, T value, Action<T> setter, OrigamiTheme theme)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _setter = setter ?? throw new ArgumentNullException(nameof(setter));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _value = value;
    }

    // ── Variant + sizing (delegated to TextField behind the scenes) ─

    public NumericFieldBuilder<T> Variant(OrigamiVariant variant) { _variant = variant; return this; }
    public NumericFieldBuilder<T> Primary() => Variant(OrigamiVariant.Primary);
    public NumericFieldBuilder<T> Success() => Variant(OrigamiVariant.Success);
    public NumericFieldBuilder<T> Warning() => Variant(OrigamiVariant.Warning);
    public NumericFieldBuilder<T> Danger()  => Variant(OrigamiVariant.Danger);
    public NumericFieldBuilder<T> Info()    => Variant(OrigamiVariant.Info);
    public NumericFieldBuilder<T> Subtle()  => Variant(OrigamiVariant.Subtle);

    public NumericFieldBuilder<T> Width(UnitValue width) { _width = width; return this; }
    public NumericFieldBuilder<T> Height(float height) { _height = MathF.Max(20, height); return this; }
    public NumericFieldBuilder<T> ReadOnly(bool readOnly = true) { _readOnly = readOnly; return this; }
    public NumericFieldBuilder<T> Placeholder(string text) { _placeholder = text ?? string.Empty; return this; }
    public NumericFieldBuilder<T> SelectAllOnFocus(bool select = true) { _selectAllOnFocus = select; return this; }

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
    public NumericFieldBuilder<T> Prefix(string text, Color color) { _prefixText = text; _prefixColor = color; return this; }
    public NumericFieldBuilder<T> TrailingIcon(string glyph, Action? onClick = null)
    {
        _trailingIconGlyph = glyph;
        _trailingIconClick = onClick;
        return this;
    }

    // ── Terminator ──────────────────────────────────────────────────

    public void Show()
    {
        if (Origami.IsReadOnly) _readOnly = true;
        // Format current value to a string for the underlying TextField. The text reflects
        // the live value every frame, which means a parse failure (e.g. user typed a stray
        // letter) gracefully reverts to the last good value as soon as we re-render.
        string text;
        try { text = _value.ToString(_format, _culture); }
        catch { text = _value.ToString(); }

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
            .SubmitOnEnter()
            .CharFilter(NumericCharFilter);

        if (!string.IsNullOrEmpty(_leadingIconGlyph))
            tb.LeadingIcon(_leadingIconGlyph!);
        if (!string.IsNullOrEmpty(_trailingIconGlyph))
            tb.TrailingIcon(_trailingIconGlyph!, _trailingIconClick);
        if (!string.IsNullOrEmpty(_prefixText))
            tb.Prefix(_prefixText!, _prefixColor ?? Color.FromArgb(255, 200, 200, 200));

        if (_error != null) tb.Error(_error);
        if (_helperText != null) tb.HelperText(_helperText);
        if (stringValidator != null) tb.Validator(stringValidator);

        tb.Show();
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
    }
}
