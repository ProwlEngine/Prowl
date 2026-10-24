// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.Networking.Enums;
using Prowl.Runtime.Networking.Systems;
using Prowl.Vector;

using static Prowl.Runtime.Networking.Systems.NetworkSystem;

namespace Prowl.Runtime.Networking.Objects;

internal sealed partial class NetworkObject(NetworkSystem scene, GameObject gameObject)
{
    public readonly HashSet<Component> AuthorityListeners = new();
    public readonly HashSet<Component> SpawnListeners = new();
    internal readonly NetworkSystem System = scene;
    public readonly TransformSync TransformSync = new();
    public uint Generation;
    public bool HasLocalAuthority;

    /// <summary>The host-assigned number messages use for this object in place of its identifier.</summary>
    public uint NetId;

    public int NotifiedOwnerId = NoOwner;
    public int Owner = NoOwner;
    public bool SpawnNotified;
    public bool TearingDown;
    public Float3? TransferVelocity;
    public NetworkOrphaned Orphaned => GameObject.Network.Orphaned;
    public GameObject GameObject { get; } = gameObject;
}
