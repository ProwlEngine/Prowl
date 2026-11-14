// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Networking;

/// <summary>Why a networked object left this peer, as <see cref="NetworkAccessor.DespawnReason" /> reports it.</summary>
public enum NetworkDespawnReason : byte
{
    /// <summary>It was destroyed.</summary>
    Destroyed,

    /// <summary>The host stopped sending it to this peer. It still exists on the host.</summary>
    OutOfInterest,

    /// <summary>The section it was in was unloaded.</summary>
    SectionUnloaded,

    /// <summary>The networked scene changed.</summary>
    SceneChanged,

    /// <summary>The session ended.</summary>
    SessionEnded
}
