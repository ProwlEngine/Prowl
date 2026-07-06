// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>
/// Fluent builder for an Origami menu bar: a horizontal strip of top-level menu buttons
/// (File, Edit, …), each opening a dropdown built with the <see cref="ContextBuilder"/> API
/// (so items, submenus, icons, shortcuts, separators and toggles all work). Construct via
/// <see cref="Origami.MenuBar(Paper, string)"/>, add menus with <see cref="Menu"/>, then call
/// <see cref="Show"/>.
/// </summary>
/// <remarks>
/// <para>Interaction: a CLICK opens a menu; while one is open, hovering another button switches to
/// it; clicking the open button toggles it closed; clicking anywhere else closes it. The bar owns
/// its own full-screen click-catcher, so click-away works regardless of what the host draws around
/// it. State is kept per-instance in element storage, so multiple menu bars can coexist.</para>
/// <para>Self-contained: depends only on Paper + Origami primitives, so it works in any Paper app,
/// not just the editor sample.</para>
/// </remarks>
public sealed class MenuBarBuilder
{
    // Per-instance state, parked in element storage so callers hold no state of their own.
    private sealed class State
    {
        public int Open = -1;
        public readonly float[] AnchorX = new float[MaxMenus];
        public readonly float[] AnchorY = new float[MaxMenus];
    }

    private const int MaxMenus = 32;

    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private readonly List<(string Label, Action<ContextBuilder> Build)> _menus = new();
    private float _height = 32f;
    private bool _background = true;

    internal MenuBarBuilder(Paper paper, string id, OrigamiTheme theme)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    /// <summary>Add a top-level menu. <paramref name="build"/> populates its dropdown via the
    /// <see cref="ContextBuilder"/> API (items, submenus, separators, shortcuts, toggles).</summary>
    public MenuBarBuilder Menu(string label, Action<ContextBuilder> build)
    {
        if (_menus.Count < MaxMenus) _menus.Add((label ?? string.Empty, build));
        return this;
    }

    /// <summary>Bar height in pixels (default 32).</summary>
    public MenuBarBuilder Height(float height) { _height = MathF.Max(20f, height); return this; }

    /// <summary>Draw the bar as floating text with no glass fill or border (default keeps the fill).</summary>
    public MenuBarBuilder Transparent(bool transparent = true) { _background = !transparent; return this; }

    /// <summary>Render the menu bar.</summary>
    public void Show()
    {
        var ink = _theme.Ink;
        var font = _theme.Medium ?? _theme.Font;
        if (font == null || _menus.Count == 0) return;

        // State lives on the parent element, keyed by this bar's id (so sibling bars don't collide).
        var parentH = _paper.CurrentParent;
        var state = _paper.GetElementStorage<State>(parentH, $"{_id}_st", null!) ?? new State();
        _paper.SetElementStorage(parentH, $"{_id}_st", state);

        bool open = state.Open >= 0 && state.Open < _menus.Count;

        // When open, the bar rides above a transparent click-catcher so the bar stays hoverable
        // (to switch menus) while a click anywhere else drops focus.
        Color barFill = _background ? _theme.Glass : Color.FromArgb(0, 0, 0, 0);
        Color barBorder = _background ? _theme.BorderSoft : Color.FromArgb(0, 0, 0, 0);
        using (_paper.Row(_id).Width(UnitValue.Auto).Height(_height).Rounded(8).Padding(5, 5, 0, 0)
            .Layer(open ? Layer.Topmost + 2 : Layer.Base)
            .BackgroundColor(barFill).BorderColor(barBorder).BorderWidth(_background ? 1 : 0)
            .Enter())
        {
            for (int i = 0; i < _menus.Count; i++)
                DrawItem(i, parentH, state, font, ink);
        }

        if (open)
        {
            _paper.Box($"{_id}_bd").PositionType(PositionType.SelfDirected)
                .Position(-99999, -99999).Size(199999, 199999)
                .Layer(Layer.Topmost).BackgroundColor(Color.FromArgb(0, 0, 0, 0))
                .StopEventPropagation().OnClick(0, (_, _) => state.Open = -1);

            ContextMenu.Panel(_paper, $"{_id}_drop", state.AnchorX[state.Open], state.AnchorY[state.Open],
                Layer.Topmost + 3, () => state.Open = -1, _menus[state.Open].Build);
        }
    }

    private void DrawItem(int index, ElementHandle parentH, State state, Scribe.FontFile font, OrigamiRamp ink)
    {
        bool active = state.Open == index;

        var box = _paper.Box($"{_id}_i{index}")
            .Width(UnitValue.Auto).Height(UnitValue.Auto).Rounded(6).Padding(10, 10, 4, 4)
            .Margin(0, 0, UnitValue.Stretch(), UnitValue.Stretch())
            .BackgroundColor(active ? _theme.Hover : Color.FromArgb(0, 0, 0, 0))
            .Hovered.BackgroundColor(_theme.Hover).End()
            .OnClick(index, (idx, _) => state.Open = state.Open == idx ? -1 : idx)
            // Record this item's drop anchor every frame (post-layout, so it's always the real
            // position, relative to the bar's parent where the dropdown is rendered). Switching by
            // hover then lands the drop correctly with no build-vs-event timing gap.
            .OnPostLayout((_, rect) =>
            {
                var pr = parentH.Data.LayoutRect;
                state.AnchorX[index] = (float)(rect.Min.X - pr.Min.X);
                state.AnchorY[index] = (float)(rect.Max.Y - pr.Min.Y) + 4f;
            });

        using (box.Enter())
        {
            // Click opens; hovering only SWITCHES while a menu is already open (never opens from idle).
            if (state.Open >= 0 && state.Open != index && _paper.IsParentHovered)
                state.Open = index;

            _paper.Box($"{_id}_t{index}").Width(UnitValue.Auto).Height(UnitValue.Auto).IsNotInteractable()
                .Text(_menus[index].Label, font)
                .TextColor(active ? ink.C500 : ink.C300)
                .FontSize(_theme.Metrics.FontSize).Alignment(TextAlignment.MiddleCenter);
        }
    }
}
