// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.UI;

/// <summary>
/// Controls how matching is performed when <see cref="ScaleMode.ScaleWithScreenSize"/> is used.
/// </summary>
public enum ScreenMatchMode
{
    /// <summary>
    /// Blend between matching the width and height of the reference resolution
    /// using <see cref="GameCanvas.MatchWidthOrHeight"/>.
    /// </summary>
    MatchWidthOrHeight,

    /// <summary>
    /// Expand the canvas area so it never becomes smaller than the reference resolution.
    /// </summary>
    Expand,

    /// <summary>
    /// Shrink the canvas area so it never becomes larger than the reference resolution.
    /// </summary>
    Shrink,
}
