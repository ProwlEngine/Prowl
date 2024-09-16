// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Prowl.Runtime.NodeSystem;

public class GraphParameter
{
    public enum ParameterType
    {
        Int,
        Double,
        Bool,
        Texture,
        Material
    }

    public string name;
    public ParameterType type;
    [ShowIf(nameof(IsInt))] public int intVal;
    [ShowIf(nameof(IsDouble))] public double doubleVal;
    [ShowIf(nameof(IsBool))] public bool boolVal;
    [ShowIf(nameof(IsTexture))] public AssetRef<Texture> textureRef;
    [ShowIf(nameof(IsMaterial))] public AssetRef<Material> materialRef;

    public bool IsInt => type == ParameterType.Int;
    public bool IsDouble => type == ParameterType.Double;
    public bool IsBool => type == ParameterType.Bool;
    public bool IsTexture => type == ParameterType.Texture;
    public bool IsMaterial => type == ParameterType.Material;
}

public abstract class NodeGraph : ScriptableObject, ISerializationCallbackReceiver
{
    [SerializeField] int _nextID = 0;
    public int NextID => _nextID++;

    /// <summary> All nodes in the graph. <para/>
    /// See: <see cref="AddNode{T}"/> </summary>
    public List<Node> nodes = [];

    public virtual (string, Type)[] NodeTypes { get; } = [];
    public virtual (string, Type)[] NodeReflectionTypes { get; } = [];
    public abstract string[] NodeCategories { get; }

    public List<GraphParameter> parameters = [];

    public void Validate()
    {
        var attrib = GetType().GetCustomAttribute<RequireNodeAttribute>(true);
        if (attrib != null)
            foreach (Type type in attrib.types)
                if (!nodes.Where(n => n.GetType() == type).Any())
                    AddNode(type);

        OnValidate();
        foreach (var node in nodes)
            node.OnValidate();
    }

    /// <summary> Add a node to the graph by type (convenience method - will call the System.Type version) </summary>
    public T AddNode<T>() where T : Node
    {
        return AddNode(typeof(T)) as T;
    }

    /// <summary> Add a node to the graph by type </summary>
    public virtual Node AddNode(Type type)
    {
        // if it has a DisallowMultipleNodesAttribute and there is already one in the graph return null
        var attrib = type.GetCustomAttribute<Node.DisallowMultipleNodesAttribute>(true);
        if (attrib != null)
        {
            if (nodes.Where(n => n.GetType() == type).Count() >= attrib.max)
            {
                Debug.LogError($"Only {attrib.max} nodes of type {type} are allowed in the graph!");
                return null;
            }
        }

        Node node = Activator.CreateInstance(type) as Node;
        node.graph = this;
        nodes.Add(node);
        node.OnEnable();
        return node;
    }

    public T GetNode<T>() where T : Node
    {
        return nodes.Where(n => n.GetType() == typeof(T)).FirstOrDefault() as T;
    }

    public IEnumerable<T> GetNodes<T>() where T : Node
    {
        return nodes.Where(n => n.GetType() == typeof(T)).Cast<T>();
    }

    public virtual Node GetNode(int instanceID)
    {
        return nodes.Where(n => n.InstanceID == instanceID).FirstOrDefault();
    }

    /// <summary> Creates a copy of the original node in the graph </summary>
    public virtual Node CopyNode(Node original)
    {
        SerializedProperty nodeTag = Serializer.Serialize(original);
        Node node = Serializer.Deserialize<Node>(nodeTag);
        node.graph = this;
        node.position += new Vector2(30, 30);
        node.ClearConnections();
        nodes.Add(node);
        return node;
    }

    /// <summary> Safely remove a node and all its connections </summary>
    /// <param name="node"> The node to remove </param>
    public virtual void RemoveNode(Node node)
    {
        // check if we have a RequireNode attribute
        var attrib = GetType().GetCustomAttribute<RequireNodeAttribute>(true);
        if (attrib != null)
        {
            if (attrib.Requires(node.GetType()))
            {
                Debug.LogError($"Cannot remove node of type {node.GetType()} from graph!");
                return;
            }
        }

        node.ClearConnections();
        nodes.Remove(node);
    }

    /// <summary> Remove all nodes and connections from the graph </summary>
    public virtual void Clear()
    {
        nodes.Clear();
    }

    /// <summary> Create a new deep copy of this graph </summary>
    public virtual NodeGraph Copy()
    {
        SerializedProperty graphTag = Serializer.Serialize(this);
        NodeGraph graph = Serializer.Deserialize<NodeGraph>(graphTag);
        // Instantiate all nodes inside the graph
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] == null) continue;
            SerializedProperty nodeTag = Serializer.Serialize(nodes[i]);
            Node node = Serializer.Deserialize<Node>(nodeTag);
            node.graph = graph;
            graph.nodes[i] = node;
        }

        // Redirect all connections
        for (int i = 0; i < graph.nodes.Count; i++)
        {
            if (graph.nodes[i] == null) continue;
            foreach (NodePort port in graph.nodes[i].Ports)
            {
                port.Redirect(nodes, graph.nodes);
            }
        }

        return graph;
    }

    protected virtual void OnDestroy()
    {
        // Remove all nodes prior to graph destruction
        Clear();
    }

    public override void OnBeforeSerialize() { }

    public override void OnAfterDeserialize()
    {
        // Clear null nodes
        nodes.RemoveAll(n => n == null);
        foreach (Node node in nodes)
        {
            node.graph = this;
            node.VerifyConnections();
            node.OnEnable();
        }

        Validate();
    }

    #region Attributes
    /// <summary> Automatically ensures the existance of a certain node type, and prevents it from being deleted. </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class RequireNodeAttribute(params Type[] type) : Attribute
    {
        public Type[] types = type;

        public bool Requires(Type type)
        {
            if (type == null) return false;
            foreach (Type t in types)
                if (t == type) return true;
            return false;
        }
    }
    #endregion
}
