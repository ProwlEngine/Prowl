// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Runtime.CSG;

/// <summary>Plane edits for convex brushes: dropping a face and the plane a chamfer cuts along.</summary>
public static class CSGMeshEdit
{
    /// <summary>Drops a face by removing its plane, the neighbouring faces grow to close the gap.</summary>
    public static bool RemoveFace(Plane[] planes, int planeIndex, out Plane[] result)
        => RemoveFace(planes, planeIndex, out result, out _, []);

    /// <inheritdoc cref="RemoveFace(Plane[], int, out Plane[])"/>
    /// <remarks><paramref name="surfaces"/> is the brush's surface data, returned without the removed face so the rest keep their faces.</remarks>
    public static bool RemoveFace(Plane[] planes, int planeIndex, out Plane[] result, out SurfaceData[] keptSurfaces, SurfaceData[] surfaces)
    {
        result = planes;
        keptSurfaces = surfaces;
        if (planeIndex < 0 || planeIndex >= planes.Length || planes.Length <= 4) return false;

        var rebuilt = new List<Plane>(planes.Length - 1);
        var kept = new List<SurfaceData>(planes.Length - 1);
        for (int i = 0; i < planes.Length; i++)
        {
            if (i == planeIndex) continue;
            rebuilt.Add(planes[i]);
            if (i < surfaces.Length) kept.Add(surfaces[i]);
        }

        Plane[] array = rebuilt.ToArray();
        if (!GeometryGenerator.ConvexFromPlanes(array).IsValid) return false;

        result = array;
        keptSurfaces = kept.ToArray();
        return true;
    }

    /// <summary>Cuts a corner or an edge off by adding one plane along the average of the meeting faces' normals.</summary>
    /// <param name="normals">Outward normals of the faces meeting at the corner or edge.</param>
    /// <param name="corner">The point the cut is measured back from.</param>
    /// <param name="distance">How far back along the average normal to cut.</param>
    /// <param name="plane">The cutting plane.</param>
    public static bool ChamferPlane(IReadOnlyList<Float3> normals, Float3 corner, float distance, out Plane plane)
    {
        plane = default;
        if (normals.Count < 2 || distance <= 1e-5f) return false;

        Float3 average = Float3.Zero;
        foreach (Float3 n in normals) average += n;

        if (Float3.LengthSquared(average) < 1e-10f) return false;
        average = Float3.Normalize(average);

        plane = new Plane(average, Float3.Dot(average, corner - average * distance));
        return true;
    }

}
