namespace Prowl.Wicked.Network;

using Prowl.Wicked.Core;
using Prowl.Wicked.Network.Messages;
using Prowl.Wicked.Network.Rpc;
using Prowl.Wicked.Network.Serialization;
using Prowl.Wicked.Tools;
using Prowl.Wicked.Transport;

/// <summary>
/// Handles client-side networking logic.
/// </summary>
public static class NetworkClient
{
    private static readonly Dictionary<ushort, Action<NetworkReader>> _handlers = new();

    // Batch spawning support (like Mirror's ObjectSpawnStarted/Finished)
    private static bool _isSpawnFinished = true;
    private static readonly Dictionary<uint, SpawnMessage> _pendingSpawns = new();

    /// <summary>
    /// The current connection state.
    /// </summary>
    internal static ConnectState ConnectState { get; private set; } = ConnectState.None;

    /// <summary>
    /// True if client is connecting or connected (network is active).
    /// </summary>
    public static bool Active => ConnectState == ConnectState.Connecting ||
                                  ConnectState == ConnectState.Connected;

    /// <summary>
    /// True if currently connecting (before connected).
    /// </summary>
    public static bool IsConnecting => ConnectState == ConnectState.Connecting;

    /// <summary>
    /// True if currently connected to the server.
    /// </summary>
    public static bool IsConnected => ConnectState == ConnectState.Connected;

    /// <summary>
    /// True if the client is ready to receive game state.
    /// </summary>
    public static bool IsReady { get; private set; }

    /// <summary>
    /// The local connection to the server.
    /// </summary>
    public static NetworkConnection? Connection { get; private set; }

    /// <summary>
    /// The local player entity (only one per client).
    /// Set when receiving a SpawnMessage with IsLocalPlayer = true.
    /// </summary>
    public static Entity? LocalPlayer { get; private set; }

    /// <summary>
    /// If true, exceptions during message handling will disconnect from the server.
    /// This is for security - prevents attackers from causing crashes with malformed data.
    /// Recommended to keep this true in production.
    /// </summary>
    public static bool exceptionsDisconnect = true;

    /// <summary>
    /// The current connection quality based on RTT and jitter.
    /// </summary>
    public static ConnectionQuality connectionQuality { get; private set; } = ConnectionQuality.ESTIMATING;

    /// <summary>
    /// The method used to calculate connection quality.
    /// </summary>
    public static ConnectionQualityMethod connectionQualityMethod = ConnectionQualityMethod.Simple;

    /// <summary>
    /// Client's local timeline - used for snapshot interpolation and NetworkTime.time.
    /// </summary>
    public static double localTimeline { get; internal set; }

    /// <summary>
    /// Event raised when the client connects to the server.
    /// </summary>
    public static event Action? OnConnected;

    /// <summary>
    /// Event raised when the client disconnects from the server.
    /// </summary>
    public static event Action? OnDisconnected;

    /// <summary>
    /// Event raised when a transport error occurs.
    /// </summary>
    public static event Action<string>? OnError;

    /// <summary>
    /// Starts the client and connects to a server.
    /// </summary>
    internal static void Start()
    {
        if (Active)
        {
            Console.WriteLine("NetworkClient: Already active");
            return;
        }

        ConnectState = ConnectState.Connecting;
        RegisterHandlers();

        // Hook into transport events
        var transport = NetworkManager.Transport;
        if (transport != null)
        {
            transport.OnClientConnect += OnTransportConnect;
            transport.OnClientDisconnect += OnTransportDisconnect;
            transport.OnClientData += OnTransportData;
            transport.OnClientError += OnTransportError;
        }
    }

    /// <summary>
    /// Disconnects from the server.
    /// </summary>
    public static void Disconnect()
    {
        // Only if connected or connecting.
        // Don't disconnect() again if already in the process of
        // disconnecting or fully disconnected.
        if (ConnectState != ConnectState.Connecting &&
            ConnectState != ConnectState.Connected)
            return;

        // We are disconnecting until OnTransportDisconnected is called.
        ConnectState = ConnectState.Disconnecting;
        IsReady = false;

        // Call disconnect on the transport
        NetworkManager.Transport?.ClientDisconnect();
    }

    /// <summary>
    /// Stops the client (internal cleanup).
    /// </summary>
    internal static void Stop()
    {
        if (ConnectState == ConnectState.None) return;

        // Clean up all client entities
        DestroyAllClientObjects();

        _handlers.Clear();

        var transport = NetworkManager.Transport;
        if (transport != null)
        {
            transport.OnClientConnect -= OnTransportConnect;
            transport.OnClientDisconnect -= OnTransportDisconnect;
            transport.OnClientData -= OnTransportData;
            transport.OnClientError -= OnTransportError;
        }

        ConnectState = ConnectState.None;
        IsReady = false;
        Connection = null;
        LocalPlayer = null;

        // Reset batch spawning state
        _isSpawnFinished = true;
        _pendingSpawns.Clear();

        // Clear events
        OnConnected = null;
        OnDisconnected = null;
        OnError = null;
    }

    /// <summary>
    /// Destroys all networked objects on the client.
    /// </summary>
    private static void DestroyAllClientObjects()
    {
        if (World.Active == null) return;

        try
        {
            foreach (var entity in World.Active.Entities.Values.ToArray())
            {
                if (entity != null)
                {
                    // Call OnStopLocalPlayer if applicable
                    if (entity.IsLocalPlayer)
                    {
                        foreach (var behaviour in entity.Behaviours)
                            behaviour.OnStopLocalPlayer();
                    }

                    // Call OnStopClient
                    foreach (var behaviour in entity.Behaviours)
                        behaviour.OnStopClient();

                    // In host mode, don't destroy server-owned objects
                    bool shouldDestroy = !entity.IsServer;
                    if (shouldDestroy)
                    {
                        World.Active.DestroyEntity(entity);
                    }
                }
            }
        }
        catch (InvalidOperationException e)
        {
            Console.WriteLine($"NetworkClient: Error destroying client objects: {e.Message}");
        }
    }

    /// <summary>
    /// Registers a message handler.
    /// </summary>
    public static void RegisterHandler<T>(Action<T> handler) where T : INetworkMessage, new()
    {
        var messageId = MessageRegistry.GetMessageId<T>();
        _handlers[messageId] = (reader) =>
        {
            var message = new T();
            message.Deserialize(reader);
            handler(message);
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
        RegisterHandler<SpawnStartedMessage>(OnSpawnStarted);
        RegisterHandler<SpawnFinishedMessage>(OnSpawnFinished);
        RegisterHandler<SpawnMessage>(OnSpawnMessage);
        RegisterHandler<DespawnMessage>(OnDespawnMessage);
        RegisterHandler<SyncDataMessage>(OnSyncDataMessage);
        RegisterHandler<RpcMessage>(OnRpcMessage);
        RegisterHandler<OwnershipMessage>(OnOwnershipMessage);

        // Ping/Pong for RTT calculation
        RegisterHandler<NetworkPingMessage>(msg =>
        {
            NetworkTime.OnClientPing(msg);
        });

        RegisterHandler<NetworkPongMessage>(msg =>
        {
            NetworkTime.OnClientPong(msg);
        });
    }

    private static void OnSpawnStarted(SpawnStartedMessage _)
    {
        // Clear any previous pending spawns and mark that we're in batch mode
        _pendingSpawns.Clear();
        _isSpawnFinished = false;
    }

    private static void OnSpawnFinished(SpawnFinishedMessage _)
    {
        // Process all pending spawns in order of NetId
        // This ensures consistent ordering like Mirror does
        foreach (var kvp in _pendingSpawns.OrderBy(x => x.Key))
        {
            ProcessSpawnMessage(kvp.Value);
        }

        _pendingSpawns.Clear();
        _isSpawnFinished = true;
    }

    private static void OnSpawnMessage(SpawnMessage msg)
    {
        if (World.Active == null)
        {
            Console.WriteLine("NetworkClient: No active world for spawn");
            return;
        }

        // If we're in a batch (between SpawnStarted and SpawnFinished),
        // defer processing until all spawns are received.
        // This prevents issues with cross-references between entities.
        if (!_isSpawnFinished)
        {
            _pendingSpawns[msg.NetId] = msg;
            return;
        }

        // Process immediately if we're not in batch mode
        ProcessSpawnMessage(msg);
    }

    private static void ProcessSpawnMessage(SpawnMessage msg)
    {
        if (World.Active == null) return;

        // Check if entity already exists
        var existing = World.Active.FindEntity(msg.NetId);
        if (existing != null)
        {
            Console.WriteLine($"NetworkClient: Entity {msg.NetId} already exists");
            return;
        }

        // Create the entity
        var entity = World.Active.CreateEntity(msg.NetId);

        // Create behaviours from indices using BehaviourRegistry
        for (int i = 0; i < msg.BehaviourIndices.Length; i++)
        {
            var behaviourIndex = msg.BehaviourIndices[i];
            var behaviour = BehaviourRegistry.CreateInstance(behaviourIndex);
            entity.AddBehaviour(behaviour);

            // Apply initial sync data if available
            if (i < msg.BehaviourSyncData.Length && msg.BehaviourSyncData[i] != null)
            {
                behaviour.ApplySyncData(msg.BehaviourSyncData[i]);
            }
        }

        // Determine ownership
        bool isOwned = msg.OwnerId == Connection?.ConnectionId;

        // Track local player - only ONE entity per client has this
        if (msg.IsLocalPlayer)
        {
            LocalPlayer = entity;
            Console.WriteLine($"NetworkClient: Set LocalPlayer to entity {msg.NetId}");
        }

        // IsLocalPlayer is true ONLY if this entity IS our tracked LocalPlayer
        // This ensures only ONE entity has IsLocalPlayer = true
        bool isLocalPlayer = (entity == LocalPlayer);

        // Spawn the entity (isServer = false, isClient = true)
        World.Active.Spawn(entity, false, true, isLocalPlayer, isOwned);

        Console.WriteLine($"NetworkClient: Spawned entity {msg.NetId}, LocalPlayer={isLocalPlayer}, Owned={isOwned}");
    }

    private static void OnDespawnMessage(DespawnMessage msg)
    {
        var entity = World.Active?.FindEntity(msg.NetId);
        if (entity != null)
        {
            World.Active?.DestroyEntity(entity);
            Console.WriteLine($"NetworkClient: Despawned entity {msg.NetId}");
        }
    }

    private static void OnSyncDataMessage(SyncDataMessage msg)
    {
        var entity = World.Active?.FindEntity(msg.NetId);
        if (entity == null) return;

        var behaviour = entity.GetBehaviourByIndex(msg.BehaviourIndex);
        if (behaviour != null)
        {
            behaviour.ApplyDeltaSyncData(msg.DirtyMask, msg.SyncData);
        }
    }

    private static void OnRpcMessage(RpcMessage msg)
    {
        var entity = World.Active?.FindEntity(msg.NetId);
        if (entity == null)
        {
            Console.WriteLine($"NetworkClient: RPC target entity {msg.NetId} not found");
            return;
        }

        // Find the behaviour and invoke the method
        if (msg.BehaviourIndex < entity.Behaviours.Count)
        {
            var behaviour = entity.Behaviours[msg.BehaviourIndex];
            InvokeRpc(behaviour, msg.MethodName, msg.Arguments);
        }
    }

    /// <summary>
    /// Handles ownership change message from server.
    /// Like Mirror's ChangeOwner - handles authority and local player changes.
    /// </summary>
    private static void OnOwnershipMessage(OwnershipMessage msg)
    {
        var entity = World.Active?.FindEntity(msg.NetId);
        if (entity == null)
        {
            Console.WriteLine($"NetworkClient: OnOwnershipMessage - Entity {msg.NetId} not found");
            return;
        }

        Console.WriteLine($"NetworkClient: OnOwnershipMessage - NetId={msg.NetId}, IsOwner={msg.IsOwner}, IsLocalPlayer={msg.IsLocalPlayer}");

        // Was local player before, but not anymore?
        // Call OnStopLocalPlayer BEFORE setting new values
        if (entity.IsLocalPlayer && !msg.IsLocalPlayer)
        {
            entity.NotifyLocalPlayerStopped();
        }

        // Set ownership flag (aka authority)
        bool wasOwned = entity.IsOwned;
        entity.IsOwned = msg.IsOwner;

        // Add/Remove from connection's owned set
        if (Connection != null)
        {
            if (entity.IsOwned)
                Connection.AddOwnedEntity(entity.NetId);
            else
                Connection.RemoveOwnedEntity(entity.NetId);
        }

        // Call OnStartAuthority / OnStopAuthority using Entity's notify methods
        // These handle the flags properly to prevent double-calling
        if (!wasOwned && entity.IsOwned)
        {
            entity.NotifyAuthorityGained();
        }
        else if (wasOwned && !entity.IsOwned)
        {
            entity.NotifyAuthorityLost();
        }

        // Set local player flag
        entity.IsLocalPlayer = msg.IsLocalPlayer;

        // Entity is now local player - update our static helper field
        if (msg.IsLocalPlayer)
        {
            LocalPlayer = entity;
            entity.NotifyLocalPlayerStarted();
        }
        // Entity's isLocalPlayer was set to false
        // Clear our static LocalPlayer IF it was this entity
        else if (LocalPlayer == entity)
        {
            LocalPlayer = null;
        }
    }

    private static void InvokeRpc(EntityBehaviour behaviour, string methodName, byte[] arguments)
    {
        var reader = new NetworkReader(arguments);
        behaviour.InvokeRpc(methodName, reader);
    }

    /// <summary>
    /// Sends a message to the server.
    /// </summary>
    public static void Send<T>(T message) where T : INetworkMessage
    {
        if (!Active) return;

        var writer = new NetworkWriter();
        var messageId = MessageRegistry.GetMessageId<T>();
        writer.WriteUShort(messageId);
        message.Serialize(writer);

        NetworkManager.Transport?.ClientSend(writer.ToArraySegment());
    }

    /// <summary>
    /// Sends a command to the server.
    /// </summary>
    public static void SendCommand(uint netId, byte behaviourIndex, string methodName, byte[] arguments)
    {
        var message = new CommandMessage
        {
            NetId = netId,
            BehaviourIndex = behaviourIndex,
            MethodName = methodName,
            Arguments = arguments
        };
        Send(message);
    }

    /// <summary>
    /// Marks the client as ready to receive game state.
    /// </summary>
    public static void Ready()
    {
        if (!Active || IsReady) return;

        IsReady = true;
        Send(new ReadyMessage());
    }

    // Transport event handlers

    private static void OnTransportConnect()
    {
        if (Connection == null)
        {
            Connection = new NetworkConnection(-1) // Client doesn't know its own ID
            {
                Address = "localhost",
                IsAuthenticated = true
            };
        }

        // Set connected state before invoking callbacks
        ConnectState = ConnectState.Connected;

        Console.WriteLine("NetworkClient: Connected to server");
        OnConnected?.Invoke();

        // Auto-ready for now
        Ready();
    }

    private static void OnTransportDisconnect()
    {
        // Prevent running cleanup twice if already disconnected
        if (ConnectState == ConnectState.Disconnected) return;

        // Raise the event before changing state because 'Active' depends on this
        OnDisconnected?.Invoke();

        ConnectState = ConnectState.Disconnected;
        IsReady = false;

        // Clean up all client entities
        DestroyAllClientObjects();

        // Clear the connection
        Connection = null;

        Console.WriteLine("NetworkClient: Disconnected from server");
    }

    private static void OnTransportError(string error)
    {
        // Transport errors happen and are generally recoverable
        Console.WriteLine($"NetworkClient: Transport error: {error}");
        OnError?.Invoke(error);
    }

    private static void OnTransportData(ArraySegment<byte> data)
    {
        if (Connection != null)
            Connection.LastMessageTime = NetworkTime.localTime;

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
                Console.WriteLine($"NetworkClient: Disconnecting because reading message ID caused an Exception: {ex}");
                Disconnect();
            }
            else
            {
                Console.WriteLine($"NetworkClient: Error reading message ID: {ex}");
            }
            return;
        }

        if (_handlers.TryGetValue(messageId, out var handler))
        {
            try
            {
                handler(reader);
            }
            catch (Exception ex)
            {
                // should we disconnect on exceptions?
                if (exceptionsDisconnect)
                {
                    Console.WriteLine($"NetworkClient: Disconnecting because handling message {messageId} caused an Exception. This can happen if the other side accidentally (or an attacker intentionally) sent invalid data. Reason: {ex}");
                    Disconnect();
                }
                else
                {
                    Console.WriteLine($"NetworkClient: Error handling message {messageId}: {ex}");
                }
            }
        }
        else
        {
            Console.WriteLine($"NetworkClient: Unknown message ID {messageId}");
        }
    }

    /// <summary>
    /// Sets up the local connection for host mode.
    /// </summary>
    internal static void SetupLocalConnection(NetworkConnection localConnection)
    {
        Connection = localConnection;
        ConnectState = ConnectState.Connected;
        IsReady = true;
        OnConnected?.Invoke();
    }

    /// <summary>
    /// Updates connection quality based on RTT.
    /// Called each frame from NetworkManager.
    /// </summary>
    internal static void UpdateConnectionQuality()
    {
        if (!IsConnected) return;

        // Calculate connection quality based on chosen method
        if (connectionQualityMethod == ConnectionQualityMethod.Simple)
        {
            connectionQuality = ConnectionQualityHeuristics.Simple(
                NetworkTime.rtt,
                NetworkTime.rttStandardDeviation);
        }
        // Pragmatic method would need snapshot interpolation buffer time
        // For now, fall back to Simple if Pragmatic is chosen
        else
        {
            connectionQuality = ConnectionQualityHeuristics.Simple(
                NetworkTime.rtt,
                NetworkTime.rttStandardDeviation);
        }
    }
}
