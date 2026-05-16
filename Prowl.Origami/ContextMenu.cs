// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// A single item in an Origami context menu.
/// </summary>
internal interface IContextItem
{
    void Draw(Paper paper, string id, int index, Scribe.FontFile font, OrigamiTheme theme, Action close);
}

/// <summary>
/// Fluent builder for an Origami context menu. Build items, then the system renders it.
/// </summary>
public sealed class ContextBuilder
{
    internal readonly List<IContextItem> Items = [];
    internal Action? CloseAction;

    /// <summary>Add a clickable menu item.</summary>
    public ContextBuilder Item(string label, Action onClick, bool enabled = true, string icon = "")
    {
        Items.Add(new CtxItem { Label = label, OnClick = onClick, Enabled = enabled, Icon = icon });
        return this;
    }

    /// <summary>Add a toggle item that shows a checkbox state.</summary>
    public ContextBuilder Toggle(string label, Action onClick, Func<bool> getValue, bool enabled = true)
    {
        Items.Add(new CtxToggle { Label = label, OnClick = onClick, GetValue = getValue, Enabled = enabled });
        return this;
    }

    /// <summary>Add a horizontal separator line.</summary>
    public ContextBuilder Separator()
    {
        Items.Add(new CtxSeparator());
        return this;
    }

    /// <summary>Add a submenu that expands on hover.</summary>
    public ContextBuilder Submenu(string label, Action<ContextBuilder> build, string icon = "")
    {
        var sub = new ContextBuilder();
        build(sub);
        Items.Add(new CtxSubmenu { Label = label, Icon = icon, Sub = sub });
        return this;
    }

    // ── Item types ───────────────────────────────────────────

    internal sealed class CtxItem : IContextItem
    {
        public string Label = "", Icon = "";
        public Action? OnClick;
        public bool Enabled = true;

        public void Draw(Paper paper, string id, int index, Scribe.FontFile font, OrigamiTheme theme, Action close)
        {
            var ink = theme.Ink;
            var m = theme.Metrics;
            var textColor = Enabled ? ink.C500 : ink.C300;
            float rowH = m.RowHeight;

            var row = paper.Row($"{id}_i_{index}")
                .Height(rowH)
                .Hovered.BackgroundColor(Enabled ? theme.Primary.C400 : Color.Transparent).End()
                .Rounded(m.SmallRounding);

            if (Enabled)
                row.OnClick(0, (_, _) => { OnClick?.Invoke(); close(); });

            using (row.Enter())
            {
                if (!string.IsNullOrEmpty(Icon))
                {
                    paper.Box($"{id}_ico_{index}")
                        .Width(m.RowHeight).Height(rowH).ChildLeft(m.SpacingLarge)
                        .Text(Icon, font).TextColor(textColor)
                        .FontSize(m.FontSize - 1).Alignment(TextAlignment.MiddleCenter);
                }
                else
                {
                    paper.Box($"{id}_pad_{index}").Width(m.SpacingLarge);
                }

                paper.Box($"{id}_l_{index}")
                    .Width(UnitValue.Stretch()).Height(rowH)
                    .Text(Label, font).TextColor(textColor)
                    .FontSize(m.FontSize).Alignment(TextAlignment.MiddleLeft);
            }
        }
    }

    internal sealed class CtxToggle : IContextItem
    {
        public string Label = "";
        public Action? OnClick;
        public Func<bool>? GetValue;
        public bool Enabled = true;

        public void Draw(Paper paper, string id, int index, Scribe.FontFile font, OrigamiTheme theme, Action close)
        {
            var ink = theme.Ink;
            var m = theme.Metrics;
            var textColor = Enabled ? ink.C500 : ink.C300;
            float rowH = m.RowHeight;

            var row = paper.Row($"{id}_i_{index}")
                .Height(rowH)
                .Hovered.BackgroundColor(Enabled ? theme.Primary.C400 : Color.Transparent).End()
                .Rounded(m.SmallRounding);

            if (Enabled)
                row.OnClick(0, (_, _) => { OnClick?.Invoke(); });

            using (row.Enter())
            {
                Origami.Checkbox(paper, $"{id}_t_{index}",
                        GetValue?.Invoke() ?? false, _ => { })
                    .NoLabel().ReadOnly().Show();

                paper.Box($"{id}_l_{index}")
                    .Width(UnitValue.Stretch()).Height(rowH)
                    .ChildLeft(m.Spacing)
                    .Text(Label, font).TextColor(textColor)
                    .FontSize(m.FontSize).Alignment(TextAlignment.MiddleLeft);
            }
        }
    }

    internal sealed class CtxSeparator : IContextItem
    {
        public void Draw(Paper paper, string id, int index, Scribe.FontFile font, OrigamiTheme theme, Action close)
        {
            var m = theme.Metrics;
            paper.Box($"{id}_sep_{index}")
                .Height(1).Margin(m.PaddingLarge, m.Spacing, m.PaddingLarge, m.Spacing)
                .BackgroundColor(theme.Ink.C200);
        }
    }

    internal sealed class CtxSubmenu : IContextItem
    {
        public string Label = "", Icon = "";
        public ContextBuilder? Sub;

        public void Draw(Paper paper, string id, int index, Scribe.FontFile font, OrigamiTheme theme, Action close)
        {
            var ink = theme.Ink;
            var m = theme.Metrics;
            float rowH = m.RowHeight;

            using (paper.Row($"{id}_i_{index}")
                .Height(rowH)
                .Hovered.BackgroundColor(theme.Primary.C400).End()
                .Rounded(m.SmallRounding).Enter())
            {
                if (!string.IsNullOrEmpty(Icon))
                {
                    paper.Box($"{id}_ico_{index}")
                        .Width(m.RowHeight).Height(rowH).ChildLeft(m.SpacingLarge)
                        .Text(Icon, font).TextColor(ink.C500)
                        .FontSize(m.FontSize - 1).Alignment(TextAlignment.MiddleCenter);
                }
                else
                {
                    paper.Box($"{id}_pad_{index}").Width(m.SpacingLarge);
                }

                paper.Box($"{id}_l_{index}")
                    .Width(UnitValue.Stretch()).Height(rowH)
                    .Text(Label, font).TextColor(ink.C500)
                    .FontSize(m.FontSize).Alignment(TextAlignment.MiddleLeft);

                paper.Box($"{id}_a_{index}")
                    .Width(m.CompactHeight).Height(rowH)
                    .Text(theme.Icons.ChevronRight, font).TextColor(ink.C400)
                    .FontSize(m.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);

                if (paper.IsParentHovered && Sub != null)
                    ContextMenu.RenderMenu(paper, $"{id}_s_{index}", Sub, 192, 0, close);
            }
        }
    }
}

/// <summary>
/// Static context menu system for Origami. Only one context menu open at a time.
/// Renders on Layer.Topmost + 500 so it sits above most UI but below modals.
///
/// Use <see cref="RightClickMenu"/> inside any element's scope to attach a right-click menu.
/// Use <see cref="Show"/> to open programmatically at a position.
/// </summary>
public static class ContextMenu
{
    private static bool _isOpen;
    private static float _x, _y;
    private static Action<ContextBuilder>? _buildMenu;
    private static bool _openedThisDraw;
    private static IModal? _modalHandle;

    public static bool IsOpen => _isOpen;

    /// <summary>Open a context menu at the given screen position.</summary>
    public static void Show(float x, float y, Action<ContextBuilder> build)
    {
        Close(); // close any existing
        _isOpen = true;
        _x = x;
        _y = y;
        _buildMenu = build;

        _modalHandle = new CustomDrawModal((paper, layer, _) => DrawMenu(paper, layer))
        {
            CloseOnBackdrop = true,
            CloseOnEscape = true,
        };
        Modal.Push(_modalHandle);
    }

    /// <summary>Close the current context menu.</summary>
    public static void Close()
    {
        if (_modalHandle != null)
        {
            Modal.Remove(_modalHandle);
            _modalHandle = null;
        }
        _isOpen = false;
        _buildMenu = null;
    }

    /// <summary>
    /// Attach a right-click context menu to the current parent element.
    /// Call this inside an element's Enter() scope.
    /// </summary>
    /// <summary>
    /// Attach a right-click context menu to the current parent element.
    /// Call this inside an element's Enter() scope.
    /// </summary>
    public static void RightClickMenu(Paper paper, string id, Action<ContextBuilder> build)
    {
        paper.CurrentParent.Data.OnRightClick += e =>
        {
            if (_openedThisDraw) return;
            _openedThisDraw = true;
            Show((float)paper.PointerPos.X, (float)paper.PointerPos.Y, build);
        };
    }

    /// <summary>Call once per frame to reset dedup state. The actual rendering is done by the modal system.</summary>
    public static void Tick()
    {
        _openedThisDraw = false;
    }

    /// <summary>Called by the modal system via CustomDrawModal.</summary>
    private static void DrawMenu(Paper paper, int layer)
    {
        if (!_isOpen || _buildMenu == null) return;

        var builder = new ContextBuilder();
        _buildMenu(builder);

        RenderMenu(paper, "octx", builder, _x, _y, Close, layer: layer);
    }

    /// <summary>Render a menu panel at the given position. Used internally and for submenus.</summary>
    internal static void RenderMenu(Paper paper, string id, ContextBuilder builder, float x, float y,
        Action close, int layer = Layer.Topmost + 501)
    {
        var theme = Origami.Current;
        var m = theme.Metrics;
        var font = theme.Font;
        if (font == null) return;

        var menuBox = paper.Box($"{id}_menu")
            .PositionType(PositionType.SelfDirected)
            .Position(x, y)
            .Width(200).Height(UnitValue.Auto)
            .BackgroundColor(theme.Neutral.C200)
            .BorderColor(theme.Ink.C200).BorderWidth(1)
            .Rounded(m.ContainerRounding)
            .Layer(layer)
            .ClampToScreen()
            .BoxShadow(0, 2, 16, -4, Color.FromArgb(120, 0, 0, 0))
            .StopEventPropagation();

        using (menuBox.Enter())
        {
            using (paper.Column($"{id}_col").Margin(m.PaddingSmall, m.PaddingSmall, m.PaddingSmall, m.PaddingSmall).Height(UnitValue.Auto).Enter())
            {
                for (int i = 0; i < builder.Items.Count; i++)
                    builder.Items[i].Draw(paper, id, i, font, theme, close);
            }
        }
    }
}
