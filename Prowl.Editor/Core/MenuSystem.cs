using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Editor.Core;

/// <summary>
/// A node in the editor's menu tree: a clickable item, a submenu (when it has children) or a
/// separator. The menu bar walks this tree and translates it into Origami's ContextBuilder API.
/// </summary>
public sealed class AppMenuItem
{
    public string Label;
    public Action? OnClick;
    public bool IsEnabled = true;
    public Func<bool>? IsCheckedFunc;
    public Func<bool>? IsEnabledFunc;
    public Func<string>? DynamicLabelFunc;
    public bool IsSeparator;
    public readonly List<AppMenuItem> SubItems = new();

    public AppMenuItem(string label = "", Action? onClick = null)
    {
        Label = label;
        OnClick = onClick;
    }

    public bool HasSubItems => SubItems.Count > 0;

    public static AppMenuItem Separator() => new() { IsSeparator = true };
}

/// <summary>
/// Central menu registry. Items are registered by path (e.g. "File/Save Scene").
/// The menu bar reads from this to build dropdowns.
/// </summary>
public static class MenuRegistry
{
    private static readonly List<AppMenuItem> _rootMenus = new();

    public static IReadOnlyList<AppMenuItem> RootMenus => _rootMenus;

    /// <summary>
    /// Register a menu item at the given path.
    /// Path segments are separated by "/", e.g. "File/Save Scene" or "Window/General/Scene".
    /// </summary>
    public static void Register(string path, Action onClick, bool enabled = true, Func<bool>? isChecked = null,
        Func<bool>? isEnabled = null, Func<string>? dynamicLabel = null)
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
                }
                else
                {
                    current.Add(new AppMenuItem(seg, onClick)
                    {
                        IsEnabled = enabled,
                        IsCheckedFunc = isChecked,
                        IsEnabledFunc = isEnabled,
                        DynamicLabelFunc = dynamicLabel,
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

        current.Add(AppMenuItem.Separator());
    }

    public static void Clear() => _rootMenus.Clear();
}
