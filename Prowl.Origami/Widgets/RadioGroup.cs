// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>Layout direction for a <see cref="RadioGroupBuilder{T}"/>.</summary>
public enum RadioGroupOrientation
{
    /// <summary>Stack radios vertically (one per row). Default.</summary>
    Vertical,
    /// <summary>Lay radios out left-to-right in a single row.</summary>
    Horizontal,
}

/// <summary>
/// Fluent builder for a single-select radio group bound to a typed list of items. Construct
/// via <c>Origami.RadioGroup&lt;T&gt;</c>; chain modifiers; call <see cref="Show"/> to render.
/// </summary>
/// <remarks>
/// Each row is an Origami <see cref="ToggleBuilder"/> in radio style with the item's display
/// string as its label. Picking a row delivers the new value to the setter. Equality is via
/// <see cref="EqualityComparer{T}.Default"/>; override with <see cref="Comparer"/> if needed.
/// </remarks>
public sealed class RadioGroupBuilder<T>
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private readonly IReadOnlyList<T> _items;
    private readonly T _value;
    private readonly Action<T> _setter;

    private OrigamiVariant _variant = OrigamiVariant.Default;
    private RadioGroupOrientation _orientation = RadioGroupOrientation.Vertical;
    private float _size = 18f;
    private Func<T, string>? _display;
    private Func<T, string?>? _description;
    private Func<T, bool>? _isItemEnabled;
    private IEqualityComparer<T> _comparer = EqualityComparer<T>.Default;
    private bool _disabled;
    private bool _readOnly;
    private string? _error;
    private string? _helperText;
    private float _gap = 4f;

    internal RadioGroupBuilder(Paper paper, string id, T value, Action<T> setter,
        IReadOnlyList<T> items, OrigamiTheme theme)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _setter = setter ?? throw new ArgumentNullException(nameof(setter));
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _value = value;
    }

    // ── Variant ────────────────────────────────────────────────────────

    public RadioGroupBuilder<T> Variant(OrigamiVariant v) { _variant = v; return this; }
    public RadioGroupBuilder<T> Primary() => Variant(OrigamiVariant.Primary);
    public RadioGroupBuilder<T> Success() => Variant(OrigamiVariant.Success);
    public RadioGroupBuilder<T> Warning() => Variant(OrigamiVariant.Warning);
    public RadioGroupBuilder<T> Danger()  => Variant(OrigamiVariant.Danger);
    public RadioGroupBuilder<T> Info()    => Variant(OrigamiVariant.Info);
    public RadioGroupBuilder<T> Subtle()  => Variant(OrigamiVariant.Subtle);

    // ── Layout ─────────────────────────────────────────────────────────

    public RadioGroupBuilder<T> Orientation(RadioGroupOrientation orientation) { _orientation = orientation; return this; }
    public RadioGroupBuilder<T> Vertical()   => Orientation(RadioGroupOrientation.Vertical);
    public RadioGroupBuilder<T> Horizontal() => Orientation(RadioGroupOrientation.Horizontal);
    public RadioGroupBuilder<T> Size(float size) { _size = MathF.Max(12, size); return this; }
    public RadioGroupBuilder<T> Gap(float pixels) { _gap = MathF.Max(0, pixels); return this; }

    // ── Item rendering ─────────────────────────────────────────────────

    public RadioGroupBuilder<T> Display(Func<T, string> display) { _display = display; return this; }
    public RadioGroupBuilder<T> Description(Func<T, string?> description) { _description = description; return this; }
    public RadioGroupBuilder<T> IsItemEnabled(Func<T, bool> isEnabled) { _isItemEnabled = isEnabled; return this; }
    public RadioGroupBuilder<T> Comparer(IEqualityComparer<T> comparer)
    {
        _comparer = comparer ?? EqualityComparer<T>.Default;
        return this;
    }

    // ── State ──────────────────────────────────────────────────────────

    public RadioGroupBuilder<T> Disabled(bool disabled = true) { _disabled = disabled; return this; }
    public RadioGroupBuilder<T> ReadOnly(bool readOnly = true) { _readOnly = readOnly; return this; }
    public RadioGroupBuilder<T> Error(string? message) { _error = message; return this; }
    public RadioGroupBuilder<T> HelperText(string? text) { _helperText = text; return this; }

    // ── Terminator ─────────────────────────────────────────────────────

    public void Show()
    {
        if (Origami.IsReadOnly) _disabled = true;
        var disp = _display ?? (t => t?.ToString() ?? string.Empty);
        bool hasError = !string.IsNullOrEmpty(_error);
        string? helpLine = hasError ? _error : _helperText;
        float helperH = !string.IsNullOrEmpty(helpLine) ? 16f : 0f;

        using (_paper.Column(_id).Width(UnitValue.Stretch()).Height(UnitValue.Auto).Enter())
        {
            // Group container — vertical stacks rows, horizontal flows them in a row.
            var container = _orientation == RadioGroupOrientation.Vertical
                ? _paper.Column($"{_id}_grp").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                : _paper.Row($"{_id}_grp").Width(UnitValue.Stretch()).Height(UnitValue.Auto);

            using (container.Enter())
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    var item = _items[i];
                    bool isSelected = _comparer.Equals(item, _value);
                    bool itemEnabled = _isItemEnabled?.Invoke(item) ?? true;
                    string label = disp(item);
                    string? desc = _description?.Invoke(item);

                    var capturedItem = item;
                    var radio = new ToggleBuilder(_paper, $"{_id}_r_{i}", isSelected,
                        v => { if (v) _setter(capturedItem); }, _theme)
                        .Variant(_variant)
                        .AsRadio()
                        .Size(_size)
                        .LabelRight(label);

                    if (!string.IsNullOrEmpty(desc)) radio.Description(desc);
                    if (_disabled || !itemEnabled) radio.Disabled(true);
                    if (_readOnly) radio.ReadOnly(true);

                    // Horizontal: small right-margin to space items; Vertical: small bottom gap.
                    radio.Show();

                    // Spacer between items (skip after last).
                    if (i < _items.Count - 1 && _gap > 0f)
                    {
                        if (_orientation == RadioGroupOrientation.Vertical)
                            _paper.Box($"{_id}_gap_{i}").Width(UnitValue.Stretch()).Height(_gap).IsNotInteractable();
                        else
                            _paper.Box($"{_id}_gap_{i}").Width(_gap).Height(_size).IsNotInteractable();
                    }
                }
            }

            // Group-level helper / error.
            if (helperH > 0f && _theme.Font != null)
            {
                Color color = hasError ? _theme.Red.C600 : _theme.Ink.C300;
                _paper.Box($"{_id}_help")
                    .Width(UnitValue.Stretch()).Height(helperH)
                    .Margin(2, 2, 4, 0)
                    .Alignment(TextAlignment.MiddleLeft)
                    .IsNotInteractable()
                    .Text(helpLine!, _theme.Font)
                    .TextColor(color)
                    .FontSize(_theme.Metrics.FontSize - 2);
            }
        }
    }
}
