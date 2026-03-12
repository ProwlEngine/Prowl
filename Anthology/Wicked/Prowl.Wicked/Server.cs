using System.Diagnostics;
using System.Numerics;
using Prowl.Wicked.Transport;

namespace Prowl.Wicked;

/// <summary>
/// Static server entry point. Manages client connections, entity spawning, maps, and the server tick loop.
/// Only one server can be active per process.
/// </summary>
public static class Server
{
    private static readonly List<RemoteClient> _clients = new();
    private static readonly Dictionary<uint, RemoteClient> _clientsByConnection = new();
    private static readonly Dictionary<Guid, Map> _maps = new();
    private static readonly Dictionary<uint, NetworkEntity> _entities = new();
    private static uint _nextNetworkId = 1;
    private static uint _nextClientId = 1;
    private static readonly Stopwatch _tickStopwatch = new();
    private static readonly Stopwatch _serverClock = new();

    // Type registries
    private static readonly Dictionary<Type, ushort> _entityTypeToId = new();
    private static readonly Dictionary<ushort, Func<NetworkEntity>> _entityIdToFactory = new();
    private static ushort _nextEntityTypeId = 1;
    private static readonly Dictionary<Type, ushort> _mapTypeToId = new();
    private static readonly Dictionary<ushort, Func<Map>> _mapIdToFactory = new();
    private static ushort _nextMapTypeId = 1;

    // Static RPC registry
    private static readonly Dictionary<ushort, Action<ushort, NetworkReader, uint, ushort>> _staticCommandDispatchers = new();

    /// <summary>
    /// True after Start() and before Stop() completes.
    /// </summary>
    public static bool Active { get; private set; }

    /// <summary>
    /// The transport layer used for network communication.
    /// Set before calling Start() for custom transports.
    /// </summary>
    public static IServerTransport? Transport { get; set; }

    /// <summary>
    /// Target ticks per second. Advisory — used by game code for fixed-step timing.
    /// DeltaTime is computed from elapsed wall-clock time, not derived from TickRate.
    /// </summary>
    public static float TickRate { get; set; } = 60f;

    /// <summary>
    /// Time in seconds since the last Server.Tick().
    /// Updated at the start of every Tick(). Use this inside ServerTick() methods
    /// for frame-rate-independent logic.
    /// </summary>
    public static float DeltaTime { get; internal set; }

    /// <summary>
    /// Monotonically increasing seconds since Start(). Driven by a dedicated
    /// Stopwatch that runs for the entire server lifetime (unlike the per-tick
    /// _tickStopwatch which restarts each tick).
    /// </summary>
    public static double Time => _serverClock.Elapsed.TotalSeconds;

    /// <summary>
    /// Seconds of silence before a client is auto-disconnected.
    /// Default 6 seconds (3x client PingInterval). Set to 0 to disable.
    /// </summary>
    public static float ConnectionTimeout { get; set; } = 6f;

    /// <summary>
    /// All currently connected clients.
    /// </summary>
    public static IReadOnlyCollection<RemoteClient> Clients => _clients;

    /// <summary>
    /// All currently active maps.
    /// </summary>
    public static IReadOnlyCollection<Map> Maps => _maps.Values;

    /// <summary>
    /// Starts the server, listening on the specified port.
    /// </summary>
    public static void Start(int port)
    {
        if (Active)
            throw new InvalidOperationException("Server is already active. Call Stop() before calling Start() again.");

        RegisterAllTypes();
        Active = true;
        _tickStopwatch.Restart();
        _serverClock.Restart();

        // Default to TCP if no transport was set
        Transport ??= new Transport.TcpServerTransport();

        // Wire up transport events
        Transport.OnClientConnected += HandleTransportClientConnected;
        Transport.OnClientDisconnected += HandleTransportClientDisconnected;
        Transport.OnDataReceived += HandleTransportDataReceived;
        Transport.Listen(port);

        OnStarted?.Invoke();
    }

    private static void HandleTransportClientConnected(uint connectionId)
    {
        var client = new RemoteClient();
        client.Side = NetworkSide.Server;
        if (_nextClientId == 0)
            throw new OverflowException("ClientId overflow: all uint IDs have been exhausted.");
        client.ClientId = _nextClientId++;
        client.ConnectionId = connectionId;
        client.IsConnected = true;
        client.LastReceivedTime = Time;
        _clients.Add(client);
        _clientsByConnection[connectionId] = client;

        // Send the assigned ClientId to the client
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.AssignClientId);
        writer.WriteUInt(client.ClientId);
        Transport!.Send(connectionId, writer.ToArraySegment());

        OnClientConnected?.Invoke(client);
    }

    private static void HandleTransportClientDisconnected(uint connectionId, string? reason)
    {
        if (!_clientsByConnection.TryGetValue(connectionId, out var client)) return;

        DisconnectClientInternal(client);
        _clients.Remove(client);
        _clientsByConnection.Remove(connectionId);
    }

    /// <remarks>
    /// Void ServerRpcs (promiseId == 0) that throw will log the exception and then
    /// propagate to the outer catch, which disconnects the client with "malformed message".
    /// Promise-based RPCs send an RpcError to the caller instead.
    /// </remarks>
    private static void HandleTransportDataReceived(uint connectionId, ArraySegment<byte> data)
    {
        if (_clientsByConnection.TryGetValue(connectionId, out var senderClient))
            senderClient.LastReceivedTime = Time;

        try
        {
            var reader = new NetworkReader(data);
            byte msgType = reader.ReadByte();

            switch (msgType)
            {
                case MessageType.RpcCall:
                    HandleRpcCall(connectionId, reader);
                    break;
                case MessageType.Ping:
                    HandlePing(connectionId, reader);
                    break;
                case MessageType.Authenticate:
                    HandleAuthenticate(connectionId, reader);
                    break;
            }
        }
        catch (Exception)
        {
            // Malformed message — disconnect the offending client
            Transport?.Disconnect(connectionId, "malformed message");
        }
    }

    private static void HandleRpcCall(uint connectionId, NetworkReader reader)
    {
        byte objectKind = reader.ReadByte();

        var sender = FindClientByConnection(connectionId);
        if (sender == null) return;

        switch (objectKind)
        {
            case 0: // Entity
            {
                uint networkId = reader.ReadUInt();
                var entity = FindEntity(networkId);
                if (entity == null || entity.Map == null || !entity.Map.Observers.Contains(sender))
                    return;

                ushort methodId = reader.ReadUShort();
                ushort promiseId = reader.ReadUShort();

                NetworkObject.Sender = sender;
                try
                {
                    entity.__DispatchServerRpc(methodId, reader, connectionId, promiseId);
                }
                catch (Exception ex) when (promiseId == 0)
                {
                    Console.Error.WriteLine(
                        $"[Prowl.Wicked] Unhandled exception in void EntityCommand on {entity.GetType().Name} " +
                        $"(connectionId={connectionId}): {ex.Message}");
                }
                finally
                {
                    NetworkObject.Sender = null;
                }
                break;
            }
            case 2: // Static
            {
                ushort rpcTypeId = reader.ReadUShort();
                ushort methodId = reader.ReadUShort();
                ushort promiseId = reader.ReadUShort();

                if (!_staticCommandDispatchers.TryGetValue(rpcTypeId, out var dispatcher))
                    return;

                NetworkObject.Sender = sender;
                try
                {
                    dispatcher(methodId, reader, connectionId, promiseId);
                }
                catch (Exception ex) when (promiseId == 0)
                {
                    Console.Error.WriteLine(
                        $"[Prowl.Wicked] Unhandled exception in void StaticCommand (typeId={rpcTypeId}) " +
                        $"(connectionId={connectionId}): {ex.Message}");
                }
                finally
                {
                    NetworkObject.Sender = null;
                }
                break;
            }
        }
    }

    private static void HandlePing(uint connectionId, NetworkReader reader)
    {
        double clientTimestamp = reader.ReadDouble();
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.Pong);
        writer.WriteDouble(clientTimestamp);
        writer.WriteDouble(Time);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    internal static RemoteClient? FindClientByConnection(uint connectionId)
    {
        return _clientsByConnection.GetValueOrDefault(connectionId);
    }

    /// <summary>
    /// Stops the server. Order: disconnect all clients → despawn all entities → destroy all maps.
    /// OnClientDisconnected fires for each client while PlayerEntity is still accessible, so handlers
    /// can safely access client.PlayerEntity and perform cleanup. Then all entities are despawned (lifecycle
    /// callbacks fire), then maps are destroyed.
    /// </summary>
    public static void Stop()
    {
        // 1. Disconnect all clients using shared disconnect logic
        // (fires OnClientDisconnected for each — PlayerEntity still accessible)
        // Skip per-client Transport.Disconnect since Transport.Stop() handles that below.
        for (int i = _clients.Count - 1; i >= 0; i--)
        {
            var client = _clients[i];
            DisconnectClientInternal(client);
        }
        _clients.Clear();
        _clientsByConnection.Clear();

        // 2. Despawn all entities via the Despawn() method for consistent lifecycle
        foreach (var entity in _entities.Values.ToArray())
        {
            Despawn(entity);
        }

        // 3. Destroy all maps (OnDestroyed fires before cleanup)
        foreach (var map in _maps.Values.ToArray())
        {
            map.OnDestroyed();
            map.Clear();
        }
        _maps.Clear();

        // 4. Fire stopped event, set inactive, unwire transport, stop transport
        OnStopped?.Invoke();
        Active = false;
        _tickStopwatch.Stop();
        _serverClock.Stop();

        if (Transport != null)
        {
            Transport.OnClientConnected -= HandleTransportClientConnected;
            Transport.OnClientDisconnected -= HandleTransportClientDisconnected;
            Transport.OnDataReceived -= HandleTransportDataReceived;
            Transport.Stop();
        }
    }

    /// <summary>
    /// Resets all static state. Essential for test isolation — clears event subscriptions,
    /// clients, maps, entities, and transport. Call between tests.
    /// </summary>
    public static void Reset()
    {
        Active = false;
        DeltaTime = 0f;
        Transport = null;
        TickRate = 60f;
        _clients.Clear();
        _clientsByConnection.Clear();
        _maps.Clear();
        _entities.Clear();
        _nextNetworkId = 1;
        _nextClientId = 1;
        _entityTypeToId.Clear();
        _entityIdToFactory.Clear();
        _nextEntityTypeId = 1;
        _mapTypeToId.Clear();
        _mapIdToFactory.Clear();
        _nextMapTypeId = 1;
        _staticCommandDispatchers.Clear();
        _registered = false;
        _tickStopwatch.Reset();
        _serverClock.Reset();
        ConnectionTimeout = 6f;
        OnClientConnected = null;
        OnClientDisconnected = null;
        OnPlayerEntityAssigned = null;
        OnPlayerEntityUnassigned = null;
        OnStarted = null;
        OnStopped = null;
        OnClientAuthenticate = null;
    }

    /// <summary>
    /// Creates a new map with an auto-generated GUID and adds it to the server.
    /// </summary>
    public static T CreateMap<T>() where T : Map, new()
    {
        return CreateMap<T>(Guid.NewGuid());
    }

    /// <summary>
    /// Creates a new map with the given GUID and adds it to the server.
    /// </summary>
    public static T CreateMap<T>(Guid mapId) where T : Map, new()
    {
        if (_maps.ContainsKey(mapId))
            throw new InvalidOperationException($"A map with ID '{mapId}' already exists.");

        var map = new T();
        map.MapId = mapId;
        map.Side = NetworkSide.Server;
        _maps[mapId] = map;
        map.OnCreated();
        return map;
    }

    /// <summary>
    /// Retrieves a map by its ID. Returns null if not found.
    /// </summary>
    public static Map? GetMap(Guid mapId)
    {
        return _maps.GetValueOrDefault(mapId);
    }

    /// <summary>
    /// Destroys a map. Fires OnDestroyed() first, then despawns all entities within it.
    /// </summary>
    public static void DestroyMap(Guid mapId)
    {
        if (!_maps.TryGetValue(mapId, out var map))
            return;

        // OnDestroyed fires first — lets the map do cleanup while entities still exist
        // The map is still in _maps here so Server.GetMap(mapId) works during OnDestroyed()
        map.OnDestroyed();

        // Send despawn + MapDestroy to all observers (once — not per-entity Despawn)
        if (map.Observers.Count > 0)
        {
            var writer = new NetworkWriter();
            foreach (var entity in map.Entities)
            {
                WriteEntityDespawnMessage(writer, entity);
                foreach (var observer in map.Observers)
                    SendToClient(observer, writer);
            }
            WriteMapDestroyMessage(writer, map);
            foreach (var observer in map.Observers)
                SendToClient(observer, writer);
        }

        // Local cleanup for each entity — skip replication (already sent above)
        foreach (var entity in map.Entities.ToArray())
        {
            if (!entity.IsSpawned) continue;

            // Clear player entity pointer without triggering RemoveObserver/SendObserverLeave
            if (entity.Owner != null && entity.Owner.PlayerEntity == entity)
            {
                var owner = entity.Owner;
                SendPlayerEntityUnassign(owner);
                owner.PlayerEntity = null;
                OnPlayerEntityUnassigned?.Invoke(owner, entity);
            }

            entity.OnStopServer();
            map.RemoveEntity(entity);
            _entities.Remove(entity.NetworkId);
            entity.IsSpawned = false;
            entity.OnDespawn();
            entity.Map = null;
        }

        // Fire OnObserverLeave for each observer before clearing
        foreach (var observer in map.Observers.ToArray())
            map.OnObserverLeave(observer);

        _maps.Remove(mapId);
        map.Clear();
    }

    /// <summary>
    /// Spawns an unowned entity in the specified map.
    /// Assigns NetworkId, sets IsSpawned, adds to map, and fires lifecycle callbacks:
    /// OnSpawn() → OnStartServer().
    /// </summary>
    public static T Spawn<T>(Map map)
        where T : NetworkEntity, new()
    {
        var entity = new T();
        InitializeEntity(entity, map, null);
        return entity;
    }

    /// <summary>
    /// Spawns an unowned entity with an initializer that runs before lifecycle callbacks.
    /// </summary>
    public static T Spawn<T>(Map map, Action<T> initializer)
        where T : NetworkEntity, new()
    {
        var entity = new T();
        entity.Side = NetworkSide.Server;
        entity.Owner = null;
        // Map is NOT set before the initializer — if the initializer sets Position,
        // This prevents OnEntityMoved firing before OnEntityAdded in FinalizeSpawn.
        initializer(entity);
        entity.Map = map;
        FinalizeSpawn(entity, map, null);
        return entity;
    }

    /// <summary>
    /// Spawns an owned entity in the specified map.
    /// </summary>
    public static T Spawn<T>(Map map, RemoteClient owner)
        where T : NetworkEntity, new()
    {
        var entity = new T();
        InitializeEntity(entity, map, owner);
        return entity;
    }

    /// <summary>
    /// Spawns an owned entity with an initializer that runs before lifecycle callbacks.
    /// </summary>
    public static T Spawn<T>(Map map, RemoteClient owner, Action<T> initializer)
        where T : NetworkEntity, new()
    {
        var entity = new T();
        entity.Side = NetworkSide.Server;
        entity.Owner = owner;
        // Map is NOT set before the initializer — if the initializer sets Position,
        // This prevents OnEntityMoved firing before OnEntityAdded in FinalizeSpawn.
        initializer(entity);
        entity.Map = map;
        FinalizeSpawn(entity, map, owner);
        return entity;
    }

    private static void InitializeEntity(NetworkEntity entity, Map map, RemoteClient? owner)
    {
        entity.Side = NetworkSide.Server;
        entity.Owner = owner;
        entity.Map = map;
        FinalizeSpawn(entity, map, owner);
    }

    private static void FinalizeSpawn(NetworkEntity entity, Map map, RemoteClient? owner)
    {
        if (_nextNetworkId == 0)
            throw new OverflowException("NetworkId overflow: all uint IDs have been exhausted. Consider switching to ulong.");
        entity.NetworkId = _nextNetworkId++;
        entity.OwnerClientId = owner?.ClientId ?? 0;
        entity.IsSpawned = true;
        _entities[entity.NetworkId] = entity;
        map.AddEntity(entity);
        entity.OnSpawn();
        entity.OnStartServer();

        // Replicate spawn to all current observers of the entity's map
        if (map.Observers.Count > 0)
        {
            var writer = new NetworkWriter();
            WriteEntitySpawnMessage(writer, entity);
            foreach (var observer in map.Observers)
                SendToClient(observer, writer);
        }
    }

    /// <summary>
    /// Removes an entity from the network.
    /// Fires lifecycle callbacks: OnStopServer() → removes from map → OnDespawn().
    /// If the entity is a client's player entity, unassigns it first.
    /// </summary>
    public static void Despawn(NetworkEntity entity)
    {
        if (!entity.IsSpawned) return;

        // Unassign from owner if it's their player entity
        if (entity.Owner != null && entity.Owner.PlayerEntity == entity)
            entity.Owner.UnassignPlayerEntity();

        // Replicate despawn to all current observers before removing from map
        if (entity.Map != null && entity.Map.Observers.Count > 0)
        {
            var writer = new NetworkWriter();
            WriteEntityDespawnMessage(writer, entity);
            foreach (var observer in entity.Map.Observers)
                SendToClient(observer, writer);
        }

        entity.OnStopServer();
        entity.Map?.RemoveEntity(entity);
        _entities.Remove(entity.NetworkId);
        entity.IsSpawned = false;
        entity.OnDespawn();
        entity.Map = null;
    }

    /// <summary>
    /// Changes the owner of a spawned entity. Pass null to remove ownership.
    /// Fires OnOwnerChanged on the entity.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown if the entity is not spawned or the new owner is not connected.
    /// </exception>
    public static void ChangeOwner(NetworkEntity entity, RemoteClient? newOwner)
    {
        if (!entity.IsSpawned)
            throw new InvalidOperationException("Cannot change owner of a despawned entity.");
        if (newOwner != null && !newOwner.IsConnected)
            throw new InvalidOperationException("New owner must be connected.");

        var oldOwner = entity.Owner;

        // If entity is the old owner's PlayerEntity, unassign it first
        if (oldOwner != null && oldOwner.PlayerEntity == entity)
            oldOwner.UnassignPlayerEntity();

        entity.Owner = newOwner;
        entity.OwnerClientId = newOwner?.ClientId ?? 0;
        entity.OnOwnerChanged(oldOwner, newOwner);

        // Replicate ownership change to all observers
        if (entity.Map != null && entity.Map.Observers.Count > 0)
        {
            var writer = new NetworkWriter();
            WriteOwnerChangeMessage(writer, entity);
            foreach (var observer in entity.Map.Observers)
                SendToClient(observer, writer);
        }
    }

    /// <summary>
    /// Shared disconnect logic: fires callbacks while PlayerEntity is still accessible,
    /// then unassigns. Does NOT remove from _clients or call Transport.Disconnect — callers handle those.
    /// </summary>
    private static void DisconnectClientInternal(RemoteClient client)
    {
        client.IsConnected = false;
        OnClientDisconnected?.Invoke(client);

        if (client.HasPlayerEntity)
            client.UnassignPlayerEntity();
    }

    /// <summary>
    /// Disconnects a client: fires callbacks (PlayerEntity still accessible), unassigns player entity,
    /// removes from client list. Called internally by RemoteClient.Disconnect().
    /// Does NOT call Transport.Disconnect — the caller handles that.
    /// </summary>
    internal static void RemoveClient(RemoteClient client)
    {
        DisconnectClientInternal(client);
        _clients.Remove(client);
        _clientsByConnection.Remove(client.ConnectionId);
    }

    /// <summary>
    /// Registers an entity type for network spawning.
    /// Both server and client must register types in the same order so IDs match.
    /// </summary>
    public static void RegisterEntity<T>() where T : NetworkEntity, new()
    {
        var type = typeof(T);
        if (_entityTypeToId.ContainsKey(type)) return;
        var id = _nextEntityTypeId++;
        _entityTypeToId[type] = id;
        _entityIdToFactory[id] = () => new T();
    }

    /// <summary>
    /// Registers a map type for network replication.
    /// Both server and client must register types in the same order so IDs match.
    /// </summary>
    public static void RegisterMap<T>() where T : Map, new()
    {
        var type = typeof(T);
        if (_mapTypeToId.ContainsKey(type)) return;
        var id = _nextMapTypeId++;
        _mapTypeToId[type] = id;
        _mapIdToFactory[id] = () => new T();
    }

    /// <summary>
    /// Registers a static command dispatcher for a given type ID.
    /// Called by weaver-generated registration code.
    /// </summary>
    public static void RegisterStaticRpc(ushort rpcTypeId, Action<ushort, NetworkReader, uint, ushort> dispatcher)
    {
        _staticCommandDispatchers[rpcTypeId] = dispatcher;
    }

    /// <summary>
    /// Returns the type ID for a registered entity type. Throws if unregistered.
    /// </summary>
    internal static ushort GetEntityTypeId(Type type)
    {
        if (_entityTypeToId.TryGetValue(type, out var id))
            return id;
        throw new InvalidOperationException($"Entity type {type.Name} is not registered. Call Server.RegisterEntity<{type.Name}>() before spawning.");
    }

    /// <summary>
    /// Returns the type ID for a registered map type. Throws if unregistered.
    /// </summary>
    internal static ushort GetMapTypeId(Type type)
    {
        if (_mapTypeToId.TryGetValue(type, out var id))
            return id;
        throw new InvalidOperationException($"Map type {type.Name} is not registered. Call Server.RegisterMap<{type.Name}>() before creating.");
    }

    /// <summary>
    /// Finds an entity by NetworkId across all maps. Returns null if not found.
    /// </summary>
    public static NetworkEntity? FindEntity(uint networkId)
    {
        return _entities.GetValueOrDefault(networkId);
    }

    /// <summary>
    /// Finds an entity by NetworkId across all maps, cast to type T. Returns null if not found or wrong type.
    /// </summary>
    public static T? FindEntity<T>(uint networkId) where T : NetworkEntity
    {
        return _entities.GetValueOrDefault(networkId) as T;
    }

    /// <summary>
    /// Processes one server tick: updates DeltaTime, processes transport I/O,
    /// checks connection timeouts, ticks entities.
    /// </summary>
    public static void Tick()
    {
        // Update DeltaTime from elapsed time since last tick
        DeltaTime = (float)_tickStopwatch.Elapsed.TotalSeconds;
        _tickStopwatch.Restart();

        Transport?.Tick();

        if (ConnectionTimeout > 0)
        {
            foreach (var client in _clients.ToArray())
            {
                if (Time - client.LastReceivedTime > ConnectionTimeout)
                {
                    Transport?.Disconnect(client.ConnectionId, "connection timed out");
                }
            }
        }

        // Tick all entities (snapshot — ServerTick may spawn or despawn entities)
        foreach (var entity in _entities.Values.ToArray())
            entity.ServerTick();

    }

    // ── Replication helpers ──

    private static void SendToClient(RemoteClient client, NetworkWriter writer)
    {
        Transport!.Send(client.ConnectionId, writer.ToArraySegment());
    }

    private static void WriteEntitySpawnMessage(NetworkWriter writer, NetworkEntity entity)
    {
        writer.Reset();
        writer.WriteByte(MessageType.EntitySpawn);
        writer.WriteUInt(entity.NetworkId);
        writer.WriteUShort(GetEntityTypeId(entity.GetType()));
        writer.WriteUInt(entity.OwnerClientId);
        writer.WriteGuid(entity.Map!.MapId);
        entity.PackSpawnData(writer);
    }

    private static void WriteEntityDespawnMessage(NetworkWriter writer, NetworkEntity entity)
    {
        writer.Reset();
        writer.WriteByte(MessageType.EntityDespawn);
        writer.WriteUInt(entity.NetworkId);
    }

    private static void WriteOwnerChangeMessage(NetworkWriter writer, NetworkEntity entity)
    {
        writer.Reset();
        writer.WriteByte(MessageType.OwnerChange);
        writer.WriteUInt(entity.NetworkId);
        writer.WriteUInt(entity.OwnerClientId);
    }

    private static void WriteMapCreateMessage(NetworkWriter writer, Map map)
    {
        writer.Reset();
        writer.WriteByte(MessageType.MapCreate);
        writer.WriteUShort(GetMapTypeId(map.GetType()));
        writer.WriteGuid(map.MapId);
    }

    private static void WriteMapDestroyMessage(NetworkWriter writer, Map map)
    {
        writer.Reset();
        writer.WriteByte(MessageType.MapDestroy);
        writer.WriteGuid(map.MapId);
    }

    internal static void SendPlayerEntityAssign(RemoteClient client, NetworkEntity entity)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.PlayerEntityAssign);
        writer.WriteUInt(entity.NetworkId);
        SendToClient(client, writer);
    }

    internal static void SendPlayerEntityUnassign(RemoteClient client)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.PlayerEntityUnassign);
        SendToClient(client, writer);
    }

    internal static void SendObserverEnter(RemoteClient client, Map map)
    {
        var writer = new NetworkWriter();
        WriteMapCreateMessage(writer, map);
        SendToClient(client, writer);

        foreach (var entity in map.Entities)
        {
            WriteEntitySpawnMessage(writer, entity);
            SendToClient(client, writer);
        }
    }

    internal static void SendObserverLeave(RemoteClient client, Map map)
    {
        var writer = new NetworkWriter();
        foreach (var entity in map.Entities)
        {
            WriteEntityDespawnMessage(writer, entity);
            SendToClient(client, writer);
        }

        WriteMapDestroyMessage(writer, map);
        SendToClient(client, writer);
    }

    internal static void SendEntityDespawnToObservers(NetworkEntity entity, Map map)
    {
        if (map.Observers.Count > 0)
        {
            var writer = new NetworkWriter();
            WriteEntityDespawnMessage(writer, entity);
            foreach (var observer in map.Observers)
                SendToClient(observer, writer);
        }
    }

    internal static void SendEntitySpawnToObservers(NetworkEntity entity, Map map)
    {
        if (map.Observers.Count > 0)
        {
            var writer = new NetworkWriter();
            WriteEntitySpawnMessage(writer, entity);
            foreach (var observer in map.Observers)
                SendToClient(observer, writer);
        }
    }

    /// <summary>Fired when a client connects.</summary>
    public static event Action<RemoteClient>? OnClientConnected;

    /// <summary>Fired when a client disconnects.</summary>
    public static event Action<RemoteClient>? OnClientDisconnected;

    /// <summary>Fired after a player entity is assigned to a client.</summary>
    public static event Action<RemoteClient, NetworkEntity>? OnPlayerEntityAssigned;

    /// <summary>Fired after a player entity is unassigned from a client.</summary>
    public static event Action<RemoteClient, NetworkEntity>? OnPlayerEntityUnassigned;

    internal static void FirePlayerEntityAssigned(RemoteClient client, NetworkEntity entity)
        => OnPlayerEntityAssigned?.Invoke(client, entity);

    internal static void FirePlayerEntityUnassigned(RemoteClient client, NetworkEntity entity)
        => OnPlayerEntityUnassigned?.Invoke(client, entity);

    internal static void RegisterEntityByType(Type type)
    {
        if (_entityTypeToId.ContainsKey(type)) return;
        var id = _nextEntityTypeId++;
        _entityTypeToId[type] = id;
        _entityIdToFactory[id] = () => (NetworkEntity)Activator.CreateInstance(type)!;
    }

    internal static void RegisterMapByType(Type type)
    {
        if (_mapTypeToId.ContainsKey(type)) return;
        var id = _nextMapTypeId++;
        _mapTypeToId[type] = id;
        _mapIdToFactory[id] = () => (Map)Activator.CreateInstance(type)!;
    }

    private static bool _registered;

    internal static void RegisterAllTypes()
    {
        if (_registered) return;
        _registered = true;

        var entityBaseType = typeof(NetworkEntity);
        var mapBaseType = typeof(Map);

        var entityTypes = new List<Type>();
        var mapTypes = new List<Type>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch { continue; }

            foreach (var type in types)
            {
                if (type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null)
                    continue;

                if (entityBaseType.IsAssignableFrom(type) && type != entityBaseType)
                    entityTypes.Add(type);
                else if (mapBaseType.IsAssignableFrom(type) && type != mapBaseType)
                    mapTypes.Add(type);
            }
        }

        entityTypes.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.Ordinal));
        mapTypes.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.Ordinal));

        foreach (var t in entityTypes) RegisterEntityByType(t);
        foreach (var t in mapTypes) RegisterMapByType(t);

        // Discover and invoke weaver-generated static RPC registrations
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? regType;
            try { regType = assembly.GetType("ProwlWickedStaticRpcRegistration"); }
            catch { continue; }
            if (regType == null) continue;

            var method = regType.GetMethod("RegisterAll", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            method?.Invoke(null, null);
        }
    }

    /// <summary>Fired after the server starts.</summary>
    public static event Action? OnStarted;

    /// <summary>Fired when the server is stopping. Active is still true at this point.</summary>
    public static event Action? OnStopped;

    /// <summary>
    /// Fired when a client sends an authentication token. The handler should validate
    /// the token and call AcceptAuthentication() or RejectAuthentication().
    /// </summary>
    public static event Action<RemoteClient, string>? OnClientAuthenticate;

    private static void HandleAuthenticate(uint connectionId, NetworkReader reader)
    {
        string? token = reader.ReadString();
        if (token == null) return;
        var client = FindClientByConnection(connectionId);
        if (client == null) return;
        client.AuthToken = token;
        OnClientAuthenticate?.Invoke(client, token);
    }

    /// <summary>
    /// Accepts a client's authentication and assigns a user ID.
    /// Sends an AuthResult(true) message to the client.
    /// </summary>
    public static void AcceptAuthentication(RemoteClient client, string userId)
    {
        client.IsAuthenticated = true;
        client.UserId = userId;
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.AuthResult);
        writer.WriteBool(true);
        writer.WriteString(null);
        Transport!.Send(client.ConnectionId, writer.ToArraySegment());
    }

    /// <summary>
    /// Rejects a client's authentication with an optional reason.
    /// Sends an AuthResult(false) message and then disconnects the client.
    /// </summary>
    public static void RejectAuthentication(RemoteClient client, string? reason = null)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.AuthResult);
        writer.WriteBool(false);
        writer.WriteString(reason);
        Transport!.Send(client.ConnectionId, writer.ToArraySegment());
        Transport.Disconnect(client.ConnectionId, reason);
    }

    // ── RPC response helpers (called by weaver-generated dispatch code) ──

    public static void __SendRpcResponseVoid(uint connectionId, ushort promiseId)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcResponseInt(uint connectionId, ushort promiseId, int value)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        writer.WriteInt(value);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcResponseUInt(uint connectionId, ushort promiseId, uint value)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        writer.WriteUInt(value);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcResponseBool(uint connectionId, ushort promiseId, bool value)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        writer.WriteBool(value);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcResponseString(uint connectionId, ushort promiseId, string? value)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        writer.WriteString(value);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcResponseFloat(uint connectionId, ushort promiseId, float value)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        writer.WriteFloat(value);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcResponseVector2(uint connectionId, ushort promiseId, Vector2 value)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        writer.WriteVector2(value);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcResponseByte(uint connectionId, ushort promiseId, byte value)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        writer.WriteByte(value);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcResponseSByte(uint connectionId, ushort promiseId, sbyte value)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        writer.WriteSByte(value);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcResponseShort(uint connectionId, ushort promiseId, short value)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        writer.WriteShort(value);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcResponseUShort(uint connectionId, ushort promiseId, ushort value)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        writer.WriteUShort(value);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcResponseLong(uint connectionId, ushort promiseId, long value)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        writer.WriteLong(value);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcResponseULong(uint connectionId, ushort promiseId, ulong value)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        writer.WriteULong(value);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcResponseDouble(uint connectionId, ushort promiseId, double value)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        writer.WriteDouble(value);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcResponseGuid(uint connectionId, ushort promiseId, Guid value)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        writer.WriteGuid(value);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcResponseByteArray(uint connectionId, ushort promiseId, byte[]? value)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        writer.WriteByteArray(value);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcResponseIntArray(uint connectionId, ushort promiseId, int[]? value)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        writer.WriteIntArray(value);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcResponseUIntArray(uint connectionId, ushort promiseId, uint[]? value)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        writer.WriteUIntArray(value);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcResponseFloatArray(uint connectionId, ushort promiseId, float[]? value)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        writer.WriteFloatArray(value);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcResponseDoubleArray(uint connectionId, ushort promiseId, double[]? value)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        writer.WriteDoubleArray(value);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcResponseStringArray(uint connectionId, ushort promiseId, string[]? value)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        writer.WriteStringArray(value);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcResponseBoolArray(uint connectionId, ushort promiseId, bool[]? value)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        writer.WriteBoolArray(value);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcResponseLongArray(uint connectionId, ushort promiseId, long[]? value)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        writer.WriteLongArray(value);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcResponseULongArray(uint connectionId, ushort promiseId, ulong[]? value)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        writer.WriteULongArray(value);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcResponseShortArray(uint connectionId, ushort promiseId, short[]? value)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        writer.WriteShortArray(value);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcResponseUShortArray(uint connectionId, ushort promiseId, ushort[]? value)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcResponse);
        writer.WriteUShort(promiseId);
        writer.WriteUShortArray(value);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    public static void __SendRpcError(uint connectionId, ushort promiseId, string message)
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.RpcError);
        writer.WriteUShort(promiseId);
        writer.WriteString(message);
        Transport!.Send(connectionId, writer.ToArraySegment());
    }

    // ── ClientRpc sending helpers (called by weaver-generated method bodies) ──

    public static void __SendToEntityObservers(NetworkEntity entity, ArraySegment<byte> data, bool excludeOwner)
    {
        if (entity.Map == null) return;
        foreach (var observer in entity.Map.Observers)
        {
            if (excludeOwner && entity.Owner == observer) continue;
            Transport!.Send(observer.ConnectionId, data);
        }
    }

    public static void __SendToEntityOwner(NetworkEntity entity, ArraySegment<byte> data)
    {
        if (entity.Owner == null) return;
        Transport!.Send(entity.Owner.ConnectionId, data);
    }

    public static void __SendToMapObservers(Map map, ArraySegment<byte> data)
    {
        foreach (var observer in map.Observers)
            Transport!.Send(observer.ConnectionId, data);
    }

    public static void __SendToRemoteClient(RemoteClient client, ArraySegment<byte> data)
    {
        Transport!.Send(client.ConnectionId, data);
    }

    public static void __SendToRemoteClients(RemoteClient[] clients, ArraySegment<byte> data)
    {
        foreach (var client in clients)
            Transport!.Send(client.ConnectionId, data);
    }
}
