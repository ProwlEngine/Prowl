// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Docking;
using Prowl.Editor.Preferences;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;

namespace Prowl.Editor;

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
    protected Gui gui => Gui.ActiveGUI;

    private double _width, _height;
    public double _x, _y;
    private bool _wasDragged;


    public bool bAllowTabs = true;

    public Vector2 DockPosition;
    public Vector2 DockSize;

    public double MinZ = double.MaxValue;
    public double MaxZ = double.MinValue;

    public bool IsDocked => m_Leaf != null;
    private DockNode? m_Leaf;
    // private Vector2 m_DockPosition;

    public DockNode? Leaf
    {
        get => m_Leaf;
        internal set => m_Leaf = value;
    }
    public Rect Rect
    {
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
        MinZ = gui.GetCurrentInteractableZLayer();

        try
        {
            Update();
        }
        catch (Exception e)
        {
            Debug.LogError("Error in UpdateWindow: " + e.Message + "\n" + e.StackTrace);
        }

        try
        {
            isOpened = true;

            if (BackgroundFade)
            {
                gui.BlockInteractables(gui.ScreenRect);
                gui.Draw2D.DrawRectFilled(gui.ScreenRect, new System.Numerics.Vector4(0, 0, 0, 0.5f));
                // Ensure were at the start of the EditorWindows List
                EditorGuiManager.FocusWindow(this);

            }

            if (Center)
            {
                var vp_size = gui.ScreenRect.Size / 2;
                _x = vp_size.x - (_width / 2);
                _y = vp_size.y - (_height / 2);
            }

            var width = _width;
            var height = _height;
            if (IsDocked)
            {
                _x = DockPosition.x;
                _y = DockPosition.y;
                // Dock is Relative to Node, Convert to Screen Space
                _x -= gui.CurrentNode.LayoutData.Rect.x;
                _y -= gui.CurrentNode.LayoutData.Rect.y;
                width = DockSize.x;
                height = DockSize.y;
            }

            using (gui.Node("_" + Title, _id).Width(width).Height(height).Margin(EditorStylePrefs.Instance.DockSpacing).Left(_x).Top(_y).Layout(LayoutType.Column).ScaleChildren().Enter())
            {
                gui.BlockInteractables(gui.CurrentNode.LayoutData.Rect);
                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.WindowBGOne, (float)EditorStylePrefs.Instance.WindowRoundness);

                Rect = gui.CurrentNode.LayoutData.Rect;

                if (!LockSize && !IsDocked)
                    HandleResize();

                if (TitleBar)
                {
                    using (gui.Node("_Titlebar").Width(Size.Percentage(1f)).MaxHeight(40).Padding(10, 10).Enter())
                    {
                        HandleTitleBarInteraction();

                        if (IsDocked && Leaf?.LeafWindows.Count > 0)
                        {
                            double[] tabWidths = new double[Leaf.LeafWindows.Count];
                            double total = 0;
                            for (int i = 0; i < Leaf.LeafWindows.Count; i++)
                            {
                                var window = Leaf.LeafWindows[i];
                                var textSize = Font.DefaultFont.CalcTextSize(window.Title, 0);
                                tabWidths[i] = textSize.x + 20;
                                total += tabWidths[i];
                            }

                            double updatedTotal = 0;
                            if (total > gui.CurrentNode.LayoutData.Rect.width - 35)
                            {
                                for (int i = 0; i < tabWidths.Length; i++)
                                {
                                    tabWidths[i] = (tabWidths[i] / total) * (gui.CurrentNode.LayoutData.Rect.width - 35);
                                    updatedTotal += tabWidths[i];
                                }
                            }
                            else
                            {
                                updatedTotal = total;
                            }

                            double left = 0;
                            for (int i = 0; i < Leaf.LeafWindows.Count; i++)
                            {
                                var window = Leaf.LeafWindows[i];
                                var tabWidth = tabWidths[i];
                                using (gui.Node("Tab _" + window.Title, window._id).Width(tabWidth).Height(20).Left(left).Enter())
                                {
                                    left += tabWidth + 5;
                                    var tabRect = gui.CurrentNode.LayoutData.Rect;
                                    tabRect.Expand(0, 2);

                                    if (window != this)
                                    {
                                        if (gui.IsNodePressed())
                                        {
                                            Leaf.WindowNum = i;
                                            EditorGuiManager.FocusWindow(window);
                                        }
                                        if (gui.IsNodeHovered())
                                            gui.Draw2D.DrawRectFilled(tabRect, EditorStylePrefs.Instance.Borders, (float)EditorStylePrefs.Instance.TabRoundness);
                                    }
                                    if (window == this)
                                    {
                                        gui.Draw2D.DrawRectFilled(tabRect, EditorStylePrefs.Instance.Highlighted, (float)EditorStylePrefs.Instance.TabRoundness);
                                    }

                                    var textSize = Font.DefaultFont.CalcTextSize(window.Title, 0);
                                    var pos = gui.CurrentNode.LayoutData.Rect.Position;
                                    pos.x += (tabRect.width - textSize.x) * 0.5f;
                                    pos.y += (tabRect.height - (textSize.y)) * 0.5f;
                                    if (textSize.x < tabWidth - 10)
                                        gui.Draw2D.DrawText(window.Title, pos, Color.white);
                                    else
                                        gui.Draw2D.DrawText("...", new Vector2(tabRect.x + (tabRect.width * 0.5) - 5, pos.y), Color.white);

                                    // Close Button
                                    if (gui.IsNodeHovered())
                                    {
                                        using (gui.Node("_CloseButton").Width(20).Height(20).Left(Offset.Percentage(1f, -23)).Enter())
                                        {
                                            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, new Color(1, 1, 1, 150), 5);
                                            if (gui.IsPointerHovering() && gui.IsPointerClick())
                                            {
                                                //Leaf.LeafWindows.Remove(window);
                                                //EditorGuiManager.Remove(window);
                                                //EditorGuiManager.FocusWindow(Leaf.LeafWindows[Leaf.WindowNum]);
                                                if (window == this)
                                                    isOpened = false;
                                                else
                                                    EditorGuiManager.Remove(window);
                                            }
                                            gui.Draw2D.DrawText(FontAwesome6.Xmark, gui.CurrentNode.LayoutData.Rect, gui.IsPointerHovering() ? EditorStylePrefs.Instance.Hovering : Color.white);
                                        }

                                    }
                                }
                            }
                        }
                        else
                        {
                            gui.Draw2D.DrawText(Title, 20, gui.CurrentNode.LayoutData.Rect, Color.white);
                        }

                        if (!IsDocked)
                        {
                            // If the window isnt docked then theres no tab with a Close button
                            // So we need to draw the close button on the title bar instead
                            using (gui.Node("_CloseButton").Width(20).Height(20).Left(Offset.Percentage(1f, -20)).Enter())
                            {
                                if (gui.IsNodePressed())
                                    isOpened = false;
                                gui.Draw2D.DrawText(FontAwesome6.Xmark, gui.CurrentNode.LayoutData.Rect, gui.IsNodeHovered() ? EditorStylePrefs.Instance.Hovering : EditorStylePrefs.Instance.LesserText);
                            }
                        }
                    }


                    using (gui.Node("_Main").Width(Size.Percentage(1f)).Clip().Enter())
                    {
                        Draw();
                    }
                }
                else
                {
                    Draw();
                }
                gui.Draw2D.DrawRect(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Borders, 2, (float)EditorStylePrefs.Instance.WindowRoundness);
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
            Debug.LogError("Error in EditorWindow: " + e.Message + "\n" + e.StackTrace);
        }

        MaxZ = gui.GetCurrentInteractableZLayer();
    }

    private bool _wasResizing;
    private void HandleResize()
    {
        using (gui.Node("ResizeTab").TopLeft(Offset.Percentage(1f, -15)).Scale(15).IgnoreLayout().Enter())
        {
            if (gui.IsNodePressed() || gui.IsNodeActive())
            {
                if (!_wasResizing)
                {
                    _wasResizing = true;
                }
                else
                {
                    _width += gui.PointerDelta.x;
                    _height += gui.PointerDelta.y;

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

            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, gui.IsNodeHovered() ? new(1f, 1f, 1f, 0.5f) : new(1f, 1f, 1f, 0.2f), 10, 5);
        }
    }

    private void HandleTitleBarInteraction()
    {
        var titleInteract = gui.GetInteractable();
        if (EditorGuiManager.DragSplitter == null)
        {
            if (titleInteract.TakeFocus() || titleInteract.IsActive())
            {
                EditorGuiManager.FocusWindow(this);
                if (_wasDragged || gui.IsPointerMoving)
                {
                    _wasDragged = true;

                    _x += gui.PointerDelta.x;
                    _y += gui.PointerDelta.y;
                    EditorGuiManager.DraggingWindow = this;

                    if (gui.IsPointerMoving && IsDocked)
                    {
                        EditorGuiManager.Container?.DetachWindow(this);
                        // Position the window so the mouse is over the title bar
                        _x = gui.PointerPos.x - (_width / 2);
                        _y = gui.PointerPos.y - 10;
                    }

                    if (IsDockable && !IsDocked)
                    {
                        // Draw Docking Placement
                        var oldZ = gui.CurrentZIndex;
                        gui.SetZIndex(10000);
                        _ = EditorGuiManager.Container.GetPlacement(gui.PointerPos.x, gui.PointerPos.y, out var placements, out var hovered);
                        if (placements != null)
                        {
                            foreach (var possible in placements)
                            {
                                gui.Draw2D.DrawRectFilled(possible, EditorStylePrefs.Blue * 0.6f, 10);
                                gui.Draw2D.DrawRect(possible, EditorStylePrefs.Blue * 0.6f, 4, 10);
                            }
                            gui.Draw2D.DrawRect(hovered, Color.yellow, 4, 10);
                        }
                        gui.SetZIndex(oldZ);
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
                        Vector2 cursorPos = gui.PointerPos;
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
