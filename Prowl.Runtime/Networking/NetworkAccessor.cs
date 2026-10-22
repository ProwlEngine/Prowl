// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Warp;

namespace Prowl.Runtime.Networking;

/// <summary>Networking configuration and operations for a GameObject. Created only when accessed.</summary>
public sealed class NetworkAccessor
{
    private readonly GameObject _object;
    private NetworkMode _mode = NetworkMode.Snapshot;
    private OwnerTransfer _ownerTransfer = OwnerTransfer.Fixed;
    private NetworkOrphaned _orphaned = NetworkOrphaned.Destroy;
    private NetworkFlags _flags;

    internal NetworkAccessor(GameObject owner) => _object = owner;

    /// <summary>How the object participates in a session. Configure before spawning.</summary>
    public NetworkMode Mode
    {
        get => _mode;
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (value == _mode) return;
            Networking.AssertHierarchyEditable(_object);
            _mode = value;
        }
    }

    /// <summary>Who may take ownership. Only the host may change this after spawning.</summary>
    public OwnerTransfer OwnerTransfer
    {
        get => _ownerTransfer;
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (value == _ownerTransfer) return;
            Networking.SetOwnerTransfer(_object, value);
            _ownerTransfer = value;
        }
    }

    /// <summary>What happens when the owner leaves. Only the host may change this after spawning.</summary>
    public NetworkOrphaned Orphaned
    {
        get => _orphaned;
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (value == _orphaned) return;
            Networking.SetOrphaned(_object, value);
            _orphaned = value;
        }
    }

    /// <summary>Options that disable transform channels or interpolation. Only the host may change these after spawning.</summary>
    public NetworkFlags Flags
    {
        get => _flags;
        set
        {
            if (!ValidFlags(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (value == _flags) return;
            Networking.SetNetworkFlags(_object, value);
            _flags = value;
        }
    }

    /// <summary>True while this object belongs to a live network session.</summary>
    public bool Active => Networking.IsNetworked(_object);

    /// <summary>True when this peer receives this object's state from its controller.</summary>
    public bool IsProxy => Networking.IsProxy(_object);

    /// <summary>The owning peer, or null when the host controls the object or it is not networked.</summary>
    public NetPeer? Owner => Networking.GetOwner(_object);

    /// <summary>The nearest network root at or above this object, or null.</summary>
    public GameObject? Root => RootOf(_object);

    /// <summary>Host: spawns the tree for all peers, or assigns ownership if already spawned.</summary>
    public void Spawn(NetPeer? owner = null, NetworkOrphaned? orphaned = null) =>
        Networking.Spawn(_object, owner, orphaned ?? Orphaned);

    /// <summary>Host: assigns ownership to a peer, or to the host's simulation with null.</summary>
    public void AssignOwnership(NetPeer? owner) => Networking.SetOwner(_object, owner);

    /// <summary>Attempts to take ownership. Wait for authority gained before simulating.</summary>
    public bool TakeOwnership() => Networking.TransferOwnership(_object, take: true);

    /// <summary>Relinquishes ownership to the host's simulation.</summary>
    public bool DropOwnership() => Networking.TransferOwnership(_object, take: false);

    internal bool HasTransformSync => (Flags & NetworkFlags.NoTransformSync) != NetworkFlags.NoTransformSync;
    internal bool SyncsTransform(NetworkFlags exclusion) => (Flags & exclusion) == 0;
    internal void ApplyFlags(NetworkFlags value) => _flags = value;
    internal void ApplyOwnerTransfer(OwnerTransfer value) => _ownerTransfer = value;
    internal void ApplyOrphaned(NetworkOrphaned value) => _orphaned = value;
    internal static bool ValidFlags(NetworkFlags value) =>
        (value & ~(NetworkFlags.NoTransformSync | NetworkFlags.NoInterpolation)) == 0;

    internal bool IsExcluded => IsExcludedFromNetwork(_object);
    internal static NetworkMode ModeOf(GameObject go) => go._network?.Mode ?? NetworkMode.Snapshot;

    internal static bool IsExcludedFromNetwork(GameObject go)
    {
        for (GameObject? current = go; current.IsValid(); current = current.Parent)
            if (ModeOf(current) == NetworkMode.Never) return true;
        return false;
    }

    internal static GameObject? RootOf(GameObject go)
    {
        for (GameObject? current = go; current.IsValid(); current = current.Parent)
            if (ModeOf(current) == NetworkMode.Object) return current;
        return null;
    }
}
