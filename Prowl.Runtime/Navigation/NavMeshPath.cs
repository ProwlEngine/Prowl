// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>Result of a <see cref="NavMeshQuery.FindPath"/> call.</summary>
public sealed class NavMeshPath
{
    /// <summary>A path that found nothing. Distinct from a zero-length <see cref="Corners"/> on a
    /// successful path (start and end in the same spot).</summary>
    public static readonly NavMeshPath None = new() { Success = false };

    /// <summary>Whether a path was found at all. A partial path (see <see cref="Partial"/>) still counts.</summary>
    public bool Success;

    /// <summary>True when the destination could not actually be reached and this path only gets as
    /// close as the navmesh allows.</summary>
    public bool Partial;

    /// <summary>The string-pulled waypoints an agent should walk toward, in order. What a
    /// <see cref="NavMeshAgent"/> actually steers along.</summary>
    public Float3[] Corners = [];

    /// <summary>The raw polygon corridor the path crosses, as each polygon's own index within
    /// whichever tile it belongs to (not a globally unique id - two different tiles can each have a
    /// polygon at the same index). Lower-level diagnostic data (which polygons a path touches, not the
    /// route itself) - <see cref="NavMeshAgent"/>'s selected-gizmo draws <see cref="Corners"/> instead,
    /// since that's the actual line an agent walks. Walk <see cref="Corners"/> for movement; this is
    /// not it.</summary>
    public int[] CorridorPolygons = [];

    /// <summary>Parallel to <see cref="Corners"/>: true at the corner where the path starts crossing a
    /// <see cref="NavMeshLink"/> rather than walking ordinary ground. An agent reaching such a corner
    /// is the trigger for <see cref="NavMeshLink"/>'s traversal notification once movement drives that.</summary>
    public bool[] CornerIsOffMeshLink = [];
}
