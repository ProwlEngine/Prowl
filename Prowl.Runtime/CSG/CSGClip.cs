// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime.CSG;

/// <summary>What happens to the two halves a clip plane divides a brush into.</summary>
public enum CSGClipMode
{
    /// <summary>Keep what is behind the plane, discard what is in front.</summary>
    RemoveFront,
    /// <summary>Keep what is in front of the plane, discard what is behind.</summary>
    RemoveBack,
    /// <summary>Keep both, as two separate brushes.</summary>
    Split,
}

/// <summary>Cuts brushes with a plane by adding it as one more half space.</summary>
public static class CSGClip
{
    /// <summary>The plane through three points, or false when they are collinear.</summary>
    public static bool PlaneFromPoints(Float3 a, Float3 b, Float3 c, out Plane plane)
    {
        Float3 normal = Float3.Cross(b - a, c - a);
        plane = default;

        if (Float3.LengthSquared(normal) < 1e-10f) return false;

        normal = Float3.Normalize(normal);
        plane = new Plane(normal, Float3.Dot(normal, a));
        return true;
    }

    /// <summary>The plane through two points that runs away from the viewer, as when dragging a cut line across the screen.</summary>
    public static bool PlaneFromEdge(Float3 a, Float3 b, Float3 viewDirection, out Plane plane)
        => PlaneFromPoints(a, b, a + Float3.NormalizeSafe(viewDirection, Float3.UnitZ), out plane);

    /// <summary>Adding a half space keeps what is behind the plane.</summary>
    private static Plane[] Append(IReadOnlyList<Plane> planes, Plane cut)
    {
        var result = new Plane[planes.Count + 1];
        for (int i = 0; i < planes.Count; i++) result[i] = planes[i];

        result[^1] = cut;
        return result;
    }

    /// <summary>Whether the plane passes through the solid.</summary>
    public static bool Crosses(IReadOnlyList<Float3> corners, Plane plane, float epsilon = 1e-4f)
    {
        bool front = false, back = false;

        foreach (Float3 p in corners)
        {
            float d = plane.GetSignedDistanceToPoint(p);
            if (d > epsilon) front = true;
            else if (d < -epsilon) back = true;

            if (front && back) return true;
        }

        return false;
    }
}
