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
    public uint Generation;
    public bool HasLocalAuthority;

    /// <summary>Host: the clients that do not have this object, or null when they all do.</summary>
    public HashSet<int>? HiddenFrom;

    /// <summary>The host-assigned number messages use for this object in place of its identifier.</summary>
    public uint NetId;

    /// <summary>What it was built from, for peers to build their own copy.</summary>
    public required SpawnTemplate Template;

    public int NotifiedOwnerId = NoOwner;
    public int Owner = NoOwner;
    public bool SpawnNotified;
    public bool TearingDown;

    /// <summary>Client: kept through a scene change, until the host spawns it again or finishes its spawns without it.</summary>
    public bool AwaitingRespawn;
    public Float3? TransferVelocity;
    public NetworkOrphaned Orphaned => GameObject.Network.Orphaned;

    /// <summary>Its networked components, fixed for the life of the spawn. A component's place here is its index on the wire.</summary>
    public Component[] Components { get; init; } = NetworkComponents.Gather(gameObject);

    private NetworkTransform[]? _transforms;

    /// <summary>
    ///     The NetworkTransform components in its tree, in tree order, fixed when it spawns to the ones it was built
    ///     with, as Mirror gathers its behaviours once in Awake. A transform's place here is its index on the wire.
    /// </summary>
    public NetworkTransform[] Transforms
    {
        get
        {
            if (_transforms == null)
            {
                SetTransforms(NetworkComponents.GatherTransforms(GameObject));
            }

            return _transforms!;
        }
    }

    /// <summary>Takes the object's transforms as given, in wire order.</summary>
    public void SetTransforms(NetworkTransform[] transforms)
    {
        _transforms = transforms;
        for (int i = 0; i < transforms.Length; i++)
        {
            transforms[i].Entry = this;
            transforms[i].Index = (byte)i;
            transforms[i].SendSlot = -1;
            transforms[i].HasSendTick = false;
        }
    }

    /// <summary>The transform that moves the networked object itself, or null when it has none.</summary>
    public NetworkTransform? RootTransform
    {
        get
        {
            foreach (NetworkTransform transform in Transforms)
            {
                if (transform.IsValid() && ReferenceEquals(transform.Moved, GameObject))
                {
                    return transform;
                }
            }

            return null;
        }
    }

    /// <summary>The changed [Sync] members of each networked component not sent yet, a bit each, or null for none.</summary>
    public ulong[]? Dirty;

    /// <summary>True while this object is on the list of objects with changes to send.</summary>
    public bool StateDirty;

    /// <summary>The tick each networked component last sent its changes on, once it has, for its send interval.</summary>
    public (uint Tick, bool Set)[]? StateSent;

    /// <summary>The index of a networked component of this object, or -1.</summary>
    public int IndexOf(Component component) => Array.IndexOf(Components, component);
    public GameObject GameObject { get; } = gameObject;
}
