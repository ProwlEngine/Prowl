// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Editor.Modeling;

/// <summary>
/// Where a moved point snaps to under the mouse: a corner within reach first, then the nearest point of an edge within
/// reach (its middle when the mouse is near that), then the face under the mouse. What is being moved is left out by
/// the caller.
/// </summary>
public static class Snapping
{
    /// <summary>How near on screen, in pixels, a corner, an edge and an edge's middle pull.</summary>
    public const float CornerReach = 16f, EdgeReach = 12f, MiddleReach = 10f;

    /// <param name="toScreen">Where a world point is on screen, or null when it is behind the view.</param>
    /// <param name="faceHit">Where the mouse's ray first meets a face that may be snapped to, if it does.</param>
    public static bool Find(IEnumerable<Float3> corners, IEnumerable<(Float3 A, Float3 B)> edges, Float3? faceHit,
                            Func<Float3, Float2?> toScreen, Float2 mouse, out Float3 point)
    {
        point = default;
        float best = CornerReach;
        bool found = false;
        foreach (Float3 corner in corners)
        {
            if (toScreen(corner) is not { } at) continue;
            float distance = Float2.Distance(at, mouse);
            if (distance >= best) continue;
            best = distance;
            point = corner;
            found = true;
        }
        if (found) return true;

        best = EdgeReach;
        foreach ((Float3 a, Float3 b) in edges)
        {
            if (toScreen(a) is not { } from || toScreen(b) is not { } to) continue;
            Float2 along = to - from;
            float lengthSq = Float2.Dot(along, along);
            float t = lengthSq > 1e-6f ? Math.Clamp(Float2.Dot(mouse - from, along) / lengthSq, 0f, 1f) : 0f;
            if (Float2.Distance(from + along * 0.5f, mouse) < MiddleReach) t = 0.5f;

            float distance = Float2.Distance(from + along * t, mouse);
            if (distance >= best) continue;
            best = distance;
            point = a + (b - a) * t;
            found = true;
        }
        if (found) return true;

        if (faceHit is not { } hit) return false;
        point = hit;
        return true;
    }
}
