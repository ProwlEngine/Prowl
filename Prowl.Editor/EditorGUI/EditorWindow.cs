using Prowl.Editor.EditorGUI.Docking;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Graphics;
using System.ComponentModel;

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

        public DockNode Leaf {
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
                if(IsDocked)
                {
                    _x = DockPosition.x;
                    _y = DockPosition.y;
                    width = DockSize.x;
                    height = DockSize.y;
                }

                using (g.Node().Width(width).Height(height).Left(_x).Top(_y).Layout(LayoutType.Column).AutoScaleChildren().Enter())
                {
                    // TODO: Resize

                    if (TitleBar)
                    {
                        using (g.Node().Width(Size.Percentage(1f)).MaxHeight(20).Enter())
                        {
                            g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.HeaderColor);

                            if (IsDocked && Leaf.LeafWindows.Count > 0)
                            {
                                // Draw Tabs
                                var tabWidth = g.CurrentNode.LayoutData.Rect.width / Leaf.LeafWindows.Count;
                                tabWidth = Math.Min(tabWidth, 75);

                                for (int i = 0; i < Leaf.LeafWindows.Count; i++)
                                {
                                    var window = Leaf.LeafWindows[i];
                                    using (g.Node().Width(tabWidth).Height(20).Left(i * (tabWidth + 5)).Enter())
                                    {
                                        if (window != this)
                                        {
                                            var interact = g.GetInteractable();
                                            if (interact.TakeFocus())
                                            {
                                                Leaf.WindowNum = i;
                                            }
                                            if (interact.IsHovered())
                                                g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, new(1f, 1f, 1f, 0.5f));
                                        }
                                        g.DrawText(UIDrawList.DefaultFont, window.Title, 20, g.CurrentNode.LayoutData.Rect, Color.white);
                                    }
                                }
                            }
                            else
                            {
                                g.DrawText(UIDrawList.DefaultFont, Title, 20, g.CurrentNode.LayoutData.Rect, Color.white);
                            }

                            // Close Button
                            using (g.Node().Width(20).Height(20).Left(Offset.Percentage(1f, -20)).Enter())
                            {
                                var interact = g.GetInteractable();
                                if (interact.TakeFocus())
                                    isOpened = false;
                                if (interact.IsHovered())
                                    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, new(1f, 1f, 1f, 0.5f));
                                g.DrawText(UIDrawList.DefaultFont, FontAwesome6.X, 20, g.CurrentNode.LayoutData.Rect, Color.white);
                            }

                            var titleInteract = g.GetInteractable();
                            if (EditorGui.DragSplitter == null)
                            {
                                if (titleInteract.TakeFocus() || titleInteract.IsActive())
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

                        using (g.Node().Width(Size.Percentage(1f)).Clip().Enter())
                        {
                            g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.WindowBGColor);
                            Draw();
                        }
                    }
                    else
                    {
                        g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.WindowBGColor);
                        Draw();
                    }
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

        protected virtual void Draw() { }
        protected virtual void Update() { }
        protected virtual void Close() { }

    }
}