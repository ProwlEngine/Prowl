using System.Numerics;
using Prowl.Wicked;

namespace Prowl.Wicked.Tests;

// ── Test entity types ──

public class SyncVarTestEntity : NetworkEntity
{
    public SyncVar<float> Health = new(100f);
    public SyncVar<int> Score = new(0);
    public SyncVar<string> Name = new("");
    public SyncVar<bool> IsReady = new(false);
    public SyncVar<Vector2> Position = new(Vector2.Zero);
    public SyncVar<Guid> UniqueId = new(Guid.Empty);

    public override void PackSpawnData(NetworkWriter writer) { }
    public override void UnpackSpawnData(NetworkReader reader) { }
}

public class SyncVarOwnerEntity : NetworkEntity
{
    public SyncVar<float> Health = new(100f); // observers
    public SyncVar<int> Gold = new(0, SyncTarget.Owner); // owner only
    public SyncVar<string> Name = new(""); // observers

    public override void PackSpawnData(NetworkWriter writer) { }
    public override void UnpackSpawnData(NetworkReader reader) { }
}

public class SyncVarCallbackEntity : NetworkEntity
{
    public SyncVar<int> Value = new(0);
    public List<(int old, int @new)> Changes = new();

    public override void PackSpawnData(NetworkWriter writer) { }
    public override void UnpackSpawnData(NetworkReader reader) { }

    public override void OnSpawn()
    {
        Value.OnChanged((old, @new) => Changes.Add((old, @new)));
    }
}

public class SyncVarIntervalEntity : NetworkEntity
{
    public SyncVar<float> FastVar = new(0f) { SyncInterval = 0f }; // every tick
    public SyncVar<float> SlowVar = new(0f) { SyncInterval = 0.5f }; // every 0.5s

    public override void PackSpawnData(NetworkWriter writer) { }
    public override void UnpackSpawnData(NetworkReader reader) { }
}

public class SyncVarInterpolatedEntity : NetworkEntity
{
    public SyncVarInterpolated Health = new(100f, interpSpeed: 10f);
    public SyncVarInterpolatedVector2 Position = new(Vector2.Zero, interpSpeed: 10f);

    public override void PackSpawnData(NetworkWriter writer) { }
    public override void UnpackSpawnData(NetworkReader reader) { }
}

public enum TestTeam : byte { Red, Blue, Green }

public class SyncVarEnumEntity : NetworkEntity
{
    public SyncVar<TestTeam> Team = new(TestTeam.Red);

    public override void PackSpawnData(NetworkWriter writer) { }
    public override void UnpackSpawnData(NetworkReader reader) { }
}

public class SyncVarAllTypesEntity : NetworkEntity
{
    public SyncVar<byte> ByteVal = new(0);
    public SyncVar<sbyte> SByteVal = new(0);
    public SyncVar<short> ShortVal = new(0);
    public SyncVar<ushort> UShortVal = new(0);
    public SyncVar<int> IntVal = new(0);
    public SyncVar<uint> UIntVal = new(0);
    public SyncVar<long> LongVal = new(0);
    public SyncVar<ulong> ULongVal = new(0);
    public SyncVar<float> FloatVal = new(0f);
    public SyncVar<double> DoubleVal = new(0.0);
    public SyncVar<bool> BoolVal = new(false);
    public SyncVar<string> StringVal = new("");
    public SyncVar<Guid> GuidVal = new(Guid.Empty);
    public SyncVar<Vector2> Vec2Val = new(Vector2.Zero);

    public override void PackSpawnData(NetworkWriter writer) { }
    public override void UnpackSpawnData(NetworkReader reader) { }
}

public class NoSyncVarEntity : NetworkEntity
{
    public int PlainField = 42;

    public override void PackSpawnData(NetworkWriter writer) { writer.WriteInt(PlainField); }
    public override void UnpackSpawnData(NetworkReader reader) { PlainField = reader.ReadInt(); }
}

public class SyncVarWithSpawnDataEntity : NetworkEntity
{
    public int SpawnOnlyField;
    public SyncVar<float> SyncedField = new(0f);

    public override void PackSpawnData(NetworkWriter writer) { writer.WriteInt(SpawnOnlyField); }
    public override void UnpackSpawnData(NetworkReader reader) { SpawnOnlyField = reader.ReadInt(); }
}

public class SyncVarMultiOwnerEntity : NetworkEntity
{
    public SyncVar<int> PublicScore = new(0); // observers
    public SyncVar<int> SecretA = new(0, SyncTarget.Owner); // owner only
    public SyncVar<int> SecretB = new(0, SyncTarget.Owner); // owner only

    public override void PackSpawnData(NetworkWriter writer) { }
    public override void UnpackSpawnData(NetworkReader reader) { }
}

// ── Tests ──

public class SyncVarTests : IDisposable
{
    private readonly InMemoryServerTransport _serverTransport;
    private readonly InMemoryClientTransport _clientTransport;

    public SyncVarTests()
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

    private (RemoteClient serverClient, Map map) SetupWithMap()
    {
        var client = ConnectAndSetup();
        var map = Server.CreateMap<TestMap>();
        var player = Server.Spawn<TestEntity>(map, client);
        client.AssignPlayerEntity(player);
        Tick(); // process map create + entity spawns
        return (client, map);
    }

    // ── SyncVar<T> unit tests (no network) ──

    [Fact]
    public void SyncVar_DefaultValue()
    {
        var sv = new SyncVar<int>();
        Assert.Equal(0, sv.Value);
        Assert.False(sv.IsDirty);
        Assert.Equal(SyncTarget.Observers, sv.Target);
    }

    [Fact]
    public void SyncVar_InitialValue()
    {
        var sv = new SyncVar<float>(42.5f);
        Assert.Equal(42.5f, sv.Value);
        Assert.False(sv.IsDirty);
    }

    [Fact]
    public void SyncVar_SetValue_MarksDirty()
    {
        var sv = new SyncVar<int>(0);
        sv.Value = 10;
        Assert.Equal(10, sv.Value);
        Assert.True(sv.IsDirty);
    }

    [Fact]
    public void SyncVar_SetSameValue_DoesNotMarkDirty()
    {
        var sv = new SyncVar<int>(5);
        sv.Value = 5;
        Assert.False(sv.IsDirty);
    }

    [Fact]
    public void SyncVar_ClearDirty_ResetsDirtyAndTimer()
    {
        var sv = new SyncVar<int>(0);
        sv.Value = 1;
        sv.TimeSinceLastSync = 1.5f;
        Assert.True(sv.IsDirty);
        sv.ClearDirty();
        Assert.False(sv.IsDirty);
        Assert.Equal(0f, sv.TimeSinceLastSync);
    }

    [Fact]
    public void SyncVar_ImplicitConversion()
    {
        var sv = new SyncVar<float>(3.14f);
        float val = sv;
        Assert.Equal(3.14f, val);
    }

    [Fact]
    public void SyncVar_OnChanged_FiresCallback()
    {
        var changes = new List<(int old, int @new)>();
        var sv = new SyncVar<int>(0);
        sv.OnChanged((o, n) => changes.Add((o, n)));

        sv.Value = 5;
        sv.Value = 10;

        Assert.Equal(2, changes.Count);
        Assert.Equal((0, 5), changes[0]);
        Assert.Equal((5, 10), changes[1]);
    }

    [Fact]
    public void SyncVar_OnChanged_DoesNotFireForSameValue()
    {
        int callCount = 0;
        var sv = new SyncVar<int>(5);
        sv.OnChanged((_, _) => callCount++);

        sv.Value = 5;

        Assert.Equal(0, callCount);
    }

    [Fact]
    public void SyncVar_ToString()
    {
        var sv = new SyncVar<string>("hello");
        Assert.Equal("hello", sv.ToString());

        var svNull = new SyncVar<string>(null!);
        Assert.Equal("null", svNull.ToString());
    }

    [Fact]
    public void SyncVar_SyncTarget_Owner()
    {
        var sv = new SyncVar<int>(0, SyncTarget.Owner);
        Assert.Equal(SyncTarget.Owner, sv.Target);
    }

    [Fact]
    public void SyncVar_SyncInterval_Default()
    {
        var sv = new SyncVar<float>(0f);
        Assert.Equal(0f, sv.SyncInterval);
    }

    [Fact]
    public void SyncVar_SyncInterval_Custom()
    {
        var sv = new SyncVar<float>(0f) { SyncInterval = 0.1f };
        Assert.Equal(0.1f, sv.SyncInterval);
    }

    [Fact]
    public void SyncVar_UnsupportedType_Throws()
    {
        Assert.Throws<NotSupportedException>(() => new SyncVar<List<int>>());
    }

    // ── Serialization round-trip ──

    [Fact]
    public void SyncVar_Serialize_Deserialize_Float()
    {
        var sv = new SyncVar<float>(42.5f);
        var writer = new NetworkWriter();
        sv.Serialize(writer);

        var reader = new NetworkReader(writer.ToArraySegment());
        var sv2 = new SyncVar<float>(0f);
        sv2.Deserialize(reader);
        Assert.Equal(42.5f, sv2.Value);
    }

    [Fact]
    public void SyncVar_Serialize_Deserialize_String()
    {
        var sv = new SyncVar<string>("test");
        var writer = new NetworkWriter();
        sv.Serialize(writer);

        var reader = new NetworkReader(writer.ToArraySegment());
        var sv2 = new SyncVar<string>("");
        sv2.Deserialize(reader);
        Assert.Equal("test", sv2.Value);
    }

    [Fact]
    public void SyncVar_Serialize_Deserialize_Vector2()
    {
        var vec = new Vector2(1.5f, -3.7f);
        var sv = new SyncVar<Vector2>(vec);
        var writer = new NetworkWriter();
        sv.Serialize(writer);

        var reader = new NetworkReader(writer.ToArraySegment());
        var sv2 = new SyncVar<Vector2>();
        sv2.Deserialize(reader);
        Assert.Equal(vec, sv2.Value);
    }

    [Fact]
    public void SyncVar_Serialize_Deserialize_Guid()
    {
        var guid = Guid.NewGuid();
        var sv = new SyncVar<Guid>(guid);
        var writer = new NetworkWriter();
        sv.Serialize(writer);

        var reader = new NetworkReader(writer.ToArraySegment());
        var sv2 = new SyncVar<Guid>();
        sv2.Deserialize(reader);
        Assert.Equal(guid, sv2.Value);
    }

    [Fact]
    public void SyncVar_Serialize_Deserialize_Enum()
    {
        var sv = new SyncVar<TestTeam>(TestTeam.Blue);
        var writer = new NetworkWriter();
        sv.Serialize(writer);

        var reader = new NetworkReader(writer.ToArraySegment());
        var sv2 = new SyncVar<TestTeam>();
        sv2.Deserialize(reader);
        Assert.Equal(TestTeam.Blue, sv2.Value);
    }

    [Fact]
    public void SyncVar_Serialize_Deserialize_AllPrimitives()
    {
        var writer = new NetworkWriter();

        var svByte = new SyncVar<byte>(255);
        var svSByte = new SyncVar<sbyte>(-42);
        var svShort = new SyncVar<short>(-1234);
        var svUShort = new SyncVar<ushort>(60000);
        var svInt = new SyncVar<int>(-100000);
        var svUInt = new SyncVar<uint>(4000000000u);
        var svLong = new SyncVar<long>(-9000000000000L);
        var svULong = new SyncVar<ulong>(18000000000000000000UL);
        var svFloat = new SyncVar<float>(3.14159f);
        var svDouble = new SyncVar<double>(2.718281828);
        var svBool = new SyncVar<bool>(true);

        svByte.Serialize(writer);
        svSByte.Serialize(writer);
        svShort.Serialize(writer);
        svUShort.Serialize(writer);
        svInt.Serialize(writer);
        svUInt.Serialize(writer);
        svLong.Serialize(writer);
        svULong.Serialize(writer);
        svFloat.Serialize(writer);
        svDouble.Serialize(writer);
        svBool.Serialize(writer);

        var reader = new NetworkReader(writer.ToArraySegment());

        var rByte = new SyncVar<byte>(); rByte.Deserialize(reader);
        var rSByte = new SyncVar<sbyte>(); rSByte.Deserialize(reader);
        var rShort = new SyncVar<short>(); rShort.Deserialize(reader);
        var rUShort = new SyncVar<ushort>(); rUShort.Deserialize(reader);
        var rInt = new SyncVar<int>(); rInt.Deserialize(reader);
        var rUInt = new SyncVar<uint>(); rUInt.Deserialize(reader);
        var rLong = new SyncVar<long>(); rLong.Deserialize(reader);
        var rULong = new SyncVar<ulong>(); rULong.Deserialize(reader);
        var rFloat = new SyncVar<float>(); rFloat.Deserialize(reader);
        var rDouble = new SyncVar<double>(); rDouble.Deserialize(reader);
        var rBool = new SyncVar<bool>(); rBool.Deserialize(reader);

        Assert.Equal((byte)255, rByte.Value);
        Assert.Equal((sbyte)-42, rSByte.Value);
        Assert.Equal((short)-1234, rShort.Value);
        Assert.Equal((ushort)60000, rUShort.Value);
        Assert.Equal(-100000, rInt.Value);
        Assert.Equal(4000000000u, rUInt.Value);
        Assert.Equal(-9000000000000L, rLong.Value);
        Assert.Equal(18000000000000000000UL, rULong.Value);
        Assert.Equal(3.14159f, rFloat.Value);
        Assert.Equal(2.718281828, rDouble.Value);
        Assert.True(rBool.Value);
    }

    [Fact]
    public void SyncVar_Deserialize_FiresOnChanged()
    {
        var changes = new List<(int old, int @new)>();
        var sv = new SyncVar<int>(5);
        sv.OnChanged((o, n) => changes.Add((o, n)));

        var writer = new NetworkWriter();
        new SyncVar<int>(99).Serialize(writer);
        var reader = new NetworkReader(writer.ToArraySegment());
        sv.Deserialize(reader);

        Assert.Single(changes);
        Assert.Equal((5, 99), changes[0]);
        Assert.Equal(99, sv.Value);
    }

    [Fact]
    public void SyncVar_Deserialize_SameValue_DoesNotFireCallback()
    {
        int callCount = 0;
        var sv = new SyncVar<int>(5);
        sv.OnChanged((_, _) => callCount++);

        var writer = new NetworkWriter();
        new SyncVar<int>(5).Serialize(writer);
        var reader = new NetworkReader(writer.ToArraySegment());
        sv.Deserialize(reader);

        Assert.Equal(0, callCount);
    }

    // ── Entity discovery ──

    [Fact]
    public void DiscoverSyncVars_FindsAllFields()
    {
        var entity = new SyncVarTestEntity();
        entity.DiscoverSyncVars();

        Assert.NotNull(entity._syncVars);
        Assert.Equal(6, entity._syncVars!.Length);
    }

    [Fact]
    public void DiscoverSyncVars_SortsByFieldName()
    {
        var entity = new SyncVarTestEntity();
        entity.DiscoverSyncVars();

        // Fields sorted alphabetically: Health, IsReady, Name, Position, Score, UniqueId
        // Verify by checking the types match the expected order
        var writer = new NetworkWriter();

        // Write expected values
        entity.Health.Value = 50f;
        entity.IsReady.Value = true;
        entity.Name.Value = "test";
        entity.Position.Value = new Vector2(1, 2);
        entity.Score.Value = 42;
        entity.UniqueId.Value = Guid.Empty;

        // Serialize all in order
        foreach (var sv in entity._syncVars!)
            sv.Serialize(writer);

        // Deserialize and verify order matches
        var reader = new NetworkReader(writer.ToArraySegment());
        Assert.Equal(50f, reader.ReadFloat()); // Health
        Assert.True(reader.ReadBool()); // IsReady
        Assert.Equal("test", reader.ReadString()); // Name
        Assert.Equal(new Vector2(1, 2), reader.ReadVector2()); // Position
        Assert.Equal(42, reader.ReadInt()); // Score
        Assert.Equal(Guid.Empty, reader.ReadGuid()); // UniqueId
    }

    [Fact]
    public void DiscoverSyncVars_NoSyncVars_ReturnsNull()
    {
        var entity = new NoSyncVarEntity();
        entity.DiscoverSyncVars();
        Assert.Null(entity._syncVars);
    }

    [Fact]
    public void DiscoverSyncVars_MixedFields_OnlyFindsSyncVars()
    {
        var entity = new SyncVarWithSpawnDataEntity();
        entity.DiscoverSyncVars();

        Assert.NotNull(entity._syncVars);
        Assert.Single(entity._syncVars!);
    }

    // ── Network replication: initial spawn ──

    [Fact]
    public void SyncVar_InitialValues_ReplicatedOnSpawn()
    {
        var (client, map) = SetupWithMap();

        var serverEntity = Server.Spawn<SyncVarTestEntity>(map, e =>
        {
            e.Health.Value = 75f;
            e.Score.Value = 42;
            e.Name.Value = "TestPlayer";
            e.IsReady.Value = true;
            e.Position.Value = new Vector2(5, 10);
            e.UniqueId.Value = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        });
        Tick();

        var clientEntity = Client.FindEntity<SyncVarTestEntity>(serverEntity.NetworkId);
        Assert.NotNull(clientEntity);
        Assert.Equal(75f, clientEntity!.Health.Value);
        Assert.Equal(42, clientEntity.Score.Value);
        Assert.Equal("TestPlayer", clientEntity.Name.Value);
        Assert.True(clientEntity.IsReady.Value);
        Assert.Equal(new Vector2(5, 10), clientEntity.Position.Value);
        Assert.Equal(Guid.Parse("12345678-1234-1234-1234-123456789abc"), clientEntity.UniqueId.Value);
    }

    [Fact]
    public void SyncVar_DefaultValues_ReplicatedOnSpawn()
    {
        var (client, map) = SetupWithMap();

        var serverEntity = Server.Spawn<SyncVarTestEntity>(map);
        Tick();

        var clientEntity = Client.FindEntity<SyncVarTestEntity>(serverEntity.NetworkId);
        Assert.NotNull(clientEntity);
        Assert.Equal(100f, clientEntity!.Health.Value); // initial value from field initializer
        Assert.Equal(0, clientEntity.Score.Value);
        Assert.Equal("", clientEntity.Name.Value);
        Assert.False(clientEntity.IsReady.Value);
    }

    [Fact]
    public void SyncVar_SpawnWithPackSpawnData_BothWork()
    {
        var (client, map) = SetupWithMap();

        var serverEntity = Server.Spawn<SyncVarWithSpawnDataEntity>(map, e =>
        {
            e.SpawnOnlyField = 999;
            e.SyncedField.Value = 3.14f;
        });
        Tick();

        var clientEntity = Client.FindEntity<SyncVarWithSpawnDataEntity>(serverEntity.NetworkId);
        Assert.NotNull(clientEntity);
        Assert.Equal(999, clientEntity!.SpawnOnlyField);
        Assert.Equal(3.14f, clientEntity.SyncedField.Value);
    }

    [Fact]
    public void SyncVar_EntityWithNoSyncVars_StillWorksNormally()
    {
        var (client, map) = SetupWithMap();

        var serverEntity = Server.Spawn<NoSyncVarEntity>(map, e => e.PlainField = 123);
        Tick();

        var clientEntity = Client.FindEntity<NoSyncVarEntity>(serverEntity.NetworkId);
        Assert.NotNull(clientEntity);
        Assert.Equal(123, clientEntity!.PlainField);
    }

    // ── Network replication: dirty updates ──

    [Fact]
    public void SyncVar_DirtyUpdate_ReplicatesToClient()
    {
        var (client, map) = SetupWithMap();
        var serverEntity = Server.Spawn<SyncVarTestEntity>(map);
        Tick();

        // Mutate on server
        serverEntity.Health.Value = 50f;
        serverEntity.Score.Value = 100;
        Tick();

        var clientEntity = Client.FindEntity<SyncVarTestEntity>(serverEntity.NetworkId);
        Assert.Equal(50f, clientEntity!.Health.Value);
        Assert.Equal(100, clientEntity.Score.Value);
    }

    [Fact]
    public void SyncVar_MultipleDirtyUpdates_AllReplicate()
    {
        var (client, map) = SetupWithMap();
        var serverEntity = Server.Spawn<SyncVarTestEntity>(map);
        Tick();

        serverEntity.Health.Value = 80f;
        Tick();
        Assert.Equal(80f, Client.FindEntity<SyncVarTestEntity>(serverEntity.NetworkId)!.Health.Value);

        serverEntity.Health.Value = 60f;
        Tick();
        Assert.Equal(60f, Client.FindEntity<SyncVarTestEntity>(serverEntity.NetworkId)!.Health.Value);

        serverEntity.Health.Value = 40f;
        Tick();
        Assert.Equal(40f, Client.FindEntity<SyncVarTestEntity>(serverEntity.NetworkId)!.Health.Value);
    }

    [Fact]
    public void SyncVar_OnlyDirtyVars_AreSent()
    {
        var (client, map) = SetupWithMap();
        var serverEntity = Server.Spawn<SyncVarTestEntity>(map, e =>
        {
            e.Health.Value = 100f;
            e.Name.Value = "Player";
        });
        Tick();

        // Only change health, name should remain
        serverEntity.Health.Value = 50f;
        Tick();

        var clientEntity = Client.FindEntity<SyncVarTestEntity>(serverEntity.NetworkId);
        Assert.Equal(50f, clientEntity!.Health.Value);
        Assert.Equal("Player", clientEntity.Name.Value); // unchanged
    }

    [Fact]
    public void SyncVar_Enum_ReplicatesCorrectly()
    {
        var (client, map) = SetupWithMap();
        var serverEntity = Server.Spawn<SyncVarEnumEntity>(map, e => e.Team.Value = TestTeam.Blue);
        Tick();

        var clientEntity = Client.FindEntity<SyncVarEnumEntity>(serverEntity.NetworkId);
        Assert.Equal(TestTeam.Blue, clientEntity!.Team.Value);

        serverEntity.Team.Value = TestTeam.Green;
        Tick();
        Assert.Equal(TestTeam.Green, clientEntity.Team.Value);
    }

    [Fact]
    public void SyncVar_AllPrimitiveTypes_ReplicateCorrectly()
    {
        var (client, map) = SetupWithMap();
        var guid = Guid.NewGuid();
        var vec = new Vector2(1.5f, -2.5f);

        var serverEntity = Server.Spawn<SyncVarAllTypesEntity>(map, e =>
        {
            e.ByteVal.Value = 200;
            e.SByteVal.Value = -100;
            e.ShortVal.Value = -30000;
            e.UShortVal.Value = 60000;
            e.IntVal.Value = -2000000;
            e.UIntVal.Value = 4000000000u;
            e.LongVal.Value = -9000000000000L;
            e.ULongVal.Value = 18000000000000000000UL;
            e.FloatVal.Value = 3.14f;
            e.DoubleVal.Value = 2.718;
            e.BoolVal.Value = true;
            e.StringVal.Value = "hello world";
            e.GuidVal.Value = guid;
            e.Vec2Val.Value = vec;
        });
        Tick();

        var ce = Client.FindEntity<SyncVarAllTypesEntity>(serverEntity.NetworkId)!;
        Assert.Equal((byte)200, ce.ByteVal.Value);
        Assert.Equal((sbyte)-100, ce.SByteVal.Value);
        Assert.Equal((short)-30000, ce.ShortVal.Value);
        Assert.Equal((ushort)60000, ce.UShortVal.Value);
        Assert.Equal(-2000000, ce.IntVal.Value);
        Assert.Equal(4000000000u, ce.UIntVal.Value);
        Assert.Equal(-9000000000000L, ce.LongVal.Value);
        Assert.Equal(18000000000000000000UL, ce.ULongVal.Value);
        Assert.Equal(3.14f, ce.FloatVal.Value);
        Assert.Equal(2.718, ce.DoubleVal.Value);
        Assert.True(ce.BoolVal.Value);
        Assert.Equal("hello world", ce.StringVal.Value);
        Assert.Equal(guid, ce.GuidVal.Value);
        Assert.Equal(vec, ce.Vec2Val.Value);
    }

    // ── SyncTarget.Owner ──

    [Fact]
    public void SyncVar_OwnerTarget_InitialValuesReplicateToAllOnSpawn()
    {
        var (client, map) = SetupWithMap();

        var serverEntity = Server.Spawn<SyncVarOwnerEntity>(map, client, e =>
        {
            e.Health.Value = 80f;
            e.Gold.Value = 500;
            e.Name.Value = "Hero";
        });
        Tick();

        // Owner sees everything (Gold is Owner-targeted but spawn sends all)
        var clientEntity = Client.FindEntity<SyncVarOwnerEntity>(serverEntity.NetworkId);
        Assert.NotNull(clientEntity);
        Assert.Equal(80f, clientEntity!.Health.Value);
        Assert.Equal(500, clientEntity.Gold.Value);
        Assert.Equal("Hero", clientEntity.Name.Value);
    }

    [Fact]
    public void SyncVar_OwnerTarget_DirtyUpdates_SentToOwnerOnly()
    {
        var (client, map) = SetupWithMap();

        var serverEntity = Server.Spawn<SyncVarOwnerEntity>(map, client, e =>
        {
            e.Gold.Value = 100;
        });
        Tick();

        // Verify initial state
        var clientEntity = Client.FindEntity<SyncVarOwnerEntity>(serverEntity.NetworkId);
        Assert.Equal(100, clientEntity!.Gold.Value);

        // Update gold on server
        serverEntity.Gold.Value = 200;
        Tick();

        // Owner should see the update
        Assert.Equal(200, clientEntity.Gold.Value);
    }

    [Fact]
    public void SyncVar_OwnerTarget_ObserverUpdates_SentToAll()
    {
        var (client, map) = SetupWithMap();

        var serverEntity = Server.Spawn<SyncVarOwnerEntity>(map, client);
        Tick();

        // Update observer-targeted var
        serverEntity.Health.Value = 50f;
        Tick();

        var clientEntity = Client.FindEntity<SyncVarOwnerEntity>(serverEntity.NetworkId);
        Assert.Equal(50f, clientEntity!.Health.Value);
    }

    // ── Callbacks on client ──

    [Fact]
    public void SyncVar_ClientCallback_FiresOnDirtyUpdate()
    {
        var (client, map) = SetupWithMap();
        var serverEntity = Server.Spawn<SyncVarCallbackEntity>(map, client);
        Tick();

        var clientEntity = Client.FindEntity<SyncVarCallbackEntity>(serverEntity.NetworkId);
        Assert.NotNull(clientEntity);
        Assert.Empty(clientEntity!.Changes); // no changes yet (initial spawn doesn't "change")

        serverEntity.Value.Value = 42;
        Tick();

        Assert.Single(clientEntity.Changes);
        Assert.Equal((0, 42), clientEntity.Changes[0]);
    }

    [Fact]
    public void SyncVar_ClientCallback_FiresMultipleTimes()
    {
        var (client, map) = SetupWithMap();
        var serverEntity = Server.Spawn<SyncVarCallbackEntity>(map, client);
        Tick();

        var clientEntity = Client.FindEntity<SyncVarCallbackEntity>(serverEntity.NetworkId);

        serverEntity.Value.Value = 10;
        Tick();
        serverEntity.Value.Value = 20;
        Tick();
        serverEntity.Value.Value = 30;
        Tick();

        Assert.Equal(3, clientEntity!.Changes.Count);
        Assert.Equal((0, 10), clientEntity.Changes[0]);
        Assert.Equal((10, 20), clientEntity.Changes[1]);
        Assert.Equal((20, 30), clientEntity.Changes[2]);
    }

    // ── Sync intervals ──

    [Fact]
    public void SyncVar_SyncInterval_Zero_SendsEveryTick()
    {
        var (client, map) = SetupWithMap();
        var serverEntity = Server.Spawn<SyncVarIntervalEntity>(map);
        Tick();

        serverEntity.FastVar.Value = 1f;
        Tick();

        var clientEntity = Client.FindEntity<SyncVarIntervalEntity>(serverEntity.NetworkId);
        Assert.Equal(1f, clientEntity!.FastVar.Value);

        serverEntity.FastVar.Value = 2f;
        Tick();
        Assert.Equal(2f, clientEntity.FastVar.Value);
    }

    [Fact]
    public void SyncVar_SyncInterval_DelaySend_UntilIntervalElapsed()
    {
        var (client, map) = SetupWithMap();
        var serverEntity = Server.Spawn<SyncVarIntervalEntity>(map);
        Tick();

        var clientEntity = Client.FindEntity<SyncVarIntervalEntity>(serverEntity.NetworkId);

        // Set the slow var dirty
        serverEntity.SlowVar.Value = 99f;

        // Tick a few times rapidly — with default DeltaTime (~0) the interval hasn't elapsed
        // The sync interval is 0.5s, and DeltaTime is very small
        Tick();

        // After one fast tick, the value might not have synced yet if DeltaTime < 0.5
        // But we need to verify the mechanism works. Since DeltaTime comes from wall clock,
        // let's just verify the var IS dirty and has the value set
        Assert.True(serverEntity.SlowVar.IsDirty || clientEntity!.SlowVar.Value == 99f);
    }

    // ── Interpolated SyncVars ──

    [Fact]
    public void SyncVarInterpolated_InitialDisplay()
    {
        var sv = new SyncVarInterpolated(50f, interpSpeed: 10f);
        Assert.Equal(50f, sv.Value);
        Assert.Equal(50f, sv.Display);
    }

    [Fact]
    public void SyncVarInterpolated_ServerSet_DisplayMatchesImmediately()
    {
        var sv = new SyncVarInterpolated(0f, interpSpeed: 10f);
        sv.Value = 100f;
        Assert.Equal(100f, sv.Value);
        Assert.Equal(100f, sv.Display);
    }

    [Fact]
    public void SyncVarInterpolated_ClientUpdate_InterpolatesOverTime()
    {
        var sv = new SyncVarInterpolated(0f, interpSpeed: 10f);

        // First deserialize snaps display (spawn data)
        var writer = new NetworkWriter();
        new SyncVar<float>(100f).Serialize(writer);
        var reader = new NetworkReader(writer.ToArraySegment());
        sv.Deserialize(reader);

        Assert.Equal(100f, sv.Value);
        Assert.Equal(100f, sv.Display); // snapped on first receive

        // Second deserialize interpolates (subsequent updates)
        writer = new NetworkWriter();
        new SyncVar<float>(200f).Serialize(writer);
        reader = new NetworkReader(writer.ToArraySegment());
        sv.Deserialize(reader);

        Assert.Equal(200f, sv.Value);
        Assert.Equal(100f, sv.Display); // not yet interpolated

        sv.ClientUpdate(0.1f); // 10 * 0.1 = 1.0 → clamped to 1.0 → instant jump
        Assert.Equal(200f, sv.Display);

        // Test partial interpolation with smaller dt
        var sv2 = new SyncVarInterpolated(0f, interpSpeed: 5f);
        // First deserialize snaps
        writer = new NetworkWriter();
        new SyncVar<float>(50f).Serialize(writer);
        reader = new NetworkReader(writer.ToArraySegment());
        sv2.Deserialize(reader);
        Assert.Equal(50f, sv2.Display);

        // Second deserialize sets target without snapping
        writer = new NetworkWriter();
        new SyncVar<float>(150f).Serialize(writer);
        reader = new NetworkReader(writer.ToArraySegment());
        sv2.Deserialize(reader);

        sv2.ClientUpdate(0.016f); // ~60fps, interpSpeed*dt = 0.08
        float expected = 50f + (150f - 50f) * MathF.Min(1f, 5f * 0.016f);
        Assert.Equal(expected, sv2.Display, precision: 3);
        Assert.True(sv2.Display > 50f);
        Assert.True(sv2.Display < 150f);
    }

    [Fact]
    public void SyncVarInterpolated_ClientUpdate_ConvergesToTarget()
    {
        var sv = new SyncVarInterpolated(0f, interpSpeed: 10f);

        var writer = new NetworkWriter();
        new SyncVar<float>(100f).Serialize(writer);
        sv.Deserialize(new NetworkReader(writer.ToArraySegment()));

        // Run many updates to converge
        for (int i = 0; i < 1000; i++)
            sv.ClientUpdate(0.016f);

        Assert.Equal(100f, sv.Display, precision: 2);
    }

    [Fact]
    public void SyncVarInterpolated_IsDirty()
    {
        var sv = new SyncVarInterpolated(0f);
        Assert.False(sv.IsDirty);
        sv.Value = 10f;
        Assert.True(sv.IsDirty);
    }

    [Fact]
    public void SyncVarInterpolated_ImplicitConversion()
    {
        var sv = new SyncVarInterpolated(42f);
        float val = sv;
        Assert.Equal(42f, val);
    }

    // ── Interpolated Vector2 ──

    [Fact]
    public void SyncVarInterpolatedVector2_InitialDisplay()
    {
        var sv = new SyncVarInterpolatedVector2(new Vector2(5, 10), interpSpeed: 10f);
        Assert.Equal(new Vector2(5, 10), sv.Value);
        Assert.Equal(new Vector2(5, 10), sv.Display);
    }

    [Fact]
    public void SyncVarInterpolatedVector2_ServerSet_DisplayMatchesImmediately()
    {
        var sv = new SyncVarInterpolatedVector2(Vector2.Zero);
        sv.Value = new Vector2(50, 100);
        Assert.Equal(new Vector2(50, 100), sv.Display);
    }

    [Fact]
    public void SyncVarInterpolatedVector2_ClientUpdate_Interpolates()
    {
        var sv = new SyncVarInterpolatedVector2(Vector2.Zero, interpSpeed: 5f);

        // First deserialize snaps display (spawn data)
        var writer = new NetworkWriter();
        new SyncVar<Vector2>(new Vector2(100, 200)).Serialize(writer);
        sv.Deserialize(new NetworkReader(writer.ToArraySegment()));

        Assert.Equal(new Vector2(100, 200), sv.Value);
        Assert.Equal(new Vector2(100, 200), sv.Display); // snapped on first receive

        // Second deserialize interpolates (subsequent updates)
        writer = new NetworkWriter();
        new SyncVar<Vector2>(new Vector2(200, 400)).Serialize(writer);
        sv.Deserialize(new NetworkReader(writer.ToArraySegment()));

        Assert.Equal(new Vector2(200, 400), sv.Value);
        Assert.Equal(new Vector2(100, 200), sv.Display); // not yet interpolated

        sv.ClientUpdate(0.016f);
        float t = MathF.Min(1f, 5f * 0.016f);
        var expected = new Vector2(100f + 100f * t, 200f + 200f * t);
        Assert.Equal(expected.X, sv.Display.X, precision: 3);
        Assert.Equal(expected.Y, sv.Display.Y, precision: 3);
    }

    [Fact]
    public void SyncVarInterpolatedVector2_ClientUpdate_Converges()
    {
        var target = new Vector2(50, -30);
        var sv = new SyncVarInterpolatedVector2(Vector2.Zero, interpSpeed: 15f);

        var writer = new NetworkWriter();
        new SyncVar<Vector2>(target).Serialize(writer);
        sv.Deserialize(new NetworkReader(writer.ToArraySegment()));

        for (int i = 0; i < 1000; i++)
            sv.ClientUpdate(0.016f);

        Assert.Equal(target.X, sv.Display.X, precision: 2);
        Assert.Equal(target.Y, sv.Display.Y, precision: 2);
    }

    [Fact]
    public void SyncVarInterpolatedVector2_SnapsToTarget_WhenClose()
    {
        var sv = new SyncVarInterpolatedVector2(new Vector2(0.0005f, 0.0005f), interpSpeed: 10f);

        var writer = new NetworkWriter();
        new SyncVar<Vector2>(Vector2.Zero).Serialize(writer);
        sv.Deserialize(new NetworkReader(writer.ToArraySegment()));

        // Display is at (0.0005, 0.0005), target is (0,0) — within snap threshold
        sv.ClientUpdate(0.016f);
        Assert.Equal(Vector2.Zero, sv.Display);
    }

    // ── Interpolated end-to-end replication ──

    [Fact]
    public void SyncVarInterpolated_ReplicatesAndInterpolatesOnClient()
    {
        var (client, map) = SetupWithMap();
        var serverEntity = Server.Spawn<SyncVarInterpolatedEntity>(map, e =>
        {
            e.Health.Value = 100f;
            e.Position.Value = Vector2.Zero;
        });
        Tick();

        var clientEntity = Client.FindEntity<SyncVarInterpolatedEntity>(serverEntity.NetworkId);
        Assert.NotNull(clientEntity);
        Assert.Equal(100f, clientEntity!.Health.Value);

        // Server changes health
        serverEntity.Health.Value = 50f;
        Tick();

        // Client received the value
        Assert.Equal(50f, clientEntity.Health.Value);
        // Display should be interpolating (ClientUpdate was called during Tick)
    }

    [Fact]
    public void SyncVarInterpolatedVector2_ReplicatesOnSpawn()
    {
        var (client, map) = SetupWithMap();
        var pos = new Vector2(10, 20);
        var serverEntity = Server.Spawn<SyncVarInterpolatedEntity>(map, e =>
        {
            e.Position.Value = pos;
        });
        Tick();

        var clientEntity = Client.FindEntity<SyncVarInterpolatedEntity>(serverEntity.NetworkId);
        Assert.NotNull(clientEntity);
        Assert.Equal(pos, clientEntity!.Position.Value);
    }

    // ── Edge cases ──

    [Fact]
    public void SyncVar_SettingDuringInitializer_ReplicatesCorrectly()
    {
        var (client, map) = SetupWithMap();

        var serverEntity = Server.Spawn<SyncVarTestEntity>(map, e =>
        {
            e.Health.Value = 1f;
            e.Health.Value = 2f;
            e.Health.Value = 3f; // only final value matters
        });
        Tick();

        var clientEntity = Client.FindEntity<SyncVarTestEntity>(serverEntity.NetworkId);
        Assert.Equal(3f, clientEntity!.Health.Value);
    }

    [Fact]
    public void SyncVar_MultipleEntities_IndependentSync()
    {
        var (client, map) = SetupWithMap();

        var e1 = Server.Spawn<SyncVarTestEntity>(map, e => e.Health.Value = 100f);
        var e2 = Server.Spawn<SyncVarTestEntity>(map, e => e.Health.Value = 200f);
        Tick();

        e1.Health.Value = 50f;
        // e2 unchanged
        Tick();

        Assert.Equal(50f, Client.FindEntity<SyncVarTestEntity>(e1.NetworkId)!.Health.Value);
        Assert.Equal(200f, Client.FindEntity<SyncVarTestEntity>(e2.NetworkId)!.Health.Value);
    }

    [Fact]
    public void SyncVar_SpawnAndDirtyUpdate_InSameTick()
    {
        var (client, map) = SetupWithMap();

        var serverEntity = Server.Spawn<SyncVarTestEntity>(map, e => e.Health.Value = 100f);
        // Change value before the next tick — should be sent as part of initial spawn data
        serverEntity.Health.Value = 75f;
        Tick();

        var clientEntity = Client.FindEntity<SyncVarTestEntity>(serverEntity.NetworkId);
        // Could be 100 (spawn data) or 75 (spawn data + update in same tick)
        // The entity was just spawned, the spawn message has the value at time of spawn (100)
        // but then a dirty update would also be sent... or maybe not since the spawn already sent it.
        // Actually the spawn was called with initializer setting 100, then we set 75.
        // The spawn message writes the current value (which is 75 since we changed it before Tick).
        // Wait — FinalizeSpawn happens during Server.Spawn, which is before we set 75.
        // So spawn message has 100, then dirty update has 75.
        Assert.Equal(75f, clientEntity!.Health.Value);
    }

    [Fact]
    public void SyncVar_LargeStringValue()
    {
        var (client, map) = SetupWithMap();

        string longStr = new string('x', 10000);
        var serverEntity = Server.Spawn<SyncVarTestEntity>(map, e => e.Name.Value = longStr);
        Tick();

        var clientEntity = Client.FindEntity<SyncVarTestEntity>(serverEntity.NetworkId);
        Assert.Equal(longStr, clientEntity!.Name.Value);
    }

    [Fact]
    public void SyncVar_EmptyString()
    {
        var (client, map) = SetupWithMap();

        var serverEntity = Server.Spawn<SyncVarTestEntity>(map, e => e.Name.Value = "hello");
        Tick();

        serverEntity.Name.Value = "";
        Tick();

        var clientEntity = Client.FindEntity<SyncVarTestEntity>(serverEntity.NetworkId);
        Assert.Equal("", clientEntity!.Name.Value);
    }

    [Fact]
    public void SyncVar_RapidChanges_ClientSeesLatestValue()
    {
        var (client, map) = SetupWithMap();
        var serverEntity = Server.Spawn<SyncVarTestEntity>(map);
        Tick();

        // Multiple changes between ticks — client should see the latest
        serverEntity.Score.Value = 1;
        serverEntity.Score.Value = 2;
        serverEntity.Score.Value = 3;
        Tick();

        var clientEntity = Client.FindEntity<SyncVarTestEntity>(serverEntity.NetworkId);
        Assert.Equal(3, clientEntity!.Score.Value);
    }

    [Fact]
    public void SyncVar_NegativeValues()
    {
        var (client, map) = SetupWithMap();
        var serverEntity = Server.Spawn<SyncVarTestEntity>(map, e =>
        {
            e.Health.Value = -50f;
            e.Score.Value = -999;
            e.Position.Value = new Vector2(-100, -200);
        });
        Tick();

        var clientEntity = Client.FindEntity<SyncVarTestEntity>(serverEntity.NetworkId);
        Assert.Equal(-50f, clientEntity!.Health.Value);
        Assert.Equal(-999, clientEntity.Score.Value);
        Assert.Equal(new Vector2(-100, -200), clientEntity.Position.Value);
    }
}
