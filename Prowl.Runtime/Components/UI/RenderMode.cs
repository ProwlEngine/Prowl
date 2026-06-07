// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.UI;

/// <summary>
/// Render mode for a <see cref="GameCanvas"/>.
/// </summary>
public enum RenderMode
{
    /// <summary>
    /// The GameCanvas is rendered as an overlay on top of the entire screen.
    /// UI elements are sized in screen-space pixels.
    /// </summary>
    ScreenSpaceOverlay,

    /// <summary>
    /// The GameCanvas is rendered as a screen-space overlay on a specific camera's
    /// output. Currently behaves identically to <see cref="ScreenSpaceOverlay"/>.
    /// </summary>
    ScreenSpaceCamera,

    /// <summary>
    /// The GameCanvas lives in world space. Use <see cref="WorldCanvas"/> for this mode.
    /// </summary>
    WorldSpace,
}
