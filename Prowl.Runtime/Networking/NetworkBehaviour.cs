// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Courier;
using Prowl.Echo;

namespace Prowl.Runtime.Networking;

/// <summary>
///     The base of a networked script: one that has [Sync] properties, RPCs, custom state or network callbacks. It sits
///     on a networked object or on any object under one, and needs nothing else on the object. A networked object holds
///     at most <see cref="NetworkComponents.MaxPerObject" /> of them.
/// </summary>
public abstract class NetworkBehaviour : Component
{
    /// <summary>Who writes this behaviour's state.</summary>
    public SyncDirection SyncDirection = SyncDirection.ServerToClient;

    /// <summary>Who receives this behaviour's state.</summary>
    public SyncMode SyncMode = SyncMode.Observers;

    /// <summary>The least seconds between sends of this behaviour's changed state. 0 sends changes on the next tick.</summary>
    [Range(0, 2)] public float SyncInterval;

    // The networked object it is part of while spawned, and its place among that object's behaviours.
    [SerializeIgnore] internal NetworkObject? Entry;
    [SerializeIgnore] internal int Index = -1;

    /// <summary>The id of the networked object this is part of, the same on every peer, or 0 while not spawned.</summary>
    public uint NetId => Spawned is { } entry ? entry.NetId : 0;

    /// <summary>Its place among the networked object's behaviours, the same on every peer, or -1 while not spawned.</summary>
    public int ComponentIndex => Spawned != null ? Index : -1;

    /// <summary>True on the host while the object is spawned.</summary>
    public bool IsServer => Networking.System is { IsActive: true, IsHost: true } system && system.IsNetworked(GameObject);

    /// <summary>True on every peer that has the object spawned, the host included, which is a client of its own too.</summary>
    public bool IsClient => Networking.System is { IsActive: true } system && system.IsNetworked(GameObject);

    /// <summary>True when this peer owns the object.</summary>
    public bool IsOwner => Networking.System is { IsActive: true } system && system.FindEntry(GameObject) is { } entry &&
                           system.Session?.LocalPeer is { } local && entry.Owner == local.Id;

    /// <summary>
    ///     True when this peer writes this behaviour's state. On the host: <see cref="SyncDirection.ServerToClient" />, or
    ///     when the host owns the object. On a client: <see cref="SyncDirection.ClientToServer" /> on an object it owns.
    /// </summary>
    public bool Authority
    {
        get
        {
            if (!IsClient)
            {
                return false;
            }

            return IsServer
                ? SyncDirection == SyncDirection.ServerToClient || IsOwner
                : SyncDirection == SyncDirection.ClientToServer && IsOwner;
        }
    }

    /// <summary>True when the object this is part of is this peer's player.</summary>
    public bool IsLocalPlayer => Networking.System?.LocalPlayer is { } player && player == NetworkAccessor.RootOf(GameObject);

    /// <summary>The peer that owns the object, or null when the host controls it or it is not spawned.</summary>
    public NetPeer? Owner => Networking.System?.GetOwner(GameObject);

    /// <summary>The object was spawned on the host. Runs before <see cref="OnStartClient" />.</summary>
    public virtual void OnStartServer()
    {
    }

    /// <summary>The object is being despawned on the host. Runs after <see cref="OnStopClient" />.</summary>
    public virtual void OnStopServer()
    {
    }

    /// <summary>
    ///     The object arrived on this peer, with its state in place, the host included. On a joining client it runs once
    ///     every object of the join has arrived, while <see cref="Networking.IsReady" /> is still false, so an RPC to the
    ///     host from here is dropped: send it from <see cref="INetworkListener.OnNetworkReady" />.
    /// </summary>
    public virtual void OnStartClient()
    {
    }

    /// <summary>The object is leaving this peer: destroyed, out of interest, or the session ended. Read <see cref="NetworkAccessor.DespawnReason" /> for which.</summary>
    public virtual void OnStopClient()
    {
    }

    /// <summary>The object became this peer's player. Runs after <see cref="OnStartAuthority" />.</summary>
    public virtual void OnStartLocalPlayer()
    {
    }

    /// <summary>The object stopped being this peer's player.</summary>
    public virtual void OnStopLocalPlayer()
    {
    }

    /// <summary>This peer came to own the object, at spawn or when it was handed over.</summary>
    public virtual void OnStartAuthority()
    {
    }

    /// <summary>This peer stopped owning the object.</summary>
    public virtual void OnStopAuthority()
    {
    }

    /// <summary>The object's owner changed. Runs between <see cref="OnStopAuthority" /> and <see cref="OnStartAuthority" />.</summary>
    public virtual void OnOwnerChanged(NetPeer? previousOwner, NetPeer? owner)
    {
    }

    /// <summary>
    ///     Writes state of the behaviour's own, for state that packs better by hand than as [Sync] values. The engine
    ///     writes the [Sync] values itself, so calling base is not needed. <paramref name="initial" /> is true whenever
    ///     all of it is needed: for a spawn, and for a new owner. Otherwise it follows <see cref="SetDirty" /> and may
    ///     write only what changed, since state travels reliably and in order.
    /// </summary>
    public virtual void OnSerialize(NetWriter writer, bool initial)
    {
    }

    /// <summary>Reads what <see cref="OnSerialize" /> wrote, with the <paramref name="initial" /> it was written with.</summary>
    public virtual void OnDeserialize(ref NetReader reader, bool initial)
    {
    }

    /// <summary>Sends every [Sync] value and the custom state again at the next send. Does nothing where <see cref="Authority" /> doesn't hold.</summary>
    public void SetDirty()
    {
        if (Spawned is { } entry && Authority)
        {
            entry.System.Sync.MarkAllDirty(this);
        }
    }

    /// <summary>The networked object it is part of, while it is spawned and this behaviour is one of its own.</summary>
    internal NetworkObject? Spawned => Entry is { TearingDown: false } entry ? entry : null;

    /// <summary>The sync rules as the engine reads them.</summary>
    internal SyncFlags Flags =>
        (SyncDirection == SyncDirection.ClientToServer ? SyncFlags.FromOwner : SyncFlags.None) |
        (SyncMode == SyncMode.Owner ? SyncFlags.ToOwner : SyncFlags.None);

    /// <summary>The ticks between sends of changed state, from <see cref="SyncInterval" />, at least 1.</summary>
    internal int IntervalTicks
    {
        get
        {
            if (!float.IsFinite(SyncInterval) || SyncInterval <= 0)
            {
                return 1;
            }

            double ticks = Math.Ceiling(SyncInterval / NetworkSystem.TickInterval - 1e-6);
            return (int)Math.Clamp(ticks, 1, NetworkAccessor.MaxSendInterval);
        }
    }
}
