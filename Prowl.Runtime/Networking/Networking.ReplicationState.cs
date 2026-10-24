// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Resources;

namespace Prowl.Runtime.Networking;

public static partial class Networking
{
    /// <summary>
    ///     Everything one session knows. A session starts with a fresh one and drops it at the end, so nothing it
    ///     learned can leak into the next. Settings, warnings already given, and the call being dispatched are not here.
    /// </summary>
    private sealed class ReplicationState
    {
        // The scene this session replicates: the current one on the host, the last one received on a client
        // (which only becomes current at the end of the frame it arrived in).
        public Scene? Scene;

        public readonly EntryTable Entries = new();

        // Identifier lookups for messages, over the replicated scene. Filled on demand and rebuilt on a miss,
        // at most once a frame, so a stream of unknown ids can't make every lookup walk the whole scene.
        public readonly Dictionary<Guid, EngineObject> Index = new();
        public int IndexFrame = -1;

        // The object a spawn is building, by identifier, until it joins the scene.
        public Dictionary<Guid, EngineObject>? Spawning;

        // Host only. Zero is never an object.
        public uint NextNetId;

        public NetworkJoinState JoinState;
        public ulong SceneGeneration;
        public double ReadyDeadline;
        public bool CatchUpApplied;
        public readonly Dictionary<int, PeerReadiness> PeerReadiness = new();

        // [Sync] values changed since the last tick.
        public readonly HashSet<(Component Component, uint Slot)> Dirty = new();

        public double NextTick;
        public uint Tick;
        public double LastTransformTime;
    }
}
