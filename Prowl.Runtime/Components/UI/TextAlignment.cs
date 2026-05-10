// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.UI;

[System.Flags]
public enum TextAlignment
{
    // Individual axis components (distinct powers of two)
    Top    = 1 << 0,
    Bottom = 1 << 1,
    Left   = 1 << 2,
    Right  = 1 << 3,
    Center = 1 << 4,  // vertical center
    Middle = 1 << 5,  // horizontal center

    // Convenience masks (optional)
    HorizontalMask = Left | Middle | Right,
    VerticalMask   = Top | Center | Bottom,

    // All 9 named combinations
    TopLeft      = Top | Left,
    TopCenter    = Top | Middle,
    TopRight     = Top | Right,

    CenterLeft   = Center | Left,
    CenterMiddle = Center | Middle,
    CenterRight  = Center | Right,

    BottomLeft   = Bottom | Left,
    BottomCenter = Bottom | Middle,
    BottomRight  = Bottom | Right,
}
