// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Runtime.GraphTools;

/// <summary>
/// Mark a Node type as available in every graph type's creation menu, regardless of
/// the graph's <see cref="Graph.NodeMarkerInterface"/>. Use sparingly — for nodes that
/// genuinely make sense in any graph (relays, subgraphs, comments, group inputs).
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class UniversalNodeAttribute : Attribute { }

/// <summary>
/// Hide a Node type from the node-creation menu. Used for nodes that only exist as
/// the output of a specific gesture (e.g. <see cref="RelayNode"/> — inserted by alt+
/// clicking a wire, never spawned from scratch) or for abstract/obsolete types that
/// should still deserialize but not be manually selectable.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class HiddenFromMenuAttribute : Attribute { }

/// <summary>Snapshot of a single port at registration time (so the menu can filter by
/// compatibility without instantiating the node first).</summary>
public readonly struct PortInfo
{
    public readonly string Name;
    public readonly Type DataType;
    public readonly PortDirection Direction;
    public readonly bool AcceptsMultiple;

    public PortInfo(string name, Type type, PortDirection dir, bool acceptsMultiple)
    { Name = name; DataType = type; Direction = dir; AcceptsMultiple = acceptsMultiple; }
}

/// <summary>
/// Cached metadata for a Node type that's eligible to appear in a node-creation menu.
/// Port snapshots let callers filter by compatibility (e.g. wire-drop popup showing only
/// nodes with a matching input port).
/// </summary>
public readonly struct NodeRegistration
{
    public readonly Type Type;
    public readonly string Title;
    public readonly string Category;
    public readonly IReadOnlyList<PortInfo> Ports;
    public readonly bool IsUniversal;
    public readonly bool IsHiddenFromMenu;

    public NodeRegistration(Type type, string title, string category, IReadOnlyList<PortInfo> ports, bool isUniversal, bool isHiddenFromMenu)
    { Type = type; Title = title; Category = category; Ports = ports; IsUniversal = isUniversal; IsHiddenFromMenu = isHiddenFromMenu; }

    /// <summary>
    /// Does this node have any port that could accept a wire from a port of the given type
    /// + direction? Mirrors the Phase-2 <c>GraphLayout.ArePortsCompatible</c> rules.
    /// </summary>
    public bool HasCompatiblePort(Type sourceType, PortDirection sourceDirection)
    {
        var needDir = sourceDirection == PortDirection.Output ? PortDirection.Input : PortDirection.Output;
        foreach (var p in Ports)
        {
            if (p.Direction != needDir) continue;
            if (p.DataType == sourceType) return true;
            // Inputs typed object accept anything; flip when source is the input.
            if (sourceDirection == PortDirection.Output && p.DataType == typeof(object)) return true;
            if (sourceDirection == PortDirection.Input && sourceType == typeof(object)) return true;
        }
        return false;
    }

    /// <summary>Find the first port matching an opposite-direction wire of the given type.</summary>
    public PortInfo? FindCompatiblePort(Type sourceType, PortDirection sourceDirection)
    {
        var needDir = sourceDirection == PortDirection.Output ? PortDirection.Input : PortDirection.Output;
        foreach (var p in Ports)
        {
            if (p.Direction != needDir) continue;
            if (p.DataType == sourceType) return p;
            if (sourceDirection == PortDirection.Output && p.DataType == typeof(object)) return p;
            if (sourceDirection == PortDirection.Input && sourceType == typeof(object)) return p;
        }
        return null;
    }
}

/// <summary>
/// Reflection-based registry of every concrete <see cref="Node"/> subclass in the
/// AppDomain. Built lazily on first query and cached. Designed so users can drop new
/// graph types and node types into their own assemblies and have them picked up
/// automatically — no manual registration step.
/// </summary>
/// <remarks>
/// Filtering by graph compatibility uses marker interfaces: a graph implementation
/// returns its <see cref="Graph.NodeMarkerInterface"/> (e.g. <c>IShaderGraphNode</c>),
/// and the registry returns every node type that implements that interface. A node can
/// implement multiple markers to be reusable across graph types.
/// </remarks>
public static class NodeRegistry
{
    private static List<NodeRegistration>? s_all;
    private static readonly Dictionary<Type, List<NodeRegistration>> s_byMarker = new();
    private static readonly object s_lock = new();

    /// <summary>
    /// All concrete <see cref="Node"/> types in the AppDomain that have a parameterless
    /// constructor. Built once and reused.
    /// </summary>
    public static IReadOnlyList<NodeRegistration> All
    {
        get { EnsureBuilt(); return s_all!; }
    }

    /// <summary>
    /// Returns every registered node type that implements <paramref name="markerInterface"/>.
    /// Pass <c>null</c> to get every node type (no filtering).
    /// </summary>
    public static IReadOnlyList<NodeRegistration> GetForMarker(Type? markerInterface)
    {
        EnsureBuilt();
        lock (s_lock)
        {
            if (markerInterface != null && s_byMarker.TryGetValue(markerInterface, out var cached))
                return cached;

            var filtered = new List<NodeRegistration>();
            foreach (var reg in s_all!)
            {
                if (reg.IsHiddenFromMenu) continue;
                if (markerInterface == null
                    || reg.IsUniversal
                    || markerInterface.IsAssignableFrom(reg.Type))
                    filtered.Add(reg);
            }
            if (markerInterface != null) s_byMarker[markerInterface] = filtered;
            return filtered;
        }
    }

    /// <summary>
    /// Force a full rescan — call after adding/removing assemblies at runtime (e.g. after
    /// recompiling user scripts).
    /// </summary>
    public static void Reinitialize()
    {
        lock (s_lock)
        {
            s_all = null;
            s_byMarker.Clear();
        }
    }

    private static void EnsureBuilt()
    {
        if (s_all != null) return;
        lock (s_lock)
        {
            if (s_all != null) return;

            var list = new List<NodeRegistration>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException ex) { types = ex.Types!; }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t == null) continue;
                    if (t.IsAbstract || !typeof(Node).IsAssignableFrom(t)) continue;
                    if (t.GetConstructor(Type.EmptyTypes) == null) continue;

                    // Instantiate once to read display metadata + port snapshot. Title /
                    // Category are virtual properties on the Node; ports come from
                    // EnsureDefined → DefineNode (rebuilt fresh each load anyway).
                    string title, category;
                    List<PortInfo> ports;
                    try
                    {
                        var instance = (Node)Activator.CreateInstance(t)!;
                        instance.EnsureDefined();
                        title = instance.Title;
                        category = instance.Category;
                        ports = new List<PortInfo>(instance.Inputs.Count + instance.Outputs.Count);
                        foreach (var p in instance.Inputs)  ports.Add(new PortInfo(p.Name, p.DataType, p.Direction, p.AcceptsMultiple));
                        foreach (var p in instance.Outputs) ports.Add(new PortInfo(p.Name, p.DataType, p.Direction, p.AcceptsMultiple));
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"NodeRegistry: skipping {t.FullName} — instantiation failed ({ex.Message}).");
                        continue;
                    }

                    bool universal = t.GetCustomAttributes(typeof(UniversalNodeAttribute), inherit: false).Length > 0;
                    bool hidden = t.GetCustomAttributes(typeof(HiddenFromMenuAttribute), inherit: false).Length > 0;
                    list.Add(new NodeRegistration(t, title, category, ports, universal, hidden));
                }
            }

            s_all = list;
            Debug.Log($"NodeRegistry: discovered {list.Count} node type(s).");
        }
    }
}
