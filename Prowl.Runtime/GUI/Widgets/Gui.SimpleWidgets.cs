using System;
using System.Reflection.Emit;
using Microsoft.VisualBasic;
using Prowl.Runtime.GUI.Graphics;
using Prowl.Runtime.GUI.Layout;
using Silk.NET.Vulkan;

namespace Prowl.Runtime.GUI
{
    public partial class Gui
    {
        public const int ScrollVWidth = 6;
        public const int ScrollVPadding = 2;

        public void ScrollV(GuiStyle? style = null)
        {
            style ??= new();
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
                            DrawRectFilled(barRect, style.ScrollBarActiveColor, style.ScrollBarRoundness);
                            {
                                n.VScroll += Input.MouseDelta.y * 2f;
                                layoutDirty = true;
                            }
                        }
                        else if (interact.IsHovered()) DrawRectFilled(barRect, style.ScrollBarHoveredColor, (float)style.ScrollBarRoundness);
                        else DrawRectFilled(barRect, style.WidgetColor, style.ScrollBarRoundness);

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


        public bool Button(string? label, Offset x, Offset y, Size width, Size height, GuiStyle? style = null, bool invisible = false, bool repeat = false, int rounded_corners = 15) => Button(label, x, y, width, height, out _, style, invisible, repeat, rounded_corners);
        public bool Button(string? label, Offset x, Offset y, Size width, Size height, out LayoutNode node, GuiStyle? style = null, bool invisible = false, bool repeat = false, int rounded_corners = 15)
        {
            style ??= new();
            var g = Gui.ActiveGUI;
            using ((node = g.Node()).Left(x).Top(y).Width(width).Height(height).Padding(2).Enter())
            {
                Interactable interact = g.GetInteractable();

                if (!invisible)
                {
                    var col = g.ActiveID == interact.ID ? style.BtnActiveColor :
                              g.HoveredID == interact.ID ? style.BtnHoveredColor : style.WidgetColor;

                    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, col, style.WidgetRoundness, rounded_corners);
                    if (style.BorderThickness > 0)
                        g.DrawRect(g.CurrentNode.LayoutData.Rect, style.Border, style.BorderThickness, style.WidgetRoundness, rounded_corners);

                    g.DrawText(style.Font.IsAvailable ? style.Font.Res : UIDrawList.DefaultFont, label, style.FontSize, g.CurrentNode.LayoutData.InnerRect, style.TextColor);
                }

                if (repeat)
                    return interact.IsActive();
                return interact.TakeFocus();
            }
        }

        public bool InputDouble(ref double value, Offset x, Offset y, Size width, GuiStyle? style = null)
        {
            string textValue = "";
            var changed = InputField(ref textValue, 16, InputFieldFlags.NumbersOnly, x, y, width, style);
            if (changed && Double.TryParse(textValue, out value)) return true;
            return false;
        }

        public bool InputInt(ref int value, Offset x, Offset y, Size width, GuiStyle? style = null)
        {
            string textValue = "";
            var changed = InputField(ref textValue, 16, InputFieldFlags.NumbersOnly, x, y, width, style);
            if (changed && int.TryParse(textValue, out value)) return true;
            return false;
        }

        public bool Combo(string popupName, ref int ItemIndex, string[] Items, Offset x, Offset y, Size width, Size height, GuiStyle? style = null, float PopupHeight = 100)
        {
            style ??= new();
            var g = Gui.ActiveGUI;
            using (g.Node().Left(x).Top(y).Width(width).Height(height).Padding(2).Enter())
            {
                Interactable interact = g.GetInteractable();
        
                var col = g.ActiveID == interact.ID ? style.BtnActiveColor :
                          g.HoveredID == interact.ID ? style.BtnHoveredColor : style.WidgetColor;
        
                g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, col, style.WidgetRoundness);
                if (style.BorderThickness > 0)
                    g.DrawRect(g.CurrentNode.LayoutData.Rect, style.Border, style.BorderThickness, style.WidgetRoundness);
        
                g.DrawText(style.Font.IsAvailable ? style.Font.Res : UIDrawList.DefaultFont, Items[ItemIndex], style.FontSize, g.CurrentNode.LayoutData.InnerRect, style.TextColor);
        
                if (interact.TakeFocus())
                    g.OpenPopup(popupName, g.CurrentNode.LayoutData.Rect.BottomLeft);
        
                y.PixelOffset = 1;
                var NewIndex = ItemIndex;
                if (g.BeginPopup(popupName, out var node))
                {
                    using (node.Width(width).Height(Items.Length * 30).Enter())
                    {
                        for (var Index = 0; Index < Items.Length; ++Index)
                        {
                            var rect = new Rect(node.LayoutData.Rect.x, node.LayoutData.Rect.y + Index * 30, node.LayoutData.Rect.width, 25);
                            var element = g.GetInteractable(rect);
                            if (element.TakeFocus())
                            {
                                NewIndex = Index;
                                g.ClosePopup(popupName);
                            }
                            else if (element.IsHovered())
                            {
                                g.DrawRectFilled(rect, style.BtnHoveredColor, style.WidgetRoundness);
                            }
                            g.DrawText(style.Font.IsAvailable ? style.Font.Res : UIDrawList.DefaultFont, Items[Index], style.FontSize, rect, style.TextColor);
                            //if (LGuiSelectable.OnProcess(Items[Index], ItemIndex == Index, 0, 20 * (Index + 5), width, 20))
                            //{
                            //    NewIndex = Index;
                            //    g.ClosePopup(PopupID);
                            //}
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

        public bool Checkbox(ref bool value, Offset x, Offset y, out LayoutNode node, GuiStyle? style = null)
        {
            using ((node = Node()).Left(x).Top(y).Width(20).Height(20).Enter())
            {
                Interactable interact = GetInteractable();

                    var col = ActiveID == interact.ID ? style.BtnActiveColor :
                              HoveredID == interact.ID ? style.BtnHoveredColor : style.WidgetColor;

                    DrawRectFilled(CurrentNode.LayoutData.Rect, col, style.WidgetRoundness);
                    if (style.BorderThickness > 0)
                        DrawRect(CurrentNode.LayoutData.Rect, style.Border, style.BorderThickness, style.WidgetRoundness);

                if (value)
                {
                    var check = CurrentNode.LayoutData.Rect;
                    check.Expand(-4);
                    DrawRectFilled(check, style.TextHighlightColor, style.WidgetRoundness);
                }

                if (interact.TakeFocus())
                {
                    value = !value;
                    return true;
                }
                return false;
            }
        }

        public void SimpleTooltip(string tip, Vector2? topleft = null, float wrapWidth = 200f, GuiStyle? style = null)
        {
            if (PreviousControlIsHovered())
            {
                var font = style.Font.IsAvailable ? style.Font.Res : UIDrawList.DefaultFont;
                var pos = (topleft ?? PointerPos) + new Vector2(10, 10);
                var size = font.CalcTextSize(tip, style.FontSize, 0, wrapWidth);
                DrawRectFilled(pos - new Vector2(5), size + new Vector2(10), GuiStyle.WindowBackground, 10);
                DrawRect(pos - new Vector2(5), size + new Vector2(10), GuiStyle.Borders, 2, 10);
                DrawText(font, tip, style.FontSize, pos, style.TextColor);
            }
        }

        public bool Tooltip(out LayoutNode? node, Vector2? topleft = null, bool invisible = false)
        {
            node = null;
            if (!PreviousControlIsHovered())
                return false;

            // Append to Root
            var pos = topleft ?? PointerPos;
            using ((node = _nodes[0].AppendNode()).Left(pos.x).Top(pos.y).IgnoreLayout().Enter())
            {
                SetZIndex(1100);

                // Clamp node position so that its always in screen bounds
                var rect = CurrentNode.LayoutData.Rect;
                if (rect.x + rect.width > ScreenRect.width)
                    CurrentNode.Left(ScreenRect.width - rect.width);
                if (rect.y + rect.height > ScreenRect.height)
                    CurrentNode.Top(ScreenRect.height - rect.height);

                if (!invisible)
                {
                    DrawRectFilled(CurrentNode.LayoutData.InnerRect, GuiStyle.WindowBackground, 10);
                    DrawRect(CurrentNode.LayoutData.InnerRect, GuiStyle.Borders, 2, 10);
                }
                return true;
            }
        }

        public void OpenPopup(string name, Vector2? topleft = null)
        {
            SetStorage("PU_" + name, true);
            SetStorage("PU_POS_" + name, topleft ?? PointerPos);
        }

        private static LayoutNode? currentPopupParent = null;

        public bool BeginPopup(string name, out LayoutNode? node, bool invisible = false)
        {
            node = null;
            var show = GetStorage<bool>("PU_" + name);
            if (show)
            {
                currentPopupParent = CurrentNode;
                var pos = GetStorage<Vector2>("PU_POS_" + name);
                // Append to Root
                using ((node = _nodes[0].AppendNode()).Left(pos.x).Top(pos.y).IgnoreLayout().Enter())
                {
                    SetZIndex(1000);
                    CreateBlocker(CurrentNode.LayoutData.Rect);

                    // Clamp node position so that its always in screen bounds
                    var rect = CurrentNode.LayoutData.Rect;
                    if (rect.x + rect.width > ScreenRect.width)
                        CurrentNode.Left(ScreenRect.width - rect.width);
                    if (rect.y + rect.height > ScreenRect.height)
                        CurrentNode.Top(ScreenRect.height - rect.height);

                    if (IsPointerDown(Silk.NET.Input.MouseButton.Left) && !node.LayoutData.Rect.Contains(PointerPos))
                    {
                        ClosePopup(name);
                        return false;
                    }

                    if (!invisible)
                    {
                        DrawRectFilled(CurrentNode.LayoutData.InnerRect, GuiStyle.WindowBackground, 10);
                        DrawRect(CurrentNode.LayoutData.InnerRect, GuiStyle.Borders, 2, 10);
                    }

                }
            }
            return show;
        }

        public void ClosePopup(string name)
        {
            if(currentPopupParent == null)
                throw new Exception("Attempting to close a popup that is not open.");
            SetStorage(currentPopupParent, "PU_" + name, false);
            currentPopupParent = null;
        }

    }
}