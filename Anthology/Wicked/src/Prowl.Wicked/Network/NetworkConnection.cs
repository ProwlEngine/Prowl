namespace Prowl.Wicked.Network;

using Prowl.Wicked.Core;
using Prowl.Wicked.Tools;

/// <summary>
/// Represents a network connection between server and client.
/// </summary>
public class NetworkConnection
{
    private readonly HashSet<uint> _ownedEntities = new();
    private readonly HashSet<Entity> _observing = new();

    // ping for rtt (round trip time)
    // useful for statistics, lag compensation, etc.
    double lastPingTime = 0;
    internal ExponentialMovingAverage _rtt = new ExponentialMovingAverage(NetworkTime.PingWindowSize);

    /// <summary>
    /// The unique identifier for this connection.
    /// Server uses this to identify clients. Host has ConnectionId = 0.
    /// </summary>
    public int ConnectionId { get; }

    /// <summary>
    /// True if this connection is ready to receive game state.
    /// </summary>
    public bool IsReady { get; internal set; }

    /// <summary>
    /// True if this connection is authenticated.
    /// </summary>
    public bool IsAuthenticated { get; internal set; }

    /// <summary>
    /// The IP address of the remote endpoint.
    /// </summary>
    public string Address { get; internal set; } = string.Empty;

    /// <summary>
    /// Network IDs of entities owned by this connection.
    /// </summary>
    public IReadOnlySet<uint> OwnedEntities => _ownedEntities;

    /// <summary>
    /// Entities this connection is currently observing (can see).
    /// Like Mirror's NetworkConnectionToClient.observing.
    /// </summary>
    public IReadOnlySet<Entity> Observing => _observing;

    /// <summary>
    /// The entity representing the player for this connection.
    /// </summary>
    public Core.Entity? PlayerEntity { get; internal set; }

    /// <summary>
    /// Round trip time (in seconds) that it takes a message to go server->client->server.
    /// </summary>
    public double rtt => _rtt.Value;

    /// <summary>
    /// Round trip time variance aka jitter, in seconds.
    /// </summary>
    public double rttVariance => _rtt.Variance;

    /// <summary>
    /// Round trip time standard deviation in seconds.
    /// </summary>
    public double rttStandardDeviation => _rtt.StandardDeviation;

    /// <summary>
    /// Last time a message was received from this connection.
    /// </summary>
    public double LastMessageTime { get; internal set; }

    /// <summary>
    /// Last time a ping was sent to this connection.
    /// </summary>
    internal double LastPingTime
    {
        get => lastPingTime;
        set => lastPingTime = value;
    }

    /// <summary>
    /// Creates a new network connection.
    /// </summary>
    public NetworkConnection(int connectionId)
    {
        ConnectionId = connectionId;
    }

    /// <summary>
    /// Adds an entity to the owned entities set.
    /// </summary>
    internal void AddOwnedEntity(uint netId)
    {
        _ownedEntities.Add(netId);
    }

    /// <summary>
    /// Removes an entity from the owned entities set.
    /// </summary>
    internal void RemoveOwnedEntity(uint netId)
    {
        _ownedEntities.Remove(netId);
    }

    /// <summary>
    /// Clears all owned entities.
    /// </summary>
    internal void ClearOwnedEntities()
    {
        _ownedEntities.Clear();
    }

    /// <summary>
    /// Adds an entity to observing and sends spawn message.
    /// Like Mirror's NetworkConnectionToClient.AddToObserving.
    /// </summary>
    internal void AddToObserving(Entity entity)
    {
        _observing.Add(entity);

        // Spawn entity for this connection
        NetworkServer.ShowForConnection(entity, this);
    }

    /// <summary>
    /// Removes an entity from observing and optionally sends despawn message.
    /// Like Mirror's NetworkConnectionToClient.RemoveFromObserving.
    /// </summary>
    /// <param name="entity">The entity to remove.</param>
    /// <param name="isDestroyed">If true, entity is being destroyed so no hide message needed.</param>
    internal void RemoveFromObserving(Entity entity, bool isDestroyed)
    {
        _observing.Remove(entity);

        if (!isDestroyed)
        {
            // Hide entity for this connection
            NetworkServer.HideForConnection(entity, this);
        }
    }

    /// <summary>
    /// Checks if this connection is observing an entity.
    /// </summary>
    internal bool IsObserving(Entity entity)
    {
        return _observing.Contains(entity);
    }

    /// <summary>
    /// Checks if this connection is observing an entity by NetId.
    /// </summary>
    internal bool IsObserving(uint netId)
    {
        foreach (var entity in _observing)
        {
            if (entity.NetId == netId)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Removes this connection from all entities' observer lists.
    /// Called when connection disconnects.
    /// </summary>
    internal void RemoveFromObservingsObservers()
    {
        foreach (var entity in _observing)
        {
            entity.RemoveObserver(this);
        }
        _observing.Clear();
    }

    /// <summary>
    /// Clears all observing entities.
    /// </summary>
    internal void ClearObserving()
    {
        _observing.Clear();
    }

    /// <summary>
    /// Checks if a message has been received within the timeout period.
    /// </summary>
    internal bool IsAlive(float timeout)
    {
        return Core.GameLoop.Time - LastMessageTime < timeout;
    }

    /// <summary>
    /// Performs cleanup when the connection is being removed.
    /// </summary>
    internal virtual void Cleanup()
    {
        _ownedEntities.Clear();
        _observing.Clear();
        PlayerEntity = null;
        IsReady = false;
    }

    /// <summary>
    /// Disconnects this connection.
    /// </summary>
    public virtual void Disconnect()
    {
        // Base implementation does nothing
        // Derived classes (LocalConnection) may override
    }

    public override string ToString()
    {
        return $"Connection(Id={ConnectionId}, Address={Address}, Ready={IsReady})";
    }
}
