// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// Fluent builder for an Origami single-select dropdown. Construct via
/// <see cref="Origami.Dropdown{T}(Paper,string,T,Action{T},IReadOnlyList{T})"/> and call
/// <see cref="Show"/> to render.
/// </summary>
/// <remarks>
/// <para>Controlled widget: the caller owns the value and passes a setter; Origami never
/// stores the selection. Click-outside, Escape and clicking an item all close the popover.
/// Up / Down move the keyboard cursor; Enter activates it.</para>
/// <para>Search, pagination, scrolling, custom rows and custom triggers are all opt-in.
/// The defaults render a tidy box with a chevron and a flat scrollable list.</para>
/// </remarks>
public sealed class DropdownBuilder<T>
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private readonly IReadOnlyList<T> _items;
    private readonly T _value;
    private readonly Action<T> _setter;

    private OrigamiVariant _variant = OrigamiVariant.Default;
    private bool _disabled;
    private Func<T, string>? _display;
    private Func<T, string>? _icon;
    private Func<T, string>? _secondary;
    private Func<T, bool>? _isEnabled;
    private Action<T, DropdownItemContext>? _itemRender;
    private Action<DropdownTriggerContext>? _customTrigger;
    private IEqualityComparer<T> _comparer = EqualityComparer<T>.Default;

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
    private float _height = 24f;

    internal DropdownBuilder(Paper paper, string id, T value, Action<T> setter,
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

    public DropdownBuilder<T> Variant(OrigamiVariant variant) { _variant = variant; return this; }
    public DropdownBuilder<T> Primary() => Variant(OrigamiVariant.Primary);
    public DropdownBuilder<T> Success() => Variant(OrigamiVariant.Success);
    public DropdownBuilder<T> Warning() => Variant(OrigamiVariant.Warning);
    public DropdownBuilder<T> Danger()  => Variant(OrigamiVariant.Danger);
    public DropdownBuilder<T> Info()    => Variant(OrigamiVariant.Info);
    public DropdownBuilder<T> Subtle()  => Variant(OrigamiVariant.Subtle);

    // ── Sizing ─────────────────────────────────────────────────────────

    /// <summary>Trigger width (default <see cref="UnitValue.Stretch()"/>).</summary>
    public DropdownBuilder<T> Width(UnitValue width) { _width = width; return this; }
    /// <summary>Trigger height in pixels (default 24).</summary>
    public DropdownBuilder<T> Height(float height) { _height = MathF.Max(16, height); return this; }
    /// <summary>Per-row height inside the popover (default 24).</summary>
    public DropdownBuilder<T> ItemHeight(float h) { _itemHeight = MathF.Max(16, h); return this; }
    /// <summary>Maximum popover height — the list scrolls if it would exceed this.</summary>
    public DropdownBuilder<T> MaxHeight(float h) { _maxHeight = MathF.Max(64, h); return this; }
    /// <summary>Override popover width — by default the popover matches the trigger.</summary>
    public DropdownBuilder<T> PopoverWidth(float w) { _popoverWidth = MathF.Max(80, w); return this; }

    // ── Item rendering ─────────────────────────────────────────────────

    /// <summary>How to render an item as a string for the trigger and default row label. Defaults to <c>ToString()</c>.</summary>
    public DropdownBuilder<T> Display(Func<T, string> display) { _display = display; return this; }
    /// <summary>Optional leading glyph for each row.</summary>
    public DropdownBuilder<T> Icon(Func<T, string> icon) { _icon = icon; return this; }
    /// <summary>Optional trailing muted text for each row.</summary>
    public DropdownBuilder<T> Secondary(Func<T, string> secondary) { _secondary = secondary; return this; }
    /// <summary>Per-item enable predicate. Disabled items are dimmed and not selectable.</summary>
    public DropdownBuilder<T> IsItemEnabled(Func<T, bool> isEnabled) { _isEnabled = isEnabled; return this; }
    /// <summary>Replace the entire row body. The row's click handling is still owned by Origami.</summary>
    public DropdownBuilder<T> ItemRender(Action<T, DropdownItemContext> render) { _itemRender = render; return this; }
    /// <summary>Replace the trigger contents. Origami still owns the trigger box (background, click).</summary>
    public DropdownBuilder<T> CustomTrigger(Action<DropdownTriggerContext> render) { _customTrigger = render; return this; }
    /// <summary>Override the equality used to mark which item is currently selected.</summary>
    public DropdownBuilder<T> Comparer(IEqualityComparer<T> comparer)
    {
        _comparer = comparer ?? EqualityComparer<T>.Default;
        return this;
    }

    // ── Behaviour ──────────────────────────────────────────────────────

    /// <summary>Enable the search bar at the top of the popover.</summary>
    public DropdownBuilder<T> Searchable(string placeholder = "Search...")
    {
        _searchable = true;
        _searchPlaceholder = placeholder ?? "Search...";
        return this;
    }

    /// <summary>Custom search predicate. <c>(item, searchText) => bool</c>. Defaults to case-insensitive substring on the display string.</summary>
    public DropdownBuilder<T> SearchFilter(Func<T, string, bool> filter) { _searchFilter = filter; return this; }

    /// <summary>Enable pagination with the given page size. Pagination and scroll work together: scroll within a page if the page is taller than <see cref="MaxHeight"/>.</summary>
    public DropdownBuilder<T> PageSize(int pageSize) { _pageSize = Math.Max(0, pageSize); return this; }

    /// <summary>Trigger placeholder when the current value isn't in the items list.</summary>
    public DropdownBuilder<T> Placeholder(string text) { _placeholder = text ?? string.Empty; return this; }

    /// <summary>Text shown inside the popover when filtering returns no items.</summary>
    public DropdownBuilder<T> EmptyText(string text) { _emptyText = text ?? string.Empty; return this; }

    // ── Terminator ─────────────────────────────────────────────────────

    /// <summary>Render the dropdown.</summary>
    public void Show()
    {
        if (Origami.IsReadOnly) _disabled = true;
        var ramp = _theme.Get(_variant);
        var ink = _theme.Ink;
        var font = _theme.Font;
        var icons = _theme.Icons;
        var disp = _display ?? (t => t?.ToString() ?? string.Empty);

        // Resolve current selection presentation.
        int selectedIdx = -1;
        for (int i = 0; i < _items.Count; i++)
        {
            if (_comparer.Equals(_items[i], _value)) { selectedIdx = i; break; }
        }
        bool isEmpty = selectedIdx < 0;
        string triggerText = isEmpty ? _placeholder : disp(_items[selectedIdx]);

        ElementHandle trigHandle = default;

        bool subtle = _variant == OrigamiVariant.Subtle;
        Color trigBg     = subtle ? Color.Transparent
                                  : (_variant == OrigamiVariant.Default ? _theme.Neutral.C200 : ramp.C200);
        Color trigBorder = subtle ? Color.Transparent
                                  : (_variant == OrigamiVariant.Default ? _theme.Neutral.C400 : ramp.C400);
        Color trigBorderHover = subtle ? _theme.Neutral.C400 : ramp.C500;
        Color chevColor  = _variant is OrigamiVariant.Default or OrigamiVariant.Subtle
                                  ? ink.C400 : ramp.C600;

        // Use Row so children flow left-to-right; per-child Margin (not ChildLeft/Right)
        // gives reliable label-stretches-chevron-on-right layout.
        var trigger = _paper.Row(_id)
            .Width(_width).Height(_height)
            .BackgroundColor(trigBg)
            .BorderColor(trigBorder).BorderWidth(1)
            .Hovered.BorderColor(trigBorderHover).End()
            .Rounded(_theme.Metrics.Rounding)
            .OnClick(e =>
            {
                if (_disabled) return;
                bool cur = _paper.GetElementStorage(trigHandle, DropdownInternal.KeyOpen, false);
                _paper.SetElementStorage(trigHandle, DropdownInternal.KeyOpen, !cur);
                _paper.SetElementStorage(trigHandle, DropdownInternal.KeyHighlight, selectedIdx);
            });

        using (trigger.Enter())
        {
            trigHandle = _paper.CurrentParent;
            bool isOpen = _paper.GetElementStorage(trigHandle, DropdownInternal.KeyOpen, false);
            isOpen = DropdownInternal.HandleCloseInteraction(_paper, trigHandle, isOpen);

            // Trigger contents
            if (_customTrigger != null)
            {
                var ctx = new DropdownTriggerContext(isOpen, triggerText, isEmpty, ramp, ink, _theme);
                _customTrigger(ctx);
            }
            else
            {
                if (font != null)
                {
                    // Label stretches to consume the row; chevron sits flush against the right edge.
                    var m = _theme.Metrics;
                    _paper.Box($"{_id}_lbl")
                        .Width(UnitValue.Stretch())
                        .Margin(m.SpacingLarge, m.PaddingSmall, 0, 0)
                        .Alignment(TextAlignment.MiddleLeft)
                        .IsNotInteractable()
                        .Text(triggerText, font)
                        .TextColor(isEmpty ? ink.C300 : ink.C500)
                        .FontSize(m.FontSize);

                    string chev = isOpen
                        ? (string.IsNullOrEmpty(icons.ChevronUp) ? "^" : icons.ChevronUp)
                        : (string.IsNullOrEmpty(icons.ChevronDown) ? "v" : icons.ChevronDown);
                    _paper.Box($"{_id}_chev")
                        .Width(m.IconWidth)
                        .Margin(0, m.Padding, 0, 0)
                        .Alignment(TextAlignment.MiddleCenter)
                        .IsNotInteractable()
                        .Text(chev, font).TextColor(chevColor).FontSize(m.FontSize * 0.85f);
                }
            }

            if (isOpen)
            {
                // Modal backdrop dims everything behind the popover and catches outside clicks.
                DropdownInternal.RenderBackdrop(_paper, $"{_id}_bd", trigHandle, dim: true);

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
                    IsSelected = item => _comparer.Equals(item, _value),
                    OnItemClick = (idx, item) => _setter(item),
                    CustomItemRender = _itemRender,
                    ShowCheckboxes = false,
                    CloseOnSelect = true,
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
}
