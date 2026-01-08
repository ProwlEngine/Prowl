namespace Prowl.Wicked.Network;

using Prowl.Wicked.Core;
using Prowl.Wicked.Interest;
using Prowl.Wicked.Network.Messages;
using Prowl.Wicked.Network.Rpc;
using Prowl.Wicked.Network.Serialization;
using Prowl.Wicked.Tools;
using Prowl.Wicked.Transport;

/// <summary>
/// Handles server-side networking logic.
/// </summary>
public static class NetworkServer
{
    private static readonly Dictionary<int, NetworkConnection> _connections = new();
    private static readonly Dictionary<ushort, Action<NetworkConnection, NetworkReader>> _handlers = new();
    private static uint _nextNetId = 1;

    /// <summary>
    /// True if the server is currently active.
    /// </summary>
    public static bool Active { get; private set; }

    // tick rate is defined as 'sendRate' because that's more obvious.
    // for example, 30 Hz would mean 30 state updates per second.
    /// <summary>
    /// Server send rate in Hz. Determines how often state updates are sent to clients.
    /// 30 Hz for fast paced games like Counter-Strike.
    /// 10 Hz for slow paced games like Dota, Starcraft, etc.
    /// </summary>
    public static int sendRate = 30;

    /// <summary>
    /// Calculated send interval based on sendRate.
    /// </summary>
    public static float sendInterval => 1f / sendRate;

    /// <summary>
    /// If true, exceptions during message handling will disconnect the connection.
    /// This is for security - prevents attackers from causing crashes with malformed data.
    /// Recommended to keep this true in production.
    /// </summary>
    public static bool exceptionsDisconnect = true;

    /// <summary>
    /// Maximum number of concurrent connections allowed.
    /// Set to 0 for unlimited connections.
    /// </summary>
    public static int MaxConnections { get; set; } = 100;

    /// <summary>
    /// Whether to automatically disconnect inactive connections.
    /// </summary>
    public static bool DisconnectInactiveConnections { get; set; } = false;

    /// <summary>
    /// Time in seconds before an inactive connection is disconnected.
    /// </summary>
    public static float DisconnectInactiveTimeout { get; set; } = 60f;

    /// <summary>
    /// The interest manager used for area of interest filtering.
    /// Defaults to DefaultInterestManager which makes all entities visible to all connections.
    /// Like Mirror's NetworkServer.aoi.
    /// </summary>
    public static InterestManagementBase? InterestManager { get; set; } = DefaultInterestManager.Instance;

    /// <summary>
    /// All connected clients.
    /// </summary>
    public static IReadOnlyDictionary<int, NetworkConnection> Connections => _connections;

    /// <summary>
    /// The local connection (for host mode).
    /// </summary>
    public static NetworkConnection? LocalConnection { get; internal set; }

    /// <summary>
    /// Event raised when a client connects.
    /// </summary>
    public static event Action<NetworkConnection>? OnClientConnected;

    /// <summary>
    /// Event raised when a client disconnects.
    /// </summary>
    public static event Action<NetworkConnection>? OnClientDisconnected;

    /// <summary>
    /// Event raised when the server starts.
    /// </summary>
    public static event Action? OnServerStarted;

    /// <summary>
    /// Event raised when the server stops.
    /// </summary>
    public static event Action? OnServerStopped;

    /// <summary>
    /// Gets the next available network ID for spawning entities.
    /// </summary>
    internal static uint GetNextNetId()
    {
        return _nextNetId++;
    }

    /// <summary>
    /// Resets the network ID counter.
    /// </summary>
    internal static void ResetNetIdCounter()
    {
        _nextNetId = 1;
    }

    /// <summary>
    /// Starts the server.
    /// </summary>
    internal static void Start()
    {
        if (Active)
        {
            Console.WriteLine("NetworkServer: Already active");
            return;
        }

        Active = true;
        RegisterHandlers();

        // Hook into transport events
        var transport = NetworkManager.Transport;
        if (transport != null)
        {
            transport.OnServerConnect += OnTransportConnect;
            transport.OnServerDisconnect += OnTransportDisconnect;
            transport.OnServerData += OnTransportData;
            transport.OnServerError += OnTransportError;
        }

        OnServerStarted?.Invoke();
    }

    /// <summary>
    /// Stops the server.
    /// </summary>
    internal static void Stop()
    {
        if (!Active) return;

        // Disconnect all clients
        foreach (var conn in _connections.Values.ToArray())
        {
            DisconnectClient(conn.ConnectionId);
        }

        _connections.Clear();
        _handlers.Clear();

        // Reset interest manager state
        InterestManager?.ResetState();

        var transport = NetworkManager.Transport;
        if (transport != null)
        {
            transport.OnServerConnect -= OnTransportConnect;
            transport.OnServerDisconnect -= OnTransportDisconnect;
            transport.OnServerData -= OnTransportData;
            transport.OnServerError -= OnTransportError;
        }

        Active = false;
        LocalConnection = null;
        ResetNetIdCounter();

        OnServerStopped?.Invoke();
    }

    /// <summary>
    /// Registers a message handler.
    /// </summary>
    /// <param name="handler">The handler to call when the message is received.</param>
    /// <param name="requireAuthentication">If true, connection must be authenticated before handler is called.</param>
    public static void RegisterHandler<T>(Action<NetworkConnection, T> handler, bool requireAuthentication = true) where T : INetworkMessage, new()
    {
        var messageId = MessageRegistry.GetMessageId<T>();
        _handlers[messageId] = (conn, reader) =>
        {
            // Check authentication if required
            if (requireAuthentication && !conn.IsAuthenticated)
            {
                Console.WriteLine($"NetworkServer: Disconnecting connection {conn}. Received message {typeof(T).Name} that required authentication, but the user has not authenticated yet");
                DisconnectClient(conn.ConnectionId);
                return;
            }

            var message = new T();
            message.Deserialize(reader);
            handler(conn, message);
        };
    }

    /// <summary>
    /// Unregisters a message handler.
    /// </summary>
    public static void UnregisterHandler<T>() where T : INetworkMessage, new()
    {
        var messageId = MessageRegistry.GetMessageId<T>();
        _handlers.Remove(messageId);
    }

    private static void RegisterHandlers()
    {
        // Register built-in message handlers
        RegisterHandler<ReadyMessage>((conn, msg) =>
        {
            conn.IsReady = true;
            // Send initial state to ready client
            SendSpawnMessagesToClient(conn);
        });

        RegisterHandler<CommandMessage>((conn, msg) =>
        {
            HandleCommand(conn, msg);
        });

        // Ping/Pong for RTT calculation
        RegisterHandler<NetworkPingMessage>((conn, msg) =>
        {
            NetworkTime.OnServerPing(conn, msg);
        });

        RegisterHandler<NetworkPongMessage>((conn, msg) =>
        {
            NetworkTime.OnServerPong(conn, msg);
        });
    }

    private static void HandleCommand(NetworkConnection sender, CommandMessage msg)
    {
        if (World.Active == null)
        {
            Console.WriteLine($"NetworkServer: Cannot handle command, no active world");
            return;
        }

        var entity = World.Active.FindEntity(msg.NetId);
        if (entity == null)
        {
            Console.WriteLine($"NetworkServer: Entity {msg.NetId} not found for command {msg.MethodName}");
            return;
        }

        if (msg.BehaviourIndex >= entity.Behaviours.Count)
        {
            Console.WriteLine($"NetworkServer: Behaviour index {msg.BehaviourIndex} out of range for entity {msg.NetId}");
            return;
        }

        var behaviour = entity.Behaviours[msg.BehaviourIndex];
        var reader = new NetworkReader(msg.Arguments);
        behaviour.InvokeCommand(msg.MethodName, reader, sender);
    }

    /// <summary>
    /// Sends spawn messages for all visible entities to a newly ready client.
    /// Uses RebuildObservers to determine initial visibility.
    /// </summary>
    private static void SendSpawnMessagesToClient(NetworkConnection conn)
    {
        if (World.Active == null) return;

        // Send SpawnStarted to signal batch beginning
        // This allows clients to defer processing until all spawns are received
        Send(conn, new SpawnStartedMessage());

        // Rebuild observers for all entities - this will send spawn messages
        // for entities that become visible to this connection
        foreach (var entity in World.Active.Entities.Values)
        {
            RebuildObservers(entity, true);
        }

        // Send SpawnFinished to signal batch completion
        // Clients can now process all spawns, resolving any cross-references
        Send(conn, new SpawnFinishedMessage());
    }

    // ============ Observer Management (like Mirror) ============

    /// <summary>
    /// Shows an entity to a connection by sending a spawn message.
    /// Like Mirror's NetworkServer.ShowForConnection.
    /// </summary>
    internal static void ShowForConnection(Entity entity, NetworkConnection conn)
    {
        if (conn.IsReady)
        {
            SendSpawnMessage(conn, entity);
        }
    }

    /// <summary>
    /// Hides an entity from a connection by sending a despawn message.
    /// Like Mirror's NetworkServer.HideForConnection.
    /// </summary>
    internal static void HideForConnection(Entity entity, NetworkConnection conn)
    {
        Send(conn, new DespawnMessage { NetId = entity.NetId });
    }

    /// <summary>
    /// Rebuilds the observers for an entity.
    /// Like Mirror's NetworkServer.RebuildObservers.
    /// </summary>
    /// <param name="entity">The entity to rebuild observers for.</param>
    /// <param name="initialize">True if this is the initial rebuild (first spawn).</param>
    public static void RebuildObservers(Entity entity, bool initialize)
    {
        // If there's an interest manager, use it
        if (InterestManager != null)
        {
            InterestManager.Rebuild(entity, initialize);
        }
        else
        {
            // No interest manager - use default behavior (everyone sees everything)
            RebuildObserversDefault(entity, initialize);
        }
    }

    /// <summary>
    /// Default observer rebuild - adds all ready connections.
    /// Like Mirror's RebuildObserversDefault.
    /// </summary>
    private static void RebuildObserversDefault(Entity entity, bool initialize)
    {
        // Only add all connections when rebuilding the first time
        if (initialize)
        {
            AddAllReadyConnectionsToObservers(entity);
        }
    }

    /// <summary>
    /// Adds all ready connections as observers of an entity.
    /// Like Mirror's AddAllReadyServerConnectionsToObservers.
    /// </summary>
    private static void AddAllReadyConnectionsToObservers(Entity entity)
    {
        foreach (var conn in _connections.Values)
        {
            if (conn.IsReady)
            {
                entity.AddObserver(conn);
                conn.AddToObserving(entity);
            }
        }
    }

    /// <summary>
    /// Called each server tick to check for inactive connections.
    /// Interest management rebuild is handled by the InterestManagement class itself.
    /// </summary>
    internal static void UpdateVisibility()
    {
        if (!Active || World.Active == null) return;

        // Check for inactive connections
        CheckForInactiveConnections();
    }

    /// <summary>
    /// Sends a message to a specific client.
    /// </summary>
    public static void Send<T>(NetworkConnection conn, T message) where T : INetworkMessage
    {
        if (!Active) return;

        var writer = new NetworkWriter();
        var messageId = MessageRegistry.GetMessageId<T>();
        writer.WriteUShort(messageId);
        message.Serialize(writer);

        NetworkManager.Transport?.ServerSend(conn.ConnectionId, writer.ToArraySegment());
    }

    /// <summary>
    /// Sends a message to all connected clients.
    /// </summary>
    public static void SendToAll<T>(T message) where T : INetworkMessage
    {
        foreach (var conn in _connections.Values.ToArray())
        {
            Send(conn, message);
        }
    }

    /// <summary>
    /// Sends a message to all ready clients.
    /// </summary>
    public static void SendToReady<T>(T message) where T : INetworkMessage
    {
        foreach (var conn in _connections.Values.ToArray())
        {
            if (conn.IsReady)
                Send(conn, message);
        }
    }

    /// <summary>
    /// Disconnects a client.
    /// </summary>
    public static void DisconnectClient(int connectionId)
    {
        NetworkManager.Transport?.ServerDisconnect(connectionId);
    }

    /// <summary>
    /// Gets a connection by ID.
    /// </summary>
    public static NetworkConnection? GetConnection(int connectionId)
    {
        return _connections.TryGetValue(connectionId, out var conn) ? conn : null;
    }

    /// <summary>
    /// Spawns an entity on clients based on interest.
    /// Like Mirror's SpawnObject - calls OnSpawned then RebuildObservers.
    /// </summary>
    internal static void SpawnEntity(Entity entity)
    {
        if (!Active) return;

        // Notify interest manager (for custom implementations)
        try
        {
            InterestManager?.OnSpawned(entity);
        }
        catch (Exception e)
        {
            Console.WriteLine($"NetworkServer: Exception in OnSpawned: {e}");
        }

        // Rebuild observers - this will send spawn messages to appropriate connections
        RebuildObservers(entity, true);
    }

    /// <summary>
    /// Despawns an entity on clients that were observing it.
    /// Like Mirror's UnSpawn.
    /// </summary>
    internal static void DespawnEntity(Entity entity)
    {
        if (!Active) return;

        // Notify interest manager
        try
        {
            InterestManager?.OnDestroyed(entity);
        }
        catch (Exception e)
        {
            Console.WriteLine($"NetworkServer: Exception in OnDestroyed: {e}");
        }

        // Send despawn to all observers and remove from their observing lists
        var message = new DespawnMessage { NetId = entity.NetId };
        foreach (var conn in entity.Observers.Values.ToArray())
        {
            conn.RemoveFromObserving(entity, true); // true = entity is being destroyed
            Send(conn, message);
        }

        // Clear observers on the entity
        entity.ClearObservers();
    }

    private static void SendSpawnMessage(NetworkConnection conn, Entity entity)
    {
        // Collect behaviour indices from registry
        var behaviourIndices = new byte[entity.Behaviours.Count];
        for (int i = 0; i < entity.Behaviours.Count; i++)
        {
            behaviourIndices[i] = BehaviourRegistry.GetIndex(entity.Behaviours[i].GetType());
        }

        // Collect sync data for each behaviour
        var behaviourSyncData = new object?[entity.Behaviours.Count][];
        for (int i = 0; i < entity.Behaviours.Count; i++)
        {
            var behaviour = entity.Behaviours[i];
            // For initial spawn, send all sync data
            var syncData = new object?[EntityBehaviour.SyncSlotCount];
            for (int j = 0; j < EntityBehaviour.SyncSlotCount; j++)
            {
                syncData[j] = behaviour.SyncData[j];
            }
            behaviourSyncData[i] = syncData;
        }

        var message = new SpawnMessage
        {
            NetId = entity.NetId,
            OwnerId = entity.Owner?.ConnectionId ?? -1,
            IsLocalPlayer = entity.Owner == conn,
            BehaviourIndices = behaviourIndices,
            BehaviourSyncData = behaviourSyncData
        };

        // Debug: Log what we're sending
        Console.WriteLine($"[SERVER] SendSpawnMessage: NetId={message.NetId}, Behaviours={behaviourIndices.Length}, IsLocalPlayer={message.IsLocalPlayer}, OwnerId={message.OwnerId}");
        if (behaviourSyncData.Length > 0 && behaviourSyncData[0] != null)
        {
            var syncData = behaviourSyncData[0];
            Console.WriteLine($"  SyncData[0]: X={syncData[0]}, Y={syncData[1]}, Health={syncData[2]}, Color={syncData[3]}");
        }

        Send(conn, message);
    }

    /// <summary>
    /// Sends sync data updates to all observers of an entity.
    /// </summary>
    internal static void SendSyncData(Entity entity)
    {
        if (!Active || !entity.IsDirty) return;

        // Send a SyncDataMessage for each dirty behaviour to observers only
        for (int i = 0; i < entity.Behaviours.Count; i++)
        {
            var behaviour = entity.Behaviours[i];
            if (!behaviour.IsDirty) continue;

            var message = new SyncDataMessage
            {
                NetId = entity.NetId,
                BehaviourIndex = (byte)i,
                DirtyMask = behaviour.DirtyMask,
                SyncData = GetDirtyData(behaviour)
            };

            // Send to all observers of this entity
            foreach (var conn in entity.Observers.Values)
            {
                if (conn.IsReady)
                {
                    Send(conn, message);
                }
            }
        }

        entity.ClearAllDirty();
    }

    private static object?[] GetDirtyData(Core.EntityBehaviour behaviour)
    {
        var dirtyCount = BitCount(behaviour.DirtyMask);
        var data = new object?[dirtyCount];
        int dataIndex = 0;

        for (int i = 0; i < Core.EntityBehaviour.SyncSlotCount; i++)
        {
            if ((behaviour.DirtyMask & (1u << i)) != 0)
            {
                data[dataIndex++] = behaviour.SyncData[i];
            }
        }

        return data;
    }

    private static int BitCount(uint value)
    {
        int count = 0;
        while (value != 0)
        {
            count += (int)(value & 1);
            value >>= 1;
        }
        return count;
    }

    // Transport event handlers

    private static void OnTransportConnect(int connectionId, string address)
    {
        // Validate connection
        if (!IsConnectionAllowed(connectionId, address, out string? reason))
        {
            Console.WriteLine($"NetworkServer: Rejected connection {connectionId} from {address}: {reason}");
            NetworkManager.Transport?.ServerDisconnect(connectionId);
            return;
        }

        var conn = new NetworkConnection(connectionId)
        {
            Address = address,
            IsAuthenticated = true, // For now, auto-authenticate
            LastMessageTime = NetworkTime.localTime
        };
        _connections[connectionId] = conn;

        Console.WriteLine($"NetworkServer: Client connected - {conn}");
        OnClientConnected?.Invoke(conn);
    }

    /// <summary>
    /// Validates whether a new connection should be allowed.
    /// </summary>
    private static bool IsConnectionAllowed(int connectionId, string address, out string? reason)
    {
        reason = null;

        // Check for duplicate connection ID (shouldn't happen, but safety check)
        if (_connections.ContainsKey(connectionId))
        {
            reason = "Duplicate connection ID";
            return false;
        }

        // Check max connections (exclude local host connection from count)
        if (MaxConnections > 0)
        {
            int remoteConnectionCount = _connections.Count(c => c.Key != 0);
            if (remoteConnectionCount >= MaxConnections)
            {
                reason = "Server is full";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks for and disconnects inactive connections.
    /// Called during UpdateVisibility tick.
    /// </summary>
    private static void CheckForInactiveConnections()
    {
        if (!DisconnectInactiveConnections) return;

        var currentTime = NetworkTime.localTime;
        foreach (var conn in _connections.Values.ToArray())
        {
            // Skip local host connection
            if (conn == LocalConnection) continue;

            // Check if connection has timed out
            if (currentTime - conn.LastMessageTime > DisconnectInactiveTimeout)
            {
                Console.WriteLine($"NetworkServer: Disconnecting inactive client {conn.ConnectionId} (timeout: {DisconnectInactiveTimeout}s)");
                DisconnectClient(conn.ConnectionId);
            }
        }
    }

    /// <summary>
    /// Updates ping for all connections. Sends ping messages and calculates RTT.
    /// </summary>
    internal static void UpdateConnectionPings()
    {
        foreach (var conn in _connections.Values)
        {
            // Skip local host connection
            if (conn == LocalConnection) continue;

            // Send ping if interval has elapsed
            if (NetworkTime.localTime >= conn.LastPingTime + NetworkTime.PingInterval)
            {
                // TODO: it would be safer for the server to store the last N
                // messages' timestamp and only send a message number.
                // This way clients can't just modify the timestamp.
                NetworkPingMessage pingMessage = new NetworkPingMessage(NetworkTime.localTime, 0);
                Send(conn, pingMessage);
                conn.LastPingTime = NetworkTime.localTime;
            }
        }
    }

    /// <summary>
    /// Sends a time snapshot to all ready clients for snapshot interpolation.
    /// Called every sendInterval.
    /// </summary>
    internal static void SendTimeSnapshotToAll()
    {
        // Create time snapshot message with current server time
        var message = new TimeSnapshotMessage(NetworkTime.localTime);

        // Send to all ready clients (except host's local connection)
        foreach (var conn in _connections.Values)
        {
            if (conn == LocalConnection) continue;
            if (!conn.IsReady) continue;

            Send(conn, message);
        }
    }

    private static void OnTransportDisconnect(int connectionId)
    {
        if (_connections.TryGetValue(connectionId, out var conn))
        {
            // Remove this connection from all entities' observer lists
            conn.RemoveFromObservingsObservers();

            // Clean up owned entities
            foreach (var netId in conn.OwnedEntities.ToArray())
            {
                var entity = World.Active?.FindEntity(netId);
                if (entity != null)
                {
                    entity.RemoveOwnership();
                }
            }

            // Run connection cleanup
            conn.Cleanup();

            _connections.Remove(connectionId);
            Console.WriteLine($"NetworkServer: Client disconnected - {conn}");
            OnClientDisconnected?.Invoke(conn);
        }
    }

    private static void OnTransportError(int connectionId, string error)
    {
        // Log the error - transport errors happen and are generally recoverable
        Console.WriteLine($"NetworkServer: Transport error for connection {connectionId}: {error}");

        // Optionally disconnect the problematic client
        if (_connections.ContainsKey(connectionId))
        {
            DisconnectClient(connectionId);
        }
    }

    private static void OnTransportData(int connectionId, ArraySegment<byte> data)
    {
        if (!_connections.TryGetValue(connectionId, out var conn))
            return;

        conn.LastMessageTime = NetworkTime.localTime;

        var reader = new NetworkReader(data);

        // try to read message id
        ushort messageId;
        try
        {
            messageId = reader.ReadUShort();
        }
        catch (Exception ex)
        {
            if (exceptionsDisconnect)
            {
                Console.WriteLine($"NetworkServer: Disconnecting connection {conn} because reading message ID caused an Exception: {ex}");
                DisconnectClient(connectionId);
            }
            else
            {
                Console.WriteLine($"NetworkServer: Error reading message ID from {conn}: {ex}");
            }
            return;
        }

        if (_handlers.TryGetValue(messageId, out var handler))
        {
            try
            {
                handler(conn, reader);
            }
            catch (Exception ex)
            {
                // should we disconnect on exceptions?
                if (exceptionsDisconnect)
                {
                    Console.WriteLine($"NetworkServer: Disconnecting connection {conn} because handling message {messageId} caused an Exception. This can happen if the other side accidentally (or an attacker intentionally) sent invalid data. Reason: {ex}");
                    DisconnectClient(connectionId);
                }
                else
                {
                    Console.WriteLine($"NetworkServer: Error handling message {messageId} from {conn}: {ex}");
                }
            }
        }
        else
        {
            Console.WriteLine($"NetworkServer: Unknown message ID {messageId}");
        }
    }

    /// <summary>
    /// Adds the local connection for host mode.
    /// </summary>
    internal static void AddLocalConnection()
    {
        LocalConnection = new NetworkConnection(0)
        {
            Address = "localhost",
            IsAuthenticated = true,
            IsReady = true
        };
        _connections[0] = LocalConnection;
    }

    /// <summary>
    /// Sends an ownership change message to a specific connection.
    /// Like Mirror's SendChangeOwnerMessage.
    /// </summary>
    internal static void SendChangeOwnerMessage(Entity entity, NetworkConnection conn)
    {
        // Don't send if entity isn't spawned
        if (entity.NetId == 0) return;

        // Don't send if conn isn't observing the entity
        // (may be excluded by interest management)
        if (!entity.HasObserver(conn)) return;

        // Determine if this connection is the owner
        bool isOwner = entity.Owner == conn;

        // Determine if this is the local player for this connection
        // LocalPlayer means: it's the main player entity assigned to this connection
        // AND the connection owns it
        bool isLocalPlayer = isOwner && conn.OwnedEntities.Contains(entity.NetId) && entity.IsLocalPlayer;

        Console.WriteLine($"[SERVER] SendChangeOwnerMessage: NetId={entity.NetId}, Conn={conn.ConnectionId}, IsOwner={isOwner}, IsLocalPlayer={isLocalPlayer}");

        Send(conn, new OwnershipMessage
        {
            NetId = entity.NetId,
            IsOwner = isOwner,
            IsLocalPlayer = isLocalPlayer
        });
    }

    /// <summary>
    /// Sends ownership change to all observers of an entity.
    /// Called when ownership changes.
    /// </summary>
    internal static void BroadcastOwnershipChange(Entity entity)
    {
        if (!Active || entity.NetId == 0) return;

        // Send to all observers of this entity
        foreach (var conn in entity.Observers.Values.ToArray())
        {
            SendChangeOwnerMessage(entity, conn);
        }
    }
}
