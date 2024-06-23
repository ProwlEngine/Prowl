using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Graphics;
using System.Text;

namespace Prowl.Editor.PropertyDrawers
{
    [Drawer(typeof(LayerMask))]
    public class LayerMask_PropertyDrawer : PropertyDrawer
    {
        public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value)
        {
            LayerMask maskValue = (LayerMask)value!;
            string[] layers = TagLayerManager.GetLayers();

            GuiStyle style = new();
            var g = Gui.ActiveGUI;
            using (g.Node(ID).ExpandWidth().Height(GuiStyle.ItemHeight).Enter())
            {
                Interactable interact = g.GetInteractable();

                var col = g.ActiveID == interact.ID ? style.BtnActiveColor :
                          g.HoveredID == interact.ID ? style.BtnHoveredColor : style.WidgetColor;

                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, col, style.WidgetRoundness);
                if (style.BorderThickness > 0)
                    g.Draw2D.DrawRect(g.CurrentNode.LayoutData.Rect, style.Border, style.BorderThickness, style.WidgetRoundness);

                StringBuilder sb = new();
                for (int i = 0; i < layers.Length; i++)
                {
                    if (maskValue.HasLayer((byte)i))
                    {
                        sb.Append(layers[i]);
                        sb.Append(", ");
                    }
                }

                g.Draw2D.DrawText(style.Font.IsAvailable ? style.Font.Res : UIDrawList.DefaultFont, sb.Length <= 0 ? "No Layers." : sb.ToString(), style.FontSize, g.CurrentNode.LayoutData.InnerRect, style.TextColor, false);

                var popupWidth = g.CurrentNode.LayoutData.Rect.width;
                if (interact.TakeFocus())
                    g.OpenPopup("LayerMask_Popup_" + ID, g.CurrentNode.LayoutData.Rect.BottomLeft);

                var popupHolder = g.CurrentNode;
                if (g.BeginPopup("LayerMask_Popup_" + ID, out var popupNode))
                {
                    int longestText = 0;
                    for (var Index = 0; Index < layers.Length; ++Index)
                    {
                        var textSize = style.Font.IsAvailable ? style.Font.Res.CalcTextSize(layers[Index], style.FontSize, 0) : UIDrawList.DefaultFont.CalcTextSize(layers[Index], style.FontSize, 0);
                        if (textSize.x > longestText)
                            longestText = (int)textSize.x;
                    }

                    popupWidth = Math.Max(popupWidth, longestText + 20);

                    using (popupNode.Width(popupWidth).FitContentHeight().Layout(LayoutType.Column).Enter())
                    {
                        for (int i = 0; i < layers.Length; i++)
                        {
                            if (string.IsNullOrEmpty(layers[i]))
                                continue;

                            using (g.Node("Item_" + i).ExpandWidth().Height(GuiStyle.ItemHeight).Enter())
                            {
                                bool hasLayer = maskValue.HasLayer((byte)i);
                                if (hasLayer)
                                    g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.Indigo * 0.8f, style.WidgetRoundness);

                                if (g.IsNodePressed())
                                {
                                    if(hasLayer)
                                        maskValue.RemoveLayer((byte)i);
                                    else
                                        maskValue.SetLayer((byte)i);
                                    g.ClosePopup(popupHolder);
                                }
                                else if (g.IsNodeHovered())
                                    g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, style.BtnHoveredColor, style.WidgetRoundness);

                                g.Draw2D.DrawText(i + ". " + layers[i], g.CurrentNode.LayoutData.Rect, style.TextColor);
                            }
                        }
                    }
                }

                if (maskValue.Mask != ((LayerMask)value).Mask)
                {
                    value = maskValue;
                    return true;
                }

                return false;
            }
        }
    }

}
