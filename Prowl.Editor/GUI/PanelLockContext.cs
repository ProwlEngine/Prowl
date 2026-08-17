using System;

using Prowl.Runtime;
using Prowl.Vector;

namespace Prowl.Editor.GUI;

/// <summary>
/// Cursor lock context that locks to the center of an editor panel instead of the OS window.
/// Panels keep <see cref="PanelOrigin"/> and <see cref="PanelSize"/> up to date each frame.
/// </summary>
public class PanelLockContext : CursorLockContext
{
    /// <summary>Panel origin in Paper-logical coordinates.</summary>
    public Float2 PanelOrigin;
    /// <summary>Panel size in Paper-logical coordinates.</summary>
    public Float2 PanelSize;

    public override IntRect GetConfineBounds()
    {
        float scale = PaperToWindowScale();
        Int2 min = new((int)(PanelOrigin.X * scale), (int)(PanelOrigin.Y * scale));
        // Inclusive, and clamped so an empty panel degenerates to a point rather than inverting
        Int2 max = new(
            Math.Max(min.X, (int)((PanelOrigin.X + PanelSize.X) * scale) - 1),
            Math.Max(min.Y, (int)((PanelOrigin.Y + PanelSize.Y) * scale) - 1));
        return new IntRect(min, max);
    }

    // Paper coords are in [0, fbSize/cs]; OS cursor expects winSize coords.
    // scale = cs/csFbWin converts paper -> winSize (== 1 on macOS, == cs on DPI-unaware Windows).
    private static float PaperToWindowScale()
    {
        var fb = Window.InternalWindow.FramebufferSize;
        var win = Window.InternalWindow.Size;
        float cs = Window.ContentScale;
        float csFbWin = win.X > 0 ? (float)fb.X / win.X : 1f;
        return csFbWin > 0 ? cs / csFbWin : 1f;
    }
}
