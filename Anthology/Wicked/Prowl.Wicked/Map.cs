namespace Prowl.Wicked;

/// <summary>
/// Abstract container for entities. Maps define observation scope —
/// all clients whose PlayerEntity is in a given map observe all entities in that map.
/// Subclass to add map-specific RPCs (weather, zone events, etc.).
/// </summary>
public abstract class Map : NetworkObject
{
    private readonly Dictionary<uint, NetworkEntity> _entityById = new();
    private readonly HashSet<NetworkEntity> _entities = new();
    private readonly HashSet<RemoteClient> _observers = new();

    /// <summary>
    /// Unique identifier set at creation time via Server.CreateMap().
    /// </summary>
    public Guid MapId { get; internal set; }

    /// <summary>
    /// All entities currently in this map.
    /// </summary>
    public IReadOnlyCollection<NetworkEntity> Entities => _entities;

    /// <summary>
    /// All clients currently observing this map.
    /// </summary>
    public IReadOnlyCollection<RemoteClient> Observers => _observers;

    /// <summary>
    /// Adds an entity to this map. Called internally by the spawn/transfer system.
    /// </summary>
    internal void AddEntity(NetworkEntity entity)
    {
        _entities.Add(entity);
        _entityById[entity.NetworkId] = entity;
        OnEntityEnter(entity);
    }

    /// <summary>
    /// Removes an entity from this map. Called internally by the despawn/transfer system.
    /// </summary>
    internal void RemoveEntity(NetworkEntity entity)
    {
        _entities.Remove(entity);
        _entityById.Remove(entity.NetworkId);
        OnEntityLeave(entity);
    }

    /// <summary>
    /// Adds a client as an observer of this map.
    /// </summary>
    internal void AddObserver(RemoteClient client)
    {
        _observers.Add(client);
        if (IsServer)
            Server.SendObserverEnter(client, this);
        OnObserverEnter(client);
    }

    /// <summary>
    /// Removes a client from this map's observers.
    /// </summary>
    internal void RemoveObserver(RemoteClient client)
    {
        if (IsServer)
            Server.SendObserverLeave(client, this);
        _observers.Remove(client);
        OnObserverLeave(client);
    }

    /// <summary>
    /// Transfers an entity from this map to another map atomically.
    /// Fires OnEntityLeave on the source, OnEntityEnter on the target, and OnMapChanged on the entity.
    /// For player entities, also transfers the owning client's observation from this map to the target map.
    /// </summary>
    public void TransferEntity(NetworkEntity entity, Map targetMap)
    {
        if (!IsServer)
            throw new InvalidOperationException("TransferEntity can only be called on the server.");
        if (targetMap == null)
            throw new ArgumentNullException(nameof(targetMap));
        if (!entity.IsSpawned)
            throw new InvalidOperationException("Cannot transfer a despawned entity.");
        if (entity.Map != this)
            throw new InvalidOperationException("Entity is not in this map.");
        if (targetMap == this)
            throw new InvalidOperationException("Cannot transfer an entity to the same map.");

        var oldMap = this;

        // If this is a player entity, transfer the owner's observation scope
        if (entity.Owner != null && entity.Owner.PlayerEntity == entity)
        {
            oldMap.RemoveObserver(entity.Owner);
        }

        // Send despawn to remaining observers of old map
        Server.SendEntityDespawnToObservers(entity, oldMap);

        RemoveEntity(entity);
        entity.Map = targetMap;
        targetMap.AddEntity(entity);

        // Send spawn to existing observers of new map (before adding transferring owner)
        Server.SendEntitySpawnToObservers(entity, targetMap);

        // Add the owner as an observer of the new map
        if (entity.Owner != null && entity.Owner.PlayerEntity == entity)
        {
            targetMap.AddObserver(entity.Owner);
            Server.SendPlayerEntityAssign(entity.Owner, entity);
        }

        entity.OnMapChanged(oldMap, targetMap);
    }

    /// <summary>
    /// Finds an entity in this map by NetworkId. Returns null if not found.
    /// </summary>
    public T? FindEntity<T>(uint networkId) where T : NetworkEntity
    {
        return _entityById.GetValueOrDefault(networkId) as T;
    }

    /// <summary>
    /// Returns all entities of type T in this map.
    /// </summary>
    public IEnumerable<T> GetEntities<T>() where T : NetworkEntity
    {
        return _entities.OfType<T>();
    }

    /// <summary>
    /// Removes all entities and observers. Called during map destruction.
    /// </summary>
    internal void Clear()
    {
        _entities.Clear();
        _entityById.Clear();
        _observers.Clear();
    }

    /// <summary>Called when the map is created via Server.CreateMap().</summary>
    public virtual void OnCreated() { }

    /// <summary>Called when the map is destroyed via Server.DestroyMap().</summary>
    public virtual void OnDestroyed() { }

    /// <summary>Called when an entity is added to this map.</summary>
    public virtual void OnEntityEnter(NetworkEntity entity) { }

    /// <summary>Called when an entity is removed from this map.</summary>
    public virtual void OnEntityLeave(NetworkEntity entity) { }

    /// <summary>Called when a client begins observing this map.</summary>
    public virtual void OnObserverEnter(RemoteClient client) { }

    /// <summary>Called when a client stops observing this map.</summary>
    public virtual void OnObserverLeave(RemoteClient client) { }
}
