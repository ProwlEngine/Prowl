using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Prowl.Editor.Core;
using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor;

public static class MenuContext
{
    public static GameObject? ActiveGameObject { get; private set; }
    public static void Set(GameObject? go) => ActiveGameObject = go;
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class MenuItemAttribute : Attribute
{
    public string Path { get; }
    public bool IsValidate { get; }
    public int Priority { get; }
    public string Icon { get; init; } = "";
    public bool Separator { get; init; } = false;

    public MenuItemAttribute(string path, bool isValidate = false, int priority = 1000)
    {
        Path = path;
        IsValidate = isValidate;
        Priority = priority;
    }

    private sealed record Entry(string Path, Action Action, int Priority, string Icon, Type DeclaringType, bool Separator = false);

    private static readonly List<Entry> _entries = [];
    private static readonly Dictionary<string, Func<bool>> _validators = new(StringComparer.Ordinal);
    private static bool _dirty;
    private static List<Entry>? _sorted;

    internal static void Scan(MethodInfo method)
    {
        foreach (var attr in method.GetCustomAttributes<MenuItemAttribute>())
        {
            if (attr.IsValidate)
            {
                if (method.ReturnType != typeof(bool) || method.GetParameters().Length != 0) continue;
                try { _validators[attr.Path] = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), method); }
                catch { }
            }
            else
            {
                if (method.ReturnType != typeof(void) || method.GetParameters().Length != 0) continue;
                try
                {
                    var del = (Action)Delegate.CreateDelegate(typeof(Action), method);
                    _entries.Add(new Entry(attr.Path, del, attr.Priority, attr.Icon, method.DeclaringType!, attr.Separator));
                    _dirty = true;
                }
                catch { }
            }
        }
    }

    public static void Register(string path, Action action, int priority = 1000, string icon = "")
    {
        _entries.Add(new Entry(path, action, priority, icon, typeof(MenuItemAttribute)));
        _dirty = true;
    }

    public static void UnregisterByPrefix(string prefix)
    {
        if (_entries.RemoveAll(e => e.Path.StartsWith(prefix, StringComparison.Ordinal)) > 0)
            _dirty = true;
    }

    internal static void Clear()
    {
        _entries.Clear();
        _validators.Clear();
        _sorted = null;
        _dirty = false;
    }

    private static List<Entry> GetSorted()
    {
        if (_dirty || _sorted == null)
        {
            _sorted = [.. _entries.OrderBy(e => e.Priority)];
            _dirty = false;
        }
        return _sorted;
    }

    public static void PopulateMenuRegistry()
    {
        PopulateRegistryLevel(GetSorted(), "");
    }

    private static void PopulateRegistryLevel(List<Entry> all, string levelPath)
    {
        string prefix = levelPath.Length > 0 ? levelPath + "/" : "";

        var leaves = new List<Entry>();
        var branchOrder = new List<string>();
        var branches = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);

        foreach (var e in all)
        {
            if (!e.Path.StartsWith(prefix, StringComparison.Ordinal)) continue;
            string rest = e.Path.Substring(prefix.Length);
            int slash = rest.IndexOf('/');
            if (slash < 0)
                leaves.Add(e);
            else
            {
                string key = rest.Substring(0, slash);
                if (!branches.ContainsKey(key)) { branches[key] = []; branchOrder.Add(key); }
                branches[key].Add(e);
            }
        }

        var items = new List<(int Priority, Entry? Leaf, string? Branch)>();
        foreach (var leaf in leaves) items.Add((leaf.Priority, leaf, null));
        foreach (var key in branchOrder) items.Add((branches[key].Min(e => e.Priority), null, key));
        items.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        for (int i = 0; i < items.Count; i++)
        {
            var (_, leaf, branch) = items[i];

            bool wantsSep = leaf?.Separator ?? false;
            if (branch != null)
                wantsSep = branches[branch].OrderBy(e => e.Priority).First().Separator;

            if (i > 0 && wantsSep && levelPath.Length > 0)
                MenuRegistry.RegisterSeparator(levelPath);

            if (leaf != null)
            {
                var validator = _validators.TryGetValue(leaf.Path, out var v) ? v : null;
                Func<bool>? isChecked = null;
                if (typeof(DockPanel).IsAssignableFrom(leaf.DeclaringType))
                {
                    var t = leaf.DeclaringType;
                    isChecked = () => EditorApplication.Instance?.IsPanelOpen(t) ?? false;
                }
                MenuRegistry.Register(leaf.Path, leaf.Action, isChecked: isChecked, isEnabled: validator, icon: leaf.Icon);
            }
            else if (branch != null)
            {
                string branchPath = levelPath.Length > 0 ? levelPath + "/" + branch : branch;
                PopulateRegistryLevel(all, branchPath);
                string? branchIcon = branches[branch].OrderBy(e => e.Priority)
                    .Select(e => e.Icon)
                    .FirstOrDefault(ic => !string.IsNullOrEmpty(ic));
                if (!string.IsNullOrEmpty(branchIcon))
                    MenuRegistry.RegisterBranchIcon(branchPath, branchIcon);
            }
        }
    }

    public static void BuildContextMenu(ContextBuilder builder, string rootPath)
    {
        string prefix = rootPath.TrimEnd('/') + "/";
        var relevant = GetSorted().Where(e => e.Path.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        BuildLevel(builder, relevant, prefix);
    }

    private static void BuildLevel(ContextBuilder builder, List<Entry> entries, string prefix)
    {
        var leaves = new List<Entry>();
        var branchOrder = new List<string>();
        var branches = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);

        foreach (var e in entries)
        {
            string rest = e.Path.Substring(prefix.Length);
            int slash = rest.IndexOf('/');
            if (slash < 0)
                leaves.Add(e);
            else
            {
                string key = rest.Substring(0, slash);
                if (!branches.ContainsKey(key)) { branches[key] = []; branchOrder.Add(key); }
                branches[key].Add(e);
            }
        }

        var items = new List<(int Priority, Entry? Leaf, string? Branch)>();
        foreach (var leaf in leaves) items.Add((leaf.Priority, leaf, null));
        foreach (var key in branchOrder) items.Add((branches[key].Min(e => e.Priority), null, key));
        items.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        for (int i = 0; i < items.Count; i++)
        {
            var (_, leaf, branch) = items[i];

            bool wantsSep = leaf?.Separator ?? false;
            if (branch != null)
                wantsSep = branches[branch].OrderBy(e => e.Priority).First().Separator;

            if (i > 0 && wantsSep) builder.Separator();

            if (leaf != null)
            {
                var validator = _validators.TryGetValue(leaf.Path, out var v) ? v : null;
                builder.Item(leaf.Path.Substring(prefix.Length), leaf.Action, enabled: validator?.Invoke() ?? true, icon: leaf.Icon);
            }
            else if (branch != null)
            {
                var list = branches[branch];
                string subPrefix = prefix + branch + "/";
                string icon = list.OrderBy(e => e.Priority).Select(e => e.Icon).FirstOrDefault(ic => !string.IsNullOrEmpty(ic)) ?? "";
                builder.Submenu(branch, sub => BuildLevel(sub, list, subPrefix), icon);
            }
        }
    }
}
