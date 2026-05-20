// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Concurrent;
using System.Reflection;

using Prowl.Editor.Theming;
using Prowl.Runtime;

namespace Prowl.Editor;

/// <summary>
/// Resolves the icon glyph for a component type by walking up the inheritance chain looking
/// for <see cref="ComponentIconAttribute"/>. Results are cached per-type so reflection only
/// runs once per component class. Falls back to a puzzle-piece glyph.
/// </summary>
public static class ComponentIconRegistry
{
    private static readonly ConcurrentDictionary<Type, string> _cache = new();

    public static string GetIcon(MonoBehaviour component) => GetIcon(component.GetType());

    public static string GetIcon(Type componentType)
        => _cache.GetOrAdd(componentType, Resolve);

    private static string Resolve(Type t)
    {
        for (var cur = t; cur != null && cur != typeof(object); cur = cur.BaseType)
        {
            var attr = cur.GetCustomAttribute<ComponentIconAttribute>(inherit: false);
            if (attr != null && !string.IsNullOrEmpty(attr.Icon)) return attr.Icon;
        }
        return EditorIcons.PuzzlePiece;
    }
}
