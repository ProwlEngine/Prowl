using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.Editor;

public static class MainMenuBar
{
    private const float DropdownWidth = 200f;
    private const float ItemHeight = 24f;

    public static void Draw(Paper paper)
    {
        var font = EditorTheme.DefaultFont;
        var items = MenuRegistry.RootMenus;

        using (paper.Row("menubar")
            .PositionType(PositionType.SelfDirected)
            .Position(0, 0)
            .Size(paper.Percent(100), EditorTheme.MenuBarHeight)
            .BackgroundColor(EditorTheme.MenuBarBackground)
            .BorderColor(EditorTheme.Border)
            .BorderBottom(1)
            .ChildLeft(4)
            .Enter())
        {
            for (int i = 0; i < items.Count; i++)
            {
                int index = i;
                var item = items[i];

                // Each top-level menu item — the dropdown is a child so IsParentHovered
                // covers both the label and the dropdown area
                using (paper.Box($"menu_{index}")
                    .Height(EditorTheme.MenuBarHeight)
                    .ChildLeft(8).ChildRight(8)
                    .Rounded(3)
                    .BackgroundColor(paper.IsParentHovered ? EditorTheme.ButtonHovered : Color.Transparent)
                    .Hovered.BackgroundColor(EditorTheme.ButtonHovered).End()
                    .Enter())
                {
                    if (font != null)
                        paper.Box($"menu_lbl_{index}")
                            .Text(item.Label, font)
                            .TextColor(EditorTheme.Text)
                            .FontSize(EditorTheme.FontSize);

                    if (paper.IsParentHovered && item.HasSubItems)
                    {
                        // Position dropdown flush with the bottom of the menu bar, slight overlap
                        RenderDropdown(paper, $"dd_{index}", item.SubItems, 0, EditorTheme.MenuBarHeight - 2);
                    }
                }
            }
        }
    }

    private static void RenderDropdown(Paper paper, string id, List<MenuItem> items, float x, float y)
    {
        var font = EditorTheme.DefaultFont;

        using (paper.Column(id)
            .PositionType(PositionType.SelfDirected)
            .Position(x, y)
            .Width(DropdownWidth)
            .Height(UnitValue.Auto)
            .BackgroundColor(EditorTheme.PanelBackground)
            .BorderColor(EditorTheme.Border)
            .BorderWidth(1)
            .Rounded(4)
            .ChildTop(2).ChildBottom(2)
            .ChildLeft(2).ChildRight(2)
            .Layer(Layer.Topmost)
            .Enter())
        {
            for (int i = 0; i < items.Count; i++)
            {
                int index = i;
                var item = items[i];

                if (item.IsSeparator)
                {
                    paper.Box($"{id}_sep_{index}")
                        .Height(1)
                        .Margin(4, 4, 4, 4)
                        .BackgroundColor(EditorTheme.Border);
                    continue;
                }

                var textColor = item.IsEnabled ? EditorTheme.Text : EditorTheme.TextDisabled;

                // Menu item row — submenu is a child so IsParentHovered keeps it open
                using (paper.Row($"{id}_i_{index}")
                    .Height(ItemHeight)
                    .BackgroundColor(Color.Transparent)
                    .Rounded(3)
                    .Hovered.BackgroundColor(EditorTheme.Accent).End()
                    .OnClick(item, (captured, e) =>
                    {
                        if (captured.IsEnabled && captured.OnClick != null)
                            captured.OnClick();
                    })
                    .Enter())
                {
                    // Checkmark / spacer
                    if (font != null)
                    {
                        paper.Box($"{id}_chk_{index}")
                            .Width(24)
                            .Text(item.IsChecked ? "\u2713" : "", font)
                            .TextColor(textColor)
                            .FontSize(EditorTheme.FontSize);
                    }

                    // Label
                    if (font != null)
                    {
                        paper.Box($"{id}_lbl_{index}")
                            .Text(item.Label, font)
                            .TextColor(textColor)
                            .FontSize(EditorTheme.FontSize);
                    }

                    // Submenu arrow + render on hover
                    if (item.HasSubItems)
                    {
                        if (font != null)
                        {
                            paper.Box($"{id}_arr_{index}")
                                .Width(20)
                                .Margin(0, 4, 0, 0)
                                .Text("\u25B6", font)
                                .TextColor(textColor)
                                .FontSize(10f);
                        }

                        if (paper.IsParentHovered)
                        {
                            // Overlap by 5px so mouse can travel to submenu without gap
                            RenderDropdown(paper, $"{id}_s_{index}", item.SubItems, DropdownWidth - 5, 0);
                        }
                    }
                }
            }
        }
    }
}
