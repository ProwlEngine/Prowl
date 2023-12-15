using HexaEngine.ImGuiNET;
using HexaEngine.ImNodesNET;
using Prowl.Editor.PropertyDrawers;
using Prowl.Runtime;
using Prowl.Runtime.NodeSystem;
using Raylib_cs;
using System.Reflection;
using System.Text;
using static Prowl.Runtime.NodeSystem.Node;

namespace Prowl.Editor.Drawers.NodeSystem
{
    public abstract class NodeEditor
    {
        protected internal abstract Type GraphType { get; }

        static NodeEditor()
        {
            var style = ImNodes.GetStyle();
            var bg = new System.Numerics.Vector4(0.17f, 0.17f, 0.18f, 1f);
            style.Colors[(int)ImNodesCol.NodeBackground] = ImGui.GetColorU32(bg);
            style.Colors[(int)ImNodesCol.NodeBackgroundHovered] = ImGui.GetColorU32(bg * 1.5f);
            style.Colors[(int)ImNodesCol.NodeBackgroundSelected] = ImGui.GetColorU32(bg * 2f);
            style.Colors[(int)ImNodesCol.NodeOutline] = ImGui.GetColorU32(new System.Numerics.Vector4(0.10f, 0.11f, 0.11f, 0.75f));
            style.Colors[(int)ImNodesCol.TitleBar] = ImGui.GetColorU32(bg);
            style.Colors[(int)ImNodesCol.TitleBarHovered] = ImGui.GetColorU32(bg * 1.5f);
            style.Colors[(int)ImNodesCol.TitleBarSelected] = ImGui.GetColorU32(bg * 2f);
            style.Colors[(int)ImNodesCol.Link] = ImGui.GetColorU32(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1f));
            style.Colors[(int)ImNodesCol.LinkHovered] = ImGui.GetColorU32(new System.Numerics.Vector4(0.19f, 0.37f, 0.55f, 1f));
            style.Colors[(int)ImNodesCol.LinkSelected] = ImGui.GetColorU32(new System.Numerics.Vector4(0.06f, 0.53f, 0.98f, 1f));
            style.Colors[(int)ImNodesCol.Pin] = ImGui.GetColorU32(new System.Numerics.Vector4(0.1f, 0.5f, 0.1f, 1f));

            style.Colors[(int)ImNodesCol.GridBackground] = ImGui.GetColorU32(new System.Numerics.Vector4(0.1f, 0.1f, 0.15f, 1f));
            style.Colors[(int)ImNodesCol.GridLine] = ImGui.GetColorU32(new System.Numerics.Vector4(1f, 1f, 1f, 0.15f));

            style.NodeBorderThickness = 3f;
        }

        #region Graph

        public virtual bool Draw(NodeGraph graph)
        {
            bool changed = false;
            graph.SetContext();

            ImNodes.BeginNodeEditor();
            var drawlist = ImGui.GetWindowDrawList();
            foreach (var node in graph.nodes)
            {
                ImNodes.BeginNode(node.InstanceID);

                ImGui.Dummy(new System.Numerics.Vector2(node.Width, 0));

                ImNodes.BeginNodeTitleBar();
                changed |= OnDrawTitle(node);
                // Draw Line as a Seperator without ImGui.Seperator()
                var cPos = ImGui.GetCursorScreenPos();
                drawlist.AddLine(new(cPos.X, cPos.Y + 3), new(cPos.X + (ImNodes.GetNodeDimensions(node.InstanceID).X - 15), cPos.Y + 3), ImGui.GetColorU32(ImGuiCol.Text));
                ImNodes.EndNodeTitleBar();


                changed |= OnNodeDraw(node);

                ImNodes.EndNode();
                var nodePos = ImNodes.GetNodeGridSpacePos(node.InstanceID);
                if (node.position != default && nodePos == default)
                {
                    ImNodes.SetNodeGridSpacePos(node.InstanceID, node.position);
                    nodePos = node.position;
                }
                else
                {
                    var newPos = ImNodes.GetNodeGridSpacePos(node.InstanceID);
                    if (newPos != node.position.ToFloat())
                    {
                        changed = true;
                        node.position = newPos;
                    }
                }
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

            if (ImGui.IsKeyPressed(ImGuiKey.Delete))
            {
                int numLinks = ImNodes.NumSelectedLinks();
                if (numLinks > 0)
                {
                    int[] ids = new int[numLinks];
                    ImNodes.GetSelectedLinks(ref ids[0]);
                    foreach (var id in ids)
                    {
                        var nodeAndID = FindLink(graph, id);
                        if (nodeAndID.Item1 != null)
                        {
                            changed = true;
                            nodeAndID.Item1.Disconnect(nodeAndID.Item2);
                        }
                    }
                }
                int numNodes = ImNodes.NumSelectedNodes();
                if (numNodes > 0)
                {
                    int[] ids = new int[numNodes];
                    ImNodes.GetSelectedNodes(ref ids[0]);
                    foreach (var id in ids)
                    {
                        foreach (var node in graph.nodes)
                            if (node.InstanceID == id)
                            {
                                changed = true;
                                graph.RemoveNode(node);
                                break;
                            }
                    }
                }
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
                Debug.Log("Disconnected Link");
            }

            return changed;
        }

        (NodePort, int) FindLink(NodeGraph graph, int link_id)
        {
            foreach (var node in graph.nodes)
                foreach (var port in node.Ports)
                    for (int i = 0; i < port.ConnectionCount; i++)
                        if (port.GetConnectionInstanceID(i) == link_id)
                            return (port, i);
            return (null, 0);
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
            bool changed = false;
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
                    if (PropertyDrawer.Draw(node, field, node.Width))
                    {
                        changed = true;
                        node.OnValidate();
                    }
                }

            }

            foreach (var output in node.Outputs)
                changed |= OnDrawPort(output);
            foreach (var output in node.DynamicOutputs)
                changed |= OnDrawPort(output);
            return changed;
        }

        private static readonly Dictionary<Type, uint> typeColorCache = new Dictionary<Type, uint>();

        public static uint GetUniqueColorForType(Type type)
        {
            if (typeColorCache.TryGetValue(type, out uint cachedColor))
                return cachedColor;
            uint uniqueColor = GenerateUniqueColor(type);
            typeColorCache[type] = uniqueColor;
            return uniqueColor;
        }

        private static uint GenerateUniqueColor(Type type)
        {
            unchecked
            {
                var array = type.FullName.GetUTF8Bytes();
                if (array == null)
                    return 0;
                int hash = 17;
                foreach (byte element in array)
                    hash = hash * 31 + element;
                var ran = new Random(hash+5);
                float r = 0;
                float g = 0;
                float b = 0;
                ImGui.ColorConvertHSVtoRGB((float)ran.NextDouble(), 0.8f + (float)ran.NextDouble() * 0.2f, 0.8f + (float)ran.NextDouble() * 0.2f, ref r, ref g, ref b);
                return ImGui.GetColorU32(new Vector4(r, g, b, 1.0f));
            }

        }

        public virtual bool OnDrawPort(NodePort port)
        {
            // Get random color based on port.ValueType
            ImNodes.PushColorStyle(ImNodesCol.Pin, GetUniqueColorForType(port.ValueType));

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
                    if (PropertyDrawer.Draw(port.fieldName, ref value, port.node.Width))
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
