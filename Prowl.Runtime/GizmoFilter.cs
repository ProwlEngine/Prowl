// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Ember;

namespace Prowl.Runtime;

/// <summary>
/// Which component types draw their gizmos. Every type draws by default; the editor turns individual types off.
/// Types are kept by full name so a choice survives a script reload and applies in every project that has the type.
/// </summary>
public static class GizmoFilter
{
    private static readonly HashSet<string> s_disabled = new(StringComparer.Ordinal);
    private static readonly ReloadCache<Type, bool> s_enabled = new(type => !s_disabled.Contains(Key(type)));

    /// <summary>The full names of the types that are turned off.</summary>
    public static IReadOnlyCollection<string> Disabled => s_disabled;

    /// <summary>The name a type is kept under.</summary>
    public static string Key(Type type) => type.FullName ?? type.Name;

    /// <summary>Whether components of <paramref name="type"/> draw their gizmos.</summary>
    public static bool IsEnabled(Type type) => s_disabled.Count == 0 || s_enabled[type];

    /// <summary>Replaces every choice with this set of turned off type names.</summary>
    public static void SetDisabled(IEnumerable<string> fullNames)
    {
        var next = new HashSet<string>(fullNames, StringComparer.Ordinal);
        if (next.SetEquals(s_disabled)) return;
        s_disabled.Clear();
        s_disabled.UnionWith(next);
        s_enabled.Reset();
    }
}
