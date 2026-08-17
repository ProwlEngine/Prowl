// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// How the cursor is constrained - independent of cursor visibility.
/// </summary>
public enum CursorLockMode
{
    /// <summary>The cursor moves freely and may leave the window.</summary>
    None,

    /// <summary>
    /// The cursor moves freely but is held inside the current context's bounds. Polled once per frame
    /// rather than enforced by the OS, so a fast flick can briefly land a click on another window.
    /// </summary>
    Confined,

    /// <summary>The cursor is hidden and pinned to the current context's center (FPS look).</summary>
    Locked
}

/// <summary>
/// Defines a cursor lock context where the cursor locks to, and whether locking is allowed.
/// Contexts are stacked via Input.PushLockContext / PopLockContext.
/// The topmost context controls cursor lock behavior.
/// </summary>
public class CursorLockContext
{
    /// <summary>Whether cursor locking is allowed in this context.</summary>
    public bool AllowLock { get; set; } = true;

    /// <summary>
    /// Returns the screen-space position the cursor should be locked to.
    /// Override for custom centering behavior (e.g., center of a panel instead of window).
    /// </summary>
    public virtual Int2 GetLockCenter() => GetConfineBounds().Center;

    /// <summary>
    /// Returns the inclusive window-space rect <see cref="CursorLockMode.Confined"/> holds the cursor
    /// inside. Override to confine to something smaller than the window (e.g. an editor panel).
    /// </summary>
    public virtual IntRect GetConfineBounds()
    {
        var size = Window.InternalWindow.Size;
        return new IntRect(Int2.Zero, new Int2(size.X - 1, size.Y - 1));
    }
}
