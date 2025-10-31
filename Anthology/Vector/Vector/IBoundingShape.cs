// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector.Geometry;

namespace Prowl.Vector
{
    /// <summary>
    /// Interface for bounding shapes that support GJK collision detection and rendering.
    /// </summary>
    public interface IBoundingShape
    {
        /// <summary>
        /// Returns the point on the shape that is farthest in the given direction.
        /// This is used for collision detection algorithms like GJK.
        /// </summary>
        /// <param name="direction">The direction to search in (doesn't need to be normalized).</param>
        /// <returns>The farthest point on the shape in the given direction.</returns>
        Float3 SupportMap(Float3 direction);

        /// <summary>
        /// Generates geometry data for this shape as a BMesh-like structure.
        /// The returned GeometryData can be converted to triangle or line meshes for rendering.
        /// </summary>
        /// <param name="resolution">Level of detail for curved surfaces (e.g., number of segments for spheres).</param>
        /// <returns>GeometryData containing vertices, edges, loops, and faces with full topology information.</returns>
        GeometryData GetGeometryData(int resolution = 16);
    }
}
