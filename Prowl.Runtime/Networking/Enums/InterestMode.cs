// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Networking;

/// <summary>How the host decides which networked objects a client is near enough to receive.</summary>
public enum InterestMode : byte
{
    /// <summary>Every client receives every object its visibility rules allow.</summary>
    None,

    /// <summary>A client receives the objects within range of its viewpoints, checked every few ticks.</summary>
    Distance
}
