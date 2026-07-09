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
    /// The GameCanvas is projected through a specific camera's view onto a plane a fixed distance
    /// in front of it, so it shares the camera's perspective and can be occluded by nearer geometry.
    /// </summary>
    ScreenSpaceCamera,

    /// <summary>
    /// The GameCanvas lives in world space, positioned by its GameObject transform - a physical panel
    /// in the scene. Design pixels map 1:1 to world units (scale the transform to resize).
    /// </summary>
    WorldSpace,
}
