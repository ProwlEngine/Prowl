using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Editor.Core;

/// <summary> Represents a single item in a hierarchical menu system, with support for labels, icons, click actions, dynamic state, and nested sub-items. </summary>
public sealed class AppMenuItem
{
    /// <summary> The display text of the menu item. </summary>
    public string Label;
    /// <summary> The icon identifier for the menu item. </summary>
    public string Icon = "";
    /// <summary> The action to invoke when the menu item is clicked. </summary>
    public Action? OnClick;
    /// <summary> Whether the menu item is enabled. Defaults to true. </summary>
    public bool IsEnabled = true;
    /// <summary> A function that returns whether the menu item should show a check mark. </summary>
    public Func<bool>? IsCheckedFunc;
    /// <summary> A function that dynamically determines whether the menu item is enabled. </summary>
    public Func<bool>? IsEnabledFunc;
    /// <summary> A function that dynamically provides the label text for the menu item. </summary>
    public Func<string>? DynamicLabelFunc;
    /// <summary> Whether this item is a visual separator rather than a clickable menu item. </summary>
    public bool IsSeparator;
    /// <summary> The child menu items nested under this item. </summary>
    public readonly List<AppMenuItem> SubItems = new();

    /// <summary> Initializes a new instance of AppMenuItem with the given label and click action. </summary>
    public AppMenuItem(string label = "", Action? onClick = null)
    {
        Label = label;
        OnClick = onClick;
    }

    /// <summary> Whether this menu item has any sub-items. </summary>
    public bool HasSubItems => SubItems.Count > 0;

    /// <summary> Creates a menu item that renders as a visual separator. </summary>
    public static AppMenuItem Separator() => new() { IsSeparator = true };
}

/// <summary> Provides methods to register, organize, and clear a hierarchical menu system built from AppMenuItem nodes. </summary>
public static class MenuRegistry
{
    private static readonly List<AppMenuItem> _rootMenus = new();

    /// <summary> The top-level menu items registered in the system. </summary>
    public static IReadOnlyList<AppMenuItem> RootMenus => _rootMenus;

    /// <summary> Registers a menu item at the specified slash-delimited path. Creates intermediate nodes as needed. If the path already exists, updates its properties with the provided values. </summary>
    public static void Register(string path, Action onClick, bool enabled = true, Func<bool>? isChecked = null,
        Func<bool>? isEnabled = null, Func<string>? dynamicLabel = null, string icon = "")
    {
        var segments = path.Split('/');
        var current = _rootMenus;

        for (int i = 0; i < segments.Length; i++)
        {
            string seg = segments[i];
            bool isLast = i == segments.Length - 1;

            var existing = current.FirstOrDefault(m => m.Label == seg && !m.IsSeparator);

            if (isLast)
            {
                if (existing != null)
                {
                    existing.OnClick = onClick;
                    existing.IsEnabled = enabled;
                    existing.IsCheckedFunc = isChecked;
                    existing.IsEnabledFunc = isEnabled;
                    existing.DynamicLabelFunc = dynamicLabel;
                    existing.Icon = icon;
                }
                else
                {
                    current.Add(new AppMenuItem(seg, onClick)
                    {
                        IsEnabled = enabled,
                        IsCheckedFunc = isChecked,
                        IsEnabledFunc = isEnabled,
                        DynamicLabelFunc = dynamicLabel,
                        Icon = icon,
                    });
                }
            }
            else
            {
                if (existing == null)
                {
                    existing = new AppMenuItem(seg);
                    current.Add(existing);
                }
                current = existing.SubItems;
            }
        }
    }

    /// <summary> Adds a visual separator to the sub-menu at the specified parent path. Does nothing if the parent path does not exist. </summary>
    public static void RegisterSeparator(string parentPath)
    {
        var segments = parentPath.Split('/');
        var current = _rootMenus;

        foreach (var seg in segments)
        {
            var existing = current.FirstOrDefault(m => m.Label == seg && !m.IsSeparator);
            if (existing == null) return;
            current = existing.SubItems;
        }

        current.Add(AppMenuItem.Separator());
    }

    /// <summary> Sets the icon on the menu item at the specified path. Does nothing if the path does not exist or the icon is null or empty. </summary>
    public static void RegisterBranchIcon(string path, string icon)
    {
        if (string.IsNullOrEmpty(icon)) return;
        var segments = path.Split('/');
        var current = _rootMenus;

        for (int i = 0; i < segments.Length - 1; i++)
        {
            var node = current.FirstOrDefault(m => m.Label == segments[i] && !m.IsSeparator);
            if (node == null) return;
            current = node.SubItems;
        }

        var target = current.FirstOrDefault(m => m.Label == segments[segments.Length - 1] && !m.IsSeparator);
        if (target != null) target.Icon = icon;
    }

    /// <summary> Removes all registered menu items. </summary>
    public static void Clear() => _rootMenus.Clear();
}
