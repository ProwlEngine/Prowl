// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.OrigamiUI;

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

    public static void Hover(int elementId, TooltipContent content)
    {
        if (_activeElementId == elementId)
            _hoverTime += _lastDeltaTime;
        else
        {
            _activeElementId = elementId;
            _hoverTime = 0;
        }
        _pending = content;
    }

    public static void Hover(int elementId, string text)
        => Hover(elementId, new TooltipContent(text));

    public static void Draw(Paper paper)
    {
        _lastDeltaTime = paper.DeltaTime;

        if (_pending == null)
        {
            _activeElementId = 0;
            _hoverTime = 0;
            return;
        }

        if (_hoverTime < _showDelay)
        {
            _pending = null;
            return;
        }

        var theme = Origami.Current;
        var font = theme.Font;
        if (font == null) { _pending = null; return; }

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
        const float padX = 11f, padY = 6f;

        // Estimate width - cap at MaxWidth so long text wraps instead of stretching
        float textW = 0;
        if (hasTitle) textW = MathF.Max(textW, (float)paper.MeasureText(content.Title!, titleFontSize, font).X);
        if (hasText) textW = MathF.Max(textW, (float)paper.MeasureText(content.Text, fontSize, font).X);
        if (hasShortcut) textW += (float)paper.MeasureText(content.Shortcut!, fontSize, font).X + m.PaddingLarge;

        float naturalW = textW + padX * 2 + (hasIcon ? m.HeaderHeight : 0f);
        float tooltipW = MathF.Min(content.MaxWidth, naturalW);
        if (tooltipW < 40) tooltipW = 40;
        bool needsWrap = naturalW > content.MaxWidth;

        // Position below cursor
        var pos = paper.PointerPos;
        float tooltipX = (float)pos.X + 14;
        float tooltipY = (float)pos.Y + 18;

        // Clamp to screen
        float screenW = (float)paper.ScreenRect.Size.X;
        if (tooltipX + tooltipW > screenW - 4) tooltipX = screenW - tooltipW - 4;
        if (tooltipX < 4) tooltipX = 4;

        Color bgColor = Color.FromArgb(255, 42, 36, 64);   // #2a2440

        float arrowPx = (float)pos.X;
        float arrowPy = (float)pos.Y;

        using (paper.Column("tt_root")
            .PositionType(PositionType.SelfDirected)
            .Position(tooltipX, tooltipY)
            .Width(tooltipW).Height(UnitValue.Auto)
            .BackgroundColor(bgColor)
            .Rounded(7f)
            .BoxShadow(0, 6, 20, 0, Color.FromArgb(128, 0, 0, 0))
            .Padding(padX, padX, padY, padY)
            .ColBetween(m.SpacingSmall)
            .Layer(Layer.Topmost + 1000)
            .ClampToScreen()
            .IsNotInteractable()
            .OnPostLayout((handle, rect) => paper.Draw(ref handle,
                (canvas, r) => DrawArrow(canvas, r, arrowPx, arrowPy, bgColor)))
            .Enter())
        {
            if (hasTitle || hasIcon)
            {
                using (paper.Row("tt_hdr").Height(UnitValue.Auto).RowBetween(m.Spacing).Enter())
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
    /// Draw the little 8px arrow (an 45deg-rotated square) on the bubble edge that faces the
    /// anchor. Placed on the top edge when the bubble sits below the pointer, on the bottom edge
    /// when it was flipped above.
    /// </summary>
    private static void DrawArrow(Canvas canvas, Rect rect, float pointerX, float pointerY, Color color)
    {
        const float half = 5.6f;   // half-diagonal of an 8px square rotated 45deg
        float left = (float)rect.Min.X;
        float top = (float)rect.Min.Y;
        float right = (float)(rect.Min.X + rect.Size.X);
        float bottom = (float)(rect.Min.Y + rect.Size.Y);

        float edgeY = pointerY >= bottom ? bottom : top;   // opposite edge points at the anchor
        float ax = Math.Clamp(pointerX, left + 7f + half, right - 7f - half);

        canvas.SaveState();
        canvas.BeginPath();
        canvas.MoveTo(ax, edgeY - half);
        canvas.LineTo(ax + half, edgeY);
        canvas.LineTo(ax, edgeY + half);
        canvas.LineTo(ax - half, edgeY);
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
    public static ElementBuilder Tooltip(this ElementBuilder builder, string text)
    {
        builder.OnHover(text, (captured, e) => TooltipSystem.Hover(e.Source.Data.ID, captured));
        return builder;
    }

    public static ElementBuilder Tooltip(this ElementBuilder builder, string title, string description)
    {
        var content = new TooltipContent { Title = title, Text = description };
        builder.OnHover(content, (captured, e) => TooltipSystem.Hover(e.Source.Data.ID, captured));
        return builder;
    }

    public static ElementBuilder Tooltip(this ElementBuilder builder, TooltipContent content)
    {
        builder.OnHover(content, (captured, e) => TooltipSystem.Hover(e.Source.Data.ID, captured));
        return builder;
    }
}
