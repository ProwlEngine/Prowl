// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Courier;
using Prowl.Vector;

namespace Prowl.Runtime.Networking;

/// <summary>Networking configuration and operations for a GameObject. Created only when accessed.</summary>
public sealed class NetworkAccessor
{
    private readonly GameObject _object;
    private NetworkFlags _flags;
    private int _sendInterval = 1;
    private bool _networked;
    private bool _serverOnly;
    private NetworkOrphaned _orphaned = NetworkOrphaned.Destroy;
    private NetworkVisibility _visibility;
    private float _cullDistance;
    private NetworkGroups _groups;

    internal NetworkAccessor(GameObject owner) => _object = owner;

    /// <summary>
    ///     Makes the object a networked object, as Mirror's NetworkIdentity: kept in sync for its whole life, with
    ///     everything below it part of it. Spawning sets it. Configure before the session starts for scene objects.
    /// </summary>
    public bool Networked
    {
        get => _networked;
        set => _networked = value;
    }

    /// <summary>The object and everything below it never leave the host, as Mirror's serverOnly.</summary>
    public bool ServerOnly
    {
        get => _serverOnly;
        set => _serverOnly = value;
    }

    /// <summary>The old three way setting, kept until scene content stops going over the wire.</summary>
    [Obsolete("Use Networked and ServerOnly.")]
    public NetworkMode Mode
    {
        get => _serverOnly ? NetworkMode.Never : _networked ? NetworkMode.Object : NetworkMode.Snapshot;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _networked = value == NetworkMode.Object;
            _serverOnly = value == NetworkMode.Never;
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

    /// <summary>
    ///     How many ticks apart the object's moving transform and changed [Sync] values go out, from 1 (every tick)
    ///     to 255. Only the host may change this after spawning.
    /// </summary>
    public int SendInterval
    {
        get => _sendInterval;
        set
        {
            if (value < 1 || value > MaxSendInterval)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (value == _sendInterval)
            {
                return;
            }

            Networking.System?.SetSendInterval(_object, value);
            _sendInterval = value;
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

    /// <summary>
    ///     How far from a viewpoint a client still receives this object under <see cref="InterestMode.Distance" />, in place
    ///     of the viewpoint's own radius. Zero leaves it to the viewpoint.
    /// </summary>
    public float CullDistance
    {
        get => _cullDistance;
        set
        {
            if (!float.IsFinite(value) || value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            _cullDistance = value;
        }
    }

    /// <summary>
    ///     The groups whose clients receive this object, such as a team. None sends it to every client. Only the host may
    ///     change this after spawning.
    /// </summary>
    public NetworkGroups Groups
    {
        get => _groups;
        set
        {
            if (value == _groups)
            {
                return;
            }

            NetworkSystem? system = Networking.System;
            system?.CheckVisibilityChange(_object);
            _groups = value;
            system?.RefreshVisibility(_object);
        }
    }

    /// <summary>Why the object left this peer. Read it in <see cref="INetworkEvents.OnNetworkDespawn" />.</summary>
    public NetworkDespawnReason DespawnReason { get; internal set; }

    /// <summary>True while this object belongs to a live network session.</summary>
    public bool Active => Networking.System?.IsNetworked(_object) == true;

    /// <summary>True when the object is this peer's player, as Mirror's isLocalPlayer.</summary>
    public bool IsLocalPlayer => Networking.System?.LocalPlayer == _object;

    /// <summary>True when this peer receives this object's state from its controller.</summary>
    public bool IsProxy => Networking.System?.IsProxy(_object) == true;

    /// <summary>The owning peer, or null when the host controls the object or it is not networked.</summary>
    public NetPeer? Owner => Networking.System?.GetOwner(_object);

    /// <summary>The networked object this one is, or is part of, or null.</summary>
    public GameObject? Root => RootOf(_object);

    // Static objects don't move, so there is nothing to sync.
    internal bool HasTransformSync =>
        !_object.IsStatic && (Flags & NetworkFlags.NoTransformSync) != NetworkFlags.NoTransformSync;

    internal bool IsExcluded => IsExcludedFromNetwork(_object);

    /// <summary>
    ///     Moves the object to a world pose at once, so peers jump there instead of blending across. Only the peer
    ///     that controls it may, while it is networked.
    /// </summary>
    public void Teleport(Float3 position, Quaternion rotation)
    {
        if (Networking.System is { IsActive: true } system && system.FindEntry(_object) is { } entry)
        {
            if (entry.GameObject != _object)
            {
                throw new InvalidOperationException("Teleport the networked object's root, not a part of it.");
            }

            if (!system.HasControl(_object))
            {
                throw new InvalidOperationException("Only the peer that controls a networked object can teleport it.");
            }

            entry.TransformSync.Teleport();
        }

        _object.Transform.Position = position;
        _object.Transform.Rotation = rotation;
    }

    /// <summary>Host: spawns the tree for all peers, or assigns ownership if already spawned.</summary>
    public void SpawnSerialized(NetPeer? owner = null, NetworkOrphaned? orphaned = null)
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
            Networked = true;
            Orphaned = policy;
        }
    }

    /// <summary>
    ///     Host: assigns ownership to a peer, or to the host's simulation with null, as Mirror's AssignClientAuthority.
    ///     Only the host changes ownership: a client that wants an object asks with a host RPC.
    /// </summary>
    public void AssignOwnership(NetPeer? owner) => Networking.System?.SetOwner(_object, owner);

    /// <summary>Host: the object goes back to the host's simulation, as Mirror's RemoveClientAuthority.</summary>
    public void RemoveOwnership() => Networking.System?.SetOwner(_object, null);

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
    internal const int MaxSendInterval = byte.MaxValue;

    internal void ApplyFlags(NetworkFlags value) => _flags = value;
    internal void ApplySendInterval(int value) => _sendInterval = value;
    internal void ApplyOrphaned(NetworkOrphaned value) => _orphaned = value;

    internal static bool ValidFlags(NetworkFlags value) =>
        (value & ~(NetworkFlags.NoTransformSync | NetworkFlags.NoInterpolation)) == 0;

    internal static bool NetworkedOf(GameObject go) => go._network?._networked == true;

    internal static bool ServerOnlyOf(GameObject go) => go._network?._serverOnly == true;

    internal static int SendIntervalOf(GameObject go) => go._network?.SendInterval ?? 1;

    internal static bool IsExcludedFromNetwork(GameObject go)
    {
        for (GameObject? current = go; current.IsValid(); current = current.Parent)
        {
            if (ServerOnlyOf(current))
            {
                return true;
            }
        }

        return false;
    }

    // The outermost one: a networked object inside another is part of it.
    internal static GameObject? RootOf(GameObject go)
    {
        GameObject? root = null;
        for (GameObject? current = go; current.IsValid(); current = current.Parent)
        {
            if (NetworkedOf(current))
            {
                root = current;
            }
        }

        return root;
    }
}
