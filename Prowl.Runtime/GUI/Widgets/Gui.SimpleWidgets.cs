using Prowl.Icons;
using Prowl.Runtime.GUI.Graphics;
using Prowl.Runtime.GUI.Layout;
using System;

namespace Prowl.Runtime.GUI
{
    public partial class Gui
    {
        public const int ScrollVWidth = 6;
        public const int ScrollVPadding = 2;

        public double VScrollBarWidth() => CurrentNode.LayoutData.ContentRect.height > CurrentNode.LayoutData.Rect.height ? ScrollVWidth + (ScrollVPadding * 5) : 0;

        public void ScrollV(GuiStyle? style = null)
        {
            style ??= new();
            var n = CurrentNode;
            CurrentNode.VScroll = GetNodeStorage<double>("VScroll");
            //CurrentNode.PaddingRight(ScrollVWidth + ScrollVPadding);

            using (Node("_VScroll").Width(ScrollVWidth).Height(Size.Percentage(1f, -(ScrollVPadding * 2))).Left(Offset.Percentage(1f, -(ScrollVWidth + ScrollVPadding))).Top(ScrollVPadding).IgnoreLayout().Enter())
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
                            Draw2D.DrawRectFilled(barRect, style.ScrollBarActiveColor, style.ScrollBarRoundness);
                            {
                                n.VScroll += PointerDelta.y * 2f;
                                layoutDirty = true;
                            }
                        }
                        else if (interact.IsHovered()) Draw2D.DrawRectFilled(barRect, style.ScrollBarHoveredColor, (float)style.ScrollBarRoundness);
                        else Draw2D.DrawRectFilled(barRect, style.WidgetColor, style.ScrollBarRoundness);

                        if (IsPointerHovering(n.LayoutData.Rect) && PointerWheel != 0)
                        {
                            n.VScroll -= PointerWheel * 10;
                            layoutDirty = true;
                        }

                        n.VScroll = MathD.Clamp(n.VScroll, 0, overflowHeight);
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

        public LayoutNode TextNode(string id, string text, Font? font = null)
        {
            using (Node("#_Text_" + id).Enter())
            {
                Draw2D.DrawText(font ?? UIDrawList.DefaultFont, text, 20, CurrentNode.LayoutData.InnerRect, Color.white);
                return CurrentNode;
            }
        }

        public bool Combo(string ID, string popupName, ref int ItemIndex, string[] Items, Offset x, Offset y, Size width, Size height, GuiStyle? style = null, string? label = null)
        {
            style ??= new();
            var g = Gui.ActiveGUI;
            using (g.Node(ID).Left(x).Top(y).Width(width).Height(height).Padding(2).Enter())
            {
                Interactable interact = g.GetInteractable();
        
                var col = g.ActiveID == interact.ID ? style.BtnActiveColor :
                          g.HoveredID == interact.ID ? style.BtnHoveredColor : style.WidgetColor;
        
                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, col, style.WidgetRoundness);
                if (style.BorderThickness > 0)
                    g.Draw2D.DrawRect(g.CurrentNode.LayoutData.Rect, style.Border, style.BorderThickness, style.WidgetRoundness);
        
                if(label == null)
                    g.Draw2D.DrawText(style.Font.IsAvailable ? style.Font.Res : UIDrawList.DefaultFont, Items[ItemIndex], style.FontSize, g.CurrentNode.LayoutData.InnerRect, style.TextColor);
                else
                    g.Draw2D.DrawText(style.Font.IsAvailable ? style.Font.Res : UIDrawList.DefaultFont, label, style.FontSize, g.CurrentNode.LayoutData.InnerRect, style.TextColor);

                var popupWidth = g.CurrentNode.LayoutData.Rect.width;
                if (interact.TakeFocus())
                    g.OpenPopup(popupName, g.CurrentNode.LayoutData.Rect.BottomLeft);
        
                y.PixelOffset = 1;
                var NewIndex = ItemIndex;
                if (g.BeginPopup(popupName, out var popupNode))
                {
                    int longestText = 0;
                    for (var Index = 0; Index < Items.Length; ++Index)
                    {
                        var textSize = style.Font.IsAvailable ? style.Font.Res.CalcTextSize(Items[Index], style.FontSize, 0) : UIDrawList.DefaultFont.CalcTextSize(Items[Index], style.FontSize, 0);
                        if (textSize.x > longestText)
                            longestText = (int)textSize.x;
                    }

                    popupWidth = Math.Max(popupWidth, longestText + 20);

                    using (popupNode.Width(popupWidth).Height(Items.Length * GuiStyle.ItemHeight).Layout(LayoutType.Column).Enter())
                    {
                        for (var Index = 0; Index < Items.Length; ++Index)
                        {
                            using (g.Node(popupName + "_Item_" + Index).ExpandWidth().Height(GuiStyle.ItemHeight).Enter())
                            {
                                if (g.IsNodePressed())
                                {
                                    NewIndex = Index;
                                    g.ClosePopup(popupNode.Parent);
                                }
                                else if (g.IsNodeHovered())
                                    g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, style.BtnHoveredColor, style.WidgetRoundness);

                                g.Draw2D.DrawText(Items[Index], g.CurrentNode.LayoutData.Rect, style.TextColor);
                            }
                        }
                    }
                }
        
                if (ItemIndex != NewIndex)
                {
                    ItemIndex = NewIndex;
                    return true;
                }
        
                return false;
            }
        
        }

        public bool Checkbox(string ID, ref bool value, Offset x, Offset y, out LayoutNode node, GuiStyle? style = null)
        {
            style ??= new();
            x.PixelOffset += 5;
            y.PixelOffset += 5;
            using ((node = Node(ID)).Left(x).Top(y).Scale(GuiStyle.ItemHeight - 10).Enter())
            {
                Interactable interact = GetInteractable();

                    var col = ActiveID == interact.ID ? style.BtnActiveColor :
                              HoveredID == interact.ID ? style.BtnHoveredColor : style.WidgetColor;

                Draw2D.DrawRectFilled(CurrentNode.LayoutData.Rect, col, style.WidgetRoundness);
                if (style.BorderThickness > 0)
                    Draw2D.DrawRect(CurrentNode.LayoutData.Rect, style.Border, style.BorderThickness, style.WidgetRoundness);

                if (value)
                {
                    var check = CurrentNode.LayoutData.Rect;
                    check.Expand(-4);
                    Draw2D.DrawRectFilled(check, style.TextHighlightColor, style.WidgetRoundness);
                }

                if (interact.TakeFocus())
                {
                    value = !value;
                    return true;
                }
                return false;
            }
        }

        public void Tooltip(string tip, Vector2? topleft = null, float wrapWidth = -1, GuiStyle? style = null)
        {

            style ??= new();
            if (PreviousInteractableIsHovered())
            {
                var oldZ = Gui.ActiveGUI.CurrentZIndex;
                Gui.ActiveGUI.Draw2D.DrawList.PushClipRectFullScreen();
                Gui.ActiveGUI.SetZIndex(11000);

                var font = style.Font.IsAvailable ? style.Font.Res : UIDrawList.DefaultFont;
                var pos = (topleft ?? PointerPos) + new Vector2(10, 10);
                var size = font.CalcTextSize(tip, style.FontSize, 0, wrapWidth);
                Draw2D.DrawRectFilled(pos - new Vector2(5), size + new Vector2(10), GuiStyle.WindowBackground, 10);
                Draw2D.DrawRect(pos - new Vector2(5), size + new Vector2(10), GuiStyle.Borders, 2, 10);
                Draw2D.DrawText(font, tip, style.FontSize, pos, style.TextColor, wrapWidth);

                Gui.ActiveGUI.Draw2D.DrawList.PopClipRect();
                Gui.ActiveGUI.SetZIndex(oldZ);
            }

        }

        public void OpenPopup(string id, Vector2? topleft = null, LayoutNode? popupHolder = null)
        {
            SetNodeStorage(popupHolder ?? CurrentNode, "Popup", true);
            SetNodeStorage(popupHolder ?? CurrentNode, "Popup_ID", id.GetHashCode());
            SetNodeStorage(popupHolder ?? CurrentNode, "Popup_Frame", Time.frameCount);
            SetNodeStorage(popupHolder ?? CurrentNode, "PU_POS_" + id, topleft ?? PointerPos);
        }

        private static int nextPopupIndex;

        public bool BeginPopup(string id, out LayoutNode? node, bool invisible = false)
        {
            node = null;
            var show = GetNodeStorage<bool>("Popup");
            show &= GetNodeStorage<int>("Popup_ID") == id.GetHashCode();
            // If this node is showing a Popup and the Popup ID is the same as this ID
            if (show)
            {
                var pos = GetNodeStorage<Vector2>("PU_POS_" + id);
                var parentNode = CurrentNode;
                // Append to Root
                using ((node = rootNode.AppendNode("PU_" + id)).Left(pos.x).Top(pos.y).IgnoreLayout().Enter())
                {
                    SetZIndex(50000 + nextPopupIndex, false);
                    BlockInteractables(CurrentNode.LayoutData.Rect);

                    // Clamp node position so that its always in screen bounds
                    var rect = CurrentNode.LayoutData.Rect;
                    if (pos.x + rect.width > ScreenRect.width)
                    {
                        CurrentNode.Left(ScreenRect.width - rect.width);
                        pos.x = ScreenRect.width - rect.width;
                        SetNodeStorage(parentNode, "PU_POS_" + id, pos);
                    }
                    if (pos.y + rect.height > ScreenRect.height)
                    {
                        CurrentNode.Top(ScreenRect.height - rect.height);
                        pos.y = ScreenRect.height - rect.height;
                        SetNodeStorage(parentNode, "PU_POS_" + id, pos);
                    }

                    // Dont close Popup on the same frame it was opened
                    long frame = GetNodeStorage<long>(parentNode, "Popup_Frame");
                    if (frame != Time.frameCount)
                    {
                        if ((IsPointerDown(Silk.NET.Input.MouseButton.Left) || IsPointerDown(Silk.NET.Input.MouseButton.Right)) &&
                            !IsPointerMoving &&
                            !node.LayoutData.Rect.Contains(PointerPos) && // Mouse not in Popup
                            //!parentNode.LayoutData.Rect.Contains(PointerPos) && // Mouse not in Parent
                            !IsBlockedByInteractable(PointerPos, 50000 + nextPopupIndex)) // Not blocked by any interactables above this popup
                        {
                            ClosePopup(parentNode);
                            return false;
                        }
                    }

                    if (!invisible)
                    {
                        Draw2D.PushClip(ScreenRect, true);
                        Draw2D.DrawRectFilled(CurrentNode.LayoutData.Rect, GuiStyle.WindowBackground, 10);
                        Draw2D.DrawRect(CurrentNode.LayoutData.Rect, GuiStyle.Borders, 2, 10);
                        Draw2D.PopClip();
                    }

                    nextPopupIndex++;
                }
            }
            return show;
        }

        public void ClosePopup(LayoutNode? popupHolder = null)
        {
            SetNodeStorage(popupHolder ?? CurrentNode, "Popup", false);
            SetNodeStorage(popupHolder ?? CurrentNode, "Popup_ID", -1);
        }

        public bool Search(string ID, ref string searchText, Offset x, Offset y, Size width, Size? height = null, GuiStyle? style = null)
        {
            style ??= new();
            searchText ??= "";
            var g = Runtime.GUI.Gui.ActiveGUI;

            style.WidgetColor = GuiStyle.WindowBackground;
            style.Border = GuiStyle.Borders;
            style.WidgetRoundness = 8f;
            style.BorderThickness = 1f;
            var changed = InputField(ID, ref searchText, 32, InputFieldFlags.None, x, y, width, height, style);
            if(string.IsNullOrWhiteSpace(searchText) && !g.PreviousInteractableIsFocus())
            {
                var pos = g.PreviousNode.LayoutData.InnerRect.Position + new Vector2(8, 3);
                // Center text vertically
                pos.y += (g.PreviousNode.LayoutData.InnerRect.height - style.FontSize) / 2;
                g.Draw2D.DrawText(style.Font.IsAvailable ? style.Font.Res : UIDrawList.DefaultFont, FontAwesome6.MagnifyingGlass + "Search...", style.FontSize, pos, GuiStyle.Base6);
            }
            return changed;
        }

    }
}