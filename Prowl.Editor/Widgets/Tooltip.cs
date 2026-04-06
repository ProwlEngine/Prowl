using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Vector;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Widgets;

/// <summary>
/// Tooltip system. Use the ElementBuilder extension method .Tooltip("text") on any element.
/// Draw with Tooltip.Draw() in EndGui.
/// </summary>
public static class Tooltip
{
    private static string? _pendingText;
    private static int _pendingElementId;
    private static float _hoverTime;
    private static int _activeElementId;
    private const float ShowDelay = 0.4f;

    /// <summary>
    /// Called by the extension method when an element with a tooltip is hovered.
    /// </summary>
    internal static void NotifyHover(int elementId, string text)
    {
        if (_activeElementId == elementId)
        {
            _hoverTime += Prowl.Runtime.Time.UnscaledDeltaTime;
        }
        else
        {
            _activeElementId = elementId;
            _hoverTime = 0;
        }
        _pendingText = text;
        _pendingElementId = elementId;
    }

    /// <summary>
    /// Draw the tooltip if one is active. Call from EditorApplication.EndGui.
    /// </summary>
    public static void Draw(Paper paper)
    {
        // If nothing hovered this frame, reset
        if (_pendingText == null)
        {
            _activeElementId = 0;
            _hoverTime = 0;
            return;
        }

        // Only show after delay
        if (_hoverTime < ShowDelay)
        {
            _pendingText = null;
            return;
        }

        var font = EditorTheme.DefaultFont;
        if (font == null) { _pendingText = null; return; }

        Float2 pos = paper.PointerPos;

        paper.Box("tooltip")
            .PositionType(PositionType.SelfDirected)
            .Position((float)pos.X + 14, (float)pos.Y + 14)
            .Height(UnitValue.Auto)
            .Width(UnitValue.Auto)
            .BackgroundColor(Color.FromArgb(230, 40, 40, 43))
            .BorderColor(EditorTheme.Ink200).BorderWidth(1)
            .Rounded(4)
            .ChildLeft(8).ChildRight(8).ChildTop(4).ChildBottom(4)
            .Layer(Layer.Topmost)
            .IsNotInteractable()
            .Text(_pendingText, font)
            .TextColor(EditorTheme.Text)
            .FontSize(EditorTheme.FontSize - 1);

        _pendingText = null;
    }
}

/// <summary>
/// Extension method to add tooltips to any ElementBuilder.
/// </summary>
public static class TooltipExtensions
{
    /// <summary>
    /// Add a tooltip to this element. Shows after a hover delay.
    /// </summary>
    public static ElementBuilder Tooltip(this ElementBuilder builder, string text)
    {
        builder.OnHover(text, (captured, e) =>
        {
            Widgets.Tooltip.NotifyHover(e.Source.Data.ID, captured);
        });
        return builder;
    }
}
