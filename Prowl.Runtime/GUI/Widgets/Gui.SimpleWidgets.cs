using Prowl.Icons;
using Prowl.Runtime.GUI.Graphics;
using Prowl.Runtime.GUI.Layout;
using System;
using System.Runtime.CompilerServices;

namespace Prowl.Runtime.GUI
{
    public partial class Gui
    {
        public const int ScrollVWidth = 6;
        public const int ScrollVPadding = 2;

        public double VScrollBarWidth() => CurrentNode.LayoutData.ContentRect.height > CurrentNode.LayoutData.Rect.height ? ScrollVWidth + (ScrollVPadding * 2) : 0;

        public void ScrollV(GuiStyle? style = null)
        {
            style ??= new();
            var n = CurrentNode;
            CurrentNode.VScroll = GetStorage<double>("VScroll");
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
                            DrawRectFilled(barRect, style.ScrollBarActiveColor, style.ScrollBarRoundness);
                            {
                                n.VScroll += PointerDelta.y * 2f;
                                layoutDirty = true;
                            }
                        }
                        else if (interact.IsHovered()) DrawRectFilled(barRect, style.ScrollBarHoveredColor, (float)style.ScrollBarRoundness);
                        else DrawRectFilled(barRect, style.WidgetColor, style.ScrollBarRoundness);

                        if (IsHovering(n.LayoutData.Rect) && PointerWheel != 0)
                        {
                            n.VScroll -= PointerWheel * 10;
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

        /// <summary>
        /// A Shortcut to an Interactable Node
        /// This creates a node with an Interactable and outputs the pressed and hovered states
        /// </summary>
        public LayoutNode ButtonNode(string ID, out bool pressed, out bool hovered) => ButtonNode(ID, out pressed, out _, out hovered);
        /// <summary>
        /// A Shortcut to an Interactable Node
        /// This creates a node with an Interactable and outputs the pressed, active and hovered states
        /// </summary>
        public LayoutNode ButtonNode(string ID, out bool pressed, out bool active, out bool hovered)
        {
            LayoutNode node = Node(ID);
            using (node.Enter())
            {
                Interactable interact = GetInteractable();

                pressed = interact.TakeFocus();
                active = interact.IsActive();
                hovered = interact.IsHovered();
            }
            return node;
        }

        public LayoutNode TextNode(string id, string text, Font? font = null)
        {
            using (Node("#_Text_" + id).Enter())
            {
                DrawText(font ?? UIDrawList.DefaultFont, text, 20, CurrentNode.LayoutData.InnerRect, Color.white);
                return CurrentNode;
            }
        }

        [Obsolete("Use ButtonNode instead")]
        public bool Button(string ID, string? label, Offset x, Offset y, Size width, Size height, GuiStyle? style = null, bool invisible = false, bool repeat = false, int rounded_corners = 15) => Button(ID, label, x, y, width, height, out _, style, invisible, repeat, rounded_corners);
        [Obsolete("Use ButtonNode instead")]
        public bool Button(string ID, string? label, Offset x, Offset y, Size width, Size height, out LayoutNode node, GuiStyle? style = null, bool invisible = false, bool repeat = false, int rounded_corners = 15)
        {
            style ??= new();
            var g = Gui.ActiveGUI;
            using ((node = g.Node(ID)).Left(x).Top(y).Width(width).Height(height).Padding(2).Enter())
            {
                Interactable interact = g.GetInteractable();

                if (!invisible)
                {
                    var col = g.ActiveID == interact.ID ? style.BtnActiveColor :
                              g.HoveredID == interact.ID ? style.BtnHoveredColor : style.WidgetColor;

                    g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, col, style.WidgetRoundness, rounded_corners);
                    if (style.BorderThickness > 0)
                        g.DrawRect(g.CurrentNode.LayoutData.Rect, style.Border, style.BorderThickness, style.WidgetRoundness, rounded_corners);
                }

                if(label != null)
                    g.DrawText(style.Font.IsAvailable ? style.Font.Res : UIDrawList.DefaultFont, label, style.FontSize, g.CurrentNode.LayoutData.InnerRect, interact.IsHovered() ? style.TextColor * 0.5f : style.TextColor);


                if (repeat)
                    return interact.IsActive();
                return interact.TakeFocus();
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
        
                g.DrawRectFilled(g.CurrentNode.LayoutData.Rect, col, style.WidgetRoundness);
                if (style.BorderThickness > 0)
                    g.DrawRect(g.CurrentNode.LayoutData.Rect, style.Border, style.BorderThickness, style.WidgetRoundness);
        
                if(label == null)
                    g.DrawText(style.Font.IsAvailable ? style.Font.Res : UIDrawList.DefaultFont, Items[ItemIndex], style.FontSize, g.CurrentNode.LayoutData.InnerRect, style.TextColor);
                else
                    g.DrawText(style.Font.IsAvailable ? style.Font.Res : UIDrawList.DefaultFont, label, style.FontSize, g.CurrentNode.LayoutData.InnerRect, style.TextColor);

                var popupWidth = g.CurrentNode.LayoutData.Rect.width;
                if (interact.TakeFocus())
                    g.OpenPopup(popupName, g.CurrentNode.LayoutData.Rect.BottomLeft);
        
                y.PixelOffset = 1;
                var NewIndex = ItemIndex;
                if (g.BeginPopup(popupName, out var node))
                {
                    int longestText = 0;
                    for (var Index = 0; Index < Items.Length; ++Index)
                    {
                        var textSize = style.Font.IsAvailable ? style.Font.Res.CalcTextSize(Items[Index], style.FontSize, 0) : UIDrawList.DefaultFont.CalcTextSize(Items[Index], style.FontSize, 0);
                        if (textSize.x > longestText)
                            longestText = (int)textSize.x;
                    }

                    popupWidth = Math.Max(popupWidth, longestText + 20);

                    using (node.Width(popupWidth).Height(Items.Length * GuiStyle.ItemHeight).Enter())
                    {
                        for (var Index = 0; Index < Items.Length; ++Index)
                        {
                            var rect = new Rect(node.LayoutData.Rect.x, node.LayoutData.Rect.y + Index * GuiStyle.ItemHeight, node.LayoutData.Rect.width, 25);
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

        public void SimpleTooltip(string tip, Vector2? topleft = null, float wrapWidth = -1, GuiStyle? style = null)
        {

            style ??= new();
            if (PreviousControlIsHovered())
            {
                var oldZ = Gui.ActiveGUI.CurrentZIndex;
                Gui.ActiveGUI.DrawList.PushClipRectFullScreen();
                Gui.ActiveGUI.SetZIndex(11000);

                var font = style.Font.IsAvailable ? style.Font.Res : UIDrawList.DefaultFont;
                var pos = (topleft ?? PointerPos) + new Vector2(10, 10);
                var size = font.CalcTextSize(tip, style.FontSize, 0, wrapWidth);
                DrawRectFilled(pos - new Vector2(5), size + new Vector2(10), GuiStyle.WindowBackground, 10);
                DrawRect(pos - new Vector2(5), size + new Vector2(10), GuiStyle.Borders, 2, 10);
                DrawText(font, tip, style.FontSize, pos, style.TextColor, wrapWidth);

                Gui.ActiveGUI.DrawList.PopClipRect();
                Gui.ActiveGUI.SetZIndex(oldZ);
            }

        }

        public bool Tooltip(string ID, out LayoutNode? node, Vector2? topleft = null, bool invisible = false)
        {
            node = null;
            if (!PreviousControlIsHovered())
                return false;


            // Append to Root
            var pos = topleft ?? PointerPos;
            using ((node = rootNode.AppendNode(ID)).Left(pos.x).Top(pos.y).IgnoreLayout().Enter())
            {
                var oldZ = Gui.ActiveGUI.CurrentZIndex;
                Gui.ActiveGUI.DrawList.PushClipRectFullScreen();
                Gui.ActiveGUI.SetZIndex(11000);

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

                Gui.ActiveGUI.DrawList.PopClipRect();
                Gui.ActiveGUI.SetZIndex(oldZ);

                return true;
            }
        }

        public LayoutNode SeperatorHNode(float thickness = 1f, Color? color = null, [CallerLineNumber] int intID = 0)
        {
            color ??= Color.white;
            using (Node("#_Sep", intID).ExpandWidth().Height(thickness).Enter())
            {
                var rect = CurrentNode.LayoutData.Rect;
                var start = new Vector2(rect.Left, (rect.Top + rect.Bottom) / 2f);
                var end = new Vector2(rect.Right, (rect.Top + rect.Bottom) / 2f);
                DrawLine(start, end, color.Value, thickness);
                return CurrentNode;
            }
        }

        public LayoutNode ToggleNode(string id, ref bool value)
        {
            using (Node(id).Enter())
            {
                Interactable interact = GetInteractable();
                if (interact.TakeFocus())
                    value = !value;

                return CurrentNode;
            }
        }

        public LayoutNode OpenCloseNode(string id, out bool opened, bool openByDefault = true)
        {
            opened = GetStorage<bool>("H_" + id, openByDefault);
            var result = ToggleNode(id, ref opened);
            SetStorage("H_" + id, opened);
            return result;
        }

        public void OpenPopup(string id, Vector2? topleft = null)
        {
            SetStorage("PU_" + id, true);
            SetStorage("PU_POS_" + id, topleft ?? PointerPos);
        }

        private static LayoutNode? currentPopupParent = null;
        private static int nextPopupIndex;

        public bool BeginPopup(string id, out LayoutNode? node, bool invisible = false)
        {
            node = null;
            var show = GetStorage<bool>("PU_" + id);
            if (show)
            {
                currentPopupParent = CurrentNode;
                var pos = GetStorage<Vector2>("PU_POS_" + id);
                // Append to Root
                using ((node = rootNode.AppendNode("PU_" + id)).Left(pos.x).Top(pos.y).IgnoreLayout().Enter())
                {
                    SetZIndex(1000 + nextPopupIndex++, false);
                    CreateBlocker(CurrentNode.LayoutData.Rect);

                    // Clamp node position so that its always in screen bounds
                    var rect = CurrentNode.LayoutData.Rect;
                    if (pos.x + rect.width > ScreenRect.width)
                    {
                        CurrentNode.Left(ScreenRect.width - rect.width);
                        pos.x = ScreenRect.width - rect.width;
                        SetStorage("PU_POS_" + id, pos);
                    }
                    if (pos.y + rect.height > ScreenRect.height)
                    {
                        CurrentNode.Top(ScreenRect.height - rect.height);
                        pos.y = ScreenRect.height - rect.height;
                        SetStorage("PU_POS_" + id, pos);
                    }

                    if (IsPointerDown(Silk.NET.Input.MouseButton.Left) && 
                        !node.LayoutData.Rect.Contains(PointerPos) && // Mouse not in Popup
                        !currentPopupParent.LayoutData.Rect.Contains(PointerPos)) // Mouse not in Parent
                    {
                        ClosePopup(id);
                        return false;
                    }

                    if (!invisible)
                    {
                        PushClip(ScreenRect, true);
                        DrawRectFilled(CurrentNode.LayoutData.Rect, GuiStyle.WindowBackground, 10);
                        DrawRect(CurrentNode.LayoutData.Rect, GuiStyle.Borders, 2, 10);
                        PopClip();
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

        public bool IsPopupOpen(string name) => GetStorage<bool>("PU_" + name);
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
            if(string.IsNullOrWhiteSpace(searchText) && !g.PreviousControlIsFocus())
            {
                var pos = g.PreviousNode.LayoutData.InnerRect.Position + new Vector2(8, 3);
                // Center text vertically
                pos.y += (g.PreviousNode.LayoutData.InnerRect.height - style.FontSize) / 2;
                g.DrawText(style.Font.IsAvailable ? style.Font.Res : UIDrawList.DefaultFont, FontAwesome6.MagnifyingGlass + "Search...", style.FontSize, pos, GuiStyle.Base6);
            }
            return changed;
        }

    }
}