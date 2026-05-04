using System;
using System.Collections.Generic;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Runtime;
using Prowl.Vector;

using static System.Net.Mime.MediaTypeNames;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Widgets;

public class ContextMenuBuilder
{
    private readonly List<IContextMenuItem> _items = new();
    private Action? _onClose;

    internal void SetCloseAction(Action onClose) => _onClose = onClose;


    public ContextMenuBuilder Toggle(string label, Action onClick, Func<bool> toggleValue, bool enabled = true, bool shouldCloseOnClick = false)
    {
        _items.Add(new ContextMenuToggle { Label = label, OnClick = onClick, ToggleValue = toggleValue, IsEnabled = enabled, ShouldCloseOnClick = shouldCloseOnClick});
        return this;
    }


    public ContextMenuBuilder Item(string label, Action onClick, bool enabled = true, string icon = "", bool shouldCloseOnClick = true)
    {
        _items.Add(new ContextMenuItem { Label = label, OnClick = onClick, IsEnabled = enabled, Icon = icon, ShouldCloseOnClick = shouldCloseOnClick });
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
        _items.Add(new ContextMenuItem { Label = label, SubMenu = sub, IsEnabled = true, Icon = icon, ShouldCloseOnClick = false});
        return this;
    }

    public void Render(Paper paper, string id, float x, float y, bool isSubmenu = false, Color? backgroundColor  = null)
    {
        var font = EditorTheme.DefaultFont;
        if (font == null) return;

        using (paper.Box(id)
            .PositionType(PositionType.SelfDirected)
            .Position(x, y)
            .Width(200)
            .Height(UnitValue.Auto)
            .BackgroundColor(backgroundColor ?? EditorTheme.Purple200)
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

                    var textColor = item.IsEnabled ? EditorTheme.Ink500 : EditorTheme.Ink300;


                    item.Draw(paper, id, i, font, textColor, _onClose);
                    
                }
            }
        }
    }

    private interface IContextMenuItem
    {
        public bool ShouldCloseOnClick { get; set; }
        public bool IsEnabled { get; set; }
        void Draw(Paper paper, string id, int index, Scribe.FontFile font, Color textColor, Action onClose = null);
    }

    private struct ContextMenuItem : IContextMenuItem
    {
        public string Icon;
        public string Label;
        public Action? OnClick;
        public bool IsSeparator;

        public bool ShouldCloseOnClick { get; set; }
        public bool IsEnabled { get; set; }
        public ContextMenuBuilder? SubMenu;

        public void Draw(Paper paper, string id, int index, Scribe.FontFile font, Color textColor, Action onClose = null)
        {
            ContextMenuItem item = this;

            if (item.IsSeparator)
            {
                paper.Box($"{id}_sep_{index}")
                    .Height(1.25f).Margin(10, 5)
                    .BackgroundColor(EditorTheme.Ink200);
                return;
            }

            using (paper.Row($"{id}_i_{index}")
                        .Height(EditorTheme.RowHeight)
                        .Hovered.BackgroundColor(item.IsEnabled ? EditorTheme.Purple400 : Color.Transparent).End()
                        .Rounded(3)
                        .OnClick(item, (captured, e) =>
                        {
                            if (captured.IsEnabled)
                            {
                                captured.OnClick?.Invoke();
                                if (item.ShouldCloseOnClick)
                                    onClose?.Invoke();
                            }
                        })
                        .Enter())
            {
                if (string.IsNullOrWhiteSpace(item.Icon))
                {
                    paper.Box($"{id}_l_{index}")
                        .Width(UnitValue.Stretch())
                        .Margin(10, 0, 0, 0)
                        .Height(EditorTheme.RowHeight)
                        .Text(item.Label, font).TextColor(textColor).FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);
                }
                else
                {
                    paper.Box($"{id}_i_{index}")
                        .Margin(10, 0, 0, 0)
                        .Size(EditorTheme.RowHeight)
                        .Text(item.Icon, font).TextColor(textColor).FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

                    paper.Box($"{id}_l_{index}")
                        .Width(UnitValue.Stretch())
                        .Margin(5, 0, 0, 0)
                        .Height(EditorTheme.RowHeight)
                        .Text(item.Label, font).TextColor(textColor).FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);
                }


                if (item.SubMenu != null)
                {
                    paper.Box($"{id}_a_{index}")
                        .Size(EditorTheme.RowHeight)
                        .Text(EditorIcons.ChevronRight, font).TextColor(EditorTheme.Ink400).FontSize(10f).Alignment(TextAlignment.MiddleLeft);

                    if (paper.IsParentHovered)
                        item.SubMenu.Render(paper, $"{id}_s_{index}", 190, 0, isSubmenu: true);
                }
            }
        }
    }

    private struct ContextMenuToggle : IContextMenuItem
    {
        public string Label;
        public Action? OnClick;

        /// <summary>
        /// Gets the current value of the toggle. This is a Func instead of a simple bool to allow for dynamic values that may change outside of the menu (e.g. a "Show Grid" toggle that reflects the current visibility of the grid, which can also be toggled via a keyboard shortcut).
        /// </summary>
        public Func<bool>? ToggleValue;

        public bool ShouldCloseOnClick { get; set; }
        public bool IsEnabled { get; set; }

        public void Draw(Paper paper, string id, int index, Scribe.FontFile font, Color textColor, Action onClose = null)
        {
            ContextMenuToggle item = this;

            using (paper.Row($"{id}_i_{index}")
                        .Height(EditorTheme.RowHeight)
                        .Hovered.BackgroundColor(IsEnabled ? EditorTheme.Purple400 : Color.Transparent).End()
                        .Rounded(3)
                        .OnClick(item, (captured, e) =>
                        {
                            if (captured.IsEnabled)
                            {
                                captured.OnClick?.Invoke();
                                if (item.ShouldCloseOnClick)
                                    onClose?.Invoke();
                            }
                        })
                        .Enter())
            {

                // Read-only indicator inside menu rows — the row itself owns the click; the
                // checkbox just shows current state.
                Origami.Checkbox(paper, $"{id}_t_{index}",
                        ToggleValue != null ? ToggleValue.Invoke() : false, _ => { })
                    .NoLabel().ReadOnly().Show();


                paper.Box($"{id}_l_{index}")
                        .Width(UnitValue.Stretch())
                        .Margin(5, 0, 0, 0)
                        .Height(EditorTheme.RowHeight)
                        .Text(Label, font).TextColor(textColor).FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft);

            }
        }
    }
}

/// <summary>
/// Shows a context menu on right-click of the current parent element.
/// Only one context menu can be open per frame. Uses a fullscreen backdrop to close on outside click.
/// </summary>
public static class ContextMenuHelper
{
    // Prevent multiple menus opening on the same frame from event bubbling
    private static long _openedOnFrame = -1;

    // Track the currently open menu so we can close it
    private static Action? _closeCurrentMenu;

    public static void OpenContextMenu(Paper paper, string id, ElementHandle? parent = null)
    {
        var parentEl = parent ?? paper.CurrentParent;

        // paper.PointerPos and parentEl.Data.X/Y are both in Paper-logical space.
        var mouse = paper.PointerPos;
        var relativePosition = new Float2(
                parentEl.Data.RelativeX + (mouse.X - parentEl.Data.X),
                parentEl.Data.RelativeY + (mouse.Y - parentEl.Data.Y)
            );

        long frame = Time.FrameCount;
        if (_openedOnFrame == frame) return; // Another menu already opened this frame
        _openedOnFrame = frame;

        // Close any previously open menu
        _closeCurrentMenu?.Invoke();

        paper.SetElementStorage(parentEl, $"{id}_open", true);
        paper.SetElementStorage(parentEl, $"{id}_x", (float)relativePosition.X);
        paper.SetElementStorage(parentEl, $"{id}_y", (float)relativePosition.Y);
    }

    public static bool ContextMenu(Paper paper, string id, Action<ContextMenuBuilder> build, ElementHandle? parent = null)
    {
        var parentEl = parent ?? paper.CurrentParent;
        bool isOpen = paper.GetElementStorage(parentEl, $"{id}_open", false);
        float menuX = paper.GetElementStorage(parentEl, $"{id}_x", 0f);
        float menuY = paper.GetElementStorage(parentEl, $"{id}_y", 0f);

        if (isOpen)
        {
            Action close = () => paper.SetElementStorage(parentEl, $"{id}_open", false);
            _closeCurrentMenu = close;

            var builder = new ContextMenuBuilder();
            builder.SetCloseAction(close);
            build(builder);

            using (paper.Box($"{id}_anchor")
                .PositionType(PositionType.SelfDirected)
                .Position(menuX, menuY)
                .Width(UnitValue.Auto).Height(UnitValue.Auto)
                .StopEventPropagation()
                .Enter())
            {

                // Fullscreen backdrop click to close
                paper.Box($"{id}_backdrop")
                    .PositionType(PositionType.SelfDirected)
                    .Position(-9999, -9999)
                    .Size(99999, 99999)
                    .Layer(Layer.Topmost)
                    .StopEventPropagation()
                    .OnClick(0, (_, _) => close())
                    .OnRightClick(0, (_, _) => close());

                builder.Render(paper, $"{id}_ctx", 0, 0);
            }
        }

        return isOpen;
    }

    public static bool ClickMenu(Paper paper, string id, Action<ContextMenuBuilder> build, ElementHandle? parent = null)
    {
        var parentEl = parent ?? paper.CurrentParent;
        bool isOpen = paper.GetElementStorage(parentEl, $"{id}_open", false);
        float menuX = paper.GetElementStorage(parentEl, $"{id}_x", 0f);
        float menuY = paper.GetElementStorage(parentEl, $"{id}_y", 0f);

        // Right-click opens at cursor position only if no other menu opened this frame
        parentEl.Data.OnClick += e =>
        {
            long frame = Time.FrameCount;
            if (_openedOnFrame == frame) return; // Another menu already opened this frame
            _openedOnFrame = frame;

            // Close any previously open menu
            _closeCurrentMenu?.Invoke();

            paper.SetElementStorage(parentEl, $"{id}_open", true);
            paper.SetElementStorage(parentEl, $"{id}_x", (float)e.RelativePosition.X);
            paper.SetElementStorage(parentEl, $"{id}_y", (float)e.RelativePosition.Y);
        };

        if (isOpen)
        {
            Action close = () => paper.SetElementStorage(parentEl, $"{id}_open", false);
            _closeCurrentMenu = close;

            var builder = new ContextMenuBuilder();
            builder.SetCloseAction(close);
            build(builder);

            // Fullscreen backdrop click to close
            paper.Box($"{id}_backdrop")
                .PositionType(PositionType.SelfDirected)
                .Position(-9999, -9999)
                .Size(99999, 99999)
                .BackgroundColor(Color.FromArgb(85, 0, 0, 0))
                .Layer(Layer.Topmost)
                .StopEventPropagation()
                .OnClick(0, (_, _) => close())
                .OnRightClick(0, (_, _) => close());

            using (paper.Box($"{id}_anchor")
                .PositionType(PositionType.SelfDirected)
                .Position(menuX, menuY)
                .Width(UnitValue.Auto).Height(UnitValue.Auto)
                .StopEventPropagation()
                .Enter())
            {
                builder.Render(paper, $"{id}_ctx", 0, 0, backgroundColor: EditorTheme.Neutral200);
            }
        }

        return isOpen;
    }

    public static bool RightClickMenu(Paper paper, string id, Action<ContextMenuBuilder> build)
    {
        var parentEl = paper.CurrentParent;
        bool isOpen = paper.GetElementStorage(parentEl, $"{id}_open", false);
        float menuX = paper.GetElementStorage(parentEl, $"{id}_x", 0f);
        float menuY = paper.GetElementStorage(parentEl, $"{id}_y", 0f);

        // Right-click opens at cursor position only if no other menu opened this frame
        parentEl.Data.OnRightClick += e =>
        {
            long frame = Time.FrameCount;
            if (_openedOnFrame == frame) return; // Another menu already opened this frame
            _openedOnFrame = frame;

            // Close any previously open menu
            _closeCurrentMenu?.Invoke();

            paper.SetElementStorage(parentEl, $"{id}_open", true);
            paper.SetElementStorage(parentEl, $"{id}_x", (float)e.RelativePosition.X);
            paper.SetElementStorage(parentEl, $"{id}_y", (float)e.RelativePosition.Y);
        };

        if (isOpen)
        {
            Action close = () => paper.SetElementStorage(parentEl, $"{id}_open", false);
            _closeCurrentMenu = close;

            var builder = new ContextMenuBuilder();
            builder.SetCloseAction(close);
            build(builder);

            // Fullscreen backdrop click to close
            paper.Box($"{id}_backdrop")
                .PositionType(PositionType.SelfDirected)
                .Position(-9999, -9999)
                .Size(99999, 99999)
                .BackgroundColor(Color.FromArgb(85, 0, 0, 0))
                .Layer(Layer.Topmost)
                .StopEventPropagation()
                .OnClick(0, (_, _) => close())
                .OnRightClick(0, (_, _) => close());

            using (paper.Box($"{id}_anchor")
                .PositionType(PositionType.SelfDirected)
                .Position(menuX, menuY)
                .Width(UnitValue.Auto).Height(UnitValue.Auto)
                .StopEventPropagation()
                .Enter())
            {
                builder.Render(paper, $"{id}_ctx", 0, 0);
            }
        }

        return isOpen;
    }
}
