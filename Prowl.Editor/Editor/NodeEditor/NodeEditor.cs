using Prowl.Editor.Preferences;
using Prowl.Editor.PropertyDrawers;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.NodeSystem;
using System.Reflection;
using static Prowl.Runtime.NodeSystem.Node;

namespace Prowl.Editor
{
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

        private Vector2? dragSelectionStart = null;
        private Rect dragSelection;
        private string? selectedCatagory = null;

        private SelectHandler<WeakReference> SelectHandler = new((item) => !item.IsAlive, (a, b) => ReferenceEquals(a.Target, b.Target));

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


        public Texture2D Update(bool focused, Vector2 screenPos, uint width, uint height, out bool changed)
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
                    foreach (var node in safeNodes)
                    {
                        hasChanged |= DrawNode(index++, g, node, offset);
                    }
                    //offset = beforeOffset;

                    if (draggingPort != null && draggingPort != null)
                    {
                        // Draw Connection
                        var port = (draggingPort!.Target as NodePort)!;
                        var col = EditorStylePrefs.RandomPastel(port.ValueType, 1f);
                        g.Draw2D.DrawLine(port.LastKnownPosition, g.PointerPos, col, 2);

                        if (g.IsPointerUp(MouseButton.Left) || !draggingPort.IsAlive)
                            draggingPort = null;
                    }

                    if (g.IsKeyDown(Key.V)) AlignVertically();
                    if (g.IsKeyDown(Key.H)) AlignHorizontally();
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

                    if (g.IsNodeHovered())
                    {
                        if (g.IsPointerClick(MouseButton.Right, true) || g.IsKeyDown(Key.Space))
                            g.OpenPopup("NodeCreatePopup", g.PointerPos);

                        if (g.IsPointerDown(MouseButton.Middle))
                            offset += g.PointerDelta;

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
                        using (popup.Width(180).Padding(5).Layout(LayoutType.Column).Spacing(5).FitContentHeight().MaxHeight(40).Scroll().Clip().Enter())
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


                    if (hasChanged)
                    {
                        graph.OnValidate();
                        foreach (var node in graph.nodes)
                            node.OnValidate();
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

        private bool DrawNode(int index, Gui g, Node node, Vector2 offset)
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
                        graph.RemoveNode(node);
                        changed = true;
                    }
                }

                if (node.ShowTitle)
                {
                    using (g.Node("Header").ExpandWidth().Height(itemSize).Enter())
                    {
                        g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, EditorStylePrefs.RandomPastel(node.GetType()), roundness, 3);
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
                            changed |= DrawOutputs(g, node);
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

        private bool HandleNodeSelection(int index, Gui g, Node node)
        {
            var roundness = (float)EditorStylePrefs.Instance.WindowRoundness;
            bool changed = false;

            if(dragSelectionStart != null && g.CurrentNode.LayoutData.Rect.Overlaps(dragSelection))
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
                        graph.CopyNode(node); close = true;
                    }

                    if (EditorGUI.StyledButton("Delete"))
                    {
                        graph.RemoveNode(node); close = true;
                    }

                    if (SelectHandler.Count > 1)
                    {
                        if (EditorGUI.StyledButton("Delete All"))
                        {
                            SelectHandler.Foreach((go) => { graph.RemoveNode(go.Target as Node); });
                            SelectHandler.Clear();
                            close = true;
                        }

                        if (close |= EditorGUI.StyledButton("Align Vertically"))
                        {
                            AlignVertically(); close = true;
                        }

                        if (close |= EditorGUI.StyledButton("Align Horizontally"))
                        {
                            AlignHorizontally(); close = true;
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

        private void AlignHorizontally()
        {
            double totalX = 0;
            SelectHandler.Foreach((go) => { totalX += (go.Target as Node).position.x; });
            double avgX = totalX / SelectHandler.Count;
            SelectHandler.Foreach((go) => { (go.Target as Node).position.x = avgX; });
        }

        private void AlignVertically()
        {
            double totalY = 0;
            SelectHandler.Foreach((go) => { totalY += (go.Target as Node).position.y; });
            double avgY = totalY / SelectHandler.Count;
            SelectHandler.Foreach((go) => { (go.Target as Node).position.y = avgY; });
        }

        private bool DrawOutputs(Gui g, Node node)
        {
            bool changed = false;
            using (g.Node("Out").FitContentHeight().Layout(LayoutType.Column).Padding(5).Enter())
            {
                foreach (var port in node.Outputs)
                {
                    // Draw Port Name
                    var pos = g.CurrentNode.LayoutData.GlobalPosition;
                    var width = g.CurrentNode.LayoutData.Rect.width;
                    var textwidth = Font.DefaultFont.CalcTextSize(port.fieldName, 0).x;
                    g.Draw2D.DrawText(port.fieldName, pos + new Vector2(width - textwidth - 10, 5));

                    changed |= DrawPort(g, port, new Vector2(width - 10, 2));
                }
            }
            return changed;
        }

        private bool DrawInputs(Gui g, Node node, double itemSize)
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
                    using (g.Node("InputBackingField", fieldIndex++).FitContentWidth().Top(y).Enter())
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

        private WeakReference? draggingPort = null;

        private bool DrawPort(Gui g, NodePort port, Vector2 center)
        {
            if(draggingPort?.IsAlive == false)
                draggingPort = null;

            bool changed = false;
            using (g.Node("Port", port.GetHashCode()).Scale(10).TopLeft(center.x, center.y).IgnoreLayout().Enter())
            {
                //g.CurrentNode.PositionRelativeTo(null);
                var trueCenter = g.CurrentNode.LayoutData.Rect.Center;
                port.LastKnownPosition = trueCenter;

                var col = EditorStylePrefs.RandomPastel(port.ValueType, 1f);
                if (g.IsNodeHovered())
                {
                    // If were hovering and not dragging port (or dragging this port) highlight it
                    if(draggingPort == null || draggingPort.Target == port || (draggingPort.Target as NodePort)!.CanConnectTo(port))
                        col *= 2.0f;

                    // If no port is being dragged and this node is active (being dragged) start a port drag
                    if (draggingPort == null && g.IsNodeActive())
                    {
                        draggingPort = new(port);
                    }
                    else if (draggingPort != null && g.IsPointerUp(MouseButton.Left))
                    {
                        if (draggingPort.Target != port && (draggingPort.Target as NodePort)!.CanConnectTo(port))
                        {
                            (draggingPort.Target as NodePort)!.Connect(port);
                            draggingPort = null;
                            changed = true;
                        }
                    }

                    if(g.IsPointerClick(MouseButton.Right, true))
                    {
                        port.ClearConnections();
                        changed = true;
                    }
                }


                if (draggingPort != null)
                {
                    if ((draggingPort.Target as NodePort)!.CanConnectTo(port)) // Draw Connection
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

            return changed;
        }

        private bool HandleDraggingNode(Gui g, Node node)
        {
            if (g.IsNodeActive())
            {
                SelectHandler.Foreach((go) =>
                {
                    var n = go.Target as Node;
                    n.position += g.PointerDelta;
                });
                //node.position += g.PointerDelta;
                return true;
            }

            return false;
        }

        private bool DrawBackingField(Gui g, Node node, int fieldIndex, NodePort port)
        {
            bool changed = false;
            var fieldInfo = GetFieldInfo(port.node.GetType(), port.fieldName);
            InputAttribute field = fieldInfo.GetCustomAttributes<InputAttribute>(true).FirstOrDefault();
            bool showBacking = true;
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

        static FieldInfo GetFieldInfo(Type type, string fieldName)
        {
            // If we can't find field in the first run, it's probably a private field in a base class.
            FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            // Search base classes for private fields only. Public fields are found above
            while (field == null && (type = type.BaseType) != typeof(Node)) field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            return field;
        }

        public void Release()
        {
            RenderTarget?.DestroyImmediate();
        }
    }
}