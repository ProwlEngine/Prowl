// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using Prowl.Echo;
using Prowl.Editor.Widgets;
using Prowl.Runtime;

namespace Prowl.Editor;

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
    }

    private static readonly List<Entry> _entries = [];
    private static bool _initialized;

    public static IReadOnlyList<Entry> Entries => _entries;

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
    /// </summary>
    public static void BuildMenu(ContextMenuBuilder builder, string currentFolder, Action<string>? onCreated = null)
    {
        foreach (var entry in _entries)
        {
            var captured = entry;
            string icon = !string.IsNullOrEmpty(captured.Icon) ? captured.Icon : EditorIcons.FileCirclePlus;
            builder.Item($"{icon}  {captured.Name}", () =>
            {
                var path = CreateAsset(captured, currentFolder);
                if (path != null) onCreated?.Invoke(path);
            });
        }
    }

    /// <summary>
    /// Registers one menu item per asset type under "Assets/Create {Name}" in the main menu bar.
    /// </summary>
    public static void RegisterMenuBarItems()
    {
        foreach (var entry in _entries)
        {
            var captured = entry;
            MenuRegistry.Register($"Assets/Create {captured.Name}", () => CreateAsset(captured, AssetCreateMenu.GetCurrentFolder()));
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

        string name = AssetCreateMenu.FindUniqueName(absFolder, $"New {entry.Name}", entry.Extension);
        string filePath = Path.Combine(absFolder, name);

        try
        {
            var instance = Activator.CreateInstance(entry.Type);
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
