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

    public ContextMenuBuilder Item(string label, Action onClick, bool enabled = true, string icon = "")
    {
        _items.Add(new ContextMenuItem { Label = label, OnClick = onClick, IsEnabled = enabled, Icon = icon });
        return this;
    }

    public ContextMenuBuilder Separator()
    {
        _items.Add(new ContextMenuItem { IsSeparator = true });
        return this;
    }

    public ContextMenuBuilder Submenu(string label, Action<ContextMenuBuilder> build, string icon = "")
    {
        var sub = new ContextMenuBuilder();
        sub._onClose = _onClose;
        build(sub);
        _items.Add(new ContextMenuItem { Label = label, SubMenu = sub, IsEnabled = true, Icon = icon });
        return this;
    }

    public void Render(Paper paper, string id, float x, float y)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        using (paper.Box(id)
            .PositionType(PositionType.SelfDirected)
            .Position(x, y)
            .Width(200)
            .Height(UnitValue.Auto)
            .BackgroundColor(EditorTheme.Purple200)
            .BorderColor(EditorTheme.Ink200).BorderWidth(1.25f)
            .Rounded(4)
            .Layer(Layer.Topmost)
            .ClampToScreen()
            .BoxShadow(0, 0, 40, -25, Color.FromArgb(155, Color.Black))
            .Enter())
        {
            using (paper.Column(id)
                .Margin(4)
                .Height(UnitValue.Auto)
                .Enter())

            {
                for (int i = 0; i < _items.Count; i++)
                {
                    var item = _items[i];

                    if (item.IsSeparator)
                    {
                        paper.Box($"{id}_sep_{i}")
                            .Height(1.25f).Margin(10, 5)
                            .BackgroundColor(EditorTheme.Ink200);
                        continue;
                    }

                    var textColor = item.IsEnabled ? EditorTheme.Text : EditorTheme.TextDisabled;

                    using (paper.Row($"{id}_i_{i}")
                        .Height(EditorTheme.RowHeight)
                        .Hovered.BackgroundColor(item.IsEnabled ? EditorTheme.Purple400 : Color.Transparent).End()
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
                        if (string.IsNullOrWhiteSpace(item.Icon))
                        {
                            paper.Box($"{id}_l_{i}")
                                .Width(UnitValue.Stretch())
                                .Margin(10, 0, 0, 0)
                                .Height(EditorTheme.RowHeight)
                                .Text(item.Label, font).TextColor(textColor).FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);
                        }
                        else
                        {
                            paper.Box($"{id}_i_{i}")
                                .Margin(10, 0, 0, 0)
                                .Size(EditorTheme.RowHeight)
                                .Text(item.Icon, font).TextColor(textColor).FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

                            paper.Box($"{id}_l_{i}")
                                .Width(UnitValue.Stretch())
                                .Margin(5, 0, 0, 0)
                                .Height(EditorTheme.RowHeight)
                                .Text(item.Label, font).TextColor(textColor).FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);
                        }


                        if (item.SubMenu != null)
                        {
                            paper.Box($"{id}_a_{i}")
                                .Size(EditorTheme.RowHeight)
                                .Text(EditorIcons.ChevronRight, font).TextColor(EditorTheme.Ink400).FontSize(10f).Alignment(TextAlignment.MiddleLeft);

                            if (paper.IsParentHovered)
                                item.SubMenu.Render(paper, $"{id}_s_{i}", 190, 0);
                        }
                    }
                }
            }
        }
    }

    private struct ContextMenuItem
    {
        public string Icon;
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
