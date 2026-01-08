namespace Prowl.Wicked.Network;

using Prowl.Wicked.Core;
using Prowl.Wicked.Tools;
using Prowl.Wicked.Transport;

/// <summary>
/// Main network orchestrator that manages server and client connections.
/// </summary>
public static class NetworkManager
{
    /// <summary>
    /// The transport used for networking.
    /// </summary>
    public static ITransport? Transport { get; set; }

    /// <summary>
    /// The current network mode.
    /// </summary>
    public static NetworkMode Mode { get; private set; } = NetworkMode.Offline;

    /// <summary>
    /// True if running as a server (Server or Host mode).
    /// </summary>
    public static bool IsServer => Mode is NetworkMode.Server or NetworkMode.Host;

    /// <summary>
    /// True if running as a client (Client or Host mode).
    /// </summary>
    public static bool IsClient => Mode is NetworkMode.Client or NetworkMode.Host;

    /// <summary>
    /// True if running in host mode (both server and client).
    /// </summary>
    public static bool IsHost => Mode == NetworkMode.Host;

    /// <summary>
    /// True if currently offline.
    /// </summary>
    public static bool IsOffline => Mode == NetworkMode.Offline;

    /// <summary>
    /// Event raised when the network starts in any mode.
    /// </summary>
    public static event Action<NetworkMode>? OnNetworkStarted;

    /// <summary>
    /// Event raised when the network stops.
    /// </summary>
    public static event Action? OnNetworkStopped;

    /// <summary>
    /// Initializes the network manager with a transport.
    /// This must be called before any networking operations.
    /// Scans all loaded assemblies for EntityBehaviour types and registers them.
    /// </summary>
    public static void Initialize(ITransport transport)
    {
        // Initialize behaviour registry first (scans assemblies for EntityBehaviour types)
        BehaviourRegistry.Initialize();

        Transport = transport;
        Transport.Initialize();

        // Subscribe to game loop events for ticking
        GameLoop.OnPostUpdate += Tick;
    }

    /// <summary>
    /// Shuts down the network manager.
    /// </summary>
    public static void Shutdown()
    {
        Stop();
        GameLoop.OnPostUpdate -= Tick;
        Transport?.Shutdown();
        Transport = null;
    }

    /// <summary>
    /// Starts as a dedicated server.
    /// </summary>
    public static void StartServer(int port)
    {
        if (Mode != NetworkMode.Offline)
        {
            Console.WriteLine("NetworkManager: Already running. Stop first.");
            return;
        }

        if (Transport == null)
        {
            Console.WriteLine("NetworkManager: No transport configured. Call Initialize() first.");
            return;
        }

        // Create world if needed
        if (World.Active == null)
            World.Create();

        Mode = NetworkMode.Server;
        NetworkServer.Start();
        Transport.ServerStart(port);

        Console.WriteLine($"NetworkManager: Started server on port {port}");
        OnNetworkStarted?.Invoke(Mode);
    }

    /// <summary>
    /// Starts as a client and connects to a server.
    /// </summary>
    public static void StartClient(string address, int port)
    {
        if (Mode != NetworkMode.Offline)
        {
            Console.WriteLine("NetworkManager: Already running. Stop first.");
            return;
        }

        if (Transport == null)
        {
            Console.WriteLine("NetworkManager: No transport configured. Call Initialize() first.");
            return;
        }

        // Create world if needed
        if (World.Active == null)
            World.Create();

        Mode = NetworkMode.Client;
        NetworkClient.Start();
        Transport.ClientConnect(address, port);

        Console.WriteLine($"NetworkManager: Connecting to {address}:{port}");
        OnNetworkStarted?.Invoke(Mode);
    }

    /// <summary>
    /// Starts in host mode (server + local client).
    /// </summary>
    public static void StartHost(int port)
    {
        if (Mode != NetworkMode.Offline)
        {
            Console.WriteLine("NetworkManager: Already running. Stop first.");
            return;
        }

        if (Transport == null)
        {
            Console.WriteLine("NetworkManager: No transport configured. Call Initialize() first.");
            return;
        }

        // Create world if needed
        if (World.Active == null)
            World.Create();

        Mode = NetworkMode.Host;

        // Start server
        NetworkServer.Start();
        Transport.ServerStart(port);

        // Add local connection for host
        NetworkServer.AddLocalConnection();

        // Start client in local mode
        NetworkClient.Start();
        NetworkClient.SetupLocalConnection(NetworkServer.LocalConnection!);

        Console.WriteLine($"NetworkManager: Started host on port {port}");
        OnNetworkStarted?.Invoke(Mode);
    }

    /// <summary>
    /// Stops all networking.
    /// </summary>
    public static void Stop()
    {
        if (Mode == NetworkMode.Offline) return;

        Console.WriteLine("NetworkManager: Stopping...");

        // Stop client
        if (IsClient)
        {
            Transport?.ClientDisconnect();
            NetworkClient.Stop();
        }

        // Stop server
        if (IsServer)
        {
            Transport?.ServerStop();
            NetworkServer.Stop();
        }

        // Clear the world
        World.Clear();

        Mode = NetworkMode.Offline;
        OnNetworkStopped?.Invoke();

        Console.WriteLine("NetworkManager: Stopped");
    }

    /// <summary>
    /// Spawns a new entity on the network.
    /// Must be called on the server.
    /// Note: Add all behaviours BEFORE calling this if you need initial sync data.
    /// </summary>
    public static Entity Spawn()
    {
        if (!IsServer)
            throw new InvalidOperationException("Spawn can only be called on the server.");

        if (World.Active == null)
            throw new InvalidOperationException("No active world.");

        var entity = World.Active.CreateEntity();

        // Setup network state
        entity.IsServer = true;
        entity.IsClient = IsHost; // In host mode, server entities are also client entities

        // Start the entity
        World.Active.Spawn(entity, true, IsHost);

        // Notify clients
        NetworkServer.SpawnEntity(entity);

        return entity;
    }

    /// <summary>
    /// Spawns a new entity with a behaviour attached.
    /// This ensures the behaviour is present before the spawn message is sent.
    /// </summary>
    public static Entity Spawn<T>() where T : EntityBehaviour, new()
    {
        if (!IsServer)
            throw new InvalidOperationException("Spawn can only be called on the server.");

        if (World.Active == null)
            throw new InvalidOperationException("No active world.");

        var entity = World.Active.CreateEntity();
        entity.AddBehaviour<T>();

        // Setup network state
        entity.IsServer = true;
        entity.IsClient = IsHost;

        // Start the entity (calls OnStartServer after sync data is initialized)
        World.Active.Spawn(entity, true, IsHost);

        // Notify clients (now includes behaviour and sync data)
        NetworkServer.SpawnEntity(entity);

        return entity;
    }

    /// <summary>
    /// Creates an entity without sending spawn message.
    /// Use FinalizeSpawn() after adding behaviours to send the spawn message.
    /// </summary>
    public static Entity CreateDeferred()
    {
        if (!IsServer)
            throw new InvalidOperationException("CreateDeferred can only be called on the server.");

        if (World.Active == null)
            throw new InvalidOperationException("No active world.");

        var entity = World.Active.CreateEntity();
        entity.IsServer = true;
        entity.IsClient = IsHost;

        return entity;
    }

    /// <summary>
    /// Finalizes a deferred spawn by starting the entity and sending spawn message.
    /// Call this after adding all behaviours.
    /// </summary>
    /// <param name="entity">The entity to spawn</param>
    /// <param name="owner">The owning connection (null for server-owned)</param>
    /// <param name="isLocalPlayer">True if this is the player entity for the owner connection</param>
    public static void FinalizeSpawn(Entity entity, NetworkConnection? owner = null, bool isLocalPlayer = false)
    {
        if (!IsServer)
            throw new InvalidOperationException("FinalizeSpawn can only be called on the server.");

        if (owner != null)
        {
            entity.Owner = owner;
            owner.AddOwnedEntity(entity.NetId);

            if (isLocalPlayer)
            {
                owner.PlayerEntity = entity;
            }
        }

        // On the SERVER machine, IsLocalPlayer should only be true if:
        // 1. We're in host mode (server + client on same machine)
        // 2. This entity's owner IS the local connection (host's player)
        // 3. This is marked as a player entity
        bool isLocalOnThisMachine = IsHost && owner == NetworkServer.LocalConnection && isLocalPlayer;
        bool isOwnedOnThisMachine = IsHost && owner == NetworkServer.LocalConnection;

        // Start the entity with correct local flags
        World.Active?.Spawn(entity, true, IsHost, isLocalOnThisMachine, isOwnedOnThisMachine);

        // Notify clients (SpawnMessage will set IsLocalPlayer per-connection)
        NetworkServer.SpawnEntity(entity);
    }

    /// <summary>
    /// Spawns a new entity with a specific owner.
    /// Must be called on the server.
    /// </summary>
    public static Entity SpawnWithOwner(NetworkConnection owner)
    {
        var entity = Spawn();
        entity.Owner = owner;
        entity.IsOwned = IsHost && owner == NetworkServer.LocalConnection;
        owner.AddOwnedEntity(entity.NetId);

        // Re-send spawn message with ownership info
        // (In a real implementation, we'd include this in the initial spawn)

        return entity;
    }

    /// <summary>
    /// Spawns a player entity for a connection.
    /// Must be called on the server.
    /// </summary>
    public static Entity SpawnPlayer(NetworkConnection owner)
    {
        var entity = SpawnWithOwner(owner);
        entity.IsLocalPlayer = true;
        owner.PlayerEntity = entity;
        return entity;
    }

    /// <summary>
    /// Despawns an entity from the network.
    /// Must be called on the server.
    /// </summary>
    public static void Despawn(Entity entity)
    {
        if (!IsServer)
            throw new InvalidOperationException("Despawn can only be called on the server.");

        if (World.Active == null) return;

        // Notify clients before destroying
        NetworkServer.DespawnEntity(entity);

        // Remove from owner
        if (entity.Owner != null)
        {
            entity.Owner.RemoveOwnedEntity(entity.NetId);
            if (entity.Owner.PlayerEntity == entity)
                entity.Owner.PlayerEntity = null;
        }

        // Destroy locally
        World.Active.DestroyEntity(entity);
    }

    // for tick rate limiting
    static double lastServerSendTime;

    /// <summary>
    /// Called every frame to process network messages.
    /// </summary>
    private static void Tick()
    {
        // Update network time at the start of each frame
        NetworkTime.EarlyUpdate();

        // Process transport messages
        Transport?.Tick();

        // Server tick
        if (IsServer && World.Active != null)
        {
            // Update entity visibility (AOI)
            NetworkServer.UpdateVisibility();

            // Update pings for all connections
            NetworkServer.UpdateConnectionPings();

            // Only send sync data at the configured sendRate
            if (AccurateInterval.Elapsed(NetworkTime.localTime, NetworkServer.sendInterval, ref lastServerSendTime))
            {
                // Send sync data updates
                foreach (var entity in World.Active.Entities.Values)
                {
                    if (entity.IsDirty)
                    {
                        NetworkServer.SendSyncData(entity);
                    }
                }
            }
        }

        // Client tick
        if (IsClient && !IsHost) // Host doesn't need to send pings to itself
        {
            // Update ping time (sends NetworkPingMessage)
            NetworkTime.UpdateClient();

            // Update connection quality
            NetworkClient.UpdateConnectionQuality();
        }
    }
}
