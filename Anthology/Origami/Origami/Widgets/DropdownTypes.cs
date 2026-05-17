// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Drawing;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.OrigamiUI;

/// <summary>
/// Per-item state passed to a custom item renderer. Lets the caller branch on
/// selection / hover / disabled / highlight without recomputing it themselves.
/// </summary>
public readonly struct DropdownItemContext
{
    /// <summary>Position of this item in the (filtered) list.</summary>
    public readonly int Index;
    /// <summary>True when the item is the currently selected value (or one of, in multi-select).</summary>
    public readonly bool IsSelected;
    /// <summary>True when the keyboard cursor is parked on this item.</summary>
    public readonly bool IsHighlighted;
    /// <summary>True when the user has explicitly disabled this item via <c>IsItemEnabled</c>.</summary>
    public readonly bool IsDisabled;
    /// <summary>The configured row height. Custom renderers should respect this for layout consistency.</summary>
    public readonly float RowHeight;
    /// <summary>The active surface ramp for the dropdown's variant.</summary>
    public readonly OrigamiRamp Surface;
    /// <summary>The active ink ramp.</summary>
    public readonly OrigamiRamp Ink;
    /// <summary>The active theme — full access if a renderer needs metrics, font, etc.</summary>
    public readonly OrigamiTheme Theme;

    internal DropdownItemContext(int index, bool isSelected, bool isHighlighted, bool isDisabled,
        float rowHeight, OrigamiRamp surface, OrigamiRamp ink, OrigamiTheme theme)
    {
        Index = index;
        IsSelected = isSelected;
        IsHighlighted = isHighlighted;
        IsDisabled = isDisabled;
        RowHeight = rowHeight;
        Surface = surface;
        Ink = ink;
        Theme = theme;
    }
}

/// <summary>
/// Context passed to a custom trigger renderer. The trigger element itself (background,
/// click handling) is owned by Origami; the callback draws inside it.
/// </summary>
public readonly struct DropdownTriggerContext
{
    /// <summary>True while the popover is open.</summary>
    public readonly bool IsOpen;
    /// <summary>The text Origami would have shown ("Select..." placeholder, single value's display, or multi summary like "3 selected").</summary>
    public readonly string DisplayText;
    /// <summary>True when no value (single) / no values (multi) is selected.</summary>
    public readonly bool IsEmpty;
    /// <summary>Active surface ramp.</summary>
    public readonly OrigamiRamp Surface;
    /// <summary>Active ink ramp.</summary>
    public readonly OrigamiRamp Ink;
    /// <summary>Active theme.</summary>
    public readonly OrigamiTheme Theme;

    internal DropdownTriggerContext(bool isOpen, string displayText, bool isEmpty,
        OrigamiRamp surface, OrigamiRamp ink, OrigamiTheme theme)
    {
        IsOpen = isOpen;
        DisplayText = displayText;
        IsEmpty = isEmpty;
        Surface = surface;
        Ink = ink;
        Theme = theme;
    }
}

/// <summary>
/// Shared rendering pipeline used by both <see cref="DropdownBuilder{T}"/> (single) and
/// <see cref="MultiDropdownBuilder{T}"/>. Centralises the trigger box, popover frame,
/// search bar, list/pagination, keyboard nav, click-outside detection — everything that's
/// the same regardless of how rows are clicked / what selection looks like.
/// </summary>
internal static class DropdownInternal
{
    // Storage keys on the trigger handle (centralised so Single/Multi don't fight for slots).
    internal const string KeyOpen        = "drop_open";
    internal const string KeySearch      = "drop_search";
    internal const string KeyHighlight   = "drop_hl";
    internal const string KeyPage        = "drop_page";

    /// <summary>
    /// Handles Esc to close. Click-outside is handled separately by the modal backdrop
    /// rendered via <see cref="RenderBackdrop"/>; this keeps the trigger logic small.
    /// </summary>
    internal static bool HandleCloseInteraction(Paper paper, ElementHandle trigHandle, bool isOpen)
    {
        if (!isOpen) return false;

        if (paper.IsKeyPressed(PaperKey.Escape))
        {
            paper.SetElementStorage(trigHandle, KeyOpen, false);
            return false;
        }
        return true;
    }

    /// <summary>
    /// Full-screen backdrop drawn when a dropdown is open. Sits on
    /// <see cref="Layer.Overlay"/> so the popover (which uses <see cref="Layer.Topmost"/>)
    /// stays interactive above it. Clicking the backdrop closes the dropdown; events are
    /// stopped from propagating so scrolls / clicks behind don't leak.
    /// </summary>
    internal static void RenderBackdrop(Paper paper, string id, ElementHandle trigHandle, bool dim)
    {
        var capturedTrig = trigHandle;
        var box = paper.Box(id)
            .PositionType(PositionType.SelfDirected)
            .Position(-9999, -9999)
            .Size(99999, 99999)
            .Layer(Layer.Overlay)
            .OnClick(_ => paper.SetElementStorage(capturedTrig, KeyOpen, false));

        if (dim)
            box.BackgroundColor(System.Drawing.Color.FromArgb(80, 0, 0, 0));

        box.StopEventPropagation();
    }

    /// <summary>
    /// Parameters for <see cref="RenderPopover"/>. Bundled so the two builders don't have to
    /// pass twenty positional arguments. Reference type so internal mutations
    /// (e.g. resolving search text default) don't surprise callers.
    /// </summary>
    internal sealed class PopoverParams<T>
    {
        public Paper Paper = null!;
        public string Id = null!;
        public OrigamiTheme Theme = null!;
        public OrigamiVariant Variant;
        public System.Collections.Generic.IReadOnlyList<T> Items = null!;
        public Func<T, string> Display = null!;
        public Func<T, string>? Icon;
        public Func<T, string>? Secondary;
        public Func<T, bool>? IsEnabled;
        public Func<T, bool> IsSelected = null!;
        public Action<int, T> OnItemClick = null!;
        public Action<T, DropdownItemContext>? CustomItemRender;
        public bool ShowCheckboxes;
        public bool CloseOnSelect = true;
        public bool Searchable;
        public string SearchPlaceholder = "Search...";
        public Func<T, string, bool>? SearchFilter;
        public int PageSize;
        public float MaxHeight = 320f;
        public float ItemHeight = 24f;
        public string EmptyText = "No results";

        public ElementHandle TriggerHandle;
        public float TriggerWidth;
        public float? PopoverWidth;
        public float TriggerHeight;
    }

    /// <summary>Default "is this item visible for the current search text?" predicate.</summary>
    internal static bool DefaultMatch<T>(T item, string search, Func<T, string> display)
    {
        if (string.IsNullOrEmpty(search)) return true;
        var s = display(item) ?? string.Empty;
        return s.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Renders the popover and all its content. Caller supplies <see cref="PopoverParams{T}"/>
    /// and is responsible for already having opened a layout scope where the popover should
    /// hook into (typically inside the trigger element's <c>using</c> block).
    /// </summary>
    internal static void RenderPopover<T>(PopoverParams<T> p)
    {
        var paper = p.Paper;
        var ramp = p.Theme.Get(p.Variant);
        var ink = p.Theme.Ink;
        var font = p.Theme.Font;
        var icons = p.Theme.Icons;

        // Resolve filtered list once per frame.
        string searchText = p.Searchable ? paper.GetElementStorage(p.TriggerHandle, KeySearch, string.Empty) : string.Empty;
        var filtered = new System.Collections.Generic.List<int>(p.Items.Count);
        for (int i = 0; i < p.Items.Count; i++)
        {
            if (p.Searchable && !string.IsNullOrEmpty(searchText))
            {
                bool match = p.SearchFilter != null
                    ? p.SearchFilter(p.Items[i], searchText)
                    : DefaultMatch(p.Items[i], searchText, p.Display);
                if (!match) continue;
            }
            filtered.Add(i);
        }

        // Pagination
        int pageCount = 1;
        int pageStart = 0;
        int pageEnd = filtered.Count;
        int pageIdx = paper.GetElementStorage(p.TriggerHandle, KeyPage, 0);
        if (p.PageSize > 0 && filtered.Count > p.PageSize)
        {
            pageCount = (filtered.Count + p.PageSize - 1) / p.PageSize;
            if (pageIdx >= pageCount) pageIdx = pageCount - 1;
            if (pageIdx < 0) pageIdx = 0;
            paper.SetElementStorage(p.TriggerHandle, KeyPage, pageIdx);
            pageStart = pageIdx * p.PageSize;
            pageEnd = Math.Min(filtered.Count, pageStart + p.PageSize);
        }

        // Keyboard nav: highlight cursor (in filtered-list index space).
        int hl = paper.GetElementStorage(p.TriggerHandle, KeyHighlight, -1);
        if (hl >= filtered.Count) hl = filtered.Count - 1;

        if (paper.IsKeyPressedOrRepeating(PaperKey.Down))
        {
            hl = filtered.Count == 0 ? -1 : (hl + 1) % filtered.Count;
            paper.SetElementStorage(p.TriggerHandle, KeyHighlight, hl);
            // Page-follow: jump to the page containing the highlighted item so it's visible.
            if (p.PageSize > 0 && hl >= 0)
            {
                int newPage = hl / p.PageSize;
                if (newPage != pageIdx)
                {
                    paper.SetElementStorage(p.TriggerHandle, KeyPage, newPage);
                    pageIdx = newPage;
                    pageStart = pageIdx * p.PageSize;
                    pageEnd = Math.Min(filtered.Count, pageStart + p.PageSize);
                }
            }
        }
        else if (paper.IsKeyPressedOrRepeating(PaperKey.Up))
        {
            hl = filtered.Count == 0 ? -1 : (hl <= 0 ? filtered.Count - 1 : hl - 1);
            paper.SetElementStorage(p.TriggerHandle, KeyHighlight, hl);
            if (p.PageSize > 0 && hl >= 0)
            {
                int newPage = hl / p.PageSize;
                if (newPage != pageIdx)
                {
                    paper.SetElementStorage(p.TriggerHandle, KeyPage, newPage);
                    pageIdx = newPage;
                    pageStart = pageIdx * p.PageSize;
                    pageEnd = Math.Min(filtered.Count, pageStart + p.PageSize);
                }
            }
        }
        else if (paper.IsKeyPressed(PaperKey.Enter) && hl >= 0 && hl < filtered.Count)
        {
            int realIdx = filtered[hl];
            bool enabled = p.IsEnabled == null || p.IsEnabled(p.Items[realIdx]);
            if (enabled)
            {
                p.OnItemClick(realIdx, p.Items[realIdx]);
                if (p.CloseOnSelect) paper.SetElementStorage(p.TriggerHandle, KeyOpen, false);
            }
        }

        // Always render below the trigger; ClampToScreen on the popover handles edges.
        float popY = p.TriggerHeight - 1f;
        float popoverWidth = p.PopoverWidth ?? p.TriggerWidth;

        // Layout sizing.
        float padX = 4f;
        float padY = 4f;
        float searchH = p.Searchable ? 24f : 0f;
        float searchGap = p.Searchable ? 4f : 0f;
        float paginationH = p.PageSize > 0 && pageCount > 1 ? 24f : 0f;
        float paginationGap = paginationH > 0f ? 4f : 0f;

        int visibleCount = pageEnd - pageStart;
        if (visibleCount == 0) visibleCount = 1; // reserve a row for the empty-state line
        float listMax = MathF.Max(p.ItemHeight, p.MaxHeight - padY * 2 - searchH - searchGap - paginationH - paginationGap);
        float listH = MathF.Min(listMax, visibleCount * p.ItemHeight);
        float popH = padY * 2 + searchH + searchGap + listH + paginationH + paginationGap;

        Color popBorder = p.Variant is OrigamiVariant.Default or OrigamiVariant.Subtle
            ? p.Theme.Neutral.C400 : ramp.C400;
        // StopEventPropagation: the popover is logically a child of the trigger element, so
        // without this any click inside (search bar, page button, item row) bubbles up to the
        // trigger and re-toggles the open state — closing the dropdown unintentionally.
        var popBuilder = paper.Column($"{p.Id}_pop")
            .PositionType(PositionType.SelfDirected)
            .Position(0, popY)
            .Width(popoverWidth)
            .Height(popH)
            .BackgroundColor(p.Theme.Neutral.C200)
            .BorderColor(popBorder).BorderWidth(1)
            .Rounded(p.Theme.Metrics.Rounding)
            .Padding(padX, padX, padY, padY)
            .ColBetween(searchGap)
            .HookToParent()
            .Layer(Layer.Topmost)
            .ClampToScreen()
            .StopEventPropagation();

        using (popBuilder.Enter())
        {
            // Search bar
            if (p.Searchable && font != null)
            {
                using (paper.Row($"{p.Id}_search")
                    .Height(searchH)
                    .BackgroundColor(p.Theme.Neutral.C100)
                    .BorderColor(p.Theme.Neutral.C400).BorderWidth(1)
                    .Focused.BorderColor(ramp.C500).End()
                    .Rounded(3)
                    .ChildLeft(6).ChildRight(4)
                    .RowBetween(4)
                    .TabIndex(0)
                    .Enter())
                {
                    if (!string.IsNullOrEmpty(icons.Search))
                    {
                        paper.Box($"{p.Id}_search_ic")
                            .Width(14)
                            .Alignment(TextAlignment.MiddleLeft)
                            .Text(icons.Search, font).TextColor(ink.C300).FontSize(p.Theme.Metrics.FontSize * 0.85f);
                    }

                    paper.Box($"{p.Id}_search_tf")
                        .Height(searchH).Width(UnitValue.Stretch())
                        .HookToParent()
                        .IsNotInteractable()
                        .FontSize(p.Theme.Metrics.FontSize)
                        .Alignment(TextAlignment.MiddleLeft)
                        .TextField(searchText, font,
                            onChange: v =>
                            {
                                paper.SetElementStorage(p.TriggerHandle, KeySearch, v ?? string.Empty);
                                paper.SetElementStorage(p.TriggerHandle, KeyHighlight, 0);
                                paper.SetElementStorage(p.TriggerHandle, KeyPage, 0);
                            },
                            textColor: ink.C500,
                            placeholder: p.SearchPlaceholder,
                            placeholderColor: ink.C300,
                            intID: (p.Id + "_search").GetHashCode());

                    if (!string.IsNullOrEmpty(searchText) && !string.IsNullOrEmpty(icons.Close))
                    {
                        paper.Box($"{p.Id}_search_clr")
                            .Rounded(8).Size(16)
                            .Alignment(TextAlignment.MiddleCenter)
                            .Hovered.BackgroundColor(p.Theme.Neutral.C300).End()
                            .Text(icons.Close, font).TextColor(ink.C400).FontSize(p.Theme.Metrics.FontSize)
                            .OnClick(e =>
                            {
                                paper.SetElementStorage(p.TriggerHandle, KeySearch, string.Empty);
                                paper.SetElementStorage(p.TriggerHandle, KeyHighlight, 0);
                                paper.SetElementStorage(p.TriggerHandle, KeyPage, 0);
                            });
                    }
                }
            }

            // List
            if (filtered.Count == 0)
            {
                paper.Box($"{p.Id}_empty")
                    .Height(listH)
                    .Alignment(TextAlignment.MiddleCenter)
                    .Text(p.EmptyText, font).TextColor(ink.C300).FontSize(p.Theme.Metrics.FontSize);
            }
            else
            {
                // Use Origami.ScrollView so theme/transition behaviour is consistent.
                Origami.ScrollView(paper, $"{p.Id}_list", popoverWidth - padX * 2, listH)
                    .Padding(0)
                    .Body(() =>
                    {
                        for (int li = pageStart; li < pageEnd; li++)
                        {
                            int realIdx = filtered[li];
                            T item = p.Items[realIdx];
                            bool selected = p.IsSelected(item);
                            bool highlighted = li == hl;
                            bool enabled = p.IsEnabled == null || p.IsEnabled(item);

                            var rowCtx = new DropdownItemContext(li, selected, highlighted, !enabled,
                                p.ItemHeight, ramp, ink, p.Theme);

                            // Row background colors: selected > highlighted > hover > none.
                            // Subtle variant stays neutral; everything else paints with the variant ramp.
                            bool useRamp = p.Variant != OrigamiVariant.Subtle;
                            Color selRampBg  = useRamp ? ramp.C400 : p.Theme.Neutral.C400;
                            Color hlRampBg   = useRamp ? ramp.C300 : p.Theme.Neutral.C300;
                            Color hoverRampBg= useRamp ? ramp.C500 : p.Theme.Neutral.C400;
                            Color rowBg = Color.Transparent;
                            if (selected) rowBg = selRampBg;
                            else if (highlighted) rowBg = hlRampBg;

                            int capturedReal = realIdx;
                            T capturedItem = item;
                            var row = paper.Row($"{p.Id}_r_{realIdx}")
                                .Height(p.ItemHeight)
                                .BackgroundColor(rowBg)
                                .Hovered.BackgroundColor(enabled ? hoverRampBg : rowBg).End()
                                .Rounded(2)
                                .ChildLeft(6).ChildRight(6)
                                .RowBetween(6)
                                .OnClick(e =>
                                {
                                    if (!enabled) return;
                                    p.OnItemClick(capturedReal, capturedItem);
                                    if (p.CloseOnSelect) paper.SetElementStorage(p.TriggerHandle, KeyOpen, false);
                                });

                            using (row.Enter())
                            {
                                if (p.CustomItemRender != null)
                                {
                                    p.CustomItemRender(item, rowCtx);
                                    continue;
                                }

                                // Default row layout: [check]? [icon]? label [secondary]?
                                if (p.ShowCheckboxes && font != null)
                                {
                                    string box = selected
                                        ? (string.IsNullOrEmpty(icons.CheckboxOn) ? icons.Check : icons.CheckboxOn)
                                        : icons.CheckboxOff;
                                    paper.Box($"{p.Id}_r_{realIdx}_chk")
                                        .Width(16)
                                        .Alignment(TextAlignment.MiddleCenter)
                                        .Text(box ?? string.Empty, font)
                                        .TextColor(selected ? ramp.C600 : ink.C300)
                                        .FontSize(p.Theme.Metrics.FontSize);
                                }

                                if (p.Icon != null && font != null)
                                {
                                    string g = p.Icon(item) ?? string.Empty;
                                    if (!string.IsNullOrEmpty(g))
                                    {
                                        paper.Box($"{p.Id}_r_{realIdx}_ic")
                                            .Width(16)
                                            .Alignment(TextAlignment.MiddleCenter)
                                            .Text(g, font).TextColor(ink.C400).FontSize(p.Theme.Metrics.FontSize);
                                    }
                                }

                                if (font != null)
                                {
                                    paper.Box($"{p.Id}_r_{realIdx}_lbl")
                                        .Width(UnitValue.Stretch())
                                        .Alignment(TextAlignment.MiddleLeft)
                                        .Text(p.Display(item) ?? string.Empty, font)
                                        .TextColor(enabled ? (selected ? ink.C600 : ink.C500) : ink.C300)
                                        .FontSize(p.Theme.Metrics.FontSize);
                                }

                                if (p.Secondary != null && font != null)
                                {
                                    string sec = p.Secondary(item) ?? string.Empty;
                                    if (!string.IsNullOrEmpty(sec))
                                    {
                                        paper.Box($"{p.Id}_r_{realIdx}_sec")
                                            .Alignment(TextAlignment.MiddleRight)
                                            .Text(sec, font).TextColor(ink.C300).FontSize(p.Theme.Metrics.FontSize * 0.9f);
                                    }
                                }
                            }
                        }
                    });
            }

            // Pagination footer
            if (paginationH > 0f && font != null)
            {
                using (paper.Row($"{p.Id}_pg")
                    .Height(paginationH)
                    .ChildLeft(2).ChildRight(2)
                    .RowBetween(4)
                    .Enter())
                {
                    bool canPrev = pageIdx > 0;
                    bool canNext = pageIdx < pageCount - 1;
                    int capturedPage = pageIdx;

                    paper.Box($"{p.Id}_pg_prev")
                        .Width(20).Height(paginationH).Rounded(3)
                        .Alignment(TextAlignment.MiddleCenter)
                        .BackgroundColor(canPrev ? p.Theme.Neutral.C300 : p.Theme.Neutral.C200)
                        .Hovered.BackgroundColor(canPrev ? ramp.C400 : p.Theme.Neutral.C200).End()
                        .Text(string.IsNullOrEmpty(icons.ChevronLeft) ? "<" : icons.ChevronLeft, font)
                        .TextColor(canPrev ? ink.C500 : ink.C300)
                        .FontSize(p.Theme.Metrics.FontSize * 0.85f)
                        .OnClick(e =>
                        {
                            if (!canPrev) return;
                            paper.SetElementStorage(p.TriggerHandle, KeyPage, capturedPage - 1);
                        });

                    paper.Box($"{p.Id}_pg_label")
                        .Width(UnitValue.Stretch()).Height(paginationH)
                        .Alignment(TextAlignment.MiddleCenter)
                        .Text($"{pageIdx + 1} / {pageCount}", font)
                        .TextColor(ink.C400)
                        .FontSize(p.Theme.Metrics.FontSize * 0.85f);

                    paper.Box($"{p.Id}_pg_next")
                        .Width(20).Height(paginationH).Rounded(3)
                        .Alignment(TextAlignment.MiddleCenter)
                        .BackgroundColor(canNext ? p.Theme.Neutral.C300 : p.Theme.Neutral.C200)
                        .Hovered.BackgroundColor(canNext ? ramp.C400 : p.Theme.Neutral.C200).End()
                        .Text(string.IsNullOrEmpty(icons.ChevronRight) ? ">" : icons.ChevronRight, font)
                        .TextColor(canNext ? ink.C500 : ink.C300)
                        .FontSize(p.Theme.Metrics.FontSize * 0.85f)
                        .OnClick(e =>
                        {
                            if (!canNext) return;
                            paper.SetElementStorage(p.TriggerHandle, KeyPage, capturedPage + 1);
                        });
                }
            }
        }
    }
}
