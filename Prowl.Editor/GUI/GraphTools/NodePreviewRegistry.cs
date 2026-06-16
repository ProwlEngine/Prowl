// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Reflection;

using Prowl.Quill;
using Prowl.Runtime.GraphTools;
using Prowl.Vector;

namespace Prowl.Editor.GraphTools;

/// <summary>
/// Mark a <see cref="NodePreviewDrawer"/> subclass as the previewer for a specific
/// node type that implements <see cref="INodePreview"/>. Same discovery pattern as
/// <see cref="NodeRendererAttribute"/> picked up reflectively at startup.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NodePreviewDrawerAttribute : Attribute
{
    public Type TargetType { get; }
    public NodePreviewDrawerAttribute(Type targetType) => TargetType = targetType;
}

/// <summary>Renders the preview region of a single node. Stateless one instance per
/// node type, shared across every node of that type.</summary>
public abstract class NodePreviewDrawer
{
    /// <summary>Draw into <paramref name="rect"/> (graph-space). The canvas has the
    /// graph's pan/zoom transform already applied draw in graph units.</summary>
    public abstract void Draw(Canvas canvas, Node node, Rect rect, float zoom);
}

/// <summary>
/// Discovers <see cref="NodePreviewDrawer"/> subclasses by attribute. Returns null
/// for node types without a registered drawer the renderer then skips the preview
/// area even if the node implements <see cref="INodePreview"/>.
/// </summary>
public static class NodePreviewRegistry
{
    private static readonly Dictionary<Type, Type> _typeToDrawer = new();
    private static readonly Dictionary<Type, NodePreviewDrawer> _cache = new();
    private static bool _initialized;

    public static void Reinitialize()
    {
        _initialized = false;
        _cache.Clear();
        Initialize();
    }

    /// <summary>Drop cached type maps so the script AssemblyLoadContext can be collected.</summary>
    public static void ClearCache()
    {
        _initialized = false;
        _typeToDrawer.Clear();
        _cache.Clear();
    }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        _typeToDrawer.Clear();
        _cache.Clear();

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (!typeof(NodePreviewDrawer).IsAssignableFrom(type) || type.IsAbstract) continue;
                var attr = type.GetCustomAttribute<NodePreviewDrawerAttribute>();
                if (attr == null) continue;
                _typeToDrawer[attr.TargetType] = type;
            }
        }
    }

    /// <summary>Resolve the drawer for <paramref name="nodeType"/>. Walks base types so
    /// a drawer registered for a common base covers every concrete subclass.</summary>
    public static NodePreviewDrawer? GetDrawer(Type nodeType)
    {
        if (!_initialized) Initialize();
        if (_cache.TryGetValue(nodeType, out var cached)) return cached;

        if (_typeToDrawer.TryGetValue(nodeType, out var drawerType))
            return Cache(nodeType, drawerType);

        Type? baseType = nodeType.BaseType;
        while (baseType != null && baseType != typeof(object))
        {
            if (_typeToDrawer.TryGetValue(baseType, out drawerType))
                return Cache(nodeType, drawerType);
            baseType = baseType.BaseType;
        }
        return null;
    }

    private static NodePreviewDrawer Cache(Type targetType, Type drawerType)
    {
        var d = (NodePreviewDrawer)Activator.CreateInstance(drawerType)!;
        _cache[targetType] = d;
        return d;
    }
}
