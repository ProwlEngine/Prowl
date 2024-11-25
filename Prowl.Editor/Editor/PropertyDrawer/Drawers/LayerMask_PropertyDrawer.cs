// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Text;

using Prowl.Editor.Preferences;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Layout;

namespace Prowl.Editor.PropertyDrawers;

[Drawer(typeof(LayerMask))]
public class LayerMask_PropertyDrawer : PropertyDrawer
{
    public override double MinWidth => 125;

    public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value, List<Attribute>? attributes = null)
    {
        double ItemSize = EditorStylePrefs.Instance.ItemSize;

        LayerMask maskValue = (LayerMask)value!;
        var layers = TagLayerManager.GetLayers();

        var g = Gui.ActiveGUI;
        using (g.Node(ID).ExpandWidth().Height(ItemSize).Enter())
        {
            Interactable interact = g.GetInteractable();

            var col = g.ActiveID == interact.ID ? EditorStylePrefs.Instance.Highlighted :
                g.HoveredID == interact.ID ? EditorStylePrefs.Instance.Hovering : EditorStylePrefs.Instance.WindowBGOne;

            g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, col, (float)EditorStylePrefs.Instance.ButtonRoundness);
            g.Draw2D.DrawRect(g.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Borders, 1f, (float)EditorStylePrefs.Instance.ButtonRoundness);

            StringBuilder sb = new();
            bool everything = maskValue.Mask == LayerMask.Everything.Mask;
            if (!everything)
            {
                for (int i = 0; i < layers.Count; i++)
                {
                    if (maskValue.HasLayer((byte)i) && layers[i].Length > 0)
                    {
                        sb.Append(layers[i]);
                        sb.Append(", ");
                    }
                }
            }
            else
            {
                sb.Append("Everything");
            }

            g.Draw2D.DrawText(sb.Length <= 0 ? "No Layers." : sb.ToString(), g.CurrentNode.LayoutData.InnerRect, false);

            var popupWidth = g.CurrentNode.LayoutData.Rect.width;
            if (interact.TakeFocus())
                g.OpenPopup("LayerMask_Popup_" + ID, g.CurrentNode.LayoutData.Rect.BottomLeft);

            if (g.BeginPopup("LayerMask_Popup_" + ID, out var popupNode, false, EditorGUI.InputStyle))
            {
                int longestText = 0;
                for (var Index = 0; Index < layers.Count; ++Index)
                {
                    var textSize = Font.DefaultFont.CalcTextSize(layers[Index], 0);
                    if (textSize.x > longestText)
                        longestText = (int)textSize.x;
                }

                popupWidth = Math.Max(popupWidth, longestText + 20);

                using (popupNode.Width(popupWidth).FitContentHeight().Layout(LayoutType.Column).Enter())
                {
                    NothingButton(ItemSize, ref maskValue, g);
                    EverythingButton(ItemSize, ref maskValue, g);

                    for (int i = 0; i < layers.Count; i++)
                    {
                        if (string.IsNullOrEmpty(layers[i]))
                            continue;

                        LayerButton(ItemSize, ref maskValue, layers, g, i);
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

    private static void LayerButton(double ItemSize, ref LayerMask maskValue, IReadOnlyList<string> layers, Gui g, int i)
    {
        using (g.Node("Item_" + i).ExpandWidth().Height(ItemSize).Enter())
        {
            bool hasLayer = maskValue.HasLayer((byte)i);
            if (hasLayer)
                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Highlighted, (float)EditorStylePrefs.Instance.ButtonRoundness);

            if (g.IsNodePressed())
            {
                if (hasLayer)
                    maskValue.RemoveLayer((byte)i);
                else
                    maskValue.SetLayer((byte)i);
                g.CloseAllPopups();
            }
            else if (g.IsNodeHovered())
                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Hovering, (float)EditorStylePrefs.Instance.ButtonRoundness);

            g.Draw2D.DrawText(i + ". " + layers[i], g.CurrentNode.LayoutData.Rect);
        }
    }

    private static void NothingButton(double ItemSize, ref LayerMask maskValue, Gui g)
    {
        using (g.Node("NothingBtn").ExpandWidth().Height(ItemSize).Enter())
        {
            if (g.IsNodePressed())
            {
                maskValue.Clear();
                g.CloseAllPopups();
            }
            else if (g.IsNodeHovered())
                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Hovering, (float)EditorStylePrefs.Instance.ButtonRoundness);

            g.Draw2D.DrawText("Nothing", g.CurrentNode.LayoutData.Rect);
        }
    }

    private static void EverythingButton(double ItemSize, ref LayerMask maskValue, Gui g)
    {
        using (g.Node("EverythingBtn").ExpandWidth().Height(ItemSize).Enter())
        {
            if (g.IsNodePressed())
            {
                maskValue.Clear();
                for (int i = 0; i < 32; i++)
                    maskValue.SetLayer((byte)i);

                g.CloseAllPopups();
            }
            else if (g.IsNodeHovered())
                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Hovering, (float)EditorStylePrefs.Instance.ButtonRoundness);

            g.Draw2D.DrawText("Everything", g.CurrentNode.LayoutData.Rect);
        }
    }
}
