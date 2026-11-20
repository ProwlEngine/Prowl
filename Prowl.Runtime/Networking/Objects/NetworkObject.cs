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

    /// <summary>The prefab it was spawned from and its tree's identifiers then, or empty for one sent whole.</summary>
    // What it was built from, for peers to build their own copy, or null when it goes serialized.
    public SpawnTemplate? Template;

    public int NotifiedOwnerId = NoOwner;
    public int Owner = NoOwner;
    public bool SpawnNotified;
    public bool TearingDown;
    public Float3? TransferVelocity;
    public NetworkOrphaned Orphaned => GameObject.Network.Orphaned;

    /// <summary>Its networked components, fixed for the life of the spawn. A component's place here is its index on the wire.</summary>
    public Component[] Components { get; init; } = NetworkComponents.Gather(gameObject);

    private NetworkTransform[]? _transforms;

    /// <summary>
    ///     The NetworkTransform components in its tree, in tree order, fixed from the first time they are asked for. A
    ///     transform's place here is its index on the wire.
    /// </summary>
    public NetworkTransform[] Transforms
    {
        get
        {
            if (_transforms == null)
            {
                _transforms = NetworkComponents.GatherTransforms(GameObject);
                for (int i = 0; i < _transforms.Length; i++)
                {
                    _transforms[i].Entry = this;
                    _transforms[i].Index = (byte)i;
                    _transforms[i].SnapshotSlot = -1;
                    _transforms[i].HasSendTick = false;
                }
            }

            return _transforms;
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
