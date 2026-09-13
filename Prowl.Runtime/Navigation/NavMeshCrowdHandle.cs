// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Recast.Detour.Crowd;

namespace Prowl.Runtime.Navigation;

/// <summary>An agent's slot inside a <see cref="NavMeshCrowd"/>. Opaque outside this file - the only
/// place <see cref="DtCrowdAgent"/> is named anywhere in the engine - so callers (<see cref="NavMeshAgent"/>)
/// hold and pass this around without ever seeing a Detour type.</summary>
public readonly struct NavMeshCrowdHandle
{
    /// <summary>The wrapped Detour crowd agent. Internal: this is the one place its type leaks outside
    /// <see cref="NavMeshCrowd"/>, and only to other files within this same assembly.</summary>
    internal DtCrowdAgent Agent { get; }

    /// <summary>Wraps a Detour crowd agent, as returned by <see cref="NavMeshCrowd.AddAgent"/>.</summary>
    internal NavMeshCrowdHandle(DtCrowdAgent agent) => Agent = agent;
}
