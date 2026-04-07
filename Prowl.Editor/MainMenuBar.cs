using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.Editor;

public static class MainMenuBar
{
    private const float DropdownWidth = 200f;
    private const float ItemHeight = 24f;

    private static int _openMenuIndex = -1;

    public static void Draw(Paper paper)
    {
        var font = EditorTheme.DefaultFont;
        var items = MenuRegistry.RootMenus;

        using (paper.Row("menubar")
            .PositionType(PositionType.SelfDirected)
            .Position(0, 0)
            .Size(paper.Percent(100), EditorTheme.MenuBarHeight)
            .BackgroundColor(EditorTheme.Neutral200)
            .ChildLeft(10)
            .RowBetween(10)
            .Enter())
        {
            for (int i = 0; i < items.Count; i++)
            {
                int index = i;
                var item = items[i];

                using (paper.Box($"menu_{index}")
                    .Height(EditorTheme.MenuBarHeight)
                    .Width(UnitValue.Auto)
                    .BackgroundColor(_openMenuIndex == index ? EditorTheme.Ink200 : Color.Transparent)
                    .Hovered.BackgroundColor(EditorTheme.Ink200).End()
                        .Text(item.Label, font)
                        .TextColor(EditorTheme.Ink500)
                        .Alignment(TextAlignment.MiddleCenter)
                        .FontSize(EditorTheme.FontSize)
                    .OnClick(index, (idx, _) =>
                    {
                        _openMenuIndex = _openMenuIndex == idx ? -1 : idx;
                    })
                    .Enter())
                {
                    // If hovering a different menu while one is open, switch to it
                    if (_openMenuIndex >= 0 && _openMenuIndex != index && paper.IsParentHovered)
                        _openMenuIndex = index;
                }
            }
        }

        // Render the open dropdown outside the menubar row (so backdrop covers everything)
        if (_openMenuIndex >= 0 && _openMenuIndex < items.Count)
        {
            var openItem = items[_openMenuIndex];
            if (openItem.HasSubItems)
            {
                // Backdrop — click anywhere outside to close
                paper.Box("menubar_backdrop")
                    .PositionType(PositionType.SelfDirected)
                    .Position(0, EditorTheme.MenuBarHeight)
                    .Size(99999, 99999)
                    .BackgroundColor(Color.FromArgb(20, 0, 0, 0))
                    .Layer(Layer.Topmost)
                    .OnClick(0, (_, _) => _openMenuIndex = -1);

                // Calculate X position: 10px left margin + sum of previous menu widths + gaps
                // We approximate since we don't have exact layout data
                float xPos = 10f + _openMenuIndex * 60f; // rough estimate

                RenderDropdown(paper, $"dd_{_openMenuIndex}", openItem.SubItems, xPos, EditorTheme.MenuBarHeight - 2);
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
            .BackgroundColor(EditorTheme.Neutral300)
            .BorderColor(EditorTheme.Ink200)
            .BorderWidth(1)
            .Rounded(4)
            .ChildTop(2).ChildBottom(2)
            .ChildLeft(2).ChildRight(2)
            .Layer(Layer.Topmost)
            .ClampToScreen()
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
                        .BackgroundColor(EditorTheme.Ink200);
                    continue;
                }

                var textColor = item.IsEnabled ? EditorTheme.Ink500 : EditorTheme.Ink300;

                using (paper.Row($"{id}_i_{index}")
                    .Height(ItemHeight)
                    .BackgroundColor(Color.Transparent)
                    .Rounded(3)
                    .Hovered.BackgroundColor(EditorTheme.Purple400).End()
                    .OnClick(item, (captured, e) =>
                    {
                        if (captured.IsEnabled && captured.OnClick != null)
                        {
                            captured.OnClick();
                            _openMenuIndex = -1;
                        }
                    })
                    .Enter())
                {
                    if (font != null)
                    {
                        paper.Box($"{id}_chk_{index}")
                            .Width(24)
                            .Text(item.IsChecked ? "\u2713" : "", font)
                            .TextColor(textColor)
                            .FontSize(EditorTheme.FontSize);

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
                            RenderDropdown(paper, $"{id}_s_{index}", item.SubItems, DropdownWidth - 5, 0);
                    }
                }
            }
        }
    }
}
