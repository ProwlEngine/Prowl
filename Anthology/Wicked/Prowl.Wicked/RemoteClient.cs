namespace Prowl.Wicked;

/// <summary>
/// Represents a connected client. On the server, each connected player has a RemoteClient.
/// On the client, Client.LocalClient is the local player's RemoteClient.
/// </summary>
public sealed class RemoteClient
{
    /// <summary>
    /// Which side of the network this instance lives on.
    /// </summary>
    public NetworkSide Side { get; internal set; } = NetworkSide.Unspecified;

    /// <summary>
    /// True if this instance lives on the server side.
    /// </summary>
    public bool IsServer => Side == NetworkSide.Server;

    /// <summary>
    /// True if this instance lives on the client side.
    /// </summary>
    public bool IsClient => Side == NetworkSide.Client;

    /// <summary>
    /// Unique identifier for this client connection, assigned by the server.
    /// </summary>
    public uint ClientId { get; internal set; }

    /// <summary>
    /// The transport-level connection identifier. Used internally to route
    /// disconnect calls through the transport layer. Set when the transport
    /// connection is accepted.
    /// </summary>
    internal uint ConnectionId { get; set; }

    /// <summary>
    /// True if this client has an active connection.
    /// </summary>
    public bool IsConnected { get; internal set; }

    /// <summary>
    /// The entity assigned to this client via AssignPlayerEntity().
    /// Null before assignment and after UnassignPlayerEntity().
    /// </summary>
    public NetworkEntity? PlayerEntity { get; internal set; }

    /// <summary>
    /// True when PlayerEntity is not null.
    /// </summary>
    public bool HasPlayerEntity => PlayerEntity != null;

    /// <summary>
    /// Arbitrary user data attached to this client. Use this for login state,
    /// session info, or any per-client data that was previously stored in
    /// RemoteClient subclass fields.
    /// </summary>
    public object? UserData { get; set; }

    /// <summary>
    /// True after the server has accepted this client's authentication token.
    /// </summary>
    public bool IsAuthenticated { get; internal set; }

    /// <summary>
    /// The authenticated user's unique identifier (e.g., Supabase UID).
    /// Set by Server.AcceptAuthentication().
    /// </summary>
    public string? UserId { get; internal set; }

    /// <summary>
    /// The raw authentication token sent by the client. Server-only, internal use.
    /// </summary>
    internal string? AuthToken { get; set; }

    /// <summary>
    /// Server-side timestamp of the last data received from this client.
    /// Used for connection timeout detection.
    /// </summary>
    internal double LastReceivedTime { get; set; }

    /// <summary>
    /// Assigns a spawned entity as this client's player entity.
    /// Triggers map observation — the client begins seeing entities in the entity's map.
    /// Must be called on the server.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if called when server is not active, the entity is not spawned, not in a map,
    /// not owned by this client, or if this client already has a player entity assigned.
    /// </exception>
    public void AssignPlayerEntity(NetworkEntity entity)
    {
        if (!Server.Active)
            throw new InvalidOperationException("AssignPlayerEntity can only be called on the server.");
        if (!entity.IsSpawned)
            throw new InvalidOperationException("Entity must be spawned before assigning as player entity.");
        if (entity.Map == null)
            throw new InvalidOperationException("Entity must be in a map before assigning as player entity.");
        if (entity.Owner != this)
            throw new InvalidOperationException("Entity must be owned by this client.");
        if (HasPlayerEntity)
            throw new InvalidOperationException("Client already has a player entity assigned. Call UnassignPlayerEntity() first.");

        PlayerEntity = entity;

        // Add this client as an observer of the entity's map
        entity.Map.AddObserver(this);

        // Notify the client so it can set LocalClient.PlayerEntity
        Server.SendPlayerEntityAssign(this, entity);

        Server.FirePlayerEntityAssigned(this, entity);
    }

    /// <summary>
    /// Removes the player entity assignment. The client stops observing the map.
    /// Does not despawn the entity.
    /// </summary>
    public void UnassignPlayerEntity()
    {
        if (!Server.Active)
            throw new InvalidOperationException("UnassignPlayerEntity can only be called on the server.");

        var entity = PlayerEntity;
        PlayerEntity = null;

        // Notify the client before observer removal tears down the map
        Server.SendPlayerEntityUnassign(this);

        // Remove this client from the map's observers
        if (entity?.Map != null)
            entity.Map.RemoveObserver(this);

        if (entity != null)
            Server.FirePlayerEntityUnassigned(this, entity);
    }

    /// <summary>
    /// Forcefully disconnects this client. Server-only — sends a disconnect message
    /// and closes the connection. Use Client.Disconnect() on the client side.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if called when server is not active.</exception>
    public void Disconnect(string? reason = null)
    {
        if (!Server.Active)
            throw new InvalidOperationException("RemoteClient.Disconnect() can only be called on the server. Use Client.Disconnect() on the client side.");

        // RemoveClient calls DisconnectClientInternal which handles:
        // IsConnected=false, OnClientDisconnected, UnassignPlayerEntity
        Server.RemoveClient(this);
        Server.Transport?.Disconnect(ConnectionId, reason);
    }
}
