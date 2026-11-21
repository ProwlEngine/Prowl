// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.Courier;
using Prowl.Vector;

namespace Prowl.Runtime.Networking;

/// <summary>Networking configuration and operations for a GameObject. Created only when accessed.</summary>
public sealed class NetworkAccessor
{
    private readonly GameObject _object;
    private bool _networked;
    private bool _serverOnly;
    private NetworkOrphaned _orphaned = NetworkOrphaned.Destroy;
    private NetworkVisibility _visibility;
    private float _cullDistance;
    private NetworkGroups _groups;

    // The send interval of each component of this object that has one set.
    private Dictionary<Component, int>? _componentIntervals;

    // Intervals as stored, by component index, until the object's components are there to take them.
    private List<(int Index, int Ticks)>? _storedIntervals;

    internal NetworkAccessor(GameObject owner) => _object = owner;

    /// <summary>
    ///     Makes the object a networked object: kept in sync for its whole life, with
    ///     everything below it part of it. Spawning sets it. Configure before the session starts for scene objects:
    ///     setting it during one spawns nothing, and it can't be cleared while the object is spawned.
    /// </summary>
    public bool Networked
    {
        get => _networked;
        set
        {
            if (!value && _networked && IsSpawnedRoot)
            {
                throw new InvalidOperationException(
                    $"'{_object.Name}' is spawned. Despawn it before taking it off the network.");
            }

            _networked = value;
        }
    }

    /// <summary>
    ///     The object and everything below it never leave the host. It is for a
    ///     whole networked object or anything outside one: on a part of a networked object it does nothing, since
    ///     every peer builds the whole object. It can't be set on an object that is spawned, or part of one.
    /// </summary>
    public bool ServerOnly
    {
        get => _serverOnly;
        set
        {
            if (value && !_serverOnly && Active)
            {
                throw new InvalidOperationException(
                    $"'{_object.Name}' is spawned, or part of an object that is. Despawn it before making it server only.");
            }

            _serverOnly = value;
        }
    }

    private bool IsSpawnedRoot =>
        Networking.System is { IsActive: true } system && system.FindEntry(_object) is { } entry && entry.GameObject == _object;

    /// <summary>What a saved object holds, taken as it is.</summary>
    internal void LoadSharing(bool networked, bool serverOnly) => (_networked, _serverOnly) = (networked, serverOnly);

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

    /// <summary>
    ///     How many ticks apart a component on this object sends its changed state, [Sync] values and its own
    ///     <see cref="INetworkSerialize" /> state alike, from 1 (every tick) to 255. It is this
    ///     peer's own setting for the state it writes, and is never sent. Call it on the object the component is on.
    /// </summary>
    public void SetSendInterval(Component component, int ticks)
    {
        ArgumentNullException.ThrowIfNull(component);
        if (ticks < 1 || ticks > MaxSendInterval)
        {
            throw new ArgumentOutOfRangeException(nameof(ticks));
        }

        if (component.GameObject != _object)
        {
            throw new ArgumentException("The component is not on this object.", nameof(component));
        }

        TakeStoredIntervals();
        if (ticks == 1)
        {
            _componentIntervals?.Remove(component);
        }
        else
        {
            (_componentIntervals ??= new Dictionary<Component, int>())[component] = ticks;
        }
    }

    internal bool HasComponentIntervals => TakeStoredIntervals() is { Count: > 0 };

    internal void ClearComponentIntervals() => (_componentIntervals, _storedIntervals) = (null, null);

    internal void SetStoredIntervals(List<(int Index, int Ticks)> stored) =>
        _storedIntervals = stored.Count > 0 ? stored : null;

    // The intervals by component, once the stored ones found theirs.
    private Dictionary<Component, int>? TakeStoredIntervals()
    {
        if (_storedIntervals is not { } stored)
        {
            return _componentIntervals;
        }

        _storedIntervals = null;
        List<Component> components = [.. _object.GetComponents<Component>()];
        foreach ((int index, int ticks) in stored)
        {
            if (index < components.Count)
            {
                (_componentIntervals ??= new Dictionary<Component, int>())[components[index]] = ticks;
            }
        }

        return _componentIntervals;
    }

    /// <summary>The ticks between sends of a component's changed state. See <see cref="SetSendInterval" />.</summary>
    public int GetSendInterval(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return SendIntervalOf(component);
    }

    /// <summary>
    ///     Which clients receive this object. Only the host may change this after spawning. It is read from the
    ///     networked object itself, not from the objects under it, as are <see cref="Groups" /> and
    ///     <see cref="CullDistance" />.
    /// </summary>
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

    /// <summary>The groups as their bits, for the editor to store as a prefab override.</summary>
    internal ulong GroupMask
    {
        get => _groups.Mask;
        set => Groups = new NetworkGroups(value);
    }

    /// <summary>Why the object left this peer. Read it in <see cref="INetworkEvents.OnNetworkDespawn" />.</summary>
    public NetworkDespawnReason DespawnReason { get; internal set; }

    /// <summary>True while this object belongs to a live network session.</summary>
    public bool Active => Networking.System?.IsNetworked(_object) == true;

    /// <summary>The id of the networked object this is or is part of, the same on every peer, or 0 while not spawned.</summary>
    public uint NetId => Networking.System?.FindEntry(_object)?.NetId ?? 0;

    /// <summary>True when the object is this peer's player.</summary>
    public bool IsLocalPlayer => Networking.System?.LocalPlayer == _object;

    /// <summary>True when this peer receives this object's state from its controller.</summary>
    public bool IsProxy => Networking.System?.IsProxy(_object) == true;

    /// <summary>
    ///     True when this peer drives this spawned object: it owns it, or it is the host and nobody does. False for
    ///     an object that is not spawned, which is no proxy either.
    /// </summary>
    public bool HasControl =>
        Networking.System is { IsActive: true } system && system.FindEntry(_object) is { TearingDown: false } entry &&
        system.HasControl(entry);

    /// <summary>The owning peer, or null when the host controls the object or it is not networked.</summary>
    public NetPeer? Owner => Networking.System?.GetOwner(_object);

    /// <summary>The networked object this one is, or is part of, or null.</summary>
    public GameObject? Root => RootOf(_object);

    internal bool IsExcluded => IsExcludedFromNetwork(_object);

    /// <summary>
    ///     Moves the object to a world pose at once, so peers jump there instead of blending across. Only the peer
    ///     that controls it may, while it is networked. It tags what the <see cref="NetworkTransform" /> that moves the
    ///     networked object itself sends. For a child with a transform of its own, use
    ///     <see cref="NetworkTransform.Teleport" /> on that one.
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

            if (entry.RootTransform is { } transform)
            {
                transform.Sync.Teleport();
            }
        }

        _object.Transform.Position = position;
        _object.Transform.Rotation = rotation;
    }

    /// <summary>What a spawn outside a session leaves on the object: it is networked, with its orphaned policy.</summary>
    internal void MarkSpawned(NetworkOrphaned? orphaned)
    {
        NetworkOrphaned policy = orphaned ?? Orphaned;
        if (!Enum.IsDefined(policy))
        {
            throw new ArgumentOutOfRangeException(nameof(orphaned));
        }

        Networked = true;
        Orphaned = policy;
    }

    /// <summary>
    ///     Host: assigns ownership to a peer, or to the host's simulation with null.
    ///     Only the host changes ownership: a client that wants an object asks with a host RPC. It throws on a client,
    ///     for an object that is not a spawned networked object, and for a player object given to anyone but the peer
    ///     it is the player of. Outside a session it does nothing.
    /// </summary>
    public void AssignOwnership(NetPeer? owner) => Networking.System?.SetOwner(_object, owner);

    /// <summary>
    ///     Host: the object goes back to the host's simulation. The same as
    ///     <see cref="AssignOwnership" /> with null.
    /// </summary>
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

    internal const int MaxSendInterval = byte.MaxValue;

    internal void ApplyOrphaned(NetworkOrphaned value) => _orphaned = value;

    internal static bool NetworkedOf(GameObject go) => go._network?._networked == true;

    internal static bool ServerOnlyOf(GameObject go) => go._network?._serverOnly == true;

    /// <summary>The ticks between sends of a component's [Sync] state: its own, or 1 when it has none set.</summary>
    internal static int SendIntervalOf(Component component) =>
        component.GameObject._network?.TakeStoredIntervals() is { } intervals &&
        intervals.TryGetValue(component, out int interval)
            ? interval
            : 1;

    internal static bool IsExcludedFromNetwork(GameObject go)
    {
        // Looked for from the networked object up: on a part of one it means nothing.
        GameObject? root = RootOf(go);
        for (GameObject? current = root.IsValid() ? root : go; current.IsValid(); current = current.Parent)
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
