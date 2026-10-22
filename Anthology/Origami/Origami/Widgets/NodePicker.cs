// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.OrigamiUI;

/// <summary>Fluent builder for the entries of a <see cref="NodePicker"/>, rebuilt every frame it is open.</summary>
public sealed class NodePickerBuilder
{
    internal readonly struct Entry
    {
        public Entry(string category, string label, string? tooltip, Action onPick)
        {
            Category = category; Label = label; Tooltip = tooltip; OnPick = onPick;
        }

        public string Category { get; }
        public string Label { get; }
        public string? Tooltip { get; }
        public Action OnPick { get; }
    }

    internal readonly List<Entry> Entries = [];
    internal string Hint = "Search nodes...";
    private string _section = "";

    /// <summary>The faint text in the empty search field.</summary>
    public NodePickerBuilder Placeholder(string text) { Hint = text; return this; }

    /// <summary>Starts a category: the items added after it are listed under this header.</summary>
    public NodePickerBuilder Section(string category) { _section = category; return this; }

    /// <summary>Adds one entry under the current section.</summary>
    public NodePickerBuilder Item(string label, Action onPick, string? tooltip = null)
    {
        Entries.Add(new Entry(_section, label, tooltip, onPick));
        return this;
    }

    /// <summary>Adds an entry per item, each under its own category, in the order given.</summary>
    public NodePickerBuilder Items<T>(IEnumerable<T> items, Func<T, string> category, Func<T, string> label,
        Func<T, string?> tooltip, Action<T> onPick)
    {
        foreach (T item in items)
        {
            T picked = item;
            Entries.Add(new Entry(category(item), label(item), tooltip(item), () => onPick(picked)));
        }
        return this;
    }
}

/// <summary>
/// A searchable, categorised popup for creating nodes. Like <see cref="ContextMenu"/> only one is open at
/// a time, it sits in a modal that closes on Escape or a click outside, and it keeps its own search text.
/// </summary>
public static class NodePicker
{
    private const float Width = 280f;
    private const float MaxHeight = 360f;
    private const float ListHeight = 300f;

    private static bool _isOpen;
    private static float _x, _y;
    private static string _search = "";
    private static Action<NodePickerBuilder>? _build;
    private static IModal? _modalHandle;

    public static bool IsOpen => _isOpen;

    /// <summary>Opens the picker at a screen position, closing any open one.</summary>
    public static void Show(float x, float y, Action<NodePickerBuilder> build)
    {
        Close();
        _isOpen = true;
        _x = x;
        _y = y;
        _search = "";
        _build = build;

        _modalHandle = new CustomDrawModal((paper, layer, _) => Draw(paper, layer))
        {
            CloseOnBackdrop = true,
            CloseOnEscape = true,
            // A click outside or Escape removes the modal without going through Close.
            OnDismissed = _ => { _isOpen = false; _modalHandle = null; _build = null; },
        };
        Modal.Push(_modalHandle);
    }

    public static void Close()
    {
        if (_modalHandle != null)
        {
            Modal.Remove(_modalHandle);
            _modalHandle = null;
        }
        _isOpen = false;
        _build = null;
    }

    private static void Draw(Paper paper, int layer)
    {
        if (!_isOpen || _build == null) return;

        var theme = Origami.Current;
        var font = theme.Font;
        if (font == null) return;
        var m = theme.Metrics;

        var builder = new NodePickerBuilder();
        _build(builder);
        List<NodePickerBuilder.Entry> matches = Filter(builder.Entries);

        using (paper.Column("npick").PositionType(PositionType.SelfDirected).Position(_x, _y)
            .Width(Width).Height(UnitValue.Auto).MaxHeight(MaxHeight).Layer(layer)
            .ClampToScreen()
            .BackgroundColor(theme.Popover).Rounded(m.ContainerRounding)
            .BorderColor(theme.BorderStrong).BorderWidth(1)
            .Padding(m.Padding, m.Padding, m.Padding, m.Padding).Gap(m.Spacing)
            .DropShadow(0, 14, 40, 0, theme.Shadow)
            .StopEventPropagation().Enter())
        {
            Origami.SearchField(paper, "npick_search", _search, v => _search = v, builder.Hint).AutoFocus().Show();

            if (matches.Count == 0)
            {
                paper.Box("npick_none").Width(UnitValue.Stretch()).Height(m.RowHeight).IsNotInteractable()
                    .Text("Nothing matches.", font).TextColor(theme.Ink.C200)
                    .FontSize(m.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
                return;
            }

            Origami.ScrollView(paper, "npick_list", Width - m.Padding * 2f, ListHeight).Body(() =>
            {
                string? category = null;
                for (int i = 0; i < matches.Count; i++)
                {
                    NodePickerBuilder.Entry entry = matches[i];
                    if (entry.Category != category)
                    {
                        category = entry.Category;
                        if (category.Length > 0)
                            paper.Box("npick_cat", i).Width(UnitValue.Stretch()).Height(20).IsNotInteractable()
                                .Margin(m.Padding, 0, m.Spacing, 0)
                                .Text(category, font).TextColor(theme.Ink.C200)
                                .FontSize(m.FontSizeSmall * 0.9f).Alignment(TextAlignment.MiddleLeft);
                    }

                    var row = paper.Box("npick_item", i).Width(UnitValue.Stretch()).Height(m.RowHeight)
                        .Padding(m.Padding, m.Padding, 0, 0).Rounded(m.SmallRounding)
                        .Hovered.BackgroundColor(theme.Hover).End()
                        .Text(entry.Label, font).TextColor(theme.Ink.C500)
                        .FontSize(m.FontSizeSmall).Alignment(TextAlignment.MiddleLeft)
                        .OnClick(entry, (picked, _) => { Close(); picked.OnPick(); });
                    if (!string.IsNullOrEmpty(entry.Tooltip)) row.Tooltip(entry.Tooltip!);
                }
            });
        }
    }

    private static List<NodePickerBuilder.Entry> Filter(List<NodePickerBuilder.Entry> entries)
    {
        if (_search.Length == 0) return entries;

        var matches = new List<NodePickerBuilder.Entry>();
        foreach (NodePickerBuilder.Entry entry in entries)
            if (entry.Label.Contains(_search, StringComparison.OrdinalIgnoreCase)
                || entry.Category.Contains(_search, StringComparison.OrdinalIgnoreCase))
                matches.Add(entry);
        return matches;
    }
}
