using System.Diagnostics;
using System.Numerics;
using Prowl.Wicked.Transport;

namespace Prowl.Wicked;

/// <summary>
/// Static client entry point. Manages the connection to a server, local entities, and the client tick loop.
/// Only one client can be active per process.
/// </summary>
public static class Client
{
    private static readonly Dictionary<uint, NetworkEntity> _entities = new();
    private static readonly Dictionary<Guid, Map> _maps = new();
    private static readonly Stopwatch _tickStopwatch = new();

    // Type registries
    private static readonly Dictionary<Type, ushort> _entityTypeToId = new();
    private static readonly Dictionary<ushort, Func<NetworkEntity>> _entityIdToFactory = new();
    private static ushort _nextEntityTypeId = 1;
    private static readonly Dictionary<Type, ushort> _mapTypeToId = new();
    private static readonly Dictionary<ushort, Func<Map>> _mapIdToFactory = new();
    private static ushort _nextMapTypeId = 1;

    // RPC promise tracking
    private static readonly Dictionary<ushort, (RpcPromise promise, byte returnTypeCode)> _pendingPromises = new();
    private static ushort _nextPromiseId = 1;

    // Time sync
    private static readonly Stopwatch _localClock = new();
    private static double _serverTimeOffset;
    private static double _rttEma;
    private static double _rttVarianceEma;
    private static double _pingTimer;
    private static bool _hasFirstSample;

    // Static RPC registry
    private static readonly Dictionary<ushort, Action<ushort, NetworkReader>> _staticRpcDispatchers = new();

    /// <summary>
    /// True after Connect() and before Disconnect() completes.
    /// </summary>
    public static bool Active { get; private set; }

    /// <summary>
    /// True only when the connection to the server is fully established.
    /// Set when the transport fires OnConnected, not immediately in Connect().
    /// </summary>
    public static bool IsConnected { get; private set; }

    /// <summary>
    /// The local player's RemoteClient instance. Null before Connect() and after Disconnect().
    /// </summary>
    public static RemoteClient? LocalClient { get; private set; }

    /// <summary>
    /// The transport layer used for network communication.
    /// Set before calling Connect() for custom transports.
    /// </summary>
    public static IClientTransport? Transport { get; set; }

    /// <summary>
    /// Time in seconds since the last Client.Tick().
    /// Updated at the start of every Tick(). Use this for client-side timing.
    /// </summary>
    public static float DeltaTime { get; internal set; }

    /// <summary>
    /// Monotonically increasing seconds since Connect(). Driven by a dedicated
    /// Stopwatch that runs for the entire client lifetime.
    /// </summary>
    public static double LocalTime => _localClock.Elapsed.TotalSeconds;

    /// <summary>
    /// Estimated server time based on ping/pong synchronization.
    /// Returns 0 until the first pong is received.
    /// </summary>
    public static double ServerTime => _hasFirstSample ? LocalTime + _serverTimeOffset : 0.0;

    /// <summary>
    /// Exponential moving average of the round-trip time in seconds.
    /// </summary>
    public static double RoundTripTime => _rttEma;

    /// <summary>
    /// Standard deviation of the round-trip time in seconds.
    /// </summary>
    public static double StandardDeviation => Math.Sqrt(_rttVarianceEma);

    /// <summary>
    /// Seconds between automatic ping messages. Default 2 seconds.
    /// </summary>
    public static float PingInterval { get; set; } = 2f;

    /// <summary>
    /// Number of samples in the EMA window for RTT smoothing. Default 10.
    /// </summary>
    public static int PingWindowSize { get; set; } = 10;

    /// <summary>
    /// Authentication token to send to the server on connect.
    /// Set before calling Connect(). If null, no auth message is sent (backward-compatible).
    /// </summary>
    public static string? AuthToken { get; set; }

    /// <summary>
    /// Convenience property for the map the local player entity is in.
    /// Returns null if there is no local client, no player entity, or the player entity is not in a map.
    /// </summary>
    public static Map? CurrentMap => LocalClient?.PlayerEntity?.Map;

    /// <summary>
    /// All entities currently tracked on the client side.
    /// </summary>
    public static IReadOnlyCollection<NetworkEntity> Entities => _entities.Values;

    /// <summary>
    /// Connects to a server.
    /// Sets Active, creates the LocalClient, and initiates the transport connection.
    /// IsConnected becomes true when the transport fires OnConnected, not immediately.
    /// </summary>
    public static void Connect(string host, int port)
    {
        if (Active)
            throw new InvalidOperationException("Client is already active. Call Disconnect() before calling Connect() again.");

        RegisterAllTypes();
        Active = true;
        LocalClient = new RemoteClient();
        LocalClient.Side = NetworkSide.Client;
        _tickStopwatch.Restart();
        _localClock.Restart();
        _pingTimer = PingInterval;

        // Default to TCP if no transport was set
        Transport ??= new Transport.TcpClientTransport();

        // Wire up transport events
        Transport.OnConnected += HandleTransportConnected;
        Transport.OnDisconnected += HandleTransportDisconnected;
        Transport.OnDataReceived += HandleTransportDataReceived;
        Transport.Connect(host, port);
    }

    private static void HandleTransportConnected()
    {
        IsConnected = true;
        if (LocalClient != null)
            LocalClient.IsConnected = true;
        OnConnected?.Invoke();

        // Send auth token if set (otherwise backward-compatible, no-op)
        if (AuthToken != null)
        {
            var writer = new NetworkWriter();
            writer.WriteByte(MessageType.Authenticate);
            writer.WriteString(AuthToken);
            Transport?.Send(writer.ToArraySegment());
        }
    }

    private static void HandleTransportDisconnected(string? reason)
    {
        // Unwire transport events to prevent double-wiring on reconnect
        if (Transport != null)
        {
            Transport.OnConnected -= HandleTransportConnected;
            Transport.OnDisconnected -= HandleTransportDisconnected;
            Transport.OnDataReceived -= HandleTransportDataReceived;
        }

        RejectPendingPromises();
        OnDisconnected?.Invoke();
        TeardownEntities();
        TeardownMaps();
        IsConnected = false;
        Active = false;
        LocalClient = null;
        _tickStopwatch.Stop();
        _localClock.Stop();
    }

    /// <summary>
    /// Disconnects from the server.
    /// Events fire while state is still valid, then state is cleared.
    /// </summary>
    public static void Disconnect()
    {
        if (!Active) return;

        RejectPendingPromises();
        // Fire event while LocalClient is still accessible
        OnDisconnected?.Invoke();

        // Unwire transport events before disconnecting
        if (Transport != null)
        {
            Transport.OnConnected -= HandleTransportConnected;
            Transport.OnDisconnected -= HandleTransportDisconnected;
            Transport.OnDataReceived -= HandleTransportDataReceived;
            Transport.Disconnect();
        }

        TeardownEntities();
        TeardownMaps();
        Active = false;
        IsConnected = false;
        LocalClient = null;
        _tickStopwatch.Stop();
        _localClock.Stop();
    }

    /// <summary>
    /// Fires client-side lifecycle callbacks on all tracked entities before clearing them.
    /// Called during both explicit disconnect and transport-initiated disconnect.
    /// </summary>
    private static void TeardownEntities()
    {
        foreach (var entity in _entities.Values.ToArray())
        {
            if (entity.IsOwner)
                entity.OnStopOwner();
            entity.OnStopClient();
            entity.Map?.RemoveEntity(entity);
            entity.IsSpawned = false;
            entity.OnDespawn();
            entity.Map = null;
        }
        _entities.Clear();
    }

    private static void TeardownMaps()
    {
        foreach (var map in _maps.Values)
            map.OnDestroyed();
        _maps.Clear();
    }

    // ── Message handling ──

    private static void HandleTransportDataReceived(ArraySegment<byte> data)
    {
        var reader = new NetworkReader(data);
        byte msgType = reader.ReadByte();

        switch (msgType)
        {
            case MessageType.AssignClientId:
                HandleAssignClientId(reader);
                break;
            case MessageType.PlayerEntityAssign:
                HandlePlayerEntityAssign(reader);
                break;
            case MessageType.PlayerEntityUnassign:
                HandlePlayerEntityUnassign(reader);
                break;
            case MessageType.EntitySpawn:
                HandleEntitySpawn(reader);
                break;
            case MessageType.EntityDespawn:
                HandleEntityDespawn(reader);
                break;
            case MessageType.OwnerChange:
                HandleOwnerChange(reader);
                break;
            case MessageType.MapCreate:
                HandleMapCreate(reader);
                break;
            case MessageType.MapDestroy:
                HandleMapDestroy(reader);
                break;
            case MessageType.ClientRpcCall:
                HandleClientRpcCall(reader);
                break;
            case MessageType.RpcResponse:
                HandleRpcResponse(reader);
                break;
            case MessageType.RpcError:
                HandleRpcError(reader);
                break;
            case MessageType.Pong:
                HandlePong(reader);
                break;
            case MessageType.AuthResult:
                HandleAuthResult(reader);
                break;
        }
    }

    private static void HandleAssignClientId(NetworkReader reader)
    {
        uint clientId = reader.ReadUInt();
        if (LocalClient != null)
            LocalClient.ClientId = clientId;
    }

    private static void HandlePlayerEntityAssign(NetworkReader reader)
    {
        uint networkId = reader.ReadUInt();
        if (LocalClient == null) return;
        var entity = FindEntity(networkId);
        if (entity == null) return;
        LocalClient.PlayerEntity = entity;
    }

    private static void HandlePlayerEntityUnassign(NetworkReader reader)
    {
        if (LocalClient == null) return;
        LocalClient.PlayerEntity = null;
    }

    private static void HandleEntitySpawn(NetworkReader reader)
    {
        uint networkId = reader.ReadUInt();
        ushort typeId = reader.ReadUShort();
        uint ownerClientId = reader.ReadUInt();
        Guid mapId = reader.ReadGuid();

        var entity = CreateEntityFromTypeId(typeId);
        if (entity == null) return;

        if (!_maps.TryGetValue(mapId, out var map)) return;

        entity.Side = NetworkSide.Client;
        entity.NetworkId = networkId;
        entity.OwnerClientId = ownerClientId;

        // Set Owner to LocalClient if this entity is owned by us
        if (ownerClientId != 0 && LocalClient != null && LocalClient.ClientId == ownerClientId)
            entity.Owner = LocalClient;

        entity.Map = map;
        map.AddEntity(entity);
        entity.UnpackSpawnData(reader);
        entity.IsSpawned = true;
        TrackEntity(entity);
        entity.OnSpawn();
        entity.OnStartClient();
        if (entity.IsOwner)
            entity.OnStartOwner();
    }

    private static void HandleEntityDespawn(NetworkReader reader)
    {
        uint networkId = reader.ReadUInt();

        if (!_entities.TryGetValue(networkId, out var entity)) return;

        if (entity.IsOwner)
            entity.OnStopOwner();
        entity.OnStopClient();
        entity.Map?.RemoveEntity(entity);
        UntrackEntity(entity);
        entity.IsSpawned = false;
        entity.OnDespawn();
        entity.Map = null;
    }

    private static void HandleOwnerChange(NetworkReader reader)
    {
        uint networkId = reader.ReadUInt();
        uint newOwnerClientId = reader.ReadUInt();

        if (!_entities.TryGetValue(networkId, out var entity)) return;

        bool wasOwner = entity.IsOwner;
        var oldOwner = entity.Owner;

        entity.OwnerClientId = newOwnerClientId;

        // Set Owner to LocalClient if we are the new owner, null otherwise
        if (newOwnerClientId != 0 && LocalClient != null && LocalClient.ClientId == newOwnerClientId)
            entity.Owner = LocalClient;
        else
            entity.Owner = null;

        if (wasOwner)
            entity.OnStopOwner();
        entity.OnOwnerChanged(oldOwner, entity.Owner);
        if (entity.IsOwner)
            entity.OnStartOwner();
    }

    private static void HandleMapCreate(NetworkReader reader)
    {
        ushort mapTypeId = reader.ReadUShort();
        Guid mapId = reader.ReadGuid();

        var map = CreateMapFromTypeId(mapTypeId);
        if (map == null) return;

        map.MapId = mapId;
        map.Side = NetworkSide.Client;
        _maps[mapId] = map;
        map.OnCreated();
    }

    private static void HandleMapDestroy(NetworkReader reader)
    {
        Guid mapId = reader.ReadGuid();

        if (!_maps.TryGetValue(mapId, out var map)) return;

        map.OnDestroyed();

        // Cleanup any remaining entities in this map
        foreach (var entity in map.Entities.ToArray())
        {
            if (entity.IsOwner)
                entity.OnStopOwner();
            entity.OnStopClient();
            map.RemoveEntity(entity);
            UntrackEntity(entity);
            entity.IsSpawned = false;
            entity.OnDespawn();
            entity.Map = null;
        }

        _maps.Remove(mapId);
    }

    // ── RPC message handling ──

    private static void HandleClientRpcCall(NetworkReader reader)
    {
        byte objectKind = reader.ReadByte();

        switch (objectKind)
        {
            case 0: // Entity
            {
                uint networkId = reader.ReadUInt();
                var entity = FindEntity(networkId);
                if (entity == null) return;

                ushort methodId = reader.ReadUShort();
                entity.__DispatchClientRpc(methodId, reader);
                break;
            }
            case 1: // Map
            {
                Guid mapId = reader.ReadGuid();
                var map = GetMap(mapId);
                if (map == null) return;

                ushort methodId = reader.ReadUShort();
                map.__DispatchClientRpc(methodId, reader);
                break;
            }
            case 2: // Static
            {
                ushort rpcTypeId = reader.ReadUShort();
                ushort methodId = reader.ReadUShort();

                if (!_staticRpcDispatchers.TryGetValue(rpcTypeId, out var dispatcher))
                    return;

                dispatcher(methodId, reader);
                break;
            }
        }
    }

    private static void HandleRpcResponse(NetworkReader reader)
    {
        ushort promiseId = reader.ReadUShort();
        if (!_pendingPromises.Remove(promiseId, out var entry)) return;

        switch (entry.returnTypeCode)
        {
            case 0: entry.promise.Resolve(); break;
            case 1: ((RpcPromise<int>)entry.promise).Resolve(reader.ReadInt()); break;
            case 2: ((RpcPromise<uint>)entry.promise).Resolve(reader.ReadUInt()); break;
            case 3: ((RpcPromise<bool>)entry.promise).Resolve(reader.ReadBool()); break;
            case 4: ((RpcPromise<string>)entry.promise).Resolve(reader.ReadString()!); break;
            case 5: ((RpcPromise<float>)entry.promise).Resolve(reader.ReadFloat()); break;
            case 6: ((RpcPromise<System.Numerics.Vector2>)entry.promise).Resolve(reader.ReadVector2()); break;
            case 7: ((RpcPromise<byte>)entry.promise).Resolve(reader.ReadByte()); break;
            case 8: ((RpcPromise<sbyte>)entry.promise).Resolve(reader.ReadSByte()); break;
            case 9: ((RpcPromise<short>)entry.promise).Resolve(reader.ReadShort()); break;
            case 10: ((RpcPromise<ushort>)entry.promise).Resolve(reader.ReadUShort()); break;
            case 11: ((RpcPromise<long>)entry.promise).Resolve(reader.ReadLong()); break;
            case 12: ((RpcPromise<ulong>)entry.promise).Resolve(reader.ReadULong()); break;
            case 13: ((RpcPromise<double>)entry.promise).Resolve(reader.ReadDouble()); break;
            case 14: ((RpcPromise<Guid>)entry.promise).Resolve(reader.ReadGuid()); break;
            case 15: ((RpcPromise<byte[]>)entry.promise).Resolve(reader.ReadByteArray()!); break;
            case 16: ((RpcPromise<int[]>)entry.promise).Resolve(reader.ReadIntArray()!); break;
            case 17: ((RpcPromise<uint[]>)entry.promise).Resolve(reader.ReadUIntArray()!); break;
            case 18: ((RpcPromise<float[]>)entry.promise).Resolve(reader.ReadFloatArray()!); break;
            case 19: ((RpcPromise<double[]>)entry.promise).Resolve(reader.ReadDoubleArray()!); break;
            case 20: ((RpcPromise<string[]>)entry.promise).Resolve(reader.ReadStringArray()!); break;
            case 21: ((RpcPromise<bool[]>)entry.promise).Resolve(reader.ReadBoolArray()!); break;
            case 22: ((RpcPromise<long[]>)entry.promise).Resolve(reader.ReadLongArray()!); break;
            case 23: ((RpcPromise<ulong[]>)entry.promise).Resolve(reader.ReadULongArray()!); break;
            case 24: ((RpcPromise<short[]>)entry.promise).Resolve(reader.ReadShortArray()!); break;
            case 25: ((RpcPromise<ushort[]>)entry.promise).Resolve(reader.ReadUShortArray()!); break;
            default: entry.promise.Reject(new Exception($"Unknown RPC return type code: {entry.returnTypeCode}")); break;
        }
    }

    private static void HandleRpcError(NetworkReader reader)
    {
        ushort promiseId = reader.ReadUShort();
        string? message = reader.ReadString();

        if (!_pendingPromises.Remove(promiseId, out var entry)) return;

        entry.promise.Reject(new Exception(message ?? "Unknown RPC error"));
    }

    // ── RPC helpers (called by weaver-generated code) ──

    public static void __SendToServer(ArraySegment<byte> data)
    {
        Transport?.Send(data);
    }

    public static ushort __TrackPromise(RpcPromise promise, byte returnTypeCode)
    {
        var id = _nextPromiseId++;
        if (_nextPromiseId == 0) _nextPromiseId = 1;
        // Skip IDs that are already in use (extremely unlikely but prevents silent overwrite)
        while (_pendingPromises.ContainsKey(id))
        {
            id = _nextPromiseId++;
            if (_nextPromiseId == 0) _nextPromiseId = 1;
        }
        _pendingPromises[id] = (promise, returnTypeCode);
        return id;
    }

    /// <summary>
    /// Registers a static RPC dispatcher for a given type ID.
    /// Called by weaver-generated registration code.
    /// </summary>
    public static void RegisterStaticRpc(ushort rpcTypeId, Action<ushort, NetworkReader> dispatcher)
    {
        _staticRpcDispatchers[rpcTypeId] = dispatcher;
    }

    /// <summary>
    /// Resets all static state. Essential for test isolation — clears event subscriptions,
    /// connection state, timing, entities, and transport. Call between tests.
    /// </summary>
    public static void Reset()
    {
        Active = false;
        IsConnected = false;
        LocalClient = null;
        Transport = null;
        DeltaTime = 0f;
        _entities.Clear();
        _maps.Clear();
        _entityTypeToId.Clear();
        _entityIdToFactory.Clear();
        _nextEntityTypeId = 1;
        _mapTypeToId.Clear();
        _mapIdToFactory.Clear();
        _nextMapTypeId = 1;
        _pendingPromises.Clear();
        _nextPromiseId = 1;
        _staticRpcDispatchers.Clear();
        _registered = false;
        _tickStopwatch.Reset();
        _localClock.Reset();
        _serverTimeOffset = 0;
        _rttEma = 0;
        _rttVarianceEma = 0;
        _pingTimer = 0;
        _hasFirstSample = false;
        PingInterval = 2f;
        PingWindowSize = 10;
        OnConnected = null;
        OnDisconnected = null;
        AuthToken = null;
        OnAuthenticated = null;
        OnAuthRejected = null;
    }

    /// <summary>
    /// Registers an entity type so the client can instantiate server-spawned entities.
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
    /// Registers a map type so the client can instantiate server-created maps.
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

    internal static NetworkEntity? CreateEntityFromTypeId(ushort typeId)
    {
        return _entityIdToFactory.TryGetValue(typeId, out var factory) ? factory() : null;
    }

    internal static Map? CreateMapFromTypeId(ushort typeId)
    {
        return _mapIdToFactory.TryGetValue(typeId, out var factory) ? factory() : null;
    }

    /// <summary>
    /// All currently active maps on the client side.
    /// </summary>
    public static IReadOnlyCollection<Map> Maps => _maps.Values;

    /// <summary>
    /// Finds a map by its ID on the client. Returns null if not found.
    /// </summary>
    public static Map? GetMap(Guid mapId)
    {
        return _maps.GetValueOrDefault(mapId);
    }

    /// <summary>
    /// Tracks an entity on the client side. Called internally when the client receives
    /// a spawn message from the server.
    /// </summary>
    internal static void TrackEntity(NetworkEntity entity)
    {
        _entities[entity.NetworkId] = entity;
    }

    /// <summary>
    /// Untracks an entity on the client side. Called internally when the client receives
    /// a despawn message from the server.
    /// </summary>
    internal static void UntrackEntity(NetworkEntity entity)
    {
        _entities.Remove(entity.NetworkId);
    }

    /// <summary>
    /// Finds an entity by NetworkId on the client. Returns null if not found.
    /// </summary>
    public static NetworkEntity? FindEntity(uint networkId)
    {
        return _entities.GetValueOrDefault(networkId);
    }

    /// <summary>
    /// Finds an entity by NetworkId on the client, cast to type T. Returns null if not found or wrong type.
    /// </summary>
    public static T? FindEntity<T>(uint networkId) where T : NetworkEntity
    {
        return _entities.GetValueOrDefault(networkId) as T;
    }

    /// <summary>
    /// Processes one client tick: updates DeltaTime, processes transport I/O,
    /// message processing, promise resolution, entity ticking.
    /// </summary>
    public static void Tick()
    {
        // Update DeltaTime from elapsed time since last tick
        DeltaTime = (float)_tickStopwatch.Elapsed.TotalSeconds;
        _tickStopwatch.Restart();

        Transport?.Tick();

        if (IsConnected)
        {
            _pingTimer += DeltaTime;
            if (_pingTimer >= PingInterval)
            {
                _pingTimer -= PingInterval;
                SendPing();
            }
        }

        // Check for timed-out promises
        if (_pendingPromises.Count > 0)
        {
            List<ushort>? timedOut = null;
            foreach (var (id, entry) in _pendingPromises)
            {
                if (entry.promise.CheckTimeout())
                    (timedOut ??= new()).Add(id);
            }
            if (timedOut != null)
                foreach (var id in timedOut)
                    _pendingPromises.Remove(id);
        }

        // Tick all client-side entities (snapshot — ClientTick may trigger despawns)
        foreach (var entity in _entities.Values.ToArray())
            entity.ClientTick();
    }

    private static void RejectPendingPromises()
    {
        foreach (var entry in _pendingPromises.Values)
            entry.promise.Reject(new Exception("Disconnected"));
        _pendingPromises.Clear();
    }

    private static void SendPing()
    {
        var writer = new NetworkWriter();
        writer.WriteByte(MessageType.Ping);
        writer.WriteDouble(_localClock.Elapsed.TotalSeconds);
        Transport?.Send(writer.ToArraySegment());
    }

    private static void HandlePong(NetworkReader reader)
    {
        double echoedClientTimestamp = reader.ReadDouble();
        double serverTimestamp = reader.ReadDouble();
        double localNow = _localClock.Elapsed.TotalSeconds;
        double rtt = localNow - echoedClientTimestamp;
        if (rtt < 0) return;

        double halfRtt = rtt / 2.0;
        double alpha = 2.0 / (PingWindowSize + 1);

        if (!_hasFirstSample)
        {
            _rttEma = rtt;
            _rttVarianceEma = 0;
            _hasFirstSample = true;
        }
        else
        {
            double diff = rtt - _rttEma;
            _rttEma = alpha * rtt + (1.0 - alpha) * _rttEma;
            _rttVarianceEma = alpha * (diff * diff) + (1.0 - alpha) * _rttVarianceEma;
        }

        _serverTimeOffset = (serverTimestamp + halfRtt) - localNow;
    }

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

    /// <summary>Fired when the connection is fully established.</summary>
    public static event Action? OnConnected;

    /// <summary>Fired when the connection is lost or closed.</summary>
    public static event Action? OnDisconnected;

    /// <summary>Fired when the server accepts the client's authentication.</summary>
    public static event Action? OnAuthenticated;

    /// <summary>Fired when the server rejects the client's authentication. Parameter: reason (may be null).</summary>
    public static event Action<string?>? OnAuthRejected;

    private static void HandleAuthResult(NetworkReader reader)
    {
        bool accepted = reader.ReadBool();
        string? reason = reader.ReadString();
        if (accepted)
            OnAuthenticated?.Invoke();
        else
            OnAuthRejected?.Invoke(reason);
    }
}
