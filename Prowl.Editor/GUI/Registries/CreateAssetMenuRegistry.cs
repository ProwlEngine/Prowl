// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using Prowl.Echo;
using Prowl.Editor.Core;
using Prowl.Editor.Core.Tasks;
using Prowl.Editor.Theming;
using Prowl.OrigamiUI;
using Prowl.Rosetta;
using Prowl.Runtime;

namespace Prowl.Editor.GUI.Registries;

/// <summary>
/// Discovers all <see cref="EngineObject"/> subclasses decorated with
/// <see cref="CreateAssetMenuAttribute"/> and provides menu-building helpers
/// for the Assets menu and Project panel context menu.
/// </summary>
public static class CreateAssetMenuRegistry
{
    public struct Entry
    {
        public Type Type;
        public string Name;
        public string Extension;
        public string Icon;
        public int Order;
        /// <summary>Optional factory when set, used in place of Activator.CreateInstance.
        /// Lets manually-registered templates seed initial state on creation.</summary>
        public Func<EngineObject>? Factory;
    }

    private static readonly List<Entry> _entries = [];
    private static bool _initialized;

    /// <summary>Register a manual menu entry that uses a custom factory. Call from
    /// editor startup (e.g. after the registry's auto-scan completes). Useful when one
    /// asset class needs multiple menu entries with different starter content
    /// (Shader Graph / Surface vs Shader Graph / Image Effect, etc.).</summary>
    public static void AddManualEntry(string name, string extension, string icon, int order, Type type, Func<EngineObject> factory)
    {
        _entries.Add(new Entry
        {
            Type = type, Name = name, Extension = extension,
            Icon = icon, Order = order, Factory = factory,
        });
        _entries.Sort((a, b) => a.Order.CompareTo(b.Order));
    }

    /// <summary>Remove every manual entry whose Name starts with <paramref name="prefix"/>.
    /// Used by template registrars to clear their own entries before re-adding (so
    /// re-registration after script reload doesn't stack duplicates).</summary>
    public static void RemoveManualEntriesByPrefix(string prefix)
    {
        _entries.RemoveAll(e => e.Factory != null && e.Name.StartsWith(prefix));
    }

    public static IReadOnlyList<Entry> Entries => _entries;

    [Runtime.OnAssemblyLoad]
    public static void Reinitialize() { _initialized = false; Initialize(); }

    /// <summary>
    /// Drop ALL cached entries (including manual factory entries, which hold a user-supplied
    /// <see cref="Type"/> and factory delegate) so the script AssemblyLoadContext can be
    /// collected. Manual entries are re-registered after reload by their owners (e.g.
    /// <c>ShaderTypeCreateMenu.Register()</c>).
    /// </summary>
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

        // Preserve manual factory entries they're registered separately (e.g.
        // shader-graph templates) and shouldn't be wiped by a re-scan of attribute
        // entries on script reload.
        var manuals = _entries.FindAll(e => e.Factory != null);
        _entries.Clear();
        _entries.AddRange(manuals);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (type.IsAbstract || !typeof(EngineObject).IsAssignableFrom(type)) continue;
                var attr = type.GetCustomAttribute<CreateAssetMenuAttribute>();
                if (attr == null) continue;

                _entries.Add(new Entry
                {
                    Type = type,
                    Name = attr.Name,
                    Extension = attr.Extension,
                    Icon = attr.Icon,
                    Order = attr.Order,
                });
            }
        }

        _entries.Sort((a, b) => a.Order.CompareTo(b.Order));
        Debug.Log($"CreateAssetMenuRegistry: {_entries.Count} asset types registered.");
    }

    /// <summary>
    /// Appends one menu item per registered asset type to the given context menu builder.
    /// Names containing '/' are split into nested submenus (e.g. "Shader Graph/Surface"
    /// produces a "Shader Graph" submenu with a "Surface" item inside).
    /// </summary>
    public static void BuildMenu(ContextBuilder builder, string currentFolder, Action<string>? onCreated = null)
    {
        BuildMenuRecursive(builder, _entries, "", currentFolder, onCreated);
    }

    private static void BuildMenuRecursive(ContextBuilder builder,
        IEnumerable<Entry> entries, string prefix, string currentFolder,
        Action<string>? onCreated)
    {
        // Group entries by their first path segment after the current prefix.
        // Leaves (no further '/') become Item; branches recurse into Submenu.
        var leaves = new List<Entry>();
        var branches = new Dictionary<string, List<Entry>>();
        foreach (var e in entries)
        {
            var rest = e.Name.Substring(prefix.Length);
            int slash = rest.IndexOf('/');
            if (slash < 0) { leaves.Add(e); continue; }
            var head = rest.Substring(0, slash);
            if (!branches.TryGetValue(head, out var list))
                branches[head] = list = new List<Entry>();
            list.Add(e);
        }

        foreach (var leaf in leaves)
        {
            var captured = leaf;
            string display = captured.Name.Substring(prefix.Length);
            string icon = !string.IsNullOrEmpty(captured.Icon) ? captured.Icon : EditorIcons.FileCirclePlus;
            builder.Item($"{icon}  {display}", () =>
            {
                var task = new Core.Tasks.CreateAssetTask();

                task.TaskType = CreateAssetTask.AssetType.Asset;
                task.BeginCreateTask(captured, currentFolder);

            });
        }
        foreach (var (head, list) in branches)
        {
            // Capture for closure submenu builder runs lazily.
            var subPrefix = prefix + head + "/";
            var subList = list;
            builder.Submenu(head, sub => BuildMenuRecursive(sub, subList, subPrefix, currentFolder, onCreated));
        }
    }

    /// <summary>
    /// Registers one menu item per asset type under "Assets/Create {Name}" in the main menu bar.
    /// </summary>
    public static void RegisterMenuBarItems()
    {
        string assets = Loc.Get("menu.assets");
        foreach (var entry in _entries)
        {
            var captured = entry;
            MenuRegistry.Register($"{assets}/Create {captured.Name}", () => CreateAsset(captured, AssetCreateMenu.GetCurrentFolder()));
        }
    }

    /// <summary>
    /// Create an asset of a specific type on disk. Used programmatically (e.g. Terrain auto-setup).
    /// Returns the relative path on success, null on failure.
    /// </summary>
    public static string? CreateAssetByType<T>(string relativeFolder, string baseName, string extension) where T : new()
    {
        string absFolder = AssetCreateMenu.GetAbsoluteFolder(relativeFolder);
        if (!Directory.Exists(absFolder)) return null;

        string name = AssetCreateMenu.FindUniqueName(absFolder, baseName, extension);
        string filePath = Path.Combine(absFolder, name);

        try
        {
            var instance = new T();
            var echo = Serializer.Serialize(typeof(object), instance);
            if (echo != null)
                File.WriteAllText(filePath, echo.WriteToString());

            Debug.Log($"Created {typeof(T).Name}: {name}");
            return string.IsNullOrEmpty(relativeFolder) ? name : relativeFolder + "/" + name;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to create {typeof(T).Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Create an asset file on disk for the given registry entry.
    /// Returns the relative path on success, null on failure.
    /// </summary>
    private static string? CreateAsset(Entry entry, string relativeFolder)
    {
        string absFolder = AssetCreateMenu.GetAbsoluteFolder(relativeFolder);
        if (!Directory.Exists(absFolder)) return null;

        // Submenu entries (Name = "Shader Graph/Surface") must use only the LAST
        // segment as the file basename otherwise the menu hierarchy bleeds into
        // the file path and Path.Combine creates an "intermediate folder/file" that
        // fails. The full Name is still used for the menu label.
        int lastSlash = entry.Name.LastIndexOf('/');
        string baseName = lastSlash >= 0 ? entry.Name.Substring(lastSlash + 1) : entry.Name;

        string name = AssetCreateMenu.FindUniqueName(absFolder, $"New {baseName}", entry.Extension);
        string filePath = Path.Combine(absFolder, name);

        try
        {
            // Manual factory wins over Activator lets template entries seed initial state.
            object? instance = entry.Factory != null ? entry.Factory() : Activator.CreateInstance(entry.Type);
            var echo = Serializer.Serialize(typeof(object), instance);
            if (echo != null)
                File.WriteAllText(filePath, echo.WriteToString());

            Debug.Log($"Created {entry.Name}: {name}");
            return string.IsNullOrEmpty(relativeFolder) ? name : relativeFolder + "/" + name;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to create {entry.Name}: {ex.Message}");
            return null;
        }
    }
}
