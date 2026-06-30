// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// Fluent builder for an Origami text field. One builder covers the common variations:
/// plain text, search, password, multi-line, with optional autocomplete.
/// </summary>
/// <remarks>
/// <para>Visual structure: an outer focusable row carries the border and background; inside,
/// optional leading slot, the editable text element (hooked to the row so focus / hover
/// inherit), and optional trailing slot (clear, password-eye, custom). Below the row,
/// helper or error text renders only when configured.</para>
/// <para>The text element is always Paper's <c>TextField</c> (single-line) or <c>TextArea</c>
/// (multi-line). Origami adds chrome, slot management, validation, and autocomplete on top.</para>
/// </remarks>
public sealed class TextFieldBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private readonly string _value;
    private readonly Action<string> _setter;

    private OrigamiVariant _variant = OrigamiVariant.Default;
    private UnitValue _width = UnitValue.Stretch();
    private float _height = 26f;
    private bool _readOnly;
    private int _maxLength;
    private string _placeholder = "";
    private bool _selectAllOnFocus;

    // Slots
    private string? _leadingIconGlyph;
    private Action? _leadingIconClick;
    private string? _trailingIconGlyph;
    private Action? _trailingIconClick;
    private bool _showClearButton;
    private string? _prefixText;
    private Color? _prefixColor;

    // Modes
    private bool _isSearch;
    private bool _isPassword;
    private char _passwordMask = '●'; // ●
    private bool _multiLine;
    private int _multiLineRows = 4;

    // Behaviour
    private bool _submitOnEnter;

    // Filtering
    private Func<char, string, bool>? _charFilter;

    // Validation / helper
    private string? _error;
    private string? _helperText;
    private Func<string, (bool ok, string? message)>? _validator;

    // AutoComplete
    private IReadOnlyList<string>? _acItems;
    private Func<string, string, bool>? _acFilter;
    private Action<string>? _acOnPick;
    private int _acMax = 8;

    // Element-storage keys for the deferred "force-update next frame" channel. Populated by
    // ApplyPick / clear-button click; drained at the top of the next frame's render.
    private const string KeyForcePending = "force_pending";
    private const string KeyForceValue   = "force_value";

    internal TextFieldBuilder(Paper paper, string id, string value, Action<string> setter, OrigamiTheme theme)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _setter = setter ?? throw new ArgumentNullException(nameof(setter));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _value = value ?? string.Empty;
    }

    // ── Variant ────────────────────────────────────────────────────────

    public TextFieldBuilder Variant(OrigamiVariant variant) { _variant = variant; return this; }
    public TextFieldBuilder Primary() => Variant(OrigamiVariant.Primary);
    public TextFieldBuilder Success() => Variant(OrigamiVariant.Success);
    public TextFieldBuilder Warning() => Variant(OrigamiVariant.Warning);
    public TextFieldBuilder Danger()  => Variant(OrigamiVariant.Danger);
    public TextFieldBuilder Info()    => Variant(OrigamiVariant.Info);
    public TextFieldBuilder Subtle()  => Variant(OrigamiVariant.Subtle);

    // ── Sizing ─────────────────────────────────────────────────────────

    public TextFieldBuilder Width(UnitValue width) { _width = width; return this; }
    public TextFieldBuilder Height(float height) { _height = MathF.Max(20, height); return this; }

    // ── Behaviour ──────────────────────────────────────────────────────

    public TextFieldBuilder ReadOnly(bool readOnly = true) { _readOnly = readOnly; return this; }
    public TextFieldBuilder MaxLength(int maxLength) { _maxLength = Math.Max(0, maxLength); return this; }
    public TextFieldBuilder Placeholder(string text) { _placeholder = text ?? string.Empty; return this; }
    public TextFieldBuilder SelectAllOnFocus(bool select = true) { _selectAllOnFocus = select; return this; }
    /// <summary>When true, pressing Enter in a single-line field commits the value and defocuses.</summary>
    public TextFieldBuilder SubmitOnEnter(bool submit = true) { _submitOnEnter = submit; return this; }

    // ── Modes ──────────────────────────────────────────────────────────

    /// <summary>Render as a search field: leading magnifier, default "Search..." placeholder, clear button.</summary>
    public TextFieldBuilder Search(string placeholder = "Search...")
    {
        _isSearch = true;
        if (string.IsNullOrEmpty(_placeholder)) _placeholder = placeholder;
        _showClearButton = true;
        return this;
    }

    /// <summary>Mask the value with <paramref name="maskChar"/>. Adds a show/hide eye toggle on the right.</summary>
    public TextFieldBuilder Password(char maskChar = '●')
    {
        _isPassword = true;
        _passwordMask = maskChar;
        return this;
    }

    /// <summary>Render as a multi-line text area. Height is computed from <paramref name="rows"/>.</summary>
    public TextFieldBuilder MultiLine(int rows = 4)
    {
        _multiLine = true;
        _multiLineRows = Math.Max(2, rows);
        return this;
    }

    // ── Slots ──────────────────────────────────────────────────────────

    /// <summary>Glyph to show at the left of the input. If <paramref name="onClick"/> is set the icon is clickable.</summary>
    public TextFieldBuilder LeadingIcon(string glyph, Action? onClick = null)
    {
        _leadingIconGlyph = glyph;
        _leadingIconClick = onClick;
        return this;
    }

    /// <summary>Glyph to show at the right of the input.</summary>
    public TextFieldBuilder TrailingIcon(string glyph, Action? onClick = null)
    {
        _trailingIconGlyph = glyph;
        _trailingIconClick = onClick;
        return this;
    }

    /// <summary>Show a small "X" clear button on the right while the value is non-empty.</summary>
    public TextFieldBuilder ClearButton(bool enabled = true) { _showClearButton = enabled; return this; }

    /// <summary>
    /// Show a small colored label badge inside the field, before the text input.
    /// Useful for channel labels like "X", "Y", "Z" in vector fields or "H", "S", "V" in color fields.
    /// </summary>
    public TextFieldBuilder Prefix(string text, Color color) { _prefixText = text; _prefixColor = color; return this; }

    // ── Filtering ──────────────────────────────────────────────────────

    public TextFieldBuilder CharFilter(Func<char, string, bool> filter) { _charFilter = filter; return this; }

    /// <summary>Allow only digits and an optional leading <c>-</c>.</summary>
    public TextFieldBuilder IntFilter() => CharFilter(static (c, cur) =>
        char.IsDigit(c) || (c == '-' && !cur.Contains('-')));

    /// <summary>Allow digits, single decimal point, optional leading <c>-</c>, and exponent <c>e/E</c>.</summary>
    public TextFieldBuilder FloatFilter() => CharFilter(static (c, cur) =>
    {
        if (char.IsDigit(c)) return true;
        if (c == '-' && (cur.Length == 0 || cur.EndsWith('e') || cur.EndsWith('E'))) return true;
        if ((c == '.' || c == ',') && !cur.Contains('.') && !cur.Contains(',')) return true;
        if ((c == 'e' || c == 'E') && cur.Length > 0 && !cur.Contains('e') && !cur.Contains('E')) return true;
        return false;
    });

    /// <summary>Allow ASCII letters and digits only.</summary>
    public TextFieldBuilder AlphaNumeric() => CharFilter(static (c, _) => char.IsLetterOrDigit(c));

    /// <summary>Reject space characters.</summary>
    public TextFieldBuilder NoSpaces() => CharFilter(static (c, _) => c != ' ');

    // ── Validation / helper ────────────────────────────────────────────

    /// <summary>Force an error state with a message under the field. Overrides any validator output.</summary>
    public TextFieldBuilder Error(string? message) { _error = message; return this; }

    /// <summary>Muted hint shown under the field when no error is active.</summary>
    public TextFieldBuilder HelperText(string? text) { _helperText = text; return this; }

    /// <summary>
    /// Validator returning <c>(ok, message)</c>. Runs every frame on the current value;
    /// when <c>ok</c> is false the field renders in the error state with the message.
    /// </summary>
    public TextFieldBuilder Validator(Func<string, (bool ok, string? message)> validator)
    {
        _validator = validator;
        return this;
    }

    // ── AutoComplete ───────────────────────────────────────────────────

    /// <summary>
    /// Show a popover of suggestions filtered by the current value. Picking a suggestion
    /// (click or Enter) replaces the value and closes the popover. Default filter is
    /// case-insensitive substring; override with <see cref="AutoCompleteFilter"/>.
    /// </summary>
    public TextFieldBuilder AutoComplete(IReadOnlyList<string> items, Action<string>? onPick = null, int max = 8)
    {
        _acItems = items;
        _acOnPick = onPick;
        _acMax = Math.Max(1, max);
        return this;
    }

    /// <summary>Custom autocomplete filter — <c>(item, currentValue) => bool</c>.</summary>
    public TextFieldBuilder AutoCompleteFilter(Func<string, string, bool> filter) { _acFilter = filter; return this; }

    // ── Terminator ─────────────────────────────────────────────────────

    public void Show()
    {
        if (Origami.IsReadOnly) _readOnly = true;
        var ramp = _theme.Get(_variant);
        var ink = _theme.Ink;
        var font = _theme.Font;
        if (font == null) return;
        var icons = _theme.Icons;
        bool subtle = _variant == OrigamiVariant.Subtle;

        // Validation runs every frame on the present value; explicit Error() overrides.
        string? errorMsg = _error;
        if (errorMsg == null && _validator != null)
        {
            var (ok, msg) = _validator(_value);
            if (!ok) errorMsg = msg ?? "Invalid";
        }
        bool hasError = !string.IsNullOrEmpty(errorMsg);

        // Borders: idle uses the variant ramp (or neutral for Default/Subtle); error
        // overrides with the danger ramp; focus-state border uses ramp.C500.
        Color bgColor     = subtle ? Color.Transparent
                                   : (_variant == OrigamiVariant.Default ? _theme.Neutral.C200 : ramp.C200);
        Color idleBorder  = hasError ? _theme.Red.C500
                          : subtle    ? Color.Transparent
                                      : (_variant == OrigamiVariant.Default ? _theme.Neutral.C400 : ramp.C400);
        Color focusBorder = hasError ? _theme.Red.C500 : ramp.C500;

        // Total widget height = field row + helper line (when present).
        float helperH = (hasError || !string.IsNullOrEmpty(_helperText)) ? 16f : 0f;
        float fieldH  = _multiLine ? ComputeMultiLineHeight() : _height;

        using (_paper.Column(_id).Width(_width).Height(fieldH).Enter())
        {
            ElementHandle rowHandle = default;

            // ── Field row (border, slots, text element) ────────────────
            var rowBuilder = _paper.Row($"{_id}_row")
                .Width(UnitValue.Stretch()).Height(fieldH)
                .BackgroundColor(bgColor)
                .BorderColor(idleBorder).BorderWidth(1)
                .Focused.BorderColor(focusBorder).End()
                .Rounded(_theme.Metrics.Rounding)
                .TabIndex(0);

            using (rowBuilder.Enter())
            {
                rowHandle = _paper.CurrentParent;

                // Leading slot ─────────────────────────────────────────
                if (_isSearch && !string.IsNullOrEmpty(icons.Search))
                {
                    DrawIcon($"{_id}_lead", icons.Search, ink.C300, leftPad: 8, rightPad: 4, click: null, font);
                }
                else if (!string.IsNullOrEmpty(_leadingIconGlyph))
                {
                    DrawIcon($"{_id}_lead", _leadingIconGlyph!, ink.C400, leftPad: 8, rightPad: 4, click: _leadingIconClick, font);
                }

                // Prefix badge ────────────────────────────────────────
                if (!string.IsNullOrEmpty(_prefixText) && font != null)
                {
                    float pfxH = _height - 4;
                    float pfxRound = MathF.Max(1, _theme.Metrics.Rounding - 2);
                    var pfxBg = _prefixColor.HasValue
                        ? Color.FromArgb(20, _prefixColor.Value.R, _prefixColor.Value.G, _prefixColor.Value.B)
                        : Color.FromArgb(20, ink.C400.R, ink.C400.G, ink.C400.B);
                    var pfxFg = _prefixColor ?? ink.C500;
                    _paper.Box($"{_id}_pfx")
                        .Height(pfxH).Width(pfxH)
                        .Margin(2, 0, 2, 0)
                        .Rounded(pfxRound)
                        .BackgroundColor(pfxBg)
                        .Text(_prefixText!, font)
                        .TextColor(pfxFg)
                        .FontSize(_theme.Metrics.FontSize - 1)
                        .Alignment(TextAlignment.MiddleCenter)
                        .IsNotInteractable();
                }

                // The edit element ─────────────────────────────────────
                var settings = ElementBuilder.TextInputSettings.Default;
                settings.Font = font;
                settings.TextColor = ink.C500;
                settings.PlaceholderColor = ink.C300;
                settings.Placeholder = _placeholder;
                settings.ReadOnly = _readOnly;
                settings.MaxLength = _maxLength;
                settings.SelectAllOnFocus = _selectAllOnFocus;
                settings.CharFilter = _charFilter;
                if (_multiLine) settings.DoWrap = true;

                bool pwShow = _paper.GetElementStorage(rowHandle, "pwShow", false);
                if (_isPassword && !pwShow)
                    settings.MaskChar = _passwordMask;

                // Drain any pending programmatic value push (autocomplete pick, clear-button,
                // future Validator rewrites). Paper applies this even when the field is focused;
                // we don't otherwise sync from external while focused, because filters /
                // formatters can round-trip in-progress chars (e.g. "0." > 0 > "0") and the
                // string compare would fire spurious select-alls.
                bool forcePending = _paper.GetElementStorage(rowHandle, KeyForcePending, false);
                if (forcePending)
                {
                    settings.ForceValue = _paper.GetElementStorage(rowHandle, KeyForceValue, _value ?? string.Empty);
                    settings.ForceSelectAll = true;
                    _paper.SetElementStorage(rowHandle, KeyForcePending, false);
                }

                bool noLeftPad = _isSearch || !string.IsNullOrEmpty(_leadingIconGlyph);
                float editLeftPad = noLeftPad ? 0 : 8;

                // Inner edit element. HookToParent + IsNotInteractable lets the row collect
                // clicks (so it gains focus via TabIndex(0)) while the textfield's own event
                // handlers (cursor placement, drag-select, keyboard) still fire via the hook.
                var editBuilder = _paper.Box($"{_id}_tf")
                    .Width(UnitValue.Stretch())
                    .Height(_multiLine ? UnitValue.Stretch() : _theme.Metrics.FontSize)
                    .Margin(editLeftPad, 0, UnitValue.Stretch(), UnitValue.Stretch())
                    .HookToParent()
                    .IsNotInteractable()
                    .FontSize(_theme.Metrics.FontSize);

                if (_multiLine)
                    editBuilder.TextArea(_value, settings,
                        onChange: v => _setter(v ?? string.Empty),
                        intID: _id.GetHashCode());
                else
                    editBuilder.TextField(_value, settings,
                        onChange: v => _setter(v ?? string.Empty),
                        intID: _id.GetHashCode());

                // Trailing slot ────────────────────────────────────────
                bool drewTrailing = false;

                if (_showClearButton && !string.IsNullOrEmpty(_value) && !_readOnly && !string.IsNullOrEmpty(icons.Close))
                {
                    var capturedRowForClear = rowHandle;
                    DrawIcon($"{_id}_clear", icons.Close, ink.C400, leftPad: 4, rightPad: _isPassword ? 0 : 6,
                        click: () =>
                        {
                            _setter(string.Empty);
                            // Force-push so the focused field's internal value also clears
                            // (without this, the focused-state-wins rule would leave the old
                            // text visible until blur).
                            QueueForceUpdate(capturedRowForClear, string.Empty);
                        }, font);
                    drewTrailing = true;
                }

                if (_isPassword && font != null)
                {
                    // Eye / Eye-Slash glyph would be nicest here; absent that, fall back to
                    // 'O' / 'X' so the toggle is still legible without a dedicated icon font.
                    string eye = pwShow ? (string.IsNullOrEmpty(icons.CheckboxOff) ? "x" : icons.CheckboxOff)
                                        : (string.IsNullOrEmpty(icons.CheckboxOn)  ? "o" : icons.CheckboxOn);
                    var capturedRow = rowHandle;
                    DrawIcon($"{_id}_eye", eye, ink.C400, leftPad: 4, rightPad: 6,
                        click: () => _paper.SetElementStorage(capturedRow, "pwShow", !pwShow), font);
                    drewTrailing = true;
                }

                if (!drewTrailing && !string.IsNullOrEmpty(_trailingIconGlyph))
                {
                    DrawIcon($"{_id}_trail", _trailingIconGlyph!, ink.C400, leftPad: 4, rightPad: 6,
                        click: _trailingIconClick, font);
                }
                else if (!drewTrailing)
                {
                    // Reserve trailing padding so the text doesn't kiss the right border.
                    _paper.Box($"{_id}_endpad").Width(6).Height(fieldH).IsNotInteractable();
                }

                // AutoComplete popover ─────────────────────────────────
                if (_acItems != null && _acItems.Count > 0)
                    DrawAutoCompletePopover(rowHandle, ramp);

                // Submit on Enter: defocus the field when Enter is pressed (single-line only)
                if (_submitOnEnter && !_multiLine && _paper.IsParentFocusWithin
                    && _paper.IsKeyPressed(PaperKey.Enter))
                {
                    _paper.ClearFocus();
                }
            }

            // ── Helper / error line ───────────────────────────────────
            if (helperH > 0f)
            {
                string text = errorMsg ?? _helperText ?? string.Empty;
                Color color = hasError ? _theme.Red.C600 : ink.C300;
                _paper.Box($"{_id}_help")
                    .Width(UnitValue.Stretch()).Height(helperH)
                    .Margin(2, 2, 2, 0)
                    .Alignment(TextAlignment.MiddleLeft)
                    .IsNotInteractable()
                    .Text(text, font).TextColor(color).FontSize(_theme.Metrics.FontSize - 2);
            }
        }
    }

    // ── Internals ──────────────────────────────────────────────────────

    private float ComputeMultiLineHeight()
    {
        // Approximate: lineHeight * rows + small inner pad. The text element itself fills
        // the row vertically; this just picks the row size based on requested rows.
        return MathF.Round(_theme.Metrics.FontSize * 1.4f * _multiLineRows + 8f);
    }

    private void DrawIcon(string id, string glyph, Color color, float leftPad, float rightPad, Action? click, Prowl.Scribe.FontFile font)
    {
        var box = _paper.Box(id)
            .Width(16).Height(_height)
            .Margin(leftPad, rightPad, 0, 0)
            .Alignment(TextAlignment.MiddleCenter)
            .Text(glyph, font)
            .TextColor(color)
            .FontSize(_theme.Metrics.FontSize);

        if (click != null)
        {
            box.Hovered.TextColor(_theme.Ink.C500).End();
            box.OnClick(_ => click());
        }
        else
        {
            box.IsNotInteractable();
        }
    }

    private void DrawAutoCompletePopover(ElementHandle rowHandle, OrigamiRamp ramp)
    {
        // Open whenever the row is focused, value is non-empty, has matches, and the user
        // hasn't suppressed it via Esc. Suppression auto-clears once the value goes empty
        // so future typing re-opens the popover without forcing the user to refocus.
        bool suppressed = _paper.GetElementStorage(rowHandle, "acHide", false);
        if (string.IsNullOrEmpty(_value) && suppressed)
        {
            _paper.SetElementStorage(rowHandle, "acHide", false);
            suppressed = false;
        }

        bool focused = _paper.IsElementFocused(rowHandle.Data.ID);
        if (!focused || string.IsNullOrEmpty(_value) || suppressed) return;

        // Filter candidates.
        var matches = new List<string>(Math.Min(_acItems!.Count, _acMax));
        for (int i = 0; i < _acItems.Count && matches.Count < _acMax; i++)
        {
            string item = _acItems[i];
            bool match = _acFilter != null
                ? _acFilter(item, _value)
                : item.Contains(_value, StringComparison.OrdinalIgnoreCase);
            if (match && !string.Equals(item, _value, StringComparison.Ordinal))
                matches.Add(item);
        }
        if (matches.Count == 0) return;

        // Highlight cursor.
        int hl = _paper.GetElementStorage(rowHandle, "acHl", 0);
        if (hl >= matches.Count) hl = matches.Count - 1;
        if (hl < 0) hl = 0;

        if (_paper.IsKeyPressed(PaperKey.Escape))
        {
            _paper.SetElementStorage(rowHandle, "acHide", true);
            return;
        }
        if (_paper.IsKeyPressedOrRepeating(PaperKey.Down))
        {
            hl = (hl + 1) % matches.Count;
            _paper.SetElementStorage(rowHandle, "acHl", hl);
        }
        else if (_paper.IsKeyPressedOrRepeating(PaperKey.Up))
        {
            hl = hl <= 0 ? matches.Count - 1 : hl - 1;
            _paper.SetElementStorage(rowHandle, "acHl", hl);
        }
        else if (_paper.IsKeyPressed(PaperKey.Enter) || _paper.IsKeyPressed(PaperKey.Tab))
        {
            string pick = matches[hl];
            ApplyPick(rowHandle, pick);
            return;
        }

        float rowH = 22f;
        float padY = 4f;
        float popH = matches.Count * rowH + padY * 2;
        var font = _theme.Font;

        var capturedRow = rowHandle;
        var capturedMatches = matches;
        Color popBorder = _variant is OrigamiVariant.Default or OrigamiVariant.Subtle
            ? _theme.Neutral.C400 : ramp.C400;

        using (_paper.Column($"{_id}_acpop")
            .PositionType(PositionType.SelfDirected)
            .Position(0, _height - 1)
            .Width(UnitValue.Stretch())
            .Height(popH)
            .BackgroundColor(_theme.Neutral.C200)
            .BorderColor(popBorder).BorderWidth(1)
            .Rounded(_theme.Metrics.Rounding)
            .Padding(4, 4, padY, padY)
            .HookToParent()
            .Layer(Layer.Topmost)
            .ClampToScreen()
            .StopEventPropagation()
            .Enter())
        {
            for (int i = 0; i < matches.Count; i++)
            {
                int idx = i;
                string item = matches[i];
                bool highlighted = i == hl;
                Color rowBg = highlighted ? ramp.C300 : Color.Transparent;

                _paper.Row($"{_id}_acrow_{i}")
                    .Height(rowH)
                    .BackgroundColor(rowBg)
                    .Hovered.BackgroundColor(ramp.C400).End()
                    .Rounded(2)
                    .ChildLeft(8).ChildRight(8)
                    .Alignment(TextAlignment.MiddleLeft)
                    .Text(item, font)
                    .TextColor(_theme.Ink.C500)
                    .FontSize(_theme.Metrics.FontSize)
                    .OnClick(_ => ApplyPick(capturedRow, item));
            }
        }
    }

    private void ApplyPick(ElementHandle rowHandle, string pick)
    {
        _setter(pick);
        _acOnPick?.Invoke(pick);
        // Push the picked value into the focused field's internal state on the next frame.
        QueueForceUpdate(rowHandle, pick);
        _paper.SetElementStorage(rowHandle, "acHide", true);
        _paper.SetElementStorage(rowHandle, "acHl", 0);
    }

    /// <summary>
    /// Queue a programmatic value push for the next frame. The drain at the top of
    /// <see cref="Show"/> turns this into a <c>TextInputSettings.ForceValue</c>, which Paper
    /// applies to the field's internal state even when focused.
    /// </summary>
    private void QueueForceUpdate(ElementHandle rowHandle, string value)
    {
        _paper.SetElementStorage(rowHandle, KeyForceValue, value);
        _paper.SetElementStorage(rowHandle, KeyForcePending, true);
    }
}
