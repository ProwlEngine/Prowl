using Hexa.NET.ImGui;
using Prowl.Editor.Docking;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Silk.NET.Core;
using System.Reflection;

namespace Prowl.Editor;

public static class EditorGuiManager
{
    public static System.Numerics.Vector4 SelectedColor => new System.Numerics.Vector4(0.06f, 0.53f, 0.98f, 1.00f);

    public static Runtime.GUI.Gui Gui;
    public static DockContainer Container;
    public static EditorWindow DraggingWindow;
    public static DockNode DragSplitter;
    private static Vector2 m_DragPos;
    private static double m_StartSplitPos;

    public static WeakReference FocusedWindow;

    public static List<EditorWindow> Windows = [];

    static List<EditorWindow> WindowsToRemove = [];

    public static void Initialize()
    {
        Gui = new();
        Input.OnKeyEvent += Gui.SetKeyState;
        Input.OnMouseEvent += Gui.SetPointerState;
    }

    public static void FocusWindow(EditorWindow editorWindow)
    {
        Windows.Remove(editorWindow);
        Windows.Add(editorWindow);
        FocusedWindow = new WeakReference(editorWindow);
    }

    internal static void Remove(EditorWindow editorWindow)
    {
        WindowsToRemove.Add(editorWindow);
    }

    public static void Update()
    {
        if (FocusedWindow != null && FocusedWindow.Target != null)
            FocusWindow(FocusedWindow.Target as EditorWindow); // Ensure focused window is always on top (But below floating windows if docked)

        // Sort by docking as well, Docked windows are guranteed to come first
        Windows.Sort((a, b) => b.IsDocked.CompareTo(a.IsDocked));

        Rect screenRect = new Rect(0, 0, Runtime.Graphics.Resolution.x, Runtime.Graphics.Resolution.y);

        Vector2 framebufferAndInputScale = new((float)Window.InternalWindow.FramebufferSize.X / (float)Window.InternalWindow.Size.X, (float)Window.InternalWindow.FramebufferSize.Y / (float)Window.InternalWindow.Size.Y);

        Gui.PointerWheel = Input.MouseWheelDelta;
        EditorGuiManager.Gui.ProcessFrame(screenRect, 1f, framebufferAndInputScale, (g) => {

            // Draw Background
            g.DrawRectFilled(screenRect, GuiStyle.Background);

            Container ??= new();
            var rect = screenRect;
            rect.Expand(-8);
            Container.Update(rect);


            if (DragSplitter != null)
            {
                DragSplitter.GetSplitterBounds(out var bmins, out var bmaxs, 4);

                g.SetZIndex(11000);
                g.DrawRectFilled(Rect.CreateFromMinMax(bmins, bmaxs), Color.yellow);
                g.SetZIndex(0);

                if (!g.IsPointerDown(Silk.NET.Input.MouseButton.Left))
                    DragSplitter = null;
            }

            if (DraggingWindow == null)
            {
                Vector2 cursorPos = g.PointerPos;
                if (!g.IsPointerMoving && (g.ActiveID == 0 || g.ActiveID == null) && DragSplitter == null)
                {
                    DockNode node = Container.Root.TraceSeparator(cursorPos.x, cursorPos.y);
                    if (node != null)
                    {
                        node.GetSplitterBounds(out var bmins, out var bmaxs, 4);

                        g.SetZIndex(11000);
                        g.DrawRectFilled(Rect.CreateFromMinMax(bmins, bmaxs), Color.yellow);
                        g.SetZIndex(0);

                        if (g.IsPointerDown(Silk.NET.Input.MouseButton.Left))
                        {
                            m_DragPos = cursorPos;
                            DragSplitter = node;
                            if (DragSplitter.Type == DockNode.NodeType.SplitVertical)
                                m_StartSplitPos = Mathf.Lerp(DragSplitter.Mins.x, DragSplitter.Maxs.x, DragSplitter.SplitDistance);
                            else
                                m_StartSplitPos = Mathf.Lerp(DragSplitter.Mins.y, DragSplitter.Maxs.y, DragSplitter.SplitDistance);
                        }
                    }
                }
                else if (g.IsPointerMoving && DragSplitter != null)
                {
                    Vector2 dragDelta = cursorPos - m_DragPos;

                    if (DragSplitter.Type == DockNode.NodeType.SplitVertical)
                    {
                        double w = DragSplitter.Maxs.x - DragSplitter.Mins.x;
                        double split = m_StartSplitPos + dragDelta.x;
                        split -= DragSplitter.Mins.x;
                        split = (double)Math.Floor(split);
                        split = Math.Clamp(split, 1.0f, w - 1.0f);
                        split /= w;

                        DragSplitter.SplitDistance = split;
                    }
                    else if (DragSplitter.Type == DockNode.NodeType.SplitHorizontal)
                    {
                        double h = DragSplitter.Maxs.y - DragSplitter.Mins.y;
                        double split = m_StartSplitPos + dragDelta.y;
                        split -= DragSplitter.Mins.y;
                        split = (double)Math.Floor(split);
                        split = Math.Clamp(split, 1.0f, h - 1.0f);
                        split /= h;

                        DragSplitter.SplitDistance = split;
                    }
                }
            }

            for (int i = 0; i < Windows.Count; i++)
            {
                var window = Windows[i];
                if (!window.IsDocked || window.Leaf.LeafWindows[window.Leaf.WindowNum] == window)
                {
                    g.SetZIndex(i);
                    g.PushID((ulong)window._id);
                    window.ProcessFrame();
                    g.PopID();

                    // Focus Window
                    // If your pointer is over the window and left/right click is down, and no window's rect is above this window's rect
                    // Focus it
                    if (g.IsHovering(window.Rect) && (g.IsPointerClick(Silk.NET.Input.MouseButton.Left) || g.IsPointerClick(Silk.NET.Input.MouseButton.Right)))
                    {
                        bool focus = true;
                        for (int j = i + 1; j < Windows.Count; j++)
                        {
                            var nextWindow = Windows[j];
                            if (!nextWindow.IsDocked || nextWindow.Leaf.LeafWindows[nextWindow.Leaf.WindowNum] == nextWindow)
                                if (g.IsHovering(Windows[j].Rect))
                                {
                                    focus = false;
                                    break;
                                }
                        }
                        if (focus)
                        {
                            FocusWindow(window);
                        }
                    }

                }

            }
            g.SetZIndex(0);
        });

        foreach (var window in WindowsToRemove)
            Windows.Remove(window);
        WindowsToRemove.Clear();
    }

    #region ImGUI attributes

    public static bool HandleBeginImGUIAttributes(object target, IEnumerable<IImGUIAttri> attribs)
    {
        foreach (IImGUIAttri imGuiAttribute in attribs)
            switch (imGuiAttribute.AttribType())
            {

                case GuiAttribType.Space:
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 5);
                    break;

                case GuiAttribType.Text:
                    ImGui.TextWrapped((imGuiAttribute as TextAttribute).text);
                    break;

                case GuiAttribType.Indent:
                    ImGui.Indent((imGuiAttribute as IndentAttribute).indent);
                    break;

                case GuiAttribType.ShowIf:
                    var showIf = imGuiAttribute as ShowIfAttribute;
                    var field = target.GetType().GetField(showIf.propertyName);
                    if (field != null && field.FieldType == typeof(bool))
                    {
                        if ((bool)field.GetValue(target) == showIf.inverted)
                            return false;
                    }
                    else
                    {
                        var prop = target.GetType().GetProperty(showIf.propertyName);
                        if (prop != null && prop.PropertyType == typeof(bool))
                        {
                            if ((bool)prop.GetValue(target) == showIf.inverted)
                                return false;
                        }
                    }
                    break;

                case GuiAttribType.Separator:
                    ImGui.Separator();
                    break;

                case GuiAttribType.Sameline:
                    ImGui.SameLine();
                    break;

                case GuiAttribType.Disabled:
                    ImGui.BeginDisabled();
                    break;

                case GuiAttribType.Header:
                    ImGui.Text((imGuiAttribute as HeaderAttribute).name);
                    ImGui.Separator();
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 8);
                    break;

                case GuiAttribType.StartGroup:
                    var group = (imGuiAttribute as StartGroupAttribute);
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 10);
                    GUIHelper.TextCenter(group.name, 1f, false);
                    curGroupHeight = ImGui.GetCursorPosY();
                    ImGui.Separator();
                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 3);
                    ImGui.BeginChild(group.name, new System.Numerics.Vector2(-1, group.height), ImGuiChildFlags.Border);
                    ImGui.Indent();
                    break;

            }
        return true;
    }

    static float curGroupHeight = 0;

    public static void HandleEndImGUIAttributes(IEnumerable<IImGUIAttri> attribs)
    {
        foreach (IImGUIAttri imGuiAttribute in attribs)
            switch (imGuiAttribute.AttribType())
            {

                case GuiAttribType.Disabled:
                    ImGui.EndDisabled();
                    break;

                case GuiAttribType.Unindent:
                    ImGui.Unindent((imGuiAttribute as UnindentAttribute).unindent);
                    break;

                case GuiAttribType.EndGroup:
                    ImGui.Unindent();
                    ImGui.EndChild();

                    // Draw a background with the color of Seperators
                    float curHeight = ImGui.GetCursorPosY();
                    uint col = ImGui.GetColorU32(ImGuiCol.Separator);
                    ImGui.GetWindowDrawList().AddRect(new System.Numerics.Vector2(ImGui.GetWindowPos().X + 18, curGroupHeight), new System.Numerics.Vector2(ImGui.GetWindowPos().X + ImGui.GetWindowWidth() - 3, ImGui.GetWindowPos().Y + curHeight), col, 0f, 2);

                    ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 5);
                    break;

                case GuiAttribType.Tooltip:
                    GUIHelper.Tooltip((imGuiAttribute as TooltipAttribute).tooltip);
                    break;

            }
    }

    public static bool HandleAttributeButtons(object target)
    {
        foreach (MethodInfo method in target.GetType().GetMethods())
        {
            var attribute = method.GetCustomAttribute<ImGUIButtonAttribute>();
            if (attribute != null)
                if (ImGui.Button(attribute.buttonText))
                {
                    try
                    {
                        method.Invoke(target, null);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("Error During ImGui Button Execution: " + e.Message + "\n" + e.StackTrace);
                    }
                    return true;
                }
        }
        return false;
    }

    #endregion
}
