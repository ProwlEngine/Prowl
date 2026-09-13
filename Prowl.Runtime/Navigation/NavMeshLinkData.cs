// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>
/// A resolved, world-space off-mesh connection ready to hand to <see cref="NavMeshQuery"/>. Deliberately
/// plain data rather than the <see cref="NavMeshLink"/> component itself: <see cref="NavMeshQuery"/>
/// stays engine-data-only and never depends on a MonoBehaviour type, and a surface can resolve this
/// once per rebuild rather than every query reaching back into the scene.
/// </summary>
public readonly struct NavMeshLinkData
{
    /// <summary>World-space position of the connection's first endpoint.</summary>
    public readonly Float3 Start;

    /// <summary>World-space position of the connection's second endpoint.</summary>
    public readonly Float3 End;

    /// <summary>How close an agent's path needs to pass to either endpoint to use this connection.</summary>
    public readonly float Radius;

    /// <summary>Whether the connection can be crossed from either end, or only <see cref="Start"/> to
    /// <see cref="End"/>.</summary>
    public readonly bool Bidirectional;

    /// <summary>The navmesh area (see <see cref="NavMeshAreas"/>) this connection belongs to - its
    /// cost applies to crossing it.</summary>
    public readonly int AreaIndex;

    public NavMeshLinkData(Float3 start, Float3 end, float radius, bool bidirectional, int areaIndex)
    {
        Start = start;
        End = end;
        Radius = radius;
        Bidirectional = bidirectional;
        AreaIndex = areaIndex;
    }
}
