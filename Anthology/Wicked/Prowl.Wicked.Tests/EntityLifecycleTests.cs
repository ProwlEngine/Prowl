using System.Numerics;
using Prowl.Wicked;
using Prowl.Wicked.Transport;

namespace Prowl.Wicked.Tests;

public class EntityLifecycleTests : IDisposable
{
    private readonly InMemoryServerTransport _serverTransport;
    private readonly InMemoryClientTransport _clientTransport;

    public EntityLifecycleTests()
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

    private RemoteClient ConnectClient()
    {
        Server.Start(0);
        Client.Connect("localhost", 0);
        Tick();
        return Server.Clients.First();
    }

    // ── Spawning ──

    [Fact]
    public void SpawnUnowned_AssignsNetworkIdAndMap()
    {
        Server.Start(0);
        var map = Server.CreateMap<TestMap>();

        var entity = Server.Spawn<TestEntity>(map, e => e.Position = new Vector2(10, 5));

        Assert.NotEqual(0u, entity.NetworkId);
        Assert.True(entity.IsSpawned);
        Assert.True(entity.IsServer);
        Assert.Equal(map, entity.Map);
        Assert.Equal(new Vector2(10, 5), entity.Position);
        Assert.True(entity.FacingRight);
        Assert.Null(entity.Owner);
        Assert.Equal(0u, entity.OwnerClientId);
    }

    [Fact]
    public void SpawnOwned_SetsOwnerAndOwnerClientId()
    {
        var client = ConnectClient();
        var map = Server.CreateMap<TestMap>();

        var entity = Server.Spawn<TestEntity>(map, client);

        Assert.Equal(client, entity.Owner);
        Assert.Equal(client.ClientId, entity.OwnerClientId);
    }

    [Fact]
    public void SpawnWithInitializer_RunsBeforeLifecycleCallbacks()
    {
        Server.Start(0);
        var map = Server.CreateMap<TestMap>();
        bool initializerRan = false;
        List<string>? logAtInitTime = null;

        var entity = Server.Spawn<LifecycleTrackingEntity>(map, e =>
        {
            initializerRan = true;
            logAtInitTime = new List<string>(e.CallbackLog); // snapshot — should be empty
        });

        Assert.True(initializerRan);
        Assert.Empty(logAtInitTime!); // No callbacks fired yet during initializer
        Assert.Equal(new[] { "OnSpawn", "OnStartServer" }, entity.CallbackLog);
    }

    [Fact]
    public void SpawnWithInitializer_MapIsNullDuringInitializer()
    {
        Server.Start(0);
        var map = Server.CreateMap<TestMap>();
        Map? mapDuringInit = null;
        bool checkedMap = false;

        Server.Spawn<TestEntity>(map, e =>
        {
            mapDuringInit = e.Map;
            checkedMap = true;
        });

        Assert.True(checkedMap);
        Assert.Null(mapDuringInit);
    }

    [Fact]
    public void SpawnOwnedWithInitializer_SetsOwnerBeforeInitializer()
    {
        var client = ConnectClient();
        var map = Server.CreateMap<TestMap>();
        RemoteClient? ownerDuringInit = null;

        Server.Spawn<TestEntity>(map, client, e =>
        {
            ownerDuringInit = e.Owner;
        });

        Assert.Equal(client, ownerDuringInit);
    }

    [Fact]
    public void Spawn_AssignsUniqueNetworkIds()
    {
        Server.Start(0);
        var map = Server.CreateMap<TestMap>();

        var e1 = Server.Spawn<TestEntity>(map);
        var e2 = Server.Spawn<TestEntity>(map);
        var e3 = Server.Spawn<TestEntity>(map);

        Assert.NotEqual(e1.NetworkId, e2.NetworkId);
        Assert.NotEqual(e2.NetworkId, e3.NetworkId);
        Assert.NotEqual(e1.NetworkId, e3.NetworkId);
    }

    [Fact]
    public void Spawn_AddsEntityToMap()
    {
        Server.Start(0);
        var map = Server.CreateMap<TestMap>();

        var entity = Server.Spawn<TestEntity>(map);

        Assert.Contains(entity, map.Entities);
    }

    // ── Lifecycle callback ordering ──

    [Fact]
    public void Spawn_FiresOnSpawnThenOnStartServer()
    {
        Server.Start(0);
        var map = Server.CreateMap<TestMap>();

        var entity = Server.Spawn<LifecycleTrackingEntity>(map);

        Assert.Equal(new[] { "OnSpawn", "OnStartServer" }, entity.CallbackLog);
    }

    [Fact]
    public void Despawn_FiresOnStopServerThenOnDespawn()
    {
        Server.Start(0);
        var map = Server.CreateMap<TestMap>();
        var entity = Server.Spawn<LifecycleTrackingEntity>(map);
        entity.CallbackLog.Clear();

        Server.Despawn(entity);

        Assert.Equal(new[] { "OnStopServer", "OnDespawn" }, entity.CallbackLog);
    }

    [Fact]
    public void Despawn_SetsIsSpawnedFalse()
    {
        Server.Start(0);
        var map = Server.CreateMap<TestMap>();
        var entity = Server.Spawn<TestEntity>(map);

        Server.Despawn(entity);

        Assert.False(entity.IsSpawned);
    }

    [Fact]
    public void Despawn_RemovesEntityFromMap()
    {
        Server.Start(0);
        var map = Server.CreateMap<TestMap>();
        var entity = Server.Spawn<TestEntity>(map);

        Server.Despawn(entity);

        Assert.DoesNotContain(entity, map.Entities);
    }

    [Fact]
    public void Despawn_MapAccessibleDuringOnDespawn_NullAfter()
    {
        Server.Start(0);
        var map = Server.CreateMap<TestMap>();
        Map? mapDuringDespawn = null;

        var entity = Server.Spawn<CallbackEntity>(map);
        entity.OnDespawnAction = e => mapDuringDespawn = e.Map;

        Server.Despawn(entity);

        Assert.Equal(map, mapDuringDespawn);
        Assert.Null(entity.Map);
    }

    [Fact]
    public void Despawn_EntityNotInMapEntitiesDuringOnDespawn()
    {
        Server.Start(0);
        var map = Server.CreateMap<TestMap>();
        bool entityInMapDuringDespawn = true;

        var entity = Server.Spawn<CallbackEntity>(map);
        entity.OnDespawnAction = e =>
        {
            entityInMapDuringDespawn = e.Map!.Entities.Contains(e);
        };

        Server.Despawn(entity);

        Assert.False(entityInMapDuringDespawn);
    }

    [Fact]
    public void Despawn_DoubleDespawnIsNoOp()
    {
        Server.Start(0);
        var map = Server.CreateMap<TestMap>();
        var entity = Server.Spawn<LifecycleTrackingEntity>(map);
        entity.CallbackLog.Clear();

        Server.Despawn(entity);
        Server.Despawn(entity); // Should not throw or fire callbacks again

        Assert.Equal(new[] { "OnStopServer", "OnDespawn" }, entity.CallbackLog);
    }

    // ── Ownership ──

    [Fact]
    public void ChangeOwner_UpdatesOwnerAndOwnerClientId()
    {
        var client = ConnectClient();
        var map = Server.CreateMap<TestMap>();
        var entity = Server.Spawn<TestEntity>(map);

        Server.ChangeOwner(entity, client);

        Assert.Equal(client, entity.Owner);
        Assert.Equal(client.ClientId, entity.OwnerClientId);
    }

    [Fact]
    public void ChangeOwner_ToNull_RemovesOwnership()
    {
        var client = ConnectClient();
        var map = Server.CreateMap<TestMap>();
        var entity = Server.Spawn<TestEntity>(map, client);

        Server.ChangeOwner(entity, null);

        Assert.Null(entity.Owner);
        Assert.Equal(0u, entity.OwnerClientId);
    }

    [Fact]
    public void ChangeOwner_FiresOnOwnerChanged()
    {
        var client = ConnectClient();
        var map = Server.CreateMap<TestMap>();

        RemoteClient? oldOwner = null;
        RemoteClient? newOwner = null;
        var entity = Server.Spawn<CallbackEntity>(map);
        entity.OnOwnerChangedAction = (e, old, @new) =>
        {
            oldOwner = old;
            newOwner = @new;
        };

        Server.ChangeOwner(entity, client);

        Assert.Null(oldOwner);
        Assert.Equal(client, newOwner);
    }

    [Fact]
    public void ChangeOwner_ThrowsIfEntityNotSpawned()
    {
        var client = ConnectClient();
        var entity = new TestEntity();

        Assert.Throws<InvalidOperationException>(() =>
            Server.ChangeOwner(entity, client));
    }

    [Fact]
    public void ChangeOwner_ThrowsIfNewOwnerNotConnected()
    {
        var client = ConnectClient();
        var map = Server.CreateMap<TestMap>();
        var entity = Server.Spawn<TestEntity>(map);

        // Disconnect the client
        client.Disconnect();
        Tick();

        Assert.Throws<InvalidOperationException>(() =>
            Server.ChangeOwner(entity, client));
    }

    // ── Player entity assignment ──

    [Fact]
    public void AssignPlayerEntity_SetsPlayerEntityAndAddsObserver()
    {
        var client = ConnectClient();
        var map = Server.CreateMap<TestMap>();
        var entity = Server.Spawn<TestEntity>(map, client);

        client.AssignPlayerEntity(entity);

        Assert.Equal(entity, client.PlayerEntity);
        Assert.True(client.HasPlayerEntity);
        Assert.Contains(client, map.Observers);
    }

    [Fact]
    public void UnassignPlayerEntity_ClearsAndRemovesObserver()
    {
        var client = ConnectClient();
        var map = Server.CreateMap<TestMap>();
        var entity = Server.Spawn<TestEntity>(map, client);
        client.AssignPlayerEntity(entity);

        client.UnassignPlayerEntity();

        Assert.Null(client.PlayerEntity);
        Assert.False(client.HasPlayerEntity);
        Assert.DoesNotContain(client, map.Observers);
    }

    [Fact]
    public void AssignPlayerEntity_ThrowsIfAlreadyAssigned()
    {
        var client = ConnectClient();
        var map = Server.CreateMap<TestMap>();
        var e1 = Server.Spawn<TestEntity>(map, client);
        var e2 = Server.Spawn<TestEntity>(map, client);
        client.AssignPlayerEntity(e1);

        Assert.Throws<InvalidOperationException>(() =>
            client.AssignPlayerEntity(e2));
    }

    [Fact]
    public void AssignPlayerEntity_ThrowsIfEntityNotOwned()
    {
        var client = ConnectClient();
        var map = Server.CreateMap<TestMap>();
        var entity = Server.Spawn<TestEntity>(map); // unowned

        Assert.Throws<InvalidOperationException>(() =>
            client.AssignPlayerEntity(entity));
    }

    [Fact]
    public void AssignPlayerEntity_ThrowsIfEntityNotSpawned()
    {
        var client = ConnectClient();
        var entity = new TestEntity();

        Assert.Throws<InvalidOperationException>(() =>
            client.AssignPlayerEntity(entity));
    }

    [Fact]
    public void Despawn_PlayerEntity_UnassignsFirst()
    {
        var client = ConnectClient();
        var map = Server.CreateMap<TestMap>();
        var entity = Server.Spawn<TestEntity>(map, client);
        client.AssignPlayerEntity(entity);

        Server.Despawn(entity);

        Assert.Null(client.PlayerEntity);
        Assert.False(client.HasPlayerEntity);
    }

    // ── Disconnect cleans up player entity ──

    [Fact]
    public void ClientDisconnect_UnassignsPlayerEntity()
    {
        var client = ConnectClient();
        var map = Server.CreateMap<TestMap>();
        var entity = Server.Spawn<TestEntity>(map, client);
        client.AssignPlayerEntity(entity);

        // Server fires OnClientDisconnected while PlayerEntity is still accessible
        RemoteClient? disconnectedClient = null;
        NetworkEntity? playerEntityDuringDisconnect = null;
        Server.OnClientDisconnected += c =>
        {
            disconnectedClient = c;
            playerEntityDuringDisconnect = c.PlayerEntity;
        };

        Client.Disconnect();
        Tick();

        Assert.NotNull(disconnectedClient);
        Assert.Equal(entity, playerEntityDuringDisconnect);
        Assert.Null(disconnectedClient.PlayerEntity); // Unassigned after callback
    }

    [Fact]
    public void ClientDisconnect_DoesNotDespawnPlayerEntity()
    {
        var client = ConnectClient();
        var map = Server.CreateMap<TestMap>();
        var entity = Server.Spawn<TestEntity>(map, client);
        client.AssignPlayerEntity(entity);

        Client.Disconnect();
        Tick();

        // Entity remains spawned — game code must despawn explicitly
        Assert.True(entity.IsSpawned);
        Assert.Contains(entity, map.Entities);
    }

    [Fact]
    public void ClientDisconnect_CanDespawnPlayerEntityInCallback()
    {
        var client = ConnectClient();
        var map = Server.CreateMap<TestMap>();
        var entity = Server.Spawn<TestEntity>(map, client);
        client.AssignPlayerEntity(entity);

        Server.OnClientDisconnected += c =>
        {
            if (c.PlayerEntity != null)
                Server.Despawn(c.PlayerEntity);
        };

        Client.Disconnect();
        Tick();

        Assert.False(entity.IsSpawned);
        Assert.DoesNotContain(entity, map.Entities);
    }

    // ── FindEntity ──

    [Fact]
    public void ServerFindEntity_ReturnsSpawnedEntity()
    {
        Server.Start(0);
        var map = Server.CreateMap<TestMap>();
        var entity = Server.Spawn<TestEntity>(map);

        var found = Server.FindEntity(entity.NetworkId);

        Assert.Equal(entity, found);
    }

    [Fact]
    public void ServerFindEntity_ReturnsNullAfterDespawn()
    {
        Server.Start(0);
        var map = Server.CreateMap<TestMap>();
        var entity = Server.Spawn<TestEntity>(map);
        var id = entity.NetworkId;

        Server.Despawn(entity);

        Assert.Null(Server.FindEntity(id));
    }

    [Fact]
    public void MapFindEntity_ReturnsEntityInMap()
    {
        Server.Start(0);
        var map = Server.CreateMap<TestMap>();
        var entity = Server.Spawn<TestEntity>(map);

        var found = map.FindEntity<TestEntity>(entity.NetworkId);

        Assert.Equal(entity, found);
    }

    [Fact]
    public void MapGetEntities_FiltersByType()
    {
        Server.Start(0);
        var map = Server.CreateMap<TestMap>();

        var e1 = Server.Spawn<TestEntity>(map);
        var e2 = Server.Spawn<LifecycleTrackingEntity>(map);
        var e3 = Server.Spawn<TestEntity>(map);

        var testEntities = map.GetEntities<TestEntity>().ToList();
        var trackingEntities = map.GetEntities<LifecycleTrackingEntity>().ToList();

        Assert.Equal(2, testEntities.Count);
        Assert.Single(trackingEntities);
        Assert.Equal(e2, trackingEntities[0]);
    }

    // ── ServerTick ──

    [Fact]
    public void ServerTick_CallsEntityServerTick()
    {
        Server.Start(0);
        var map = Server.CreateMap<TestMap>();
        var entity = Server.Spawn<LifecycleTrackingEntity>(map);
        entity.CallbackLog.Clear();

        Server.Tick();

        Assert.Contains("ServerTick", entity.CallbackLog);
    }

    [Fact]
    public void ServerTick_UpdatesDeltaTime()
    {
        Server.Start(0);

        // First tick resets the stopwatch
        Server.Tick();
        Thread.Sleep(20);
        Server.Tick();

        Assert.True(Server.DeltaTime > 0);
    }

    // ── Map lifecycle ──

    [Fact]
    public void CreateMap_SetsMapIdAndIsServer()
    {
        Server.Start(0);

        var map = Server.CreateMap<TestMap>();

        Assert.NotEqual(Guid.Empty, map.MapId);
        Assert.True(map.IsServer);
    }

    [Fact]
    public void GetMap_ReturnsCreatedMap()
    {
        Server.Start(0);
        var map = Server.CreateMap<TestMap>();

        Assert.Equal(map, Server.GetMap(map.MapId));
    }

    [Fact]
    public void GetMap_ReturnsNullForUnknownId()
    {
        Server.Start(0);

        Assert.Null(Server.GetMap(Guid.NewGuid()));
    }

    [Fact]
    public void DestroyMap_DespawnsEntitiesAndRemovesMap()
    {
        Server.Start(0);
        var map = Server.CreateMap<TestMap>();
        var entity = Server.Spawn<TestEntity>(map);

        Server.DestroyMap(map.MapId);

        Assert.Null(Server.GetMap(map.MapId));
        Assert.False(entity.IsSpawned);
    }

    [Fact]
    public void DestroyMap_FiresOnDestroyedBeforeDespawns()
    {
        Server.Start(0);
        var map = Server.CreateMap<TrackingMap>();
        var entity = Server.Spawn<TestEntity>(map);

        Server.DestroyMap(map.MapId);

        // OnDestroyed fires first, then entities are despawned
        Assert.True(map.DestroyedBeforeEntitiesDespawned);
    }

    [Fact]
    public void DestroyMap_MapAccessibleViaGetMapDuringOnDestroyed()
    {
        Server.Start(0);
        var map = Server.CreateMap<TrackingMap>();

        Server.DestroyMap(map.MapId);

        Assert.True(map.MapFoundDuringOnDestroyed);
    }

    // ── Map transfers ──

    [Fact]
    public void TransferEntity_MovesEntityBetweenMaps()
    {
        Server.Start(0);
        var map1 = Server.CreateMap<TestMap>();
        var map2 = Server.CreateMap<TestMap>();
        var entity = Server.Spawn<TestEntity>(map1);

        map1.TransferEntity(entity, map2);

        Assert.Equal(map2, entity.Map);
        Assert.DoesNotContain(entity, map1.Entities);
        Assert.Contains(entity, map2.Entities);
    }

    [Fact]
    public void TransferEntity_FiresOnMapChanged()
    {
        Server.Start(0);
        var map1 = Server.CreateMap<TestMap>();
        var map2 = Server.CreateMap<TestMap>();

        Map? oldMap = null;
        Map? newMap = null;
        var entity = Server.Spawn<CallbackEntity>(map1);
        entity.OnMapChangedAction = (e, old, @new) =>
        {
            oldMap = old;
            newMap = @new;
        };

        map1.TransferEntity(entity, map2);

        Assert.Equal(map1, oldMap);
        Assert.Equal(map2, newMap);
    }

    [Fact]
    public void TransferEntity_PlayerEntity_MovesObserverScope()
    {
        var client = ConnectClient();
        var map1 = Server.CreateMap<TestMap>();
        var map2 = Server.CreateMap<TestMap>();
        var entity = Server.Spawn<TestEntity>(map1, client);
        client.AssignPlayerEntity(entity);

        Assert.Contains(client, map1.Observers);

        map1.TransferEntity(entity, map2);

        Assert.DoesNotContain(client, map1.Observers);
        Assert.Contains(client, map2.Observers);
    }

    [Fact]
    public void TransferEntity_ThrowsIfEntityNotInSourceMap()
    {
        Server.Start(0);
        var map1 = Server.CreateMap<TestMap>();
        var map2 = Server.CreateMap<TestMap>();
        var entity = Server.Spawn<TestEntity>(map2);

        Assert.Throws<InvalidOperationException>(() =>
            map1.TransferEntity(entity, map2));
    }

    // ── Map callbacks ──

    [Fact]
    public void Map_FiresOnEntityEnterAndLeave()
    {
        Server.Start(0);
        var map = Server.CreateMap<TrackingMap>();

        var entity = Server.Spawn<TestEntity>(map);
        Assert.Contains(entity, map.EnteredEntities);

        Server.Despawn(entity);
        Assert.Contains(entity, map.LeftEntities);
    }

    [Fact]
    public void Map_FiresOnObserverEnterAndLeave()
    {
        var client = ConnectClient();
        var map = Server.CreateMap<TrackingMap>();
        var entity = Server.Spawn<TestEntity>(map, client);

        client.AssignPlayerEntity(entity);
        Assert.Contains(client, map.EnteredObservers);

        client.UnassignPlayerEntity();
        Assert.Contains(client, map.LeftObservers);
    }

    [Fact]
    public void Map_OnCreated_Fires()
    {
        Server.Start(0);

        var map = Server.CreateMap<TrackingMap>();

        Assert.True(map.CreatedFired);
    }
}

// ── Test helper types ──

public class LifecycleTrackingEntity : NetworkEntity
{
    public List<string> CallbackLog { get; } = new();

    public override void OnSpawn() => CallbackLog.Add("OnSpawn");
    public override void OnDespawn() => CallbackLog.Add("OnDespawn");
    public override void OnStartServer() => CallbackLog.Add("OnStartServer");
    public override void OnStopServer() => CallbackLog.Add("OnStopServer");
    public override void OnStartClient() => CallbackLog.Add("OnStartClient");
    public override void OnStopClient() => CallbackLog.Add("OnStopClient");
    public override void OnStartOwner() => CallbackLog.Add("OnStartOwner");
    public override void OnStopOwner() => CallbackLog.Add("OnStopOwner");
    public override void OnOwnerChanged(RemoteClient? oldOwner, RemoteClient? newOwner) => CallbackLog.Add("OnOwnerChanged");
    public override void ServerTick() => CallbackLog.Add("ServerTick");
    public override void ClientTick() => CallbackLog.Add("ClientTick");
}

public class CallbackEntity : NetworkEntity
{
    public Action<CallbackEntity>? OnDespawnAction { get; set; }
    public Action<CallbackEntity, RemoteClient?, RemoteClient?>? OnOwnerChangedAction { get; set; }
    public Action<CallbackEntity, Map, Map>? OnMapChangedAction { get; set; }

    public override void OnDespawn() => OnDespawnAction?.Invoke(this);
    public override void OnOwnerChanged(RemoteClient? oldOwner, RemoteClient? newOwner)
        => OnOwnerChangedAction?.Invoke(this, oldOwner, newOwner);
    public override void OnMapChanged(Map oldMap, Map newMap)
        => OnMapChangedAction?.Invoke(this, oldMap, newMap);
}

public class TrackingMap : Map
{
    public bool CreatedFired { get; private set; }
    public bool DestroyedBeforeEntitiesDespawned { get; private set; }
    public bool MapFoundDuringOnDestroyed { get; private set; }
    public List<NetworkEntity> EnteredEntities { get; } = new();
    public List<NetworkEntity> LeftEntities { get; } = new();
    public List<RemoteClient> EnteredObservers { get; } = new();
    public List<RemoteClient> LeftObservers { get; } = new();

    public override void OnCreated() => CreatedFired = true;

    public override void OnDestroyed()
    {
        DestroyedBeforeEntitiesDespawned = Entities.Count > 0;
        MapFoundDuringOnDestroyed = Server.GetMap(MapId) != null;
    }

    public override void OnEntityEnter(NetworkEntity entity) => EnteredEntities.Add(entity);
    public override void OnEntityLeave(NetworkEntity entity) => LeftEntities.Add(entity);
    public override void OnObserverEnter(RemoteClient client) => EnteredObservers.Add(client);
    public override void OnObserverLeave(RemoteClient client) => LeftObservers.Add(client);
}
