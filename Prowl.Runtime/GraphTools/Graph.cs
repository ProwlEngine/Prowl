// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools;

/// <summary>
/// Abstract base for any node-based asset (shader graphs, behaviour trees, visual scripts, ...).
/// Holds the graph topology — nodes, edges, blackboard variables, sticky notes, groups —
/// and is serialized as a regular Prowl asset via Echo. Subclasses register themselves
/// with <see cref="CreateAssetMenuAttribute"/> for a specific file extension and provide
/// graph-type-specific node libraries.
/// </summary>
/// <remarks>
/// Echo serializes the polymorphic <see cref="Nodes"/> list automatically (each entry's
/// concrete type is recorded), the same way <c>List&lt;MonoBehaviour&gt;</c> works on
/// <c>GameObject</c>. All persisted state lives in public fields — Echo doesn't see
/// properties.
/// </remarks>
public abstract class Graph : EngineObject
{
    /// <summary>All nodes in the graph. Polymorphic — subclasses live in this list.</summary>
    public List<Node> Nodes = new();

    /// <summary>All wires connecting node ports.</summary>
    public List<Edge> Edges = new();

    /// <summary>Blackboard variables (typed values exposed across the whole graph).</summary>
    public List<BlackboardVariable> Blackboard = new();

    /// <summary>Free-floating sticky notes for documentation.</summary>
    public List<StickyNote> StickyNotes = new();

    /// <summary>Visual groups that contain nodes (titled coloured regions).</summary>
    public List<NodeGroup> Groups = new();

    /// <summary>Last view state — pan offset and zoom — so the editor reopens where you left off.</summary>
    public Float2 ViewportPan = Float2.Zero;
    public float ViewportZoom = 1f;

    protected Graph(string name) : base(name) { }
    protected Graph() : base("New Graph") { }

    /// <summary>
    /// The marker interface a Node type must implement to appear in this graph's
    /// node-creation menu. Subclasses return their own marker interface (e.g.
    /// <c>typeof(IShaderGraphNode)</c>); user-defined graphs declare their own marker
    /// and implement it on the nodes they want to expose. Return <c>null</c> to allow
    /// every <see cref="Node"/> subclass.
    /// </summary>
    /// <remarks>
    /// Discovery + caching happens in <see cref="NodeRegistry"/> — purely reflection-based
    /// so users can add new graph types and node types in their own assemblies without
    /// touching the framework.
    /// </remarks>
    public abstract Type? NodeMarkerInterface { get; }

    // ─── Node lookup ──────────────────────────────────────────────────────────────────

    /// <summary>Find a node by its stable Id, or null if it's been removed.</summary>
    public Node? FindNode(Guid id)
    {
        for (int i = 0; i < Nodes.Count; i++)
            if (Nodes[i].Id == id) return Nodes[i];
        return null;
    }

    /// <summary>
    /// Add a node with a fresh Id (assigns one if Id is empty). The framework expects
    /// every node to have a unique non-empty Id — Edges reference nodes by Id, not index,
    /// so reordering / inserting is safe.
    /// </summary>
    public T AddNode<T>(T node) where T : Node
    {
        if (node.Id == Guid.Empty) node.Id = Guid.NewGuid();
        Nodes.Add(node);
        return node;
    }

    /// <summary>Remove a node and any edges referencing it.</summary>
    public void RemoveNode(Guid id)
    {
        Nodes.RemoveAll(n => n.Id == id);
        Edges.RemoveAll(e => e.SourceNodeId == id || e.TargetNodeId == id);
    }

    /// <summary>
    /// Returns true if there's an edge between the given source-port and target-port.
    /// Cheap — used by the renderer to skip drawing unconnected default-value editors.
    /// </summary>
    public bool IsPortConnected(Guid nodeId, string portName, PortDirection direction)
    {
        foreach (var e in Edges)
        {
            if (direction == PortDirection.Output)
            {
                if (e.SourceNodeId == nodeId && e.SourcePortName == portName) return true;
            }
            else
            {
                if (e.TargetNodeId == nodeId && e.TargetPortName == portName) return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Enumerate every edge attached to the given port. Output ports may have multiple
    /// outgoing wires; inputs are normally single-connection but can be multi if the
    /// port's <see cref="Port.AcceptsMultiple"/> is true. Used by the renderer to draw
    /// wires and by interaction code to detect "what's already connected here".
    /// </summary>
    public IEnumerable<Edge> GetEdgesForPort(Guid nodeId, string portName, PortDirection direction)
    {
        foreach (var e in Edges)
        {
            if (direction == PortDirection.Output)
            {
                if (e.SourceNodeId == nodeId && e.SourcePortName == portName) yield return e;
            }
            else
            {
                if (e.TargetNodeId == nodeId && e.TargetPortName == portName) yield return e;
            }
        }
    }
}
