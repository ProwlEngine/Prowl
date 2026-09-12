// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Result of a navmesh query such as <see cref="NavMesh.SamplePosition"/>,
/// <see cref="NavMesh.Raycast"/>, or <see cref="NavMesh.FindClosestEdge"/>
/// (matches Unity's NavMeshHit).
/// </summary>
public struct NavMeshHit
{
    /// <summary>The resulting location on the navmesh.</summary>
    public Float3 Position;

    /// <summary>Normal at the hit (edge/wall normal for raycast and closest-edge queries;
    /// straight up for position samples).</summary>
    public Float3 Normal;

    /// <summary>Distance from the query origin to <see cref="Position"/>.</summary>
    public float Distance;

    /// <summary>Area mask bit of the polygon at the hit location (1 &lt;&lt; area index).</summary>
    public int Mask;

    /// <summary>True when the query found something.</summary>
    public bool Hit;
}
