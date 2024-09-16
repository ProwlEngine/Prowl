// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Editor.Preferences;
using Prowl.Editor.PropertyDrawers;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.NodeSystem;
using Prowl.Runtime.Utils;
using Prowl.Runtime.Utils.NodeSystem.Nodes;

using static Prowl.Runtime.NodeSystem.Node;

namespace Prowl.Editor;

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

    public void SetGraph(NodeGraph graph) => nodegraph = graph;
    public void SetEditor(NodeEditor editor) => this.editor = editor;
    public void SetSelectHandler(SelectHandler<WeakReference> handler) => SelectHandler = handler;

    public virtual void OnEnable() { }

    public abstract bool DrawNode(int index, Gui g, Node node);
    public virtual void OnDisable() { }
}

public class DefaultNodeEditor : ScriptedNodeEditor
{
    public override bool DrawNode(int index, Gui g, Node node)
    {
        bool changed = false;
        var itemSize = EditorStylePrefs.Instance.ItemSize;
        var roundness = (float)EditorStylePrefs.Instance.WindowRoundness;
        var nodePos = editor.GridToWindow(node.position);
        using (g.Node("Node", index).Width(node.Width).FitContentHeight().TopLeft(nodePos.x, nodePos.y).Layout(LayoutType.Column).Enter())
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
                    rect.Position += new Vector2(1, 1);
                    g.Draw2D.DrawText(node.Title, rect, Color.black);
                    g.Draw2D.DrawText(node.Title, g.CurrentNode.LayoutData.Rect);

                    if (!string.IsNullOrWhiteSpace(node.Error))
                        g.Draw2D.DrawText(FontAwesome6.CircleExclamation + node.Error, g.CurrentNode.LayoutData.GlobalPosition + new Vector2(g.CurrentNode.LayoutData.GlobalContentWidth + 5, 5), Color.red);

                    changed |= HandleNodeSelection(index, g, node);
                    changed |= HandleDraggingNode(g, node);

                    using (g.Node("In/Out").ExpandWidth().FitContentHeight().Layout(LayoutType.Row).ScaleChildren().Enter())
                    {
                        if (node.Inputs.Count() > 0)
                            changed |= DrawInputs(g, node, itemSize, true, false);

                        if (node.Outputs.Count() > 0)
                            changed |= DrawOutputs(g, node, itemSize, true, false);
                    }
                }
            }

            using (g.Node("Content").ExpandWidth().FitContentHeight().Layout(LayoutType.Column).Enter())
            {
                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.WindowBGOne, roundness, node.ShowTitle ? 12 : 15);

                changed |= HandleNodeSelection(index, g, node);
                changed |= HandleDraggingNode(g, node);

                using (g.Node("In/Out").ExpandWidth().FitContentHeight().Layout(LayoutType.Row).ScaleChildren().Enter())
                {
                    if (node.Inputs.Count() > 0)
                    {
                        changed |= DrawInputs(g, node, itemSize, false, true);
                    }

                    if (node.Outputs.Count() > 0)
                    {
                        changed |= DrawOutputs(g, node, itemSize, false, true);
                    }
                }

                using (g.Node("Fields").ExpandWidth().FitContentHeight().Layout(LayoutType.Column).Padding(5).Enter())
                {
                    // Draw Fields
                    List<MemberInfo> members = [];
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

            if (!g.IsKeyDown(Key.LeftShift))
            {
                // find the Nearest non-selected node on X Axis
                var nearestX = nodegraph.nodes.OrderBy((n) => Math.Abs(n.position.x - node.position.x)).FirstOrDefault((n) => !SelectHandler.IsSelected(new WeakReference(n)));
                if (nearestX != null)
                {
                    if (Math.Abs(nearestX.position.x - node.position.x) < 5)
                    {
                        node.position.x = nearestX.position.x;
                        var nodePos = editor.GridToWindow(node.position);
                        var nearestPos = editor.GridToWindow(nearestX.position);
                        g.Draw2D.DrawLine(nodePos, nearestPos, Color.yellow);
                    }
                }

                // Clamp max width edge as well
                var nearestMaxX = nodegraph.nodes.OrderBy((n) => Math.Abs(n.position.x + n.Width - node.position.x - node.Width)).FirstOrDefault((n) => !SelectHandler.IsSelected(new WeakReference(n)));
                if (nearestMaxX != null)
                {
                    if (Math.Abs(nearestMaxX.position.x + nearestMaxX.Width - node.position.x - node.Width) < 5)
                    {
                        node.position.x = nearestMaxX.position.x + nearestMaxX.Width - node.Width;
                        var nodePos = editor.GridToWindow(node.position);
                        var nearestPos = editor.GridToWindow(nearestMaxX.position);
                        g.Draw2D.DrawLine(nodePos + new Vector2(node.Width, 0), nearestPos + new Vector2(nearestMaxX.Width, 0), Color.yellow);
                    }
                }

                // find the Nearest non-selected node on Y Axis
                var nearestY = nodegraph.nodes.OrderBy((n) => Math.Abs(n.position.y - node.position.y)).FirstOrDefault((n) => !SelectHandler.IsSelected(new WeakReference(n)));
                if (nearestY != null)
                {
                    if (Math.Abs(nearestY.position.y - node.position.y) < 5)
                    {
                        node.position.y = nearestY.position.y;
                        var nodePos = editor.GridToWindow(node.position);
                        var nearestPos = editor.GridToWindow(nearestY.position);
                        g.Draw2D.DrawLine(nodePos, nearestPos, Color.yellow);
                    }
                }

                // Unfortunately we cant clamp the max height edge as we dont have the height of the other nodes
            }

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


    protected bool DrawOutputs(Gui g, Node node, double itemSize, bool isHeader, bool showName)
    {
        bool changed = false;
        int fieldIndex = 0;
        using (g.Node("Out").FitContentHeight().Layout(LayoutType.Column).Padding(5).Enter())
        {
            foreach (var port in node.Outputs)
            {
                if (port.IsOnHeader != isHeader)
                    continue;

                var width = g.CurrentNode.LayoutData.Rect.width;
                var textwidth = Font.DefaultFont.CalcTextSize(port.fieldName, 0).x;

                using (g.Node("OutputDummy", fieldIndex++).FitContentWidth().Height(itemSize).Enter())
                {
                    // Draw Port Name
                    if (showName)
                    {
                        var pos = g.CurrentNode.LayoutData.GlobalPosition;
                        g.Draw2D.DrawText(port.fieldName, pos + new Vector2(width - textwidth - 15, 5));
                    }

                    bool isFlow = port.ValueType == typeof(FlowNode);
                    changed |= DrawPort(g, port, isHeader ? new(width - 20, 3) : new(width - 10, 7), isFlow);
                }
            }
        }
        return changed;
    }

    protected bool DrawInputs(Gui g, Node node, double itemSize, bool isHeader, bool showName)
    {
        bool changed = false;
        using (g.Node("In").FitContentHeight().Padding(5).Enter())
        {
            // Draw Inputs
            double y = 0;
            int fieldIndex = 0;

            foreach (var port in node.Inputs)
            {
                if (port.IsOnHeader != isHeader)
                    continue;

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
                    if (showName)
                    {
                        var pos = g.CurrentNode.LayoutData.GlobalPosition;
                        g.Draw2D.DrawText(port.fieldName, pos + new Vector2(5, 5));
                    }
                    bool isFlow = port.ValueType == typeof(FlowNode);
                    changed |= DrawPort(g, port, isHeader ? new(0, 3) : new(-10, 7), isFlow);
                    //g.Draw2D.DrawCircleFilled(pos + new Vector2(-5, 12), 5, EditorStylePrefs.RandomPastel(port.ValueType, 1f));
                }

                y += itemSize;
                fieldIndex++;
            }
        }
        return changed;
    }

    protected bool DrawPort(Gui g, NodePort port, Vector2 center, bool onHeader = false)
    {
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
                if (editor.draggingPort == null || editor.draggingPort == port || (editor.draggingPort as NodePort).CanConnectTo(port))
                    col *= 2.0f;

                // If no port is being dragged and this node is active (being dragged) start a port drag
                if (editor.draggingPort == null && g.IsNodeActive())
                {
                    editor.draggingPort = port;
                    editor.reroutePoints.Clear();
                }
                else if (editor.draggingPort != null && g.IsPointerUp(MouseButton.Left))
                {
                    var dragging = (editor.draggingPort as NodePort);
                    if (editor.draggingPort != port && dragging.CanConnectTo(port))
                    {
                        dragging.Connect(port);
                        dragging.GetReroutePoints(dragging.ConnectionCount - 1).AddRange(editor.reroutePoints);
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


            if (onHeader)
            {
                g.Draw2D.DrawTriangleFilled(trueCenter, new Vector2(1, 0), 5, Color.white);
                //g.Draw2D.DrawTriangle(trueCenter, new Vector2(1, 0), 5, Color.white, 2);
            }
            else
            {
                var portCol = col;
                if (!editor.draggingPort?.CanConnectTo(port) ?? false) // Draw Connection
                    portCol *= 0.5f;
                g.Draw2D.DrawCircleFilled(trueCenter, 5, portCol);
            }

            //if (port.IsOutput)
            //{
            //    foreach (var other in port.GetConnections())
            //    {
            //        var a = trueCenter + new Vector2(50, 0);
            //        var b = other.LastKnownPosition - new Vector2(50, 0);
            //        g.Draw2D.DrawBezierLine(trueCenter, a, other.LastKnownPosition, b, onHeader ? Color.white : col, 2);
            //    }
            //}
        }

        g.Tooltip(port.ValueType.Name);

        return changed;
    }

    protected bool DrawBackingField(Gui g, Node node, int fieldIndex, NodePort port)
    {
        if (port.IsDynamic) return false;

        bool changed = false;
        var fieldInfo = GetFieldInfo(port.node.GetType(), port.fieldName);
        if (fieldInfo is null)
        {
            return changed;
        }
        InputAttribute? field = fieldInfo.GetCustomAttributes<InputAttribute>(true).FirstOrDefault();
        bool showBacking = false;
        if (field is not null && field.backingValue != ShowBackingValue.Never)
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

    protected static FieldInfo? GetFieldInfo(Type type, string fieldName)
    {
        // If we can't find field in the first run, it's probably a private field in a base class.
        FieldInfo? field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        // Search base classes for private fields only. Public fields are found above
        while (field is not null && type.BaseType is not null && (type = type.BaseType) != typeof(Node)) field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        return field;
    }

}

[NodeEditor(typeof(CommentNode))]
public class CommentNodeEditor : DefaultNodeEditor
{
    private bool isRenamingHeader;
    private bool isRenamingDesc;

    public override bool DrawNode(int index, Gui g, Node node)
    {
        var comment = node as CommentNode;

        bool changed = false;
        var itemSize = EditorStylePrefs.Instance.ItemSize;
        var roundness = (float)EditorStylePrefs.Instance.WindowRoundness;
        var nodePos = editor.GridToWindow(node.position);
        using (g.Node("Node", index).Scale(250).TopLeft(nodePos.x, nodePos.y).Layout(LayoutType.Column).ScaleChildren().Enter())
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
                if (g.IsNodeHovered() && g.IsPointerDoubleClick())
                    isRenamingDesc = true;

                if (isRenamingDesc)
                {
                    changed |= g.InputField("DescRename", ref comment.Desc, 200, Gui.InputFieldFlags.Multiline, 0, 0, Size.Percentage(1f), Size.Percentage(1f));

                    if (!g.IsPointerHovering() && (g.IsPointerClick(MouseButton.Left) || g.IsPointerClick(MouseButton.Right)))
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
    public bool IsDragging => draggingPort != null || dragSelectionStart != null;
    public bool IsDraggingPort => draggingPort != null;

    private readonly NodeGraph graph;
    private readonly NodeEditorInputHandler inputHandler;
    private readonly Gui gui;

    private Vector2 topleft;
    private double zoom = 1.0f;
    private double targetzoom = 1.0f;
    private bool hasChanged;

    private RenderTexture RenderTarget;

    internal NodePort? draggingPort;
    internal List<Vector2> reroutePoints = [];

    internal Vector2? dragSelectionStart;
    internal Rect dragSelection;

    private string _searchText = string.Empty;
    private static NodeMenuItemInfo rootMenuItem;

    private readonly SelectHandler<WeakReference> SelectHandler = new((item) => !item.IsAlive, (a, b) => ReferenceEquals(a.Target, b.Target));

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

    [OnAssemblyUnload]
    public static void OnAssemblyUnload() => rootMenuItem = null;

    public void RefreshRenderTexture(Vector2 renderSize)
    {
        RenderTarget?.Dispose();

        RenderTarget = new RenderTexture(
            (uint)renderSize.x,
            (uint)renderSize.y,
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

            if ((RenderTarget == null) || (MathD.ApproximatelyEquals(MathD.Max(width, 1), RenderTarget.Width) || MathD.ApproximatelyEquals(MathD.Max(height, 1), RenderTarget.Height)))
                RefreshRenderTexture(new(MathD.Max(width, 1), MathD.Max(height, 1)));

            Veldrid.CommandList commandList = Graphics.GetCommandList();
            commandList.Name = "Node Editor Command Buffer";

            commandList.SetFramebuffer(RenderTarget.Framebuffer);
            commandList.ClearColorTarget(0, Veldrid.RgbaFloat.Black);
            commandList.ClearDepthStencil(1.0f);

            hasChanged = false;
            gui.ProcessFrame(commandList, new Rect(0, 0, width, height), (float)zoom, Vector2.one, EditorPreferences.Instance.AntiAliasing, (g) =>
            {
                g.Draw2D.DrawRectFilled(new Rect(0, 0, width / zoom, height / zoom), EditorStylePrefs.Instance.Background);

                if (g.PointerWheel != 0)
                {
                    targetzoom = MathD.Clamp(targetzoom + g.PointerWheel * 0.1, 0.1, 2.0);
                    g.ClosePopup();
                    //offset -= (g.PointerPos - (new Vector2(width / targetzoom, height / targetzoom) / 2)) * g.PointerWheel * 0.5f;
                }
                zoom = MathD.Lerp(zoom, targetzoom, 0.1);

                if (g.IsPointerDown(MouseButton.Middle))
                {
                    topleft -= g.PointerDelta;
                    g.ClosePopup();
                }

                // Draw Connections behind nodes
                DrawConnections();
                DrawNodes(g);


                if (draggingPort != null)
                {
                    if (gui.IsPointerUp(MouseButton.Left))
                        draggingPort = null;
                    else if (gui.IsPointerClick(MouseButton.Right))
                        reroutePoints.Add(WindowToGrid(gui.PointerPos));
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

                if (!IsDragging)
                {
                    if (g.FocusID == 0)
                    {
                        KeyShortcuts(g);
                    }

                    if (g.IsNodeHovered())
                    {
                        if (!SelectHandler.SelectedThisFrame && g.IsNodePressed())
                            SelectHandler.Clear();

                        if (dragSelectionStart == null && g.IsPointerClick(MouseButton.Left))
                            dragSelectionStart = g.PointerPos;
                    }
                }
            });

            Graphics.SubmitCommandList(commandList);

            commandList.Dispose();
        }
        finally
        {
            changed = hasChanged;
            Input.PopHandler();
        }

        return RenderTarget.ColorBuffers[0];
    }

    private void KeyShortcuts(Gui g)
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

    private void DrawNodes(Gui g)
    {
        int index = 0;
        var safeNodes = graph.nodes.ToArray();
        List<int> unusedKeys = [.. customEditors.Keys];
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
                hasChanged |= customEditor.DrawNode(index++, g, node);
                unusedKeys.Remove(key);
            }
        }

        foreach (var key in unusedKeys)
        {
            customEditors[key].OnDisable();
            customEditors.Remove(key);
        }
    }

    // Make relative to window
    public Vector2 GridToWindow(Vector2 gridPos) => gridPos - topleft;

    // Make relative to grid
    public Vector2 WindowToGrid(Vector2 windowPos) => windowPos + topleft;

    private void DrawConnections()
    {
        List<Vector2> gridPoints = new(2);
        foreach (var node in graph.nodes)
        {
            //If a null node is found, return. This can happen if the nodes associated script is deleted. It is currently not possible in Unity to delete a null asset.
            if (node == null) continue;

            // Draw full connections and output > reroute
            foreach (var output in node.Outputs)
            {
                Color portColor = EditorStylePrefs.RandomPastel(output.ValueType);

                for (int k = 0; k < output.ConnectionCount; k++)
                {
                    var input = output.GetConnection(k);
                    var reroutes = output.GetReroutePoints(k);

                    // Error handling
                    if (input == null) continue; //If a script has been updated and the port doesn't exist, it is removed and null is returned. If this happens, return.
                    if (!input.IsConnectedTo(output)) input.Connect(output);

                    gridPoints.Clear();
                    gridPoints.Add(output.LastKnownPosition);
                    reroutes.ForEach((p) => gridPoints.Add(GridToWindow(p)));
                    gridPoints.Add(input.LastKnownPosition);
                    DrawNoodle(portColor, EditorStylePrefs.Instance.NoodlePathType, EditorStylePrefs.Instance.NoodleStrokeType, EditorStylePrefs.Instance.NoodleStrokeWidth, gridPoints);

                    // Loop through reroute points and draw the points
                    for (int i = 0; i < reroutes.Count; i++)
                    {
                        gui.Draw2D.DrawCircleFilled(GridToWindow(reroutes[i]), 5, portColor);
                    }
                }
            }
        }

        // Draw the noodle being dragged
        if (draggingPort != null)
        {
            var col = EditorStylePrefs.RandomPastel(draggingPort.ValueType);

            gridPoints.Clear();
            gridPoints.Add(draggingPort.LastKnownPosition);
            reroutePoints.ForEach((p) => gridPoints.Add(GridToWindow(p)));
            gridPoints.Add(gui.PointerPos);
            DrawNoodle(col, EditorStylePrefs.Instance.NoodlePathType, EditorStylePrefs.Instance.NoodleStrokeType, EditorStylePrefs.Instance.NoodleStrokeWidth, gridPoints);
        }
    }

    static Vector2 CalculateBezierPoint(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, double t)
    {
        double u = 1 - t;
        double tt = t * t, uu = u * u;
        double uuu = uu * u, ttt = tt * t;
        return new Vector2(
            (uuu * p0.x) + (3 * uu * t * p1.x) + (3 * u * tt * p2.x) + (ttt * p3.x),
            (uuu * p0.y) + (3 * uu * t * p1.y) + (3 * u * tt * p2.y) + (ttt * p3.y)
        );
    }

    public void DrawNoodle(Color color, EditorStylePrefs.NoodlePath path, EditorStylePrefs.NoodleStroke stroke, double noodleThickness, List<Vector2> gridPoints)
    {
        float thickness = (float)noodleThickness;

        int length = gridPoints.Count;
        switch (path)
        {
            case EditorStylePrefs.NoodlePath.Curvy:
                Vector2 outputTangent = Vector2.right;
                for (int i = 0; i < length - 1; i++)
                {
                    Vector2 inputTangent;
                    // Cached most variables that repeat themselves here to avoid so many indexer calls :p
                    Vector2 point_a = gridPoints[i];
                    Vector2 point_b = gridPoints[i + 1];
                    double dist_ab = Vector2.Distance(point_a, point_b);
                    if (i == 0) outputTangent = dist_ab * 0.01f * Vector2.right;
                    if (i < length - 2)
                    {
                        Vector2 point_c = gridPoints[i + 2];
                        Vector2 ab = (point_b - point_a).normalized;
                        Vector2 cb = (point_b - point_c).normalized;
                        Vector2 ac = (point_c - point_a).normalized;
                        Vector2 p = (ab + cb) * 0.5f;
                        double tangentLength = (dist_ab + Vector2.Distance(point_b, point_c)) * 0.005f;
                        double side = ((ac.x * (point_b.y - point_a.y)) - (ac.y * (point_b.x - point_a.x)));

                        p = tangentLength * MathD.Sign(side) * new Vector2(-p.y, p.x);
                        inputTangent = p;
                    }
                    else
                    {
                        inputTangent = dist_ab * 0.01f * Vector2.left;
                    }

                    // Calculates the tangents for the bezier's curves.
                    Vector2 tangent_a = point_a + outputTangent * 50;
                    Vector2 tangent_b = point_b + inputTangent * 50;
                    // Hover effect.
                    int division = MathD.RoundToInt(.2f * dist_ab) + 3;
                    // Coloring and bezier drawing.
                    int draw = 0;
                    Vector2 bezierPrevious = point_a;
                    for (int j = 1; j <= division; ++j)
                    {
                        if (stroke == EditorStylePrefs.NoodleStroke.Dashed)
                        {
                            draw++;
                            if (draw >= 2) draw = -2;
                            if (draw < 0) continue;
                            if (draw == 0) bezierPrevious = CalculateBezierPoint(point_a, tangent_a, tangent_b, point_b, (j - 1f) / division);
                        }
                        Vector2 bezierNext = CalculateBezierPoint(point_a, tangent_a, tangent_b, point_b, j / (float)division);
                        gui.Draw2D.DrawLine(bezierPrevious, bezierNext, color, thickness);
                        bezierPrevious = bezierNext;
                    }
                    outputTangent = -inputTangent;
                }
                break;
            case EditorStylePrefs.NoodlePath.Straight:
                for (int i = 0; i < length - 1; i++)
                {
                    Vector2 point_a = gridPoints[i];
                    Vector2 point_b = gridPoints[i + 1];
                    // Draws the line with the coloring.
                    Vector2 prev_point = point_a;
                    // Approximately one segment per 5 pixels
                    int segments = (int)Vector2.Distance(point_a, point_b) / 5;
                    segments = Math.Max(segments, 1);

                    int draw = 0;
                    for (int j = 0; j <= segments; j++)
                    {
                        draw++;
                        float t = j / (float)segments;
                        Vector2 lerp = Vector2.Lerp(point_a, point_b, t);
                        if (draw > 0)
                        {
                            gui.Draw2D.DrawLine(prev_point, lerp, color, thickness);
                        }
                        prev_point = lerp;
                        if (stroke == EditorStylePrefs.NoodleStroke.Dashed && draw >= 2) draw = -2;
                    }
                }
                break;
            case EditorStylePrefs.NoodlePath.Angled:
                for (int i = 0; i < length - 1; i++)
                {
                    if (i == length - 1) continue; // Skip last index
                    if (gridPoints[i].x <= gridPoints[i + 1].x - 50)
                    {
                        double midpoint = (gridPoints[i].x + gridPoints[i + 1].x) * 0.5f;
                        Vector2 start_1 = gridPoints[i];
                        Vector2 end_1 = gridPoints[i + 1];
                        start_1.x = midpoint;
                        end_1.x = midpoint;
                        if (i == length - 2)
                        {
                            gui.Draw2D.DrawLine(gridPoints[i], start_1, color, thickness);
                            gui.Draw2D.DrawLine(start_1, end_1, color, thickness);
                            gui.Draw2D.DrawLine(end_1, gridPoints[i + 1], color, thickness);
                        }
                        else
                        {
                            gui.Draw2D.DrawLine(gridPoints[i], start_1, color, thickness);
                            gui.Draw2D.DrawLine(start_1, end_1, color, thickness);
                            gui.Draw2D.DrawLine(end_1, gridPoints[i + 1], color, thickness);
                        }
                    }
                    else
                    {
                        double midpoint = (gridPoints[i].y + gridPoints[i + 1].y) * 0.5f;
                        Vector2 start_1 = gridPoints[i];
                        Vector2 end_1 = gridPoints[i + 1];
                        start_1.x += 25;
                        end_1.x -= 25;
                        Vector2 start_2 = start_1;
                        Vector2 end_2 = end_1;
                        start_2.y = midpoint;
                        end_2.y = midpoint;
                        if (i == length - 2)
                        {
                            gui.Draw2D.DrawLine(gridPoints[i], start_1, color, thickness);
                            gui.Draw2D.DrawLine(start_1, start_2, color, thickness);
                            gui.Draw2D.DrawLine(start_2, end_2, color, thickness);
                            gui.Draw2D.DrawLine(end_2, end_1, color, thickness);
                            gui.Draw2D.DrawLine(end_1, gridPoints[i + 1], color, thickness);
                        }
                        else
                        {
                            gui.Draw2D.DrawLine(gridPoints[i], start_1, color, thickness);
                            gui.Draw2D.DrawLine(start_1, start_2, color, thickness);
                            gui.Draw2D.DrawLine(start_2, end_2, color, thickness);
                            gui.Draw2D.DrawLine(end_2, end_1, color, thickness);
                            gui.Draw2D.DrawLine(end_1, gridPoints[i + 1], color, thickness);
                        }
                    }
                }
                break;
            case EditorStylePrefs.NoodlePath.ShaderLab:
                Vector2 start = gridPoints[0];
                Vector2 end = gridPoints[length - 1];
                //Modify first and last point in array so we can loop trough them nicely.
                gridPoints[0] = gridPoints[0] + Vector2.right * 20;
                gridPoints[length - 1] = gridPoints[length - 1] + Vector2.left * 20;
                //Draw first vertical lines going out from nodes
                gui.Draw2D.DrawLine(start, gridPoints[0], color, thickness);
                gui.Draw2D.DrawLine(end, gridPoints[length - 1], color, thickness);
                for (int i = 0; i < length - 1; i++)
                {
                    Vector2 point_a = gridPoints[i];
                    Vector2 point_b = gridPoints[i + 1];
                    // Draws the line with the coloring.
                    Vector2 prev_point = point_a;
                    // Approximately one segment per 5 pixels
                    int segments = (int)Vector2.Distance(point_a, point_b) / 5;
                    segments = Math.Max(segments, 1);

                    int draw = 0;
                    for (int j = 0; j <= segments; j++)
                    {
                        draw++;
                        double t = j / (float)segments;
                        Vector2 lerp = Vector2.Lerp(point_a, point_b, t);
                        if (draw > 0)
                        {
                            gui.Draw2D.DrawLine(prev_point, lerp, color, thickness);
                        }
                        prev_point = lerp;
                        if (stroke == EditorStylePrefs.NoodleStroke.Dashed && draw >= 2) draw = -2;
                    }
                }
                gridPoints[0] = start;
                gridPoints[length - 1] = end;
                break;
        }
    }

    public bool DrawBlackBoard(Gui gui)
    {
        using (gui.Node("BlackBoard").TopLeft(5).Width(250).Height(250).Enter())
        {
            object? obj = graph;
            // Get graph.parameters field
            FieldInfo props = graph.GetType().GetField("parameters", BindingFlags.Instance | BindingFlags.Public) ?? throw new Exception();
            return EditorGUI.PropertyGrid("BlackBoard", ref obj, [props], EditorGUI.PropertyGridConfig.NoHeader);
        }
    }

    public void DrawContextMenu(Gui g)
    {
        if (g.IsNodeHovered())
        {
            if (g.IsPointerClick(MouseButton.Right, true) || g.IsKeyPressed(Key.Space))
                g.OpenPopup("NodeCreatePopup", g.PointerPos);
        }

        var popupHolder = g.CurrentNode;
        if (g.BeginPopup("NodeCreatePopup", out var popup))
        {
            using (popup.Width(200).Layout(LayoutType.Column).Padding(5).Spacing(5).FitContentHeight().Enter())
            {
                g.Search("##searchBox", ref _searchText, 0, 0, Size.Percentage(1f));

                EditorGUI.Separator();

                rootMenuItem ??= GetNodeMenuTree(graph.NodeCategories, graph.NodeTypes, graph.NodeReflectionTypes);
                DrawMenuItems(rootMenuItem, g);
            }
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


    private void DrawMenuItems(NodeMenuItemInfo menuItem, Gui g)
    {
        // bool foundName = false;
        bool hasSearch = string.IsNullOrEmpty(_searchText) == false;
        foreach (var item in menuItem.Children)
        {
            if (hasSearch && (item.Name.Contains(_searchText, StringComparison.CurrentCultureIgnoreCase) == false || item.Type == null))
            {
                DrawMenuItems(item, g);
                // TODO: `foundName` is not used anywhere
                // if (hasSearch && item.Name.Equals(_searchText, StringComparison.CurrentCultureIgnoreCase))
                    // foundName = true;
                continue;
            }

            if (item.Type != null)
            {
                if (EditorGUI.StyledButton(item.Name))
                {
                    Node node = null;
                    if (item.Method != null)
                    {
                        // Reflected Node
                        var rNode = graph.AddNode<ReflectedNode>();
                        rNode.SetMethod(item.Method);
                        node = rNode;
                    }
                    else
                    {
                        node = graph.AddNode(item.Type);
                    }
                    node.position = WindowToGrid(gui.PointerPos);
                }
            }
            else
            {

                if (EditorGUI.StyledButton(item.Name))
                    g.OpenPopup(item.Name + "Popup", g.PreviousNode.LayoutData.Rect.TopRight);

                // Enter the Button's Node
                using (g.PreviousNode.Enter())
                {
                    // Draw a > to indicate a popup
                    Rect rect = g.CurrentNode.LayoutData.Rect;
                    rect.x = rect.x + rect.width - 25;
                    rect.width = 20;
                    g.Draw2D.DrawText(FontAwesome6.ChevronRight, rect, Color.white);
                }

                if (g.BeginPopup(item.Name + "Popup", out var node))
                {
                    double largestWidth = 0;
                    foreach (var child in item.Children)
                    {
                        double width = Font.DefaultFont.CalcTextSize(child.Name, 0).x + 30;
                        if (child.Type == null)
                            width += 25;
                        if (width > largestWidth)
                            largestWidth = width;
                    }

                    using (node.Width(largestWidth).Layout(LayoutType.Column).Padding(5).Spacing(5).FitContentHeight().MaxHeight(400).Scroll().Clip().Enter())
                    {
                        DrawMenuItems(item, g);
                    }
                }
            }
        }
    }

    private NodeMenuItemInfo GetNodeMenuTree(string[] nodeCategories, (string, Type)[] nodeTypes, (string, Type)[] reflectionTypes)
    {
        var allNodeTypes = AppDomain.CurrentDomain.GetAssemblies()
                                    .SelectMany(assembly => assembly.GetTypes())
                                    .Where(type => type.IsSubclassOf(typeof(Node)) && !type.IsAbstract)
                                    .ToArray();

        var items = allNodeTypes.Select(type =>
        {
            string Name = type.Name;
            var addToMenuAttribute = type.GetCustomAttribute<NodeAttribute>();
            if (addToMenuAttribute != null)
                Name = addToMenuAttribute.catagory;
            return (Name, type);
        }).ToArray();


        // Create a root MenuItemInfo object to serve as the starting point of the tree
        NodeMenuItemInfo root = new NodeMenuItemInfo { Name = "Root" };

        foreach (var (path, type) in items)
        {
            string[] parts = path.Split('/');

            // If first part is 'Hidden' then skip this node
            if (parts[0] == "Hidden") continue;

            // Make sure this root path is allowed in this graph
            if (nodeCategories != null && !nodeCategories.Contains(parts[0], StringComparer.OrdinalIgnoreCase)) continue;

            root.AddChild(path, type);
        }

        // Add Graph specific nodes to the root
        if (nodeTypes != null)
        {
            foreach (var type in nodeTypes)
                root.Children.Add(new NodeMenuItemInfo { Name = type.Item1, Type = type.Item2 });

            // Add all methods in Input class
            //foreach (var method in typeof(Input).GetMethods().Where(m => m.IsSpecialName == false && m.DeclaringType == typeof(Input)))
            //    if(ReflectedNode.IsSupported(method))
            //        root.Children.Add(new NodeMenuItemInfo { Name = ReflectedNode.GetNodeName(method), Type = typeof(Input), Method = method });

            // Add all methods in GameObject class
            //foreach (var method in typeof(GameObject).GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public).Where(m => m.IsSpecialName == false && m.DeclaringType == typeof(GameObject)))
            //    if(ReflectedNode.IsSupported(method))
            //        root.AddChild(ReflectedNode.GetNodeName(method), typeof(GameObject), method);
        }

        // Add all Reflection Types
        if (reflectionTypes != null)
        {
            foreach (var type in reflectionTypes)
                foreach (var method in type.Item2.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public).Where(m => m.IsSpecialName == false && m.DeclaringType == type.Item2))
                    if (ReflectedNode.IsSupported(method))
                        root.AddChild(type.Item1 + "/" + ReflectedNode.GetNodeName(method), type.Item2, method);
        }

        SortChildren(root);
        return root;
    }

    private void SortChildren(NodeMenuItemInfo node)
    {
        node.Children.Sort((x, y) => x.Type == null ? -1 : 1);

        foreach (var child in node.Children)
            SortChildren(child);
    }

    private class NodeMenuItemInfo
    {
        public string Name;
        public Type Type;
        public readonly MethodInfo Method;
        public readonly List<NodeMenuItemInfo> Children = [];

        public NodeMenuItemInfo() { }

        public NodeMenuItemInfo(Type type, MethodInfo method = null)
        {
            Type = type;
            Method = method;
            Name = type.Name;
            var addToMenuAttribute = type.GetCustomAttribute<NodeAttribute>();
            if (addToMenuAttribute != null)
                Name = addToMenuAttribute.catagory;
        }

        public void AddChild(string path, Type type, MethodInfo method = null)
        {
            string[] parts = path.Split('/');
            NodeMenuItemInfo currentNode = this;

            for (int i = 0; i < parts.Length - 1; i++)  // Skip the last part
            {
                string part = parts[i];
                NodeMenuItemInfo childNode = currentNode.Children.Find(c => c.Name == part);

                if (childNode == null)
                {
                    childNode = new NodeMenuItemInfo { Name = part };
                    currentNode.Children.Add(childNode);
                }

                currentNode = childNode;
            }

            NodeMenuItemInfo leafNode = new NodeMenuItemInfo(type, method)
            {
                Name = parts[^1]  // Get the last part
            };

            currentNode.Children.Add(leafNode);
        }
    }
}
