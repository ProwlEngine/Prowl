// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;
using System.Text;

using Prowl.Editor.Theming;
using Prowl.Runtime;

namespace Prowl.Editor.Core;

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

    /// <summary>False for a shortcut with no key, which never fires.</summary>
    public bool IsBound => Key != KeyCode.Unknown;

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

    /// <summary>Global shortcuts fire anywhere in the editor. Every other category only fires in its own panel.</summary>
    public bool IsGlobal => Category == ShortcutManager.GlobalCategory;

    /// <summary>True when both can fire on the same key press: one of them is global, or both belong to the same panel.</summary>
    public bool SharesScopeWith(ShortcutDefinition other) => IsGlobal || other.IsGlobal || Category == other.Category;

    /// <summary> Initializes a new shortcut definition. Category is derived from the id path (the part before the last '/'), or "General" if there is no slash. </summary>
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
    /// <summary>The category of shortcuts that fire anywhere in the editor.</summary>
    public const string GlobalCategory = "Global";

    private static readonly Dictionary<string, ShortcutDefinition> _shortcuts = [];

    /// <summary>Goes up whenever a binding changes, so anything built from the bindings knows to rebuild.</summary>
    public static int Version { get; private set; }
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
        // Most Mac keyboards (laptops especially) have no dedicated forward-delete
        // key; the key labeled "delete" reports as Backspace. Default Delete-bound
        // shortcuts to Backspace on macOS so they work out of the box.
        if (key == KeyCode.Delete && RuntimeUtils.IsMac())
            key = KeyCode.Backspace;

        var def = new ShortcutDefinition(id, displayName, new ShortcutBinding(key, ctrl, shift, alt));

        // If overrides have been loaded and one exists for this id, apply it
        if (_overridesLoaded)
        {
            Dictionary<string, ShortcutBinding> overrides = EditorSettings.Instance.ShortcutOverrides;
            if (overrides.TryGetValue(id, out ShortcutBinding? ov))
                def.Override = ov;
        }

        _shortcuts[id] = def;
        Version++;
    }

    // ================================================================
    //  Querying
    // ================================================================

    /// <summary>
    /// Returns true if the shortcut was triggered this frame.
    /// Checks <see cref="Input.GetKeyDown"/> for the primary key and enforces
    /// exact modifier state. On macOS the "Ctrl" modifier is interpreted as Cmd
    /// (Super) so platform-native shortcuts like Cmd+S work without rebinding.
    /// </summary>
    public static bool IsPressed(string id)
    {
        if (IsRebinding) return false;

        // Don't fire shortcuts when Paper has keyboard focus (e.g. text field is active)
        if (EditorApplication.Instance?.PaperInstance?.WantsCaptureKeyboard == true) return false;

        if (!_shortcuts.TryGetValue(id, out ShortcutDefinition? def)) return false;

        ShortcutBinding b = def.Binding;
        if (!b.IsBound || !Input.GetKeyDown(b.Key)) return false;
        if (b.Ctrl != IsPlatformCtrlPressed()) return false;
        if (b.Shift != Input.IsShiftPressed) return false;
        if (b.Alt != Input.IsAltPressed) return false;
        return true;
    }

    /// <summary>Whether any shortcut other than the given ones was triggered this frame.</summary>
    public static bool AnyPressedExcept(params string[] excluded)
    {
        foreach (string id in _shortcuts.Keys)
            if (System.Array.IndexOf(excluded, id) < 0 && IsPressed(id)) return true;
        return false;
    }

    /// <summary>
    /// Like <see cref="IsPressed"/>, but also hides the key from gameplay until it's released. Shortcuts run
    /// in the GUI, after gameplay has updated, so without this the game reads the same press next frame.
    /// </summary>
    public static bool TryConsume(string id)
    {
        if (!IsPressed(id)) return false;
        GUI.GameViewInputHandler.SuppressKeyUntilRelease(_shortcuts[id].Binding.Key);
        return true;
    }

    /// <summary>
    /// The platform's "command" modifier Cmd (Super) on macOS, Ctrl everywhere else.
    /// All shortcut bindings declared with <c>ctrl: true</c> are routed through this.
    /// </summary>
    private static bool IsPlatformCtrlPressed()
    {
        if (RuntimeUtils.IsMac())
            return Input.GetKey(KeyCode.SuperLeft) || Input.GetKey(KeyCode.SuperRight);
        return Input.IsCtrlPressed;
    }

    /// <summary>The shortcut registered under an id, or null.</summary>
    public static ShortcutDefinition? Find(string id) => _shortcuts.TryGetValue(id, out ShortcutDefinition? def) ? def : null;

    /// <summary>Get the effective binding for a shortcut.</summary>
    public static ShortcutBinding? GetBinding(string id)
        => _shortcuts.TryGetValue(id, out ShortcutDefinition? def) ? def.Binding : null;

    /// <summary>Formats a tooltip using the shortcut's current binding.</summary>
    public static string GetTooltip(string label, string id)
        => GetBinding(id) is { } binding ? $"{label} ({GetDisplayString(binding)})" : label;

    /// <summary>Get the default binding for a shortcut.</summary>
    public static ShortcutBinding? GetDefault(string id)
        => _shortcuts.TryGetValue(id, out ShortcutDefinition? def) ? def.Default : null;

    /// <summary>Get all registered shortcut definitions.</summary>
    public static IEnumerable<ShortcutDefinition> GetAllShortcuts()
        => _shortcuts.Values.OrderBy(s => s.Category).ThenBy(s => s.DisplayName);

    // ================================================================
    //  Override Management
    // ================================================================

    /// <summary>Set a user override for a shortcut and persist to settings.</summary>
    public static void SetOverride(string id, ShortcutBinding binding)
    {
        if (!_shortcuts.TryGetValue(id, out ShortcutDefinition? def)) return;
        def.Override = binding;
        SaveOverrides();
    }

    /// <summary>Clear the user override for a shortcut (reverts to default) and persist.</summary>
    public static void ClearOverride(string id)
    {
        if (!_shortcuts.TryGetValue(id, out ShortcutDefinition? def)) return;
        def.Override = null;
        SaveOverrides();
    }

    /// <summary>Clear all user overrides and persist.</summary>
    public static void ClearAllOverrides()
    {
        foreach (ShortcutDefinition def in _shortcuts.Values)
            def.Override = null;
        SaveOverrides();
    }

    /// <summary>The shortcuts that would fire on the same key press as <paramref name="shortcut"/> bound to <paramref name="binding"/>.</summary>
    public static List<ShortcutDefinition> FindConflicts(ShortcutDefinition shortcut, ShortcutBinding binding)
    {
        var conflicts = new List<ShortcutDefinition>();
        foreach (ShortcutDefinition other in _shortcuts.Values)
            if (binding.IsBound && other != shortcut && other.Binding.Equals(binding) && shortcut.SharesScopeWith(other))
                conflicts.Add(other);
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
        ReloadOverrides();
    }

    /// <summary>Replaces every override with the ones in EditorSettings, after the settings were swapped out.</summary>
    public static void ReloadOverrides()
    {
        Dictionary<string, ShortcutBinding> overrides = EditorSettings.Instance.ShortcutOverrides;
        foreach ((string id, ShortcutDefinition def) in _shortcuts)
            def.Override = overrides.TryGetValue(id, out ShortcutBinding? binding) ? binding : null;
        Version++;
    }

    /// <summary>Save current overrides to EditorSettings.</summary>
    public static void SaveOverrides()
    {
        var overrides = new Dictionary<string, ShortcutBinding>();
        foreach ((string? id, ShortcutDefinition? def) in _shortcuts)
        {
            if (def.Override != null)
                overrides[id] = def.Override;
        }
        EditorSettings.Instance.ShortcutOverrides = overrides;
        EditorSettings.Instance.Save();
        Version++;
    }

    // ================================================================
    //  Display
    // ================================================================

    /// <summary>
    /// Returns a human-readable string for a binding, e.g. "Ctrl+Shift+S" or "Delete".
    /// </summary>
    public static string GetDisplayString(ShortcutBinding? binding)
    {
        if (binding == null || !binding.IsBound) return Rosetta.Loc.Get("pref.not_bound");

        bool isMac = RuntimeUtils.IsMac();
        var sb = new StringBuilder();
        if (binding.Ctrl) sb.Append(isMac ? "Cmd+" : "Ctrl+");
        if (binding.Shift) sb.Append("Shift+");
        if (binding.Alt) sb.Append(isMac ? "Option+" : "Alt+");
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
        ShortcutManager.Register("Global/Save", "Save All", KeyCode.S, ctrl: true);
        ShortcutManager.Register("Global/SaveAs", "Save Scene As", KeyCode.S, ctrl: true, shift: true);
        ShortcutManager.Register("Global/NewScene", "New Scene", KeyCode.N, ctrl: true);
        ShortcutManager.Register("Global/Undo", "Undo", KeyCode.Z, ctrl: true);
        ShortcutManager.Register("Global/Redo", "Redo", KeyCode.Y, ctrl: true);
        ShortcutManager.Register("Global/TogglePlay", "Play / Stop", KeyCode.F5);
        ShortcutManager.Register("Global/TogglePause", "Pause / Resume", KeyCode.F7);

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
        ShortcutManager.Register("Scene/ToggleMaximize", "Toggle Maximize", KeyCode.F3);

        // Game View
        ShortcutManager.Register("Game/ToggleMaximize", "Toggle Maximize", KeyCode.F3);

        // Hierarchy
        ShortcutManager.Register("Hierarchy/Delete", "Delete Selected", KeyCode.Delete);
        ShortcutManager.Register("Hierarchy/Duplicate", "Duplicate Selected", KeyCode.D, ctrl: true);
        ShortcutManager.Register("Hierarchy/Copy", "Copy Selected", KeyCode.C, ctrl: true);
        ShortcutManager.Register("Hierarchy/Paste", "Paste", KeyCode.V, ctrl: true);
        ShortcutManager.Register("Hierarchy/Rename", "Rename Selected", KeyCode.F2);
        ShortcutManager.Register("Hierarchy/CreateEmptyChild", "Create Empty Child", KeyCode.N, shift: true, alt: true);
        ShortcutManager.Register("Hierarchy/CreateEmptyParent", "Create Empty Parent", KeyCode.G, ctrl: true, shift: true);

        // Project
        ShortcutManager.Register("Project/Delete", "Delete Selected", KeyCode.Delete);
        ShortcutManager.Register("Project/Rename", "Rename Selected", KeyCode.F2);
        ShortcutManager.Register("Project/Duplicate", "Duplicate Selected", KeyCode.D, ctrl: true);
        ShortcutManager.Register("Project/Copy", "Copy Selected", KeyCode.C, ctrl: true);
        ShortcutManager.Register("Project/Cut", "Cut Selected", KeyCode.X, ctrl: true);
        ShortcutManager.Register("Project/Paste", "Paste", KeyCode.V, ctrl: true);
        ShortcutManager.Register("Project/CancelCut", "Cancel Cut", KeyCode.Escape);

        // Graph Editor
        ShortcutManager.Register("GraphEditor/Delete", "Delete Selected", KeyCode.Delete);
        ShortcutManager.Register("GraphEditor/Duplicate", "Duplicate Selected", KeyCode.D, ctrl: true);
        ShortcutManager.Register("GraphEditor/Copy", "Copy Selected", KeyCode.C, ctrl: true);
        ShortcutManager.Register("GraphEditor/Paste", "Paste", KeyCode.V, ctrl: true);
        ShortcutManager.Register("GraphEditor/SelectAll", "Select All", KeyCode.A, ctrl: true);
        ShortcutManager.Register("GraphEditor/FrameSelection", "Frame Selection", KeyCode.F);
        ShortcutManager.Register("GraphEditor/Recenter", "Recenter View", KeyCode.Space);
        ShortcutManager.Register("GraphEditor/GroupSelection", "Group Selected", KeyCode.G, ctrl: true);

        ShortcutManager.Register("CSG/ModeSelect", "Select Tool", KeyCode.Number1, alt: true);
        ShortcutManager.Register("CSG/ModeCreate", "Create Tool", KeyCode.Number2, alt: true);
        ShortcutManager.Register("CSG/ModeEdit", "Edit Tool", KeyCode.Number3, alt: true);
        ShortcutManager.Register("CSG/ModeUVMove", "UV Move Tool", KeyCode.Number4, alt: true);
        ShortcutManager.Register("CSG/ModeUVRotate", "UV Rotate Tool", KeyCode.Number5, alt: true);
        ShortcutManager.Register("CSG/ModeUVScale", "UV Scale Tool", KeyCode.Number6, alt: true);
        ShortcutManager.Register("CSG/ModePivot", "Pivot Tool", KeyCode.Number7, alt: true);
        ShortcutManager.Register("CSG/SetAdditive", "Set Additive", KeyCode.Equal);
        ShortcutManager.Register("CSG/SetSubtractive", "Set Subtractive", KeyCode.Minus);
        ShortcutManager.Register("CSG/SetIntersecting", "Set Intersecting", KeyCode.BackSlash);
        ShortcutManager.Register("CSG/SetPassThrough", "Set Pass Through", KeyCode.Slash);
        ShortcutManager.Register("CSG/SnapToGrid", "Snap Selection To Grid", KeyCode.End, ctrl: true);
        ShortcutManager.Register("MeshEdit/VertexMode", "Vertex Mode", KeyCode.Number1);
        ShortcutManager.Register("MeshEdit/EdgeMode", "Edge Mode", KeyCode.Number2);
        ShortcutManager.Register("MeshEdit/FaceMode", "Face Mode", KeyCode.Number3);
        ShortcutManager.Register("MeshEdit/SelectAll", "Select All Elements", KeyCode.A);
        ShortcutManager.Register("MeshEdit/SelectNone", "Select No Elements", KeyCode.A, alt: true);
        ShortcutManager.Register("MeshEdit/Invert", "Invert Element Selection", KeyCode.I, ctrl: true);
        ShortcutManager.Register("MeshEdit/More", "Select More", KeyCode.Equal, ctrl: true);
        ShortcutManager.Register("MeshEdit/Less", "Select Less", KeyCode.Minus, ctrl: true);
        ShortcutManager.Register("MeshEdit/Linked", "Select Linked", KeyCode.L, ctrl: true);
        ShortcutManager.Register("MeshEdit/Similar", "Select Similar", KeyCode.G, shift: true);
        ShortcutManager.Register("MeshEdit/XRay", "X Ray", KeyCode.Z, alt: true);
        ShortcutManager.Register("MeshEdit/Hide", "Hide Elements", KeyCode.H);
        ShortcutManager.Register("MeshEdit/HideUnselected", "Hide Unselected Elements", KeyCode.H, shift: true);
        ShortcutManager.Register("MeshEdit/Reveal", "Reveal Elements", KeyCode.H, alt: true);
        ShortcutManager.Register("MeshEdit/Frame", "Frame Selected Elements", KeyCode.Home);
        ShortcutManager.Register("MeshEdit/Fill", "Fill", KeyCode.F);
        ShortcutManager.Register("MeshEdit/Grab", "Grab", KeyCode.G);
        ShortcutManager.Register("MeshEdit/Extrude", "Extrude Region", KeyCode.E);
        ShortcutManager.Register("MeshEdit/Bevel", "Bevel", KeyCode.B, ctrl: true);
        ShortcutManager.Register("MeshEdit/LoopCut", "Loop Cut", KeyCode.R, ctrl: true);
        ShortcutManager.Register("MeshEdit/Knife", "Knife", KeyCode.K);
        ShortcutManager.Register("MeshEdit/SnapToBrushes", "Snap To Brushes", KeyCode.Tab, shift: true);
        ShortcutManager.Register("MeshEdit/Proportional", "Proportional Editing", KeyCode.O);
        ShortcutManager.Register("ModelEdit/Toggle", "Model Edit Mode", KeyCode.Tab);
        ShortcutManager.Register("ModelEdit/Join", "Join Model Objects", KeyCode.J, ctrl: true);
        ShortcutManager.Register("MeshEdit/Inset", "Inset Faces", KeyCode.I);
        ShortcutManager.Register("MeshEdit/Triangulate", "Triangulate Faces", KeyCode.T, ctrl: true);
        ShortcutManager.Register("MeshEdit/TrisToQuads", "Triangles To Quads", KeyCode.J, alt: true);
        ShortcutManager.Register("MeshEdit/Bridge", "Bridge Faces", KeyCode.E, ctrl: true);
        ShortcutManager.Register("MeshEdit/Symmetrize", "Symmetrize", KeyCode.M, ctrl: true, shift: true);
        ShortcutManager.Register("MeshEdit/MergeByDistance", "Merge By Distance", KeyCode.M, shift: true);
        ShortcutManager.Register("MeshEdit/Separate", "Separate", KeyCode.P);
        ShortcutManager.Register("MeshEdit/Smooth", "Smooth", KeyCode.S, shift: true);
        ShortcutManager.Register("MeshEdit/Dissolve", "Dissolve", KeyCode.X);
        ShortcutManager.Register("MeshEdit/LimitedDissolve", "Limited Dissolve", KeyCode.X, ctrl: true);
        ShortcutManager.Register("MeshEdit/DeleteFace", "Delete Face", KeyCode.Delete, ctrl: true);
        ShortcutManager.Register("MeshEdit/Merge", "Merge", KeyCode.M);
        ShortcutManager.Register("MeshEdit/Connect", "Connect Points", KeyCode.J);
        ShortcutManager.Register("MeshEdit/Flatten", "Flatten", KeyCode.F, shift: true);
        ShortcutManager.Register("MeshEdit/Rotate", "Rotate", KeyCode.R);
        ShortcutManager.Register("MeshEdit/Scale", "Scale", KeyCode.S);
        ShortcutManager.Register("CSG/FlipX", "Flip On X", KeyCode.X, ctrl: true, alt: true);
        ShortcutManager.Register("CSG/FlipY", "Flip On Y", KeyCode.Y, ctrl: true, alt: true);
        ShortcutManager.Register("CSG/FlipZ", "Flip On Z", KeyCode.Z, ctrl: true, alt: true);
        ShortcutManager.Register("CSG/RotateLeft", "Rotate Anticlockwise", KeyCode.Comma, ctrl: true);
        ShortcutManager.Register("CSG/RotateRight", "Rotate Clockwise", KeyCode.Period, ctrl: true);
        ShortcutManager.Register("CSG/Hide", "Hide Selected", KeyCode.H);
        ShortcutManager.Register("CSG/HideUnselected", "Hide Unselected", KeyCode.H, ctrl: true);
        ShortcutManager.Register("CSG/ToggleVisibility", "Toggle Visibility", KeyCode.H, shift: true);
        ShortcutManager.Register("CSG/UnhideAll", "Unhide All", KeyCode.U);
        ShortcutManager.Register("CSG/HalveGrid", "Halve Grid", KeyCode.LeftBracket);
        ShortcutManager.Register("CSG/DoubleGrid", "Double Grid", KeyCode.RightBracket);
        ShortcutManager.Register("CSG/NudgeLeft", "Nudge Left", KeyCode.Left, ctrl: true, shift: true);
        ShortcutManager.Register("CSG/NudgeRight", "Nudge Right", KeyCode.Right, ctrl: true, shift: true);
        ShortcutManager.Register("CSG/NudgeBack", "Nudge Back", KeyCode.Down, ctrl: true, shift: true);
        ShortcutManager.Register("CSG/NudgeForward", "Nudge Forward", KeyCode.Up, ctrl: true, shift: true);
        ShortcutManager.Register("CSG/NudgeDown", "Nudge Down", KeyCode.PageDown, ctrl: true, shift: true);
        ShortcutManager.Register("CSG/NudgeUp", "Nudge Up", KeyCode.PageUp, ctrl: true, shift: true);
        ShortcutManager.Register("CSG/Smear", "Paint Surface Look (hold)", KeyCode.G);
        ShortcutManager.Register("CSG/ClipSwapSide", "Swap Kept Side", KeyCode.Tab);
        ShortcutManager.Register("CSG/Confirm", "Confirm Cut or Shape", KeyCode.Enter);
        ShortcutManager.Register("CSG/RepeatClone", "Repeat Last Clone", KeyCode.D, ctrl: true, shift: true);
        ShortcutManager.Register("CSG/GroupSelection", "Group Selection", KeyCode.G, ctrl: true);
        ShortcutManager.Register("CSG/Earlier", "Move Earlier in CSG Order", KeyCode.LeftBracket, ctrl: true);
        ShortcutManager.Register("CSG/Later", "Move Later in CSG Order", KeyCode.RightBracket, ctrl: true);
        ShortcutManager.Register("CSG/ShapeBox", "Draw Box", KeyCode.B);
        ShortcutManager.Register("CSG/ShapeCylinder", "Draw Cylinder", KeyCode.C);
        ShortcutManager.Register("CSG/ShapeSphere", "Draw Sphere", KeyCode.K);
        ShortcutManager.Register("CSG/ShapeLinearStairs", "Draw Stairs", KeyCode.L);
        ShortcutManager.Register("CSG/ShapeSpiralStairs", "Draw Spiral Stairs", KeyCode.P);
        ShortcutManager.Register("CSG/ShapeRoom", "Draw Room", KeyCode.O);
        ShortcutManager.Register("CSG/ShapePolygon", "Draw Polygon", KeyCode.N);
    }
}
