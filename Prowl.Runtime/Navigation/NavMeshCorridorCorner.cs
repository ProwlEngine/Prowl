// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.Navigation;

/// <summary>One corner of a <see cref="NavMeshAgent"/>'s live corridor, as <see cref="NavMeshAgent.UpcomingCorners"/>
/// reports it: where it is, and how far along the corridor (corner to corner, not a straight line from the
/// agent's current position) walking there actually is.</summary>
public readonly struct NavMeshCorridorCorner
{
    /// <summary>World-space position of this corner.</summary>
    public Float3 Position { get; }

    /// <summary>Distance along the corridor from the agent's current position to this corner - the sum of
    /// every segment length up to and including this one, not a straight line to it.</summary>
    public float Distance { get; }

    internal NavMeshCorridorCorner(Float3 position, float distance)
    {
        Position = position;
        Distance = distance;
    }
}
