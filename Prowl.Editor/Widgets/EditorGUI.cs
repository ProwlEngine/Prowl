using System;
using System.Globalization;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Scribe;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Widgets;

/// <summary>
/// Core editor widget library. Immediate-mode drawing, callback-based state updates.
/// Each widget draws itself and returns a WidgetResult for chaining OnValueChanged callbacks.
/// State is managed via Paper element storage values persist across frames automatically.
/// </summary>
public static class EditorGUI
{
    private static FontFile? Font => EditorTheme.DefaultFont;
    private static float FontSz => EditorTheme.FontSize;
    private static float LabelW => EditorTheme.LabelWidth;

    /// <summary>
    /// Fullscreen backdrop element. Two flavours:
    /// <list type="bullet">
    /// <item><description><paramref name="dim"/>=true (default): semi-transparent black on <c>Layer.Overlay</c> — modal-style darken.</description></item>
    /// <item><description><paramref name="dim"/>=false: invisible click-blocker on <c>Layer.Topmost</c> — popup-style click-outside-to-close.</description></item>
    /// </list>
    /// <paramref name="onClose"/> fires on click; pass <c>null</c> for a non-dismissable backdrop (e.g. a modal that requires a button).
    /// </summary>
    public static void Backdrop(Paper paper, string id, Action? onClose = null, bool dim = true)
    {
        // Both flavours use a huge SelfDirected rectangle so the backdrop covers the entire
        // screen regardless of where in the layout tree it's emitted from. Size(Stretch)
        // would only fill the parent, which can be just a panel (e.g. the graph editor).
        var box = paper.Box(id)
            .PositionType(PositionType.SelfDirected)
            .Position(-9999, -9999)
            .Size(99999, 99999)
            .Layer(dim ? Layer.Overlay : Layer.Topmost);

        if (dim)
            box.BackgroundColor(Color.FromArgb(120, 0, 0, 0));

        // Backdrops absorb interaction with whatever is behind them — stop events from
        // bubbling to ancestors, so e.g. scrolling over a popup doesn't pan the canvas.
        box.StopEventPropagation();

        if (onClose != null)
            box.OnClick(0, (_, _) => onClose());
    }

    public static Color LerpRGB(Color a, Color b, float t)
    {
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t),
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t)
        );
    }

    // ================================================================
    //  Context Menu
    // ================================================================

}
