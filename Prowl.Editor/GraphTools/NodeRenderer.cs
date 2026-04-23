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
/// Mark a <see cref="NodeRenderer"/> subclass as the drawer for a specific node type.
/// Discovered at startup via <see cref="NodeRendererRegistry"/>. Mirrors the
/// <c>[CustomEditor(typeof(X))]</c> pattern used by the Inspector so users can author
/// node-type-specific visuals the same way they author custom inspectors.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class NodeRendererAttribute : Attribute
{
    public Type TargetType { get; }
    public NodeRendererAttribute(Type targetType) => TargetType = targetType;
}

/// <summary>
/// Pluggable renderer for a single <see cref="Node"/>. Owns its node's shape, port
/// layout, and drawing allowing nodes to be rendered as vertical strips, icons,
/// diamonds, or anything else rather than the default left-to-right card.
/// </summary>
/// <remarks>
/// A renderer is stateless one instance is shared across every node of its target
/// type, so it MUST NOT cache per-node state on itself. Geometry methods return values
/// in graph-space coordinates; <see cref="Draw"/> paints through a Quill canvas that's
/// already been transformed into graph space by <see cref="GraphRendering"/>.
/// </remarks>
public abstract class NodeRenderer
{
    /// <summary>Axis-aligned bounding box of the node in graph space. Used for
    /// hit-testing, marquee selection, and framing.</summary>
    public abstract Rect GetRect(Node node);

    /// <summary>Centre point of <paramref name="port"/>'s connection circle in graph
    /// space where wires attach. <paramref name="port"/> belongs to
    /// <paramref name="node"/>'s Inputs or Outputs list.</summary>
    public abstract Float2 GetPortPosition(Node node, Port port);

    /// <summary>Paint the node. <paramref name="zoom"/> is used for LOD decisions
    /// (e.g. skipping text below a threshold). <paramref name="hoveredPort"/> is set
    /// when the cursor is over a port on this node so the renderer can highlight it.</summary>
    public abstract void Draw(Canvas canvas, Prowl.Runtime.GraphTools.Graph graph, Node node,
        bool isSelected, bool isHovered,
        (string portName, PortDirection direction)? hoveredPort,
        float zoom, Prowl.Scribe.FontFile? font);
}

/// <summary>
/// Discovers <see cref="NodeRenderer"/> subclasses marked with
/// <see cref="NodeRendererAttribute"/> and maps node types to their renderer.
/// Falls back to <see cref="DefaultNodeRenderer"/> for any node type without a
/// registered override. Initialised from <c>EditorApplication</c> at startup.
/// </summary>
public static class NodeRendererRegistry
{
    private static readonly Dictionary<Type, Type> _typeToRenderer = new();
    private static readonly Dictionary<Type, NodeRenderer> _cache = new();
    private static readonly NodeRenderer _default = new DefaultNodeRenderer();
    private static bool _initialized;

    public static void Reinitialize()
    {
        _initialized = false;
        _cache.Clear();
        Initialize();
    }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        _typeToRenderer.Clear();
        _cache.Clear();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (!typeof(NodeRenderer).IsAssignableFrom(type) || type.IsAbstract) continue;
                var attr = type.GetCustomAttribute<NodeRendererAttribute>();
                if (attr == null) continue;
                _typeToRenderer[attr.TargetType] = type;
            }
        }

        Runtime.Debug.Log($"NodeRendererRegistry: {_typeToRenderer.Count} custom node renderers registered.");
    }

    /// <summary>
    /// Resolve the renderer for <paramref name="nodeType"/>. Walks base types so a
    /// renderer registered for a common base (e.g. a "MathNode" abstract) covers every
    /// concrete subclass that doesn't register its own. Always returns a usable renderer
    /// falls back to <see cref="DefaultNodeRenderer"/>.
    /// </summary>
    public static NodeRenderer GetRenderer(Type nodeType)
    {
        if (!_initialized) Initialize();
        if (_cache.TryGetValue(nodeType, out var cached)) return cached;

        if (_typeToRenderer.TryGetValue(nodeType, out var rendererType))
            return Cache(nodeType, rendererType);

        Type? baseType = nodeType.BaseType;
        while (baseType != null && baseType != typeof(object))
        {
            if (_typeToRenderer.TryGetValue(baseType, out rendererType))
                return Cache(nodeType, rendererType);
            baseType = baseType.BaseType;
        }

        _cache[nodeType] = _default;
        return _default;
    }

    /// <summary>Convenience: <c>GetRenderer(node.GetType())</c>.</summary>
    public static NodeRenderer GetRenderer(Node node) => GetRenderer(node.GetType());

    private static NodeRenderer Cache(Type targetType, Type rendererType)
    {
        var r = (NodeRenderer)Activator.CreateInstance(rendererType)!;
        _cache[targetType] = r;
        return r;
    }
}
