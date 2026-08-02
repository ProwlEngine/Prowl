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

    public override Int2 GetLockCenter()
    {
        var fb = Window.InternalWindow.FramebufferSize;
        var win = Window.InternalWindow.Size;
        float cs = Window.ContentScale;
        float csFbWin = win.X > 0 ? (float)fb.X / win.X : 1f;
        // Paper coords are in [0, fbSize/cs]; OS cursor expects winSize coords.
        // scale = cs/csFbWin converts paper -> winSize (== 1 on macOS, == cs on DPI-unaware Windows).
        float scale = csFbWin > 0 ? cs / csFbWin : 1f;
        float centerX = (PanelOrigin.X + PanelSize.X / 2) * scale;
        float centerY = (PanelOrigin.Y + PanelSize.Y / 2) * scale;
        return new Int2((int)centerX, (int)centerY);
    }
}
