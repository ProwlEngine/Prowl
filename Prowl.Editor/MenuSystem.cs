using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Editor;

public class MenuItem
{
    public string Label;
    public Action? OnClick;
    public List<MenuItem> SubItems = new();
    public bool IsSeparator;
    public bool IsEnabled = true;
    public Func<bool>? IsCheckedFunc;

    public bool IsChecked => IsCheckedFunc?.Invoke() ?? false;
    public bool HasSubItems => SubItems.Count > 0;

    public MenuItem(string label, Action? onClick = null)
    {
        Label = label;
        OnClick = onClick;
    }

    public static MenuItem Separator() => new("") { IsSeparator = true };
}

/// <summary>
/// Central menu registry. Items are registered by path (e.g. "File/Save Scene").
/// The menu bar reads from this to build dropdowns.
/// </summary>
public static class MenuRegistry
{
    private static readonly List<MenuItem> _rootMenus = new();

    public static IReadOnlyList<MenuItem> RootMenus => _rootMenus;

    /// <summary>
    /// Register a menu item at the given path.
    /// Path segments are separated by "/", e.g. "File/Save Scene" or "Window/General/Scene".
    /// </summary>
    public static void Register(string path, Action onClick, bool enabled = true, Func<bool>? isChecked = null)
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
                }
                else
                {
                    current.Add(new MenuItem(seg, onClick) { IsEnabled = enabled, IsCheckedFunc = isChecked });
                }
            }
            else
            {
                if (existing == null)
                {
                    existing = new MenuItem(seg);
                    current.Add(existing);
                }
                current = existing.SubItems;
            }
        }
    }

    /// <summary>
    /// Register a separator after the last item in the given parent path.
    /// </summary>
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

        current.Add(MenuItem.Separator());
    }

    public static void Clear() => _rootMenus.Clear();
}
