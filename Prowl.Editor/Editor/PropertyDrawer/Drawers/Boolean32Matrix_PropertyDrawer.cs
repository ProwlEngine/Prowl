// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Preferences;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.Utils;

namespace Prowl.Editor.PropertyDrawers;

[Drawer(typeof(Boolean32Matrix))]
public class Boolean32Matrix_PropertyDrawer : PropertyDrawer
{
    private const float CELL_SIZE = 20f;
    private static bool showLabels = true;

    public override bool PropertyLayout(Gui gui, string label, int index, Type propertyType, ref object? value, EditorGUI.PropertyGridConfig config, List<Attribute>? attributes = null)
    {
        if (value == null)
        {
            value = new Boolean32Matrix(true);
            return true;
        }

        var iSize = EditorStylePrefs.Instance.ItemSize;

        Boolean32Matrix matrix = (Boolean32Matrix)value;
        bool changed = false;
        var layers = TagLayerManager.GetLayers();

        using (gui.Node(label + "_Matrix").ExpandWidth().FitContentHeight().Layout(LayoutType.Column).Enter())
        {
            // Background
            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.WindowBGOne, (float)EditorStylePrefs.Instance.WindowRoundness);

            // Header toolbar
            using (gui.Node("Header").ExpandWidth().Height(iSize).Layout(LayoutType.Row).Padding(5).Enter())
            {
                // Toggle labels button
                using (gui.Node("ToggleLabels").Scale(iSize).Enter())
                {
                    if (gui.IsNodePressed())
                        showLabels = !showLabels;

                    if (gui.IsNodeHovered())
                        gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Hovering);

                    gui.Draw2D.DrawText(showLabels ? FontAwesome6.TableList : FontAwesome6.TableCells,
                        gui.CurrentNode.LayoutData.InnerRect,
                        gui.IsNodeHovered() ? Color.white : EditorStylePrefs.Instance.LesserText);
                }

                // Quick actions
                using (gui.Node("Actions").Layout(LayoutType.Row).Enter())
                {
                    using (gui.Node("AllBtn").Width(iSize).Height(iSize).Enter())
                    {
                        if (gui.IsNodePressed())
                        {
                            matrix.SetAll(true);
                            changed = true;
                        }
                        if (gui.IsNodeHovered())
                            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Hovering);
                        gui.Draw2D.DrawText("All", gui.CurrentNode.LayoutData.InnerRect);
                        gui.Tooltip("Set all collision to true.");
                    }

                    using (gui.Node("NoneBtn").Width(iSize * 2).Height(iSize).Enter())
                    {
                        if (gui.IsNodePressed())
                        {
                            matrix.SetAll(false);
                            changed = true;
                        }
                        if (gui.IsNodeHovered())
                            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Hovering);
                        gui.Draw2D.DrawText("None", gui.CurrentNode.LayoutData.InnerRect);
                        gui.Tooltip("Set all collision to false.");
                    }

                    using (gui.Node("SymBtn").Width(iSize * 2).Height(iSize).Enter())
                    {
                        if (gui.IsNodePressed())
                        {
                            matrix.MakeSymmetric();
                            changed = true;
                        }
                        if (gui.IsNodeHovered())
                            gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.Rect, EditorStylePrefs.Instance.Hovering);
                        gui.Draw2D.DrawText("Sym", gui.CurrentNode.LayoutData.InnerRect);

                        gui.Tooltip("If at any point the Collision Matrix is not Symmetrical, This will fix that.");
                    }
                }
            }

            // Matrix content in scrollable area
            using (gui.Node("ScrollView").ExpandWidth().FitContentHeight().Layout(LayoutType.Column).Enter())
            {
                // Labels row
                if (showLabels)
                {
                    using (gui.Node("Labels").ExpandWidth().Height(CELL_SIZE).Layout(LayoutType.Row).Enter())
                    {
                        // Empty corner cell
                        gui.Node("Corner").Width(CELL_SIZE * 6).Height(CELL_SIZE);

                        // Layer labels - vertical text
                        for (int i = 0; i < layers.Count; i++)
                        {
                            if (string.IsNullOrEmpty(layers[i]))
                                continue;

                            using (gui.Node($"Label_{i}").Scale(CELL_SIZE).Enter())
                            {
                                // Calculate positioning for vertical text
                                string text = layers[i];

                                gui.Draw2D.DrawText(
                                    text[0].ToString(),
                                    gui.CurrentNode.LayoutData.Rect,
                                    EditorStylePrefs.Instance.LesserText
                                );

                                gui.GetInteractable(); // Create interactable for Tooltip

                                gui.Tooltip(text);
                            }
                        }
                    }
                }

                // Matrix rows
                for (int row = 0; row < layers.Count; row++)
                {
                    if (string.IsNullOrEmpty(layers[row]))
                        continue;

                    using (gui.Node($"Row_{row}").ExpandWidth().Height(CELL_SIZE).Layout(LayoutType.Row).Enter())
                    {
                        // Row label
                        if (showLabels)
                        {
                            using (gui.Node($"RowLabel_{row}").Width(CELL_SIZE * 6).Height(CELL_SIZE).Enter())
                            {
                                gui.Draw2D.DrawText(layers[row], gui.CurrentNode.LayoutData.InnerRect);
                            }
                        }

                        // Checkboxes
                        for (int col = 0; col < layers.Count; col++)
                        {
                            if (string.IsNullOrEmpty(layers[col]))
                                continue;

                            using (gui.Node($"Cell_{row}_{col}").Scale(CELL_SIZE).Enter())
                            {
                                bool val = matrix[row, col];

                                // Draw checkbox background
                                var color = val ? EditorStylePrefs.Instance.Highlighted : EditorStylePrefs.Instance.WindowBGTwo;
                                if (gui.IsNodeHovered())
                                    color = Color.Lerp(color, EditorStylePrefs.Instance.Hovering, 0.3f);

                                gui.Draw2D.DrawRectFilled(gui.CurrentNode.LayoutData.InnerRect, color);
                                gui.Draw2D.DrawRect(gui.CurrentNode.LayoutData.InnerRect, EditorStylePrefs.Instance.Borders);

                                if (gui.IsNodePressed())
                                {
                                    matrix.SetSymmetric(row, col, !val);
                                    changed = true;
                                }

                                // Tooltip
                                gui.Tooltip($"{layers[row]} vs {layers[col]}");
                            }
                        }
                    }
                }
            }
        }

        return changed;
    }
}
