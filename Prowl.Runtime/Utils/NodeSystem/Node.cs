using Prowl.Runtime.Utils.NodeSystem;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Runtime.NodeSystem
{
    /// <summary>
    /// Base class for all nodes
    /// </summary>
    /// <example>
    /// Classes extending this class will be considered as valid nodes by xNode.
    /// <code>
    /// [System.Serializable]
    /// public class Adder : Node {
    ///     [Input] public float a;
    ///     [Input] public float b;
    ///     [Output] public float result;
    ///
    ///     // GetValue should be overridden to return a value for any specified output port
    ///     public override object GetValue(NodePort port) {
    ///         return a + b;
    ///     }
    /// }
    /// </code>
    /// </example>
    [Serializable]
    public abstract class Node
    {
        /// <summary> Used by <see cref="InputAttribute"/> and <see cref="OutputAttribute"/> to determine when to display the field value associated with a <see cref="NodePort"/> </summary>
        public enum ShowBackingValue
        {
            /// <summary> Never show the backing value </summary>
            Never,
            /// <summary> Show the backing value only when the port does not have any active connections </summary>
            Unconnected,
            /// <summary> Always show the backing value </summary>
            Always
        }

        public enum ConnectionType
        {
            /// <summary> Allow multiple connections</summary>
            Multiple,
            /// <summary> always override the current connection </summary>
            Override,
        }

        /// <summary> Tells which types of input to allow </summary>
        public enum TypeConstraint
        {
            /// <summary> Allow all types of input</summary>
            None,
            /// <summary> Allow connections where input value type is assignable from output value type (eg. ScriptableObject --> Object)</summary>
            Inherited,
            /// <summary> Allow only similar types </summary>
            Strict,
            /// <summary> Allow connections where output value type is assignable from input value type (eg. Object --> ScriptableObject)</summary>
            InheritedInverse,
            /// <summary> Allow connections where output value type is assignable from input value or input value type is assignable from output value type</summary>
            InheritedAny
        }

        /// <summary> Iterate over all ports on this node. </summary>
        public IEnumerable<NodePort> Ports { get { foreach (NodePort port in ports.Values) yield return port; } }
        /// <summary> Iterate over all outputs on this node. </summary>
        public IEnumerable<NodePort> Outputs { get { foreach (NodePort port in Ports) { if (port.IsOutput) yield return port; } } }
        /// <summary> Iterate over all inputs on this node. </summary>
        public IEnumerable<NodePort> Inputs { get { foreach (NodePort port in Ports) { if (port.IsInput) yield return port; } } }
        /// <summary> Iterate over all dynamic ports on this node. </summary>
        public IEnumerable<NodePort> DynamicPorts { get { foreach (NodePort port in Ports) { if (port.IsDynamic) yield return port; } } }
        /// <summary> Iterate over all dynamic outputs on this node. </summary>
        public IEnumerable<NodePort> DynamicOutputs { get { foreach (NodePort port in Ports) { if (port.IsDynamic && port.IsOutput) yield return port; } } }
        /// <summary> Iterate over all dynamic inputs on this node. </summary>
        public IEnumerable<NodePort> DynamicInputs { get { foreach (NodePort port in Ports) { if (port.IsDynamic && port.IsInput) yield return port; } } }
        /// <summary> Parent <see cref="NodeGraph"/> </summary>
        [SerializeField, HideInInspector] public NodeGraph graph;
        /// <summary> Position on the <see cref="NodeGraph"/> </summary>
        [SerializeField, HideInInspector] public Vector2 position;
        /// <summary> It is recommended not to modify these at hand. Instead, see <see cref="InputAttribute"/> and <see cref="OutputAttribute"/> </summary>
        [SerializeField, HideInInspector] private Dictionary<string, NodePort> ports = new Dictionary<string, NodePort>();
        [HideInInspector] public int InstanceID;

        public abstract string Title { get; }
        public abstract float Width { get; }

        public void OnEnable()
        {
            InstanceID = graph.NextID;
            NodeDataCache.UpdatePorts(this, ports);
            Init();
        }

        /// <summary> Initialize node. Called on enable. </summary>
        protected virtual void Init() { }
        public virtual void OnValidate() { }

        /// <summary> Checks all connections for invalid references, and removes them. </summary>
        public void VerifyConnections()
        {
            foreach (NodePort port in Ports) port.VerifyConnections();
        }

        #region Dynamic Ports
        /// <summary> Convenience function. </summary>
        /// <seealso cref="AddInstancePort"/>
        /// <seealso cref="AddInstanceOutput"/>
        public NodePort AddDynamicInput(Type type, Node.ConnectionType connectionType = Node.ConnectionType.Multiple, Node.TypeConstraint typeConstraint = TypeConstraint.None, string fieldName = null)
        {
            return AddDynamicPort(type, NodePort.IO.Input, connectionType, typeConstraint, fieldName);
        }

        /// <summary> Convenience function. </summary>
        /// <seealso cref="AddInstancePort"/>
        /// <seealso cref="AddInstanceInput"/>
        public NodePort AddDynamicOutput(Type type, Node.ConnectionType connectionType = Node.ConnectionType.Multiple, Node.TypeConstraint typeConstraint = TypeConstraint.None, string fieldName = null)
        {
            return AddDynamicPort(type, NodePort.IO.Output, connectionType, typeConstraint, fieldName);
        }

        /// <summary> Add a dynamic, serialized port to this node. </summary>
        /// <seealso cref="AddDynamicInput"/>
        /// <seealso cref="AddDynamicOutput"/>
        private NodePort AddDynamicPort(Type type, NodePort.IO direction, Node.ConnectionType connectionType = Node.ConnectionType.Multiple, Node.TypeConstraint typeConstraint = TypeConstraint.None, string fieldName = null)
        {
            if (fieldName == null)
            {
                fieldName = "dynamicInput_0";
                int i = 0;
                while (HasPort(fieldName)) fieldName = "dynamicInput_" + (++i);
            }
            else if (HasPort(fieldName))
            {
                Debug.LogWarning("Port '" + fieldName + "' already exists in " + GetType().Name);
                return ports[fieldName];
            }
            NodePort port = new NodePort(fieldName, type, direction, connectionType, typeConstraint, this);
            ports.Add(fieldName, port);
            return port;
        }

        /// <summary> Remove an dynamic port from the node </summary>
        public void RemoveDynamicPort(string fieldName)
        {
            NodePort dynamicPort = GetPort(fieldName);
            if (dynamicPort == null) throw new ArgumentException("port " + fieldName + " doesn't exist");
            RemoveDynamicPort(GetPort(fieldName));
        }

        /// <summary> Remove an dynamic port from the node </summary>
        public void RemoveDynamicPort(NodePort port)
        {
            if (port == null) throw new ArgumentNullException("port");
            else if (port.IsStatic) throw new ArgumentException("cannot remove static port");
            port.ClearConnections();
            ports.Remove(port.fieldName);
        }

        /// <summary> Removes all dynamic ports from the node </summary>
        //[ContextMenu("Clear Dynamic Ports")]
        public void ClearDynamicPorts()
        {
            List<NodePort> dynamicPorts = new List<NodePort>(DynamicPorts);
            foreach (NodePort port in dynamicPorts)
            {
                RemoveDynamicPort(port);
            }
        }
        #endregion

        #region Ports
        /// <summary> Returns output port which matches fieldName </summary>
        public NodePort GetOutputPort(string fieldName)
        {
            NodePort port = GetPort(fieldName);
            if (port == null || port.direction != NodePort.IO.Output) return null;
            else return port;
        }

        /// <summary> Returns input port which matches fieldName </summary>
        public NodePort GetInputPort(string fieldName)
        {
            NodePort port = GetPort(fieldName);
            if (port == null || port.direction != NodePort.IO.Input) return null;
            else return port;
        }

        /// <summary> Returns port which matches fieldName </summary>
        public NodePort GetPort(string fieldName)
        {
            NodePort port;
            if (ports.TryGetValue(fieldName, out port)) return port;
            else return null;
        }

        public NodePort GetPort(int instanceID)
        {
            return ports.Values.Where(p => p.InstanceID == instanceID).FirstOrDefault();
        }

        public bool HasPort(string fieldName)
        {
            return ports.ContainsKey(fieldName);
        }
        #endregion

        #region Inputs/Outputs
        /// <summary> Return input value for a specified port. Returns fallback value if no ports are connected </summary>
        /// <param name="fieldName">Field name of requested input port</param>
        /// <param name="fallback">If no ports are connected, this value will be returned</param>
        public T GetInputValue<T>(string fieldName, T fallback = default(T))
        {
            NodePort port = GetPort(fieldName);
            if (port != null && port.IsConnected) return port.GetInputValue<T>();
            else return fallback;
        }

        /// <summary> Return all input values for a specified port. Returns fallback value if no ports are connected </summary>
        /// <param name="fieldName">Field name of requested input port</param>
        /// <param name="fallback">If no ports are connected, this value will be returned</param>
        public T[] GetInputValues<T>(string fieldName, params T[] fallback)
        {
            NodePort port = GetPort(fieldName);
            if (port != null && port.IsConnected) return port.GetInputValues<T>();
            else return fallback;
        }

        /// <summary> Returns a value based on requested port output. Should be overridden in all derived nodes with outputs. </summary>
        /// <param name="port">The requested port.</param>
        public virtual object GetValue(NodePort port)
        {
            Debug.LogWarning("No GetValue(NodePort port) override defined for " + GetType());
            return null;
        }
        #endregion

        /// <summary> Called after a connection between two <see cref="NodePort"/>s is created </summary>
        /// <param name="from">Output</param> <param name="to">Input</param>
        public virtual void OnCreateConnection(NodePort from, NodePort to) { }

        /// <summary> Called after a connection is removed from this port </summary>
        /// <param name="port">Output or Input</param>
        public virtual void OnRemoveConnection(NodePort port) { }

        /// <summary> Disconnect everything from this node </summary>
        public void ClearConnections()
        {
            foreach (NodePort port in Ports) port.ClearConnections();
        }

        #region Attributes
        /// <summary> Mark a serializable field as an input port. You can access this through <see cref="GetInputPort(string)"/> </summary>
        [AttributeUsage(AttributeTargets.Field)]
        public class InputAttribute : Attribute
        {
            public ShowBackingValue backingValue;
            public ConnectionType connectionType;
            public bool dynamicPortList;
            public TypeConstraint typeConstraint;

            /// <summary> Mark a serializable field as an input port. You can access this through <see cref="GetInputPort(string)"/> </summary>
            /// <param name="backingValue">Should we display the backing value for this port as an editor field? </param>
            /// <param name="connectionType">Should we allow multiple connections? </param>
            /// <param name="typeConstraint">Constrains which input connections can be made to this port </param>
            /// <param name="dynamicPortList">If true, will display a reorderable list of inputs instead of a single port. Will automatically add and display values for lists and arrays </param>
            public InputAttribute(ShowBackingValue backingValue = ShowBackingValue.Unconnected, ConnectionType connectionType = ConnectionType.Multiple, TypeConstraint typeConstraint = TypeConstraint.None, bool dynamicPortList = false)
            {
                this.backingValue = backingValue;
                this.connectionType = connectionType;
                this.dynamicPortList = dynamicPortList;
                this.typeConstraint = typeConstraint;
            }
        }

        /// <summary> Mark a serializable field as an output port. You can access this through <see cref="GetOutputPort(string)"/> </summary>
        [AttributeUsage(AttributeTargets.Field)]
        public class OutputAttribute : Attribute
        {
            public ShowBackingValue backingValue;
            public ConnectionType connectionType;
            public bool dynamicPortList;
            public TypeConstraint typeConstraint;

            /// <summary> Mark a serializable field as an output port. You can access this through <see cref="GetOutputPort(string)"/> </summary>
            /// <param name="backingValue">Should we display the backing value for this port as an editor field? </param>
            /// <param name="connectionType">Should we allow multiple connections? </param>
            /// <param name="typeConstraint">Constrains which input connections can be made from this port </param>
            /// <param name="dynamicPortList">If true, will display a reorderable list of outputs instead of a single port. Will automatically add and display values for lists and arrays </param>
            public OutputAttribute(ShowBackingValue backingValue = ShowBackingValue.Never, ConnectionType connectionType = ConnectionType.Multiple, TypeConstraint typeConstraint = TypeConstraint.None, bool dynamicPortList = false)
            {
                this.backingValue = backingValue;
                this.connectionType = connectionType;
                this.dynamicPortList = dynamicPortList;
                this.typeConstraint = typeConstraint;
            }
        }

        /// <summary> Manually supply node class with a context menu path </summary>
        [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
        public class CreateNodeMenuAttribute : Attribute
        {
            public string menuName;
            public int order;
            /// <summary> Manually supply node class with a context menu path </summary>
            /// <param name="menuName"> Path to this node in the context menu. Null or empty hides it. </param>
            public CreateNodeMenuAttribute(string menuName)
            {
                this.menuName = menuName;
                this.order = 0;
            }

            /// <summary> Manually supply node class with a context menu path </summary>
            /// <param name="menuName"> Path to this node in the context menu. Null or empty hides it. </param>
            /// <param name="order"> The order by which the menu items are displayed. </param>
            public CreateNodeMenuAttribute(string menuName, int order)
            {
                this.menuName = menuName;
                this.order = order;
            }
        }

        /// <summary> Prevents Node of the same type to be added more than once (configurable) to a NodeGraph </summary>
        [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
        public class DisallowMultipleNodesAttribute : Attribute
        {
            public int max;
            /// <summary> Prevents Node of the same type to be added more than once (configurable) to a NodeGraph </summary>
            /// <param name="max"> How many nodes to allow. Defaults to 1. </param>
            public DisallowMultipleNodesAttribute(int max = 1)
            {
                this.max = max;
            }
        }
        #endregion

    }
}
