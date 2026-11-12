// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using Prowl.Editor.Core;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor.Utils;

/// <summary>
/// Used by the project panel and the hierarchy panel to CTRL+click, SHIFT+click, and single-click multiple items.
/// </summary>
internal static class SelectionHelper
{
    private static readonly List<object> _selection = [];
    private static object? _lastClickedItem;

    /// <summary>
    /// Handle selection behaviors for an element.
    /// </summary>
    /// <param name="builder">The element builder.</param>
    /// <param name="paper">Used to retrieve CTRL and SHIFT key modifier status.</param>
    /// <param name="item">The selectable object.</param>
    /// <param name="items">The full list of selectable objects.</param>
    /// <param name="index">The index of <paramref name="item"/> in <paramref name="items"/>.</param>
    /// <param name="buildContextMenu">A callback that takes the clicked item and returns a context menu builder.</param>
    /// <returns>The <paramref name="builder"/> instance.</returns>
    public static ElementBuilder HandleMultiSelect(this ElementBuilder builder, Paper paper, object item, IReadOnlyList<object> items, int index, Func<object, Action<ContextBuilder>> buildContextMenu)
    {
        var state = (paper, item, index, items, buildContextMenu);
        return builder.OnPress(state, static (state, e) =>
            {
                e.StopPropagation();
                HandlePress(state.paper, state.item, state.items, state.index);
            })
            .OnClick(state, static (state, e) =>
            {
                e.StopPropagation();
                HandleClick(state.paper, state.item, state.items, state.index);
            })
            .OnRightClick(state, static (state, e) =>
            {
                e.StopPropagation();
                HandleRightClick(state.paper, state.item, state.items, state.index);
                Origami.ContextMenu(state.paper.PointerPos.X, state.paper.PointerPos.Y, state.buildContextMenu(state.item));
            })
            .OnDragEnd(static e => _selection.Clear());
    }

    /// <summary>
    /// Handle selection behaviors for a table widget.
    /// </summary>
    /// <param name="builder">The element builder.</param>
    /// <param name="paper">Used to retrieve CTRL and SHIFT key modifier status.</param>
    /// <param name="items">The full list of selectable objects.</param>
    /// <param name="buildContextMenu">A callback that takes the clicked item and returns a context menu builder.</param>
    /// <returns>The <paramref name="builder"/> instance.</returns>
    public static TableBuilder HandleMultiSelect(this TableBuilder builder, Paper paper, IReadOnlyList<object> items, Func<object, Action<ContextBuilder>> buildContextMenu)
    {
        return builder.MultiSelect()
            .IsSelected(i => IsSelected(items[i]))
            .OnSelectPreview((i, ctrl, shift) => HandlePress(paper, items[i], items, i))
            .OnSelectModified((i, ctrl, shift) => HandleClick(paper, items[i], items, i))
            .OnRowContext(i =>
            {
                HandleRightClick(paper, items[i], items, i);
                Origami.ContextMenu(paper.PointerPos.X, paper.PointerPos.Y, buildContextMenu(items[i]));
            })
            .OnRowDragEnd(static e => _selection.Clear());
    }

    /// <summary>
    /// Handle selection behaviors for an element.
    /// </summary>
    /// <param name="builder">The element builder.</param>
    /// <param name="paper">Used to retrieve CTRL and SHIFT key modifier status.</param>
    /// <param name="items">The list of tree nodes.</param>
    /// <param name="buildContextMenu">A callback that takes the clicked item and returns a context menu builder.</param>
    /// <returns>The <paramref name="builder"/> instance.</returns>
    public static TreeBuilder HandleMultiSelect(this TreeBuilder builder, Paper paper, IReadOnlyList<TreeNode> items, Func<object, Action<ContextBuilder>> buildContextMenu)
    {
        return builder.MultiSelect()
            .OnSelectPreview((e, ctrl, shift) => HandlePress(paper, e.Node.UserData, new TreeNodeListWrapper(items), e.Index))
            .OnSelectModified((e, ctrl, shift) => HandleClick(paper, e.Node.UserData, new TreeNodeListWrapper(items), e.Index))
            .OnRightClick(e =>
            {
                HandleRightClick(paper, e.Node.UserData, new TreeNodeListWrapper(items), e.Index);
                Origami.ContextMenu(paper.PointerPos.X, paper.PointerPos.Y, buildContextMenu(e.Node.UserData));
            })
            .OnDragEnd(static e => _selection.Clear());
    }

    /// <summary>
    /// Checks if an object is currently selected, i.e. if it is in the temporary selection, or if is no temporary
    /// selection, if it's in the global selection. This is used to highlight items in the list.
    /// </summary>
    public static bool IsSelected(object obj) => _selection.Count > 0 ? _selection.Contains(obj) : Selection.IsSelected(obj);

    /// <summary>
    /// Get all selected objects of a specific type, either from the temporary selection, if it exists, or from the
    /// global selection. This is used when dragging.
    /// </summary>
    public static IEnumerable<T> GetSelected<T>() => _selection.Count > 0 ? _selection.OfType<T>() : Selection.GetSelected<T>();

    /// <summary>
    /// Handle a left mouse down event on an item in a list, supporting Ctrl and Shift modifiers.
    /// </summary>
    /// <param name="paper">Used to retrieve modifier key status.</param>
    /// <param name="clickedItem">The item that was clicked.</param>
    /// <param name="allItems">All items in the list (for Shift-select range).</param>
    /// <param name="clickedIndex">Index of clicked item in allItems.</param>
    private static void HandlePress(Paper paper, object clickedItem, IReadOnlyList<object> allItems, int clickedIndex)
    {
        // If the item is already selected, then drag every selected item as a group.
        if (Selection.IsSelected(clickedItem))
            return;

        // Otherwise, handle selection as with the mouse down case.
        if (paper.IsKeyDown(PaperKey.LeftShift) == true || paper.IsKeyDown(PaperKey.RightShift))
        {
            // Shift+Click: select range from last clicked to current.
            int lastClickedIndex = GetLastClickedIndex(allItems, fallback: clickedIndex);
            _selection.Clear();
            for (int i = int.Min(clickedIndex, lastClickedIndex); i <= int.Max(clickedIndex, lastClickedIndex); i++)
                _selection.Add(allItems[i]);
        }
        else if (paper.IsCommandDown())
        {
            // Ctrl+Click: toggle single item.
            _selection.Clear();
            _selection.AddRange(Selection.Selected);
            _selection.Add(clickedItem);
        }
        else
        {
            // Plain click: single select.
            _selection.Clear();
            _selection.Add(clickedItem);
        }
    }

    /// <summary>
    /// Handle a left mouse up event on an item in a list, supporting Ctrl and Shift modifiers.
    /// </summary>
    /// <param name="paper">Used to retrieve modifier key status.</param>
    /// <param name="clickedItem">The item that was clicked.</param>
    /// <param name="allItems">All items in the list (for Shift-select range).</param>
    /// <param name="clickedIndex">Index of clicked item in allItems.</param>
    private static void HandleClick(Paper paper, object clickedItem, IReadOnlyList<object> allItems, int clickedIndex)
    {
        if (paper.IsKeyDown(PaperKey.LeftShift) == true || paper.IsKeyDown(PaperKey.RightShift))
        {
            // Shift+Click: select range from last clicked to current.
            int lastClickedIndex = GetLastClickedIndex(allItems, fallback: clickedIndex);

            // The Selection class makes the last item in the list the ActiveObject.
            // The ActiveObject should be the clicked item, so add that last.
            _selection.Clear();
            if (lastClickedIndex < clickedIndex)
            {
                for (int i = lastClickedIndex; i <= clickedIndex; i++)
                    _selection.Add(allItems[i]);
            }
            else
            {
                for (int i = lastClickedIndex; i >= clickedIndex; i--)
                    _selection.Add(allItems[i]);
            }
            Selection.ReplaceSelection(_selection);
        }
        else if (paper.IsCommandDown())
        {
            // Ctrl+Click: toggle single item
            Selection.ToggleSelection(clickedItem);

            // Use this clicked item when shift-clicking.
            _lastClickedItem = clickedItem;
        }
        else
        {
            // Plain click: single select.
            Selection.Select(clickedItem);

            // Use this clicked item when shift-clicking.
            _lastClickedItem = clickedItem;
        }

        // Clear the temporary selection.
        _selection.Clear();
    }

    /// <summary>
    /// Handle a right mouse down event on an item in a list, supporting Ctrl and Shift modifiers.
    /// </summary>
    /// <param name="paper">Used to retrieve modifier key status.</param>
    /// <param name="clickedItem">The item that was clicked.</param>
    /// <param name="allItems">All items in the list (for Shift-select range).</param>
    /// <param name="clickedIndex">Index of clicked item in allItems.</param>
    private static void HandleRightClick(Paper paper, object clickedItem, IReadOnlyList<object> allItems, int clickedIndex)
    {
        // Behave like mouse down.
        HandlePress(paper, clickedItem, allItems, clickedIndex);

        // But make the selection permanent.
        if (_selection.Count > 0)
        {
            Selection.ReplaceSelection(_selection);
            _selection.Clear();
        }

        // At least one object must be selected.
        Debug.Assert(Selection.Count > 0);
    }

    /// <summary>
    /// Gets the index of the last clicked item.
    /// </summary>
    /// <param name="items">The list items.</param>
    /// <param name="fallback">The index to return if the last clicked item isn't in the list.</param>
    /// <returns>The index of the last clicked item, or <paramref name="fallback"/> if it wasn't found.</returns>
    private static int GetLastClickedIndex(IReadOnlyList<object> items, int fallback)
    {
        for (int i = 0; i < items.Count; i++)
            if (Equals(_lastClickedItem, items[i]))
                return i;
        return fallback;
    }

    /// <summary>Wrapper class used to convert a list of TreeNodes into a list of SceneHeader / GameObjects.</summary>
    private class TreeNodeListWrapper(IReadOnlyList<TreeNode> underlyingList) : IReadOnlyList<object>
    {
        public object this[int index] => underlyingList[index].UserData;

        public int Count => underlyingList.Count;

        public IEnumerator<object> GetEnumerator() => throw new NotImplementedException();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

}
