using System.Numerics;
using Prowl.Wicked;
using Prowl.Wicked.Transport;

namespace Prowl.Wicked.Tests;

/// <summary>
/// Tests the observation model: which clients observe which entities based on
/// map membership.
/// these tests verify the logical visibility rules that drive it.
/// </summary>
public class ObservationTests : IDisposable
{
    private readonly InMemoryServerTransport _serverTransport;
    private readonly List<InMemoryClientTransport> _clientTransports = new();

    public ObservationTests()
    {
        _serverTransport = new InMemoryServerTransport();
        Server.Transport = _serverTransport;
        Server.Start(0);
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

    /// <summary>
    /// Connects a client via InMemoryTransport and returns the server-side RemoteClient.
    /// Each call creates a fresh transport, so multiple clients can coexist.
    /// </summary>
    private RemoteClient ConnectAndGetServerClient()
    {
        var transport = new InMemoryClientTransport(_serverTransport);
        _clientTransports.Add(transport);

        // Use a temporary Client connection to trigger the server-side accept
        Client.Reset();
        Client.Transport = transport;
        Client.Connect("localhost", 0);
        Tick();

        return Server.Clients.Last();
    }

    /// <summary>
    /// Helper: spawn a player entity for a client, assign it, and return it.
    /// </summary>
    private TestEntity SpawnAndAssignPlayer(RemoteClient client, Map map)
    {
        var entity = Server.Spawn<TestEntity>(map, client);
        client.AssignPlayerEntity(entity);
        return entity;
    }

    // ── Basic observation ──

    [Fact]
    public void ClientBecomesObserverWhenPlayerEntityAssigned()
    {
        var client = ConnectAndGetServerClient();
        var map = Server.CreateMap<TestMap>();

        var player = SpawnAndAssignPlayer(client, map);

        Assert.Contains(client, map.Observers);
        Assert.Single(map.Observers);
    }

    [Fact]
    public void ClientStopsObservingWhenPlayerEntityUnassigned()
    {
        var client = ConnectAndGetServerClient();
        var map = Server.CreateMap<TestMap>();
        SpawnAndAssignPlayer(client, map);

        client.UnassignPlayerEntity();

        Assert.Empty(map.Observers);
    }

    [Fact]
    public void ObserverSeesAllEntitiesInTheirMap()
    {
        var client = ConnectAndGetServerClient();
        var map = Server.CreateMap<TestMap>();

        var npc1 = Server.Spawn<TestEntity>(map, e => e.Position = new Vector2(10, 5));
        var npc2 = Server.Spawn<TestEntity>(map, e => e.Position = new Vector2(-5, 12));
        var player = SpawnAndAssignPlayer(client, map);

        // Client is an observer of the map → should see all 3 entities
        Assert.Contains(client, map.Observers);
        Assert.Equal(3, map.Entities.Count);
        Assert.Contains(npc1, map.Entities);
        Assert.Contains(npc2, map.Entities);
        Assert.Contains(player, map.Entities);
    }

    [Fact]
    public void ObserverDoesNotSeeEntitiesInOtherMaps()
    {
        var client = ConnectAndGetServerClient();
        var town = Server.CreateMap<TestMap>();
        var dungeon = Server.CreateMap<TestMap>();

        SpawnAndAssignPlayer(client, town);
        var dungeonBoss = Server.Spawn<TestEntity>(dungeon);

        Assert.Contains(client, town.Observers);
        Assert.DoesNotContain(client, dungeon.Observers);
        Assert.DoesNotContain(dungeonBoss, town.Entities);
    }

    // ── Multiple clients in the same map ──

    [Fact]
    public void MultipleClientsObserveSameMap()
    {
        var client1 = ConnectAndGetServerClient();
        var client2 = ConnectAndGetServerClient();
        var map = Server.CreateMap<TestMap>();

        SpawnAndAssignPlayer(client1, map);
        SpawnAndAssignPlayer(client2, map);

        Assert.Equal(2, map.Observers.Count);
        Assert.Contains(client1, map.Observers);
        Assert.Contains(client2, map.Observers);
    }

    [Fact]
    public void MultipleClientsSeeSameEntities()
    {
        var client1 = ConnectAndGetServerClient();
        var client2 = ConnectAndGetServerClient();
        var map = Server.CreateMap<TestMap>();

        var npc = Server.Spawn<TestEntity>(map, e => e.Position = new Vector2(10, 5));
        var player1 = SpawnAndAssignPlayer(client1, map);
        var player2 = SpawnAndAssignPlayer(client2, map);

        // Both observers see the same 3 entities (npc + 2 players)
        Assert.Equal(3, map.Entities.Count);
        Assert.Contains(npc, map.Entities);
        Assert.Contains(player1, map.Entities);
        Assert.Contains(player2, map.Entities);
    }

    // ── Multiple clients in different maps ──

    [Fact]
    public void ClientsInDifferentMapsSeeOnlyTheirOwnMapEntities()
    {
        var client1 = ConnectAndGetServerClient();
        var client2 = ConnectAndGetServerClient();
        var town = Server.CreateMap<TestMap>();
        var dungeon = Server.CreateMap<TestMap>();

        var townNpc = Server.Spawn<TestEntity>(town, e => e.Position = new Vector2(10, 5));
        var boss = Server.Spawn<TestEntity>(dungeon);
        SpawnAndAssignPlayer(client1, town);
        SpawnAndAssignPlayer(client2, dungeon);

        // client1 observes town only
        Assert.Contains(client1, town.Observers);
        Assert.DoesNotContain(client1, dungeon.Observers);
        Assert.Contains(townNpc, town.Entities);
        Assert.DoesNotContain(boss, town.Entities);

        // client2 observes dungeon only
        Assert.Contains(client2, dungeon.Observers);
        Assert.DoesNotContain(client2, town.Observers);
        Assert.Contains(boss, dungeon.Entities);
        Assert.DoesNotContain(townNpc, dungeon.Entities);
    }

    // ── Entity spawned/despawned in observed map ──

    [Fact]
    public void EntitySpawnedInObservedMapIsVisible()
    {
        var client = ConnectAndGetServerClient();
        var map = Server.CreateMap<TestMap>();
        SpawnAndAssignPlayer(client, map);

        // Spawn an NPC after the client is already observing
        var npc = Server.Spawn<TestEntity>(map, e => e.Position = new Vector2(20, 0));

        Assert.Contains(npc, map.Entities);
        Assert.Contains(client, map.Observers);
    }

    [Fact]
    public void EntityDespawnedInObservedMapIsNoLongerVisible()
    {
        var client = ConnectAndGetServerClient();
        var map = Server.CreateMap<TestMap>();
        var npc = Server.Spawn<TestEntity>(map, e => e.Position = new Vector2(10, 5));
        SpawnAndAssignPlayer(client, map);

        Server.Despawn(npc);

        Assert.DoesNotContain(npc, map.Entities);
    }

    [Fact]
    public void EntitySpawnedInUnobservedMapIsNotVisibleToClient()
    {
        var client = ConnectAndGetServerClient();
        var town = Server.CreateMap<TestMap>();
        var dungeon = Server.CreateMap<TestMap>();
        SpawnAndAssignPlayer(client, town);

        var boss = Server.Spawn<TestEntity>(dungeon);

        // Client observes town, not dungeon
        Assert.DoesNotContain(client, dungeon.Observers);
        Assert.DoesNotContain(boss, town.Entities);
    }

    // ── Map transfer changes visibility ──

    [Fact]
    public void TransferToNewMap_ClientSeesNewMapEntities()
    {
        var client = ConnectAndGetServerClient();
        var town = Server.CreateMap<TestMap>();
        var dungeon = Server.CreateMap<TestMap>();

        var townNpc = Server.Spawn<TestEntity>(town, e => e.Position = new Vector2(10, 5));
        var boss = Server.Spawn<TestEntity>(dungeon);
        var player = SpawnAndAssignPlayer(client, town);

        // Before transfer: observes town
        Assert.Contains(client, town.Observers);
        Assert.DoesNotContain(client, dungeon.Observers);

        town.TransferEntity(player, dungeon);

        // After transfer: observes dungeon, not town
        Assert.DoesNotContain(client, town.Observers);
        Assert.Contains(client, dungeon.Observers);
        Assert.Contains(boss, dungeon.Entities);
        Assert.Contains(player, dungeon.Entities);
    }

    [Fact]
    public void TransferToNewMap_ClientStopsSeingOldMapEntities()
    {
        var client = ConnectAndGetServerClient();
        var town = Server.CreateMap<TestMap>();
        var dungeon = Server.CreateMap<TestMap>();

        var townNpc = Server.Spawn<TestEntity>(town, e => e.Position = new Vector2(10, 5));
        var player = SpawnAndAssignPlayer(client, town);

        town.TransferEntity(player, dungeon);

        // Client no longer observes town
        Assert.DoesNotContain(client, town.Observers);
        // townNpc is still in town, but client can't see it
        Assert.Contains(townNpc, town.Entities);
        Assert.DoesNotContain(townNpc, dungeon.Entities);
    }

    [Fact]
    public void TransferToNewMap_OtherClientsInOldMapStopSeeingTransferredPlayer()
    {
        var client1 = ConnectAndGetServerClient();
        var client2 = ConnectAndGetServerClient();
        var town = Server.CreateMap<TestMap>();
        var dungeon = Server.CreateMap<TestMap>();

        var player1 = SpawnAndAssignPlayer(client1, town);
        SpawnAndAssignPlayer(client2, town);

        // Both in town initially
        Assert.Equal(2, town.Observers.Count);
        Assert.Contains(player1, town.Entities);

        town.TransferEntity(player1, dungeon);

        // player1 left town — client2 still observes town, but player1's entity is gone
        Assert.Single(town.Observers);
        Assert.Contains(client2, town.Observers);
        Assert.DoesNotContain(player1, town.Entities);
        Assert.Contains(player1, dungeon.Entities);
    }

    [Fact]
    public void TransferToNewMap_NewMapObserversSeeArrivingPlayer()
    {
        var client1 = ConnectAndGetServerClient();
        var client2 = ConnectAndGetServerClient();
        var town = Server.CreateMap<TestMap>();
        var dungeon = Server.CreateMap<TestMap>();

        var player1 = SpawnAndAssignPlayer(client1, town);
        SpawnAndAssignPlayer(client2, dungeon);

        // client2 in dungeon, doesn't see player1 yet
        Assert.DoesNotContain(player1, dungeon.Entities);

        town.TransferEntity(player1, dungeon);

        // player1 arrives in dungeon — client2 can see them
        Assert.Contains(player1, dungeon.Entities);
        Assert.Equal(2, dungeon.Observers.Count);
    }

    // ── Disconnect removes observer ──

    [Fact]
    public void DisconnectedClientRemovedFromObservers()
    {
        var client = ConnectAndGetServerClient();
        var map = Server.CreateMap<TestMap>();
        SpawnAndAssignPlayer(client, map);

        Assert.Single(map.Observers);

        client.Disconnect();
        Tick();

        Assert.Empty(map.Observers);
    }

    [Fact]
    public void DisconnectedClientDoesNotAffectOtherObservers()
    {
        var client1 = ConnectAndGetServerClient();
        var client2 = ConnectAndGetServerClient();
        var map = Server.CreateMap<TestMap>();

        SpawnAndAssignPlayer(client1, map);
        SpawnAndAssignPlayer(client2, map);

        Assert.Equal(2, map.Observers.Count);

        client1.Disconnect();
        Tick();

        Assert.Single(map.Observers);
        Assert.Contains(client2, map.Observers);
    }

    // ── Unowned entities visible to all map observers ──

    [Fact]
    public void UnownedEntitiesVisibleToAllObservers()
    {
        var client1 = ConnectAndGetServerClient();
        var client2 = ConnectAndGetServerClient();
        var map = Server.CreateMap<TestMap>();

        var npc = Server.Spawn<TestEntity>(map, e => e.Position = new Vector2(10, 5));
        var item = Server.Spawn<TestEntity>(map, e => e.Position = new Vector2(-3, 2));
        SpawnAndAssignPlayer(client1, map);
        SpawnAndAssignPlayer(client2, map);

        // Both clients observe the map, both see the unowned entities
        Assert.Equal(2, map.Observers.Count);
        Assert.Contains(npc, map.Entities);
        Assert.Contains(item, map.Entities);
        Assert.Equal(4, map.Entities.Count); // 2 players + npc + item
    }

    // ── Map destruction visibility ──

    [Fact]
    public void DestroyMap_RemovesAllObservers()
    {
        var client1 = ConnectAndGetServerClient();
        var client2 = ConnectAndGetServerClient();
        var map = Server.CreateMap<TrackingMap>();

        SpawnAndAssignPlayer(client1, map);
        SpawnAndAssignPlayer(client2, map);

        Assert.Equal(2, map.Observers.Count);

        Server.DestroyMap(map.MapId);

        Assert.Empty(map.Observers);
        Assert.Empty(map.Entities);
    }

    [Fact]
    public void MapWithEntitiesButNoObservers()
    {
        var map = Server.CreateMap<TestMap>();
        var npc = Server.Spawn<TestEntity>(map, e => e.Position = new Vector2(10, 5));

        // Entities exist but nobody is watching
        Assert.Single(map.Entities);
        Assert.Empty(map.Observers);
    }

}
