// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Echo;
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
public abstract class Graph : EngineObject, ISerializable
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

    // ─── Custom Serialization (MissingNode fallback) ─────────────────────────────────
    // We own Serialize/Deserialize so we can intercept each Node tag, look up its
    // $type, and — if the type no longer resolves — wrap the raw EchoObject in a
    // MissingNode that preserves the original payload for recovery on re-save. Same
    // pattern GameObject uses for MissingMonobehaviour.

    public void Serialize(ref EchoObject compound, SerializationContext ctx)
    {
        SerializeHeader(compound);

        // Nodes are polymorphic — Serializer writes the $type tag that Deserialize
        // uses below to detect unresolvable types.
        var nodesTag = EchoObject.NewList();
        foreach (var n in Nodes)
            nodesTag.ListAdd(Serializer.Serialize(typeof(Node), n, ctx));
        compound.Add("Nodes", nodesTag);

        compound.Add("Edges",       Serializer.Serialize(typeof(List<Edge>), Edges, ctx));
        compound.Add("Blackboard",  Serializer.Serialize(typeof(List<BlackboardVariable>), Blackboard, ctx));
        compound.Add("StickyNotes", Serializer.Serialize(typeof(List<StickyNote>), StickyNotes, ctx));
        compound.Add("Groups",      Serializer.Serialize(typeof(List<NodeGroup>), Groups, ctx));

        compound.Add("ViewportPan",  Serializer.Serialize(typeof(Float2), ViewportPan, ctx));
        compound.Add("ViewportZoom", new EchoObject(ViewportZoom));
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        DeserializeHeader(value);

        Nodes = new List<Node>();
        var nodesTag = value.Get("Nodes");
        if (nodesTag != null && nodesTag.TagType == EchoType.List)
        {
            foreach (var nodeTag in nodesTag.List)
            {
                // Missing-type fallback — same shape as GameObject's component handling.
                var typeProperty = nodeTag.Get("$type");
                if (typeProperty != null && !string.IsNullOrWhiteSpace(typeProperty.StringValue))
                {
                    var resolved = RuntimeUtils.FindType(typeProperty.StringValue);
                    if (resolved == null)
                    {
                        Debug.LogWarning($"Missing Node Type: {typeProperty.StringValue} on Graph '{Name}'");
                        Nodes.Add(new MissingNode
                        {
                            MissingTypeName = typeProperty.StringValue,
                            SerializedPayload = nodeTag,
                        });
                        continue;
                    }
                    if (resolved == typeof(MissingNode))
                    {
                        // Re-entry: we saved a MissingNode previously. If its original
                        // payload's $type now resolves (user restored the assembly),
                        // recover it; otherwise keep it as a MissingNode so we don't
                        // lose the payload on this round-trip either.
                        HandleMissingNode(nodeTag, ctx);
                        continue;
                    }
                }

                var node = Serializer.Deserialize<Node>(nodeTag, ctx);
                if (node != null) Nodes.Add(node);
            }
        }

        Edges       = Serializer.Deserialize<List<Edge>>(value.Get("Edges") ?? EchoObject.NewList(), ctx) ?? new();
        Blackboard  = Serializer.Deserialize<List<BlackboardVariable>>(value.Get("Blackboard") ?? EchoObject.NewList(), ctx) ?? new();
        StickyNotes = Serializer.Deserialize<List<StickyNote>>(value.Get("StickyNotes") ?? EchoObject.NewList(), ctx) ?? new();
        Groups      = Serializer.Deserialize<List<NodeGroup>>(value.Get("Groups") ?? EchoObject.NewList(), ctx) ?? new();

        var panTag = value.Get("ViewportPan");
        if (panTag != null) ViewportPan = Serializer.Deserialize<Float2>(panTag, ctx);
        ViewportZoom = value.Get("ViewportZoom")?.FloatValue ?? 1f;

        // Drop edges whose endpoints don't exist anymore — defensive against edges
        // surviving a node that even the MissingNode fallback couldn't resurrect.
        var validIds = new HashSet<Guid>();
        foreach (var n in Nodes) validIds.Add(n.Id);
        Edges.RemoveAll(e => !validIds.Contains(e.SourceNodeId) || !validIds.Contains(e.TargetNodeId));
    }

    /// <summary>Try to rehydrate a previously-saved <see cref="MissingNode"/> back into
    /// its original concrete type — happens when the user restores the assembly that
    /// defines the type after saving the graph with a placeholder.</summary>
    private void HandleMissingNode(EchoObject nodeTag, SerializationContext ctx)
    {
        var missing = Serializer.Deserialize<MissingNode>(nodeTag, ctx);
        if (missing == null) return;

        var payload = missing.SerializedPayload;
        if (payload != null && payload.TryGet("$type", out var typeProp))
        {
            var resolved = RuntimeUtils.FindType(typeProp.StringValue);
            if (resolved != null)
            {
                var recovered = Serializer.Deserialize<Node>(payload, ctx);
                if (recovered != null)
                {
                    Nodes.Add(recovered);
                    return;
                }
            }
        }
        // Still unresolved — keep the MissingNode so payload isn't lost.
        Nodes.Add(missing);
    }
}
