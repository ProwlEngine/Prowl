// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// Fluent builder for an Origami multi-select dropdown. Construct via
/// <see cref="Origami.MultiDropdown{T}(Paper,string,IEnumerable{T},Action{IReadOnlyList{T}},IReadOnlyList{T})"/>.
/// </summary>
/// <remarks>
/// <para>Each row carries a checkbox; clicking toggles inclusion in the selection set
/// without closing the popover. The setter receives a fresh <see cref="IReadOnlyList{T}"/>
/// after every toggle. Use this for set-style fields such as layer masks, tag filters,
/// permissions or feature flags.</para>
/// </remarks>
public sealed class MultiDropdownBuilder<T>
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private readonly IReadOnlyList<T> _items;
    private readonly HashSet<T> _selected;
    private readonly Action<IReadOnlyList<T>> _setter;

    private OrigamiVariant _variant = OrigamiVariant.Default;
    private bool _disabled;
    private Func<T, string>? _display;
    private Func<T, string>? _icon;
    private Func<T, string>? _secondary;
    private Func<T, bool>? _isEnabled;
    private Action<T, DropdownItemContext>? _itemRender;
    private Action<DropdownTriggerContext>? _customTrigger;

    private bool _searchable;
    private string _searchPlaceholder = "Search...";
    private Func<T, string, bool>? _searchFilter;
    private int _pageSize;
    private float _maxHeight = 320f;
    private float? _popoverWidth;
    private string _placeholder = "Select...";
    private string _emptyText = "No results";
    private float _itemHeight = 24f;
    private UnitValue _width = UnitValue.Stretch();
    private float _height = 32f;
    private int _summaryItemLimit = 2;
    private string _summaryFormat = "{0} selected";

    internal MultiDropdownBuilder(Paper paper, string id, IEnumerable<T> selected,
        Action<IReadOnlyList<T>> setter, IReadOnlyList<T> items, OrigamiTheme theme,
        IEqualityComparer<T>? comparer = null)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _setter = setter ?? throw new ArgumentNullException(nameof(setter));
        _items = items ?? throw new ArgumentNullException(nameof(items));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _height = _theme.Metrics.RowHeight;
        _selected = selected != null
            ? new HashSet<T>(selected, comparer ?? EqualityComparer<T>.Default)
            : new HashSet<T>(comparer ?? EqualityComparer<T>.Default);
    }

    // ── Variant ────────────────────────────────────────────────────────

    public MultiDropdownBuilder<T> Variant(OrigamiVariant variant) { _variant = variant; return this; }
    public MultiDropdownBuilder<T> Primary() => Variant(OrigamiVariant.Primary);
    public MultiDropdownBuilder<T> Success() => Variant(OrigamiVariant.Success);
    public MultiDropdownBuilder<T> Warning() => Variant(OrigamiVariant.Warning);
    public MultiDropdownBuilder<T> Danger()  => Variant(OrigamiVariant.Danger);
    public MultiDropdownBuilder<T> Info()    => Variant(OrigamiVariant.Info);
    public MultiDropdownBuilder<T> Subtle()  => Variant(OrigamiVariant.Subtle);

    // ── Sizing ─────────────────────────────────────────────────────────

    public MultiDropdownBuilder<T> Width(UnitValue width) { _width = width; return this; }
    public MultiDropdownBuilder<T> Height(float height) { _height = MathF.Max(16, height); return this; }
    public MultiDropdownBuilder<T> ItemHeight(float h) { _itemHeight = MathF.Max(16, h); return this; }
    public MultiDropdownBuilder<T> MaxHeight(float h) { _maxHeight = MathF.Max(64, h); return this; }
    public MultiDropdownBuilder<T> PopoverWidth(float w) { _popoverWidth = MathF.Max(80, w); return this; }

    // ── Item rendering ─────────────────────────────────────────────────

    public MultiDropdownBuilder<T> Display(Func<T, string> display) { _display = display; return this; }
    public MultiDropdownBuilder<T> Icon(Func<T, string> icon) { _icon = icon; return this; }
    public MultiDropdownBuilder<T> Secondary(Func<T, string> secondary) { _secondary = secondary; return this; }
    public MultiDropdownBuilder<T> IsItemEnabled(Func<T, bool> isEnabled) { _isEnabled = isEnabled; return this; }
    public MultiDropdownBuilder<T> ItemRender(Action<T, DropdownItemContext> render) { _itemRender = render; return this; }
    public MultiDropdownBuilder<T> CustomTrigger(Action<DropdownTriggerContext> render) { _customTrigger = render; return this; }

    // ── Behaviour ──────────────────────────────────────────────────────

    public MultiDropdownBuilder<T> Searchable(string placeholder = "Search...")
    {
        _searchable = true;
        _searchPlaceholder = placeholder ?? "Search...";
        return this;
    }
    public MultiDropdownBuilder<T> SearchFilter(Func<T, string, bool> filter) { _searchFilter = filter; return this; }
    public MultiDropdownBuilder<T> PageSize(int pageSize) { _pageSize = Math.Max(0, pageSize); return this; }
    public MultiDropdownBuilder<T> Placeholder(string text) { _placeholder = text ?? string.Empty; return this; }
    public MultiDropdownBuilder<T> EmptyText(string text) { _emptyText = text ?? string.Empty; return this; }

    /// <summary>
    /// Up to this many selections are listed by name in the trigger; beyond that the trigger
    /// falls back to the count summary. Defaults to 2.
    /// </summary>
    public MultiDropdownBuilder<T> SummaryItemLimit(int limit) { _summaryItemLimit = Math.Max(0, limit); return this; }

    /// <summary>
    /// Format string for the count summary, e.g. <c>"{0} layers"</c>. <c>{0}</c> is the count.
    /// </summary>
    public MultiDropdownBuilder<T> SummaryFormat(string format) { _summaryFormat = format ?? "{0} selected"; return this; }

    // ── Terminator ─────────────────────────────────────────────────────

    public void Show()
    {
        if (Origami.IsReadOnly) _disabled = true;
        var ramp = _theme.Get(_variant);
        var ink = _theme.Ink;
        var font = _theme.Font;
        var icons = _theme.Icons;
        var disp = _display ?? (t => t?.ToString() ?? string.Empty);

        bool isEmpty = _selected.Count == 0;
        string triggerText = BuildSummary(disp, isEmpty);

        ElementHandle trigHandle = default;

        bool subtle = _variant == OrigamiVariant.Subtle;
        Color trigBg     = subtle ? Color.Transparent : _theme.Glass; // glass-in field
        Color trigBorder = subtle ? Color.Transparent : _theme.BorderSoft;
        Color trigBorderHover = _theme.BorderStrong;
        // Flex-wrap field (prototype .w2field: height auto, minHeight 32, padding 4, gap 4).
        var trigger = _paper.Row(_id)
            .Width(_width).Height(UnitValue.Auto).MinHeight(_height)
            .WrapContent().RowBetween(4).Padding(4, 4, 4, 4)
            .BackgroundColor(trigBg)
            .BorderColor(trigBorder).BorderWidth(1)
            .Hovered.BorderColor(trigBorderHover).End()
            .Rounded(_theme.Metrics.Rounding)
            .OnClick(e =>
            {
                bool cur = _paper.GetElementStorage(trigHandle, DropdownInternal.KeyOpen, false);
                _paper.SetElementStorage(trigHandle, DropdownInternal.KeyOpen, !cur);
            });

        using (trigger.Enter())
        {
            trigHandle = _paper.CurrentParent;
            bool isOpen = _paper.GetElementStorage(trigHandle, DropdownInternal.KeyOpen, false);
            isOpen = DropdownInternal.HandleCloseInteraction(_paper, trigHandle, isOpen);

            if (_customTrigger != null)
            {
                var ctx = new DropdownTriggerContext(isOpen, triggerText, isEmpty, ramp, ink, _theme);
                _customTrigger(ctx);
            }
            else if (font != null)
            {
                // Selected values render as removable chips (prototype .w2chip) that wrap onto new
                // lines as they fill; the field grows in height to fit every line.
                if (!isEmpty)
                {
                    int ci = 0;
                    foreach (var item in _selected)
                    {
                        var captured = item;
                        MultiChip($"{_id}_chip_{ci}", disp(item), () => { _selected.Remove(captured); _setter(_selected.ToList()); });
                        ci++;
                    }
                }

                // Trailing add affordance (prototype's <input placeholder="Add layer…">). Clicking
                // anywhere on the field opens the popover, so this is just the labelled hint.
                _paper.Box($"{_id}_add")
                    .Width(UnitValue.Auto).MinWidth(70).Height(22)
                    .Margin(4, 4, 0, 0)
                    .Alignment(TextAlignment.MiddleLeft)
                    .IsNotInteractable()
                    .Text(_placeholder, font)
                    .TextColor(ink.C300)
                    .FontSize(_theme.Metrics.FontSize - 0.5f);
            }

            if (isOpen)
            {
                DropdownInternal.RenderBackdrop(_paper, $"{_id}_bd", trigHandle, dim: false);

                var p = new DropdownInternal.PopoverParams<T>
                {
                    Paper = _paper,
                    Id = _id,
                    Theme = _theme,
                    Variant = _variant,
                    Items = _items,
                    Display = disp,
                    Icon = _icon,
                    Secondary = _secondary,
                    IsEnabled = _isEnabled,
                    IsSelected = item => _selected.Contains(item),
                    OnItemClick = (idx, item) =>
                    {
                        if (_selected.Contains(item)) _selected.Remove(item);
                        else _selected.Add(item);
                        _setter(_selected.ToList());
                    },
                    CustomItemRender = _itemRender,
                    ShowCheckboxes = true,
                    CloseOnSelect = false,
                    Searchable = _searchable,
                    SearchPlaceholder = _searchPlaceholder,
                    SearchFilter = _searchFilter,
                    PageSize = _pageSize,
                    MaxHeight = _maxHeight,
                    ItemHeight = _itemHeight,
                    EmptyText = _emptyText,
                    TriggerHandle = trigHandle,
                    TriggerWidth = trigHandle.Data.LayoutRect.Size.X > 0
                        ? (float)trigHandle.Data.LayoutRect.Size.X
                        : 200f,
                    PopoverWidth = _popoverWidth,
                    TriggerHeight = _height,
                };
                DropdownInternal.RenderPopover(p);
            }
        }
    }

    // A removable selection chip inside the trigger (prototype .w2chip: accent-tint fill + rim + x).
    private void MultiChip(string id, string label, Action onRemove)
    {
        var font = _theme.Font;
        Color bg = _theme.Selected;   // acc-dim (0.16)
        Color bd = Color.FromArgb(115, _theme.Primary.C500.R, _theme.Primary.C500.G, _theme.Primary.C500.B);  // ~0.45 — reads brighter, matches prototype
        Color tx = _theme.Primary.C700;                 // acc-300

        using (_paper.Row(id).Width(UnitValue.Auto).Height(22)
            .Padding(9, 3, 0, 0).Rounded(6)
            .BackgroundColor(bg).BorderColor(bd).BorderWidth(1)
            .Enter())
        {
            _paper.Box($"{id}_l").Width(UnitValue.Auto).Height(UnitValue.Auto)
                .Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
                .IsNotInteractable()
                .Text(label, font!).TextColor(tx).FontSize(_theme.Metrics.FontSize - 2f).Alignment(TextAlignment.MiddleLeft);

            var rm = onRemove;
            var xcol = tx;
            using (_paper.Box($"{id}_x").Size(15).Rounded(4).Margin(3, 0, UnitValue.Stretch(), UnitValue.Stretch())
                .Hovered.BackgroundColor(Color.FromArgb(64, _theme.Primary.C500.R, _theme.Primary.C500.G, _theme.Primary.C500.B)).End()
                .StopEventPropagation().OnClick(_ => rm())
                .Enter())
            {
                _paper.Draw((canvas, r) =>
                {
                    float cx = (float)(r.Min.X + r.Size.X / 2), cy = (float)(r.Min.Y + r.Size.Y / 2);
                    canvas.SaveState();
                    canvas.SetStrokeColor(xcol);
                    canvas.SetStrokeWidth(1.1f);
                    canvas.SetStrokeCap(EndCapStyle.Round);
                    canvas.BeginPath();
                    canvas.MoveTo(cx - 2.2f, cy - 2.2f); canvas.LineTo(cx + 2.2f, cy + 2.2f);
                    canvas.MoveTo(cx + 2.2f, cy - 2.2f); canvas.LineTo(cx - 2.2f, cy + 2.2f);
                    canvas.Stroke();
                    canvas.RestoreState();
                });
            }
        }
    }

    private string BuildSummary(Func<T, string> disp, bool isEmpty)
    {
        if (isEmpty) return _placeholder;

        // Names listed in items-list order so the trigger stays stable as the user toggles.
        if (_selected.Count <= _summaryItemLimit)
        {
            var sb = new StringBuilder();
            int written = 0;
            foreach (var item in _items)
            {
                if (!_selected.Contains(item)) continue;
                if (written++ > 0) sb.Append(", ");
                sb.Append(disp(item));
            }
            return sb.ToString();
        }

        return string.Format(_summaryFormat, _selected.Count);
    }
}
