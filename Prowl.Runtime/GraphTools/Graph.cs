// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime.GraphTools;

/// <summary>How wires are visually routed between two ports.</summary>
public enum WireRoutingStyle
{
    /// <summary>Cubic bezier with horizontal tangents the default. Smooth curves.</summary>
    Bezier,
    /// <summary>Straight line from source to target.</summary>
    Linear,
    /// <summary>Right-angle path: out horizontally, then vertically, then horizontally
    /// into the target. Useful for very dense graphs where curves overlap badly.</summary>
    Rectilinear,
}

/// <summary>
/// Abstract base for any node-based asset (shader graphs, behaviour trees, visual scripts, ...).
/// Holds the graph topology nodes, edges, blackboard variables, sticky notes, groups
/// and is serialized as a regular Prowl asset via Echo. Subclasses register themselves
/// with <see cref="CreateAssetMenuAttribute"/> for a specific file extension and provide
/// graph-type-specific node libraries.
/// </summary>
/// <remarks>
/// Echo serializes the polymorphic <see cref="Nodes"/> list automatically (each entry's
/// concrete type is recorded), the same way <c>List&lt;MonoBehaviour&gt;</c> works on
/// <c>GameObject</c>. All persisted state lives in public fields Echo doesn't see
/// properties.
/// </remarks>
public abstract class Graph : EngineObject, ISerializable
{
    /// <summary>All nodes in the graph. Polymorphic subclasses live in this list.</summary>
    public List<Node> Nodes = new();

    /// <summary>All wires connecting node ports.</summary>
    public List<Edge> Edges = new();

    /// <summary>Blackboard variables (typed values exposed across the whole graph).</summary>
    public List<BlackboardVariable> Blackboard = new();

    /// <summary>Free-floating sticky notes for documentation.</summary>
    public List<StickyNote> StickyNotes = new();

    /// <summary>Visual groups that contain nodes (titled coloured regions).</summary>
    public List<NodeGroup> Groups = new();

    /// <summary>Last view state pan offset and zoom so the editor reopens where you left off.</summary>
    public Float2 ViewportPan = Float2.Zero;
    public float ViewportZoom = 1f;

    /// <summary>Visual style for wire routing applies to every wire in this graph.
    /// User-settable from the editor toolbar.</summary>
    public WireRoutingStyle WireStyle = WireRoutingStyle.Bezier;

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
    /// Discovery + caching happens in <see cref="NodeRegistry"/> purely reflection-based
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
    /// every node to have a unique non-empty Id Edges reference nodes by Id, not index,
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
    /// Cheap used by the renderer to skip drawing unconnected default-value editors.
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
    // $type, and if the type no longer resolves wrap the raw EchoObject in a
    // MissingNode that preserves the original payload for recovery on re-save. Same
    // pattern GameObject uses for MissingMonobehaviour.

    public virtual void Serialize(ref EchoObject compound, SerializationContext ctx)
    {
        SerializeHeader(compound);

        // Nodes are polymorphic Serializer writes the $type tag that Deserialize
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

    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Deserialization needs to map a serialized $type string back to a concrete Node type. User node types must be preserved by the consuming application's trim configuration.")]
    public virtual void Deserialize(EchoObject value, SerializationContext ctx)
    {
        DeserializeHeader(value);

        Nodes = new List<Node>();
        var nodesTag = value.Get("Nodes");
        if (nodesTag != null && nodesTag.TagType == EchoType.List)
        {
            foreach (var nodeTag in nodesTag.List)
            {
                // Missing-type fallback same shape as GameObject's component handling.
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

        PruneOrphanEdges();
    }

    /// <summary>
    /// Drop edges whose endpoints can no longer be resolved the node doesn't
    /// exist (deleted or couldn't be revived from a MissingNode), the referenced
    /// port name no longer appears on the node (renamed / removed by a code
    /// change), or the port types are no longer compatible (semantic change).
    /// Every dropped edge gets a <c>Debug.LogWarning</c> so users can re-wire
    /// deliberately after refactoring their node set.
    /// </summary>
    /// <remarks>
    /// Exposed as public so callers can re-run it after mutating node ports at
    /// runtime (e.g. after changing an enum that hides/reveals ports on a node).
    /// </remarks>
    public void PruneOrphanEdges()
    {
        // Build a node-id → node map once so we don't linearly scan per edge.
        var byId = new Dictionary<Guid, Node>(Nodes.Count);
        foreach (var n in Nodes) byId[n.Id] = n;

        Edges.RemoveAll(e =>
        {
            // 1. Node missing entirely.
            if (!byId.TryGetValue(e.SourceNodeId, out var src))
            {
                Debug.LogWarning($"Graph '{Name}': edge dropped source node missing (id={e.SourceNodeId}).");
                return true;
            }
            if (!byId.TryGetValue(e.TargetNodeId, out var dst))
            {
                Debug.LogWarning($"Graph '{Name}': edge dropped target node missing (id={e.TargetNodeId}).");
                return true;
            }

            // MissingNode placeholders have zero ports; we keep the node around to
            // preserve the payload but the edges to/from it can't route anywhere.
            if (src is MissingNode || dst is MissingNode)
            {
                Debug.LogWarning($"Graph '{Name}': edge dropped endpoint is a MissingNode (restore the node's type to recover).");
                return true;
            }

            // 2. Port names gone (renamed or removed).
            src.EnsureDefined();
            dst.EnsureDefined();
            var sp = src.GetOutput(e.SourcePortName);
            var tp = dst.GetInput(e.TargetPortName);
            if (sp == null)
            {
                Debug.LogWarning($"Graph '{Name}': edge dropped source output '{src.GetType().Name}.{e.SourcePortName}' no longer exists.");
                return true;
            }
            if (tp == null)
            {
                Debug.LogWarning($"Graph '{Name}': edge dropped target input '{dst.GetType().Name}.{e.TargetPortName}' no longer exists.");
                return true;
            }

            // 3. Port types incompatible (respects numeric promotion a Float
            // wire into a Vec3 input is still valid).
            if (!PortTypes.AreCompatible(sp.DataType, tp.DataType))
            {
                Debug.LogWarning($"Graph '{Name}': edge dropped types no longer match ({src.GetType().Name}.{e.SourcePortName}:{sp.DataType.Name} → {dst.GetType().Name}.{e.TargetPortName}:{tp.DataType.Name}).");
                return true;
            }

            return false;
        });
    }

    /// <summary>Try to rehydrate a previously-saved <see cref="MissingNode"/> back into
    /// its original concrete type happens when the user restores the assembly that
    /// defines the type after saving the graph with a placeholder.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "Recovery path: looks up a previously-missing node type by its serialized name. User node types must be preserved by the consuming application's trim configuration.")]
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
        // Still unresolved keep the MissingNode so payload isn't lost.
        Nodes.Add(missing);
    }
}
