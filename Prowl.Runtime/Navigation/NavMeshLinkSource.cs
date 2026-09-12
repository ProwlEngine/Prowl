// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// A world-space off-mesh connection fed into a bake (the payload of <see cref="NavMeshLink"/>).
/// Self-contained — no Transform or component references — so it is safe to hand to a
/// background build. A link with <see cref="Width"/> &gt; 0 is expanded into parallel
/// connections across the span, so an agent enters at the nearest point along it.
/// </summary>
public readonly struct NavMeshLinkSource
{
    /// <summary>World-space endpoints. Each must land within the agent radius of walkable
    /// surface for the connection to attach.</summary>
    public readonly Float3 Start, End;

    /// <summary>World-space width of the link: how wide a span of the edge it covers. 0 leaves
    /// the connection at the agent's own radius.</summary>
    public readonly float Width;

    /// <summary>Whether the link can be traversed end-to-start as well.</summary>
    public readonly bool Bidirectional;

    /// <summary>The link's area (see <see cref="NavMeshAreas"/>); traversal cost comes from
    /// the area's cost.</summary>
    public readonly int Area;

    /// <summary>Stable user id stamped on the baked connection, used to resolve a traversing
    /// agent back to its <see cref="NavMeshLink"/> component. 0 = none.</summary>
    public readonly int UserId;

    public NavMeshLinkSource(Float3 start, Float3 end, float width, bool bidirectional, int area, int userId)
    {
        Start = start;
        End = end;
        Width = Math.Max(0f, width);
        Bidirectional = bidirectional;
        Area = area;
        UserId = userId;
    }

    /// <summary>Conservative world AABB covering both endpoints plus the width, for bounds
    /// filtering and for sizing rebuild regions.</summary>
    public AABB Bounds => new AABB(Start, Start).Encapsulating(End).Expanded(Width * 0.5f + 0.5f);

    /// <summary>
    /// The crossing points this link becomes: one per parallel connection, spread across
    /// <see cref="Width"/> (capped at 8). Unity lets an agent enter a wide link at the nearest
    /// point along its entry edge; a Detour off-mesh connection is a single point, so a span is
    /// approximated by several of them side by side and the agent takes the nearest.
    /// Re-expanded every time the cache re-contours a tile.
    /// </summary>
    /// <param name="agentRadius">Bake agent radius: the connection radius, the spacing between
    /// parallel connections, and the inset that keeps the outermost ones on the span.</param>
    /// <param name="results">Receives the crossings; not cleared.</param>
    public void ExpandCrossings(float agentRadius, System.Collections.Generic.List<(Float3 Start, Float3 End)> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        float radius = Math.Max(0.01f, agentRadius);
        int count = Width <= 0f ? 1 : Math.Clamp((int)MathF.Ceiling(Width / (2f * radius)), 1, 8);

        // Horizontal perpendicular of the span, for spreading the parallel connections. A
        // (near-)vertical link has no meaningful width axis; fall back to +X.
        var dir = new Float3(End.X - Start.X, 0, End.Z - Start.Z);
        double len = Math.Sqrt(dir.X * dir.X + dir.Z * dir.Z);
        Float3 perp = len > 1e-4 ? new Float3((float)(-dir.Z / len), 0, (float)(dir.X / len)) : new Float3(1, 0, 0);

        // Endpoints inset by the radius so the outermost connections stay on the span.
        float half = Math.Max(0f, Width * 0.5f - radius);
        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0f : -half + i * (2f * half / (count - 1));
            Float3 offset = perp * t;
            results.Add((Start + offset, End + offset));
        }
    }
}
