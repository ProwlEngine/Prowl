// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Prowl.Runtime;

namespace Prowl.Editor;

/// <summary>
/// A rebindable key combination: a primary key plus optional modifier flags.
/// Serialized to EditorSettings.json via System.Text.Json.
/// </summary>
public class ShortcutBinding
{
    public KeyCode Key { get; set; }
    public bool Ctrl { get; set; }
    public bool Shift { get; set; }
    public bool Alt { get; set; }

    public ShortcutBinding() { }

    public ShortcutBinding(KeyCode key, bool ctrl = false, bool shift = false, bool alt = false)
    {
        Key = key;
        Ctrl = ctrl;
        Shift = shift;
        Alt = alt;
    }

    public ShortcutBinding Clone() => new(Key, Ctrl, Shift, Alt);

    public bool Equals(ShortcutBinding other)
        => Key == other.Key && Ctrl == other.Ctrl && Shift == other.Shift && Alt == other.Alt;
}

/// <summary>
/// Describes a registered shortcut: its identity, display info, default binding, and optional user override.
/// </summary>
public class ShortcutDefinition
{
    public string Id { get; }
    public string DisplayName { get; }
    public string Category { get; }
    public ShortcutBinding Default { get; }
    public ShortcutBinding? Override { get; set; }

    /// <summary>The effective binding (override if set, else default).</summary>
    public ShortcutBinding Binding => Override ?? Default;

    public ShortcutDefinition(string id, string displayName, ShortcutBinding defaultBinding)
    {
        Id = id;
        DisplayName = displayName;
        Default = defaultBinding;

        int lastSlash = id.LastIndexOf('/');
        Category = lastSlash > 0 ? id[..lastSlash] : "General";
    }
}

/// <summary>
/// Central registry for named, rebindable keyboard shortcuts.
/// Shortcuts are registered at startup and checked each frame via <see cref="IsPressed"/>.
/// User overrides are persisted in <see cref="EditorSettings.ShortcutOverrides"/>.
/// </summary>
public static class ShortcutManager
{
    private static readonly Dictionary<string, ShortcutDefinition> _shortcuts = new();
    private static bool _overridesLoaded;

    /// <summary>
    /// When true, all <see cref="IsPressed"/> calls return false.
    /// Set during shortcut rebinding in the Preferences panel.
    /// </summary>
    public static bool IsRebinding { get; set; }

    // ================================================================
    //  Registration
    // ================================================================

    /// <summary>Register a shortcut with a default binding.</summary>
    public static void Register(string id, string displayName, KeyCode key,
        bool ctrl = false, bool shift = false, bool alt = false)
    {
        var def = new ShortcutDefinition(id, displayName, new ShortcutBinding(key, ctrl, shift, alt));

        // If overrides have been loaded and one exists for this id, apply it
        if (_overridesLoaded)
        {
            var overrides = EditorSettings.Instance.ShortcutOverrides;
            if (overrides.TryGetValue(id, out var ov))
                def.Override = ov;
        }

        _shortcuts[id] = def;
    }

    // ================================================================
    //  Querying
    // ================================================================

    /// <summary>
    /// Returns true if the shortcut was triggered this frame.
    /// Checks <see cref="Input.GetKeyDown"/> for the primary key and enforces
    /// exact modifier state (Ctrl/Shift/Alt must match exactly).
    /// </summary>
    public static bool IsPressed(string id)
    {
        if (IsRebinding) return false;

        // Don't fire shortcuts when Paper has keyboard focus (e.g. text field is active)
        if (EditorApplication.Instance?.PaperInstance?.WantsCaptureKeyboard == true) return false;

        if (!_shortcuts.TryGetValue(id, out var def)) return false;

        var b = def.Binding;
        if (!Input.GetKeyDown(b.Key)) return false;
        if (b.Ctrl != Input.IsCtrlPressed) return false;
        if (b.Shift != Input.IsShiftPressed) return false;
        if (b.Alt != Input.IsAltPressed) return false;
        return true;
    }

    /// <summary>Get the effective binding for a shortcut.</summary>
    public static ShortcutBinding? GetBinding(string id)
        => _shortcuts.TryGetValue(id, out var def) ? def.Binding : null;

    /// <summary>Get the default binding for a shortcut.</summary>
    public static ShortcutBinding? GetDefault(string id)
        => _shortcuts.TryGetValue(id, out var def) ? def.Default : null;

    /// <summary>Get all registered shortcut definitions.</summary>
    public static IEnumerable<ShortcutDefinition> GetAllShortcuts()
        => _shortcuts.Values.OrderBy(s => s.Category).ThenBy(s => s.DisplayName);

    // ================================================================
    //  Override Management
    // ================================================================

    /// <summary>Set a user override for a shortcut and persist to settings.</summary>
    public static void SetOverride(string id, ShortcutBinding binding)
    {
        if (!_shortcuts.TryGetValue(id, out var def)) return;
        def.Override = binding;
        SaveOverrides();
    }

    /// <summary>Clear the user override for a shortcut (reverts to default) and persist.</summary>
    public static void ClearOverride(string id)
    {
        if (!_shortcuts.TryGetValue(id, out var def)) return;
        def.Override = null;
        SaveOverrides();
    }

    /// <summary>Clear all user overrides and persist.</summary>
    public static void ClearAllOverrides()
    {
        foreach (var def in _shortcuts.Values)
            def.Override = null;
        SaveOverrides();
    }

    /// <summary>Find shortcut IDs that conflict with a proposed binding (same key + modifiers).</summary>
    public static List<string> FindConflicts(string excludeId, ShortcutBinding proposed)
    {
        var conflicts = new List<string>();
        foreach (var (id, def) in _shortcuts)
        {
            if (id == excludeId) continue;
            if (def.Binding.Equals(proposed))
                conflicts.Add(id);
        }
        return conflicts;
    }

    // ================================================================
    //  Persistence
    // ================================================================

    /// <summary>Load user overrides from EditorSettings. Called once during initialization.</summary>
    public static void LoadOverrides()
    {
        if (_overridesLoaded) return;
        _overridesLoaded = true;

        var overrides = EditorSettings.Instance.ShortcutOverrides;
        foreach (var (id, binding) in overrides)
        {
            if (_shortcuts.TryGetValue(id, out var def))
                def.Override = binding;
        }
    }

    /// <summary>Save current overrides to EditorSettings.</summary>
    public static void SaveOverrides()
    {
        var overrides = new Dictionary<string, ShortcutBinding>();
        foreach (var (id, def) in _shortcuts)
        {
            if (def.Override != null)
                overrides[id] = def.Override;
        }
        EditorSettings.Instance.ShortcutOverrides = overrides;
        EditorSettings.Instance.Save();
    }

    // ================================================================
    //  Display
    // ================================================================

    /// <summary>
    /// Returns a human-readable string for a binding, e.g. "Ctrl+Shift+S" or "Delete".
    /// </summary>
    public static string GetDisplayString(ShortcutBinding? binding)
    {
        if (binding == null) return "None";

        var sb = new StringBuilder();
        if (binding.Ctrl) sb.Append("Ctrl+");
        if (binding.Shift) sb.Append("Shift+");
        if (binding.Alt) sb.Append("Alt+");
        sb.Append(FormatKeyName(binding.Key));
        return sb.ToString();
    }

    private static string FormatKeyName(KeyCode key) => key switch
    {
        KeyCode.Space => "Space",
        KeyCode.Enter => "Enter",
        KeyCode.Escape => "Escape",
        KeyCode.Tab => "Tab",
        KeyCode.Backspace => "Backspace",
        KeyCode.Delete => "Delete",
        KeyCode.Insert => "Insert",
        KeyCode.Home => "Home",
        KeyCode.End => "End",
        KeyCode.PageUp => "PageUp",
        KeyCode.PageDown => "PageDown",
        KeyCode.Up => "Up",
        KeyCode.Down => "Down",
        KeyCode.Left => "Left",
        KeyCode.Right => "Right",
        KeyCode.F1 => "F1", KeyCode.F2 => "F2", KeyCode.F3 => "F3",
        KeyCode.F4 => "F4", KeyCode.F5 => "F5", KeyCode.F6 => "F6",
        KeyCode.F7 => "F7", KeyCode.F8 => "F8", KeyCode.F9 => "F9",
        KeyCode.F10 => "F10", KeyCode.F11 => "F11", KeyCode.F12 => "F12",
        _ => key.ToString()
    };
}

// ================================================================
//  Built-in shortcut registrations
// ================================================================

internal static class BuiltInShortcuts
{
    [InitializeOnLoad]
    public static void Register()
    {
        // Load persisted overrides first (idempotent)
        ShortcutManager.LoadOverrides();

        // Global
        ShortcutManager.Register("Global/Save", "Save Scene", KeyCode.S, ctrl: true);
        ShortcutManager.Register("Global/SaveAs", "Save Scene As", KeyCode.S, ctrl: true, shift: true);
        ShortcutManager.Register("Global/NewScene", "New Scene", KeyCode.N, ctrl: true);
        ShortcutManager.Register("Global/Undo", "Undo", KeyCode.Z, ctrl: true);
        ShortcutManager.Register("Global/Redo", "Redo", KeyCode.Y, ctrl: true);

        // Scene View
        ShortcutManager.Register("Scene/Focus", "Focus Selection", KeyCode.F);
        ShortcutManager.Register("Scene/Delete", "Delete Selected", KeyCode.Delete);
        ShortcutManager.Register("Scene/Duplicate", "Duplicate Selected", KeyCode.D, ctrl: true);
        ShortcutManager.Register("Scene/Copy", "Copy Selected", KeyCode.C, ctrl: true);
        ShortcutManager.Register("Scene/Paste", "Paste", KeyCode.V, ctrl: true);
        ShortcutManager.Register("Scene/ToolTranslate", "Translate Tool", KeyCode.W);
        ShortcutManager.Register("Scene/ToolRotate", "Rotate Tool", KeyCode.E);
        ShortcutManager.Register("Scene/ToolScale", "Scale Tool", KeyCode.R);
        ShortcutManager.Register("Scene/ToolUniversal", "Universal Tool", KeyCode.T);

        // Hierarchy
        ShortcutManager.Register("Hierarchy/Delete", "Delete Selected", KeyCode.Delete);
        ShortcutManager.Register("Hierarchy/Duplicate", "Duplicate Selected", KeyCode.D, ctrl: true);
        ShortcutManager.Register("Hierarchy/Copy", "Copy Selected", KeyCode.C, ctrl: true);
        ShortcutManager.Register("Hierarchy/Paste", "Paste", KeyCode.V, ctrl: true);
        ShortcutManager.Register("Hierarchy/Rename", "Rename Selected", KeyCode.F2);

        // Project
        ShortcutManager.Register("Project/Delete", "Delete Selected", KeyCode.Delete);
        ShortcutManager.Register("Project/Rename", "Rename Selected", KeyCode.F2);

        // Graph Editor
        ShortcutManager.Register("GraphEditor/Save", "Save Graph", KeyCode.S, ctrl: true);
        ShortcutManager.Register("GraphEditor/Delete", "Delete Selected", KeyCode.Delete);
        ShortcutManager.Register("GraphEditor/Duplicate", "Duplicate Selected", KeyCode.D, ctrl: true);
        ShortcutManager.Register("GraphEditor/Copy", "Copy Selected", KeyCode.C, ctrl: true);
        ShortcutManager.Register("GraphEditor/Paste", "Paste", KeyCode.V, ctrl: true);
        ShortcutManager.Register("GraphEditor/SelectAll", "Select All", KeyCode.A, ctrl: true);
        ShortcutManager.Register("GraphEditor/FrameSelection", "Frame Selection", KeyCode.F);
        ShortcutManager.Register("GraphEditor/GroupSelection", "Group Selected", KeyCode.G, ctrl: true);
    }
}
