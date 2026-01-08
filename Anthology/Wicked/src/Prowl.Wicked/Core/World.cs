namespace Prowl.Wicked.Core;

/// <summary>
/// Represents the game world containing all networked entities.
/// Only one world can be active at a time.
/// </summary>
public class World
{
    private readonly Dictionary<uint, Entity> _entities = new();
    private readonly List<Entity> _pendingStart = new();
    private readonly List<Entity> _pendingDestroy = new();
    private bool _isUpdating;

    /// <summary>
    /// The currently active world.
    /// </summary>
    public static World? Active { get; private set; }

    /// <summary>
    /// All entities in the world, indexed by network ID.
    /// </summary>
    public IReadOnlyDictionary<uint, Entity> Entities => _entities;

    /// <summary>
    /// The number of entities in the world.
    /// </summary>
    public int EntityCount => _entities.Count;

    /// <summary>
    /// Creates a new world and sets it as active.
    /// </summary>
    public static World Create()
    {
        var world = new World();
        SetActive(world);
        return world;
    }

    /// <summary>
    /// Sets the active world.
    /// </summary>
    public static void SetActive(World? world)
    {
        Active = world;
    }

    /// <summary>
    /// Clears and destroys the active world.
    /// </summary>
    public static void Clear()
    {
        if (Active != null)
        {
            Active.DestroyAllEntities();
            Entity.ResetNetIdCounter();
        }
        Active = null;
    }

    /// <summary>
    /// Creates a new entity in this world.
    /// </summary>
    public Entity CreateEntity()
    {
        var entity = new Entity();
        entity.World = this;
        _entities[entity.NetId] = entity;

        if (_isUpdating)
        {
            _pendingStart.Add(entity);
        }

        return entity;
    }

    /// <summary>
    /// Creates a new entity with a specific network ID.
    /// Used when spawning entities from network messages.
    /// </summary>
    internal Entity CreateEntity(uint netId)
    {
        var entity = new Entity(netId);
        entity.World = this;
        _entities[entity.NetId] = entity;

        if (_isUpdating)
        {
            _pendingStart.Add(entity);
        }

        return entity;
    }

    /// <summary>
    /// Creates a new entity with a behaviour attached.
    /// </summary>
    public Entity CreateEntity<T>() where T : EntityBehaviour, new()
    {
        var entity = CreateEntity();
        entity.AddBehaviour<T>();
        return entity;
    }

    /// <summary>
    /// Spawns an entity into the world (makes it active).
    /// Sets up network state and calls lifecycle methods.
    /// </summary>
    public void Spawn(Entity entity, bool isServer, bool isClient, bool isLocalPlayer = false, bool isOwned = false)
    {
        entity.IsServer = isServer;
        entity.IsClient = isClient;
        entity.IsLocalPlayer = isLocalPlayer;
        entity.IsOwned = isOwned;

        entity.Start();
        entity.StartNetwork();
    }

    /// <summary>
    /// Destroys an entity and removes it from the world.
    /// </summary>
    public void DestroyEntity(Entity entity)
    {
        if (!_entities.ContainsKey(entity.NetId))
            return;

        if (_isUpdating)
        {
            _pendingDestroy.Add(entity);
        }
        else
        {
            entity.OnDestroy();
            _entities.Remove(entity.NetId);
            entity.World = null;
        }
    }

    /// <summary>
    /// Finds an entity by its network ID.
    /// </summary>
    public Entity? FindEntity(uint netId)
    {
        return _entities.TryGetValue(netId, out var entity) ? entity : null;
    }

    /// <summary>
    /// Gets all entities with a specific behaviour type.
    /// </summary>
    public IEnumerable<Entity> GetEntitiesWithBehaviour<T>() where T : EntityBehaviour
    {
        List<Entity> entities = new List<Entity>(_entities.Values);
        foreach (var entity in entities)
        {
            if (entity.GetBehaviour<T>() != null)
                yield return entity;
        }
    }

    /// <summary>
    /// Destroys all entities in the world.
    /// </summary>
    public void DestroyAllEntities()
    {
        List<Entity> entities = new List<Entity>(_entities.Values);
        foreach (var entity in entities)
        {
            entity.OnDestroy();
        }
        _entities.Clear();
        _pendingStart.Clear();
        _pendingDestroy.Clear();
    }

    // Update methods called by GameLoop

    internal void Update()
    {
        _isUpdating = true;

        // Start pending entities
        foreach (var entity in _pendingStart)
        {
            entity.Start();
        }
        _pendingStart.Clear();

        // Update all entities
        List<Entity> entities = new List<Entity>(_entities.Values);
        foreach (var entity in entities)
        {
            entity.Update();
        }

        _isUpdating = false;

        // Process pending destroys
        ProcessPendingDestroys();
    }

    internal void FixedUpdate()
    {
        _isUpdating = true;

        List<Entity> entities = new List<Entity>(_entities.Values);
        foreach (var entity in entities)
        {
            entity.FixedUpdate();
        }

        _isUpdating = false;

        ProcessPendingDestroys();
    }

    internal void LateUpdate()
    {
        _isUpdating = true;

        List<Entity> entities = new List<Entity>(_entities.Values);
        foreach (var entity in entities)
        {
            entity.LateUpdate();
        }

        _isUpdating = false;

        ProcessPendingDestroys();
    }

    private void ProcessPendingDestroys()
    {
        foreach (var entity in _pendingDestroy)
        {
            entity.OnDestroy();
            _entities.Remove(entity.NetId);
            entity.World = null;
        }
        _pendingDestroy.Clear();
    }
}
