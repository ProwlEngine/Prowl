// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.Editor.Core;
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

    [Runtime.OnAssemblyLoad]
    public static void Reinitialize() { _initialized = false; Initialize(); }

    /// <summary>Drop cached menu entries (which capture user <see cref="Type"/>s) so the script AssemblyLoadContext can be collected.</summary>
    [Runtime.OnAssemblyUnload]
    public static void ClearCache()
    {
        _initialized = false;
        _entries.Clear();
    }

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
    /// Items are organized into submenus based on their path separators, recursing to
    /// arbitrary depth (e.g. "Effects/Fog/Global" becomes an "Effects" submenu containing
    /// a "Fog" submenu containing a "Global" item).
    /// </summary>
    public static void BuildMenu(ContextBuilder builder, GameObject? parent)
    {
        BuildLevel(builder, parent, _entries, 0, suppressLeadingSeparator: false);
    }

    /// <summary>
    /// Renders one menu level: entries whose path (past <paramref name="consumedLength"/>) has no
    /// further "/" become items here, entries that do get grouped into a submenu per next segment
    /// and rendered recursively. A separator flag is resolved once, at the shallowest level where
    /// its entry is distinguishable as its group's first item (so it lands right before the
    /// submenu that entry lives in, not inside every level of that submenu chain too); deeper
    /// recursion for that same leading entry is told to suppress the flag via
    /// <paramref name="suppressLeadingSeparator"/> so it isn't rendered again lower down.
    /// </summary>
    private static void BuildLevel(ContextBuilder builder, GameObject? parent, List<MenuEntry> levelEntries, int consumedLength, bool suppressLeadingSeparator)
    {
        var leaves = new List<MenuEntry>();
        var categoryOrder = new List<string>();
        var categories = new Dictionary<string, List<MenuEntry>>(StringComparer.Ordinal);

        foreach (var entry in levelEntries)
        {
            string rel = entry.Path[consumedLength..];
            int slashIdx = rel.IndexOf('/');
            if (slashIdx < 0)
            {
                leaves.Add(entry);
            }
            else
            {
                string category = rel[..slashIdx];
                if (!categories.TryGetValue(category, out var list))
                {
                    list = [];
                    categories[category] = list;
                    categoryOrder.Add(category);
                }
                list.Add(entry);
            }
        }

        // Unified ordering of this level's leaves and submenu categories. Entries arrive here
        // pre-sorted by Order (from Initialize), so each category's list[0] is its lowest-order child.
        var items = new List<(int order, MenuEntry? leaf, string? category)>();
        foreach (var leaf in leaves) items.Add((leaf.Order, leaf, null));
        foreach (var category in categoryOrder) items.Add((categories[category][0].Order, null, category));

        bool isFirstItem = true;
        foreach (var (_, leaf, category) in items.OrderBy(i => i.order))
        {
            bool skipSeparator = isFirstItem && suppressLeadingSeparator;
            isFirstItem = false;

            if (leaf.HasValue)
            {
                var e = leaf.Value;
                if (!skipSeparator && e.Separator) builder.Separator();
                string itemName = e.Path[consumedLength..];
                string icon = !string.IsNullOrEmpty(e.Icon) ? e.Icon : "";
                builder.Item(itemName, () => e.Method.Invoke(null, [parent]), icon: icon);
            }
            else if (category != null)
            {
                var list = categories[category];
                bool hoistSeparator = !skipSeparator && list[0].Separator;
                if (hoistSeparator) builder.Separator();
                string icon = !string.IsNullOrEmpty(list[0].Icon) ? list[0].Icon : "";
                int nextConsumed = consumedLength + category.Length + 1;
                bool childSuppress = skipSeparator || hoistSeparator;
                builder.Submenu(category, sub => BuildLevel(sub, parent, list, nextConsumed, childSuppress), icon);
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
