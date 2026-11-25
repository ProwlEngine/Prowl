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

    // Send intervals saved in ticks by older versions, by component identity and index, until the components are read.
    private List<(Guid Component, int Index, int Ticks)>? _storedIntervals;

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

    internal void SetStoredIntervals(List<(Guid Component, int Index, int Ticks)> stored) =>
        _storedIntervals = stored.Count > 0 ? stored : null;

    /// <summary>Moves send intervals saved in ticks by older versions onto the behaviours they belong to, as seconds.</summary>
    internal void ApplyStoredIntervals()
    {
        if (_storedIntervals is not { } stored)
        {
            return;
        }

        _storedIntervals = null;
        List<Component> components = [.. _object.GetComponents<Component>()];
        bool anyMatched = false;
        List<(int Index, int Ticks)> unmatched = new();
        foreach ((Guid id, int index, int ticks) in stored)
        {
            // The component it was saved with, or the one copied from it, wherever it sits now.
            if (Matching(components, id) is { } match)
            {
                anyMatched = true;
                SetSeconds(match, ticks);
            }
            else
            {
                unmatched.Add((index, ticks));
            }
        }

        // Read with fresh identities, or saved before identities were stored, the components are where they were.
        if (!anyMatched)
        {
            foreach ((int index, int ticks) in unmatched)
            {
                if (index < components.Count)
                {
                    SetSeconds(components[index], ticks);
                }
            }
        }

        static void SetSeconds(Component component, int ticks)
        {
            if (component is NetworkBehaviour behaviour)
            {
                behaviour.SyncInterval = (float)(ticks * NetworkSystem.TickInterval);
            }
        }
    }

    private static Component? Matching(List<Component> components, Guid id)
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        foreach (Component component in components)
        {
            if (component.Identifier == id)
            {
                return component;
            }
        }

        foreach (Component component in components)
        {
            if (component.SourceIdentifier == id)
            {
                return component;
            }
        }

        return null;
    }

    /// <summary>
    ///     Which clients receive this object. Only the host may change this after spawning. It is read from the
    ///     networked object itself, not from the objects under it.
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
            system?.RebuildObservers(_object);
        }
    }

    /// <summary>Why the object left this peer. Read it in <see cref="NetworkBehaviour.OnStopClient" />.</summary>
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

    /// <summary>
    ///     Host: true if <paramref name="peer" /> has this object. Outside a session, always true. A client knows only
    ///     about itself: true for its own peer, false for any other.
    /// </summary>
    public bool IsVisibleTo(NetPeer peer) => Networking.System?.IsVisibleTo(_object, peer) ?? true;

    /// <summary>Host: asks the interest again who receives this object, sending or removing it where that changed.</summary>
    public void RebuildObservers() => Networking.System?.RebuildObservers(_object);

    internal const int MaxSendInterval = byte.MaxValue;

    internal void ApplyOrphaned(NetworkOrphaned value) => _orphaned = value;

    internal static bool NetworkedOf(GameObject go) => go._network?._networked == true;

    internal static bool ServerOnlyOf(GameObject go) => go._network?._serverOnly == true;

    /// <summary>The ticks between sends of a component's changed state, from its <see cref="NetworkBehaviour.SyncInterval" />.</summary>
    internal static int SendIntervalOf(Component component) =>
        component is NetworkBehaviour behaviour ? behaviour.IntervalTicks : 1;

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
