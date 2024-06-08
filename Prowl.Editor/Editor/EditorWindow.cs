using Prowl.Editor.Docking;
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

        public DockNode Leaf {
            get => m_Leaf;
            internal set => m_Leaf = value;
        }
        public Rect Rect {
            get;
            private set;
        }

        public bool IsFocused => EditorGuiManager.FocusedWindow != null && EditorGuiManager.FocusedWindow.Target == this;

        public EditorWindow() : base()
        {
            EditorGuiManager.Windows.Add(this);
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
                    g.BlockInteractables(g.ScreenRect);
                    g.Draw2D.DrawRectFilled(g.ScreenRect, new System.Numerics.Vector4(0, 0, 0, 0.5f));
                    // Ensure were at the start of the EditorWindows List
                    EditorGuiManager.FocusWindow(this);

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
                    // Dock is Relative to Node, Convert to Screen Space
                    _x -= g.CurrentNode.LayoutData.Rect.x;
                    _y -= g.CurrentNode.LayoutData.Rect.y;
                    width = DockSize.x;
                    height = DockSize.y;
                }

                using (g.Node("_" + Title, _id).Width(width).Height(height).Padding(Padding).Left(_x).Top(_y).Layout(LayoutType.Column).ScaleChildren().Enter())
                {
                    g.BlockInteractables(g.CurrentNode.LayoutData.InnerRect);
                    g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.InnerRect, GuiStyle.WindowBackground, 10);

                    Rect = g.CurrentNode.LayoutData.InnerRect;

                    if (!LockSize && !IsDocked)
                        HandleResize();

                    if (TitleBar)
                    {
                        using (g.Node("_Titlebar").Width(Size.Percentage(1f)).MaxHeight(40).Padding(10, 10).Enter())
                        {
                            HandleTitleBarInteraction();

                            if (IsDocked && Leaf.LeafWindows.Count > 0)
                            {
                                double[] tabWidths = new double[Leaf.LeafWindows.Count];
                                double total = 0;
                                for (int i = 0; i < Leaf.LeafWindows.Count; i++)
                                {
                                    var window = Leaf.LeafWindows[i];
                                    var textSize = UIDrawList.DefaultFont.CalcTextSize(window.Title, 0);
                                    tabWidths[i] = textSize.x + 20;
                                    total += tabWidths[i];
                                }
                                
                                double updatedTotal = 0;
                                if (total > g.CurrentNode.LayoutData.InnerRect.width - 35)
                                {
                                    for (int i = 0; i < tabWidths.Length; i++)
                                    {
                                        tabWidths[i] = (tabWidths[i] / total) * (g.CurrentNode.LayoutData.InnerRect.width - 35);
                                        updatedTotal += tabWidths[i];
                                    }
                                }
                                else
                                {
                                    updatedTotal = total;
                                }

                                // background rect for all tabs
                                if (Leaf.LeafWindows.Count > 1)
                                {
                                    var tabsRect = g.CurrentNode.LayoutData.InnerRect;
                                    tabsRect.x += 2;
                                    tabsRect.width = updatedTotal;
                                    tabsRect.Expand(6);
                                    g.Draw2D.DrawRectFilled(tabsRect, GuiStyle.WindowBackground * 0.8f, 10);
                                }

                                double left = 0;
                                for (int i = 0; i < Leaf.LeafWindows.Count; i++)
                                {
                                    var window = Leaf.LeafWindows[i];
                                    var tabWidth = tabWidths[i];
                                    using (g.Node("Tab _" + window.Title, window._id).Width(tabWidth).Height(20).Left(left).Enter())
                                    {
                                        left += tabWidth + 5;
                                        var tabRect = g.CurrentNode.LayoutData.Rect;
                                        tabRect.Expand(0, 2);

                                        if (window != this)
                                        {
                                            if (g.IsNodePressed())
                                            {
                                                Leaf.WindowNum = i;
                                                EditorGuiManager.FocusWindow(window);
                                            }
                                            if (g.IsNodeHovered())
                                                g.Draw2D.DrawRectFilled(tabRect, GuiStyle.Borders, 10);
                                        }
                                        if (window == this)
                                        {
                                            g.Draw2D.DrawRectFilled(tabRect, GuiStyle.Indigo, 10);
                                        }

                                        var textSize = UIDrawList.DefaultFont.CalcTextSize(window.Title, 0);
                                        var pos = g.CurrentNode.LayoutData.Rect.Position;
                                        pos.x += (tabRect.width - textSize.x) * 0.5f;
                                        pos.y += (tabRect.height - (textSize.y)) * 0.5f;
                                        if (textSize.x < tabWidth - 10)
                                            g.Draw2D.DrawText(UIDrawList.DefaultFont, window.Title, 20, pos, Color.white);
                                        else
                                            g.Draw2D.DrawText(UIDrawList.DefaultFont, "...", 20, new Vector2(tabRect.x + (tabRect.width * 0.5) - 5, pos.y), Color.white);

                                        // Close Button
                                        if (g.IsPointerHovering())
                                        {
                                            using (g.Node("_CloseButton").Width(20).Height(20).Left(Offset.Percentage(1f, -23)).Enter())
                                            {
                                                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, new Color(1, 1, 1, 150), 10);
                                                if (g.IsNodePressed())
                                                {
                                                    //Leaf.LeafWindows.Remove(window);
                                                    //EditorGuiManager.Remove(window);
                                                    //EditorGuiManager.FocusWindow(Leaf.LeafWindows[Leaf.WindowNum]);
                                                    if (window == this)
                                                        isOpened = false;
                                                    else
                                                        EditorGuiManager.Remove(window);
                                                }
                                                g.Draw2D.DrawText(UIDrawList.DefaultFont, FontAwesome6.Xmark, 20, g.CurrentNode.LayoutData.Rect, g.IsNodeHovered() ? GuiStyle.Base11 : GuiStyle.Base4);
                                            }
                                        
                                        }
                                    }
                                }
                            }
                            else
                            {
                                g.Draw2D.DrawText(UIDrawList.DefaultFont, Title, 20, g.CurrentNode.LayoutData.Rect, Color.white);
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
                    g.Draw2D.DrawRect(g.CurrentNode.LayoutData.InnerRect, GuiStyle.Borders, 2, 10);
                }


                if (!isOpened)
                {
                    if (IsDocked)
                        EditorGuiManager.Container.DetachWindow(this);
                    EditorGuiManager.Remove(this);
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
                    g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, new(1f, 1f, 1f, 0.5f));
                g.Draw2D.DrawText(UIDrawList.DefaultFont, FontAwesome6.Xmark, 20, g.CurrentNode.LayoutData.Rect, Color.white);
            }
        }

        private bool _wasResizing = false;
        private void HandleResize()
        {
            using (g.Node("ResizeTab").TopLeft(Offset.Percentage(1f, -15)).Scale(15).IgnoreLayout().Enter())
            {
                if(g.IsNodePressed() || g.IsNodeActive())
                {
                    if (!_wasResizing)
                    {
                        _wasResizing = true;
                    }
                    else
                    {
                        _width += g.PointerDelta.x;
                        _height += g.PointerDelta.y;

                        // If width or height is less than 10, move the window instead
                        if (_width < 150)
                            _width = 150;
                        if (_height < 150)
                            _height = 150;
                    }
                }
                else
                {
                    _wasResizing = false;
                }

                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, g.IsNodeHovered() ? new(1f, 1f, 1f, 0.5f) : new(1f, 1f, 1f, 0.2f), 10, 5);
            }
        }

        private void HandleTitleBarInteraction()
        {
            var titleInteract = g.GetInteractable();
            if (EditorGuiManager.DragSplitter == null)
            {
                if (titleInteract.TakeFocus() || titleInteract.IsActive())
                {
                    EditorGuiManager.FocusWindow(this);
                    if (_wasDragged || g.IsPointerMoving)
                    {
                        _wasDragged = true;

                        _x += g.PointerDelta.x;
                        _y += g.PointerDelta.y;
                        EditorGuiManager.DraggingWindow = this;

                        if (g.IsPointerMoving && IsDocked)
                        {
                            EditorGuiManager.Container.DetachWindow(this);
                            // Position the window so the mouse is over the title bar
                            _x = g.PointerPos.x - (_width / 2);
                            _y = g.PointerPos.y - 10;
                        }

                        if (IsDockable && !IsDocked)
                        {
                            // Draw Docking Placement
                            var oldZ = g.CurrentZIndex;
                            g.SetZIndex(10000);
                            _ = EditorGuiManager.Container.GetPlacement(g.PointerPos.x, g.PointerPos.y, out var placements, out var hovered);
                            if (placements != null)
                            {
                                foreach (var possible in placements)
                                {
                                    g.Draw2D.DrawRectFilled(possible, GuiStyle.Blue * 0.6f, 10);
                                    g.Draw2D.DrawRect(possible, GuiStyle.Blue * 0.6f, 4, 10);
                                }
                                g.Draw2D.DrawRect(hovered, Color.yellow, 4, 10);
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
                            EditorGuiManager.Container.AttachWindowAt(this, cursorPos.x, cursorPos.y);
                        }
                    }

                    if (EditorGuiManager.DraggingWindow == this)
                    {
                        EditorGuiManager.DraggingWindow = null;
                    }
                }
            }
        }

        protected virtual void Draw() { }
        protected virtual void Update() { }
        protected virtual void Close() { }

    }
}