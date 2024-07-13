using Prowl.Icons;
using Prowl.Runtime.GUI.Graphics;
using Prowl.Runtime.GUI.Layout;
using System;

namespace Prowl.Runtime.GUI
{
    public partial class Gui
    {
        
        public LayoutNode TextNode(string id, string text, Font? font = null)
        {
            using (Node("#_Text_" + id).Enter())
            {
                Draw2D.DrawText(font ?? Font.DefaultFont, text, 20, CurrentNode.LayoutData.InnerRect, Color.white);
                return CurrentNode;
            }
        }

        public bool Combo(string ID, string popupName, ref int ItemIndex, string[] Items, Offset x, Offset y, Size width, Size height, WidgetStyle? inputstyle = null, string? label = null)
        {
            var style = inputstyle ?? new(30);
            var g = Gui.ActiveGUI;
            using (g.Node(ID).Left(x).Top(y).Width(width).Height(height).Padding(2).Enter())
            {
                Interactable interact = g.GetInteractable();

                var col = g.ActiveID == interact.ID ? style.ActiveColor :
                          g.HoveredID == interact.ID ? style.HoveredColor : style.BGColor;

                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, col, style.Roundness);
                g.Draw2D.DrawRect(g.CurrentNode.LayoutData.Rect, style.BorderColor, style.BorderThickness, style.Roundness);

                if (label == null)
                    g.Draw2D.DrawText(Items[ItemIndex], g.CurrentNode.LayoutData.InnerRect, false);
                else
                    g.Draw2D.DrawText(label, g.CurrentNode.LayoutData.InnerRect, false);

                var popupWidth = g.CurrentNode.LayoutData.Rect.width;
                if (interact.TakeFocus())
                    g.OpenPopup(popupName, g.CurrentNode.LayoutData.Rect.BottomLeft);

                y.PixelOffset = 1;
                var NewIndex = ItemIndex;
                var popupHolder = g.CurrentNode;
                if (g.BeginPopup(popupName, out var popupNode))
                {
                    int longestText = 0;
                    for (var Index = 0; Index < Items.Length; ++Index)
                    {
                        var textSize = Font.DefaultFont.CalcTextSize(Items[Index], 0);
                        if (textSize.x > longestText)
                            longestText = (int)textSize.x;
                    }

                    popupWidth = Math.Max(popupWidth, longestText + 20);

                    using (popupNode.Width(popupWidth).Height(Items.Length * style.ItemSize).MaxHeight(250).Scroll().Layout(LayoutType.Column).Clip().Enter())
                    {
                        for (var Index = 0; Index < Items.Length; ++Index)
                        {
                            using (g.Node(popupName + "_Item_" + Index).ExpandWidth().Height(style.ItemSize).Enter())
                            {
                                if (g.IsNodePressed())
                                {
                                    NewIndex = Index;
                                    g.ClosePopup(popupHolder);
                                }
                                else if (g.IsNodeHovered())
                                    g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, style.HoveredColor, style.Roundness);

                                g.Draw2D.DrawText(Items[Index], g.CurrentNode.LayoutData.Rect);
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

        public bool Checkbox(string ID, ref bool value, Offset x, Offset y, out LayoutNode node, WidgetStyle? inputstyle = null)
        {
            var style = inputstyle ?? new(30);
            x.PixelOffset += 2;
            y.PixelOffset += 2;
            using ((node = Node(ID)).Left(x).Top(y).Scale(style.ItemSize - 4).Enter())
            {
                Interactable interact = GetInteractable();

                    var col = ActiveID == interact.ID ? style.ActiveColor :
                              HoveredID == interact.ID ? style.HoveredColor : style.BGColor;

                Draw2D.DrawRectFilled(CurrentNode.LayoutData.Rect, col, style.Roundness);
                Draw2D.DrawRect(CurrentNode.LayoutData.Rect, style.BorderColor, style.BorderThickness, style.Roundness);

                if (value)
                {
                    var check = CurrentNode.LayoutData.Rect;
                    check.Expand(-4);
                    Draw2D.DrawRectFilled(check, style.ActiveColor, style.Roundness);
                }

                if (interact.TakeFocus())
                {
                    value = !value;
                    return true;
                }
                return false;
            }
        }

        public void Tooltip(string tip, Vector2? topleft = null, float wrapWidth = -1)
        {
            if (PreviousInteractableIsHovered())
            {
                var oldZ = Gui.ActiveGUI.CurrentZIndex;
                Gui.ActiveGUI.SetZIndex(500000);

                var pos = (topleft ?? PointerPos) + new Vector2(10, 10);
                var size = Font.DefaultFont.CalcTextSize(tip, 0, wrapWidth);
                var style = new WidgetStyle(30);
                Draw2D.DrawRectFilled(pos - new Vector2(5), size + new Vector2(10), style.BGColor, 10);
                Draw2D.DrawRect(pos - new Vector2(5), size + new Vector2(10), style.BorderColor, 2, 10);
                Draw2D.DrawText(tip, pos, wrapWidth);

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

        public bool BeginPopup(string id, out LayoutNode? node, bool invisible = false, WidgetStyle? inputstyle = null)
        {
            var style = inputstyle ?? new(30);
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

                    // Dont close Popup on the same frame it was opened - 5 frame window
                    long frame = GetNodeStorage<long>(parentNode, "Popup_Frame");
                    if (frame < Time.frameCount)
                    {
                        if ((IsPointerClick(MouseButton.Left) || IsPointerClick(MouseButton.Right)) &&
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
                        Draw2D.DrawRectFilled(CurrentNode.LayoutData.Rect, style.BGColor, 10);
                        Draw2D.DrawRect(CurrentNode.LayoutData.Rect, style.BorderColor, 2, 10);
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

        public bool Search(string ID, ref string searchText, Offset x, Offset y, Size width, Size? height = null, WidgetStyle? inputstyle = null)
        {
            var style = inputstyle ?? new(30);
            searchText ??= "";
            var g = Runtime.GUI.Gui.ActiveGUI;

            style.Roundness = 8f;
            style.BorderThickness = 1f;
            var changed = InputField(ID, ref searchText, 32, InputFieldFlags.None, x, y, width, height, style);
            if(string.IsNullOrWhiteSpace(searchText) && !g.PreviousInteractableIsFocus())
            {
                var pos = g.PreviousNode.LayoutData.InnerRect.Position + new Vector2(8, 3);
                // Center text vertically
                pos.y += (g.PreviousNode.LayoutData.InnerRect.height - style.FontSize) / 2;
                g.Draw2D.DrawText(Font.DefaultFont, FontAwesome6.MagnifyingGlass + "Search...", style.FontSize, pos, Color.white * 0.6f);
            }
            return changed;
        }

    }
}