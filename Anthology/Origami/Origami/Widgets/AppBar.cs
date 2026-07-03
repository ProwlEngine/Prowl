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
/// Fluent builder for an Origami application bar: a glass pill holding a brand mark, tags, a
/// flexible spacer, icon action buttons and an avatar, laid out left-to-right in the order added.
/// Construct via <see cref="Origami.AppBar(Paper, string)"/>, chain items, then call <see cref="Show"/>.
/// </summary>
/// <remarks>Self-contained (Paper + Origami only). Icons are supplied as <c>Action&lt;Canvas, Rect&gt;</c>
/// draw callbacks so the bar has no dependency on any particular icon set.</remarks>
public sealed class AppBarBuilder
{
    private enum Kind { Brand, Tag, Spacer, Action, Avatar }
    private sealed class Item
    {
        public Kind Kind;
        public string Id = "";
        public string Text = "";
        public IOrigamiIcon? Icon;
        public Action? OnClick;
        public Color Color;
    }

    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private readonly List<Item> _items = new();
    private float _height = 44f;

    internal AppBarBuilder(Paper paper, string id, OrigamiTheme theme)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    /// <summary>Brand mark: an accent-tile logo (drawn from <paramref name="icon"/>) followed by a title.</summary>
    public AppBarBuilder Brand(IOrigamiIcon icon, string title)
    {
        _items.Add(new Item { Kind = Kind.Brand, Icon = icon, Text = title ?? "" });
        return this;
    }

    /// <summary>A small mono tag pill (e.g. a version like "v0.1").</summary>
    public AppBarBuilder Tag(string text)
    {
        _items.Add(new Item { Kind = Kind.Tag, Text = text ?? "" });
        return this;
    }

    /// <summary>A flexible gap that pushes following items to the right.</summary>
    public AppBarBuilder Spacer()
    {
        _items.Add(new Item { Kind = Kind.Spacer });
        return this;
    }

    /// <summary>A square icon button.</summary>
    public AppBarBuilder Action(string id, IOrigamiIcon icon, Action onClick)
    {
        _items.Add(new Item { Kind = Kind.Action, Id = id, Icon = icon, OnClick = onClick });
        return this;
    }

    /// <summary>A circular avatar showing initials (defaults to the accent colour).</summary>
    public AppBarBuilder Avatar(string id, string text, Color? color = null)
    {
        _items.Add(new Item { Kind = Kind.Avatar, Id = id, Text = text ?? "", Color = color ?? _theme.Primary.C500 });
        return this;
    }

    /// <summary>Bar height in pixels (default 44).</summary>
    public AppBarBuilder Height(float height) { _height = MathF.Max(24f, height); return this; }

    /// <summary>Render the app bar.</summary>
    public void Show()
    {
        var ink = _theme.Ink;
        var font = _theme.Font;
        var titleFont = _theme.SemiBold ?? font;
        var monoFont = _theme.Mono ?? font;
        if (font == null) return;

        const float gap = 10f;
        var acc = _theme.Primary.C500;

        using (_paper.Row(_id).Width(UnitValue.Percentage(100)).Height(_height).Rounded(9).Padding(12, 12, 0, 0)
            .BackgroundColor(_theme.Glass).BorderColor(_theme.BorderSoft).BorderWidth(1)
            .Enter())
        {
            // Gaps are explicit left margins: a Row's RowBetween is suppressed once children set an
            // explicit main-axis margin, which every item does here for vertical centering.
            bool first = true;
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                float lm = first ? 0f : gap;

                switch (it.Kind)
                {
                    case Kind.Brand:
                        var brandIcon = it.Icon;
                        using (_paper.Box($"{_id}_logo").Size(28).Rounded(8).Margin(lm, 0, UnitValue.Stretch(), UnitValue.Stretch())
                            .BackgroundColor(acc).Enter())
                            _paper.Draw((canvas, r) => brandIcon?.Draw(canvas, Center(r, 15f), Color.White));
                        _paper.Box($"{_id}_title").Width(UnitValue.Auto).Height(UnitValue.Auto)
                            .Margin(gap, 0, UnitValue.Stretch(), UnitValue.Stretch())
                            .Text(it.Text, titleFont!).FontSize(_theme.Metrics.FontSize)
                            .TextColor(ink.C500).Alignment(TextAlignment.MiddleLeft);
                        break;

                    case Kind.Tag:
                        _paper.Box($"{_id}_tag{i}").Width(UnitValue.Auto).Height(UnitValue.Auto).Rounded(5)
                            .Margin(lm, 0, UnitValue.Stretch(), UnitValue.Stretch()).Padding(6, 6, 2, 2)
                            .BackgroundColor(_theme.Glass).BorderColor(_theme.BorderSoft).BorderWidth(1)
                            .Text(it.Text, monoFont!).FontSize(_theme.Metrics.FontSizeSmall - 3.5f)
                            .TextColor(ink.C200).Alignment(TextAlignment.MiddleCenter);
                        break;

                    case Kind.Spacer:
                        _paper.Box($"{_id}_sp{i}").Width(UnitValue.Stretch()).Height(1);
                        first = true; // next item after a spacer needs no leading gap
                        continue;

                    case Kind.Action:
                        var icon = it.Icon;
                        var onClick = it.OnClick;
                        using (_paper.Box($"{_id}_a_{it.Id}").Size(27).Rounded(7)
                            .Margin(lm, 0, UnitValue.Stretch(), UnitValue.Stretch())
                            .Hovered.BackgroundColor(_theme.Hover).End()
                            .OnClick(0, (_, _) => onClick?.Invoke())
                            .Enter())
                            _paper.Draw((canvas, r) => icon?.Draw(canvas, Center(r, 16f), ink.C300));
                        break;

                    case Kind.Avatar:
                        using (_paper.Box($"{_id}_av_{it.Id}").Size(24).Rounded(12)
                            .Margin(lm, 0, UnitValue.Stretch(), UnitValue.Stretch())
                            .BackgroundColor(it.Color).Enter())
                            _paper.Box($"{_id}_av_{it.Id}_t").Width(UnitValue.Percentage(100)).Height(UnitValue.Percentage(100))
                                .IsNotInteractable()
                                .Text(it.Text, titleFont!).FontSize(_theme.Metrics.FontSizeSmall - 2f)
                                .TextColor(Color.White).Alignment(TextAlignment.MiddleCenter);
                        break;
                }

                first = false;
            }
        }
    }

    private static Rect Center(Rect r, float size)
    {
        float cx = (float)(r.Min.X + r.Size.X / 2), cy = (float)(r.Min.Y + r.Size.Y / 2);
        return new Rect(new Float2(cx - size / 2, cy - size / 2), new Float2(cx + size / 2, cy + size / 2));
    }
}
