// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// A compact toolbar of square icon buttons (Nebula .ftool / .ftb): the active button is accent-filled
/// with a soft glow, others show a hover state. Lays out horizontally or vertically and can wrap itself
/// in its own glass container. Build via <see cref="Origami.IconToolbar"/>, add buttons with
/// <see cref="Item"/>, then call <see cref="Show"/>.
/// </summary>
public sealed class IconToolbarBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private readonly int _selected;
    private readonly Action<int> _setter;
    private readonly List<(IOrigamiIcon icon, string? tooltip)> _items = new();

    private bool _vertical;
    private bool _container = true;
    private bool _center;
    private float _button = 30f;

    internal IconToolbarBuilder(Paper paper, string id, int selected, Action<int> setter, OrigamiTheme theme)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _setter = setter ?? throw new ArgumentNullException(nameof(setter));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _selected = selected;
    }

    /// <summary>Append a button with a vector icon and optional hover tooltip.</summary>
    public IconToolbarBuilder Item(IOrigamiIcon icon, string? tooltip = null)
    {
        _items.Add((icon, tooltip));
        return this;
    }

    /// <summary>Stack the buttons vertically (e.g. a left rail) instead of a horizontal row.</summary>
    public IconToolbarBuilder Vertical(bool vertical = true) { _vertical = vertical; return this; }
    public IconToolbarBuilder Horizontal() { _vertical = false; return this; }

    /// <summary>Wrap the buttons in a glass container with a soft border (default true).</summary>
    public IconToolbarBuilder Container(bool container = true) { _container = container; return this; }

    /// <summary>Center the toolbar horizontally within the available width (horizontal layout only).</summary>
    public IconToolbarBuilder Center(bool center = true) { _center = center; return this; }

    /// <summary>Square button size in pixels (default 30).</summary>
    public IconToolbarBuilder ButtonSize(float px) { _button = MathF.Max(18f, px); return this; }

    public void Show()
    {
        if (_items.Count == 0) return;

        var ink = _theme.Ink;
        var acc = _theme.Primary.C500;
        float btn = _button;
        const float gap = 3f;
        float pad = _container ? 4f : 0f;

        IDisposable? centerScope = null;
        if (_center && !_vertical)
            centerScope = _paper.Row($"{_id}_center").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Enter();

        ElementBuilder bar = _vertical ? _paper.Column(_id) : _paper.Row(_id);
        bar.Width(UnitValue.Auto).Height(UnitValue.Auto);
        if (_vertical) bar.ColBetween(gap); else bar.RowBetween(gap);
        if (_center && !_vertical) bar.Margin(UnitValue.Stretch(), UnitValue.Stretch(), 0, 0);
        if (_container)
            bar.Rounded(10f).Padding(pad, pad, pad, pad)
               .BackgroundColor(_theme.Glass).BorderColor(_theme.BorderSoft).BorderWidth(1);

        using (bar.Enter())
        {
            for (int i = 0; i < _items.Count; i++)
            {
                int idx = i;
                bool on = i == _selected;
                var (icon, tooltip) = _items[i];

                var b = _paper.Box($"{_id}_b{i}").Width(btn).Height(btn).Rounded(7f)
                    .OnClick(_ => _setter(idx));

                if (on)
                    b.BackgroundColor(acc).BoxShadow(0, 2, 12, -2, Color.FromArgb(160, acc.R, acc.G, acc.B));
                else
                    b.Hovered.BackgroundColor(_theme.Hover).End();

                if (!string.IsNullOrEmpty(tooltip))
                    b.Tooltip(tooltip);

                b.Icon(_paper, icon, on ? Color.White : ink.C400, size: btn * 0.5f);
            }
        }

        centerScope?.Dispose();
    }
}
