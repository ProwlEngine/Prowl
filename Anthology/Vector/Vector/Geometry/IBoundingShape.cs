// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Vector.Geometry
{
    /// <summary>
    /// Interface for bounding shapes that support GJK collision detection.
    /// </summary>
    public interface IBoundingShape
    {
        /// <summary>
        /// Returns the point on the shape that is farthest in the given direction.
        /// This is used for collision detection algorithms like GJK.
        /// </summary>
        /// <param name="direction">The direction to search in (doesn't need to be normalized).</param>
        /// <returns>The farthest point on the shape in the given direction.</returns>
        Double3 SupportMap(Double3 direction);
    }
}
