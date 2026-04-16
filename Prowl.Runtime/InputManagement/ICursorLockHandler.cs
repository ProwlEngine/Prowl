// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Defines a cursor lock context — where the cursor locks to, and whether locking is allowed.
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
    public virtual Int2 GetLockCenter()
    {
        var size = Window.InternalWindow.Size;
        return new Int2(size.X / 2, size.Y / 2);
    }
}
