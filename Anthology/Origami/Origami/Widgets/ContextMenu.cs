// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

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
    /// <param name="label">Row label.</param>
    /// <param name="onClick">Invoked on click (menu closes afterwards).</param>
    /// <param name="enabled">When false the row is dimmed and non-interactive.</param>
    /// <param name="icon">Optional text/glyph icon (glyph font may be empty — prefer <paramref name="iconDraw"/>).</param>
    /// <param name="shortcut">Optional trailing keyboard hint, e.g. "Ctrl D", "F2", "Del".</param>
    /// <param name="danger">Render the row in the danger (red) colour.</param>
    /// <param name="on">Render the row in the active/accent colour (the <c>.on</c> variant).</param>
    /// <param name="iconDraw">Optional leading icon, drawn tinted to the row's current text colour.</param>
    public ContextBuilder Item(string label, Action onClick, bool enabled = true, string icon = "",
        string shortcut = "", bool danger = false, bool on = false, IOrigamiIcon? iconDraw = null)
    {
        Items.Add(new CtxItem
        {
            Label = label, OnClick = onClick, Enabled = enabled, Icon = icon,
            Shortcut = shortcut, Danger = danger, On = on, IconDraw = iconDraw,
        });
        return this;
    }

    /// <summary>Add a non-interactive uppercase section header at the top of a group.</summary>
    public ContextBuilder Header(string text)
    {
        Items.Add(new CtxHeader { Text = text });
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

    // ── Shared row geometry / palette (Nebula .w2menu spec) ───

    internal const float RowHeight = 30f;
    internal const float RowFont = 16f;   // .w2mrow font-size (Origami metric scale ~= 12px browser)
    internal const float SubFont = 13f;   // .k shortcut / header
    internal const float IconSize = 15f;  // leading vector icon
    internal const float RowPadX = 9f;    // .w2mrow padding-x
    internal const float RowGap = 9f;     // .w2mrow flex gap

    // rgba(168,85,247,0.12) — .w2mrow:hover background.

    // ── Item types ───────────────────────────────────────────

    internal sealed class CtxItem : IContextItem
    {
        public string Label = "", Icon = "", Shortcut = "";
        public Action? OnClick;
        public IOrigamiIcon? IconDraw;
        public bool Enabled = true;
        public bool Danger;
        public bool On;

        public void Draw(Paper paper, string id, int index, Scribe.FontFile font, OrigamiTheme theme, Action close)
        {
            var ink = theme.Ink;

            var row = paper.Row($"{id}_i_{index}")
                .Height(RowHeight)
                .Padding(RowPadX, RowPadX, 0, 0)
                .RowBetween(RowGap)
                .Rounded(6f)
                .Hovered.BackgroundColor(Enabled ? theme.Hover : Color.Transparent).End();

            if (Enabled)
                row.OnClick(0, (_, _) => { OnClick?.Invoke(); close(); });

            using (row.Enter())
            {
                bool hovered = Enabled && paper.IsParentHovered;
                Color txt = !Enabled ? ink.C100                     // t-dim (disabled)
                          : Danger  ? theme.Red.C500                // .danger
                          : On      ? theme.Primary.C700            // .on = acc-300
                          : hovered ? ink.C500                      // hover = t-hi
                          :           ink.C400;                     // t

                DrawLeadingIcon(paper, $"{id}_ico_{index}", font, txt);

                paper.Box($"{id}_l_{index}")
                    .Width(UnitValue.Stretch()).Height(RowHeight)
                    .Text(Label, font).TextColor(txt)
                    .FontSize(RowFont).Alignment(TextAlignment.MiddleLeft);

                if (!string.IsNullOrEmpty(Shortcut))
                    paper.Box($"{id}_k_{index}")
                        .Width(UnitValue.Auto).Height(RowHeight)
                        .Text(Shortcut, font).TextColor(Enabled ? ink.C200 : ink.C100)   // t-lo
                        .FontSize(SubFont).Alignment(TextAlignment.MiddleRight);
            }
        }

        private void DrawLeadingIcon(Paper paper, string boxId, Scribe.FontFile font, Color color)
        {
            if (IconDraw != null)
            {
                var icon = IconDraw;
                using (paper.Box(boxId).Width(IconSize).Height(RowHeight).IsNotInteractable().Enter())
                    paper.Draw((canvas, rect) =>
                    {
                        // Center a square icon cell vertically within the full-height row box.
                        float sz = IconSize;
                        float ix = (float)(rect.Min.X + (rect.Size.X - sz) * 0.5f);
                        float iy = (float)(rect.Min.Y + (rect.Size.Y - sz) * 0.5f);
                        var cell = new Prowl.Vector.Rect(ix, iy, ix + sz, iy + sz);
                        icon.Draw(canvas, cell, color);
                    });
            }
            else if (!string.IsNullOrEmpty(Icon))
            {
                paper.Box(boxId).Width(IconSize).Height(RowHeight)
                    .Text(Icon, font).TextColor(color)
                    .FontSize(RowFont).Alignment(TextAlignment.MiddleCenter);
            }
            else
            {
                paper.Box(boxId).Width(IconSize);   // reserve space so labels stay aligned
            }
        }
    }

    internal sealed class CtxHeader : IContextItem
    {
        public string Text = "";

        public void Draw(Paper paper, string id, int index, Scribe.FontFile font, OrigamiTheme theme, Action close)
        {
            using (paper.Row($"{id}_h_{index}")
                .Height(UnitValue.Auto)
                .Padding(RowPadX, RowPadX, 6, 4)
                .IsNotInteractable()
                .Enter())
            {
                paper.Box($"{id}_ht_{index}")
                    .Width(UnitValue.Stretch()).Height(14)
                    .Text(Text.ToUpperInvariant(), theme.Medium ?? font)
                    .TextColor(theme.Ink.C100)                      // t-dim
                    .FontSize(SubFont).Alignment(TextAlignment.MiddleLeft);
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

            var row = paper.Row($"{id}_i_{index}")
                .Height(RowHeight)
                .Padding(RowPadX, RowPadX, 0, 0)
                .RowBetween(RowGap)
                .Rounded(6f)
                .Hovered.BackgroundColor(Enabled ? theme.Hover : Color.Transparent).End();

            if (Enabled)
                row.OnClick(0, (_, _) => { OnClick?.Invoke(); });

            using (row.Enter())
            {
                bool hovered = Enabled && paper.IsParentHovered;
                Color txt = !Enabled ? ink.C100 : hovered ? ink.C500 : ink.C400;

                Origami.Checkbox(paper, $"{id}_t_{index}",
                        GetValue?.Invoke() ?? false, _ => { })
                    .NoLabel().ReadOnly().Show();

                paper.Box($"{id}_l_{index}")
                    .Width(UnitValue.Stretch()).Height(RowHeight)
                    .Text(Label, font).TextColor(txt)
                    .FontSize(RowFont).Alignment(TextAlignment.MiddleLeft);
            }
        }
    }

    internal sealed class CtxSeparator : IContextItem
    {
        public void Draw(Paper paper, string id, int index, Scribe.FontFile font, OrigamiTheme theme, Action close)
        {
            // .c2sep — 1px bd line, ~5px vertical margin, ~4px horizontal inset.
            paper.Box($"{id}_sep_{index}")
                .Height(1).Margin(4, 4, 5, 5)
                .BackgroundColor(Color.FromArgb(33, 178, 150, 255));   // bd
        }
    }

    internal sealed class CtxSubmenu : IContextItem
    {
        public string Label = "", Icon = "";
        public ContextBuilder? Sub;

        public void Draw(Paper paper, string id, int index, Scribe.FontFile font, OrigamiTheme theme, Action close)
        {
            var ink = theme.Ink;

            using (paper.Row($"{id}_i_{index}")
                .Height(RowHeight)
                .Padding(RowPadX, RowPadX, 0, 0)
                .RowBetween(RowGap)
                .Rounded(6f)
                .Hovered.BackgroundColor(theme.Hover).End()
                .Enter())
            {
                bool hovered = paper.IsParentHovered;
                Color txt = hovered ? ink.C500 : ink.C400;

                if (!string.IsNullOrEmpty(Icon))
                    paper.Box($"{id}_ico_{index}")
                        .Width(IconSize).Height(RowHeight)
                        .Text(Icon, font).TextColor(txt)
                        .FontSize(RowFont).Alignment(TextAlignment.MiddleCenter);
                else
                    paper.Box($"{id}_pad_{index}").Width(IconSize);

                paper.Box($"{id}_l_{index}")
                    .Width(UnitValue.Stretch()).Height(RowHeight)
                    .Text(Label, font).TextColor(txt)
                    .FontSize(RowFont).Alignment(TextAlignment.MiddleLeft);

                Color arrow = txt;
                using (paper.Box($"{id}_a_{index}").Width(12).Height(RowHeight).IsNotInteractable().Enter())
                    paper.Draw((canvas, rect) => ContextMenu.DrawChevron(canvas, rect, arrow));

                if (paper.IsParentHovered && Sub != null)
                    // Overlap the parent panel by ~2px so the submenu reads as attached, not detached.
                    // Row-relative: 200 menu width - 5 col pad - ~2 overlap (row padding is not added to
                    // self-directed children) = 193.
                    ContextMenu.RenderMenu(paper, $"{id}_s_{index}", Sub, 193f, 0, close);
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

    // Nebula .w2menu container literals.
    private const float MenuRadius = 9f;
    private const float MenuPad = 5f;
    private const float MenuWidth = 200f;

    public static bool IsOpen => _isOpen;

    /// <summary>Open a context menu at the given screen position.</summary>
    public static void Show(float x, float y, Action<ContextBuilder> build)
    {
        Close(); // close any existing
        _openedThisDraw = true; // an explicit open counts as this frame's open (dedups with RightClickMenu)
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

    /// <summary>Render a positioned menu panel at the given screen position. Used for popups and submenus.</summary>
    internal static void RenderMenu(Paper paper, string id, ContextBuilder builder, float x, float y,
        Action close, int layer = Layer.Topmost + 501)
    {
        var theme = Origami.Current;
        var font = theme.Font;
        if (font == null) return;

        var menuBox = paper.Box($"{id}_menu")
            .PositionType(PositionType.SelfDirected)
            .Position(x, y)
            .Width(MenuWidth).Height(UnitValue.Auto)
            .BackgroundColor(theme.Popover)
            .BorderColor(theme.BorderStrong).BorderWidth(1)
            .Rounded(MenuRadius)
            .Layer(layer)
            .ClampToScreen()
            .BoxShadow(0, 14, 40, 0, theme.Shadow)
            .StopEventPropagation();

        using (menuBox.Enter())
            DrawMenuBody(paper, id, builder, font, theme, close);
    }

    /// <summary>
    /// Render a static, inline <c>.w2menu</c> panel in the current layout flow (for showcases /
    /// documentation). Mirrors <see cref="Toasts.Preview"/>: normal flow, fixed width, no backdrop
    /// or positioning. Rows still hover; clicks fire their callbacks but there is nothing to close.
    /// </summary>
    public static void Preview(Paper paper, string id, Action<ContextBuilder> build)
    {
        var theme = Origami.Current;
        var font = theme.Font;
        if (font == null) return;

        var builder = new ContextBuilder();
        build(builder);

        var menuBox = paper.Box($"{id}_menu")
            .Width(MenuWidth).Height(UnitValue.Auto)
            .BackgroundColor(theme.Popover)
            .BorderColor(theme.BorderStrong).BorderWidth(1)
            .Rounded(MenuRadius)
            .BoxShadow(0, 14, 40, 0, theme.Shadow);

        using (menuBox.Enter())
            DrawMenuBody(paper, id, builder, font, theme, static () => { });
    }

    /// <summary>
    /// Render a menu panel at an absolute screen position on a caller-chosen <paramref name="layer"/>,
    /// with a caller-owned <paramref name="close"/> action. Unlike <see cref="Show"/> this adds no
    /// backdrop and no modal push, so the caller controls focus/dismissal and stacking. Used by menu
    /// bars that keep the bar itself above their own dropdown.
    /// </summary>
    public static void Panel(Paper paper, string id, float x, float y, int layer,
        Action close, Action<ContextBuilder> build)
    {
        var builder = new ContextBuilder();
        build(builder);
        RenderMenu(paper, id, builder, x, y, close, layer);
    }

    /// <summary>Draw the padded item column shared by the popup and inline previews.</summary>
    private static void DrawMenuBody(Paper paper, string id, ContextBuilder builder,
        Scribe.FontFile font, OrigamiTheme theme, Action close)
    {
        using (paper.Column($"{id}_col")
            .Padding(MenuPad, MenuPad, MenuPad, MenuPad)
            .Height(UnitValue.Auto)
            .Enter())
        {
            for (int i = 0; i < builder.Items.Count; i++)
                builder.Items[i].Draw(paper, id, i, font, theme, close);
        }
    }

    /// <summary>Draw a small trailing chevron (submenu affordance) centred in its box.</summary>
    internal static void DrawChevron(Canvas canvas, Rect rect, Color color)
    {
        float cx = (float)(rect.Min.X + rect.Size.X * 0.5);
        float cy = (float)(rect.Min.Y + rect.Size.Y * 0.5);

        canvas.SaveState();
        canvas.SetStrokeColor(color);
        canvas.SetStrokeWidth(1.5f);
        canvas.SetStrokeCap(EndCapStyle.Round);
        canvas.BeginPath();
        canvas.MoveTo(cx - 1.5f, cy - 3.2f);
        canvas.LineTo(cx + 2f, cy);
        canvas.LineTo(cx - 1.5f, cy + 3.2f);
        canvas.Stroke();
        canvas.RestoreState();
    }
}
