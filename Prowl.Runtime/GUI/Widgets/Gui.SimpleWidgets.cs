using Prowl.Runtime.GUI.Layout;

namespace Prowl.Runtime.GUI
{
    public partial class Gui
    {
        public const int ScrollVWidth = 6;
        public const int ScrollVPadding = 2;

        public void ScrollV()
        {
            var n = CurrentNode;
            CurrentNode.VScroll = GetStorage<double>("VScroll");
            //CurrentNode.PaddingRight(ScrollVWidth + padding);

            using (Node().Width(ScrollVWidth).Height(Size.Percentage(1f, -(ScrollVPadding * 2))).Left(Offset.Percentage(1f, -(ScrollVWidth + ScrollVPadding))).Top(ScrollVPadding).IgnoreLayout().Enter())
            {
                Rect scrollRect = CurrentNode.LayoutData.Rect;
                if (n.HasLayoutData)
                {
                    if (n.LayoutData.ContentRect.height > n.LayoutData.Rect.height)
                    {
                        double overflowHeight = n.LayoutData.ContentRect.height - n.LayoutData.Rect.height;

                        double scrollRatio = n.LayoutData.Rect.height / n.LayoutData.ContentRect.height;
                        double scrollBarHeight = scrollRatio * scrollRect.height;

                        double scrollBarY = (n.VScroll / overflowHeight) * (scrollRect.height - scrollBarHeight);

                        Rect barRect = new(scrollRect.x, scrollRect.y + scrollBarY, scrollRect.width, scrollBarHeight);

                        Interactable interact = GetInteractable(barRect);

                        if (interact.TakeFocus() || interact.IsActive())
                        {
                            DrawRectFilled(barRect, Color.green, 20f);
                            {
                                n.VScroll += Input.MouseDelta.y * 2f;
                                layoutDirty = true;
                            }
                        }
                        else if (interact.IsHovered()) DrawRectFilled(barRect, Color.blue, 20f);
                        else DrawRectFilled(barRect, Color.red, 20f);

                        if (IsHovering(n.LayoutData.Rect) && Input.MouseWheelDelta != 0)
                        {
                            n.VScroll -= Input.MouseWheelDelta * 10;
                            layoutDirty = true;
                        }

                        n.VScroll = Mathf.Clamp(n.VScroll, 0, overflowHeight);
                    }
                    else if (n.VScroll != 0)
                    {
                        n.VScroll = 0;
                        layoutDirty = true;
                    }
                }
            }

            SetStorage("VScroll", CurrentNode.VScroll);
        }


        public static bool Button(string? label, Offset x, Offset y, Size width, Size height, float roundness = 2f, bool invisible = false, bool repeat = false) => Button(label, x, y, width, height, out _, roundness, invisible, repeat);
        public static bool Button(string? label, Offset x, Offset y, Size width, Size height, out LayoutNode node, float roundness = 2f, bool invisible = false, bool repeat = false)
        {
            var g = Gui.ActiveGUI;
            using ((node = g.Node()).Left(x).Top(y).Width(width).Height(height).Padding(2).Enter())
            {
                Interactable interact = g.GetInteractable();

                interact.UpdateContext();

                if (!invisible)
                {
                    var col = g.ActiveID == interact.ID ? Color.green :
                              g.HoveredID == interact.ID ? Color.blue : Color.red;

                    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, col, roundness);
                    g.DrawRect(g.CurrentNode.LayoutData.Rect, Color.yellow, roundness);
                    
                    g.DrawText(label, 20, g.CurrentNode.LayoutData.InnerRect, Color.black);
                }

                if (repeat)
                    return interact.IsActive();
                return interact.TakeFocus();
            }
        }

    }
}