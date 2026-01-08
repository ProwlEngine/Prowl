namespace Prowl.Wicked.Core;

using Prowl.Wicked.Network;

/// <summary>
/// Represents a networked entity in the game world.
/// Equivalent to Unity's NetworkIdentity - the fundamental unit of network synchronization.
/// </summary>
public class Entity
{
    private static uint _nextNetId = 1;

    private readonly List<EntityBehaviour> _behaviours = new();
    private readonly Dictionary<int, NetworkConnection> _observers = new();
    private bool _hasStarted;
    private bool _isDestroyed;

    // Lifecycle flags to prevent double-calling (like Mirror's clientStarted)
    private bool _serverStarted;
    private bool _clientStarted;
    private bool _localPlayerStarted;
    private bool _authorityStarted;

    /// <summary>
    /// Unique network identifier for this entity.
    /// </summary>
    public uint NetId { get; internal set; }

    /// <summary>
    /// True if this entity exists on the server.
    /// </summary>
    public bool IsServer { get; internal set; }

    /// <summary>
    /// True if this entity exists on a client.
    /// </summary>
    public bool IsClient { get; internal set; }

    /// <summary>
    /// True if running in host mode (server + client).
    /// </summary>
    public bool IsHost => IsServer && IsClient;

    /// <summary>
    /// True if this entity exists only on the server.
    /// </summary>
    public bool IsServerOnly => IsServer && !IsClient;

    /// <summary>
    /// True if this entity exists only on a client.
    /// </summary>
    public bool IsClientOnly => IsClient && !IsServer;

    /// <summary>
    /// True if this entity represents the local player.
    /// </summary>
    public bool IsLocalPlayer { get; internal set; }

    /// <summary>
    /// True if the local connection has authority over this entity.
    /// </summary>
    public bool IsOwned { get; internal set; }

    /// <summary>
    /// The network connection that owns this entity (null if server-owned).
    /// </summary>
    public NetworkConnection? Owner { get; internal set; }

    /// <summary>
    /// The world this entity belongs to.
    /// </summary>
    public World? World { get; internal set; }

    /// <summary>
    /// All behaviours attached to this entity.
    /// </summary>
    public IReadOnlyList<EntityBehaviour> Behaviours => _behaviours;

    /// <summary>
    /// All connections observing this entity (server only).
    /// Like Mirror's NetworkIdentity.observers.
    /// </summary>
    public IReadOnlyDictionary<int, NetworkConnection> Observers => _observers;

    /// <summary>
    /// Creates a new entity with an auto-assigned network ID.
    /// </summary>
    internal Entity()
    {
        NetId = _nextNetId++;
    }

    /// <summary>
    /// Creates a new entity with a specific network ID.
    /// </summary>
    internal Entity(uint netId)
    {
        NetId = netId;
        if (netId >= _nextNetId)
            _nextNetId = netId + 1;
    }

    /// <summary>
    /// Resets the network ID counter. Used when clearing the world.
    /// </summary>
    internal static void ResetNetIdCounter()
    {
        _nextNetId = 1;
    }

    /// <summary>
    /// Returns true if any behaviour on this entity has dirty sync data.
    /// </summary>
    public bool IsDirty
    {
        get
        {
            foreach (var behaviour in _behaviours)
            {
                if (behaviour.IsDirty)
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Clears all dirty flags on all behaviours. Called after sync data is sent.
    /// </summary>
    internal void ClearAllDirty()
    {
        foreach (var behaviour in _behaviours)
        {
            behaviour.ClearDirty();
        }
    }

    // ============ Observer Management (like Mirror's NetworkIdentity) ============

    /// <summary>
    /// Adds an observer (connection that can see this entity).
    /// Called by the interest management system.
    /// </summary>
    internal void AddObserver(NetworkConnection conn)
    {
        if (_observers.ContainsKey(conn.ConnectionId))
            return;

        // If we previously had no observers, clear all dirty bits.
        // Changes while no one was watching don't matter - first observer gets full spawn.
        // (Like Mirror's optimization in AddObserver)
        if (_observers.Count == 0)
        {
            ClearAllDirty();
        }

        _observers[conn.ConnectionId] = conn;
    }

    /// <summary>
    /// Removes an observer from this entity.
    /// </summary>
    internal void RemoveObserver(NetworkConnection conn)
    {
        _observers.Remove(conn.ConnectionId);
    }

    /// <summary>
    /// Checks if a connection is observing this entity.
    /// </summary>
    internal bool HasObserver(NetworkConnection conn)
    {
        return _observers.ContainsKey(conn.ConnectionId);
    }

    /// <summary>
    /// Clears all observers. Called when entity is despawned.
    /// </summary>
    internal void ClearObservers()
    {
        _observers.Clear();
    }

    /// <summary>
    /// Gets the internal observers dictionary for direct manipulation by InterestManagement.
    /// </summary>
    internal Dictionary<int, NetworkConnection> ObserversInternal => _observers;

    /// <summary>
    /// Transfers ownership of this entity to another connection.
    /// Only valid on the server.
    /// </summary>
    /// <param name="newOwner">The new owner, or null for server ownership.</param>
    public void TransferOwnership(NetworkConnection? newOwner)
    {
        if (!IsServer)
            throw new InvalidOperationException("Ownership can only be transferred on the server.");

        var previousOwner = Owner;

        // Update the owner's OwnedEntities set
        if (previousOwner != null)
        {
            previousOwner.RemoveOwnedEntity(NetId);
        }

        Owner = newOwner;

        if (newOwner != null)
        {
            newOwner.AddOwnedEntity(NetId);
        }

        // Update IsOwned flag (for host mode only - client sees via message)
        // Host connection ID is 0
        bool wasOwned = IsOwned;
        bool willBeOwned = IsHost && newOwner?.ConnectionId == 0;

        if (wasOwned && !willBeOwned)
        {
            // Host is losing authority
            IsOwned = false;
            NotifyAuthorityLost();
        }
        else if (!wasOwned && willBeOwned)
        {
            // Host is gaining authority
            IsOwned = true;
            NotifyAuthorityGained();
        }
        else
        {
            // No change in authority for host, just update the flag
            IsOwned = willBeOwned;
        }

        // Notify behaviours of ownership transfer (use ToArray to avoid enumeration issues)
        foreach (var behaviour in _behaviours.ToArray())
        {
            try
            {
                behaviour.OnOwnershipTransferred(previousOwner);
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Entity {NetId}] Exception in OnOwnershipTransferred: {e}");
            }
        }

        // Send ownership change message to all observers
        NetworkServer.BroadcastOwnershipChange(this);
    }

    /// <summary>
    /// Removes ownership from this entity (makes it server-owned).
    /// </summary>
    public void RemoveOwnership() => TransferOwnership(null);

    /// <summary>
    /// Adds a behaviour to this entity.
    /// Can only be called before the entity is spawned (before IsServer or IsClient is set).
    /// </summary>
    public T AddBehaviour<T>() where T : EntityBehaviour, new()
    {
        if (IsServer || IsClient)
            throw new InvalidOperationException("Cannot add EntityBehaviour after spawning!");

        var behaviour = new T();
        behaviour.Entity = this;
        behaviour.BehaviourIndex = (byte)_behaviours.Count;
        _behaviours.Add(behaviour);

        try { behaviour.OnAwake(); }
        catch (Exception e) { Console.WriteLine($"[Entity {NetId}] Exception in OnAwake: {e}"); }

        return behaviour;
    }

    /// <summary>
    /// Adds a pre-created behaviour to this entity.
    /// Can only be called before the entity is spawned (before IsServer or IsClient is set).
    /// </summary>
    internal void AddBehaviour(EntityBehaviour behaviour)
    {
        if (IsServer || IsClient)
            throw new InvalidOperationException("Cannot add EntityBehaviour after spawning!");

        behaviour.Entity = this;
        behaviour.BehaviourIndex = (byte)_behaviours.Count;
        _behaviours.Add(behaviour);

        try { behaviour.OnAwake(); }
        catch (Exception e) { Console.WriteLine($"[Entity {NetId}] Exception in OnAwake: {e}"); }
    }

    /// <summary>
    /// Gets the first behaviour of the specified type.
    /// </summary>
    public T? GetBehaviour<T>() where T : EntityBehaviour
    {
        foreach (var behaviour in _behaviours)
        {
            if (behaviour is T typed)
                return typed;
        }
        return null;
    }

    /// <summary>
    /// Gets all behaviours of the specified type.
    /// </summary>
    public IEnumerable<T> GetBehaviours<T>() where T : EntityBehaviour
    {
        foreach (var behaviour in _behaviours)
        {
            if (behaviour is T typed)
                yield return typed;
        }
    }

    // Lifecycle methods - called by World

    internal void Awake()
    {
        foreach (var behaviour in _behaviours)
        {
            behaviour.OnAwake();
        }
    }

    internal void Start()
    {
        if (_hasStarted) return;
        _hasStarted = true;

        foreach (var behaviour in _behaviours)
        {
            try
            {
                behaviour.OnStart();
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Entity {NetId}] Exception in OnStart: {e}");
            }
        }
    }

    internal void StartNetwork()
    {
        // OnStartServer - only call once per entity lifetime
        if (IsServer && !_serverStarted)
        {
            _serverStarted = true;
            foreach (var behaviour in _behaviours)
            {
                try
                {
                    behaviour.OnStartServer();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[Entity {NetId}] Exception in OnStartServer: {e}");
                }
            }
        }

        // OnStartClient - only call once per entity lifetime (like Mirror's clientStarted)
        if (IsClient && !_clientStarted)
        {
            _clientStarted = true;
            foreach (var behaviour in _behaviours)
            {
                try
                {
                    behaviour.OnStartClient();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[Entity {NetId}] Exception in OnStartClient: {e}");
                }
            }
        }

        // OnStartLocalPlayer - only call once per entity lifetime
        if (IsLocalPlayer && !_localPlayerStarted)
        {
            _localPlayerStarted = true;
            foreach (var behaviour in _behaviours)
            {
                try
                {
                    behaviour.OnStartLocalPlayer();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[Entity {NetId}] Exception in OnStartLocalPlayer: {e}");
                }
            }
        }

        // OnStartAuthority - only call once per ownership gain
        if (IsOwned && !_authorityStarted)
        {
            _authorityStarted = true;
            foreach (var behaviour in _behaviours)
            {
                try
                {
                    behaviour.OnStartAuthority();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"[Entity {NetId}] Exception in OnStartAuthority: {e}");
                }
            }
        }
    }

    internal void Update()
    {
        if (_isDestroyed) return;

        foreach (var behaviour in _behaviours)
        {
            behaviour.OnUpdate();
        }
    }

    internal void FixedUpdate()
    {
        if (_isDestroyed) return;

        foreach (var behaviour in _behaviours)
        {
            behaviour.OnFixedUpdate();
        }
    }

    internal void LateUpdate()
    {
        if (_isDestroyed) return;

        foreach (var behaviour in _behaviours)
        {
            behaviour.OnLateUpdate();
        }
    }

    internal void OnDestroy()
    {
        if (_isDestroyed) return;
        _isDestroyed = true;

        foreach (var behaviour in _behaviours)
        {
            // Only call OnStopServer if OnStartServer was called
            if (_serverStarted)
            {
                try { behaviour.OnStopServer(); }
                catch (Exception e) { Console.WriteLine($"[Entity {NetId}] Exception in OnStopServer: {e}"); }
            }

            // Only call OnStopClient if OnStartClient was called (like Mirror)
            if (_clientStarted)
            {
                try { behaviour.OnStopClient(); }
                catch (Exception e) { Console.WriteLine($"[Entity {NetId}] Exception in OnStopClient: {e}"); }
            }

            // Only call OnStopAuthority if OnStartAuthority was called
            if (_authorityStarted)
            {
                try { behaviour.OnStopAuthority(); }
                catch (Exception e) { Console.WriteLine($"[Entity {NetId}] Exception in OnStopAuthority: {e}"); }
            }

            try { behaviour.OnDestroy(); }
            catch (Exception e) { Console.WriteLine($"[Entity {NetId}] Exception in OnDestroy: {e}"); }
        }

        _behaviours.Clear();
    }

    /// <summary>
    /// Gets a behaviour by its index.
    /// </summary>
    public EntityBehaviour? GetBehaviourByIndex(byte index)
    {
        if (index < _behaviours.Count)
            return _behaviours[index];
        return null;
    }

    /// <summary>
    /// Called when this entity gains authority.
    /// Used for ownership transfer to properly trigger OnStartAuthority.
    /// </summary>
    internal void NotifyAuthorityGained()
    {
        if (_authorityStarted) return;
        _authorityStarted = true;

        foreach (var behaviour in _behaviours)
        {
            try
            {
                behaviour.OnStartAuthority();
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Entity {NetId}] Exception in OnStartAuthority: {e}");
            }
        }
    }

    /// <summary>
    /// Called when this entity loses authority.
    /// Used for ownership transfer to properly trigger OnStopAuthority.
    /// </summary>
    internal void NotifyAuthorityLost()
    {
        if (!_authorityStarted) return;
        _authorityStarted = false;

        foreach (var behaviour in _behaviours)
        {
            try
            {
                behaviour.OnStopAuthority();
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Entity {NetId}] Exception in OnStopAuthority: {e}");
            }
        }
    }

    /// <summary>
    /// Called when this entity becomes the local player.
    /// </summary>
    internal void NotifyLocalPlayerStarted()
    {
        if (_localPlayerStarted) return;
        _localPlayerStarted = true;

        foreach (var behaviour in _behaviours)
        {
            try
            {
                behaviour.OnStartLocalPlayer();
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Entity {NetId}] Exception in OnStartLocalPlayer: {e}");
            }
        }
    }

    /// <summary>
    /// Called when this entity is no longer the local player.
    /// </summary>
    internal void NotifyLocalPlayerStopped()
    {
        if (!_localPlayerStarted) return;
        _localPlayerStarted = false;

        foreach (var behaviour in _behaviours)
        {
            try
            {
                behaviour.OnStopLocalPlayer();
            }
            catch (Exception e)
            {
                Console.WriteLine($"[Entity {NetId}] Exception in OnStopLocalPlayer: {e}");
            }
        }
    }

    /// <summary>
    /// Resets lifecycle flags. Used when recycling entities or on clear.
    /// </summary>
    internal void ResetLifecycleFlags()
    {
        _serverStarted = false;
        _clientStarted = false;
        _localPlayerStarted = false;
        _authorityStarted = false;
    }
}
