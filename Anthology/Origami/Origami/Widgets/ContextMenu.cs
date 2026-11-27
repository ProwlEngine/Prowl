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
/// Fluent builder for an Origami context menu. Build items, then the system renders it.
/// </summary>
public sealed class ContextBuilder
{
    /// <summary>The list of menu items.</summary>
    internal List<ContextMenu.ContextItem> Items { get; } = [];

    /// <summary>Indicates whether space should be reserved before the item label.</summary>
    internal bool ReserveLeadingSpace { get; private set; }

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
        Items.Add(new ContextMenu.CtxItem(this)
        {
            Label = label,
            OnClick = onClick,
            Enabled = enabled,
            Icon = icon,
            Shortcut = shortcut,
            Danger = danger,
            On = on,
            IconDraw = iconDraw,
        });
        if (on || iconDraw is not null || !string.IsNullOrEmpty(icon))
            ReserveLeadingSpace = true;
        return this;
    }

    /// <summary>Add a non-interactive uppercase section header at the top of a group.</summary>
    public ContextBuilder Header(string text)
    {
        Items.Add(new ContextMenu.CtxHeader(this) { Text = text });
        return this;
    }

    /// <summary>Add a non-interactive title row (the subject of the menu, e.g. the clicked item's name),
    /// shown prominently in normal case with a divider beneath it.</summary>
    public ContextBuilder Title(string text, string icon = "", IOrigamiIcon? iconDraw = null)
    {
        Items.Add(new ContextMenu.CtxTitle(this) { Text = text, Icon = icon, IconDraw = iconDraw });
        if (iconDraw is not null || !string.IsNullOrEmpty(icon))
            ReserveLeadingSpace = true;
        return this;
    }

    /// <summary>Add a toggle item that shows a checkbox state.</summary>
    public ContextBuilder Toggle(string label, Action onClick, Func<bool> getValue, bool enabled = true)
    {
        Items.Add(new ContextMenu.CtxToggle(this) { Label = label, OnClick = onClick, GetValue = getValue, Enabled = enabled });
        return this;
    }

    /// <summary>Add a horizontal separator line.</summary>
    public ContextBuilder Separator()
    {
        Items.Add(new ContextMenu.CtxSeparator(this));
        return this;
    }

    /// <summary>
    /// Add a row whose content <paramref name="draw"/> builds every frame, for controls such as fields and
    /// sliders. It fills the menu width and clicking inside it leaves the menu open.
    /// </summary>
    public ContextBuilder Custom(Action<Paper> draw)
    {
        Items.Add(new ContextMenu.CtxCustom(this) { DrawContent = draw });
        return this;
    }

    /// <summary>Add a submenu that expands on hover.</summary>
    public ContextBuilder Submenu(string label, Action<ContextBuilder> build, string icon = "")
    {
        var sub = new ContextBuilder();
        build(sub);
        Items.Add(new ContextMenu.CtxSubmenu(this) { Label = label, Icon = icon, Sub = sub });
        if (!string.IsNullOrEmpty(icon))
            ReserveLeadingSpace = true;
        return this;
    }
}

/// <summary>
/// Static context menu system for Origami. Only one context menu open at a time.
/// Opens as a modal, so it sits above everything already open, other modals included.
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
    private static float MenuRadius => Origami.Current.Metrics.ContainerRounding;
    private const float MenuPad = 5f;

    public static bool IsOpen => _isOpen;

    // Keyboard navigation. The path holds the highlighted item at each open menu depth, so a submenu
    // is open by keyboard while the path runs deeper than its parent menu.
    private static readonly List<int> s_keyPath = new();
    private static string s_keyRoot = "";
    private static int s_depth;
    private static bool s_keysOn, s_keyHandled;

    /// <summary>Whether the open menu used a key press this frame.</summary>
    internal static bool KeyHandled => s_keyHandled;

    /// <summary>Drops the keyboard highlight, for when a different menu takes the place of the open one.</summary>
    internal static void ResetKeys() => s_keyPath.Clear();

    /// <summary>Whether the keyboard highlight is on this item of the menu being drawn.</summary>
    internal static bool IsKeyHighlighted(int index) =>
        s_keysOn && s_depth >= 1 && s_depth <= s_keyPath.Count && s_keyPath[s_depth - 1] == index;

    /// <summary>Whether the keyboard opened the submenu of this item of the menu being drawn.</summary>
    internal static bool IsKeyOpened(int index) => IsKeyHighlighted(index) && s_keyPath.Count > s_depth;

    private static bool IsNavigable(ContextItem item) =>
        item is CtxItem { Enabled: true } or CtxToggle { Enabled: true } or CtxSubmenu;

    /// <summary>Up and Down move the highlight, Right or Enter opens a submenu, Left closes it and Enter picks an item.</summary>
    private static void HandleKeys(Paper paper, ContextBuilder builder, int depth, Action close)
    {
        var items = builder.Items;
        if (depth < s_keyPath.Count && s_keyPath[depth] >= items.Count)
            s_keyPath.RemoveRange(depth, s_keyPath.Count - depth);
        int current = depth < s_keyPath.Count ? s_keyPath[depth] : -1;

        int step = KeyNav.Step(paper);
        if (step != 0)
        {
            int next = KeyNav.Next(current, items.Count, step, i => IsNavigable(items[i]));
            if (next < 0) return;
            if (current < 0) s_keyPath.Add(next);
            else s_keyPath[depth] = next;
            s_keyHandled = true;
            return;
        }
        if (current < 0) return;

        if (depth > 0 && paper.IsKeyPressed(PaperKey.Left))
        {
            s_keyPath.RemoveAt(depth);
            s_keyHandled = true;
            return;
        }

        bool enter = paper.IsKeyPressed(PaperKey.Enter) || paper.IsKeyPressed(PaperKey.KeypadEnter) || paper.IsKeyPressed(PaperKey.Space);
        switch (items[current])
        {
            case CtxSubmenu { Sub: not null } submenu when enter || paper.IsKeyPressed(PaperKey.Right):
                var subItems = submenu.Sub.Items;
                int first = KeyNav.Edge(subItems.Count, last: false, i => IsNavigable(subItems[i]));
                if (first < 0) break;
                s_keyPath.Add(first);
                s_keyHandled = true;
                break;
            case CtxItem item when enter:
                s_keyHandled = true;
                item.OnClick?.Invoke();
                close();
                break;
            case CtxToggle toggle when enter:
                s_keyHandled = true;
                toggle.OnClick?.Invoke();
                break;
        }
    }

    /// <summary>Open a context menu at the given screen position.</summary>
    public static void Show(float x, float y, Action<ContextBuilder> build)
    {
        Close(); // close any existing
        s_keyPath.Clear();
        _openedThisDraw = true; // an explicit open counts as this frame's open (dedups with RightClickMenu)
        _isOpen = true;
        _x = x;
        _y = y;
        _buildMenu = build;

        _modalHandle = new CustomDrawModal((paper, layer, _) => DrawMenu(paper, layer))
        {
            CloseOnBackdrop = true,
            CloseOnEscape = true,
            // A click outside or Escape removes the modal without going through Close.
            OnDismissed = _ => { _isOpen = false; _modalHandle = null; _buildMenu = null; },
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

        RenderMenu(paper, "octx", builder, new Rect(_x, _y, _x, _y), Close, layer: layer);
    }

    /// <summary>Render a positioned menu panel at the given screen position. Used for popups and submenus.</summary>
    internal static void RenderMenu(Paper paper, string id, ContextBuilder builder, Rect parentRect,
        Action close, int layer = Layer.Topmost + 501)
    {
        var theme = Origami.Current;
        var font = theme.Font;
        if (font == null) return;

        // For top-level context menus, parentRect is a zero-size rectangle: in which case just position the context
        // menu at that position. For submenus, it's the bounds of the parent menu item that opened the submenu
        // (not the parent menu!). We use this to set the clamp-to-screen avoidance area.

        // Countries with right-to-left languages expect submenus to open on the left.
        bool openOnRight = SystemInformation.SubmenuSide != SubmenuSide.Left;

        var menuBox = paper.Column($"{id}_menu")
            .PositionType(PositionType.SelfDirected)
            .Left(parentRect.Size.X == 0 ? parentRect.Max.X : (openOnRight ? UnitValue.Percentage(100, RowPadX) : UnitValue.Auto))
            .AnchorRight(parentRect.Size.X == 0 || openOnRight ? UnitValue.Auto : UnitValue.Percentage(100, RowPadX))
            .Top(parentRect.Size.Y == 0 ? parentRect.Min.Y : 0)
            .Size(UnitValue.Auto, UnitValue.Auto)
            .AlignItems(LayoutAlignment.Stretch)
            .BackgroundColor(theme.Popover)
            .BorderColor(theme.BorderStrong).BorderWidth(1)
            .Rounded(MenuRadius)
            .Padding(MenuPad, MenuPad, MenuPad, MenuPad)
            .Layer(layer)
            .ClampToScreen(parentRect)
            .DropShadow(0, 14, 40, 0, theme.Shadow)
            .StopEventPropagation();

        using (menuBox.Enter())
            DrawMenuBody(paper, id, builder, font, theme, close, layer);
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

        var menuBox = paper.Column($"{id}_menu")
            .Size(UnitValue.Auto, UnitValue.Auto)
            .AlignItems(LayoutAlignment.Stretch)
            .BackgroundColor(theme.Popover)
            .BorderColor(theme.BorderStrong).BorderWidth(1)
            .Rounded(MenuRadius)
            .Padding(MenuPad, MenuPad, MenuPad, MenuPad)
            .DropShadow(0, 14, 40, 0, theme.Shadow);

        using (menuBox.Enter())
            DrawMenuBody(paper, id, builder, font, theme, static () => { }, Layer.Topmost + 500, keys: false);
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
        RenderMenu(paper, id, builder, new Rect(x, y, x, y), close, layer);
    }

    /// <summary>Draw the padded item column shared by the popup and inline previews.</summary>
    private static void DrawMenuBody(Paper paper, string id, ContextBuilder builder,
        Scribe.FontFile font, OrigamiTheme theme, Action close, int layer, bool keys = true)
    {
        int depth = s_depth++;
        if (depth == 0)
        {
            // The context menu is modal, so a menu bar's dropdown left open beneath it gets no keys.
            s_keysOn = keys && (!_isOpen || id == "octx") && !paper.WantsCaptureKeyboard;
            if (s_keysOn)
            {
                s_keyHandled = false;
                if (s_keyRoot != id || paper.IsPointerMoving) s_keyPath.Clear();
                s_keyRoot = id;
            }
        }

        // Keys go to the deepest menu the keyboard has open.
        if (s_keysOn && !s_keyHandled && s_keyPath.Count <= depth + 1 && s_keyPath.Count >= depth)
            HandleKeys(paper, builder, depth, close);

        try
        {
            for (int i = 0; i < builder.Items.Count; i++)
                builder.Items[i].Draw(paper, id, i, font, theme, close, layer);
        }
        finally
        {
            s_depth--;
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

    // ── Shared row geometry / palette (Nebula .w2menu spec) ───

    // Row height / fonts track the theme metrics so the menu follows RowHeight / FontSize changes.
    private static float RowHeight => Origami.Current.Metrics.RowHeight + 6f;
    private static float RowFontSize => Origami.Current.Metrics.FontSize;
    private static float SubFontSize => Origami.Current.Metrics.FontSize - 3f;
    private const float IconSize = 15f;  // leading vector icon
    private const float RowPadX = 9f;    // .w2mrow padding-x
    private const float RowGap = 9f;     // .w2mrow flex gap
    private const float LabelMargin = 8f;       // post-label margin, always present
    private const float AffordanceMargin = 12f; // pre-chevron/shortcut/toggle margin, not always present

    // rgba(168,85,247,0.12) — .w2mrow:hover background.

    // ── Item types ───────────────────────────────────────────

    /// <summary>
    /// A single item in an Origami context menu.
    /// </summary>
    internal abstract class ContextItem(ContextBuilder builder)
    {
        public abstract void Draw(Paper paper, string id, int index, Scribe.FontFile font, OrigamiTheme theme, Action close, int layer);

        /// <summary>Draws the leading checkmark or icon.</summary>
        protected void DrawLeadingIcon(Paper paper, int index, Scribe.FontFile font, OrigamiTheme theme, Color color, bool showCheckmark = false, IOrigamiIcon? iconDraw = null, string? iconSymbol = null)
        {
            // If an icon and a checkmark are both set, the checkmark wins.
            if (showCheckmark)
            {
                // Active/checked items (e.g. the current "Sort By") show a leading green check.
                paper.Box("chk", index)
                    .Size(IconSize, RowHeight)
                    .IsNotInteractable()
                    .Visible(true)
                    .Icon(paper, Origami.Current.Icons.Check, theme.Green.C500, size: 18f);
            }
            else if (iconDraw != null)
            {
                using (paper.Box("ico", index)
                    .Size(IconSize, RowHeight)
                    .IsNotInteractable().Enter())
                    paper.Draw((canvas, rect) =>
                    {
                        // Center a square icon cell vertically within the full-height row box.
                        float sz = IconSize;
                        float ix = (float)(rect.Min.X + (rect.Size.X - sz) * 0.5f);
                        float iy = (float)(rect.Min.Y + (rect.Size.Y - sz) * 0.5f);
                        var cell = new Rect(ix, iy, ix + sz, iy + sz);
                        iconDraw.Draw(canvas, cell, color);
                    });
            }
            else if (!string.IsNullOrEmpty(iconSymbol))
            {
                paper.Box("ico", index)
                    .Size(IconSize, RowHeight)
                    .Text(iconSymbol, font).TextColor(color)
                    .FontSize(RowFontSize).Alignment(TextAlignment.MiddleCenter);
            }
            else
            {
                // No icon or checkmark.
                ReserveSpaceIfNecessary(paper, index);
            }
        }

        /// <summary>Reserve space if *any* of the menu items have a checkmark or an icon.</summary>
        protected void ReserveSpaceIfNecessary(Paper paper, int index)
        {
            if (builder.ReserveLeadingSpace)
                paper.Box("ico", index).Size(IconSize, RowHeight);
        }

        /// <summary>Draws the label portion of the menu item.</summary>
        protected ElementBuilder DrawLabel(Paper paper, int index, Scribe.FontFile font, string label, Color color, float size)
        {
            return paper.Box("l", index)
                .Size(UnitValue.Auto, RowHeight)
                .Margin(0, right: LabelMargin, 0, 0)
                .Text(label, font).TextColor(color)
                .FontSize(size).Alignment(TextAlignment.MiddleLeft);
        }
    }

    internal sealed class CtxItem(ContextBuilder builder) : ContextItem(builder)
    {
        public string Label = "", Icon = "", Shortcut = "";
        public Action? OnClick;
        public IOrigamiIcon? IconDraw;
        public bool Enabled = true;
        public bool Danger;
        public bool On;

        public override void Draw(Paper paper, string id, int index, Scribe.FontFile font, OrigamiTheme theme, Action close, int layer)
        {
            var ink = theme.Ink;

            bool keyOn = Enabled && IsKeyHighlighted(index);
            var row = paper.Row($"{id}_i_{index}")
                .Size(UnitValue.Auto, RowHeight)
                .Padding(RowPadX, RowPadX, 0, 0)
                .Gap(RowGap)
                .Rounded(theme.Metrics.Rounding)
                .BackgroundColor(keyOn ? theme.Hover : Color.Transparent)
                .Hovered.BackgroundColor(Enabled ? theme.Hover : Color.Transparent).End();

            if (Enabled)
                row.OnClick(0, (_, _) => { OnClick?.Invoke(); close(); });

            using (row.Enter())
            {
                bool hovered = Enabled && (paper.IsParentHovered || keyOn);
                Color txt = !Enabled ? ink.C100           // t-dim (disabled)
                          : Danger ? theme.Red.C500       // .danger
                          : hovered ? ink.C500            // hover = t-hi
                          : ink.C400;                     // t

                // Draw the icon/checkmark, if any.
                DrawLeadingIcon(paper, index, font, theme, txt, showCheckmark: On, IconDraw, Icon);

                // Draw the label.
                DrawLabel(paper, index, font, Label, txt, size: RowFontSize);

                // Draw the shortcut, if any.
                if (!string.IsNullOrEmpty(Shortcut))
                    paper.Box("k", index)
                        .Size(UnitValue.Auto, RowHeight)
                        .Margin(left: UnitValue.StretchOne + AffordanceMargin, 0, 0, 0)
                        .Text(Shortcut, font).TextColor(Enabled ? ink.C200 : ink.C100)   // t-lo
                        .FontSize(SubFontSize).Alignment(TextAlignment.MiddleRight);
            }
        }
    }

    internal sealed class CtxTitle(ContextBuilder builder) : ContextItem(builder)
    {
        public string Text = "";
        public string Icon = "";
        public IOrigamiIcon? IconDraw;

        public override void Draw(Paper paper, string id, int index, Scribe.FontFile font, OrigamiTheme theme, Action close, int layer)
        {
            using (paper.Row($"{id}_ti_{index}").Size(UnitValue.Auto, RowHeight).Padding(RowPadX, RowPadX, 0, 0).Gap(RowGap)
                .IsNotInteractable().Enter())
            {
                // Draw the icon, if any.
                DrawLeadingIcon(paper, index, font, theme, color: theme.Primary.C700, iconDraw: IconDraw, iconSymbol: Icon);

                // Draw the label.
                DrawLabel(paper, index, font: theme.SemiBold ?? theme.Bold ?? font, label: Text, color: theme.Ink.C500, size: RowFontSize - 1);
            }
            paper.Box($"{id}_tid_{index}").Height(1).Margin(4, 4, 3, 4)
                .BackgroundColor(theme.BorderSoft).IsNotInteractable();
        }
    }

    internal sealed class CtxHeader(ContextBuilder builder) : ContextItem(builder)
    {
        public string Text = "";

        public override void Draw(Paper paper, string id, int index, Scribe.FontFile font, OrigamiTheme theme, Action close, int layer)
        {
            using (paper.Row($"{id}_h_{index}")
                .Size(UnitValue.Auto, UnitValue.Auto)
                .Padding(RowPadX, RowPadX, 6, 4)
                .IsNotInteractable()
                .Enter())
            {
                // Draw the label.
                var label = DrawLabel(paper, index, font: theme.SemiBold ?? theme.Bold ?? font,
                    label: Text.ToUpperInvariant(), color: theme.Ink.C100 /* t-dim */, size: SubFontSize);
                label.Height(14);
                label.LetterSpacing(0.5f);
            }
        }
    }

    internal sealed class CtxToggle(ContextBuilder builder) : ContextItem(builder)
    {
        public string Label = "";
        public Action? OnClick;
        public Func<bool>? GetValue;
        public bool Enabled = true;

        public override void Draw(Paper paper, string id, int index, Scribe.FontFile font, OrigamiTheme theme, Action close, int layer)
        {
            var ink = theme.Ink;

            bool keyOn = Enabled && IsKeyHighlighted(index);
            var row = paper.Row($"{id}_i_{index}")
                .Size(UnitValue.Auto, RowHeight)
                .Padding(RowPadX, RowPadX, 0, 0)
                .Gap(RowGap)
                .Rounded(theme.Metrics.Rounding)
                .BackgroundColor(keyOn ? theme.Hover : Color.Transparent)
                .Hovered.BackgroundColor(Enabled ? theme.Hover : Color.Transparent).End();

            if (Enabled)
                row.OnClick(0, (_, _) => { OnClick?.Invoke(); });

            using (row.Enter())
            {
                bool hovered = Enabled && (paper.IsParentHovered || keyOn);
                Color txt = !Enabled ? ink.C100 : hovered ? ink.C500 : ink.C400;
                bool on = GetValue?.Invoke() ?? false;

                // Reserve space if any menu items have a checkmark or icon.
                ReserveSpaceIfNecessary(paper, index);

                // Draw the label.
                DrawLabel(paper, index, font, Label, txt, RowFontSize);

                // Pill toggle on the right (matches the Nebula .tg switch).
                var acc = theme.Primary.C500;
                using (paper.Box("tg", index)
                    .Size(32, 18).Rounded(9)
                    .Margin(left: UnitValue.StretchOne + AffordanceMargin, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                    .BackgroundColor(on ? acc : Color.FromArgb(26, 255, 255, 255))
                    .BorderColor(on ? Color.Transparent : theme.BorderSoft).BorderWidth(1)
                    .IsNotInteractable()
                    .Enter())
                    paper.Box("tk", index).Width(14).Height(14).Rounded(7)
                        .PositionType(PositionType.SelfDirected).Position(on ? 16 : 2, 1.5f)
                        .BackgroundColor(Color.White).IsNotInteractable();
            }
        }
    }

    internal sealed class CtxCustom(ContextBuilder builder) : ContextItem(builder)
    {
        public Action<Paper>? DrawContent;

        public override void Draw(Paper paper, string id, int index, Scribe.FontFile font, OrigamiTheme theme, Action close, int layer)
        {
            using (paper.Column($"{id}_cu_{index}")
                .Size(UnitValue.Auto, UnitValue.Auto)
                .Padding(RowPadX, RowPadX, 2, 2).Gap(theme.Metrics.Spacing).Enter())
                DrawContent?.Invoke(paper);
        }
    }

    internal sealed class CtxSeparator(ContextBuilder builder) : ContextItem(builder)
    {
        public override void Draw(Paper paper, string id, int index, Scribe.FontFile font, OrigamiTheme theme, Action close, int layer)
        {
            // .c2sep — 1px bd line, ~5px vertical margin, ~4px horizontal inset.
            paper.Box($"{id}_sep_{index}")
                .Height(1).Margin(4, 4, 5, 5)
                .BackgroundColor(theme.Neutral.C200);   // bd
        }
    }

    internal sealed class CtxSubmenu(ContextBuilder builder) : ContextItem(builder)
    {
        public string Label = "", Icon = "";
        public ContextBuilder? Sub;

        private struct OpenSubmenuState
        {
            public int Index;
            public float RemainingTime;
        }

        public override void Draw(Paper paper, string id, int index, Scribe.FontFile font, OrigamiTheme theme, Action close, int layer)
        {
            var ink = theme.Ink;

            // Check if the submenu should remain open for a short time, even if not hovering.
            var submenuState = paper.GetElementStorage(paper.CurrentParent, $"{id}_submenu", new OpenSubmenuState { Index = -1 });
            bool submenuIsOpen = submenuState.Index == index && submenuState.RemainingTime >= 0 || IsKeyOpened(index);
            bool keyOn = IsKeyHighlighted(index);
            ElementHandle rowHandle = default;

            using (paper.Row($"{id}_i_{index}")
                .Size(UnitValue.Auto, RowHeight)
                .Padding(RowPadX, RowPadX, 0, 0)
                .Gap(RowGap)
                .Rounded(theme.Metrics.Rounding)
                .BackgroundColor(submenuIsOpen || keyOn ? theme.Hover : Color.Transparent)
                .Hovered.BackgroundColor(theme.Hover).End()
                // Remember this row's on-screen rect so next frame the submenu can be positioned correctly.
                .OnPostLayout((h, r) => paper.SetElementStorage(h, "srect", r))
                // A press opens the submenu straight away, without waiting out the hover delay.
                .OnPress(_ => paper.SetElementStorage(rowHandle, "hover_time", SystemInformation.MenuShowDelay))
                .Enter())
            {
                rowHandle = paper.CurrentParent;
                bool hovered = paper.IsParentHovered;
                Color txt = hovered || submenuIsOpen || keyOn ? ink.C500 : ink.C400;

                // Draw the icon, if any.
                DrawLeadingIcon(paper, index, font, theme, color: theme.Primary.C700, iconSymbol: Icon);

                // Draw the label.
                DrawLabel(paper, index, font, Label, txt, RowFontSize);

                // Draw the chevron (submenu affordance).
                using (paper.Box("a", index)
                    .Size(12, RowHeight)
                    .Margin(left: UnitValue.StretchOne + AffordanceMargin, 0, 0, 0)
                    .IsNotInteractable()
                    .Enter())
                    paper.Draw((canvas, rect) => DrawChevron(canvas, rect, txt));

                // Accumulate a hover time. The submenu can only open after the threshold has been reached.
                var hoverTime = hovered ? paper.GetElementStorage(paper.CurrentParent, "hover_time", 0f) + paper.DeltaTime : 0f;
                paper.SetElementStorage(paper.CurrentParent, "hover_time", hoverTime);
                if (hoverTime >= SystemInformation.MenuShowDelay)
                    submenuIsOpen = true;

                if (submenuIsOpen && Sub != null)
                {
                    // Use the menu item's bounds (captured last frame) to position the submenu.
                    var parentRect = paper.GetElementStorage<Rect>(paper.CurrentParent, "srect", default);
                    RenderMenu(paper, $"{id}_s_{index}", Sub, parentRect, close, layer + 1);

                    // If the mouse is actually hovering over the context menu (or the submenu), continually
                    // refresh the remaining time so the submenu doesn't close.
                    if (hovered)
                        submenuState = new OpenSubmenuState { Index = index, RemainingTime = SystemInformation.MenuShowDelay + paper.DeltaTime };
                }
            }

            // If the submenu is open, decrease the remaining time (but note, it is refreshed above).
            // When it reaches zero, the submenu will close.
            if (submenuIsOpen)
            {
                submenuState.RemainingTime -= paper.DeltaTime;
                paper.SetElementStorage(paper.CurrentParent, $"{id}_submenu", submenuState);
            }
        }
    }
}
