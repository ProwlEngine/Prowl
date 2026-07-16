// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.UI;

/// <summary>
/// The pipeline destination for a UI element. Mirrors <see cref="RenderMode"/> but
/// lives on the render side, where it controls *where* the UI queue draws and
/// *which* projection it uses. Kept separate from <see cref="RenderMode"/> so the
/// pipeline never references user-facing component fields directly.
/// </summary>
public enum UISurface : byte
{
    /// <summary>Drawn into the camera's color RT in the same pass as 3D Transparent geometry.
    /// Items participate in frustum culling and back-to-front sorting.</summary>
    World    = 0,

    /// <summary>Drawn directly to the back-buffer (or camera target) after the final blit
    /// and gizmos. Bypasses HDR tonemapping and post-process.</summary>
    Overlay  = 2,
}
