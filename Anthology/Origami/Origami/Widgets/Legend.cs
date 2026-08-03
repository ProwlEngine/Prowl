// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>One row of a <see cref="LegendBuilder"/>: a swatch, a label, and an optional value string.
/// <c>Key</c> is an opaque caller-defined identifier echoed back by <see cref="LegendBuilder.OnToggle"/>
/// when the row is clicked; what it refers to (a series, a data item, a subtree, ...) is entirely up to
/// the caller.</summary>
public readonly struct LegendEntry
{
    public readonly string Label;
    public readonly Color Color;
    public readonly string? ValueText;
    public readonly int Key;
    public readonly bool Hidden;

    public LegendEntry(string label, Color color, int key, string? valueText = null, bool hidden = false)
    {
        Label = label ?? "";
        Color = color;
        ValueText = valueText;
        Key = key;
        Hidden = hidden;
    }
}

/// <summary>
/// Fluent builder for a standalone legend: a top-down list of colour swatches and labels. Construct via
/// <c>Origami.Legend</c>; chain modifiers; call <see cref="Show"/> to render. Used internally by every
/// Origami chart family for a single, consistent legend look, but takes no dependency on charts itself -
/// any widget with a list of coloured, nameable things can build its own <see cref="LegendEntry"/> list
/// and drop this in.
/// </summary>
/// <remarks>
/// This widget owns no state of its own: whether a row reads as hidden is entirely driven by
/// <see cref="LegendEntry.Hidden"/>, and a click only reports the row's key via <see cref="OnToggle"/>.
/// The caller decides what "hidden" means and where to store it.
/// </remarks>
public sealed class LegendBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private readonly IReadOnlyList<LegendEntry> _entries;

    private float _width = 125f;
    private float _padding;
    private float _swatchSize = 12f;
    private float _rowGap = 2f;
    private float? _fontSize;
    private bool _interactive;
    private Action<int>? _onToggle;

    internal LegendBuilder(Paper paper, string id, OrigamiTheme theme, IReadOnlyList<LegendEntry> entries)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _entries = entries ?? Array.Empty<LegendEntry>();
    }

    /// <summary>Width, in pixels, of the legend column. Defaults to 125.</summary>
    public LegendBuilder Width(float width) { _width = MathF.Max(1f, width); return this; }

    /// <summary>Top inset of the legend column, so its first row lines up with whatever sits beside it
    /// (a chart's plot area, say) rather than its own container's edge. Defaults to 0.</summary>
    public LegendBuilder Padding(float padding) { _padding = MathF.Max(0f, padding); return this; }

    /// <summary>Size, in pixels, of each row's colour swatch. Also sets the row height. Defaults to 12.</summary>
    public LegendBuilder SwatchSize(float size) { _swatchSize = MathF.Max(1f, size); return this; }

    /// <summary>Gap, in pixels, between rows. Defaults to 2.</summary>
    public LegendBuilder RowGap(float gap) { _rowGap = MathF.Max(0f, gap); return this; }

    /// <summary>Font size, in pixels, of the row labels. Unset uses the theme's XS size.</summary>
    public LegendBuilder FontSize(float size) { _fontSize = MathF.Max(1f, size); return this; }

    /// <summary>Let a click on a row's swatch report that row's key via <see cref="OnToggle"/>. Also gated
    /// by <see cref="Origami.LegendSelectionEnabled"/>, so a host can disable the interaction everywhere
    /// without every call site having to check it.</summary>
    public LegendBuilder Interactive(bool interactive = true) { _interactive = interactive; return this; }

    /// <summary>Called with a row's <see cref="LegendEntry.Key"/> when it is clicked, while
    /// <see cref="Interactive"/> is active. The legend itself stores no hidden state; the caller is
    /// responsible for flipping whatever it keys hidden-ness by and passing the updated
    /// <see cref="LegendEntry.Hidden"/> back in next frame.</summary>
    public LegendBuilder OnToggle(Action<int> handler) { _onToggle = handler; return this; }

    private bool InteractiveActive => _interactive && Origami.LegendSelectionEnabled;

    public void Show()
    {
        using (_paper.Column(_id).Width(_width).PaddingTop(_padding).ColBetween(_rowGap).Enter())
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                LegendEntry entry = _entries[i];
                int key = entry.Key;

                string text = entry.ValueText != null ? entry.Label + "  " + entry.ValueText : entry.Label;
                Color swatchColor = entry.Hidden ? Color.FromArgb(entry.Color.A / 3, entry.Color) : entry.Color;

                using (_paper.Row($"{_id}_row_{i}").Height(_swatchSize).RowBetween(2f).Enter())
                {
                    ElementBuilder swatch = _paper.Box($"{_id}_sw_{i}").Size(_swatchSize).BackgroundColor(swatchColor).Rounded(2f);

                    if (InteractiveActive)
                        swatch.OnClick(_ => _onToggle?.Invoke(key));

                    LabelBuilder label = Origami.Label(_paper, $"{_id}_txt_{i}", text);
                    if (_fontSize.HasValue) label.FontSize(_fontSize.Value); else label.XS();

                    label.AlignCenter().AlignLeft().Height(_swatchSize).Show();
                }
            }
        }
    }
}

public static partial class Origami
{
    /// <summary>A standalone legend: a top-down list of colour swatches, labels and optional values.
    /// Used internally by every chart family for a consistent look, and equally usable on its own for
    /// any custom graph or visualization.</summary>
    public static LegendBuilder Legend(Paper paper, string id, IReadOnlyList<LegendEntry> entries)
        => new(paper, id, Current, entries);
}
