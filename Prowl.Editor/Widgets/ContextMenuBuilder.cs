using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Widgets;

public class ContextMenuBuilder
{
    private readonly List<ContextMenuItem> _items = new();
    private Action? _onClose;

    internal void SetCloseAction(Action onClose) => _onClose = onClose;

    public ContextMenuBuilder Item(string label, Action onClick, bool enabled = true)
    {
        _items.Add(new ContextMenuItem { Label = label, OnClick = onClick, IsEnabled = enabled });
        return this;
    }

    public ContextMenuBuilder Separator()
    {
        _items.Add(new ContextMenuItem { IsSeparator = true });
        return this;
    }

    public ContextMenuBuilder Submenu(string label, Action<ContextMenuBuilder> build)
    {
        var sub = new ContextMenuBuilder();
        sub._onClose = _onClose;
        build(sub);
        _items.Add(new ContextMenuItem { Label = label, SubMenu = sub, IsEnabled = true });
        return this;
    }

    public void Render(Paper paper, string id, float x, float y)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        using (paper.Column(id)
            .PositionType(PositionType.SelfDirected)
            .Position(x, y)
            .Width(180)
            .Height(UnitValue.Auto)
            .BackgroundColor(EditorTheme.PanelBackground)
            .BorderColor(EditorTheme.Border).BorderWidth(1)
            .Rounded(4)
            .ChildTop(2).ChildBottom(2).ChildLeft(2).ChildRight(2)
            .Layer(Layer.Topmost)
            .Enter())
        {
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];

                if (item.IsSeparator)
                {
                    paper.Box($"{id}_sep_{i}")
                        .Height(1).Margin(4, 3, 4, 3)
                        .BackgroundColor(EditorTheme.Border);
                    continue;
                }

                var textColor = item.IsEnabled ? EditorTheme.Text : EditorTheme.TextDisabled;

                using (paper.Row($"{id}_i_{i}")
                    .Height(EditorTheme.RowHeight)
                    .ChildLeft(8).ChildRight(4)
                    .BackgroundColor(Color.Transparent)
                    .Hovered.BackgroundColor(item.IsEnabled ? EditorTheme.Accent : Color.Transparent).End()
                    .Rounded(3)
                    .OnClick(item, (captured, e) =>
                    {
                        if (captured.IsEnabled)
                        {
                            captured.OnClick?.Invoke();
                            _onClose?.Invoke();
                        }
                    })
                    .Enter())
                {
                    paper.Box($"{id}_l_{i}")
                        .Width(UnitValue.Stretch()).IsNotInteractable()
                        .Text(item.Label, font).TextColor(textColor).FontSize(EditorTheme.FontSize);

                    if (item.SubMenu != null)
                    {
                        paper.Box($"{id}_a_{i}").Width(16).IsNotInteractable()
                            .Text("\u25B6", font).TextColor(EditorTheme.TextDim).FontSize(10f);

                        if (paper.IsParentHovered)
                            item.SubMenu.Render(paper, $"{id}_s_{i}", 175, 0);
                    }
                }
            }
        }
    }

    private struct ContextMenuItem
    {
        public string Label;
        public Action? OnClick;
        public bool IsSeparator;
        public bool IsEnabled;
        public ContextMenuBuilder? SubMenu;
    }
}

/// <summary>
/// Shows a context menu on right-click of the current parent element.
/// Call inside an Enter() block. Uses FocusWithin to stay open.
/// </summary>
public static class ContextMenuHelper
{
    public static bool RightClickMenu(Paper paper, string id, Action<ContextMenuBuilder> build)
    {
        var parentEl = paper.CurrentParent;
        bool isOpen = paper.GetElementStorage(parentEl, $"{id}_open", false);
        float menuX = paper.GetElementStorage(parentEl, $"{id}_x", 0f);
        float menuY = paper.GetElementStorage(parentEl, $"{id}_y", 0f);

        // Right-click opens at cursor position and focuses the parent
        parentEl.Data.OnRightClick += e =>
        {
            paper.SetElementStorage(parentEl, $"{id}_open", true);
            paper.SetElementStorage(parentEl, $"{id}_x", (float)e.RelativePosition.X);
            paper.SetElementStorage(parentEl, $"{id}_y", (float)e.RelativePosition.Y);
            paper.SetFocus(parentEl);
        };

        if (isOpen)
        {
            var builder = new ContextMenuBuilder();
            builder.SetCloseAction(() => paper.SetElementStorage(parentEl, $"{id}_open", false));
            build(builder);

            using (paper.Box($"{id}_anchor")
                .PositionType(PositionType.SelfDirected)
                .Position(menuX, menuY)
                .Width(UnitValue.Auto).Height(UnitValue.Auto)
                .Enter())
            {
                builder.Render(paper, $"{id}_ctx", 0, 0);
            }

            // Close when focus leaves the parent subtree (clicked elsewhere)
            if (!paper.IsParentFocusWithin)
                paper.SetElementStorage(parentEl, $"{id}_open", false);
        }

        return isOpen;
    }
}
