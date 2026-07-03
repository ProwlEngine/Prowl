// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>Visual style for a toggle. Picks the chrome of the input itself; label/description chrome is shared.</summary>
public enum ToggleStyle
{
    /// <summary>Pill track with sliding knob. The default — most natural for boolean settings.</summary>
    Switch,
    /// <summary>Square box that fills with the variant colour when checked. Supports indeterminate.</summary>
    Checkbox,
    /// <summary>Circular outline with an inner dot when checked. Use directly for "single yes/no" or via <c>RadioGroup</c>.</summary>
    Radio,
}

/// <summary>Where the label sits relative to the toggle's visual.</summary>
public enum ToggleLabelPosition
{
    /// <summary>Label sits to the right of the visual (settings-row style — default).</summary>
    Right,
    /// <summary>Label sits to the left, visual pushed to the right edge of the row.</summary>
    Left,
    /// <summary>No label drawn — caller will provide their own.</summary>
    None,
}

/// <summary>Per-frame data passed to a custom visual renderer.</summary>
public readonly struct ToggleVisualContext
{
    /// <summary>The current boolean value.</summary>
    public readonly bool Value;
    /// <summary>Tri-state checkbox flag — null unless explicitly set.</summary>
    public readonly bool? Indeterminate;
    /// <summary>True when the toggle is interactable.</summary>
    public readonly bool Interactive;
    /// <summary>Eased 0..1 progress that follows <see cref="Value"/>. Use for color/position lerps.</summary>
    public readonly float AnimationT;
    /// <summary>Active surface ramp for the toggle's variant.</summary>
    public readonly OrigamiRamp Surface;
    /// <summary>Active ink ramp.</summary>
    public readonly OrigamiRamp Ink;
    /// <summary>Logical visual size in pixels (driven by <see cref="ToggleBuilder.Size"/>).</summary>
    public readonly float Size;
    /// <summary>The active theme — full access for callers that need icons, metrics, font.</summary>
    public readonly OrigamiTheme Theme;

    internal ToggleVisualContext(bool value, bool? indeterminate, bool interactive,
        float animT, OrigamiRamp surface, OrigamiRamp ink, float size, OrigamiTheme theme)
    {
        Value = value;
        Indeterminate = indeterminate;
        Interactive = interactive;
        AnimationT = animT;
        Surface = surface;
        Ink = ink;
        Size = size;
        Theme = theme;
    }
}

/// <summary>
/// Fluent builder for a toggle / switch / checkbox / radio. Construct via the
/// <c>Origami.Toggle</c>, <c>Origami.Switch</c>, <c>Origami.Checkbox</c> or <c>Origami.Radio</c>
/// factories; chain modifiers; call <see cref="Show"/> to render.
/// </summary>
/// <remarks>
/// <para>Controlled widget — caller owns the value, Origami invokes the setter on change.
/// Click anywhere on the row toggles. Space toggles when focused (<c>Tab</c> reaches it because
/// the row carries a TabIndex).</para>
/// <para>The visual reads its colours from the active <see cref="OrigamiVariant"/>'s ramp;
/// <c>Default</c> falls back to the theme's <c>Primary</c> ramp so the on-state still pops, and
/// <c>Subtle</c> stays on the <c>Neutral</c> ramp for whisper-quiet inline toggles.</para>
/// </remarks>
public sealed class ToggleBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private readonly bool _value;
    private readonly Action<bool> _setter;

    private OrigamiVariant _variant = OrigamiVariant.Default;
    private ToggleStyle _style = ToggleStyle.Switch;
    private float _size = 18f;

    private string? _label;
    private string? _description;
    private ToggleLabelPosition _labelPosition = ToggleLabelPosition.Right;

    private bool _disabled;
    private bool _readOnly;
    private bool? _indeterminate;
    private bool _stretch;

    private string? _onText;
    private string? _offText;
    private string? _onGlyph;
    private string? _offGlyph;

    private string? _error;
    private string? _helperText;

    private Action<ToggleVisualContext>? _customVisual;

    internal ToggleBuilder(Paper paper, string id, bool value, Action<bool> setter, OrigamiTheme theme)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _setter = setter ?? throw new ArgumentNullException(nameof(setter));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _value = value;
    }

    // ── Variant ────────────────────────────────────────────────────────

    public ToggleBuilder Variant(OrigamiVariant v) { _variant = v; return this; }
    public ToggleBuilder Primary() => Variant(OrigamiVariant.Primary);
    public ToggleBuilder Success() => Variant(OrigamiVariant.Success);
    public ToggleBuilder Warning() => Variant(OrigamiVariant.Warning);
    public ToggleBuilder Danger()  => Variant(OrigamiVariant.Danger);
    public ToggleBuilder Info()    => Variant(OrigamiVariant.Info);
    public ToggleBuilder Subtle()  => Variant(OrigamiVariant.Subtle);

    // ── Style ──────────────────────────────────────────────────────────

    public ToggleBuilder Style(ToggleStyle style) { _style = style; return this; }
    public ToggleBuilder AsSwitch()   => Style(ToggleStyle.Switch);
    public ToggleBuilder AsCheckbox() => Style(ToggleStyle.Checkbox);
    public ToggleBuilder AsRadio()    => Style(ToggleStyle.Radio);

    // ── Sizing ─────────────────────────────────────────────────────────

    /// <summary>Visual size in pixels (default 18). The track / box / dot scale around this.</summary>
    public ToggleBuilder Size(float size) { _size = MathF.Max(12, size); return this; }
    public ToggleBuilder Small()  => Size(14);
    public ToggleBuilder Medium() => Size(18);
    public ToggleBuilder Large()  => Size(24);
    /// <summary>Stretch the row to fill the available width — useful when label is on the left so the visual hugs the right edge.</summary>
    public ToggleBuilder Stretch(bool stretch = true) { _stretch = stretch; return this; }

    // ── Label / description ────────────────────────────────────────────

    public ToggleBuilder Label(string? text) { _label = text; return this; }
    public ToggleBuilder LabelLeft(string text)  { _label = text; _labelPosition = ToggleLabelPosition.Left; return this; }
    public ToggleBuilder LabelRight(string text) { _label = text; _labelPosition = ToggleLabelPosition.Right; return this; }
    public ToggleBuilder NoLabel() { _label = null; _labelPosition = ToggleLabelPosition.None; return this; }
    /// <summary>Muted subtitle drawn below the label, for "settings row with explanation" patterns.</summary>
    public ToggleBuilder Description(string? text) { _description = text; return this; }

    // ── State ──────────────────────────────────────────────────────────

    public ToggleBuilder Disabled(bool disabled = true) { _disabled = disabled; return this; }
    public ToggleBuilder ReadOnly(bool readOnly = true) { _readOnly = readOnly; return this; }

    /// <summary>
    /// Tri-state for checkboxes. <c>true</c> renders a dash regardless of <c>value</c> (use it
    /// to reflect "some children checked"). Ignored by <c>Switch</c> and <c>Radio</c> styles.
    /// </summary>
    public ToggleBuilder Indeterminate(bool? indeterminate = true) { _indeterminate = indeterminate; return this; }

    // ── On/off content (Switch only) ───────────────────────────────────

    /// <summary>Text shown inside the switch track on the on-side. Best for short ones — "ON", "Yes".</summary>
    public ToggleBuilder OnText(string text) { _onText = text; return this; }
    /// <summary>Text shown inside the switch track on the off-side.</summary>
    public ToggleBuilder OffText(string text) { _offText = text; return this; }
    /// <summary>Glyph rendered inside the switch knob when on.</summary>
    public ToggleBuilder OnGlyph(string glyph) { _onGlyph = glyph; return this; }
    /// <summary>Glyph rendered inside the switch knob when off.</summary>
    public ToggleBuilder OffGlyph(string glyph) { _offGlyph = glyph; return this; }

    // ── Validation / helper ────────────────────────────────────────────

    /// <summary>Force an error state with a message under the row.</summary>
    public ToggleBuilder Error(string? message) { _error = message; return this; }
    /// <summary>Muted hint under the row when no error is active.</summary>
    public ToggleBuilder HelperText(string? text) { _helperText = text; return this; }

    // ── Custom rendering ───────────────────────────────────────────────

    /// <summary>Replace the visual entirely. The row's click target and label chrome are still owned by Origami.</summary>
    public ToggleBuilder CustomVisual(Action<ToggleVisualContext> render) { _customVisual = render; return this; }

    // ── Terminator ─────────────────────────────────────────────────────

    public void Show()
    {
        if (Origami.IsReadOnly) _disabled = true;
        var ramp    = _theme.Get(_variant);
        var ink     = _theme.Ink;
        var font    = _theme.Font;
        var icons   = _theme.Icons;
        var metrics = _theme.Metrics;

        bool interactive = !_disabled && !_readOnly;
        bool hasError    = !string.IsNullOrEmpty(_error);
        string? helpLine = hasError ? _error : _helperText;
        float helperH    = !string.IsNullOrEmpty(helpLine) ? 16f : 0f;

        bool drawLabel = _labelPosition != ToggleLabelPosition.None && !string.IsNullOrEmpty(_label);
        bool hasDesc   = !string.IsNullOrEmpty(_description);

        // Visual footprint
        (float vw, float vh) = _style switch
        {
            ToggleStyle.Switch => (_size * 1.9f, _size * 1.05f),
            _ => (_size, _size),
        };

        // Row height auto-grows to the bigger of visual or label block.
        float labelH = hasDesc ? metrics.FontSize + 4 + (metrics.FontSize - 2) + 4 : metrics.FontSize + 6;
        float rowH = MathF.Max(vh + 4, labelH);

        using (_paper.Column(_id)
            .Width(_stretch ? UnitValue.Stretch() : UnitValue.Auto)
            .Height(UnitValue.Auto)
            .Enter())
        {
            // ── Row (focusable + clickable) ───────────────────────────
            var row = _paper.Row($"{_id}_row")
                .Width(_stretch ? UnitValue.Stretch() : UnitValue.Auto)
                .Height(rowH)
                .Rounded(metrics.Rounding)
                .BorderWidth(1).BorderColor(Color.Transparent);

            if (interactive)
            {
                row.TabIndex(0);
                row.OnClick(_ => _setter(!_value));
            }

            using (row.Enter())
            {
                var rowHandle = _paper.CurrentParent;
                float t = _paper.AnimateBool(_value, 0.18f);

                // Keyboard: Space toggles when focused (Tab reaches via TabIndex above).
                if (interactive && _paper.IsElementFocused(rowHandle.Data.ID)
                    && _paper.IsKeyPressed(PaperKey.Space))
                {
                    _setter(!_value);
                }

                bool labelLeft  = drawLabel && _labelPosition == ToggleLabelPosition.Left;
                bool labelRight = drawLabel && _labelPosition == ToggleLabelPosition.Right;

                if (labelLeft)
                {
                    DrawLabelColumn(leftPad: 6, rightPad: 6, stretch: true);
                    DrawVisual(t, ramp, ink, leftPad: 0, rightPad: 6);
                }
                else if (labelRight)
                {
                    DrawVisual(t, ramp, ink, leftPad: 6, rightPad: 0);
                    DrawLabelColumn(leftPad: 8, rightPad: 6, stretch: _stretch);
                }
                else
                {
                    DrawVisual(t, ramp, ink, leftPad: 4, rightPad: 4);
                }
            }

            // ── Helper / error line ───────────────────────────────────
            if (helperH > 0f && font != null)
            {
                Color color = hasError ? _theme.Red.C600 : ink.C300;
                _paper.Box($"{_id}_help")
                    .Width(UnitValue.Stretch()).Height(helperH)
                    .Margin(4, 2, 2, 0)
                    .Alignment(TextAlignment.MiddleLeft)
                    .IsNotInteractable()
                    .Text(helpLine!, font)
                    .TextColor(color)
                    .FontSize(metrics.FontSize - 2);
            }
        }
    }

    // ── Label column ───────────────────────────────────────────────────

    private void DrawLabelColumn(float leftPad, float rightPad, bool stretch)
    {
        var ink     = _theme.Ink;
        var font    = _theme.Font;
        var metrics = _theme.Metrics;
        if (font == null) return;

        bool hasDesc = !string.IsNullOrEmpty(_description);
        Color labelColor = _disabled ? ink.C300 : ink.C500;
        Color descColor  = _disabled ? ink.C200 : ink.C300;

        UnitValue colW = stretch ? UnitValue.Stretch() : UnitValue.Auto;

        using (_paper.Column($"{_id}_lblcol")
            .Width(colW)
            .Height(UnitValue.Auto)
            .Margin(leftPad, rightPad, UnitValue.Stretch(), UnitValue.Stretch())
            .IsNotInteractable()
            .Enter())
        {
            _paper.Box($"{_id}_lbl")
                .Width(colW)
                .Height(metrics.FontSize + 4)
                .Alignment(TextAlignment.MiddleLeft)
                .IsNotInteractable()
                .Text(_label!, font)
                .TextColor(labelColor)
                .FontSize(metrics.FontSize);

            if (hasDesc)
            {
                _paper.Box($"{_id}_desc")
                    .Width(colW)
                    .Height(metrics.FontSize + 2)
                    .Margin(0, 0, 1, 0)
                    .Alignment(TextAlignment.MiddleLeft)
                    .IsNotInteractable()
                    .Text(_description!, font)
                    .TextColor(descColor)
                    .FontSize(metrics.FontSize - 2);
            }
        }
    }

    // ── Visual dispatch ────────────────────────────────────────────────

    private void DrawVisual(float t, OrigamiRamp ramp, OrigamiRamp ink, float leftPad, float rightPad)
    {
        if (_customVisual != null)
        {
            var ctx = new ToggleVisualContext(_value, _indeterminate, !_disabled && !_readOnly,
                t, ramp, ink, _size, _theme);
            // Wrap custom visual in a sized box so the row layout is stable regardless of what
            // the caller draws inside.
            using (_paper.Box($"{_id}_cust")
                .Width(_style == ToggleStyle.Switch ? _size * 1.9f : _size)
                .Height(_style == ToggleStyle.Switch ? _size * 1.05f : _size)
                .Margin(leftPad, rightPad, UnitValue.Stretch(), UnitValue.Stretch())
                .IsNotInteractable()
                .Enter())
            {
                _customVisual(ctx);
            }
            return;
        }

        switch (_style)
        {
            case ToggleStyle.Switch:   DrawSwitch(t, ramp, ink, leftPad, rightPad); break;
            case ToggleStyle.Checkbox: DrawCheckbox(t, ramp, ink, leftPad, rightPad); break;
            case ToggleStyle.Radio:    DrawRadio(t, ramp, ink, leftPad, rightPad); break;
        }
    }

    // ── Color helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Pick the on-state ramp. Default and Subtle don't have a strong "on" colour of their own
    /// (Default is neutral, Subtle is intentionally quiet) — we promote Default to Primary so the
    /// on-state still reads, and keep Subtle on Neutral for whisper-quiet toggles.
    /// </summary>
    private OrigamiRamp OnRamp() => _variant switch
    {
        OrigamiVariant.Default => _theme.Primary,
        OrigamiVariant.Subtle  => _theme.Neutral,
        _ => _theme.Get(_variant),
    };

    private static Color WithAlpha(Color c, float a)
    {
        int alpha = Math.Clamp((int)(a * 255), 0, 255);
        return Color.FromArgb(alpha, c.R, c.G, c.B);
    }

    /// <summary>Overshooting ease so the knob springs slightly past its target (prototype cubic-bezier feel).</summary>
    private static float EaseOutBack(float t)
    {
        const float c1 = 1.4f, c3 = c1 + 1f;
        float p = t - 1f;
        return 1f + c3 * p * p * p + c1 * p * p;
    }

    // ── Switch (single Box + Canvas Draw) ──────────────────────────────

    private void DrawSwitch(float t, OrigamiRamp ramp, OrigamiRamp ink, float leftPad, float rightPad)
    {
        var font    = _theme.Font;
        var metrics = _theme.Metrics;

        float trackW = _size * 1.9f;
        float trackH = _size * 1.05f;

        // Capture every per-frame value the closure needs — the builder doesn't survive past Show().
        var onRamp = OnRamp();
        Color offBg   = Color.FromArgb(26, 255, 255, 255); // light translucent pill (prototype .tg)
        Color onBg    = _variant == OrigamiVariant.Subtle ? _theme.Neutral.C500 : onRamp.C500;
        Color trackBg = OrigamiRamp.LerpColor(offBg, onBg, t);
        Color borderCol  = _theme.Neutral.C200;
        Color knobFill   = _theme.Ink.C600;
        Color textOnFg   = _theme.Ink.C600;
        Color textOffFg  = _theme.Ink.C500;
        Color glyphOffFg = _theme.Ink.C300;
        if (_disabled)
        {
            // Fade the whole control to ~40% (prototype disabled toggle is just dimmed, not recoloured).
            trackBg  = Color.FromArgb((int)(trackBg.A * 0.4f), trackBg.R, trackBg.G, trackBg.B);
            knobFill = _theme.Ink.C300;
        }

        string? onText = _onText, offText = _offText, onGlyph = _onGlyph, offGlyph = _offGlyph;
        float fontSize = metrics.FontSize;

        using (_paper.Box($"{_id}_track")
            .Width(trackW).Height(trackH)
            .Margin(leftPad, rightPad, UnitValue.Stretch(), UnitValue.Stretch())
            .IsNotInteractable()
            .Enter())
        {
            _paper.Draw((canvas, rect) =>
            {
                float x = (float)rect.Min.X;
                float y = (float)rect.Min.Y;
                float w = (float)rect.Size.X;
                float h = (float)rect.Size.Y;
                float pad   = MathF.Max(2f, h * 0.12f);
                float knob  = h - pad * 2f;
                float knobX = x + pad + (w - knob - pad * 2f) * EaseOutBack(t); // springy slide
                float knobY = y + pad;
                float textBoxW = w - knob - pad * 2f;

                // On-glow beneath the track (fades in with the value). Save/Restore so the box
                // brush never leaks into the track / knob / text draws that follow.
                if (t > 0.02f)
                {
                    Color glow = Color.FromArgb((int)(95 * t), onBg.R, onBg.G, onBg.B);
                    canvas.SaveState();
                    canvas.SetBoxBrush(x + w * 0.5f, y + h * 0.5f + 2f, w - 2f, h - 2f, h * 0.5f, 13f, glow, Color.FromArgb(0, onBg.R, onBg.G, onBg.B));
                    canvas.BeginPath();
                    canvas.Rect(x - 16f, y - 10f, w + 32f, h + 28f);
                    canvas.Fill();
                    canvas.RestoreState();
                }

                // Track — single hardware-accelerated rounded rect.
                canvas.RoundedRectFilled(x, y, w, h, h * 0.5f, trackBg);

                // Off-state hairline border (fades out as it turns on).
                if (t < 0.98f)
                {
                    canvas.SaveState();
                    canvas.SetStrokeColor(Color.FromArgb((int)(borderCol.A * (1f - t)), borderCol.R, borderCol.G, borderCol.B));
                    canvas.SetStrokeWidth(1f);
                    canvas.BeginPath();
                    canvas.RoundedRect(x + 0.5f, y + 0.5f, w - 1f, h - 1f, h * 0.5f);
                    canvas.Stroke();
                    canvas.RestoreState();
                }

                // OnText — visible on the left while on (the side the knob has vacated).
                if (font != null && !string.IsNullOrEmpty(onText) && t > 0.05f)
                {
                    float fs = fontSize - 3;
                    var ts = canvas.MeasureText(onText!, fs, font);
                    float tx = x + pad + (textBoxW - (float)ts.X) * 0.5f;
                    float ty = y + (h - (float)ts.Y) * 0.5f;
                    canvas.DrawText(onText!, tx, ty, WithAlpha(textOnFg, t), fs, font);
                }
                if (font != null && !string.IsNullOrEmpty(offText) && t < 0.95f)
                {
                    float fs = fontSize - 3;
                    var ts = canvas.MeasureText(offText!, fs, font);
                    float tx = x + knob + pad + (textBoxW - (float)ts.X) * 0.5f;
                    float ty = y + (h - (float)ts.Y) * 0.5f;
                    canvas.DrawText(offText!, tx, ty, WithAlpha(textOffFg, 1f - t), fs, font);
                }

                // Knob — full-radius rounded rect == circle, cheaper than path Circle.
                canvas.RoundedRectFilled(knobX, knobY, knob, knob, knob * 0.5f, knobFill);

                string? glyph = t > 0.5f ? onGlyph : offGlyph;
                if (!string.IsNullOrEmpty(glyph) && font != null)
                {
                    Color gc = t > 0.5f ? onBg : glyphOffFg;
                    float fs = fontSize - 4;
                    var ts = canvas.MeasureText(glyph!, fs, font);
                    float gx = knobX + (knob - (float)ts.X) * 0.5f;
                    float gy = knobY + (knob - (float)ts.Y) * 0.5f;
                    canvas.DrawText(glyph!, gx, gy, gc, fs, font);
                }
            });
        }
    }

    // ── Checkbox (single Box + Canvas Draw) ────────────────────────────

    private void DrawCheckbox(float t, OrigamiRamp ramp, OrigamiRamp ink, float leftPad, float rightPad)
    {
        var metrics = _theme.Metrics;

        bool isIndet = _indeterminate == true;
        float effT = isIndet ? 1f : t;

        var onRamp = OnRamp();
        Color onColor  = _variant == OrigamiVariant.Subtle ? _theme.Neutral.C500 : onRamp.C500;
        Color offColor = _theme.Neutral.C200;
        Color fill = OrigamiRamp.LerpColor(offColor, onColor, effT);
        Color border = _disabled ? _theme.Neutral.C400
                     : effT > 0.5f ? onColor
                     : (_variant == OrigamiVariant.Default || _variant == OrigamiVariant.Subtle)
                         ? _theme.Neutral.C500 : ramp.C500;
        if (_disabled)
            fill = OrigamiRamp.LerpColor(fill, _theme.Neutral.C400, 0.5f);

        Color glyphFg = WithAlpha(_theme.Ink.C700, effT);
        bool drawMark = effT > 0.05f;
        float radius = _size * 0.32f;

        using (_paper.Box($"{_id}_chk")
            .Width(_size).Height(_size)
            .Margin(leftPad, rightPad, UnitValue.Stretch(), UnitValue.Stretch())
            .IsNotInteractable()
            .Enter())
        {
            _paper.Draw((canvas, rect) =>
            {
                float x = (float)rect.Min.X;
                float y = (float)rect.Min.Y;
                float w = (float)rect.Size.X;
                float h = (float)rect.Size.Y;

                // Hairline border via two stacked filled rounded rects — no path/stroke pass.
                canvas.RoundedRectFilled(x, y, w, h, radius, border);
                canvas.RoundedRectFilled(x + 1, y + 1, w - 2, h - 2, MathF.Max(0, radius - 1), fill);

                // Vector check / dash (the UI font has no tick glyph).
                if (drawMark)
                {
                    canvas.SaveState();
                    canvas.SetStrokeColor(glyphFg);
                    canvas.SetStrokeWidth(MathF.Max(1.4f, h * 0.12f));
                    canvas.SetStrokeCap(EndCapStyle.Round);
                    canvas.SetStrokeJoint(JointStyle.Round);
                    canvas.BeginPath();
                    if (isIndet)
                    {
                        canvas.MoveTo(x + w * 0.28f, y + h * 0.5f);
                        canvas.LineTo(x + w * 0.72f, y + h * 0.5f);
                    }
                    else
                    {
                        canvas.MoveTo(x + w * 0.26f, y + h * 0.52f);
                        canvas.LineTo(x + w * 0.43f, y + h * 0.68f);
                        canvas.LineTo(x + w * 0.74f, y + h * 0.34f);
                    }
                    canvas.Stroke();
                    canvas.RestoreState();
                }
            });
        }
    }

    // ── Radio (single Box + Canvas Draw) ───────────────────────────────

    private void DrawRadio(float t, OrigamiRamp ramp, OrigamiRamp ink, float leftPad, float rightPad)
    {
        // Prototype .w2rdot: 2px ring (bd-strong, accent when on) + an 8px accent dot with a glow.
        var onRamp = OnRamp();
        Color onColor = _variant == OrigamiVariant.Subtle ? _theme.Neutral.C600 : onRamp.C500;
        Color offBorder = _theme.BorderStrong; // bd-strong
        Color ringCol = _disabled ? _theme.Neutral.C400 : OrigamiRamp.LerpColor(offBorder, onColor, t);
        Color dotCol = _disabled ? _theme.Ink.C300 : onColor;
        float anim = t;

        using (_paper.Box($"{_id}_radio")
            .Width(_size).Height(_size)
            .Margin(leftPad, rightPad, UnitValue.Stretch(), UnitValue.Stretch())
            .IsNotInteractable()
            .Enter())
        {
            _paper.Draw((canvas, rect) =>
            {
                float x = (float)rect.Min.X;
                float y = (float)rect.Min.Y;
                float w = (float)rect.Size.X;
                float h = (float)rect.Size.Y;
                float r = MathF.Min(w, h) * 0.5f;
                float cx = x + w * 0.5f;
                float cy = y + h * 0.5f;

                // Dot glow (fades in with the value). Save/Restore so the box brush never leaks
                // into the ring stroke / dot fill that follow.
                if (anim > 0.02f && !_disabled)
                {
                    Color glow = Color.FromArgb((int)(90 * anim), onColor.R, onColor.G, onColor.B);
                    canvas.SaveState();
                    canvas.SetBoxBrush(cx, cy, r, r, r, 7f, glow, Color.FromArgb(0, onColor.R, onColor.G, onColor.B));
                    canvas.BeginPath();
                    canvas.Rect(x - 8f, y - 8f, w + 16f, h + 16f);
                    canvas.Fill();
                    canvas.RestoreState();
                }

                // Ring (stroked so the centre stays transparent).
                canvas.SaveState();
                canvas.SetStrokeColor(ringCol);
                canvas.SetStrokeWidth(2f);
                canvas.BeginPath();
                canvas.Circle(cx, cy, r - 1f);
                canvas.Stroke();
                canvas.RestoreState();

                float dotR = r * 0.47f * anim;
                if (dotR >= 0.5f)
                    canvas.CircleFilled(cx, cy, dotR, dotCol);
            });
        }
    }
}
