using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Runtime;

namespace Prowl.Editor.Core;

/// <summary>
/// Central selection system for the editor.
/// Supports single select, multi-select with Ctrl, range-select with Shift.
/// </summary>
public static class Selection
{
    private static readonly List<object> _selected = [];

    /// <summary>Currently selected objects.</summary>
    public static IReadOnlyList<object> Selected => _selected;

    /// <summary>The primary/active selected object (last selected).</summary>
    public static object? ActiveObject => _selected.Count > 0 ? _selected[^1] : null;

    /// <summary>Fires when the selection changes.</summary>
    public static event Action? OnSelectionChanged;

    /// <summary>Clear all selection.</summary>
    public static void Clear()
    {
        _selected.Clear();
        OnSelectionChanged?.Invoke();
    }

    /// <summary>Select a single object, clearing previous selection.</summary>
    public static void Select(object obj)
    {
        // Don't fire OnSelectionChanged if nothing has changed.
        if (_selected.Count == 1 && Equals(_selected[0], obj))
            return;
        _selected.Clear();
        _selected.Add(obj);
        OnSelectionChanged?.Invoke();
    }

    /// <summary>Add an object to the selection (Ctrl+Click).</summary>
    public static void AddToSelection(object obj)
    {
        int index = _selected.IndexOf(obj);
        if (index == -1)
            _selected.Add(obj);
        else if (index < _selected.Count - 1)
            // Make sure the most recently added object is at the end of the list.
            (_selected[index], _selected[^1]) = (_selected[^1], _selected[index]);
        OnSelectionChanged?.Invoke();
    }

    /// <summary>Remove an object from the selection (Ctrl+Click on selected).</summary>
    public static void RemoveFromSelection(object obj)
    {
        _selected.Remove(obj);
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

    /// <summary>Replaces the current selection with the given list.</summary>
    public static void ReplaceSelection(IEnumerable<object> objects)
    {
        _selected.Clear();
        _selected.AddRange(objects);
        OnSelectionChanged?.Invoke();
    }

    /// <summary>Check if an object is currently selected.</summary>
    public static bool IsSelected(object obj) => _selected.Contains(obj);

    /// <summary>Get all selected objects of a specific type.</summary>
    public static IEnumerable<T> GetSelected<T>() => _selected.OfType<T>();

    /// <summary>Get the active object as a specific type, or null.</summary>
    public static T? GetActiveAs<T>() where T : class => ActiveObject as T;

    /// <summary>Number of selected objects.</summary>
    public static int Count => _selected.Count;

    #region Snapshots

    /// <summary>The selection at one point, restored with whatever of it still exists.</summary>
    internal readonly struct Snapshot(object[] objects)
    {
        public void Restore()
        {
            _selected.Clear();
            foreach (object obj in objects)
            {
                if (obj is EngineObject eo && eo.IsNotValid()) continue;
                _selected.Add(obj);
            }
            OnSelectionChanged?.Invoke();
        }
    }

    /// <summary>The current selection, to put back later. The edit world never changes during play, so its objects come back as they were.</summary>
    internal static Snapshot TakeSnapshot() => new([.. _selected]);

    /// <summary>Restores <paramref name="snapshot"/>, dropping objects destroyed since.</summary>
    internal static void RestoreSnapshot(Snapshot snapshot) => snapshot.Restore();

    #endregion

    #region Ping

    /// <summary>The GUID currently being pinged (yellow highlight).</summary>
    public static Guid PingedGuid { get; private set; }

    /// <summary>Time remaining on the current ping highlight (seconds).</summary>
    public static float PingTimer { get; private set; }

    /// <summary>Counts pings, so a panel can tell a repeated ping of the same GUID from the one it already showed.</summary>
    public static int PingVersion { get; private set; }

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
        PingVersion++;
    }
    /// <summary>
    /// Ping an asset or object by GUID - panels that display this GUID should
    /// scroll to it and show a temporary yellow highlight without changing selection.
    /// </summary>
    public static void FastPing(Guid guid)
    {
        if (guid == Guid.Empty) return;
        if (guid != PingedGuid) PingVersion++;
        PingedGuid = guid;
        PingTimer = 0.05f;
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
