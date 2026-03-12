namespace Prowl.Wicked;

/// <summary>
/// Abstract base class for all game entities: players, NPCs, items, projectiles, etc.
/// Entities live in maps, can be owned by clients, and support the full RPC system.
/// </summary>
public abstract class NetworkEntity : NetworkObject
{
    /// <summary>
    /// Unique identifier assigned by the server at spawn time.
    /// Consistent across server and all clients.
    /// </summary>
    public uint NetworkId { get; internal set; }

    /// <summary>
    /// The RemoteClient that owns this entity. On the server, always set to the owning
    /// RemoteClient instance (or null for unowned). On the client, only non-null when
    /// the local client is the owner (set to Client.LocalClient). For other players'
    /// entities on the client, Owner is null — use OwnerClientId to check ownership.
    /// </summary>
    public RemoteClient? Owner { get; internal set; }

    /// <summary>
    /// The ClientId of the owning client, or 0 if unowned. Always accurate on both
    /// server and client. Use this to distinguish "unowned NPC" (0) from "another
    /// player's entity" (nonzero) on the client side, where Owner is null for
    /// non-local owners.
    /// </summary>
    public uint OwnerClientId { get; internal set; }

    /// <summary>
    /// True on the client side if the local client is this entity's owner.
    /// Computed — always in sync even after ownership transfer.
    /// Always false on the server.
    /// </summary>
    public bool IsOwner => IsClient && Owner != null && Owner == Client.LocalClient;

    /// <summary>
    /// True if the entity is currently active on the network.
    /// </summary>
    public bool IsSpawned { get; internal set; }

    /// <summary>
    /// The map this entity currently belongs to.
    /// </summary>
    public Map? Map { get; internal set; }

    /// <summary>
    /// Writes entity-specific initial state into the spawn packet.
    /// Called on the server after the initializer runs but before the spawn message is sent
    /// to clients. Override to include custom state that clients need at spawn time
    /// (Name, Health, equipment, etc.).
    /// The networking runtime includes Owner in the spawn packet
    /// automatically. PackSpawnData only needs to write additional entity-specific state.
    /// </summary>
    public virtual void PackSpawnData(NetworkWriter writer) { }

    /// <summary>
    /// Reads entity-specific initial state from the spawn packet.
    /// Called on the client after NetworkId/Owner are set but before
    /// OnSpawn() fires. Read order must match PackSpawnData write order.
    /// </summary>
    public virtual void UnpackSpawnData(NetworkReader reader) { }

    /// <summary>Called when the entity enters the network (both server and client).</summary>
    public virtual void OnSpawn() { }

    /// <summary>Called when the entity leaves the network (both server and client).</summary>
    public virtual void OnDespawn() { }

    /// <summary>Called after spawn on the server side.</summary>
    public virtual void OnStartServer() { }

    /// <summary>Called before despawn on the server side.</summary>
    public virtual void OnStopServer() { }

    /// <summary>Called after spawn on the client side.</summary>
    public virtual void OnStartClient() { }

    /// <summary>Called before despawn on the client side.</summary>
    public virtual void OnStopClient() { }

    /// <summary>Called after OnStartClient on the owner's client.</summary>
    public virtual void OnStartOwner() { }

    /// <summary>Called before OnStopClient on the owner's client.</summary>
    public virtual void OnStopOwner() { }

    /// <summary>Called when the entity transfers between maps.</summary>
    public virtual void OnMapChanged(Map oldMap, Map newMap) { }

    /// <summary>Called when ownership changes via Server.ChangeOwner().</summary>
    public virtual void OnOwnerChanged(RemoteClient? oldOwner, RemoteClient? newOwner) { }

    /// <summary>Called every server tick for server-side game logic.</summary>
    public virtual void ServerTick() { }

    /// <summary>Called every client tick for client-side game logic (interpolation, prediction, effects).</summary>
    public virtual void ClientTick() { }
}
