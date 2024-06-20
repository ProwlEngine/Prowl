using Prowl.Editor.Docking;
using Prowl.Editor.Preferences;
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
        Gui = new(EditorPreferences.Instance.AntiAliasing);
        Input.OnKeyEvent += Gui.SetKeyState;
        Input.OnMouseEvent += Gui.SetPointerState;
        Gui.OnPointerPosSet += (pos) => { Input.MousePosition = pos; };
        Gui.OnCursorVisibilitySet += (visible) => { Input.SetCursorVisible(visible); };
    }

    public static void FocusWindow(EditorWindow editorWindow)
    {
        if (FocusedWindow != null && FocusedWindow.Target != editorWindow)
            Gui.ClearFocus();

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
        EditorGuiManager.Gui.ProcessFrame(screenRect, 1f, framebufferAndInputScale, EditorPreferences.Instance.AntiAliasing, (g) => {

            // Draw Background
            g.Draw2D.DrawRectFilled(g.ScreenRect, GuiStyle.Background);

            Container ??= new();
            var rect = g.ScreenRect;
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
                    if (!Gui.IsBlockedByInteractable(cursorPos))
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
                                    m_StartSplitPos = MathD.Lerp(DragSplitter.Mins.x, DragSplitter.Maxs.x, DragSplitter.SplitDistance);
                                else
                                    m_StartSplitPos = MathD.Lerp(DragSplitter.Mins.y, DragSplitter.Maxs.y, DragSplitter.SplitDistance);
                            }
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

            // Focus Windows first
            var windowList = new List<EditorWindow>(Windows);
            for (int i = 0; i < windowList.Count; i++)
            {
                var window = windowList[i];
                if (g.IsPointerHovering(window.Rect) && (g.IsPointerClick(Silk.NET.Input.MouseButton.Left) || g.IsPointerClick(Silk.NET.Input.MouseButton.Right)))
                    if (!g.IsBlockedByInteractable(g.PointerPos, window.MaxZ))
                        FocusWindow(window);
            }

            // Draw/Update Windows
            for (int i = 0; i < windowList.Count; i++)
            {
                var window = windowList[i];
                if (!window.IsDocked || window.Leaf.LeafWindows[window.Leaf.WindowNum] == window)
                {
                    g.SetZIndex(i * 100);
                    g.PushID((ulong)window._id);
                    window.ProcessFrame();
                    g.PopID();
                }

            }
            g.SetZIndex(0);
        });

        foreach (var window in WindowsToRemove)
        {
            if(window.IsDocked)
                Container.DetachWindow(window);
            if (FocusedWindow != null && FocusedWindow.Target == window)
                FocusedWindow = null;
            Windows.Remove(window);
        }
        WindowsToRemove.Clear();
    }
}
