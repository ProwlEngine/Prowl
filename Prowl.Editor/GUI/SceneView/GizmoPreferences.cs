// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.Editor.Theming;
using Prowl.Editor.Utils;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Rosetta;
using Prowl.Runtime;

namespace Prowl.Editor.GUI.SceneView;

/// <summary>
/// Which component types show their gizmos in the Scene view. Saved per user, since what to see depends on the task
/// at hand; a team can still share a starting set through a project override.
/// </summary>
[EditorPreferences("Gizmos", EditorIcons.Shapes, order: 22, ProjectOverridable = true)]
public sealed class GizmoPreferences : EditorPreferences
{
    /// <summary>Full names of the component types whose gizmos are hidden. Every other type shows its gizmos.</summary>
    [HideInInspector]
    public List<string> DisabledGizmos = [];

    public override void Apply() => GizmoFilter.SetDisabled(DisabledGizmos);

    public override void OnGUI(Paper paper, float width)
    {
        string label = DisabledGizmos.Count == 0
            ? Loc.Get("scene.gizmos.none_hidden")
            : Loc.Get("scene.gizmos.hidden_count", new { count = DisabledGizmos.Count });
        Origami.Button(paper, "gizmo_prefs_show_all", label, () => SetAll(true)).FullWidth().Height(22).Show();
    }

    /// <summary>The live preferences, or null before the registry has been scanned.</summary>
    private static GizmoPreferences? Live
    {
        get
        {
            try { return Preferences.Get<GizmoPreferences>(); }
            catch (InvalidOperationException) { return null; }
        }
    }

    /// <summary>Shows or hides one type's gizmos and saves the choice.</summary>
    public static void SetEnabled(Type type, bool enabled) => SetEnabled([type], enabled);

    /// <summary>Shows or hides several types' gizmos at once, saved as one change.</summary>
    public static void SetEnabled(IEnumerable<Type> types, bool enabled)
    {
        if (Live is not { } prefs) return;
        bool changed = false;
        foreach (Type type in types)
        {
            string key = GizmoFilter.Key(type);
            changed |= enabled ? prefs.DisabledGizmos.Remove(key) : AddOnce(prefs.DisabledGizmos, key);
        }
        if (changed) Preferences.Changed(Preferences.SectionOf(prefs));

        static bool AddOnce(List<string> list, string key)
        {
            if (list.Contains(key)) return false;
            list.Add(key);
            return true;
        }
    }

    /// <summary>Shows or hides the gizmos of every type.</summary>
    public static void SetAll(bool enabled)
    {
        if (enabled)
        {
            if (Live is not { } prefs || prefs.DisabledGizmos.Count == 0) return;
            prefs.DisabledGizmos.Clear();
            Preferences.Changed(Preferences.SectionOf(prefs));
        }
        else
        {
            SetEnabled(GizmoTypes.All.Select(t => t.Type), false);
        }
    }
}

/// <summary>A component type that draws gizmos, as listed in the Scene view's gizmo menu.</summary>
public sealed record GizmoType(Type Type, string Name, string Category, string Icon);

/// <summary>Every component type that draws gizmos, grouped the way the Add Component menu groups them.</summary>
public static class GizmoTypes
{
    private static List<GizmoType>? s_all;

    /// <summary>The types, sorted by category then name.</summary>
    public static IReadOnlyList<GizmoType> All => s_all ??= Gather();

    /// <summary>Drops the list so the next read scans the reloaded scripts.</summary>
    public static void ClearCache() => s_all = null;

    /// <summary>The category types without an Add Component path fall under.</summary>
    public const string ScriptsCategory = "Scripts";

    private static List<GizmoType> Gather()
    {
        var result = new List<GizmoType>();
        foreach (Type type in EditorUtils.GetAllTypes())
        {
            if (!typeof(Component).IsAssignableFrom(type) || type.IsAbstract || type == typeof(Component)) continue;
            if (!DrawsGizmos(type)) continue;

            AddComponentMenuAttribute? menu = type.GetCustomAttribute<AddComponentMenuAttribute>();
            string path = menu?.Path ?? type.Name;
            int slash = path.LastIndexOf('/');
            string name = slash >= 0 ? path[(slash + 1)..] : path;
            string category = slash >= 0 ? path[..slash] : ScriptsCategory;
            string icon = type.GetCustomAttribute<ComponentIconAttribute>()?.Icon is { Length: > 0 } glyph ? glyph
                : menu?.Icon is { Length: > 0 } menuIcon ? menuIcon : EditorIcons.PuzzlePiece;

            result.Add(new GizmoType(type, name, category, icon));
        }

        result.Sort((a, b) =>
        {
            int c = string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return result;
    }

    private static bool DrawsGizmos(Type type)
        => RuntimeUtils.OverridesVirtual(type, nameof(Component.DrawGizmos), typeof(Component))
        || RuntimeUtils.OverridesVirtual(type, nameof(Component.DrawGizmosSelected), typeof(Component));
}
