using HexaEngine.ImGuiNET;
using HexaEngine.ImNodesNET;
using Prowl.Editor.PropertyDrawers;
using Prowl.Runtime.NodeSystem;
using Prowl.Runtime;
using System.Reflection;
using static Prowl.Runtime.NodeSystem.Node;
using System.Data;
using System.Reflection.Emit;
using System.Threading.Channels;

namespace Prowl.Editor.Drawers.NodeSystem
{
    public abstract class NodeEditor
    {
        protected internal abstract Type GraphType { get; }

        #region Graph

        public virtual bool Draw(NodeGraph graph)
        {
            bool changed = false;
            graph.SetContext();

            ImNodes.BeginNodeEditor();
            foreach (var node in graph.nodes)
            {
                ImNodes.BeginNode(node.InstanceID);

                ImNodes.BeginNodeTitleBar();
                changed |= OnDrawTitle(node);
                ImNodes.EndNodeTitleBar();

                changed |= OnNodeDraw(node);

                ImNodes.EndNode();
                node.position = ImNodes.GetNodeEditorSpacePos(node.InstanceID);
            }

            foreach (var node in graph.nodes)
                foreach (var output in node.Outputs)
                {
                    int connectionCount = output.ConnectionCount;
                    for (int i = 0; i < connectionCount; i++)
                    {
                        var link = output.GetConnection(i);
                        ImNodes.Link(output.GetConnectionInstanceID(i), output.InstanceID, link.InstanceID);
                    }
                }

            if (ImNodes.IsEditorHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
                ImGui.OpenPopup("NodeCreatePopup");
            if (ImGui.BeginPopup("NodeCreatePopup"))
            {
                foreach (var nodeType in graph.NodeTypes)
                    if (ImGui.Selectable(nodeType.Name))
                    {
                        changed = true;
                        graph.AddNode(nodeType);
                    }

                ImGui.EndPopup();
            }

            ImNodes.MiniMap();
            ImNodes.EndNodeEditor();

            int start_node_id = 0;
            int start_link_id = 0;
            int end_node_id = 0;
            int end_link_id = 0;
            if (ImNodes.IsLinkCreated(ref start_node_id, ref start_link_id, ref end_node_id, ref end_link_id))
            {
                changed = true;
                var output = graph.GetNode(start_node_id);
                var end = graph.GetNode(end_node_id);
                var A = output.GetPort(start_link_id);
                var B = end.GetPort(end_link_id);
                if (A.CanConnectTo(B))
                    A.Connect(B);
            }

            int link_id = 0;
            if (ImNodes.IsLinkDestroyed(ref link_id))
            {
                changed = true;
                // Search all nodes for the destroyed link
                foreach (var node in graph.nodes)
                {
                    var port = node.GetPort(link_id);
                    if (port != null)
                        for (int i = 0; i < port.ConnectionCount; i++)
                            if (port.GetConnectionInstanceID(i) == link_id)
                            {
                                port.Disconnect(i);
                                break;
                            }
                }
            }
            return changed;
        }

        #endregion

        #region Nodes

        public virtual bool OnDrawTitle(Node node)
        {
            ImGui.Text(node.Title);
            return false;
        }

        public virtual bool OnNodeDraw(Node node)
        {
            bool changed = true;
            foreach (var input in node.Inputs)
                changed |= OnDrawPort(input);
            foreach (var input in node.DynamicInputs)
                changed |= OnDrawPort(input);

            //var width = ImNodes.GetNodeDimensions(node.InstanceID).X - 20;
            // Draw node fields that are not ports
            foreach (var field in node.GetType().GetFields(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.GetCustomAttribute<InputAttribute>(true) != null) continue;
                if (field.GetCustomAttribute<OutputAttribute>(true) != null) continue;

                if (field.FieldType.IsEnum)
                {
                    var currentEnumValue = (Enum)field.GetValue(node);

                    ImGui.SetNextItemWidth(node.Width);
                    if (ImGui.BeginCombo(field.FieldType.Name, currentEnumValue.ToString()))
                    {
                        foreach (var enumValue in Enum.GetValues(field.FieldType))
                        {
                            bool isSelected = currentEnumValue.Equals(enumValue);

                            if (ImGui.Selectable(enumValue.ToString(), isSelected))
                                field.SetValue(node, enumValue);

                            if (isSelected)
                                ImGui.SetItemDefaultFocus();
                        }

                        ImGui.EndCombo();
                    }
                }
                else
                {
                    object value = field.GetValue(node);
                    if (PropertyDrawer.Draw(node, field, node.Width))
                    {
                        changed = true;
                        node.OnValidate();
                        field.SetValue(node, value);
                    }
                }

            }

            foreach (var output in node.Outputs)
                changed |= OnDrawPort(output);
            foreach (var output in node.DynamicOutputs)
                changed |= OnDrawPort(output);
            return changed;
        }

        public virtual bool OnDrawPort(NodePort port)
        {
            bool changed = false;
            if (port.IsInput)
            {
                ImNodes.BeginInputAttribute(port.InstanceID);

                bool drawField = false;
                var fieldInfo = GetFieldInfo(port.node.GetType(), port.fieldName);
                InputAttribute input = fieldInfo.GetCustomAttributes<InputAttribute>(true).FirstOrDefault();
                if (input.backingValue != ShowBackingValue.Never)
                    drawField = input.backingValue == ShowBackingValue.Always || (input.backingValue == ShowBackingValue.Unconnected && !port.IsConnected);
                if (drawField)
                {
                    var value = fieldInfo.GetValue(port.node);
                    var width = ImNodes.GetNodeDimensions(port.node.InstanceID).X - 20;
                    if (PropertyDrawer.Draw(port.fieldName, ref value, width))
                    {
                        changed = true;
                        port.node.OnValidate();
                    }
                    fieldInfo.SetValue(port.node, value);
                }
                else
                {
                    ImGui.Text(port.fieldName);
                }

                ImNodes.EndInputAttribute();
            }
            else if (port.IsOutput)
            {
                ImNodes.BeginOutputAttribute(port.InstanceID);
                ImGui.Text(port.fieldName);
                ImNodes.EndOutputAttribute();
            }
            return changed;
        }

        FieldInfo GetFieldInfo(Type type, string fieldName)
        {
            // If we can't find field in the first run, it's probably a private field in a base class.
            FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            // Search base classes for private fields only. Public fields are found above
            while (field == null && (type = type.BaseType) != typeof(Node)) field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            return field;
        }

        #endregion
    }

    public class BaseNodeEditor : NodeEditor
    {
        protected internal override Type GraphType => typeof(NodeGraph);
    }

    public static class NodeSystemDrawer
    {
        private static readonly Dictionary<Type, NodeEditor> _NodeEditors = new();

        public static void ClearLookUp() => _NodeEditors.Clear();

        public static void GenerateLookUp()
        {
            _NodeEditors.Clear();
            foreach (Assembly editorAssembly in EditorApplication.Instance.ExternalAssemblies.Append(typeof(EditorApplication).Assembly))
            {
                List<Type> derivedTypes = Utilities.GetDerivedTypes(typeof(NodeEditor), editorAssembly);
                foreach (Type type in derivedTypes)
                {
                    try
                    {
                        NodeEditor graphEditor = Activator.CreateInstance(type) as NodeEditor ?? throw new NullReferenceException();
                        if (!_NodeEditors.TryAdd(graphEditor.GraphType, graphEditor))
                            Debug.LogWarning($"Failed to register graph editor for {type.ToString()}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Failed to register graph editor for {type.ToString()}");
                    }
                }
            }
        }

        public static bool Draw(NodeGraph graph)
        {
            var objType = graph.GetType();
            if (_NodeEditors.TryGetValue(objType, out NodeEditor? graphEditor))
                return graphEditor.Draw(graph);
            else
            {
                foreach (KeyValuePair<Type, NodeEditor> pair in _NodeEditors)
                    if (pair.Key.IsAssignableFrom(objType))
                        return pair.Value.Draw(graph);
                Debug.LogWarning($"No graph editor found for {graph.GetType()}");
                return false;
            }
        }

    }
}
