using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Editor.Core;

public sealed class AppMenuItem
{
    public string Label;
    public string Icon = "";
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

public static class MenuRegistry
{
    private static readonly List<AppMenuItem> _rootMenus = new();

    public static IReadOnlyList<AppMenuItem> RootMenus => _rootMenus;

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

    public static void Clear() => _rootMenus.Clear();
}
