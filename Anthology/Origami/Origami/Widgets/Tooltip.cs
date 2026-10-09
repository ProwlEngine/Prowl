// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

/// <summary>The side of the anchor on which the tooltip prefers to appear.</summary>
public enum TooltipPlacement
{
    Top,
    Bottom,
    Left,
    Right
}

/// <summary>Whether the tooltip follows the cursor or sits beside its hovered element.</summary>
public enum TooltipAnchor
{
    Cursor,
    Element
}

/// <summary>Tooltip presentation. Tooltips flip to the opposite side when needed and stay on screen.</summary>
public sealed class TooltipOptions
{
    public TooltipPlacement Placement { get; set; } = TooltipPlacement.Bottom;
    public TooltipAnchor Anchor { get; set; } = TooltipAnchor.Cursor;
    public bool ShowArrow { get; set; }

    /// <summary>Hover delay in seconds, or null to use TooltipSystem.ShowDelay.</summary>
    public float? Delay { get; set; }
}

/// <summary>
/// Describes a tooltip's content. Supports plain text, title + description,
/// icon, shortcut hint, and fully custom draw callbacks.
/// </summary>
public sealed class TooltipContent
{
    public string Text = "";
    public string? Title;
    public string? Icon;
    public string? Shortcut;
    public Action<Paper>? CustomDraw;
    public float MaxWidth = 200f;
    /// <summary>The narrowest the bubble gets, for custom content that needs room the title does not give it.</summary>
    public float MinWidth;
    public TooltipOptions? Options;

    public TooltipContent() { }
    public TooltipContent(string text) => Text = text;
}

/// <summary>
/// Static tooltip system for Origami. One tooltip visible at a time.
/// Hover delay, smart positioning, rich content support.
/// Call <see cref="Draw"/> once per frame at the end of your UI pass.
/// </summary>
public static class TooltipSystem
{
    private static TooltipContent? _pending;
    private static int _activeElementId;
    private static float _hoverTime;
    private static float _showDelay = 0.5f;
    private static float _lastDeltaTime;

    public static float ShowDelay { get => _showDelay; set => _showDelay = MathF.Max(0f, value); }

    // The claim made this frame. An element and its parent can both be hovered and both have a
    // tooltip, and the deeper one is the one the pointer is really on, so it wins.
    private static TooltipContent? _claim;
    private static int _claimId;
    private static int _claimDepth = -1;
    private static Paper? _claimOwner;
    private static Paper? _anchorOwner;

    public static void Hover(int elementId, TooltipContent content) => Hover(elementId, content, 0);

    /// <summary>Asks to show a tooltip for an element this frame. A deeper element outranks a shallower one.</summary>
    public static void Hover(int elementId, TooltipContent content, int depth)
    {
        if (depth < _claimDepth)
            return;

        _claim = content;
        _claimId = elementId;
        _claimDepth = depth;
        _claimOwner = null;
    }

    public static void Hover(int elementId, string text)
        => Hover(elementId, new TooltipContent(text));

    /// <summary>Asks to show a tooltip for an element, ranked by how deep it sits in the tree.</summary>
    public static void Hover(ElementHandle element, TooltipContent content)
    {
        int depth = Depth(element);
        if (depth < _claimDepth)
            return;

        Hover(element.Data.ID, content, depth);
        _claimOwner = element.Owner;
    }

    public static void Hover(ElementHandle element, string text)
        => Hover(element, new TooltipContent(text));

    internal static int Depth(ElementHandle handle)
    {
        int depth = 0;
        for (ElementHandle h = handle.GetParentHandle(); h.IsValid; h = h.GetParentHandle())
            depth++;

        return depth;
    }

    /// <summary>
    /// Settles the frame's claims into the tooltip to show. Only the winner counts toward the hover
    /// delay; before this, every claimant reset it in turn and nested tooltips never appeared.
    /// </summary>
    private static void TakeClaim()
    {
        if (_claim != null)
        {
            if (_activeElementId == _claimId)
            {
                _hoverTime += _lastDeltaTime;
            }
            else
            {
                _activeElementId = _claimId;
                _hoverTime = 0;
            }
        }

        _pending = _claim;
        _anchorOwner = _claimOwner;
        _claim = null;
        _claimOwner = null;
        _claimDepth = -1;
    }

    public static void Draw(Paper paper)
    {
        _lastDeltaTime = paper.DeltaTime;
        TakeClaim();

        if (_pending == null)
        {
            _activeElementId = 0;
            _hoverTime = 0;
            return;
        }

        if (_hoverTime < MathF.Max(0f, _pending.Options?.Delay ?? _showDelay))
        {
            _pending = null;
            return;
        }

        var theme = Origami.Current;
        var font = theme.Font;
        if (font == null)
        {
            _pending = null;
            return;
        }

        var content = _pending;
        _pending = null;

        var ink = theme.Ink;
        var m = theme.Metrics;
        float fontSize = m.FontSize - 1;        // ~11.5px
        float titleFontSize = m.FontSize;

        bool hasTitle = !string.IsNullOrEmpty(content.Title);
        bool hasIcon = !string.IsNullOrEmpty(content.Icon);
        bool hasShortcut = !string.IsNullOrEmpty(content.Shortcut);
        bool hasText = !string.IsNullOrEmpty(content.Text);

        // .w2tip padding: 6px 11px
        const float padX = 11f;
        const float padY = 6f;

        // Estimate width - cap at MaxWidth so long text wraps instead of stretching
        float textW = 0;
        if (hasTitle)
            textW = MathF.Max(textW, (float)paper.MeasureText(content.Title!, titleFontSize, font).X);
        if (hasText)
            textW = MathF.Max(textW, (float)paper.MeasureText(content.Text, fontSize, font).X);
        if (hasShortcut)
            textW += (float)paper.MeasureText(content.Shortcut!, fontSize, font).X + m.PaddingLarge;

        float naturalW = textW + padX * 2 + (hasIcon ? m.HeaderHeight : 0f);
        float tooltipW = MathF.Min(content.MaxWidth, MathF.Max(content.MinWidth, naturalW));
        if (tooltipW < 40)
            tooltipW = 40;

        bool needsWrap = naturalW > content.MaxWidth;

        var options = content.Options;
        bool useElementAnchor = options?.Anchor == TooltipAnchor.Element && _anchorOwner == paper;
        int anchorId = _activeElementId;
        var pos = paper.PointerPos;
        Rect cursorAnchor = new Rect(pos.X, pos.Y, pos.X, pos.Y);
        TooltipPlacement placement = options?.Placement ?? TooltipPlacement.Bottom;
        // Limit width to the window too, including tooltips whose minimum width is very large.
        tooltipW = MathF.Min(tooltipW, MathF.Max(1f, paper.ScreenRect.Size.X - 8f));
        needsWrap |= naturalW > tooltipW;
        Color bgColor = theme.Popover;

        using (paper.Column("tt_root")
            .PositionType(PositionType.SelfDirected)
            .Position(0, 0)
            .Width(tooltipW).Height(UnitValue.Auto)
            .BackgroundColor(bgColor)
            .Rounded(m.ContainerRounding)
            .DropShadow(0, 6, 20, 0, Color.FromArgb(128, 0, 0, 0))
            .Padding(padX, padX, padY, padY)
            .Gap(m.SpacingSmall)
            .Layer(Layer.Topmost + 1000)
            .IsNotInteractable()
            .OnPostLayout((handle, rect) =>
            {
                // Hover claims come from the previous frame. Resolve the stable ID against this
                // frame's tree after layout so moving or resized elements keep their tooltip attached.
                ElementHandle anchorElement = useElementAnchor ? paper.FindElementByID(anchorId) : default;
                bool elementAnchor = anchorElement.IsValid;
                Rect anchor = elementAnchor ? anchorElement.Data.LayoutRect : cursorAnchor;

                // Use the measured height so wrapping and custom content position correctly
                // on the first visible frame, including when the preferred side must flip.
                Rect placed = PositionTooltip(anchor, rect.Size, paper.ScreenRect, placement,
                    elementAnchor, options?.ShowArrow == true);
                MoveTooltip(handle, placed.Min - rect.Min);
                if (options?.ShowArrow == true)
                    paper.Draw(ref handle, (canvas, r) => DrawArrow(canvas, r, anchor, bgColor, m.ContainerRounding));
            })
            .Enter())
        {
            if (hasTitle || hasIcon)
            {
                using (paper.Row("tt_hdr").Height(UnitValue.Auto).Gap(m.Spacing).Enter())
                {
                    if (hasIcon)
                        paper.Box("tt_ico").Width(m.IconWidth).Height(18)
                            .Text(content.Icon!, font).TextColor(ink.C300)
                            .FontSize(fontSize).Alignment(TextAlignment.MiddleCenter);

                    if (hasTitle)
                        paper.Box("tt_title").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                            .Text(content.Title!, font).TextColor(ink.C500)
                            .FontSize(titleFontSize).Alignment(TextAlignment.MiddleLeft);

                    if (hasShortcut)
                        paper.Box("tt_sc").Width(UnitValue.Auto).Height(UnitValue.Auto)
                            .Text(content.Shortcut!, font).TextColor(ink.C300)
                            .FontSize(fontSize - 1).Alignment(TextAlignment.MiddleRight);
                }
            }

            if (hasText)
            {
                var textBox = paper.Box("tt_text").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                    .Text(content.Text, font)
                    .TextColor(hasTitle ? ink.C300 : ink.C500)
                    .FontSize(fontSize).Alignment(TextAlignment.Left);
                if (needsWrap)
                    textBox.Wrap(Scribe.TextWrapMode.Wrap);
            }

            if (hasShortcut && !hasTitle && !hasIcon)
                paper.Box("tt_sc2").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                    .Text(content.Shortcut!, font).TextColor(ink.C300)
                    .FontSize(fontSize - 1).Alignment(TextAlignment.MiddleRight);

            content.CustomDraw?.Invoke(paper);
        }
    }

    /// <summary>
    /// Places the bubble beside its anchor, flips to the opposite side if it fits better,
    /// and clamps the result to the screen with a 4px margin.
    /// </summary>
    internal static Rect PositionTooltip(Rect anchor, Float2 size, Rect screen, TooltipPlacement placement,
        bool elementAnchor, bool showArrow = false)
    {
        Float2 center = (anchor.Min + anchor.Max) * 0.5f;
        bool centered = elementAnchor || showArrow;
        float gapX = elementAnchor ? 10f : 14f;
        float gapY = elementAnchor ? 10f : 18f;

        // Cursor tooltips keep the usual 14px/18px offset unless an arrow needs to face the anchor.
        Float2 Position(TooltipPlacement side) => side switch
        {
            TooltipPlacement.Top => new Float2(centered ? center.X - size.X / 2 : center.X + 14, anchor.Min.Y - gapY - size.Y),
            TooltipPlacement.Left => new Float2(anchor.Min.X - gapX - size.X, centered ? center.Y - size.Y / 2 : center.Y + 18),
            TooltipPlacement.Right => new Float2(anchor.Max.X + gapX, centered ? center.Y - size.Y / 2 : center.Y + 18),
            _ => new Float2(centered ? center.X - size.X / 2 : center.X + 14, anchor.Max.Y + gapY)
        };

        TooltipPlacement opposite = placement switch
        {
            TooltipPlacement.Top => TooltipPlacement.Bottom,
            TooltipPlacement.Left => TooltipPlacement.Right,
            TooltipPlacement.Right => TooltipPlacement.Left,
            _ => TooltipPlacement.Top
        };

        float minX = screen.Min.X + 4;
        float minY = screen.Min.Y + 4;
        float maxX = screen.Max.X - 4;
        float maxY = screen.Max.Y - 4;

        // Compare overflow along the placement axis before clamping to the screen.
        float Overflow(Float2 p) => placement == TooltipPlacement.Left || placement == TooltipPlacement.Right
            ? MathF.Max(0, minX - p.X) + MathF.Max(0, p.X + size.X - maxX)
            : MathF.Max(0, minY - p.Y) + MathF.Max(0, p.Y + size.Y - maxY);

        Float2 position = Position(placement);
        Float2 flipped = Position(opposite);
        if (Overflow(flipped) < Overflow(position))
            position = flipped;

        // Clamp to screen, including viewports too small to contain the entire bubble.
        position.X = Math.Clamp(position.X, minX, MathF.Max(minX, maxX - size.X));
        position.Y = Math.Clamp(position.Y, minY, MathF.Max(minY, maxY - size.Y));
        return new Rect(position.X, position.Y, position.X + size.X, position.Y + size.Y);
    }

    private static void MoveTooltip(ElementHandle handle, Float2 delta)
    {
        // Post-layout positioning must move the bubble's content along with its background.
        handle.Data.X += delta.X;
        handle.Data.Y += delta.Y;
        foreach (int child in handle.Data.ChildIndices)
            MoveTooltip(new ElementHandle(handle.Owner, child), delta);
    }

    /// <summary>
    /// Draws the little 8px arrow (a square rotated 45 degrees) on the bubble edge facing
    /// the anchor. It follows the bubble when placement flips and stays clear of rounded corners.
    /// </summary>
    private static void DrawArrow(Canvas canvas, Rect rect, Rect anchor, Color color, float rounding)
    {
        const float half = 5.6f;   // Half-diagonal of an 8px square rotated 45 degrees.
        Float2 target = (anchor.Min + anchor.Max) * 0.5f;
        bool vertical = target.Y < rect.Min.Y || target.Y > rect.Max.Y;
        if (!vertical && target.X >= rect.Min.X && target.X <= rect.Max.X)
            return;

        float length = vertical ? rect.Size.X : rect.Size.Y;
        float inset = MathF.Min(MathF.Max(0f, rounding) + half, length / 2f);
        float ax = vertical ? Math.Clamp(target.X, rect.Min.X + inset, rect.Max.X - inset)
            : target.X < rect.Min.X ? rect.Min.X : rect.Max.X;
        float ay = vertical ? target.Y < rect.Min.Y ? rect.Min.Y : rect.Max.Y
            : Math.Clamp(target.Y, rect.Min.Y + inset, rect.Max.Y - inset);

        canvas.SaveState();
        canvas.BeginPath();
        canvas.MoveTo(ax, ay - half);
        canvas.LineTo(ax + half, ay);
        canvas.LineTo(ax, ay + half);
        canvas.LineTo(ax - half, ay);
        canvas.ClosePath();
        canvas.SetFillColor(color);
        canvas.Fill();
        canvas.RestoreState();
    }
}

/// <summary>
/// Extension methods to attach tooltips to any Paper ElementBuilder.
/// </summary>
public static class TooltipExtensions
{
    public static ElementBuilder Tooltip(this ElementBuilder builder, string text, TooltipOptions? options = null)
        => builder.Tooltip(new TooltipContent(text) { Options = options });

    public static ElementBuilder Tooltip(this ElementBuilder builder, string title, string description, TooltipOptions? options = null)
        => builder.Tooltip(new TooltipContent { Title = title, Text = description, Options = options });

    public static ElementBuilder Tooltip(this ElementBuilder builder, TooltipContent content)
    {
        builder.OnHover(content, (captured, e) => TooltipSystem.Hover(e.Source, captured));
        return builder;
    }
}
