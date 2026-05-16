// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

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
    public float MaxWidth = 280f;

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
        float fontSize = theme.Metrics.FontSize - 1;
        float titleFontSize = theme.Metrics.FontSize;

        bool hasTitle = !string.IsNullOrEmpty(content.Title);
        bool hasIcon = !string.IsNullOrEmpty(content.Icon);
        bool hasShortcut = !string.IsNullOrEmpty(content.Shortcut);
        bool hasText = !string.IsNullOrEmpty(content.Text);

        float padX = 8f, padY = 6f;

        // Estimate width
        float textW = 0;
        if (hasTitle) textW = MathF.Max(textW, (float)paper.MeasureText(content.Title!, titleFontSize, font).X);
        if (hasText) textW = MathF.Max(textW, (float)paper.MeasureText(content.Text, fontSize, font).X);
        if (hasShortcut) textW += (float)paper.MeasureText(content.Shortcut!, fontSize, font).X + 12f;

        float tooltipW = MathF.Min(content.MaxWidth, textW + padX * 2 + (hasIcon ? 22f : 0f));
        if (tooltipW < 40) tooltipW = 40;

        // Position below cursor
        var pos = paper.PointerPos;
        float tooltipX = (float)pos.X + 14;
        float tooltipY = (float)pos.Y + 18;

        // Clamp to screen
        float screenW = (float)paper.ScreenRect.Size.X;
        if (tooltipX + tooltipW > screenW - 4) tooltipX = screenW - tooltipW - 4;
        if (tooltipX < 4) tooltipX = 4;

        Color bgColor = Color.FromArgb(240, theme.Neutral.C400.R, theme.Neutral.C400.G, theme.Neutral.C400.B);

        using (paper.Column("tt_root")
            .PositionType(PositionType.SelfDirected)
            .Position(tooltipX, tooltipY)
            .Width(tooltipW).Height(UnitValue.Auto)
            .BackgroundColor(bgColor)
            .BorderColor(ink.C200).BorderWidth(1)
            .Rounded(4)
            .BoxShadow(0, 2, 8, -2, Color.FromArgb(80, 0, 0, 0))
            .Padding(padX, padX, padY, padY)
            .ColBetween(2)
            .Layer(Layer.Topmost + 1000)
            .ClampToScreen()
            .IsNotInteractable()
            .Enter())
        {
            if (hasTitle || hasIcon)
            {
                using (paper.Row("tt_hdr").Height(UnitValue.Auto).RowBetween(4).Enter())
                {
                    if (hasIcon)
                        paper.Box("tt_ico").Width(16).Height(18)
                            .Text(content.Icon!, font).TextColor(ink.C400)
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
                paper.Box("tt_text").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                    .Text(content.Text, font)
                    .TextColor(hasTitle ? ink.C400 : ink.C500)
                    .FontSize(fontSize).Alignment(TextAlignment.MiddleLeft);

            if (hasShortcut && !hasTitle && !hasIcon)
                paper.Box("tt_sc2").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                    .Text(content.Shortcut!, font).TextColor(ink.C300)
                    .FontSize(fontSize - 1).Alignment(TextAlignment.MiddleRight);

            content.CustomDraw?.Invoke(paper);
        }
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
