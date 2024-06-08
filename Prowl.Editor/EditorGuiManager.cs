using Prowl.Editor.Docking;
using Prowl.Runtime;
using Prowl.Runtime.GUI;

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
        Gui.OnPointerPosSet += (pos) => { Input.MousePosition = pos; };
        Gui.OnCursorVisibilitySet += (visible) => { Input.SetCursorVisible(visible); };
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

    public static DockNode DockWindowTo(EditorWindow window, DockNode? node, DockZone zone, double split = 0.5f)
    {
        if(node != null)
            return Container.AttachWindow(window, node, zone, split);
        else
            return Container.AttachWindow(window, Container.Root, DockZone.Center, split);
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
            g.Draw2D.DrawRectFilled(screenRect, GuiStyle.Background);

            Container ??= new();
            var rect = screenRect;
            rect.Expand(-8);
            Container.Update(rect);


            if (DragSplitter != null)
            {
                DragSplitter.GetSplitterBounds(out var bmins, out var bmaxs, 4);

                g.SetZIndex(11000);
                g.Draw2D.DrawRectFilled(Rect.CreateFromMinMax(bmins, bmaxs), Color.yellow);
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
                        g.Draw2D.DrawRectFilled(Rect.CreateFromMinMax(bmins, bmaxs), Color.yellow);
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
                    g.SetZIndex(i * 100);
                    g.PushID((ulong)window._id);
                    window.ProcessFrame();
                    g.PopID();

                    // Focus Window
                    if (g.IsPointerHovering(window.Rect) && (g.IsPointerClick(Silk.NET.Input.MouseButton.Left) || g.IsPointerClick(Silk.NET.Input.MouseButton.Right)))
                        if (!g.IsBlockedByInteractable(g.PointerPos))
                            FocusWindow(window);

                }

            }
            g.SetZIndex(0);
        });

        foreach (var window in WindowsToRemove)
        {
            if(FocusedWindow != null && FocusedWindow.Target == window)
                FocusedWindow = null;
            Windows.Remove(window);
        }
        WindowsToRemove.Clear();
    }

    #region GUI attributes

    public static bool HandleBeginGUIAttributes(object target, IEnumerable<InspectorUIAttribute> attribs)
    {
        foreach (InspectorUIAttribute guiAttribute in attribs)
            switch (guiAttribute.AttribType())
            {

                case GuiAttribType.Space:
                    break;

                case GuiAttribType.Text:
                    break;

                case GuiAttribType.Indent:
                    break;

                case GuiAttribType.ShowIf:
                    var showIf = guiAttribute as ShowIfAttribute;
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
                    break;

                case GuiAttribType.Sameline:
                    break;

                case GuiAttribType.Disabled:
                    break;

                case GuiAttribType.Header:
                    break;

                case GuiAttribType.StartGroup:
                    break;

            }
        return true;
    }

    public static void HandleEndAttributes(IEnumerable<InspectorUIAttribute> attribs)
    {
        foreach (InspectorUIAttribute guiAttribute in attribs)
            switch (guiAttribute.AttribType())
            {

                case GuiAttribType.Disabled:
                    break;

                case GuiAttribType.Unindent:
                    break;

                case GuiAttribType.EndGroup:
                    break;

                case GuiAttribType.Tooltip:
                    break;

            }
    }

    public static bool HandleAttributeButtons(object target)
    {
        //foreach (MethodInfo method in target.GetType().GetMethods())
        //{
        //    var attribute = method.GetCustomAttribute<GUIButtonAttribute>();
        //    if (attribute != null)
        //        if (ImGui.Button(attribute.buttonText))
        //        {
        //            try
        //            {
        //                method.Invoke(target, null);
        //            }
        //            catch (Exception e)
        //            {
        //                Debug.LogError("Error During ImGui Button Execution: " + e.Message + "\n" + e.StackTrace);
        //            }
        //            return true;
        //        }
        //}
        return false;
    }

    #endregion
}
