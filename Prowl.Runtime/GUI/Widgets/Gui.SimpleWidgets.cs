// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Icons;
using Prowl.Runtime.GUI.Layout;

namespace Prowl.Runtime.GUI;


public enum TooltipAlign
{
    TopLeft,
    TopMiddle,
    TopRight,
    Left,
    Right,
    BottomLeft,
    BottomMiddle,
    BottomRight,
}


public partial class Gui
{
    const int HueWheelSegments = 128;

    private static List<Color32>? s_colorHues;
    private static List<Color32> ColorHues => s_colorHues ??= Enumerable.Range(0, HueWheelSegments)
        .Select(x => (Color32)Color.FromHSV((float)x / HueWheelSegments * 360f, 1f, 1f))
        .ToList();


    public LayoutNode TextNode(string id, string text, Font? font = null)
    {
        using (Node("#_Text_" + id).Enter())
        {
            Draw2D.DrawText(font ?? Font.DefaultFont, text, 20, CurrentNode.LayoutData.InnerRect, Color.white);
            return CurrentNode;
        }
    }


    public bool Combo(string ID,
        string popupName,
        ref int itemIndex,
        string[] items,
        Offset x, Offset y,
        Size width, Size height,
        WidgetStyle? inputstyle = null,
        WidgetStyle? popupstyle = null,
        string? label = null)
    {
        itemIndex = Math.Clamp(itemIndex, 0, items.Length - 1);

        WidgetStyle style = inputstyle ?? new(30);
        using (Node(ID).Left(x).Top(y).Width(width).Height(height).Padding(2).Enter())
        {
            Interactable interact = GetInteractable();

            Color col = ActiveID == interact.ID ? style.ActiveColor :
                HoveredID == interact.ID ? style.HoveredColor : style.BGColor;

            Draw2D.DrawRectFilled(CurrentNode.LayoutData.Rect, col, style.Roundness);
            Draw2D.DrawRect(CurrentNode.LayoutData.Rect, style.BorderColor, style.BorderThickness, style.Roundness);

            if (label == null)
                Draw2D.DrawText(items[itemIndex], CurrentNode.LayoutData.InnerRect, false);
            else
                Draw2D.DrawText(label, CurrentNode.LayoutData.InnerRect, false);

            double popupWidth = CurrentNode.LayoutData.Rect.width;
            if (interact.TakeFocus())
                OpenPopup(popupName, CurrentNode.LayoutData.Rect.BottomLeft);

            y.PixelOffset = 1;
            int NewIndex = itemIndex;
            LayoutNode popupHolder = CurrentNode;
            if (BeginPopup(popupName, out LayoutNode? popupNode, inputstyle: popupstyle ?? style))
            {
                int longestText = 0;
                for (int i = 0; i < items.Length; ++i)
                {
                    Vector2 textSize = Font.DefaultFont.CalcTextSize(items[i], 0);
                    if (textSize.x > longestText)
                        longestText = (int)textSize.x;
                }

                popupWidth = Math.Max(popupWidth, longestText + 20);

                using (popupNode.Width(popupWidth).Height(items.Length * style.ItemSize).MaxHeight(250).Scroll().Layout(LayoutType.Column).Clip().Enter())
                {
                    for (int i = 0; i < items.Length; ++i)
                    {
                        using (Node(popupName + "_Item_" + i).ExpandWidth().Height(style.ItemSize).Enter())
                        {
                            if (IsNodePressed())
                            {
                                NewIndex = i;
                                CloseAllPopups();
                            }
                            else if (IsNodeHovered())
                                Draw2D.DrawRectFilled(CurrentNode.LayoutData.Rect, style.HoveredColor, style.Roundness);

                            Draw2D.DrawText(items[i], CurrentNode.LayoutData.Rect);
                        }
                    }
                }
            }

            if (itemIndex != NewIndex)
            {
                itemIndex = NewIndex;
                return true;
            }

            return false;
        }
    }


    public bool Checkbox(string ID, ref bool value, Offset x, Offset y, out LayoutNode node, WidgetStyle? inputstyle = null)
    {
        WidgetStyle style = inputstyle ?? new(30);
        x.PixelOffset += 2;
        y.PixelOffset += 2;
        using ((node = Node(ID)).Left(x).Top(y).Scale(style.ItemSize - 4).Enter())
        {
            Interactable interact = GetInteractable();

            Color col = ActiveID == interact.ID ? style.ActiveColor :
                HoveredID == interact.ID ? style.HoveredColor : style.BGColor;

            Draw2D.DrawRectFilled(CurrentNode.LayoutData.Rect, col, style.Roundness);
            Draw2D.DrawRect(CurrentNode.LayoutData.Rect, style.BorderColor, style.BorderThickness, style.Roundness);

            if (value)
            {
                Rect check = CurrentNode.LayoutData.Rect;
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


    public void Tooltip(string tip, Vector2? topleft = null, float wrapWidth = -1, TooltipAlign align = TooltipAlign.TopRight)
    {
        if (PreviousInteractableIsHovered() && tip != "")
        {
            int oldZ = ActiveGUI.CurrentZIndex;
            ActiveGUI.SetZIndex(500000);

            Vector2 pos = topleft ?? PointerPos;
            var style = new WidgetStyle(30);
            var margin = new Vector2(10);
            var padding = new Vector2(5);
            Vector2 size = Font.DefaultFont.CalcTextSize(tip, 0, wrapWidth) + padding * 2 - new Vector2(0, 5);
            var offset = new Vector2(0);

            switch (align)
            {
                case TooltipAlign.TopLeft:
                    offset = new Vector2(-size.x - margin.x, -size.y - margin.y);
                    break;
                case TooltipAlign.TopMiddle:
                    offset = new Vector2(-size.x / 2 + margin.x, -size.y - margin.y);
                    break;
                case TooltipAlign.TopRight:
                    offset = new Vector2(margin.x, -size.y - margin.y);
                    break;
                case TooltipAlign.Right:
                    offset = new Vector2(margin.x, -size.y / 2);
                    break;
                case TooltipAlign.Left:
                    offset = new Vector2(-size.x - margin.x, -size.y / 2);
                    break;
                case TooltipAlign.BottomLeft:
                    offset = new Vector2(-size.x - margin.x, margin.y);
                    break;
                case TooltipAlign.BottomMiddle:
                    offset = new Vector2(-size.x / 2, margin.y);
                    break;
                case TooltipAlign.BottomRight:
                    offset = new Vector2(margin.x, margin.y);
                    break;
            }

            // Checks if the tooltip is outside the window, and keeps it aligned inside the window.
            if (pos.x > Screen.InternalWindow.Width / 2)
            {
                if (pos.x < Screen.InternalWindow.Width - offset.x - size.x)
                    pos += new Vector2(offset.x, 0);
                else
                    pos -= new Vector2(margin.x + pos.x + size.x - Screen.InternalWindow.Width, 0);
            }
            else
            {
                if (pos.x < MathD.Abs(offset.x) + margin.x)
                    pos = new Vector2(margin.x, pos.y);
                else
                    pos += new Vector2(offset.x, 0);
            }

            if (pos.y > Screen.InternalWindow.Width / 2)
            {
                if (pos.y < Screen.InternalWindow.Height - offset.y - size.y)
                    pos += new Vector2(0, offset.y);
                else
                    pos -= new Vector2(0, size.y + offset.y);
            }
            else
            {
                if (pos.y < MathD.Abs(offset.y) + margin.y)
                    pos = new Vector2(pos.x, pos.y + margin.y);
                else
                    pos += new Vector2(0, offset.y);
            }

            // Background
            Draw2D.DrawRectFilled(pos, size, style.BorderColor, 5);
            // Message
            Draw2D.DrawText(tip, pos + padding, wrapWidth);

            ActiveGUI.SetZIndex(oldZ);
        }
    }


    public bool ColorPicker(string ID, string popupName, ref Color color, bool hasAlpha, Offset x, Offset y, Size width, Size height, WidgetStyle? inputstyle = null, WidgetStyle? pickerstyle = null)
    {
        WidgetStyle style = inputstyle ??= new(30);

        using (Node(ID).Left(x).Top(y).Width(width).Height(height).Enter())
        {
            Color pure = new Color(color.r, color.g, color.b, 1);
            Color transparent = new Color(1, 1, 1, color.a);

            Rect rect = CurrentNode.LayoutData.Rect;

            Draw2D.DrawRectFilled(rect, pure, style.Roundness);

            Rect footer = rect;
            footer.y += footer.height - 3;
            footer.height = 3;

            Draw2D.DrawRectFilled(footer, transparent, style.Roundness, CornerRounding.Bottom);

            if (GetInteractable().TakeFocus())
                TogglePopup(popupName, rect.BottomLeft);

            if (BeginPopup(popupName, out LayoutNode? popupNode, inputstyle: pickerstyle ?? style))
            {
                float alpha = color.a;
                Color.ToHSV(color, out float hue, out float saturation, out float value);

                if (Color.IsGrayscale(color))
                {
                    hue = GetNodeStorage(popupNode, "HueStore", hue);
                    saturation = GetNodeStorage(popupNode, "SaturationStore", saturation);
                }

                using (popupNode.Scale(256, 382).Layout(LayoutType.Column).Padding(10).Enter())
                {
                    Rect rootRect = CurrentNode.LayoutData.Rect;
                    double minHeight = Math.Min(rootRect.width, rootRect.height);

                    using (Node("HueWheel").Width(minHeight - 20).Height(minHeight - 20).Padding(55).Enter())
                    {
                        const float wheelWidth = 24;

                        Rect cRect = CurrentNode.LayoutData.Rect;
                        double size = Math.Min(cRect.width, cRect.height) / 2;
                        float wheelRadius = (float)size - (wheelWidth / 2);

                        Draw2D.DrawCircle(cRect.Center, wheelRadius, ColorHues, HueWheelSegments, wheelWidth);

                        Vector2 dir = new Vector2(MathD.Cos(hue * MathD.Deg2Rad), MathD.Sin(hue * MathD.Deg2Rad));

                        Draw2D.DrawCircle(cRect.Center + dir * wheelRadius, wheelWidth / 2, Color.white, thickness: 2);

                        Vector2 relativePtr = PointerPos - cRect.Center;
                        double len = relativePtr.magnitude;
                        Vector2 ptrDir = relativePtr / len;

                        Interactable wheelInteract = GetInteractable(cRect, (x, y) => len <= size && len >= size - wheelWidth);

                        if (wheelInteract.IsActive())
                        {
                            hue = (float)(Math.Atan2(-ptrDir.y, -ptrDir.x) * MathD.Rad2Deg) + 180;
                            SetNodeStorage(popupNode, "HueStore", hue);
                        }

                        using (Node("SaturationValueRect").Expand().Enter())
                        {
                            Rect svRect = CurrentNode.LayoutData.Rect;

                            DrawHSVInterpolationRect(hue, svRect);

                            Draw2D.DrawCircle(new Vector2(saturation * svRect.width, value * -1 * svRect.height) + svRect.BottomLeft, 6, Color.white, thickness: 2);

                            if (GetInteractable().IsActive())
                            {
                                relativePtr = PointerPos - svRect.BottomLeft;

                                saturation = (float)MathD.Clamp01(relativePtr.x / svRect.width);
                                SetNodeStorage(popupNode, "SaturationStore", saturation);

                                value = (float)MathD.Clamp01((relativePtr.y / svRect.height) * -1);
                            }
                        }
                    }

                    using (Node("ColorDisplay").Width(80).Top(10).Height(40).Enter())
                    {
                        Rect display = CurrentNode.LayoutData.Rect;

                        Draw2D.DrawVerticalGradient(display.TopLeft, display.BottomLeft, 80, pure, color);
                    }
                }

                hue = Math.Clamp(hue, 0, 360);
                saturation = Math.Clamp(saturation, 0, 1);
                value = Math.Clamp(value, 0, 1);
                alpha = Math.Clamp(alpha, 0, 1);
                Color newColor = Color.FromHSV(hue, saturation, value, alpha);

                if (color != newColor)
                {
                    color = newColor;
                    return true;
                }
            }
        }

        return false;
    }


    // Hardware interpolation doesn't do HSV values, so manually interpolate multiple rects to help it along.
    private void DrawHSVInterpolationRect(float hue, Rect rect)
    {
        int resolution = 4;

        float xOffset = (float)rect.width / resolution;
        float yOffset = (float)rect.height / resolution;

        Vector2 size = new(xOffset, yOffset);

        for (int x = 0; x < resolution; x++)
        {
            for (int y = 0; y < resolution; y++)
            {
                float satA = (float)x / resolution;
                float satB = ((float)x + 1) / resolution;

                float valA = 1 - ((float)y / resolution);
                float valB = 1 - (((float)y + 1) / resolution);

                Vector2 offset = new Vector2(xOffset * x, yOffset * y);
                Draw2D.DrawRectFilledMultiColor(rect.Position + offset, size,
                    Color.FromHSV(hue, satA, valA),
                    Color.FromHSV(hue, satB, valA),
                    Color.FromHSV(hue, satA, valB),
                    Color.FromHSV(hue, satB, valB));
            }
        }
    }


    public bool DrawSlider(string id, ref double value, double min, double max, Offset x, Offset y, Size width, Size height, WidgetStyle? inputstyle = null)
    {
        WidgetStyle style = inputstyle ?? new(30);

        using (Node(id).Left(x).Top(y).Width(width).Height(height).Enter())
        {
            Rect nodeRect = CurrentNode.LayoutData.Rect;

            if (value < max && value > min)
            {
                const int knobRadius = 5;

                using (Node("SliderKnob").Top((nodeRect.height * 0.5) - knobRadius).Scale(knobRadius).Enter())
                {
                    Draw2D.DrawCircleFilled(CurrentNode.LayoutData.Rect.Center, knobRadius * 0.5f, style.TextColor);
                }
            }
        }

        return false;
    }


    public void OpenPopup(string id, Vector2? topleft = null, LayoutNode? popupHolder = null)
    {
        SetNodeStorage(popupHolder ?? CurrentNode, "Popup", true);
        SetNodeStorage(popupHolder ?? CurrentNode, "Popup_ID", id.GetHashCode());
        SetNodeStorage(popupHolder ?? CurrentNode, "Popup_Frame", Time.frameCount);
        SetNodeStorage(popupHolder ?? CurrentNode, "PU_POS_" + id, topleft ?? PointerPos);
    }


    public void TogglePopup(string id, Vector2? topleft = null, LayoutNode? popupHolder = null)
    {
        SetNodeStorage(popupHolder ?? CurrentNode, "Popup", !GetNodeStorage(popupHolder ?? CurrentNode, "Popup", false));
        SetNodeStorage(popupHolder ?? CurrentNode, "Popup_ID", id.GetHashCode());
        SetNodeStorage(popupHolder ?? CurrentNode, "Popup_Frame", Time.frameCount);
        SetNodeStorage(popupHolder ?? CurrentNode, "PU_POS_" + id, topleft ?? PointerPos);
    }


    private static int s_nextPopupIndex;

    public bool BeginPopup(string id, out LayoutNode? node, bool invisible = false, WidgetStyle? inputstyle = null)
    {
        WidgetStyle style = inputstyle ?? new(30);
        node = null;
        bool show = GetNodeStorage<bool>("Popup");
        show &= GetNodeStorage<int>("Popup_ID") == id.GetHashCode();
        // If this node is showing a Popup and the Popup ID is the same as this ID
        if (show)
        {
            Vector2 pos = GetNodeStorage<Vector2>("PU_POS_" + id);
            LayoutNode parentNode = CurrentNode;
            // Append to Root
            using ((node = rootNode.AppendNode("PU_" + id)).Left(pos.x).Top(pos.y).IgnoreLayout().Enter())
            {
                SetZIndex(50000 + s_nextPopupIndex, false);
                BlockInteractables(CurrentNode.LayoutData.Rect);

                // Clamp node position so that its always in screen bounds
                Rect rect = CurrentNode.LayoutData.Rect;
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
                    if (IsPointerClick(MouseButton.Left) || IsPointerClick(MouseButton.Middle) || IsPointerClick(MouseButton.Right))
                    {
                        bool isMouseContained = node.LayoutData.Rect.Contains(PointerPos); // Mouse not in Popup
                        bool isMouseBlocked = IsBlockedByInteractable(PointerPos, 50000 + s_nextPopupIndex); // Not blocked by any interactables above this popup
                                                                                                             //!parentNode.LayoutData.Rect.Contains(PointerPos) && // Mouse not in Parent
                        if (!IsPointerMoving && !isMouseContained && !isMouseBlocked)
                        {
                            CloseAllPopups();
                            return false;
                        }
                    }
                }

                if (!invisible)
                {
                    Draw2D.PushClip(ScreenRect, true);
                    Draw2D.DrawRectFilled(CurrentNode.LayoutData.Rect, style.BGColor, style.Roundness);
                    Draw2D.DrawRect(CurrentNode.LayoutData.Rect, style.BorderColor, 2, style.Roundness);
                    Draw2D.PopClip();
                }

                s_nextPopupIndex++;
            }
        }
        return show;
    }


    public void CloseAllPopups()
    {
        var stack = new Stack<LayoutNode>();
        stack.Push(rootNode);

        while (stack.Count > 0)
        {
            var target = stack.Pop();

            if (GetNodeStorage<bool>(target, "Popup"))
            {
                SetNodeStorage(target, "Popup", false);
                SetNodeStorage(target, "Popup_ID", -1);
            }

            // Push all children onto the stack
            foreach (LayoutNode child in target.Children)
            {
                stack.Push(child);
            }
        }
    }


    public bool Search(string ID, ref string searchText, Offset x, Offset y, Size width, Size? height = null, WidgetStyle? inputstyle = null, bool enterReturnsTrue = true)
    {
        WidgetStyle style = inputstyle ?? new(30);
        searchText ??= "";
        Gui g = ActiveGUI;

        bool changed = InputField(ID, ref searchText, 32, enterReturnsTrue ? InputFieldFlags.EnterReturnsTrue : InputFieldFlags.None, x, y, width, height, style);

        if (string.IsNullOrWhiteSpace(searchText) && !g.PreviousInteractableIsFocus())
        {
            Vector2 pos = g.PreviousNode.LayoutData.InnerRect.Position + new Vector2(8, 3);
            // Center text vertically
            pos.y += (g.PreviousNode.LayoutData.InnerRect.height - style.FontSize) / 2;
            g.Draw2D.DrawText(Font.DefaultFont, FontAwesome6.MagnifyingGlass + " Search...", style.FontSize, pos, Color.white * 0.6f);
        }

        return changed;
    }

}
