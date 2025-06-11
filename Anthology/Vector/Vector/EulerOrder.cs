// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Vector
{
    /// <summary>
    /// Specifies the sequence of rotations for Euler angles.
    /// The naming convention indicates the axis order. A suffix of 's' means
    /// the rotations are applied to a static (world) frame, while 'r' means
    /// they are applied to a rotating (local) frame.
    /// </summary>
    public enum EulerOrder
    {
        // Static Frame (extrinsic) rotations
        XYZs = 0,
        XYXs = 1,
        XZYs = 2,
        XZXs = 3,
        YZXs = 4,
        YZYs = 5,
        YXZs = 6,
        YXYs = 7,
        ZXYs = 8,
        ZXZs = 9,
        ZYXs = 10,
        ZYZs = 11,

        // Rotating Frame (intrinsic) rotations
        ZYXr = 12,
        ZXYr = 13,
        YXZr = 14,
        YZXr = 15,
        XZYr = 16,
        XYZr = 17,
        ZYZr = 18,
        ZXZr = 19,
        YXYr = 20,
        YZYr = 21,
        XYXr = 22,
        XZXr = 23
    }
}
