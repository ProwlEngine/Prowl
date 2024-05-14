using Prowl.Editor.EditorGUI.Docking;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Graphics;

namespace Prowl.Editor
{
    public class EditorWindow
    {
        protected string Title = "Title";
        internal readonly int _id;

        protected virtual bool Center { get; } = false;
        protected virtual double Width { get; } = 256;
        protected virtual double Height { get; } = 256;
        protected virtual bool TitleBar { get; } = true;
        protected virtual bool IsDockable { get; } = true;
        protected virtual bool LockSize { get; } = false;
        protected virtual bool BackgroundFade { get; } = false;
        protected virtual bool RoundCorners { get; } = true;
        protected virtual double Padding { get; } = 8;

        protected bool isOpened = true;
        protected Runtime.GUI.Gui g => Runtime.GUI.Gui.ActiveGUI;

        private double _width, _height;
        public double _x, _y;
        private bool _wasDragged = false;


        public bool bAllowTabs = true;

        public Vector2 DockPosition;
        public Vector2 DockSize;

        public bool IsDocked => m_Leaf != null;
        private DockNode m_Leaf;
        private Vector2 m_DockPosition;

        public DockNode Leaf
        {
            get => m_Leaf;
            internal set => m_Leaf = value;
        }

        public EditorWindow() : base()
        {
            EditorGui.Windows.Add(this);
            _id = GetHashCode();

            _width = Width;
            _height = Height;
        }

        public void ProcessFrame()
        {
            try
            {
                Update();
            }
            catch (Exception e)
            {
                Runtime.Debug.LogError("Error in UpdateWindow: " + e.Message + "\n" + e.StackTrace);
            }

            try
            {
                isOpened = true;

                if (BackgroundFade)
                {
                    g.DrawRectFilled(g.ScreenRect, new System.Numerics.Vector4(0, 0, 0, 0.5f));
                    // Ensure were at the start of the EditorWindows List
                    EditorGui.FocusWindow(this);
                }


                if (Center)
                {
                    var vp_size = g.ScreenRect.Size / 2;
                    _x = vp_size.x - (_width / 2);
                    _y = vp_size.y - (_height / 2);
                }

                var width = _width;
                var height = _height;
                if (IsDocked)
                {
                    _x = DockPosition.x;
                    _y = DockPosition.y;
                    width = DockSize.x;
                    height = DockSize.y;
                }

                using (g.Node("_" + Title, _id).Width(width).Height(height).Padding(Padding).Left(_x).Top(_y).Layout(LayoutType.Column).AutoScaleChildren().Enter())
                {
                    g.CreateBlocker(g.CurrentNode.LayoutData.InnerRect);
                    g.DrawRectFilled(g.CurrentNode.LayoutData.InnerRect, GuiStyle.WindowBackground, 10);

                    // TODO: Resize
                    // TODO: Docking needs a way to allow floating windows to be created
                    // TODO: Docking needs a way to allow docking to the root node for easier layout management

                    if (TitleBar)
                    {
                        using (g.Node("_Titlebar").Width(Size.Percentage(1f)).MaxHeight(40).Padding(10, 10).Enter())
                        {
                            HandleTitleBarInteraction();

                            if (IsDocked && Leaf.LeafWindows.Count > 0)
                            {
                                // Draw Tabs
                                var tabWidth = (g.CurrentNode.LayoutData.InnerRect.width - 35) / Leaf.LeafWindows.Count;
                                tabWidth = Math.Min(tabWidth, 100);

                                // background rect for all tabs
                                if (Leaf.LeafWindows.Count > 1)
                                {
                                    var tabsRect = g.CurrentNode.LayoutData.InnerRect;
                                    tabsRect.x += 2;
                                    tabsRect.width = (tabWidth * Leaf.LeafWindows.Count) + 1;
                                    tabsRect.Expand(6);
                                    g.DrawRectFilled(tabsRect, GuiStyle.WindowBackground * 0.8f, 10);
                                }

                                for (int i = 0; i < Leaf.LeafWindows.Count; i++)
                                {
                                    var window = Leaf.LeafWindows[i];
                                    using (g.Node("_" + window.Title, window._id).Width(tabWidth).Height(20).Left(i * (tabWidth + 5)).Enter())
                                    {
                                        var tabRect = g.CurrentNode.LayoutData.Rect;
                                        tabRect.Expand(0, 2);

                                        if (window != this)
                                        {
                                            var interact = g.GetInteractable();
                                            if (interact.TakeFocus())
                                            {
                                                Leaf.WindowNum = i;
                                                EditorGui.FocusWindow(window);
                                            }
                                            if (interact.IsHovered())
                                                g.DrawRectFilled(tabRect, GuiStyle.Borders, 10);
                                        }
                                        if (window == this)
                                        {
                                            g.DrawRectFilled(tabRect, GuiStyle.Indigo, 10);
                                        }

                                        var textSize = UIDrawList.DefaultFont.CalcTextSize(window.Title, 0);
                                        var pos = g.CurrentNode.LayoutData.Rect.Position;
                                        pos.x += (tabRect.width - textSize.x) * 0.5f;
                                        pos.y += (tabRect.height - (textSize.y)) * 0.5f;
                                        if (textSize.x < tabWidth - 10)
                                            g.DrawText(UIDrawList.DefaultFont, window.Title, 20, pos, Color.white);
                                        else
                                            g.DrawText(UIDrawList.DefaultFont, "...", 20, new Vector2(tabRect.x + (tabRect.width * 0.5) - 5, pos.y), Color.white);

                                    }
                                }
                            }
                            else
                            {
                                g.DrawText(UIDrawList.DefaultFont, Title, 20, g.CurrentNode.LayoutData.Rect, Color.white);
                            }

                            DrawAndHandleCloseButton();
                        }


                        using (g.Node("_Main").Width(Size.Percentage(1f)).Clip().Enter())
                        {
                            Draw();
                        }
                    }
                    else
                    {
                        Draw();
                    }
                    g.DrawRect(g.CurrentNode.LayoutData.InnerRect, GuiStyle.Borders, 2, 10);
                }


                if (!isOpened)
                {
                    if (IsDocked)
                        EditorGui.Container.DetachWindow(this);
                    EditorGui.Remove(this);
                    Close();
                }
            }
            catch (Exception e)
            {
                Runtime.Debug.LogError("Error in EditorWindow: " + e.Message + "\n" + e.StackTrace);
            }
        }

        private void DrawAndHandleCloseButton()
        {
            using (g.Node("_CloseButton").Width(20).Height(20).Left(Offset.Percentage(1f, -20)).Enter())
            {
                var interact = g.GetInteractable();
                if (interact.TakeFocus())
                    isOpened = false;
                if (interact.IsHovered())
                    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, new(1f, 1f, 1f, 0.5f));
                g.DrawText(UIDrawList.DefaultFont, FontAwesome6.Xmark, 20, g.CurrentNode.LayoutData.Rect, Color.white);
            }
        }

        private void HandleTitleBarInteraction()
        {
            var titleInteract = g.GetInteractable();
            if (EditorGui.DragSplitter == null)
            {
                if (titleInteract.TakeFocus() || titleInteract.IsActive())
                {
                    EditorGui.FocusWindow(this);
                    if (_wasDragged || g.IsPointerMoving)
                    {
                        _wasDragged = true;

                        _x += Input.MouseDelta.x;
                        _y += Input.MouseDelta.y;
                        EditorGui.DraggingWindow = this;

                        if (g.IsPointerMoving && IsDocked)
                        {
                            EditorGui.Container.DetachWindow(this);
                            // Position the window so the mouse is over the title bar
                            _x = g.PointerPos.x - (_width / 2);
                            _y = g.PointerPos.y - 10;
                        }

                        if (IsDockable && !IsDocked)
                        {
                            var oldZ = g.CurrentZIndex;
                            g.SetZIndex(10000);
                            // Draw Docking Placement
                            Vector2 cursorPos = g.PointerPos;
                            DockPlacement placement = EditorGui.Container.GetPlacement(cursorPos.x, cursorPos.y);
                            if (placement)
                            {
                                g.DrawList.PathLineTo(placement.PolygonVerts[0]);
                                g.DrawList.PathLineTo(placement.PolygonVerts[1]);
                                g.DrawList.PathLineTo(placement.PolygonVerts[2]);
                                g.DrawList.PathLineTo(placement.PolygonVerts[3]);
                                g.DrawList.PathFill(UIDrawList.ColorConvertFloat4ToU32(Color.yellow * 0.5f));

                                g.DrawList.PathLineTo(placement.PolygonVerts[0]);
                                g.DrawList.PathLineTo(placement.PolygonVerts[1]);
                                g.DrawList.PathLineTo(placement.PolygonVerts[2]);
                                g.DrawList.PathLineTo(placement.PolygonVerts[3]);
                                //g.DrawList.PathLineTo(placement.PolygonVerts[0]);
                                g.DrawList.PathStroke(UIDrawList.ColorConvertFloat4ToU32(Color.yellow * 0.5f), true, 2f);
                            }
                            g.SetZIndex(oldZ);
                        }
                    }
                }
                else
                {
                    if (_wasDragged)
                    {
                        _wasDragged = false;
                        if (IsDockable && !IsDocked)
                        {
                            Vector2 cursorPos = g.PointerPos;
                            EditorGui.Container.AttachWindowAt(this, cursorPos.x, cursorPos.y);
                        }
                    }

                    if (EditorGui.DraggingWindow == this)
                    {
                        EditorGui.DraggingWindow = null;
                    }
                }
            }
        }

        protected virtual void Draw() { }
        protected virtual void Update() { }
        protected virtual void Close() { }

    }
}