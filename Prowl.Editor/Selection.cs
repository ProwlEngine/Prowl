using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Editor;

/// <summary>
/// Central selection system for the editor.
/// Supports single select, multi-select with Ctrl, range-select with Shift.
/// </summary>
public static class Selection
{
    private static readonly List<object> _selected = new();
    private static object? _activeObject;
    private static int _lastClickedIndex = -1;

    /// <summary>Currently selected objects.</summary>
    public static IReadOnlyList<object> Selected => _selected;

    /// <summary>The primary/active selected object (last selected).</summary>
    public static object? ActiveObject
    {
        get => _activeObject;
        set
        {
            _activeObject = value;
            if (value != null && !_selected.Contains(value))
            {
                _selected.Clear();
                _selected.Add(value);
            }
            OnSelectionChanged?.Invoke();
        }
    }

    /// <summary>Fires when the selection changes.</summary>
    public static event Action? OnSelectionChanged;

    /// <summary>Clear all selection.</summary>
    public static void Clear()
    {
        _selected.Clear();
        _activeObject = null;
        _lastClickedIndex = -1;
        OnSelectionChanged?.Invoke();
    }

    /// <summary>Select a single object, clearing previous selection.</summary>
    public static void Select(object obj)
    {
        _selected.Clear();
        _selected.Add(obj);
        _activeObject = obj;
        OnSelectionChanged?.Invoke();
    }

    /// <summary>Add an object to the selection (Ctrl+Click).</summary>
    public static void AddToSelection(object obj)
    {
        if (!_selected.Contains(obj))
            _selected.Add(obj);
        _activeObject = obj;
        OnSelectionChanged?.Invoke();
    }

    /// <summary>Remove an object from the selection (Ctrl+Click on selected).</summary>
    public static void RemoveFromSelection(object obj)
    {
        _selected.Remove(obj);
        if (_activeObject == obj)
            _activeObject = _selected.Count > 0 ? _selected[^1] : null;
        OnSelectionChanged?.Invoke();
    }

    /// <summary>Toggle an object in the selection (Ctrl+Click).</summary>
    public static void ToggleSelection(object obj)
    {
        if (_selected.Contains(obj))
            RemoveFromSelection(obj);
        else
            AddToSelection(obj);
    }

    /// <summary>
    /// Handle a click on an item in a list, supporting Ctrl and Shift modifiers.
    /// </summary>
    /// <param name="clickedItem">The item that was clicked.</param>
    /// <param name="allItems">All items in the list (for Shift-select range).</param>
    /// <param name="clickedIndex">Index of clicked item in allItems.</param>
    /// <param name="ctrl">Is Ctrl held?</param>
    /// <param name="shift">Is Shift held?</param>
    public static void HandleListClick(object clickedItem, IReadOnlyList<object> allItems, int clickedIndex, bool ctrl, bool shift)
    {
        if (shift && _lastClickedIndex >= 0 && _lastClickedIndex < allItems.Count)
        {
            // Shift+Click: select range from last clicked to current
            int start = Math.Min(_lastClickedIndex, clickedIndex);
            int end = Math.Max(_lastClickedIndex, clickedIndex);

            if (!ctrl)
                _selected.Clear();

            for (int i = start; i <= end; i++)
            {
                if (!_selected.Contains(allItems[i]))
                    _selected.Add(allItems[i]);
            }
            _activeObject = clickedItem;
            OnSelectionChanged?.Invoke();
        }
        else if (ctrl)
        {
            // Ctrl+Click: toggle single item
            ToggleSelection(clickedItem);
            _lastClickedIndex = clickedIndex;
        }
        else
        {
            // Plain click: single select
            Select(clickedItem);
            _lastClickedIndex = clickedIndex;
        }
    }

    /// <summary>Check if an object is currently selected.</summary>
    public static bool IsSelected(object obj) => _selected.Contains(obj);

    /// <summary>Get all selected objects of a specific type.</summary>
    public static IEnumerable<T> GetSelected<T>() => _selected.OfType<T>();

    /// <summary>Get the active object as a specific type, or null.</summary>
    public static T? GetActiveAs<T>() where T : class => _activeObject as T;

    /// <summary>Number of selected objects.</summary>
    public static int Count => _selected.Count;

    #region Ping

    /// <summary>The GUID currently being pinged (yellow highlight).</summary>
    public static Guid PingedGuid { get; private set; }

    /// <summary>Time remaining on the current ping highlight (seconds).</summary>
    public static float PingTimer { get; private set; }

    private const float PingDuration = 2.5f;

    /// <summary>
    /// Ping an asset or object by GUID - panels that display this GUID should
    /// scroll to it and show a temporary yellow highlight without changing selection.
    /// </summary>
    public static void Ping(Guid guid)
    {
        if (guid == Guid.Empty) return;
        PingedGuid = guid;
        PingTimer = PingDuration;
    }

    /// <summary>Call once per frame to tick down the ping timer.</summary>
    public static void UpdatePing(float deltaTime)
    {
        if (PingTimer > 0f)
        {
            PingTimer -= deltaTime;
            if (PingTimer <= 0f)
            {
                PingTimer = 0f;
                PingedGuid = Guid.Empty;
            }
        }
    }

    /// <summary>Get the current ping highlight alpha (0-1), fading out over time.</summary>
    public static float GetPingAlpha()
    {
        if (PingTimer <= 0f) return 0f;
        // Full brightness for first 1.5s, then fade out over remaining 1s
        float fadeStart = 1.0f;
        if (PingTimer > fadeStart) return 1f;
        return PingTimer / fadeStart;
    }

    #endregion
}
