using Prowl.Editor.Preferences;
using Prowl.Editor.PropertyDrawers;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.NodeSystem;
using Prowl.Runtime.Utils;
using System.Reflection;
using static Prowl.Runtime.NodeSystem.Node;

namespace Prowl.Editor
{
    public class NodeEditorAttribute(Type type) : Attribute
    {
        public Type Type { get; private set; } = type;

        public static Dictionary<Type, Type> nodeEditors = [];

        [OnAssemblyLoad]
        public static void GenerateLookUp()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                foreach (var type in assembly.GetTypes())
                    if (type != null)
                    {
                        var attribute = type.GetCustomAttribute<NodeEditorAttribute>();
                        if (attribute == null) continue;
                        if (nodeEditors.TryGetValue(attribute.Type, out var oldType))
                            Debug.LogError($"Custom Node Editor Overwritten. {attribute.Type.Name} already has a Custom Node Editor: {oldType.Name}, being overwritten by: {type.Name}");
                        nodeEditors[attribute.Type] = type;
                    }
        }

        [OnAssemblyUnload]
        public static void ClearLookUp()
        {
            nodeEditors.Clear();
        }

        /// <returns>The editor type for that Extension</returns>
        public static Type? GetEditor(Type type)
        {
            if (nodeEditors.TryGetValue(type, out var editorType))
                return editorType;
            // If no direct custom editor, look for a base class custom editor
            foreach (var pair in nodeEditors)
                if (pair.Key.IsAssignableFrom(type))
                    return pair.Value;

            return typeof(DefaultNodeEditor);
        }
    }

    public abstract class ScriptedNodeEditor
    {
        protected NodeEditor editor;
        protected NodeGraph nodegraph;
        protected SelectHandler<WeakReference> SelectHandler;

        public void SetGraph(NodeGraph graph) => this.nodegraph = graph;
        public void SetEditor(NodeEditor editor) => this.editor = editor;
        public void SetSelectHandler(SelectHandler<WeakReference> handler) => SelectHandler = handler;

        public virtual void OnEnable() { }

        public abstract bool DrawNode(int index, Gui g, Node node, Vector2 offset);
        public virtual void OnDisable() { }
    }

    public class DefaultNodeEditor : ScriptedNodeEditor
    {
        public override bool DrawNode(int index, Gui g, Node node, Vector2 offset)
        {
            bool changed = false;
            var itemSize = EditorStylePrefs.Instance.ItemSize;
            var roundness = (float)EditorStylePrefs.Instance.WindowRoundness;
            using (g.Node("Node", index).Width(node.Width).FitContentHeight().TopLeft(node.position.x + offset.x, node.position.y + offset.y).Layout(LayoutType.Column).Enter())
            {
                if (SelectHandler.IsSelected(new WeakReference(node)))
                {
                    var selRect = g.CurrentNode.LayoutData.Rect;
                    selRect.Expand(5);
                    g.Draw2D.DrawRect(selRect, EditorStylePrefs.Instance.Highlighted, 3, roundness);
                    if (g.IsKeyPressed(Key.Delete))
                    {
                        nodegraph.RemoveNode(node);
                        changed = true;
                    }
                }

                if (node.ShowTitle)
                {
                    using (g.Node("Header").ExpandWidth().Height(itemSize).Enter())
                    {
                        g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, EditorStylePrefs.RandomPastel(node.GetType(), 1, 0.3f), roundness, 3);
                        var rect = g.CurrentNode.LayoutData.Rect;
                        rect.Min += new Vector2(1, 1);
                        rect.Max += new Vector2(1, 1);
                        g.Draw2D.DrawText(node.Title, rect, Color.black);
                        g.Draw2D.DrawText(node.Title, g.CurrentNode.LayoutData.Rect);

                        changed |= HandleNodeSelection(index, g, node);

                        changed |= HandleDraggingNode(g, node);
                    }
                }

                using (g.Node("Content").ExpandWidth().FitContentHeight().Layout(LayoutType.Column).Enter())
                {
                    if (node.ShowTitle)
                        g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.WindowBGOne, roundness, 12);
                    else
                        g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.WindowBGOne, roundness);

                    changed |= HandleNodeSelection(index, g, node);

                    changed |= HandleDraggingNode(g, node);

                    int fieldIndex = 0;
                    using (g.Node("In/Out").ExpandWidth().FitContentHeight().Layout(LayoutType.Row).ScaleChildren().Enter())
                    {
                        if (node.Inputs.Count() > 0)
                        {
                            changed |= DrawInputs(g, node, itemSize);
                        }

                        if (node.Outputs.Count() > 0)
                        {
                            changed |= DrawOutputs(g, node, itemSize);
                        }
                    }

                    using (g.Node("Fields").ExpandWidth().FitContentHeight().Layout(LayoutType.Column).Padding(5).Enter())
                    {
                        // Draw Fields
                        List<MemberInfo> members = new();
                        foreach (var field in node.GetSerializableFields())
                        {
                            if (field.GetCustomAttribute<InputAttribute>(true) != null) continue;
                            if (field.GetCustomAttribute<OutputAttribute>(true) != null) continue;

                            members.Add(field);
                        }
                        // Public properties with [ShowInInspector] attribute
                        foreach (var prop in node.GetType().GetProperties())
                        {
                            if (prop.GetCustomAttribute<ShowInInspectorAttribute>(true) == null) continue;
                            members.Add(prop);
                        }

                        object target = node;
                        if (EditorGUI.PropertyGrid(node.Title, ref target, members, EditorGUI.PropertyGridConfig.NoBackground | EditorGUI.PropertyGridConfig.NoHeader | EditorGUI.PropertyGridConfig.NoBorder))
                            changed |= true;
                    }
                }

            }

            return changed;
        }

        protected bool HandleDraggingNode(Gui g, Node node)
        {
            if (g.IsNodeActive())
            {
                SelectHandler.Foreach((go) =>
                {
                    var n = go.Target as Node;
                    n.position += g.PointerDelta;
                });
                return true;
            }
            return false;
        }

        protected bool HandleNodeSelection(int index, Gui g, Node node)
        {
            var roundness = (float)EditorStylePrefs.Instance.WindowRoundness;
            bool changed = false;

            if (editor.dragSelectionStart != null && g.CurrentNode.LayoutData.Rect.Overlaps(editor.dragSelection))
                SelectHandler.SelectIfNot(new WeakReference(node), true);

            if (g.IsNodePressed(true) && !SelectHandler.IsSelected(new WeakReference(node)))
                SelectHandler.Select(index, new WeakReference(node));

            if (g.IsNodeHovered() && g.IsPointerClick(MouseButton.Right, true))
                g.OpenPopup("NodeContextPopup", g.PointerPos);

            var popupHolder = g.CurrentNode;
            if (g.BeginPopup("NodeContextPopup", out var popup))
            {
                SelectHandler.SelectIfNot(new WeakReference(node));
                using (popup.Width(180).Padding(5).Layout(LayoutType.Column).Spacing(5).FitContentHeight().Enter())
                {
                    bool close = false;
                    if (EditorGUI.StyledButton("Duplicate"))
                    {
                        nodegraph.CopyNode(node); close = true;
                    }

                    if (EditorGUI.StyledButton("Delete"))
                    {
                        nodegraph.RemoveNode(node); close = true;
                    }

                    if (SelectHandler.Count > 1)
                    {
                        if (EditorGUI.StyledButton("Delete All"))
                        {
                            SelectHandler.Foreach((go) => { nodegraph.RemoveNode(go.Target as Node); });
                            SelectHandler.Clear();
                            close = true;
                        }

                        if (close |= EditorGUI.StyledButton("Align Vertically"))
                        {
                            editor.AlignSelectedVertically(); close = true;
                        }

                        if (close |= EditorGUI.StyledButton("Align Horizontally"))
                        {
                            editor.AlignSelectedHorizontally(); close = true;
                        }
                    }

                    if (close)
                    {
                        changed = true;
                        g.ClosePopup(popupHolder);
                    }
                }
            }

            return changed;
        }


        protected bool DrawOutputs(Gui g, Node node, double itemSize)
        {
            bool changed = false;
            int fieldIndex = 0;
            using (g.Node("Out").FitContentHeight().Layout(LayoutType.Column).Padding(5).Enter())
            {
                foreach (var port in node.Outputs)
                {
                    var width = g.CurrentNode.LayoutData.Rect.width;
                    var textwidth = Font.DefaultFont.CalcTextSize(port.fieldName, 0).x;

                    using (g.Node("OutputDummy", fieldIndex++).FitContentWidth().Height(itemSize).Enter())
                    {
                        // Draw Port Name
                        var pos = g.CurrentNode.LayoutData.GlobalPosition;
                        g.Draw2D.DrawText(port.fieldName, pos + new Vector2(width - textwidth - 15, 5));

                        changed |= DrawPort(g, port, new Vector2(width - 10, 7));
                    }
                }
            }
            return changed;
        }

        protected bool DrawInputs(Gui g, Node node, double itemSize)
        {
            bool changed = false;
            using (g.Node("In").FitContentHeight().Padding(5).Enter())
            {
                // Draw Inputs
                double y = 0;
                int fieldIndex = 0;

                foreach (var port in node.Inputs)
                {
                    // Draw Backing
                    using (g.Node("InputBackingField", fieldIndex++).FitContentWidth().FitContentHeight().Top(y).Enter())
                    {
                        g.CurrentNode.Left(-(g.CurrentNode.LayoutData.Rect.width + 5));
                        changed |= DrawBackingField(g, node, fieldIndex, port);
                    }

                    // Draw Port
                    var width = g.CurrentNode.LayoutData.InnerRect.width;
                    using (g.Node("Input", fieldIndex++).ExpandWidth().Height(itemSize).Top(y).Layout(LayoutType.Row).ScaleChildren().Enter())
                    {
                        var pos = g.CurrentNode.LayoutData.GlobalPosition;
                        g.Draw2D.DrawText(port.fieldName, pos + new Vector2(5, 5));
                        changed |= DrawPort(g, port, new Vector2(-10, 7));
                        //g.Draw2D.DrawCircleFilled(pos + new Vector2(-5, 12), 5, EditorStylePrefs.RandomPastel(port.ValueType, 1f));
                    }

                    y += itemSize;
                    fieldIndex++;
                }
            }
            return changed;
        }

        protected bool DrawPort(Gui g, NodePort port, Vector2 center)
        {
            if (editor.draggingPort?.IsAlive == false)
                editor.draggingPort = null;

            bool changed = false;
            using (g.Node("Port", port.GetHashCode()).Scale(10).TopLeft(center.x, center.y).IgnoreLayout().Enter())
            {
                //g.CurrentNode.PositionRelativeTo(null);
                var trueCenter = g.CurrentNode.LayoutData.Rect.Center;
                port.LastKnownPosition = trueCenter;

                var col = EditorStylePrefs.RandomPastel(port.ValueType, 1f, 0.2f);
                if (g.IsNodeHovered())
                {
                    // If were hovering and not dragging port (or dragging this port) highlight it
                    if (editor.draggingPort == null || editor.draggingPort.Target == port || (editor.draggingPort.Target as NodePort)!.CanConnectTo(port))
                        col *= 2.0f;

                    // If no port is being dragged and this node is active (being dragged) start a port drag
                    if (editor.draggingPort == null && g.IsNodeActive())
                    {
                        editor.draggingPort = new(port);
                    }
                    else if (editor.draggingPort != null && g.IsPointerUp(MouseButton.Left))
                    {
                        if (editor.draggingPort.Target != port && (editor.draggingPort.Target as NodePort)!.CanConnectTo(port))
                        {
                            (editor.draggingPort.Target as NodePort)!.Connect(port);
                            editor.draggingPort = null;
                            changed = true;
                        }
                    }

                    if (g.IsPointerClick(MouseButton.Right, true))
                    {
                        port.ClearConnections();
                        changed = true;
                    }
                }


                if (editor.draggingPort != null)
                {
                    if ((editor.draggingPort.Target as NodePort)!.CanConnectTo(port)) // Draw Connection
                        g.Draw2D.DrawCircleFilled(trueCenter, 5, col);
                    else

                        g.Draw2D.DrawCircleFilled(trueCenter, 5, col * 0.5f);
                }
                else
                {
                    g.Draw2D.DrawCircleFilled(trueCenter, 5, col);
                }

                if (port.IsOutput)
                {
                    foreach (var other in port.GetConnections())
                    {
                        var a = trueCenter + new Vector2(50, 0);
                        var b = other.LastKnownPosition - new Vector2(50, 0);
                        g.Draw2D.DrawBezierLine(trueCenter, a, other.LastKnownPosition, b, col, 2);
                    }
                }
            }

            g.Tooltip(port.ValueType.Name);

            return changed;
        }

        protected bool DrawBackingField(Gui g, Node node, int fieldIndex, NodePort port)
        {
            if (port.IsDynamic) return false;
            bool changed = false;
            var fieldInfo = GetFieldInfo(port.node.GetType(), port.fieldName);
            InputAttribute field = fieldInfo.GetCustomAttributes<InputAttribute>(true).FirstOrDefault();
            bool showBacking = false;
            if (field.backingValue != ShowBackingValue.Never)
                showBacking = field.backingValue == ShowBackingValue.Always || (field.backingValue == ShowBackingValue.Unconnected && !port.IsConnected);

            if (showBacking)
            {
                var value = fieldInfo.GetValue(node);
                if (DrawerAttribute.DrawProperty(g, fieldInfo.Name, fieldIndex, fieldInfo.FieldType, ref value, EditorGUI.PropertyGridConfig.NoLabel))
                {
                    changed |= true;

                    fieldInfo.SetValue(node, value);
                }
            }
            return changed;
        }
        protected static FieldInfo GetFieldInfo(Type type, string fieldName)
        {
            // If we can't find field in the first run, it's probably a private field in a base class.
            FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            // Search base classes for private fields only. Public fields are found above
            while (field == null && (type = type.BaseType) != typeof(Node)) field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            return field;
        }

    }

    [NodeEditor(typeof(CommentNode))]
    public class CommentNodeEditor : DefaultNodeEditor
    {
        private bool isRenamingHeader = false;
        private bool isRenamingDesc = false;

        public override bool DrawNode(int index, Gui g, Node node, Vector2 offset)
        {
            var comment = node as CommentNode;

            bool changed = false;
            var itemSize = EditorStylePrefs.Instance.ItemSize;
            var roundness = (float)EditorStylePrefs.Instance.WindowRoundness;
            using (g.Node("Node", index).Scale(250).TopLeft(node.position.x + offset.x, node.position.y + offset.y).Layout(LayoutType.Column).ScaleChildren().Enter())
            {
                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, new Color32(252, 215, 110, 255), roundness);

                if (SelectHandler.IsSelected(new WeakReference(node)))
                {
                    var selRect = g.CurrentNode.LayoutData.Rect;
                    selRect.Expand(5);
                    g.Draw2D.DrawRect(selRect, EditorStylePrefs.Instance.Highlighted, 3, roundness);
                    if (g.IsKeyPressed(Key.Delete))
                    {
                        nodegraph.RemoveNode(node);
                        changed = true;
                    }
                }

                using (g.Node("Header").ExpandWidth().MaxHeight(itemSize * 2).Clip().Enter())
                {
                    if (g.IsNodeHovered() && g.IsPointerDoubleClick())
                        isRenamingHeader = true;

                    
                    if (isRenamingHeader)
                    {
                        changed |= g.InputField("HeaderRename", ref comment.Header, 200, Gui.InputFieldFlags.None, 0, 0, Size.Percentage(1f), Size.Percentage(1f));

                        if (!g.IsPointerHovering() && (g.IsPointerClick(MouseButton.Left) || g.IsPointerClick(MouseButton.Right)))
                            isRenamingHeader = false;
                    }
                    else
                    {
                        g.Draw2D.DrawText(comment.Header, 32, g.CurrentNode.LayoutData.Rect, Color.black * 0.5f);
                    }


                    changed |= HandleNodeSelection(index, g, node);

                    changed |= HandleDraggingNode(g, node);
                }

                using (g.Node("Desc").ExpandWidth().Padding(10).Layout(LayoutType.Column).Clip().Enter())
                {
                    if(g.IsNodeHovered() && g.IsPointerDoubleClick())
                        isRenamingDesc = true;

                    if (isRenamingDesc)
                    {
                        changed |= g.InputField("DescRename", ref comment.Desc, 200, Gui.InputFieldFlags.Multiline, 0, 0, Size.Percentage(1f), Size.Percentage(1f));

                        if(!g.IsPointerHovering() && (g.IsPointerClick(MouseButton.Left) || g.IsPointerClick(MouseButton.Right)))
                            isRenamingDesc = false;
                    }
                    else
                    {
                        g.Draw2D.DrawText(comment.Desc, 25, g.CurrentNode.LayoutData.GlobalContentPosition, Color.black * 0.4f, g.CurrentNode.LayoutData.GlobalContentWidth);
                    }

                    changed |= HandleNodeSelection(index, g, node);

                    changed |= HandleDraggingNode(g, node);
                }

            }

            return changed;
        }
    }

    public class NodeEditor
    {
        private NodeGraph graph;
        private NodeEditorInputHandler inputHandler;
        private Gui gui;

        private Vector2 offset;
        private double zoom = 1.0f;
        private double targetzoom = 1.0f;
        private bool hasChanged = false;

        private RenderTexture RenderTarget;

        internal WeakReference? draggingPort = null;

        internal Vector2? dragSelectionStart = null;
        internal Rect dragSelection;
        private string? selectedCatagory = null;

        private SelectHandler<WeakReference> SelectHandler = new((item) => !item.IsAlive, (a, b) => ReferenceEquals(a.Target, b.Target));

        private readonly Dictionary<int, ScriptedNodeEditor> customEditors = [];

        public NodeEditor(NodeGraph graph)
        {
            this.graph = graph;

            inputHandler = new NodeEditorInputHandler();

            Input.PushHandler(inputHandler);
            gui = new Gui(EditorPreferences.Instance.AntiAliasing);
            Input.OnKeyEvent += gui.SetKeyState;
            Input.OnMouseEvent += gui.SetPointerState;
            gui.OnPointerPosSet += (pos) => { Input.MousePosition = pos; };
            gui.OnCursorVisibilitySet += (visible) => { Input.CursorVisible = visible; };
            Input.PopHandler();
        }

        ~NodeEditor()
        {
            inputHandler.Dispose();
        }

        public void RefreshRenderTexture(Vector2 renderSize)
        {
            RenderTarget?.Dispose();

            RenderTarget = new RenderTexture(
                (uint)renderSize.x,
                (uint)renderSize.y,
                [Veldrid.PixelFormat.R8_G8_B8_A8_UNorm],
                Veldrid.PixelFormat.D32_Float,
                true);
        }

        public void SetFocus(bool focused) => inputHandler.IsFocused = focused;


        public Texture2D Update(Gui windowGui, bool focused, Vector2 screenPos, uint width, uint height, out bool changed)
        {
            Input.PushHandler(inputHandler);

            SelectHandler.StartFrame();
            try
            {
                inputHandler.position = screenPos;
                inputHandler.IsFocused = focused;
                inputHandler.EarlyUpdate();

                gui.PointerWheel = Input.MouseWheelDelta;

                if ((RenderTarget == null) || (MathD.Max(width, 1) != RenderTarget.Width || MathD.Max(height, 1) != RenderTarget.Height))
                    RefreshRenderTexture(new(MathD.Max(width, 1), MathD.Max(height, 1)));

                CommandBuffer commandBuffer = CommandBufferPool.Get("Node Editor Command Buffer");

                commandBuffer.SetRenderTarget(RenderTarget);
                commandBuffer.ClearRenderTarget(true, true, Color.black, depth: 1.0f);

                    hasChanged = false;
                gui.ProcessFrame(commandBuffer, new Rect(0, 0, width, height), (float)zoom, Vector2.one, EditorPreferences.Instance.AntiAliasing, (g) =>
                {
                    g.Draw2D.DrawRectFilled(new Rect(0, 0, width / zoom, height / zoom), EditorStylePrefs.Instance.Background);

                    if (g.PointerWheel != 0)
                    {
                        targetzoom = MathD.Clamp(targetzoom + g.PointerWheel * 0.1, 0.1, 2.0);
                        g.ClosePopup();
                        //offset -= (g.PointerPos - (new Vector2(width / targetzoom, height / targetzoom) / 2)) * g.PointerWheel * 0.5f;
                    }

                    zoom = MathD.Lerp(zoom, targetzoom, 0.1);

                    int index = 0;
                    var beforeOffset = offset;
                    //offset += new Vector2(width / zoom, height / zoom) / 2;
                    var safeNodes = graph.nodes.ToArray();
                    List<int> unusedKeys = new(customEditors.Keys);
                    foreach (var node in safeNodes)
                    {
                        var key = node.GetHashCode();
                        if (!customEditors.TryGetValue(key, out var customEditor))
                        {
                            Type? editorType = NodeEditorAttribute.GetEditor(node.GetType());
                            if (editorType != null)
                            {
                                customEditor = (ScriptedNodeEditor)Activator.CreateInstance(editorType);
                                customEditor.SetEditor(this);
                                customEditor.SetGraph(graph);
                                customEditor.SetSelectHandler(SelectHandler);
                                customEditor.OnEnable();
                                customEditors[key] = customEditor;
                                unusedKeys.Remove(key);
                            }
                            else
                            {
                                gui.Draw2D.DrawText($"No Editor for Node {node.GetType().Name}", node.position, Color.red);
                            }
                        }
                        else
                        {
                            // We are still editing the same object
                            hasChanged |= customEditor.DrawNode(index++, g, node, offset);
                            unusedKeys.Remove(key);
                        }
                    }

                    foreach(var key in unusedKeys)
                    {
                        customEditors[key].OnDisable();
                        customEditors.Remove(key);
                    }
                    //offset = beforeOffset;

                    if (draggingPort != null && draggingPort != null)
                    {
                        // Draw Connection
                        var port = (draggingPort!.Target as NodePort)!;
                        var col = EditorStylePrefs.RandomPastel(port.ValueType, 1f, 0.2f);
                        g.Draw2D.DrawLine(port.LastKnownPosition, g.PointerPos, col, 2);

                        if (g.IsPointerUp(MouseButton.Left) || !draggingPort.IsAlive)
                            draggingPort = null;
                    }

                    if (g.FocusID == 0)
                    {
                        if (g.IsKeyPressed(Key.C))
                        {
                            var comment = graph.AddNode<CommentNode>();
                            comment.Header = "This a Comment :D";
                            comment.Desc = "This is a Description";
                            comment.position = g.PointerPos;
                        }

                        if (g.IsKeyPressed(Key.V)) AlignSelectedVertically();
                        if (g.IsKeyPressed(Key.H)) AlignSelectedHorizontally();
                        if (Hotkeys.IsHotkeyDown("Duplicate", new() { Key = Key.D, Ctrl = true }))
                        {
                            var newlycreated = new List<Node>();
                            SelectHandler.Foreach((go) =>
                            {
                                newlycreated.Add(graph.CopyNode(go.Target as Node));
                            });
                            SelectHandler.Clear();
                            newlycreated.ForEach((n) => SelectHandler.SelectIfNot(new WeakReference(n)));
                        }
                    }

                    if (g.IsNodeHovered())
                    {
                        if (g.IsPointerClick(MouseButton.Right, true) || g.IsKeyPressed(Key.Space))
                            g.OpenPopup("NodeCreatePopup", g.PointerPos);

                        if (g.IsPointerDown(MouseButton.Middle))
                        {
                            offset += g.PointerDelta;
                            g.ClosePopup();
                        }

                        if (!SelectHandler.SelectedThisFrame && g.IsNodePressed())
                            SelectHandler.Clear();

                        if (dragSelectionStart == null && g.IsPointerClick(MouseButton.Left))
                            dragSelectionStart = g.PointerPos;
                    }

                    if (dragSelectionStart != null && g.IsPointerDown(MouseButton.Left))
                    {
                        var min = Vector2.Min(dragSelectionStart.Value, g.PointerPos);
                        var max = Vector2.Max(dragSelectionStart.Value, g.PointerPos);
                        dragSelection = new Rect(min, max - min);

                        g.Draw2D.DrawRect(dragSelection, EditorStylePrefs.Instance.Highlighted, 2, 3);
                        g.Draw2D.DrawRectFilled(dragSelection, EditorStylePrefs.Instance.Highlighted * 0.3f, 3);
                    }
                    else
                        dragSelectionStart = null;

                    var popupHolder = g.CurrentNode;
                    if (g.BeginPopup("NodeCreatePopup", out var popup))
                    {
                        using (popup.Width(180).Padding(5).Layout(LayoutType.Column).Spacing(5).FitContentHeight().Scroll().Clip().Enter())
                        {
                            if (selectedCatagory == null)
                            {
                                foreach (var catagory in NodeAttribute.nodeCatagories.Keys)
                                    if (EditorGUI.StyledButton(catagory))
                                        selectedCatagory = catagory;
                            }
                            else
                            {
                                if (EditorGUI.StyledButton("Back"))
                                    selectedCatagory = null;

                                if (NodeAttribute.nodeCatagories.TryGetValue(selectedCatagory, out var types))
                                {
                                    foreach (var type in types)
                                        if (EditorGUI.StyledButton(type.Name))
                                        {
                                            var node = graph.AddNode(type);
                                            node.position = g.PointerPos;
                                            g.ClosePopup(popupHolder);
                                            hasChanged |= true;

                                            SelectHandler.SetSelection(new WeakReference(node));
                                        }
                                }
                            }

                            foreach (var nodeType in graph.NodeTypes)
                                if (EditorGUI.StyledButton(nodeType.Name))
                                {
                                    var node = graph.AddNode(nodeType);
                                    node.position = g.PointerPos;
                                    g.ClosePopup(popupHolder);
                                    hasChanged |= true;

                                    SelectHandler.SetSelection(new WeakReference(node));
                                }
                        }
                    }
                    else
                    {
                        selectedCatagory = null;
                    }
                });

                Graphics.ExecuteCommandBuffer(commandBuffer);

                CommandBufferPool.Release(commandBuffer);
            }
            finally
            {
                changed = hasChanged;
                Input.PopHandler();
            }

            return RenderTarget.ColorBuffers[0];
        }

        public bool DrawBlackBoard(Gui gui)
        {
            using (gui.Node("BlackBoard").TopLeft(5).Width(250).Height(250).Enter())
            {
                object? obj = graph;
                // Get graph.parameters field
                FieldInfo props = graph.GetType().GetField("parameters", BindingFlags.Instance | BindingFlags.Public);
                return EditorGUI.PropertyGrid("BlackBoard", ref obj, [props], EditorGUI.PropertyGridConfig.NoHeader);
            }
        }

        public void AlignSelectedHorizontally()
        {
            double totalX = 0;
            SelectHandler.Foreach((go) => { totalX += (go.Target as Node).position.x; });
            double avgX = totalX / SelectHandler.Count;
            SelectHandler.Foreach((go) => { (go.Target as Node).position.x = avgX; });
        }

        public void AlignSelectedVertically()
        {
            double totalY = 0;
            SelectHandler.Foreach((go) => { totalY += (go.Target as Node).position.y; });
            double avgY = totalY / SelectHandler.Count;
            SelectHandler.Foreach((go) => { (go.Target as Node).position.y = avgY; });
        }

        public void Release()
        {
            RenderTarget?.DestroyImmediate();
        }
    }
}