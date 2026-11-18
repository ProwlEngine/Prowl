// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Vector;

using static Prowl.Runtime.Networking.NetworkSystem;

namespace Prowl.Runtime.Networking;

internal sealed partial class NetworkObject(NetworkSystem scene, GameObject gameObject)
{
    public readonly HashSet<Component> AuthorityListeners = new();
    public readonly HashSet<Component> SpawnListeners = new();
    internal readonly NetworkSystem System = scene;
    public readonly TransformSync TransformSync = new();
    public uint Generation;
    public double LastCaptureTime;
    public bool HasLocalAuthority;

    /// <summary>Host: the clients that do not have this object, or null when they all do.</summary>
    public HashSet<int>? HiddenFrom;

    /// <summary>The host-assigned number messages use for this object in place of its identifier.</summary>
    public uint NetId;

    /// <summary>The prefab it was spawned from and its tree's identifiers then, or empty for one sent whole.</summary>
    // What it was built from, for peers to build their own copy, or null when it goes serialized.
    public SpawnTemplate? Template;

    public int NotifiedOwnerId = NoOwner;
    public int Owner = NoOwner;
    public bool SpawnNotified;
    public bool TearingDown;
    public Float3? TransferVelocity;
    public NetworkOrphaned Orphaned => GameObject.Network.Orphaned;
    public GameObject GameObject { get; } = gameObject;
}
