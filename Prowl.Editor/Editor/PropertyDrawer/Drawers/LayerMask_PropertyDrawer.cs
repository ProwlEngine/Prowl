using Prowl.Editor.Preferences;
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
            double ItemSize = EditorStylePrefs.Instance.ItemSize;

            LayerMask maskValue = (LayerMask)value!;
            string[] layers = TagLayerManager.GetLayers();

            var g = Gui.ActiveGUI;
            using (g.Node(ID).ExpandWidth().Height(ItemSize).Enter())
            {
                Interactable interact = g.GetInteractable();

                var col = g.ActiveID == interact.ID ? EditorStylePrefs.Instance.Highlighted :
                          g.HoveredID == interact.ID ? EditorStylePrefs.Instance.Hovering : EditorStylePrefs.Instance.WindowBGOne;

                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, col, (float)EditorStylePrefs.Instance.ButtonRoundness);
                g.Draw2D.DrawRect(g.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Borders, 1f, (float)EditorStylePrefs.Instance.ButtonRoundness);

                StringBuilder sb = new();
                for (int i = 0; i < layers.Length; i++)
                {
                    if (maskValue.HasLayer((byte)i))
                    {
                        sb.Append(layers[i]);
                        sb.Append(", ");
                    }
                }

                g.Draw2D.DrawText(sb.Length <= 0 ? "No Layers." : sb.ToString(), g.CurrentNode.LayoutData.InnerRect, false);

                var popupWidth = g.CurrentNode.LayoutData.Rect.width;
                if (interact.TakeFocus())
                    g.OpenPopup("LayerMask_Popup_" + ID, g.CurrentNode.LayoutData.Rect.BottomLeft);

                var popupHolder = g.CurrentNode;
                if (g.BeginPopup("LayerMask_Popup_" + ID, out var popupNode))
                {
                    int longestText = 0;
                    for (var Index = 0; Index < layers.Length; ++Index)
                    {
                        var textSize = UIDrawList.DefaultFont.CalcTextSize(layers[Index], 0);
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

                            using (g.Node("Item_" + i).ExpandWidth().Height(ItemSize).Enter())
                            {
                                bool hasLayer = maskValue.HasLayer((byte)i);
                                if (hasLayer)
                                    g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted, (float)EditorStylePrefs.Instance.ButtonRoundness);

                                if (g.IsNodePressed())
                                {
                                    if(hasLayer)
                                        maskValue.RemoveLayer((byte)i);
                                    else
                                        maskValue.SetLayer((byte)i);
                                    g.ClosePopup(popupHolder);
                                }
                                else if (g.IsNodeHovered())
                                    g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Hovering, (float)EditorStylePrefs.Instance.ButtonRoundness);

                                g.Draw2D.DrawText(i + ". " + layers[i], g.CurrentNode.LayoutData.Rect);
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
