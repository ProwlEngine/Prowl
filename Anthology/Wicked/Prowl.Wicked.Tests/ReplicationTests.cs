using System.Numerics;
using Prowl.Wicked.Transport;

namespace Prowl.Wicked.Tests;

/// <summary>
/// End-to-end replication tests using InMemoryTransport.
/// Verifies that server-side entity/map operations are correctly replicated to connected clients.
/// </summary>
public class ReplicationTests : IDisposable
{
    private readonly InMemoryServerTransport _serverTransport;
    private readonly InMemoryClientTransport _clientTransport;

    public ReplicationTests()
    {
        _serverTransport = new InMemoryServerTransport();
        _clientTransport = new InMemoryClientTransport(_serverTransport);

        Server.Transport = _serverTransport;
        Client.Transport = _clientTransport;
    }

    public void Dispose()
    {
        if (Server.Active) Server.Stop();
        Server.Reset();
        Client.Reset();
    }

    private void Tick(int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            Server.Tick();
            Client.Tick();
        }
    }

    private RemoteClient ConnectAndSetup()
    {
        Server.Start(0);
        Client.Connect("localhost", 0);
        Tick();
        return Server.Clients.First();
    }

    // ── Entity spawn replication ──

    [Fact]
    public void ClientReceivesEntitySpawnAfterBecomingObserver()
    {
        var serverClient = ConnectAndSetup();
        var map = Server.CreateMap<TestMap>();
        var npc = Server.Spawn<TestEntity>(map, e => e.Position = new Vector2(10, 5));
        var player = Server.Spawn<TestEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(player);

        // Client needs to process the messages
        Tick();

        var clientEntity = Client.FindEntity(npc.NetworkId);
        Assert.NotNull(clientEntity);
    }

    [Fact]
    public void ClientEntityHasCorrectProperties()
    {
        var serverClient = ConnectAndSetup();
        var map = Server.CreateMap<TestMap>();
        var npc = Server.Spawn<TestEntity>(map, e => { e.Position = new Vector2(10, 5); e.FacingRight = false; });
        var player = Server.Spawn<TestEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(player);

        Tick();

        var clientNpc = Client.FindEntity<TestEntity>(npc.NetworkId);
        Assert.NotNull(clientNpc);
        Assert.Equal(npc.NetworkId, clientNpc.NetworkId);
        Assert.Equal(new Vector2(10, 5), clientNpc.Position);
        Assert.False(clientNpc.FacingRight);
        Assert.Equal(0u, clientNpc.OwnerClientId);
        Assert.True(clientNpc.IsClient);
        Assert.True(clientNpc.IsSpawned);
    }

    [Fact]
    public void ClientEntityMapIsSetCorrectly()
    {
        var serverClient = ConnectAndSetup();
        var map = Server.CreateMap<TestMap>();
        var npc = Server.Spawn<TestEntity>(map, e => e.Position = new Vector2(10, 5));
        var player = Server.Spawn<TestEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(player);

        Tick();

        var clientNpc = Client.FindEntity(npc.NetworkId);
        Assert.NotNull(clientNpc);
        Assert.NotNull(clientNpc.Map);
        Assert.Equal(map.MapId, clientNpc.Map.MapId);
    }

    [Fact]
    public void ClientFiresOnSpawnAndOnStartClient()
    {
        var serverClient = ConnectAndSetup();
        var map = Server.CreateMap<TestMap>();
        var serverEntity = Server.Spawn<LifecycleTrackingEntity>(map);
        var player = Server.Spawn<TestEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(player);

        Tick();

        var clientEntity = Client.FindEntity<LifecycleTrackingEntity>(serverEntity.NetworkId);
        Assert.NotNull(clientEntity);
        Assert.Contains("OnSpawn", clientEntity.CallbackLog);
        Assert.Contains("OnStartClient", clientEntity.CallbackLog);
        // Verify ordering: OnSpawn before OnStartClient
        Assert.True(clientEntity.CallbackLog.IndexOf("OnSpawn") < clientEntity.CallbackLog.IndexOf("OnStartClient"));
    }

    [Fact]
    public void ClientFiresOnStartOwnerForOwnedEntity()
    {
        var serverClient = ConnectAndSetup();
        var map = Server.CreateMap<TestMap>();

        // Set the client's ClientId so ownership can be detected

        var serverEntity = Server.Spawn<LifecycleTrackingEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(serverEntity);

        Tick();

        var clientEntity = Client.FindEntity<LifecycleTrackingEntity>(serverEntity.NetworkId);
        Assert.NotNull(clientEntity);
        Assert.Contains("OnStartOwner", clientEntity.CallbackLog);
        Assert.True(clientEntity.IsOwner);
    }

    [Fact]
    public void OwnedEntityHasCorrectOwnerClientId()
    {
        var serverClient = ConnectAndSetup();
        var map = Server.CreateMap<TestMap>();


        var serverEntity = Server.Spawn<TestEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(serverEntity);

        Tick();

        var clientEntity = Client.FindEntity(serverEntity.NetworkId);
        Assert.NotNull(clientEntity);
        Assert.Equal(serverClient.ClientId, clientEntity.OwnerClientId);
    }

    // ── Entity despawn replication ──

    [Fact]
    public void ClientReceivesEntityDespawn()
    {
        var serverClient = ConnectAndSetup();
        var map = Server.CreateMap<TestMap>();
        var npc = Server.Spawn<TestEntity>(map);
        var player = Server.Spawn<TestEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(player);

        Tick(); // Client gets spawn messages

        Assert.NotNull(Client.FindEntity(npc.NetworkId));

        Server.Despawn(npc);
        Tick(); // Client gets despawn message

        Assert.Null(Client.FindEntity(npc.NetworkId));
    }

    [Fact]
    public void ClientFiresTeardownCallbacksOnDespawn()
    {
        var serverClient = ConnectAndSetup();
        var map = Server.CreateMap<TestMap>();
        var serverEntity = Server.Spawn<LifecycleTrackingEntity>(map);
        var player = Server.Spawn<TestEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(player);

        Tick();

        var clientEntity = Client.FindEntity<LifecycleTrackingEntity>(serverEntity.NetworkId);
        Assert.NotNull(clientEntity);
        clientEntity.CallbackLog.Clear();

        Server.Despawn(serverEntity);
        Tick();

        Assert.Contains("OnStopClient", clientEntity.CallbackLog);
        Assert.Contains("OnDespawn", clientEntity.CallbackLog);
        // Verify ordering: OnStopClient before OnDespawn
        Assert.True(clientEntity.CallbackLog.IndexOf("OnStopClient") < clientEntity.CallbackLog.IndexOf("OnDespawn"));
    }

    [Fact]
    public void ClientFiresOnStopOwnerBeforeOnStopClientOnOwnedDespawn()
    {
        var serverClient = ConnectAndSetup();
        var map = Server.CreateMap<TestMap>();


        var serverEntity = Server.Spawn<LifecycleTrackingEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(serverEntity);

        Tick();

        var clientEntity = Client.FindEntity<LifecycleTrackingEntity>(serverEntity.NetworkId);
        Assert.NotNull(clientEntity);
        Assert.True(clientEntity.IsOwner);
        clientEntity.CallbackLog.Clear();

        Server.Despawn(serverEntity);
        Tick();

        Assert.Contains("OnStopOwner", clientEntity.CallbackLog);
        Assert.Contains("OnStopClient", clientEntity.CallbackLog);
        Assert.Contains("OnDespawn", clientEntity.CallbackLog);
        // Verify ordering: OnStopOwner → OnStopClient → OnDespawn
        var ownerIdx = clientEntity.CallbackLog.IndexOf("OnStopOwner");
        var clientIdx = clientEntity.CallbackLog.IndexOf("OnStopClient");
        var despawnIdx = clientEntity.CallbackLog.IndexOf("OnDespawn");
        Assert.True(ownerIdx < clientIdx);
        Assert.True(clientIdx < despawnIdx);
    }

    // ── Owner change replication ──

    [Fact]
    public void ClientReceivesOwnerChange()
    {
        var serverClient = ConnectAndSetup();
        var map = Server.CreateMap<TestMap>();


        var npc = Server.Spawn<TestEntity>(map);
        var player = Server.Spawn<TestEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(player);

        Tick();

        var clientNpc = Client.FindEntity(npc.NetworkId);
        Assert.NotNull(clientNpc);
        Assert.Equal(0u, clientNpc.OwnerClientId);
        Assert.False(clientNpc.IsOwner);

        // Transfer ownership of npc to the client
        Server.ChangeOwner(npc, serverClient);
        Tick();

        Assert.Equal(serverClient.ClientId, clientNpc.OwnerClientId);
        Assert.True(clientNpc.IsOwner);
    }

    [Fact]
    public void ClientFiresOnOwnerChangedCallback()
    {
        var serverClient = ConnectAndSetup();
        var map = Server.CreateMap<TestMap>();


        var serverEntity = Server.Spawn<LifecycleTrackingEntity>(map);
        var player = Server.Spawn<TestEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(player);

        Tick();

        var clientEntity = Client.FindEntity<LifecycleTrackingEntity>(serverEntity.NetworkId);
        Assert.NotNull(clientEntity);
        clientEntity.CallbackLog.Clear();

        Server.ChangeOwner(serverEntity, serverClient);
        Tick();

        Assert.Contains("OnOwnerChanged", clientEntity.CallbackLog);
    }

    // ── Map create/destroy replication ──

    [Fact]
    public void ClientCreatesMapFromMapCreateMessage()
    {
        var serverClient = ConnectAndSetup();
        var map = Server.CreateMap<TestMap>();


        var player = Server.Spawn<TestEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(player);

        Tick();

        var clientMap = Client.GetMap(map.MapId);
        Assert.NotNull(clientMap);
        Assert.Equal(map.MapId, clientMap.MapId);
        Assert.True(clientMap.IsClient);
    }

    [Fact]
    public void ClientDestroysMapFromMapDestroyMessage()
    {
        var serverClient = ConnectAndSetup();
        var map = Server.CreateMap<TestMap>();


        var player = Server.Spawn<TestEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(player);

        Tick();
        Assert.NotNull(Client.GetMap(map.MapId));

        Server.DestroyMap(map.MapId);
        Tick();

        Assert.Null(Client.GetMap(map.MapId));
    }

    // ── PackSpawnData / UnpackSpawnData round-trip ──

    [Fact]
    public void PackSpawnDataUnpackSpawnDataRoundTrips()
    {
        var serverClient = ConnectAndSetup();
        var map = Server.CreateMap<TestMap>();


        var serverEntity = Server.Spawn<SpawnDataEntity>(map, e =>
        {
            e.CustomName = "Shopkeeper";
            e.Health = 42;
        });
        var player = Server.Spawn<TestEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(player);

        Tick();

        var clientEntity = Client.FindEntity<SpawnDataEntity>(serverEntity.NetworkId);
        Assert.NotNull(clientEntity);
        Assert.Equal("Shopkeeper", clientEntity.CustomName);
        Assert.Equal(42, clientEntity.Health);
    }

    // ── Map transfer replication ──

    [Fact]
    public void TransferEntitySendsCorrectMessages()
    {
        var serverClient = ConnectAndSetup();
        var town = Server.CreateMap<TestMap>();
        var dungeon = Server.CreateMap<TestMap>();


        var npc = Server.Spawn<TestEntity>(town, e => e.Position = new Vector2(5, 5));
        var player = Server.Spawn<TestEntity>(town, serverClient);
        serverClient.AssignPlayerEntity(player);

        Tick();

        // Client should see npc in town
        Assert.NotNull(Client.FindEntity(npc.NetworkId));

        // Transfer player to dungeon
        town.TransferEntity(player, dungeon);
        Tick();

        // Client should now see player in dungeon, and the town npc should be despawned
        var clientMap = Client.GetMap(dungeon.MapId);
        Assert.NotNull(clientMap);

        // The npc in town is no longer visible (town was left)
        Assert.Null(Client.FindEntity(npc.NetworkId));

        // Player is still tracked
        Assert.NotNull(Client.FindEntity(player.NetworkId));
    }

    [Fact]
    public void TransferEntityUpdatesClientPlayerEntity()
    {
        var serverClient = ConnectAndSetup();
        var town = Server.CreateMap<TestMap>();
        var dungeon = Server.CreateMap<TestMap>();

        var player = Server.Spawn<TestEntity>(town, serverClient);
        serverClient.AssignPlayerEntity(player);

        Tick();

        Assert.NotNull(Client.LocalClient!.PlayerEntity);
        Assert.Equal(player.NetworkId, Client.LocalClient.PlayerEntity!.NetworkId);

        // Transfer player to dungeon
        town.TransferEntity(player, dungeon);
        Tick();

        // Client.LocalClient.PlayerEntity must reference the NEW entity object (not the despawned one)
        var clientPlayer = Client.LocalClient!.PlayerEntity;
        Assert.NotNull(clientPlayer);
        Assert.Equal(player.NetworkId, clientPlayer!.NetworkId);
        Assert.True(clientPlayer.IsSpawned);
        Assert.NotNull(clientPlayer.Map);
        Assert.Equal(dungeon.MapId, clientPlayer.Map!.MapId);
    }

    // ── Disconnect teardown ──

    [Fact]
    public void DisconnectTearsDownClientEntities()
    {
        var serverClient = ConnectAndSetup();
        var map = Server.CreateMap<TestMap>();


        var serverEntity = Server.Spawn<LifecycleTrackingEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(serverEntity);

        Tick();

        var clientEntity = Client.FindEntity<LifecycleTrackingEntity>(serverEntity.NetworkId);
        Assert.NotNull(clientEntity);
        clientEntity.CallbackLog.Clear();

        Client.Disconnect();

        // Teardown callbacks should have fired in order: OnStopOwner → OnStopClient → OnDespawn
        Assert.Contains("OnStopOwner", clientEntity.CallbackLog);
        Assert.Contains("OnStopClient", clientEntity.CallbackLog);
        Assert.Contains("OnDespawn", clientEntity.CallbackLog);
        var ownerIdx = clientEntity.CallbackLog.IndexOf("OnStopOwner");
        var clientIdx = clientEntity.CallbackLog.IndexOf("OnStopClient");
        var despawnIdx = clientEntity.CallbackLog.IndexOf("OnDespawn");
        Assert.True(ownerIdx < clientIdx);
        Assert.True(clientIdx < despawnIdx);
    }

    // ── Multiple entities in same map ──

    [Fact]
    public void MultipleEntitiesInSameMapAllReplicate()
    {
        var serverClient = ConnectAndSetup();
        var map = Server.CreateMap<TestMap>();

        var e1 = Server.Spawn<TestEntity>(map, e => e.Position = new Vector2(1, 1));
        var e2 = Server.Spawn<TestEntity>(map, e => e.Position = new Vector2(2, 2));
        var e3 = Server.Spawn<TestEntity>(map, e => e.Position = new Vector2(3, 3));
        var player = Server.Spawn<TestEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(player);

        Tick();

        Assert.NotNull(Client.FindEntity(e1.NetworkId));
        Assert.NotNull(Client.FindEntity(e2.NetworkId));
        Assert.NotNull(Client.FindEntity(e3.NetworkId));
        Assert.NotNull(Client.FindEntity(player.NetworkId));
        Assert.Equal(4, Client.Entities.Count);
    }

    // ── Entity spawned after observer already watching ──

    [Fact]
    public void EntitySpawnedAfterObserverJoinsIsReplicated()
    {
        var serverClient = ConnectAndSetup();
        var map = Server.CreateMap<TestMap>();


        var player = Server.Spawn<TestEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(player);

        Tick();

        // Now spawn a new entity after the client is already observing
        var npc = Server.Spawn<TestEntity>(map, e => e.Position = new Vector2(20, 0));
        Tick();

        Assert.NotNull(Client.FindEntity(npc.NetworkId));
    }

    // ── Player entity assignment replication ──

    [Fact]
    public void ClientCurrentMapReturnsMapAfterPlayerEntityAssigned()
    {
        var serverClient = ConnectAndSetup();
        var map = Server.CreateMap<TestMap>();

        var player = Server.Spawn<TestEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(player);

        Tick();

        // Client.CurrentMap chains through LocalClient.PlayerEntity.Map.
        // If the server never tells the client which entity is the player entity,
        // LocalClient.PlayerEntity is null and CurrentMap returns null.
        Assert.NotNull(Client.LocalClient);
        Assert.NotNull(Client.LocalClient!.PlayerEntity);
        Assert.Equal(player.NetworkId, Client.LocalClient.PlayerEntity!.NetworkId);
        Assert.NotNull(Client.CurrentMap);
        Assert.Equal(map.MapId, Client.CurrentMap!.MapId);
    }

    [Fact]
    public void ClientPlayerEntityClearedAfterUnassign()
    {
        var serverClient = ConnectAndSetup();
        var map = Server.CreateMap<TestMap>();

        var player = Server.Spawn<TestEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(player);
        Tick();

        serverClient.UnassignPlayerEntity();
        Tick();

        Assert.Null(Client.LocalClient!.PlayerEntity);
        Assert.Null(Client.CurrentMap);
    }

    // ── ClientTick ──

    [Fact]
    public void ClientTick_CallsEntityClientTick()
    {
        var serverClient = ConnectAndSetup();
        var map = Server.CreateMap<TestMap>();


        var serverEntity = Server.Spawn<LifecycleTrackingEntity>(map);
        var player = Server.Spawn<TestEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(player);

        Tick(); // Replicate entities to client

        var clientEntity = Client.FindEntity<LifecycleTrackingEntity>(serverEntity.NetworkId);
        Assert.NotNull(clientEntity);
        clientEntity.CallbackLog.Clear();

        Client.Tick();

        Assert.Contains("ClientTick", clientEntity.CallbackLog);
    }
}

// ── Test helper types for replication ──

public class SpawnDataEntity : NetworkEntity
{
    public string CustomName { get; set; } = "";
    public int Health { get; set; }

    public override void PackSpawnData(NetworkWriter writer)
    {
        writer.WriteString(CustomName);
        writer.WriteInt(Health);
    }

    public override void UnpackSpawnData(NetworkReader reader)
    {
        CustomName = reader.ReadString() ?? "";
        Health = reader.ReadInt();
    }
}
