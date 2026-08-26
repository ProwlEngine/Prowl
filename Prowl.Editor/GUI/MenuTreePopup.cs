// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.Editor.GUI;

/// <summary>
/// One selectable leaf in a <see cref="MenuTreePopup"/>. <see cref="Path"/> is the full
/// slash-separated menu path; <see cref="Category"/> and <see cref="Name"/> are its split form.
/// </summary>
public readonly struct MenuTreeEntry
{
    /// <summary>Full path, e.g. <c>"Physics/Colliders/Box Collider"</c>.</summary>
    public string Path { get; init; }
    /// <summary>Everything before the last slash, e.g. <c>"Physics/Colliders"</c>. Empty at root.</summary>
    public string Category { get; init; }
    /// <summary>The leaf name, e.g. <c>"Box Collider"</c>.</summary>
    public string Name { get; init; }
    /// <summary>Icon glyph drawn to the left of the name.</summary>
    public string Icon { get; init; }
    /// <summary>Caller payload handed back to the pick callback.</summary>
    public object? Tag { get; init; }

    /// <summary>Builds an entry by splitting <paramref name="path"/> at its last slash.</summary>
    public static MenuTreeEntry FromPath(string path, string icon, object? tag)
    {
        int lastSlash = path.LastIndexOf('/');
        return new MenuTreeEntry
        {
            Path = path,
            Category = lastSlash >= 0 ? path[..lastSlash] : "",
            Name = lastSlash >= 0 ? path[(lastSlash + 1)..] : path,
            Icon = icon,
            Tag = tag,
        };
    }
}

/// <summary>Per-popup navigation state: the search box text and the folder the user has drilled into.</summary>
public sealed class MenuTreeState
{
    public string Search = "";
    public List<string> Nav = [];

    public void Reset()
    {
        Search = "";
        Nav.Clear();
    }
}

/// <summary>
/// The searchable, drill-down menu popover used by Add Component and the material inspector's
/// shader picker. Callers own the open/closed flag and the entry list; this draws the chrome, the
/// search field, the folder navigation and the rows.
/// </summary>
public static class MenuTreePopup
{
    /// <summary>
    /// Matches Origami's DropdownBuilder default popover cap (Widgets/Dropdown.cs) so these
    /// popovers scroll the same way any other dropdown in the editor does.
    /// </summary>
    public const float MaxListHeight = 320f;

    /// <summary>
    /// Fullscreen, invisible click-catcher so clicking anywhere outside the popover dismisses it,
    /// the same click-outside behaviour Origami's dropdowns use (DropdownInternal.RenderBackdrop).
    /// </summary>
    public static void Backdrop(Paper paper, string id, Action onDismiss)
    {
        paper.Box($"{id}_backdrop")
            .PositionType(PositionType.SelfDirected)
            .Position(-9999, -9999)
            .Size(99999, 99999)
            .Layer(Layer.Overlay)
            .StopEventPropagation()
            .OnClick(0, (_, _) => onDismiss());
    }

    /// <summary>
    /// Popover anchored directly below <paramref name="trigger"/>, styled like Origami's dropdown
    /// popovers. Call from inside the trigger element's scope.
    /// </summary>
    public static void Popover(
        Paper paper,
        string id,
        ElementHandle trigger,
        IReadOnlyList<MenuTreeEntry> entries,
        MenuTreeState state,
        Action<MenuTreeEntry> onPick,
        string searchPlaceholder,
        string emptyText,
        float minWidth = 280f)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        float triggerWidth = trigger.Data.LayoutRect.Size.X > 0 ? (float)trigger.Data.LayoutRect.Size.X : minWidth;
        float triggerHeight = trigger.Data.LayoutRect.Size.Y > 0 ? (float)trigger.Data.LayoutRect.Size.Y : 28f;
        float width = MathF.Max(triggerWidth, minWidth);

        const float padX = 5f, padY = 5f, searchH = 28f, searchGap = 4f;

        using (paper.Column($"{id}_pop")
            .PositionType(PositionType.SelfDirected)
            .Position(0, triggerHeight + 4f)
            .Width(width)
            .Height(UnitValue.Auto)
            .BackgroundColor(EditorTheme.Popover)
            .BorderColor(EditorTheme.BorderStrong).BorderWidth(1)
            .DropShadow(0, 14, 40, -6, EditorTheme.Shadow)
            .Rounded(EditorTheme.Roundness + 2f)
            .Padding(padX, padX, padY, padY)
            .ColBetween(searchGap)
            .HookToParent()
            .Layer(Layer.Topmost)
            .ClampToScreen()
            .StopEventPropagation()
            .Enter())
        {
            using (paper.Row($"{id}_search_row").Height(searchH).Enter())
            {
                Origami.SearchField(paper, $"{id}_search", state.Search, v => state.Search = v, searchPlaceholder).Show();
            }

            Origami.ScrollView(paper, $"{id}_scroll", width - padX * 2, MaxListHeight)
                .Padding(0)
                .Body(() =>
            {
                if (!string.IsNullOrEmpty(state.Search))
                    DrawSearchResults(paper, font, id, entries, state, onPick, emptyText);
                else
                    DrawBrowseLevel(paper, font, id, entries, state, onPick, emptyText);
            });
        }
    }

    // Flat, globally-filtered list shown while the search box has text (ignores the current folder).
    private static void DrawSearchResults(Paper paper, Prowl.Scribe.FontFile font, string id,
        IReadOnlyList<MenuTreeEntry> entries, MenuTreeState state, Action<MenuTreeEntry> onPick, string emptyText)
    {
        var filtered = entries.Where(e =>
            e.Name.Contains(state.Search, StringComparison.OrdinalIgnoreCase) ||
            e.Path.Contains(state.Search, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (filtered.Count == 0)
        {
            DrawEmpty(paper, font, id, emptyText);
            return;
        }

        for (int i = 0; i < filtered.Count; i++)
            DrawItem(paper, font, $"{id}_item_{i}", filtered[i], onPick, showPath: true);
    }

    // Click-to-navigate browser: the current folder's subfolders and leaves, with a "Back" row when
    // nested. Clicking a folder drills in; clicking Back steps back out.
    private static void DrawBrowseLevel(Paper paper, Prowl.Scribe.FontFile font, string id,
        IReadOnlyList<MenuTreeEntry> entries, MenuTreeState state, Action<MenuTreeEntry> onPick, string emptyText)
    {
        string prefix = string.Join("/", state.Nav);
        var (leaves, subfolders) = SplitLevel(entries, prefix);

        if (state.Nav.Count > 0)
        {
            string currentName = state.Nav[^1];
            using (paper.Row($"{id}_back")
                .Height(EditorTheme.RowHeight)
                .Hovered.BackgroundColor(EditorTheme.Hover).End()
                .Rounded(6).ChildLeft(9).ChildRight(9).RowBetween(9)
                .OnClick(0, (_, _) => state.Nav.RemoveAt(state.Nav.Count - 1))
                .Enter())
            {
                paper.Box($"{id}_back_ico").Width(16).Height(EditorTheme.RowHeight)
                    .Text(EditorIcons.ChevronLeft, font).TextColor(EditorTheme.Ink400)
                    .FontSize(11f).Alignment(TextAlignment.MiddleCenter);
                paper.Box($"{id}_back_name").Height(EditorTheme.RowHeight)
                    .Text(currentName, font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            }

            paper.Box($"{id}_back_sep").Height(1).Margin(8, 3, 8, 3).BackgroundColor(EditorTheme.BorderSoft);
        }

        foreach (var folder in subfolders)
        {
            var captured = folder;
            using (paper.Row($"{id}_folder_{folder}")
                .Height(EditorTheme.RowHeight)
                .Hovered.BackgroundColor(EditorTheme.Hover).End()
                .Rounded(6).ChildLeft(9).ChildRight(9).RowBetween(9)
                .OnClick(0, (_, _) => state.Nav.Add(captured))
                .Enter())
            {
                paper.Box($"{id}_folder_{folder}_ico").Width(16).Height(EditorTheme.RowHeight)
                    .Text(EditorIcons.Folder, font).TextColor(EditorTheme.Ink400)
                    .FontSize(11f).Alignment(TextAlignment.MiddleCenter);

                paper.Box($"{id}_folder_{folder}_name")
                    .Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight)
                    .Text(folder, font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

                paper.Box($"{id}_folder_{folder}_arw").Width(16).Height(EditorTheme.RowHeight)
                    .Text(EditorIcons.ChevronRight, font).TextColor(EditorTheme.Ink300)
                    .FontSize(11f).Alignment(TextAlignment.MiddleCenter);
            }
        }

        if (subfolders.Count > 0 && leaves.Count > 0)
            paper.Box($"{id}_level_sep").Height(1).Margin(8, 3, 8, 3).BackgroundColor(EditorTheme.BorderSoft);

        for (int i = 0; i < leaves.Count; i++)
            DrawItem(paper, font, $"{id}_item_{i}_{leaves[i].Name}", leaves[i], onPick, showPath: false);

        if (subfolders.Count == 0 && leaves.Count == 0)
            DrawEmpty(paper, font, id, emptyText);
    }

    // Splits entries into this level's direct leaves (Category == prefix) and its immediate
    // subfolder names (the next path segment past prefix), so browsing drills one segment at a
    // time regardless of how deep the full path goes.
    private static (List<MenuTreeEntry> Leaves, List<string> Subfolders) SplitLevel(
        IReadOnlyList<MenuTreeEntry> entries, string prefix)
    {
        var leaves = new List<MenuTreeEntry>();
        var subfolders = new HashSet<string>(StringComparer.Ordinal);

        foreach (var e in entries)
        {
            if (e.Category == prefix)
            {
                leaves.Add(e);
                continue;
            }

            if (prefix.Length > 0 && !e.Category.StartsWith(prefix + "/", StringComparison.Ordinal))
                continue;

            string rel = prefix.Length > 0 ? e.Category[(prefix.Length + 1)..] : e.Category;
            int slash = rel.IndexOf('/');
            subfolders.Add(slash < 0 ? rel : rel[..slash]);
        }

        var sortedSubfolders = subfolders.ToList();
        sortedSubfolders.Sort(StringComparer.OrdinalIgnoreCase);
        leaves.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return (leaves, sortedSubfolders);
    }

    private static void DrawEmpty(Paper paper, Prowl.Scribe.FontFile font, string id, string emptyText)
    {
        paper.Box($"{id}_empty").Height(40)
            .Text(emptyText, font)
            .TextColor(EditorTheme.Ink300)
            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
    }

    private static void DrawItem(Paper paper, Prowl.Scribe.FontFile font, string id,
        MenuTreeEntry entry, Action<MenuTreeEntry> onPick, bool showPath)
    {
        using (paper.Row(id)
            .Height(EditorTheme.RowHeight)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .Rounded(6).ChildLeft(9).ChildRight(9).RowBetween(9)
            .OnClick(entry, (e, _) => onPick(e))
            .Enter())
        {
            paper.Box($"{id}_ico")
                .Width(16).Height(EditorTheme.RowHeight)
                .Text(entry.Icon, font).TextColor(EditorTheme.Ink400)
                .FontSize(11f).Alignment(TextAlignment.MiddleCenter);

            paper.Box($"{id}_name")
                .Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight)
                .Text(entry.Name, font).TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

            // While searching, the folder the match came from is the only way to tell two
            // same-named entries apart.
            if (showPath && entry.Category.Length > 0)
            {
                paper.Box($"{id}_cat")
                    .Height(EditorTheme.RowHeight)
                    .Text(entry.Category, font).TextColor(EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);
            }
        }
    }
}
