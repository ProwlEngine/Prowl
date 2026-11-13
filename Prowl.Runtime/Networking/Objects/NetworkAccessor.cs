// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Courier;

namespace Prowl.Runtime.Networking;

/// <summary>Networking configuration and operations for a GameObject. Created only when accessed.</summary>
public sealed class NetworkAccessor
{
    private readonly GameObject _object;
    private NetworkFlags _flags;
    private NetworkMode _mode = NetworkMode.Snapshot;
    private NetworkOrphaned _orphaned = NetworkOrphaned.Destroy;
    private OwnerTransfer _ownerTransfer = OwnerTransfer.Fixed;
    private NetworkVisibility _visibility;

    internal NetworkAccessor(GameObject owner) => _object = owner;

    /// <summary>How the object participates in a session. Configure before spawning.</summary>
    public NetworkMode Mode
    {
        get => _mode;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (value == _mode)
            {
                return;
            }

            _mode = value;
        }
    }

    /// <summary>Who may take ownership. Only the host may change this after spawning.</summary>
    public OwnerTransfer OwnerTransfer
    {
        get => _ownerTransfer;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (value == _ownerTransfer)
            {
                return;
            }

            Networking.System?.SetOwnerTransfer(_object, value);
            _ownerTransfer = value;
        }
    }

    /// <summary>What happens when the owner leaves. Only the host may change this after spawning.</summary>
    public NetworkOrphaned Orphaned
    {
        get => _orphaned;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (value == _orphaned)
            {
                return;
            }

            Networking.System?.SetOrphaned(_object, value);
            _orphaned = value;
        }
    }

    /// <summary>Options that disable transform channels or interpolation. Only the host may change these after spawning.</summary>
    public NetworkFlags Flags
    {
        get => _flags;
        set
        {
            if (!ValidFlags(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (value == _flags)
            {
                return;
            }

            Networking.System?.SetNetworkFlags(_object, value);
            _flags = value;
        }
    }

    /// <summary>Which clients receive this object. Only the host may change this after spawning.</summary>
    public NetworkVisibility Visibility
    {
        get => _visibility;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (value == _visibility)
            {
                return;
            }

            NetworkSystem? system = Networking.System;
            system?.CheckVisibilityChange(_object);
            _visibility = value;
            system?.RefreshVisibility(_object);
        }
    }

    /// <summary>Why the object left this peer. Read it in <see cref="INetworkEvents.OnNetworkDespawn" />.</summary>
    public NetworkDespawnReason DespawnReason { get; internal set; }

    /// <summary>True while this object belongs to a live network session.</summary>
    public bool Active => Networking.System?.IsNetworked(_object) == true;

    /// <summary>True when this peer receives this object's state from its controller.</summary>
    public bool IsProxy => Networking.System?.IsProxy(_object) == true;

    /// <summary>The owning peer, or null when the host controls the object or it is not networked.</summary>
    public NetPeer? Owner => Networking.System?.GetOwner(_object);

    /// <summary>The nearest network root at or above this object, or null.</summary>
    public GameObject? Root => RootOf(_object);

    // Static objects don't move, so there is nothing to sync.
    internal bool HasTransformSync =>
        !_object.IsStatic && (Flags & NetworkFlags.NoTransformSync) != NetworkFlags.NoTransformSync;

    internal bool IsExcluded => IsExcludedFromNetwork(_object);

    /// <summary>Host: spawns the tree for all peers, or assigns ownership if already spawned.</summary>
    public void Spawn(NetPeer? owner = null, NetworkOrphaned? orphaned = null)
    {
        NetworkOrphaned policy = orphaned ?? Orphaned;
        if (!Enum.IsDefined(policy))
        {
            throw new ArgumentOutOfRangeException(nameof(orphaned));
        }

        if (Networking.System is { } system)
        {
            system.Spawn(_object, owner, policy);
        }
        else
        {
            Mode = NetworkMode.Object;
            Orphaned = policy;
        }
    }

    /// <summary>Host: assigns ownership to a peer, or to the host's simulation with null.</summary>
    public void AssignOwnership(NetPeer? owner) => Networking.System?.SetOwner(_object, owner);

    /// <summary>Attempts to take ownership. Wait for authority gained before simulating.</summary>
    public bool TakeOwnership() => Networking.System?.TransferOwnership(_object, true) == true;

    /// <summary>Relinquishes ownership to the host's simulation.</summary>
    public bool DropOwnership() => Networking.System?.TransferOwnership(_object, false) == true;

    /// <summary>Host: true if <paramref name="peer" /> has this object. Outside a session, always true.</summary>
    public bool IsVisibleTo(NetPeer peer) => Networking.System?.IsVisibleTo(_object, peer) ?? true;

    /// <summary>Host: sends this object to <paramref name="peer" />, whatever <see cref="Visibility" /> says. Ignored once the peer left.</summary>
    public void ShowTo(NetPeer peer) => VisibilityOverride(peer, true);

    /// <summary>Host: stops sending this object to <paramref name="peer" />. Ignored for its owner, or once the peer left.</summary>
    public void HideFrom(NetPeer peer) => VisibilityOverride(peer, false);

    /// <summary>Host: <see cref="Visibility" /> decides for <paramref name="peer" /> again. Ignored once the peer left.</summary>
    public void ClearVisibilityOverride(NetPeer peer) => VisibilityOverride(peer, null);

    /// <summary>Host: checks every peer again, sending or removing the object where that changed.</summary>
    public void RefreshVisibility() => Networking.System?.RefreshVisibility(_object);

    private void VisibilityOverride(NetPeer peer, bool? visible)
    {
        ArgumentNullException.ThrowIfNull(peer);
        if (Networking.System is not { IsActive: true } system)
        {
            throw new InvalidOperationException("Visibility can only be overridden on the host of a running session.");
        }

        system.SetVisibilityOverride(_object, peer, visible);
    }

    internal bool SyncsTransform(NetworkFlags exclusion) => (Flags & exclusion) == 0;
    internal void ApplyFlags(NetworkFlags value) => _flags = value;
    internal void ApplyOwnerTransfer(OwnerTransfer value) => _ownerTransfer = value;
    internal void ApplyOrphaned(NetworkOrphaned value) => _orphaned = value;

    internal static bool ValidFlags(NetworkFlags value) =>
        (value & ~(NetworkFlags.NoTransformSync | NetworkFlags.NoInterpolation)) == 0;

    internal static NetworkMode ModeOf(GameObject go) => go._network?.Mode ?? NetworkMode.Snapshot;

    internal static bool IsExcludedFromNetwork(GameObject go)
    {
        for (GameObject? current = go; current.IsValid(); current = current.Parent)
        {
            if (ModeOf(current) == NetworkMode.Never)
            {
                return true;
            }
        }

        return false;
    }

    internal static GameObject? RootOf(GameObject go)
    {
        for (GameObject? current = go; current.IsValid(); current = current.Parent)
        {
            if (ModeOf(current) == NetworkMode.Object)
            {
                return current;
            }
        }

        return null;
    }
}
