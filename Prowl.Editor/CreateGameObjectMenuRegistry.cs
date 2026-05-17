// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Reflection;

using Prowl.Editor.GUI;
using Prowl.OrigamiUI;
using Prowl.Runtime;

namespace Prowl.Editor;

/// <summary>
/// Marks a static method as a GameObject creation entry for the Hierarchy create menu
/// and the GameObject menu bar. The method must have signature: <c>static void Method(GameObject? parent)</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class CreateGameObjectMenuAttribute : Attribute
{
    /// <summary>Menu path using "/" for submenus (e.g. "3D Object/Cube").</summary>
    public string Path { get; }

    /// <summary>Optional icon string.</summary>
    public string Icon { get; set; } = "";

    /// <summary>Sort order. Lower values appear first.</summary>
    public int Order { get; set; } = 100;

    /// <summary>If true, a separator is inserted before this item.</summary>
    public bool Separator { get; set; } = false;

    public CreateGameObjectMenuAttribute(string path)
    {
        Path = path;
    }
}

/// <summary>
/// Discovers static methods decorated with <see cref="CreateGameObjectMenuAttribute"/>
/// and provides menu-building helpers for the Hierarchy panel and GameObject menu bar.
/// </summary>
public static class CreateGameObjectMenuRegistry
{
    internal struct MenuEntry
    {
        public string Path;
        public string Icon;
        public int Order;
        public bool Separator;
        public MethodInfo Method;
    }

    private static readonly List<MenuEntry> _entries = [];
    private static bool _initialized;

    public static void Reinitialize() { _initialized = false; Initialize(); }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        _entries.Clear();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var attr = method.GetCustomAttribute<CreateGameObjectMenuAttribute>();
                    if (attr == null) continue;

                    // Validate signature: must accept (GameObject?) parameter
                    var parms = method.GetParameters();
                    if (parms.Length != 1 || parms[0].ParameterType != typeof(GameObject))
                        continue;

                    _entries.Add(new MenuEntry
                    {
                        Path = attr.Path,
                        Icon = attr.Icon,
                        Order = attr.Order,
                        Separator = attr.Separator,
                        Method = method,
                    });
                }
            }
        }

        _entries.Sort((a, b) => a.Order.CompareTo(b.Order));
        Debug.Log($"CreateGameObjectMenuRegistry: {_entries.Count} creators registered.");
    }

    /// <summary>
    /// Build the create menu for a context menu builder (used in Hierarchy panel).
    /// Items are organized into submenus based on their path separators.
    /// </summary>
    public static void BuildMenu(ContextBuilder builder, GameObject? parent)
    {
        // Group entries by top-level path segment
        var topLevel = new List<MenuEntry>();
        var submenus = new Dictionary<string, List<MenuEntry>>(StringComparer.Ordinal);

        foreach (var entry in _entries)
        {
            int slashIdx = entry.Path.IndexOf('/');
            if (slashIdx < 0)
            {
                topLevel.Add(entry);
            }
            else
            {
                string category = entry.Path[..slashIdx];
                if (!submenus.TryGetValue(category, out var list))
                {
                    list = [];
                    submenus[category] = list;
                }
                list.Add(entry);
            }
        }

        // Render top-level items and submenus interleaved by order
        // We need a unified ordering, so build a combined list
        var combined = new List<(int order, bool isSeparator, string? submenuKey, MenuEntry? entry)>();

        foreach (var e in topLevel)
        {
            if (e.Separator) combined.Add((e.Order - 1, true, null, null));
            combined.Add((e.Order, false, null, e));
        }
        foreach (var (key, list) in submenus)
        {
            int minOrder = list[0].Order;
            // Check if first item has separator flag
            if (list[0].Separator) combined.Add((minOrder - 1, true, null, null));
            combined.Add((minOrder, false, key, null));
        }

        combined.Sort((a, b) => a.order.CompareTo(b.order));

        foreach (var (_, isSeparator, submenuKey, entry) in combined)
        {
            if (isSeparator)
            {
                builder.Separator();
            }
            else if (submenuKey != null)
            {
                var list = submenus[submenuKey];
                // Use icon from the first entry in the submenu, or default
                string submenuIcon = !string.IsNullOrEmpty(list[0].Icon) ? list[0].Icon : "";
                builder.Submenu(submenuKey, sub =>
                {
                    foreach (var subEntry in list)
                    {
                        var captured = subEntry;
                        string itemName = captured.Path[(captured.Path.IndexOf('/') + 1)..];
                        string icon = !string.IsNullOrEmpty(captured.Icon) ? captured.Icon : "";
                        sub.Item(itemName, () => captured.Method.Invoke(null, [parent]), icon: icon);
                    }
                }, icon: submenuIcon);
            }
            else if (entry.HasValue)
            {
                var e = entry.Value;
                string icon = !string.IsNullOrEmpty(e.Icon) ? e.Icon : "";
                builder.Item(e.Path, () => e.Method.Invoke(null, [parent]), icon: icon);
            }
        }
    }

    /// <summary>
    /// Register all entries under the "GameObject" top-level menu in the menu bar.
    /// </summary>
    public static void RegisterMenuBarItems()
    {
        foreach (var entry in _entries)
        {
            var captured = entry;
            MenuRegistry.Register($"GameObject/{captured.Path}", () => captured.Method.Invoke(null, [null]));
        }
    }
}
