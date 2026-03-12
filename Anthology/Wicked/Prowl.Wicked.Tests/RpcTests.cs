using System.Numerics;
using Prowl.Wicked;
using Prowl.Wicked.Transport;

namespace Prowl.Wicked.Tests;

/// <summary>
/// Integration tests for the RPC dispatch pipeline.
/// These test the full flow: call on one side → serialize → transport → deserialize → execute on other side.
/// </summary>
public class ServerRpcTests : IDisposable
{
    private readonly InMemoryServerTransport _serverTransport;
    private readonly InMemoryClientTransport _clientTransport;

    public ServerRpcTests()
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

    private (RemoteClient serverClient, RpcEntity serverEntity, RpcEntity clientEntity) SetupOwned()
    {
        Server.Start(0);
        Client.Connect("localhost", 0);
        Tick();
        var serverClient = Server.Clients.First();

        var map = Server.CreateMap<TestMap>();
        var serverEntity = Server.Spawn<RpcEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(serverEntity);
        Tick();

        var clientEntity = Client.FindEntity<RpcEntity>(serverEntity.NetworkId);
        return (serverClient, serverEntity, clientEntity!);
    }

    private (RemoteClient serverClient, RpcEntity serverNpc, RpcEntity clientNpc) SetupUnowned()
    {
        Server.Start(0);
        Client.Connect("localhost", 0);
        Tick();
        var serverClient = Server.Clients.First();

        var map = Server.CreateMap<TestMap>();
        // Spawn a player so the client observes the map
        var player = Server.Spawn<RpcEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(player);
        // Spawn an unowned NPC
        var serverNpc = Server.Spawn<RpcEntity>(map);
        Tick();

        var clientNpc = Client.FindEntity<RpcEntity>(serverNpc.NetworkId);
        return (serverClient, serverNpc, clientNpc!);
    }

    // ── EntityCommand: basic dispatch ──

    [Fact]
    public void EntityCommand_Void_ExecutesOnServer()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        clientEntity.FireVoid();
        Tick();

        Assert.Contains("FireVoid", serverEntity.Log);
        Assert.DoesNotContain("FireVoid", clientEntity.Log);
    }

    [Fact]
    public void EntityCommand_Void_WithArgs_ExecutesOnServer()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        clientEntity.FireWithArgs(42, "hello");
        Tick();

        Assert.Contains("FireWithArgs:42:hello", serverEntity.Log);
        Assert.DoesNotContain("FireWithArgs:42:hello", clientEntity.Log);
    }

    // ── EntityCommand: overloaded methods ──

    [Fact]
    public void EntityCommand_OverloadedVoid_SingleParam_DispatchesCorrectly()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        clientEntity.Attack(7);
        Tick();

        Assert.Contains("Attack:7", serverEntity.Log);
        Assert.All(serverEntity.Log, entry => Assert.DoesNotContain("Attack:7:", entry));
    }

    [Fact]
    public void EntityCommand_OverloadedVoid_TwoParams_DispatchesCorrectly()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        clientEntity.Attack(7, 25.5f);
        Tick();

        Assert.Contains("Attack:7:25.5", serverEntity.Log);
    }

    [Fact]
    public void EntityCommand_OverloadedPromise_SingleParam_DispatchesCorrectly()
    {
        var (_, _, clientEntity) = SetupOwned();

        var promise = clientEntity.Compute(5);
        Tick();

        Assert.True(promise.IsCompleted);
        Assert.Equal(50, promise.Result);
    }

    [Fact]
    public void EntityCommand_OverloadedPromise_TwoParams_DispatchesCorrectly()
    {
        var (_, _, clientEntity) = SetupOwned();

        var promise = clientEntity.Compute(3, 7);
        Tick();

        Assert.True(promise.IsCompleted);
        Assert.Equal(10, promise.Result);
    }

    // ── EntityCommand: return values ──

    [Fact]
    public void EntityCommand_ReturnsInt_ViaPromise()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        var promise = clientEntity.DoubleIt(5);

        Assert.False(promise.IsCompleted);

        Tick();

        Assert.True(promise.IsCompleted);
        Assert.True(promise.IsSuccess);
        Assert.Equal(10, promise.Result);
    }

    [Fact]
    public void EntityCommand_ReturnsString_ViaPromise()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        var promise = clientEntity.Echo("test");
        Assert.False(promise.IsCompleted);

        Tick();

        Assert.True(promise.IsCompleted);
        Assert.Equal("echo:test", promise.Result);
    }

    [Fact]
    public void EntityCommand_ReturnsBool_ViaPromise()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        var promise = clientEntity.IsAlive();
        Tick();

        Assert.True(promise.IsCompleted);
        Assert.True(promise.Result);
    }

    [Fact]
    public void EntityCommand_NonGenericPromise_Acknowledges()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        var promise = clientEntity.Acknowledge();
        Assert.False(promise.IsCompleted);

        Tick();

        Assert.True(promise.IsCompleted);
        Assert.True(promise.IsSuccess);
        Assert.Contains("Acknowledge", serverEntity.Log);
    }

    [Fact]
    public void EntityCommand_Promise_ThenCallbackFires()
    {
        var (_, _, clientEntity) = SetupOwned();

        int received = 0;
        clientEntity.DoubleIt(7).Then(v => received = v);

        Tick();

        Assert.Equal(14, received);
    }

    // ── EntityCommand: error propagation ──

    [Fact]
    public void EntityCommand_Throws_PromiseRejectsWithMessage()
    {
        var (_, _, clientEntity) = SetupOwned();

        var promise = clientEntity.ThrowsError();
        Tick();

        Assert.True(promise.IsCompleted);
        Assert.False(promise.IsSuccess);
        Assert.NotNull(promise.Error);
        Assert.Equal("Server error", promise.Error.Message);
    }

    [Fact]
    public void EntityCommand_Throws_CatchCallbackFires()
    {
        var (_, _, clientEntity) = SetupOwned();

        Exception? received = null;
        clientEntity.ThrowsError().Catch(e => received = e);

        Tick();

        Assert.NotNull(received);
        Assert.Equal("Server error", received.Message);
    }

    [Fact]
    public void EntityCommand_Throws_OnlyMessageTransmitted_NotType()
    {
        var (_, _, clientEntity) = SetupOwned();

        var promise = clientEntity.ThrowsError();
        Tick();

        Assert.IsType<Exception>(promise.Error);
    }

    // ── EntityCommand: Sender ──

    [Fact]
    public void EntityCommand_SetsSenderToCallingClient()
    {
        var (serverClient, serverEntity, clientEntity) = SetupOwned();

        var promise = clientEntity.WhoAmI();
        Tick();

        Assert.True(promise.IsCompleted);
        Assert.Equal(serverClient.ClientId, promise.Result);
    }

    [Fact]
    public void EntityCommand_SenderIsNullAfterExecution()
    {
        var (_, _, clientEntity) = SetupOwned();

        clientEntity.FireVoid();
        Tick();

        Assert.Null(NetworkObject.Sender);
    }

    // ── EntityCommand: RequireOwner ──

    [Fact]
    public void EntityCommand_RequireOwnerTrue_OwnerCanCall()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        clientEntity.OwnerOnly();
        Tick();

        Assert.Contains("OwnerOnly", serverEntity.Log);
    }

    [Fact]
    public void EntityCommand_RequireOwnerTrue_NonOwnerCannotCall()
    {
        var (serverClient, serverNpc, clientNpc) = SetupUnowned();

        clientNpc.OwnerOnly();
        Tick();

        Assert.DoesNotContain("OwnerOnly", serverNpc.Log);
    }

    [Fact]
    public void EntityCommand_RequireOwnerFalse_AnyClientCanCall()
    {
        var (_, serverNpc, clientNpc) = SetupUnowned();

        clientNpc.AnyoneCanCall();
        Tick();

        Assert.Contains("AnyoneCanCall", serverNpc.Log);
    }

    // ── EntityCommand: parameter types ──

    [Fact]
    public void EntityCommand_IntParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        clientEntity.TakeInt(int.MaxValue);
        Tick();

        Assert.Contains($"TakeInt:{int.MaxValue}", serverEntity.Log);
    }

    [Fact]
    public void EntityCommand_FloatParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        clientEntity.TakeFloat(3.14f);
        Tick();

        Assert.Contains("TakeFloat:3.14", serverEntity.Log);
    }

    [Fact]
    public void EntityCommand_StringParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        clientEntity.TakeString("hello world");
        Tick();

        Assert.Contains("TakeString:hello world", serverEntity.Log);
    }

    [Fact]
    public void EntityCommand_BoolParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        clientEntity.TakeBool(true);
        Tick();

        Assert.Contains("TakeBool:True", serverEntity.Log);
    }

    [Fact]
    public void EntityCommand_Vector2Param()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        clientEntity.TakeVector2(new Vector2(10.5f, -3.25f));
        Tick();

        Assert.Contains("TakeVector2:10.5:-3.25", serverEntity.Log);
    }

    [Fact]
    public void EntityCommand_EnumParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        clientEntity.TakeEnum(RpcTestEnum.Heal);
        Tick();

        Assert.Contains("TakeEnum:Heal", serverEntity.Log);
    }

    [Fact]
    public void EntityCommand_MultipleParams()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        clientEntity.TakeMultiple(42, "foo", true, new Vector2(1, 2));
        Tick();

        Assert.Contains("TakeMultiple:42:foo:True:1:2", serverEntity.Log);
    }

    [Fact]
    public void EntityCommand_NullStringParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        clientEntity.TakeString(null!);
        Tick();

        Assert.Contains("TakeString:null", serverEntity.Log);
    }

    [Fact]
    public void EntityCommand_INetworkSerializableParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        clientEntity.TakeSerializable(new DamageInfo { Amount = 42, Type = "fire", IsCritical = true });
        Tick();

        Assert.Contains("TakeSerializable:42:fire:True", serverEntity.Log);
    }

    [Fact]
    public void EntityCommand_ByteArrayParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        clientEntity.TakeByteArray(new byte[] { 0, 127, 255 });
        Tick();

        Assert.Contains("TakeByteArray:3:0,127,255", serverEntity.Log);
    }

    [Fact]
    public void EntityCommand_GuidParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        var testGuid = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        clientEntity.TakeGuid(testGuid);
        Tick();

        Assert.Contains($"TakeGuid:{testGuid}", serverEntity.Log);
    }

    [Fact]
    public void EntityCommand_SByteParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        clientEntity.TakeSByte(-42);
        Tick();

        Assert.Contains("TakeSByte:-42", serverEntity.Log);
    }

    [Fact]
    public void EntityCommand_ShortParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        clientEntity.TakeShort(short.MinValue);
        Tick();

        Assert.Contains($"TakeShort:{short.MinValue}", serverEntity.Log);
    }

    [Fact]
    public void EntityCommand_LongParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        clientEntity.TakeLong(long.MaxValue);
        Tick();

        Assert.Contains($"TakeLong:{long.MaxValue}", serverEntity.Log);
    }

    [Fact]
    public void EntityCommand_ULongParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        clientEntity.TakeULong(ulong.MaxValue);
        Tick();

        Assert.Contains($"TakeULong:{ulong.MaxValue}", serverEntity.Log);
    }

    [Fact]
    public void EntityCommand_DoubleParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        clientEntity.TakeDouble(3.141592653589793);
        Tick();

        Assert.Contains("TakeDouble:3.141592653589793", serverEntity.Log);
    }

    [Fact]
    public void EntityCommand_IntArrayParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        clientEntity.TakeIntArray(new[] { 1, 2, 3 });
        Tick();

        Assert.Contains("TakeIntArray:1,2,3", serverEntity.Log);
    }

    [Fact]
    public void EntityCommand_StringArrayParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        clientEntity.TakeStringArray(new[] { "a", "b", "c" });
        Tick();

        Assert.Contains("TakeStringArray:a,b,c", serverEntity.Log);
    }

    [Fact]
    public void EntityCommand_INetworkSerializableArrayParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        clientEntity.TakeSerializableArray(new[]
        {
            new DamageInfo { Amount = 10, Type = "fire", IsCritical = false },
            new DamageInfo { Amount = 25, Type = "ice", IsCritical = true }
        });
        Tick();

        Assert.Contains("TakeSerializableArray:2:10/fire;25/ice", serverEntity.Log);
    }

    // ── EntityCommand: entity reference parameters ──

    [Fact]
    public void EntityCommand_EntityRefParam_SendsNetworkId()
    {
        var (serverClient, serverEntity, clientEntity) = SetupOwned();

        // Spawn a second entity the client can reference
        var serverMap = serverEntity.Map!;
        var target = Server.Spawn<RpcEntity>(serverMap);
        Tick();

        var clientTarget = Client.FindEntity<RpcEntity>(target.NetworkId);
        Assert.NotNull(clientTarget);

        clientEntity.TakeEntityRef(clientTarget);
        Tick();

        Assert.Contains($"TakeEntityRef:{target.NetworkId}", serverEntity.Log);
    }

    [Fact]
    public void EntityCommand_EntityRefParam_NullEntity()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        clientEntity.TakeEntityRef(null);
        Tick();

        Assert.Contains("TakeEntityRef:null", serverEntity.Log);
    }

    [Fact]
    public void EntityCommand_TypedEntityRefParam_SendsNetworkId()
    {
        var (serverClient, serverEntity, clientEntity) = SetupOwned();

        var serverMap = serverEntity.Map!;
        var target = Server.Spawn<RpcEntity>(serverMap);
        Tick();

        var clientTarget = Client.FindEntity<RpcEntity>(target.NetworkId);
        Assert.NotNull(clientTarget);

        clientEntity.TakeEntityRefTyped(clientTarget);
        Tick();

        Assert.Contains($"TakeEntityRefTyped:{target.NetworkId}", serverEntity.Log);
    }

}

public class ClientRpcTests : IDisposable
{
    private readonly InMemoryServerTransport _serverTransport;
    private readonly InMemoryClientTransport _clientTransport;

    public ClientRpcTests()
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

    private (RemoteClient serverClient, RpcEntity serverEntity, RpcEntity clientEntity) SetupOwned()
    {
        Server.Start(0);
        Client.Connect("localhost", 0);
        Tick();
        var serverClient = Server.Clients.First();

        var map = Server.CreateMap<TestMap>();
        var serverEntity = Server.Spawn<RpcEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(serverEntity);
        Tick();

        var clientEntity = Client.FindEntity<RpcEntity>(serverEntity.NetworkId);
        return (serverClient, serverEntity, clientEntity!);
    }

    // ── EntityRpc: Observers (default) ──

    [Fact]
    public void EntityRpc_Observers_ExecutesOnClient()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        serverEntity.NotifyAll("boom");
        Tick();

        Assert.Contains("NotifyAll:boom", clientEntity.Log);
        Assert.DoesNotContain("NotifyAll:boom", serverEntity.Log);
    }

    [Fact]
    public void EntityRpc_Observers_WithMultipleParams()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        serverEntity.NotifyComplex(42, "test", new Vector2(1, 2));
        Tick();

        Assert.Contains("NotifyComplex:42:test:1:2", clientEntity.Log);
    }

    // ── EntityRpc: overloaded methods ──

    [Fact]
    public void EntityRpc_OverloadedVoid_SingleParam_DispatchesCorrectly()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        serverEntity.Notify("hello");
        Tick();

        Assert.Contains("Notify:hello", clientEntity.Log);
        Assert.All(clientEntity.Log, entry => Assert.DoesNotContain("Notify:hello:", entry));
    }

    [Fact]
    public void EntityRpc_OverloadedVoid_TwoParams_DispatchesCorrectly()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        serverEntity.Notify("alert", 5);
        Tick();

        Assert.Contains("Notify:alert:5", clientEntity.Log);
    }

    // ── EntityRpc: Owner ──

    [Fact]
    public void EntityRpc_Owner_ExecutesOnOwnerClient()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        serverEntity.NotifyOwner(100);
        Tick();

        Assert.Contains("NotifyOwner:100", clientEntity.Log);
    }

    [Fact]
    public void EntityRpc_Owner_UnownedEntity_SilentlyDropped()
    {
        Server.Start(0);
        Client.Connect("localhost", 0);
        Tick();
        var serverClient = Server.Clients.First();

        var map = Server.CreateMap<TestMap>();
        var player = Server.Spawn<RpcEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(player);
        var unownedEntity = Server.Spawn<RpcEntity>(map);
        Tick();

        var clientUnowned = Client.FindEntity<RpcEntity>(unownedEntity.NetworkId);

        unownedEntity.NotifyOwner(999);
        Tick();

        Assert.DoesNotContain("NotifyOwner:999", clientUnowned!.Log);
    }

    // ── EntityRpc: Player ──

    [Fact]
    public void EntityRpc_Player_ExecutesOnTargetedClient()
    {
        var (serverClient, serverEntity, clientEntity) = SetupOwned();

        serverEntity.Whisper(serverClient, "secret");
        Tick();

        Assert.Contains("Whisper:secret", clientEntity.Log);
    }

    [Fact]
    public void EntityRpc_Player_FirstParamIsNullOnClient()
    {
        var (serverClient, serverEntity, clientEntity) = SetupOwned();

        serverEntity.WhisperCheckTarget(serverClient, "msg");
        Tick();

        Assert.Contains("WhisperCheckTarget:target=null:msg", clientEntity.Log);
    }

    // ── EntityRpc: Player with RemoteClient[] ──

    [Fact]
    public void EntityRpc_PlayerArray_ExecutesOnTargetedClients()
    {
        var (serverClient, serverEntity, clientEntity) = SetupOwned();

        serverEntity.BroadcastToPlayers(new[] { serverClient }, "hello");
        Tick();

        Assert.Contains("BroadcastToPlayers:hello", clientEntity.Log);
    }

    // ── EntityRpc: ExcludeOwner ──

    [Fact]
    public void EntityRpc_ExcludeOwner_DoesNotExecuteOnOwner()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        serverEntity.NotifyNonOwners("effect");
        Tick();

        Assert.DoesNotContain("NotifyNonOwners:effect", clientEntity.Log);
    }

    // ── EntityRpc: parameter types ──

    [Fact]
    public void EntityRpc_IntParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        serverEntity.ClientTakeInt(int.MinValue);
        Tick();

        Assert.Contains($"ClientTakeInt:{int.MinValue}", clientEntity.Log);
    }

    [Fact]
    public void EntityRpc_StringParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        serverEntity.ClientTakeString("hello");
        Tick();

        Assert.Contains("ClientTakeString:hello", clientEntity.Log);
    }

    [Fact]
    public void EntityRpc_BoolParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        serverEntity.ClientTakeBool(false);
        Tick();

        Assert.Contains("ClientTakeBool:False", clientEntity.Log);
    }

    [Fact]
    public void EntityRpc_Vector2Param()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        serverEntity.ClientTakeVector2(new Vector2(-5f, 10f));
        Tick();

        Assert.Contains("ClientTakeVector2:-5:10", clientEntity.Log);
    }

    [Fact]
    public void EntityRpc_EnumParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        serverEntity.ClientTakeEnum(RpcTestEnum.Attack);
        Tick();

        Assert.Contains("ClientTakeEnum:Attack", clientEntity.Log);
    }

    [Fact]
    public void EntityRpc_NullStringParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        serverEntity.ClientTakeString(null!);
        Tick();

        Assert.Contains("ClientTakeString:null", clientEntity.Log);
    }

    [Fact]
    public void EntityRpc_ByteArrayParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        serverEntity.ClientTakeByteArray(new byte[] { 10, 20, 30 });
        Tick();

        Assert.Contains("ClientTakeByteArray:3:10,20,30", clientEntity.Log);
    }

    [Fact]
    public void EntityRpc_GuidParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        var testGuid = Guid.Parse("abcdef01-2345-6789-abcd-ef0123456789");
        serverEntity.ClientTakeGuid(testGuid);
        Tick();

        Assert.Contains($"ClientTakeGuid:{testGuid}", clientEntity.Log);
    }

    [Fact]
    public void EntityRpc_SByteParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        serverEntity.ClientTakeSByte(sbyte.MinValue);
        Tick();

        Assert.Contains($"ClientTakeSByte:{sbyte.MinValue}", clientEntity.Log);
    }

    [Fact]
    public void EntityRpc_ShortParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        serverEntity.ClientTakeShort(short.MaxValue);
        Tick();

        Assert.Contains($"ClientTakeShort:{short.MaxValue}", clientEntity.Log);
    }

    [Fact]
    public void EntityRpc_LongParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        serverEntity.ClientTakeLong(long.MinValue);
        Tick();

        Assert.Contains($"ClientTakeLong:{long.MinValue}", clientEntity.Log);
    }

    [Fact]
    public void EntityRpc_ULongParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        serverEntity.ClientTakeULong(ulong.MaxValue);
        Tick();

        Assert.Contains($"ClientTakeULong:{ulong.MaxValue}", clientEntity.Log);
    }

    [Fact]
    public void EntityRpc_DoubleParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        serverEntity.ClientTakeDouble(2.718281828459045);
        Tick();

        Assert.Contains("ClientTakeDouble:2.718281828459045", clientEntity.Log);
    }

    [Fact]
    public void EntityRpc_IntArrayParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        serverEntity.ClientTakeIntArray(new[] { 100, 200, 300 });
        Tick();

        Assert.Contains("ClientTakeIntArray:100,200,300", clientEntity.Log);
    }

    [Fact]
    public void EntityRpc_INetworkSerializableArrayParam()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        serverEntity.ClientTakeSerializableArray(new[]
        {
            new DamageInfo { Amount = 5, Type = "slash", IsCritical = false }
        });
        Tick();

        Assert.Contains("ClientTakeSerializableArray:1:5/slash", clientEntity.Log);
    }

    // ── EntityRpc: entity reference parameters ──

    [Fact]
    public void EntityRpc_EntityRefParam_SendsNetworkId()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        // Spawn a second entity to reference
        var serverMap = serverEntity.Map!;
        var target = Server.Spawn<RpcEntity>(serverMap);
        Tick();

        serverEntity.ClientTakeEntityRef(target);
        Tick();

        Assert.Contains($"ClientTakeEntityRef:{target.NetworkId}", clientEntity.Log);
    }

    [Fact]
    public void EntityRpc_EntityRefParam_NullEntity()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        serverEntity.ClientTakeEntityRef(null);
        Tick();

        Assert.Contains("ClientTakeEntityRef:null", clientEntity.Log);
    }

    [Fact]
    public void EntityRpc_TypedEntityRefParam_SendsNetworkId()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        var serverMap = serverEntity.Map!;
        var target = Server.Spawn<RpcEntity>(serverMap);
        Tick();

        serverEntity.ClientTakeEntityRefTyped(target);
        Tick();

        Assert.Contains($"ClientTakeEntityRefTyped:{target.NetworkId}", clientEntity.Log);
    }
}

public class MapRpcTests : IDisposable
{
    private readonly InMemoryServerTransport _serverTransport;
    private readonly InMemoryClientTransport _clientTransport;

    public MapRpcTests()
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

    private (RemoteClient serverClient, RpcMap serverMap, RpcMap clientMap) Setup()
    {
        Server.Start(0);
        Client.Connect("localhost", 0);
        Tick();
        var serverClient = Server.Clients.First();

        var serverMap = Server.CreateMap<RpcMap>();
        var player = Server.Spawn<RpcEntity>(serverMap, serverClient);
        serverClient.AssignPlayerEntity(player);
        Tick();

        var clientMap = (RpcMap)Client.GetMap(serverMap.MapId)!;
        return (serverClient, serverMap, clientMap);
    }

    // ── MapRpc: Observers ──

    [Fact]
    public void Map_MapRpc_Observers_ExecutesOnClient()
    {
        var (_, serverMap, clientMap) = Setup();

        serverMap.ShowAnnouncement("Boss spawned!");
        Tick();

        Assert.Contains("ShowAnnouncement:Boss spawned!", clientMap.Log);
        Assert.DoesNotContain("ShowAnnouncement:Boss spawned!", serverMap.Log);
    }

    // ── MapRpc: Player ──

    [Fact]
    public void Map_MapRpc_Player_ExecutesOnTargetedClient()
    {
        var (serverClient, serverMap, clientMap) = Setup();

        serverMap.ShowBossIntro(serverClient, "Dragon");
        Tick();

        Assert.Contains("ShowBossIntro:Dragon", clientMap.Log);
    }
}

public class StaticRpcTests : IDisposable
{
    private readonly InMemoryServerTransport _serverTransport;
    private readonly InMemoryClientTransport _clientTransport;

    public StaticRpcTests()
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

    private RemoteClient Connect()
    {
        Server.Start(0);
        Client.Connect("localhost", 0);
        Tick();
        return Server.Clients.First();
    }

    [Fact]
    public void StaticCommand_Void_ExecutesOnServer()
    {
        Connect();
        TestRpcs.ServerLog.Clear();

        TestRpcs.Login("admin", "pass123");
        Tick();

        Assert.Contains("Login:admin:pass123", TestRpcs.ServerLog);
    }

    [Fact]
    public void StaticCommand_SetsSender()
    {
        var serverClient = Connect();
        TestRpcs.ServerLog.Clear();

        var promise = TestRpcs.GetMyId();
        Tick();

        Assert.True(promise.IsCompleted);
        Assert.Equal(serverClient.ClientId, promise.Result);
    }

    [Fact]
    public void StaticCommand_RpcPromise_ReturnsValue()
    {
        Connect();
        TestRpcs.ServerLog.Clear();

        var promise = TestRpcs.LoginAndGetResult("admin", "pass123");
        Assert.False(promise.IsCompleted);

        Tick();

        Assert.True(promise.IsCompleted);
        Assert.True(promise.Result);
    }

    [Fact]
    public void StaticRpc_SingleClient_ExecutesOnClient()
    {
        var serverClient = Connect();
        TestRpcs.ClientLog.Clear();

        TestRpcs.ShowMessage(serverClient, "Welcome!");
        Tick();

        Assert.Contains("ShowMessage:Welcome!", TestRpcs.ClientLog);
    }

    [Fact]
    public void StaticRpc_MultipleClients_ExecutesOnClients()
    {
        var serverClient = Connect();
        TestRpcs.ClientLog.Clear();

        TestRpcs.ShowLoginResult(new[] { serverClient }, true, "Logged in");
        Tick();

        Assert.Contains("ShowLoginResult:True:Logged in", TestRpcs.ClientLog);
    }

    [Fact]
    public void StaticCommand_ByteArrayAndGuidParams()
    {
        Connect();
        TestRpcs.ServerLog.Clear();

        var testGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
        TestRpcs.SendData(new byte[] { 1, 2, 3, 4, 5 }, testGuid);
        Tick();

        Assert.Contains($"SendData:5:{testGuid}", TestRpcs.ServerLog);
    }

    [Fact]
    public void StaticRpc_ByteArrayAndGuidParams()
    {
        var serverClient = Connect();
        TestRpcs.ClientLog.Clear();

        var testGuid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        TestRpcs.ReceiveData(new[] { serverClient }, new byte[] { 99, 100 }, testGuid);
        Tick();

        Assert.Contains($"ReceiveData:2:{testGuid}", TestRpcs.ClientLog);
    }
}

public class InheritedRpcTests : IDisposable
{
    private readonly InMemoryServerTransport _serverTransport;
    private readonly InMemoryClientTransport _clientTransport;

    public InheritedRpcTests()
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

    private (RemoteClient serverClient, Warrior serverWarrior, Warrior clientWarrior) Setup()
    {
        Server.Start(0);
        Client.Connect("localhost", 0);
        Tick();
        var serverClient = Server.Clients.First();

        var map = Server.CreateMap<TestMap>();
        var serverWarrior = Server.Spawn<Warrior>(map, serverClient);
        serverClient.AssignPlayerEntity(serverWarrior);
        Tick();

        var clientWarrior = Client.FindEntity<Warrior>(serverWarrior.NetworkId);
        return (serverClient, serverWarrior, clientWarrior!);
    }

    [Fact]
    public void InheritedEntityCommand_DispatchesCorrectly()
    {
        var (_, serverWarrior, clientWarrior) = Setup();

        clientWarrior.Attack(42);
        Tick();

        Assert.Contains("Attack:42", serverWarrior.Log);
    }

    [Fact]
    public void OwnEntityCommand_WithInheritance_DispatchesCorrectly()
    {
        var (_, serverWarrior, clientWarrior) = Setup();

        clientWarrior.Block();
        Tick();

        Assert.Contains("Block", serverWarrior.Log);
    }

    [Fact]
    public void InheritedEntityRpc_DispatchesCorrectly()
    {
        var (_, serverWarrior, clientWarrior) = Setup();

        serverWarrior.TakeDamage(50);
        Tick();

        Assert.Contains("TakeDamage:50", clientWarrior.Log);
    }

    [Fact]
    public void OwnEntityRpc_WithInheritance_DispatchesCorrectly()
    {
        var (_, serverWarrior, clientWarrior) = Setup();

        serverWarrior.ShowBlock();
        Tick();

        Assert.Contains("ShowBlock", clientWarrior.Log);
    }
}

/// <summary>
/// Tests for RpcPromise resolution, callbacks, and timeout.
/// These test the promise infrastructure that the RPC dispatch system relies on.
/// </summary>
public class RpcPromiseTests
{
    [Fact]
    public void Resolve_FiresThen_NotCatch()
    {
        var promise = new RpcPromise();
        var log = new List<string>();
        promise.Then(() => log.Add("then")).Catch(_ => log.Add("catch")).Finally(() => log.Add("finally"));

        promise.Resolve();

        Assert.Equal(new[] { "then", "finally" }, log);
    }

    [Fact]
    public void Reject_FiresCatch_NotThen()
    {
        var promise = new RpcPromise();
        var log = new List<string>();
        promise.Then(() => log.Add("then")).Catch(_ => log.Add("catch")).Finally(() => log.Add("finally"));

        promise.Reject(new Exception("fail"));

        Assert.Equal(new[] { "catch", "finally" }, log);
    }

    [Fact]
    public void Reject_ErrorMessagePreserved()
    {
        var promise = new RpcPromise();
        promise.Reject(new Exception("Not enough resources"));

        Assert.Equal("Not enough resources", promise.Error!.Message);
    }

    [Fact]
    public void MultipleCallbacks_AllFireInOrder()
    {
        var promise = new RpcPromise<int>();
        var log = new List<string>();
        promise.Then(v => log.Add($"typed1:{v}"));
        promise.Then(v => log.Add($"typed2:{v}"));
        ((RpcPromise)promise).Then(() => log.Add("base"));
        promise.Finally(() => log.Add("finally"));

        promise.Resolve(42);

        Assert.Equal(new[] { "typed1:42", "typed2:42", "base", "finally" }, log);
    }

    [Fact]
    public void Timeout_RejectsWithTimeoutException()
    {
        var promise = new RpcPromise();
        promise.Timeout(0.01f);
        Thread.Sleep(50);

        promise.CheckTimeout();

        Assert.True(promise.IsCompleted);
        Assert.False(promise.IsSuccess);
        Assert.IsType<TimeoutException>(promise.Error);
    }

    [Fact]
    public void Timeout_DoesNotReject_IfAlreadyResolved()
    {
        var promise = new RpcPromise();
        promise.Timeout(0.01f);
        promise.Resolve();
        Thread.Sleep(50);

        Assert.False(promise.CheckTimeout());
        Assert.True(promise.IsSuccess);
    }

    [Fact]
    public void ImplicitConversion_CreatesResolvedPromise()
    {
        RpcPromise<int> promise = 42;

        Assert.True(promise.IsCompleted);
        Assert.True(promise.IsSuccess);
        Assert.Equal(42, promise.Result);
    }

    [Fact]
    public void Completed_IsPreResolved_FreshEachTime()
    {
        var a = RpcPromise.Completed;
        var b = RpcPromise.Completed;

        Assert.True(a.IsCompleted);
        Assert.True(a.IsSuccess);
        Assert.NotSame(a, b);
    }

    [Fact]
    public void CallbacksRegisteredAfterResolve_FireImmediately()
    {
        var promise = new RpcPromise<int>();
        promise.Resolve(99);

        int received = 0;
        promise.Then(v => received = v);

        Assert.Equal(99, received);
    }
}

public class PromiseLifecycleTests : IDisposable
{
    private readonly InMemoryServerTransport _serverTransport;
    private readonly InMemoryClientTransport _clientTransport;

    public PromiseLifecycleTests()
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

    private (RemoteClient serverClient, RpcEntity serverEntity, RpcEntity clientEntity) SetupOwned()
    {
        Server.Start(0);
        Client.Connect("localhost", 0);
        Tick();
        var serverClient = Server.Clients.First();

        var map = Server.CreateMap<TestMap>();
        var serverEntity = Server.Spawn<RpcEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(serverEntity);
        Tick();

        var clientEntity = Client.FindEntity<RpcEntity>(serverEntity.NetworkId);
        return (serverClient, serverEntity, clientEntity!);
    }

    [Fact]
    public void Disconnect_RejectsPendingPromises()
    {
        var (_, _, clientEntity) = SetupOwned();

        var promise = clientEntity.DoubleIt(5);
        Assert.False(promise.IsCompleted);

        Client.Disconnect();

        Assert.True(promise.IsCompleted);
        Assert.False(promise.IsSuccess);
        Assert.Equal("Disconnected", promise.Error!.Message);
    }

    [Fact]
    public void TransportDisconnect_RejectsPendingPromises()
    {
        Server.Start(0);
        Client.Connect("localhost", 0);
        Tick();
        var serverClient = Server.Clients.First();

        var promise = new RpcPromise<int>();
        Client.__TrackPromise(promise, 1);
        Assert.False(promise.IsCompleted);

        _serverTransport.Disconnect(serverClient.ConnectionId, "kicked");
        Tick();

        Assert.True(promise.IsCompleted);
        Assert.False(promise.IsSuccess);
        Assert.Equal("Disconnected", promise.Error!.Message);
    }

    [Fact]
    public void Timeout_RejectsAutomaticallyViaTick()
    {
        var (_, _, clientEntity) = SetupOwned();

        var promise = clientEntity.DoubleIt(5);
        promise.Timeout(0.01f);
        Assert.False(promise.IsCompleted);

        Thread.Sleep(50);

        Client.Tick();

        Assert.True(promise.IsCompleted);
        Assert.False(promise.IsSuccess);
        Assert.IsType<TimeoutException>(promise.Error);
    }

    [Fact]
    public void EntityCommand_ReturnsUInt_ViaPromise()
    {
        var (_, serverEntity, clientEntity) = SetupOwned();

        var promise = clientEntity.GetNetworkId();
        Assert.False(promise.IsCompleted);

        Tick();

        Assert.True(promise.IsCompleted);
        Assert.True(promise.IsSuccess);
        Assert.Equal(serverEntity.NetworkId, promise.Result);
    }

    [Fact]
    public void EntityCommand_ReturnsFloat_ViaPromise()
    {
        var (_, _, clientEntity) = SetupOwned();

        var promise = clientEntity.GetHealth();
        Assert.False(promise.IsCompleted);

        Tick();

        Assert.True(promise.IsCompleted);
        Assert.True(promise.IsSuccess);
        Assert.Equal(75.5f, promise.Result);
    }
}

public class WrongSideRpcTests : IDisposable
{
    private readonly InMemoryServerTransport _serverTransport;
    private readonly InMemoryClientTransport _clientTransport;

    public WrongSideRpcTests()
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

    [Fact]
    public void EntityCommand_CalledOnServer_Throws()
    {
        Server.Start(0);
        Client.Connect("localhost", 0);
        Tick();
        var serverClient = Server.Clients.First();

        var map = Server.CreateMap<TestMap>();
        var serverEntity = Server.Spawn<RpcEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(serverEntity);
        Tick();

        Assert.Throws<InvalidOperationException>(() => serverEntity.FireVoid());
    }

    [Fact]
    public void EntityRpc_CalledOnClient_Throws()
    {
        Server.Start(0);
        Client.Connect("localhost", 0);
        Tick();
        var serverClient = Server.Clients.First();

        var map = Server.CreateMap<TestMap>();
        var serverEntity = Server.Spawn<RpcEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(serverEntity);
        Tick();

        var clientEntity = Client.FindEntity<RpcEntity>(serverEntity.NetworkId)!;
        Assert.Throws<InvalidOperationException>(() => clientEntity.NotifyAll("test"));
    }

    [Fact]
    public void StaticCommand_CalledWithoutClient_Throws()
    {
        Server.Start(0);
        // No client connected — calling a static command should throw
        Assert.Throws<InvalidOperationException>(() => TestRpcs.Login("admin", "pass"));
    }
}

public class LocalVarRpcTests : IDisposable
{
    private readonly InMemoryServerTransport _serverTransport;
    private readonly InMemoryClientTransport _clientTransport;

    public LocalVarRpcTests()
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

    [Fact]
    public void EntityCommand_WithLocalsAndTryCatch_WorksAfterWeaving()
    {
        Server.Start(0);
        Client.Connect("localhost", 0);
        Tick();
        var serverClient = Server.Clients.First();

        var map = Server.CreateMap<TestMap>();
        var serverEntity = Server.Spawn<RpcEntity>(map, serverClient);
        serverClient.AssignPlayerEntity(serverEntity);
        Tick();

        var clientEntity = Client.FindEntity<RpcEntity>(serverEntity.NetworkId)!;
        var promise = clientEntity.ComputeWithLocals(3, 7);
        Tick();

        Assert.True(promise.IsCompleted);
        Assert.True(promise.IsSuccess);
        Assert.Equal(31, promise.Result); // sum=10, product=21, result=31
        Assert.Contains("ComputeWithLocals:10:21", serverEntity.Log);
    }
}

// ═══════════════════════════════════════════════════════════════════
//  Test types with RPC methods
// ═══════════════════════════════════════════════════════════════════

public class RpcEntity : NetworkEntity
{
    public List<string> Log { get; } = new();

    // ── EntityCommand: void ──

    [EntityCommand]
    public void FireVoid()
    {
        Log.Add("FireVoid");
    }

    [EntityCommand]
    public void FireWithArgs(int a, string b)
    {
        Log.Add($"FireWithArgs:{a}:{b}");
    }

    // ── EntityCommand: return values ──

    [EntityCommand]
    public RpcPromise<int> DoubleIt(int value)
    {
        Log.Add($"DoubleIt:{value}");
        return value * 2;
    }

    [EntityCommand]
    public RpcPromise<string> Echo(string input)
    {
        return $"echo:{input}";
    }

    [EntityCommand]
    public RpcPromise<bool> IsAlive()
    {
        return true;
    }

    [EntityCommand]
    public RpcPromise Acknowledge()
    {
        Log.Add("Acknowledge");
        return RpcPromise.Completed;
    }

    // ── EntityCommand: error ──

    [EntityCommand]
    public RpcPromise<int> ThrowsError()
    {
        throw new InvalidOperationException("Server error");
    }

    // ── EntityCommand: Sender ──

    [EntityCommand(RequireOwner = false)]
    public RpcPromise<uint> WhoAmI()
    {
        return Sender!.ClientId;
    }

    // ── EntityCommand: RequireOwner ──

    [EntityCommand]
    public void OwnerOnly()
    {
        Log.Add("OwnerOnly");
    }

    [EntityCommand(RequireOwner = false)]
    public void AnyoneCanCall()
    {
        Log.Add("AnyoneCanCall");
    }

    // ── EntityCommand: parameter types ──

    [EntityCommand]
    public void TakeInt(int v) => Log.Add($"TakeInt:{v}");

    [EntityCommand]
    public void TakeFloat(float v) => Log.Add($"TakeFloat:{v}");

    [EntityCommand]
    public void TakeString(string? v) => Log.Add($"TakeString:{v ?? "null"}");

    [EntityCommand]
    public void TakeBool(bool v) => Log.Add($"TakeBool:{v}");

    [EntityCommand]
    public void TakeVector2(Vector2 v) => Log.Add($"TakeVector2:{v.X}:{v.Y}");

    [EntityCommand]
    public void TakeEnum(RpcTestEnum v) => Log.Add($"TakeEnum:{v}");

    [EntityCommand]
    public void TakeMultiple(int a, string b, bool c, Vector2 d)
        => Log.Add($"TakeMultiple:{a}:{b}:{c}:{d.X}:{d.Y}");

    [EntityCommand]
    public void TakeSerializable(DamageInfo info) => Log.Add($"TakeSerializable:{info.Amount}:{info.Type}:{info.IsCritical}");

    [EntityCommand]
    public void TakeByteArray(byte[] data) => Log.Add($"TakeByteArray:{data.Length}:{string.Join(",", data)}");

    [EntityCommand]
    public void TakeGuid(Guid id) => Log.Add($"TakeGuid:{id}");

    [EntityCommand]
    public void TakeSByte(sbyte v) => Log.Add($"TakeSByte:{v}");

    [EntityCommand]
    public void TakeShort(short v) => Log.Add($"TakeShort:{v}");

    [EntityCommand]
    public void TakeLong(long v) => Log.Add($"TakeLong:{v}");

    [EntityCommand]
    public void TakeULong(ulong v) => Log.Add($"TakeULong:{v}");

    [EntityCommand]
    public void TakeDouble(double v) => Log.Add($"TakeDouble:{v}");

    [EntityCommand]
    public void TakeIntArray(int[] v) => Log.Add($"TakeIntArray:{string.Join(",", v)}");

    [EntityCommand]
    public void TakeStringArray(string[] v) => Log.Add($"TakeStringArray:{string.Join(",", v)}");

    [EntityCommand]
    public void TakeSerializableArray(DamageInfo[] v) => Log.Add($"TakeSerializableArray:{v.Length}:{string.Join(";", v.Select(d => $"{d.Amount}/{d.Type}"))}");

    [EntityCommand]
    public void TakeEntityRef(NetworkEntity? entity) => Log.Add($"TakeEntityRef:{(entity == null ? "null" : entity.NetworkId.ToString())}");

    [EntityCommand]
    public void TakeEntityRefTyped(RpcEntity? entity) => Log.Add($"TakeEntityRefTyped:{(entity == null ? "null" : entity.NetworkId.ToString())}");

    [EntityCommand]
    public RpcPromise<uint> GetNetworkId()
    {
        return NetworkId;
    }

    [EntityCommand]
    public RpcPromise<float> GetHealth()
    {
        return 75.5f;
    }

    // ── EntityCommand: overloaded methods ──

    [EntityCommand]
    public void Attack(int targetId)
    {
        Log.Add($"Attack:{targetId}");
    }

    [EntityCommand]
    public void Attack(int targetId, float damage)
    {
        Log.Add($"Attack:{targetId}:{damage}");
    }

    [EntityCommand]
    public RpcPromise<int> Compute(int a)
    {
        return a * 10;
    }

    [EntityCommand]
    public RpcPromise<int> Compute(int a, int b)
    {
        return a + b;
    }

    // ── EntityRpc: Observers ──

    [EntityRpc]
    public void NotifyAll(string msg)
    {
        Log.Add($"NotifyAll:{msg}");
    }

    [EntityRpc]
    public void NotifyComplex(int a, string b, Vector2 c)
    {
        Log.Add($"NotifyComplex:{a}:{b}:{c.X}:{c.Y}");
    }

    // ── EntityRpc: Owner ──

    [EntityRpc(Target = RpcTarget.Owner)]
    public void NotifyOwner(int value)
    {
        Log.Add($"NotifyOwner:{value}");
    }

    // ── EntityRpc: Player ──

    [EntityRpc(Target = RpcTarget.Player)]
    public void Whisper(RemoteClient target, string text)
    {
        Log.Add($"Whisper:{text}");
    }

    [EntityRpc(Target = RpcTarget.Player)]
    public void WhisperCheckTarget(RemoteClient target, string text)
    {
        Log.Add($"WhisperCheckTarget:target={(target == null ? "null" : "set")}:{text}");
    }

    // ── EntityRpc: Player with RemoteClient[] ──

    [EntityRpc(Target = RpcTarget.Player)]
    public void BroadcastToPlayers(RemoteClient[] targets, string text)
    {
        Log.Add($"BroadcastToPlayers:{text}");
    }

    // ── EntityRpc: overloaded methods ──

    [EntityRpc]
    public void Notify(string msg)
    {
        Log.Add($"Notify:{msg}");
    }

    [EntityRpc]
    public void Notify(string msg, int priority)
    {
        Log.Add($"Notify:{msg}:{priority}");
    }

    // ── EntityRpc: ExcludeOwner ──

    [EntityRpc(ExcludeOwner = true)]
    public void NotifyNonOwners(string msg)
    {
        Log.Add($"NotifyNonOwners:{msg}");
    }

    // ── EntityRpc: parameter types ──

    [EntityRpc]
    public void ClientTakeInt(int v) => Log.Add($"ClientTakeInt:{v}");

    [EntityRpc]
    public void ClientTakeString(string? v) => Log.Add($"ClientTakeString:{v ?? "null"}");

    [EntityRpc]
    public void ClientTakeBool(bool v) => Log.Add($"ClientTakeBool:{v}");

    [EntityRpc]
    public void ClientTakeVector2(Vector2 v) => Log.Add($"ClientTakeVector2:{v.X}:{v.Y}");

    [EntityRpc]
    public void ClientTakeEnum(RpcTestEnum v) => Log.Add($"ClientTakeEnum:{v}");

    [EntityRpc]
    public void ClientTakeByteArray(byte[] data) => Log.Add($"ClientTakeByteArray:{data.Length}:{string.Join(",", data)}");

    [EntityRpc]
    public void ClientTakeGuid(Guid id) => Log.Add($"ClientTakeGuid:{id}");

    [EntityRpc]
    public void ClientTakeSByte(sbyte v) => Log.Add($"ClientTakeSByte:{v}");

    [EntityRpc]
    public void ClientTakeShort(short v) => Log.Add($"ClientTakeShort:{v}");

    [EntityRpc]
    public void ClientTakeLong(long v) => Log.Add($"ClientTakeLong:{v}");

    [EntityRpc]
    public void ClientTakeULong(ulong v) => Log.Add($"ClientTakeULong:{v}");

    [EntityRpc]
    public void ClientTakeDouble(double v) => Log.Add($"ClientTakeDouble:{v}");

    [EntityRpc]
    public void ClientTakeIntArray(int[] v) => Log.Add($"ClientTakeIntArray:{string.Join(",", v)}");

    [EntityRpc]
    public void ClientTakeSerializableArray(DamageInfo[] v) => Log.Add($"ClientTakeSerializableArray:{v.Length}:{string.Join(";", v.Select(d => $"{d.Amount}/{d.Type}"))}");

    [EntityRpc]
    public void ClientTakeEntityRef(NetworkEntity? entity) => Log.Add($"ClientTakeEntityRef:{(entity == null ? "null" : entity.NetworkId.ToString())}");

    [EntityRpc]
    public void ClientTakeEntityRefTyped(RpcEntity? entity) => Log.Add($"ClientTakeEntityRefTyped:{(entity == null ? "null" : entity.NetworkId.ToString())}");

    // ── EntityCommand: local variables + try/catch ──

    [EntityCommand]
    public RpcPromise<int> ComputeWithLocals(int a, int b)
    {
        int sum = a + b;
        int product;
        try
        {
            product = checked(a * b);
        }
        catch (OverflowException)
        {
            product = -1;
        }
        Log.Add($"ComputeWithLocals:{sum}:{product}");
        return sum + product;
    }
}

/// <summary>
/// Static RPC methods — replaces the old RpcClient RemoteClient subclass.
/// </summary>
public static class TestRpcs
{
    public static List<string> ServerLog { get; } = new();
    public static List<string> ClientLog { get; } = new();

    [StaticCommand]
    public static void Login(string username, string password)
    {
        ServerLog.Add($"Login:{username}:{password}");
    }

    [StaticCommand]
    public static RpcPromise<bool> LoginAndGetResult(string username, string password)
    {
        ServerLog.Add($"Login:{username}:{password}");
        return username == "admin";
    }

    [StaticCommand]
    public static RpcPromise<uint> GetMyId()
    {
        return NetworkObject.Sender!.ClientId;
    }

    [StaticRpc]
    public static void ShowMessage(RemoteClient target, string msg)
    {
        ClientLog.Add($"ShowMessage:{msg}");
    }

    [StaticRpc]
    public static void ShowLoginResult(RemoteClient[] targets, bool success, string msg)
    {
        ClientLog.Add($"ShowLoginResult:{success}:{msg}");
    }

    [StaticCommand]
    public static void SendData(byte[] data, Guid id)
    {
        ServerLog.Add($"SendData:{data.Length}:{id}");
    }

    [StaticRpc]
    public static void ReceiveData(RemoteClient[] targets, byte[] data, Guid id)
    {
        ClientLog.Add($"ReceiveData:{data.Length}:{id}");
    }
}

public class RpcMap : Map
{
    public List<string> Log { get; } = new();

    [MapRpc]
    public void ShowAnnouncement(string msg)
    {
        Log.Add($"ShowAnnouncement:{msg}");
    }

    [MapRpc(Target = RpcTarget.Player)]
    public void ShowBossIntro(RemoteClient player, string bossName)
    {
        Log.Add($"ShowBossIntro:{bossName}");
    }
}

public abstract class BaseUnit : NetworkEntity
{
    public List<string> Log { get; } = new();

    [EntityCommand(RequireOwner = false)]
    public void Attack(int targetId) => Log.Add($"Attack:{targetId}");

    [EntityRpc]
    public void TakeDamage(int amount) => Log.Add($"TakeDamage:{amount}");
}

public class Warrior : BaseUnit
{
    [EntityCommand(RequireOwner = false)]
    public void Block() => Log.Add("Block");

    [EntityRpc]
    public void ShowBlock() => Log.Add("ShowBlock");
}

public enum RpcTestEnum { None, Attack, Defend, Heal }

public struct DamageInfo : INetworkSerializable
{
    public int Amount;
    public string Type;
    public bool IsCritical;

    public void Serialize(NetworkWriter writer)
    {
        writer.WriteInt(Amount);
        writer.WriteString(Type);
        writer.WriteBool(IsCritical);
    }

    public void Deserialize(NetworkReader reader)
    {
        Amount = reader.ReadInt();
        Type = reader.ReadString()!;
        IsCritical = reader.ReadBool();
    }
}
